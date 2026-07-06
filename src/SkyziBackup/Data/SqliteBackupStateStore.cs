using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NLog;
using Skyzi000.Data;

namespace SkyziBackup.Data;

/// <summary>
/// JSONで保持していた BackupDatabase の内容(二つの辞書)をSQLiteにほぼ1:1で保持する軽量ストア。
/// ユーザには透過。既存JSONがあれば初回アクセス時に自動移行する。
/// </summary>
public sealed class SqliteBackupStateStore : IDisposable
{
    public const string FileName = "database.sqlite";
    private const string CorruptFileSuffix = ".corrupt";
    private const string LegacyJsonMigratedSuffix = ".migrated";
    private const string MigratingTempSuffix = ".migrating";
    private const string WalFileSuffix = "-wal";
    private const string ShmFileSuffix = "-shm";
    private const int SchemaVersion = 1;
    private const string FullScanPendingMetaKey = "FullScanPending";
    private const string BackupCompletedMetaKey = "BackupCompleted";
    private const int WriteBatchSize = 8192;
    private const int CommitIntervalMilliseconds = 30_000;
    private const int SqliteCorruptErrorCode = 11; // SQLITE_CORRUPT
    private const int SqliteNotADbErrorCode = 26; // SQLITE_NOTADB
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ExpectedColumn[] MetaColumns =
    {
        new("Key", "TEXT", true, 1),
        new("Value", "TEXT", false, 0),
    };

    private static readonly ExpectedColumn[] DirectoryColumns =
    {
        new("Path", "TEXT", true, 1),
        new("CreationTimeUtc", "INTEGER", false, 0),
        new("LastWriteTimeUtc", "INTEGER", false, 0),
        new("FileAttributes", "INTEGER", false, 0),
    };

    private static readonly ExpectedColumn[] FileColumns =
    {
        new("Path", "TEXT", true, 1),
        new("CreationTimeUtc", "INTEGER", false, 0),
        new("LastWriteTimeUtc", "INTEGER", false, 0),
        new("OriginSize", "INTEGER", true, 0),
        new("FileAttributes", "INTEGER", false, 0),
        new("Sha1", "TEXT", false, 0),
    };

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly bool _isNewDatabase;
    private readonly object _syncRoot = new();
    private SqliteTransaction? _writeTransaction;
    private int _bulkWriteDepth;
    private int _writeOperationsSinceCommit;
    private long _lastCommitTimestamp;
    private SqliteCommand? _getDirectoryCommand;
    private SqliteCommand? _getFileCommand;
    private SqliteCommand? _directoryExistsCommand;
    private SqliteCommand? _fileExistsCommand;
    private SqliteCommand? _upsertDirectoryCommand;
    private SqliteCommand? _upsertFileCommand;
    private SqliteCommand? _removeDirectoryCommand;
    private SqliteCommand? _removeFileCommand;
    private SqliteCommand? _clearDirectoriesCommand;
    private SqliteCommand? _clearFilesCommand;
    private bool _disposed;

    public string OriginBaseDirPath { get; }
    public string DestBaseDirPath { get; }

    private SqliteBackupStateStore(string dbPath, string originBaseDirPath, string destBaseDirPath, bool isNewDatabase)
    {
        _dbPath = dbPath;
        OriginBaseDirPath = originBaseDirPath;
        DestBaseDirPath = destBaseDirPath;
        _isNewDatabase = isNewDatabase;
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        _connection = new SqliteConnection(connectionString);
        try
        {
            _connection.Open();
            if (!_isNewDatabase)
                ValidateExistingDatabaseBeforeSchemaChanges();
            InitPragmas();
            InitSchema();
            EnsureMeta();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    public static SqliteBackupStateStore OpenOrMigrate(string originBaseDirPath, string destBaseDirPath)
    {
        // JSONの絶対パス
        var jsonPath = BackupDatabase.GetDatabasePath(originBaseDirPath, destBaseDirPath);
        var sqlitePath = GetDatabasePath(originBaseDirPath, destBaseDirPath);
        var dir = Path.GetDirectoryName(sqlitePath)!;
        Directory.CreateDirectory(dir);
        if (HasDatabaseFile(sqlitePath))
        {
            try
            {
                var store = new SqliteBackupStateStore(sqlitePath, originBaseDirPath, destBaseDirPath, false);
                TryDeleteLegacyJson(jsonPath); // 移行済みなので、化石として残っている旧JSONがあれば掃除する
                return store;
            }
            catch (Exception e) when (IsPermanentlyUnusable(e))
            {
                // SQLiteストアはキャッシュ扱いなので、恒久的に利用できない(破損・スキーマ不一致・ペア不一致)場合のみ退避して作り直す。
                // 一時的なエラー(ロック・アクセス権など)はそのまま伝播させ、呼び出し元のインメモリフォールバックに任せる。
                Logger.Warn(e, "既存のSQLiteストアを開けないため作り直します: '{0}'", sqlitePath);
                QuarantineBrokenDatabase(sqlitePath);
                // 旧JSONは最初のSQLite移行時点の状態のまま更新されない化石なので、ここでは取り込まない。
                // 古い行を移行すると、スキップ判定・属性解除・削除同期などがそれを信頼して実態とずれるため、
                // 空のDBから始めて実ファイル基準(NeedsFullScanのフォールバック)で再構築させる。
                var recreated = CreateNew(null, sqlitePath, originBaseDirPath, destBaseDirPath);
                TryDeleteLegacyJson(jsonPath);
                return recreated;
            }
        }

        var created = CreateNew(jsonPath, sqlitePath, originBaseDirPath, destBaseDirPath);
        TryDeleteLegacyJson(jsonPath); // 新しいストアに移行し終えた時点で旧JSONは不要になる
        return created;
    }

    /// <summary>
    /// 移行完了後は不要になった旧JSONデータベース(と関連ファイル)を削除する。
    /// 残しておくと、SQLite側だけが消えた場合に初回移行として古い状態が取り込まれ、実態とずれた内容が信頼されてしまう。
    /// 削除失敗は警告に留めて次回成功時の掃除に任せる(本体が残っている間は<see cref="LegacyJsonMigratedSuffix" />が再移行を防ぐ)。
    /// </summary>
    private static void TryDeleteLegacyJson(string jsonPath)
    {
        try
        {
            // 削除に失敗しても化石として再移行されないよう、先に移行済みマークを補填してから消す
            // (初回移行のクラッシュ等で未マークのまま残った旧JSONにもここで印が付く)
            if (File.Exists(jsonPath) && !File.Exists(jsonPath + LegacyJsonMigratedSuffix))
                TryMarkLegacyJsonMigrated(jsonPath);
            DeleteEvenIfReadonly(jsonPath);
            DeleteEvenIfReadonly(jsonPath + DataFileWriter.BackupFileExtension);
            DeleteEvenIfReadonly(jsonPath + DataFileWriter.TempFileExtension);
            // 移行済みマークは本体側の削除がすべて成功した後にだけ消す(本体が残っているのに先に消すと再移行されてしまう)
            DeleteEvenIfReadonly(jsonPath + LegacyJsonMigratedSuffix);
        }
        catch (Exception e)
        {
            Logger.Warn(e, "旧JSONデータベースの削除に失敗: '{0}'", jsonPath);
        }
    }

    private static void DeleteEvenIfReadonly(string path)
    {
        if (!File.Exists(path))
            return;
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    /// <summary>
    /// 旧JSONを取り込んだ直後に「移行済み」マークを残す。旧JSONの削除に失敗したまま
    /// SQLite側だけが消えた場合でも、次の新規作成でstaleなJSONを再移行して信頼しないための印。
    /// 移行の確定(DB配置)前に必ず成功していること: マーク無しで配置を確定すると、旧JSONの削除失敗と
    /// 組み合わさって未マークのstale JSONが残り、後で再移行されてしまう。
    /// </summary>
    private static void MarkLegacyJsonMigrated(string jsonPath) =>
        File.WriteAllBytes(jsonPath + LegacyJsonMigratedSuffix, Array.Empty<byte>());

    private static void TryMarkLegacyJsonMigrated(string jsonPath)
    {
        try
        {
            MarkLegacyJsonMigrated(jsonPath);
        }
        catch (Exception e)
        {
            Logger.Warn(e, "旧JSONデータベースの移行済みマークの作成に失敗: '{0}'", jsonPath);
        }
    }

    /// <summary>
    /// DBがバックアップ先の実態を網羅している保証がない状態かどうか
    /// (空で作成されてから、実走査の削除同期を含むバックアップがまだ完走していない)。
    /// trueの間、削除同期のようにDBの記録を「存在の情報源」として使う処理はDBを信頼せず実ファイルを参照すること。
    /// Metaテーブルに永続化されるためプロセスを跨いで有効で、バックアップ成功時に
    /// <see cref="MarkFullScanCompleted" />で解除される。
    /// </summary>
    public bool NeedsFullScan { get; private set; }

    /// <summary>
    /// このDBが作成(再作成)されてから、バックアップが少なくとも一度完走した記録を持つかどうか。
    /// falseの間はスキップ判定の書き戻しが行き渡っておらず記録に欠落があり得るため、
    /// 属性リストアのようにDBの行を「内容の情報源」として列挙する処理はDBを使わないこと。
    /// <see cref="NeedsFullScan" />と違い削除同期の設定に依存せず、どのバックアップでも完走すれば立つ。
    /// </summary>
    public bool HasCompletedBackup { get; private set; }

    /// <summary>
    /// このDBを削除同期の情報源として信頼してよい状態に戻ったと記録する(実走査の削除同期を含むバックアップの完走時に呼ぶ)。
    /// </summary>
    internal void MarkFullScanCompleted() => SetFullScanPending(false);

    /// <summary>
    /// このDBが作成されてからバックアップが一度完走したと記録する(削除同期の設定に関わらず、バックアップ成功時に呼ぶ)。
    /// </summary>
    internal void MarkBackupCompleted()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            WriteMetaValue(BackupCompletedMetaKey, "1");
            HasCompletedBackup = true;
        }
    }

    private void SetFullScanPending(bool pending)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            WriteMetaValue(FullScanPendingMetaKey, pending ? "1" : "0");
            NeedsFullScan = pending;
        }
    }

    private void WriteMetaValue(string key, string value)
    {
        BeginWriteIfNeeded();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = _writeTransaction;
        cmd.CommandText = "INSERT INTO Meta(Key,Value) VALUES(@k,@v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
        RecordWrite();
    }

    /// <summary>
    /// 既存のSQLiteストアを破棄して作り直してよい、「このアプリにとって恒久的に利用できない」例外かどうかを判定する。
    /// 対象はファイル破損(SQLITE_CORRUPT/SQLITE_NOTADB)と、自前バリデーションが投げる
    /// <see cref="SqliteStoreValidationException" />(テーブル欠落・スキーマ不一致・ペア不一致・SchemaVersion不正/欠落)のみ。
    /// IOエラーやロック等の一時的なエラー、およびより新しいアプリが作った正当なDB(<see cref="NotSupportedException" />)は対象外。
    /// </summary>
    private static bool IsPermanentlyUnusable(Exception e) => e switch
    {
        SqliteException sqliteException => sqliteException.SqliteErrorCode is SqliteCorruptErrorCode or SqliteNotADbErrorCode,
        SqliteStoreValidationException => true,
        _ => false,
    };

    /// <summary>
    /// 旧JSONの読み込み失敗が「内容の破損(デシリアライズ失敗)」によるものかどうか。
    /// ロックやアクセス権などの一時的なIOエラーは対象外(再試行すれば読める可能性があり、破棄してはいけない)。
    /// <see cref="DataFileWriter.Read{T}" />は同期ラッパーのため<see cref="AggregateException" />に包まれて届くことがある。
    /// </summary>
    private static bool IsDeserializationFailure(Exception e) => e switch
    {
        JsonException => true,
        AggregateException { InnerExceptions.Count: 1 } aggregate => IsDeserializationFailure(aggregate.InnerExceptions[0]),
        _ => false,
    };

    /// <summary>
    /// 開けなくなった既存のSQLiteストア本体を削除せず<see cref="CorruptFileSuffix" />付きのパスへ退避する(1世代のみ保持)。
    /// -wal/-shmは削除する。退避に失敗した場合は従来通り削除にフォールバックし、自己修復を止めない。
    /// </summary>
    private static void QuarantineBrokenDatabase(string sqlitePath)
    {
        try
        {
            File.Move(sqlitePath, sqlitePath + CorruptFileSuffix, true);
            File.Delete(sqlitePath + WalFileSuffix);
            File.Delete(sqlitePath + ShmFileSuffix);
        }
        catch (Exception e)
        {
            Logger.Warn(e, "破損したSQLiteストアの退避に失敗したため削除します: '{0}'", sqlitePath);
            DeleteSqliteRelatedFiles(sqlitePath);
        }
    }

    /// <param name="jsonPath">移行元の旧JSONのパス。nullなら移行せず空のDBを作る(隔離再作成時)</param>
    private static SqliteBackupStateStore CreateNew(string? jsonPath, string sqlitePath, string originBaseDirPath, string destBaseDirPath)
    {
        DeleteSqliteRelatedFiles(sqlitePath);
        // 移行済みマークで移行をスキップする場合でも、過去の移行中断で残った一時DBは掃除する
        DeleteSqliteRelatedFiles(sqlitePath + MigratingTempSuffix);
        try
        {
            // ファイルが存在しても読めない・ペア不一致で取り込めない場合があるため、実際に取り込むかは
            // MigrateJsonToNewSqlite側の判定に任せる。移行済みマークが残っているJSONは、移行後に削除だけ
            // 失敗した化石なので取り込まない。
            // FullScanPendingマーカーは作成時にEnsureMetaが刻み、取り込み成功時だけReplaceStateが解除するため、
            // ここでの分岐は不要(空のDBは常にマーカー付きで生まれる)
            if (jsonPath != null && File.Exists(jsonPath) && !File.Exists(jsonPath + LegacyJsonMigratedSuffix))
                MigrateJsonToNewSqlite(sqlitePath, originBaseDirPath, destBaseDirPath);

            return new SqliteBackupStateStore(sqlitePath, originBaseDirPath, destBaseDirPath, true);
        }
        catch
        {
            try
            {
                DeleteSqliteRelatedFiles(sqlitePath);
            }
            catch { }

            throw;
        }
    }

    public static string GetDatabasePath(string originBaseDirPath, string destBaseDirPath)
    {
        var jsonPath = BackupDatabase.GetDatabasePath(originBaseDirPath, destBaseDirPath);
        return Path.Combine(Path.GetDirectoryName(jsonPath) ?? throw new InvalidOperationException($"Path.GetDirectoryName(jsonPath) is null. (path: {jsonPath})"),
            FileName);
    }

    public static bool Exists(string originBaseDirPath, string destBaseDirPath) => HasDatabaseFile(GetDatabasePath(originBaseDirPath, destBaseDirPath));

    private static bool HasDatabaseFile(string sqlitePath) => File.Exists(sqlitePath) && new FileInfo(sqlitePath).Length > 0;

    public static IEnumerable<string> GetDatabaseFilePaths(string originBaseDirPath, string destBaseDirPath)
    {
        var sqlitePath = GetDatabasePath(originBaseDirPath, destBaseDirPath);
        foreach (var path in GetSqliteRelatedFilePaths(sqlitePath))
            yield return path;
        foreach (var path in GetSqliteRelatedFilePaths(sqlitePath + ".migrating"))
            yield return path;
        yield return sqlitePath + CorruptFileSuffix;
    }

    public static void DeleteDatabase(string originBaseDirPath, string destBaseDirPath)
    {
        foreach (var path in GetDatabaseFilePaths(originBaseDirPath, destBaseDirPath))
            File.Delete(path);
    }

    private static IEnumerable<string> GetSqliteRelatedFilePaths(string sqlitePath)
    {
        yield return sqlitePath;
        yield return sqlitePath + WalFileSuffix;
        yield return sqlitePath + ShmFileSuffix;
    }

    private static void DeleteSqliteRelatedFiles(string sqlitePath)
    {
        foreach (var path in GetSqliteRelatedFilePaths(sqlitePath))
            File.Delete(path);
    }

    /// <returns>旧JSONから実際に状態を取り込めたら true</returns>
    private static bool MigrateJsonToNewSqlite(string sqlitePath, string originBaseDirPath, string destBaseDirPath)
    {
        var tempSqlitePath = sqlitePath + MigratingTempSuffix;
        DeleteSqliteRelatedFiles(tempSqlitePath);

        SqliteBackupStateStore? store = null;
        try
        {
            store = new SqliteBackupStateStore(tempSqlitePath, originBaseDirPath, destBaseDirPath, true);
            var imported = store.MigrateFromJson();
            store.Checkpoint();
            store.Dispose();
            store = null;
            // 取り込めた場合、DBを配置する前に移行済みマークを残す。配置後にマークすると、その間のクラッシュで
            // 「未マークの旧JSON+SQLite」が残り、後でSQLiteだけが消えたときに古いJSONが再移行されてしまう
            // (先にマークして配置前に落ちた場合は、次回は取り込まずに空DB+実走査の再構築になるだけで安全)。
            // マークの作成に失敗した場合は例外で移行ごと中止する(警告で続行するとマーク無しの配置が確定し、
            // 旧JSONの削除にも失敗した場合に未マークのstale JSONが残って再移行されてしまう)
            if (imported)
                MarkLegacyJsonMigrated(BackupDatabase.GetDatabasePath(originBaseDirPath, destBaseDirPath));
            File.Move(tempSqlitePath, sqlitePath, false);
            try
            {
                DeleteSqliteRelatedFiles(tempSqlitePath);
            }
            catch { }

            return imported;
        }
        catch
        {
            try
            {
                store?.Dispose();
            }
            catch { }

            try
            {
                DeleteSqliteRelatedFiles(tempSqlitePath);
            }
            catch { }

            throw;
        }
    }

    internal BackupDatabase ToBackupDatabase() => new(OriginBaseDirPath, DestBaseDirPath)
    {
        BackedUpDirectoriesDict = new SqliteDirectoryDictionary(this),
        BackedUpFilesDict = new SqliteFileDictionary(this),
    };

    private void InitPragmas()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA busy_timeout=5000;
PRAGMA temp_store=MEMORY;
PRAGMA cache_size=-65536;
PRAGMA mmap_size=268435456;
PRAGMA wal_autocheckpoint=4096;
PRAGMA journal_size_limit=67108864;";
        cmd.ExecuteNonQuery();
    }

    private void InitSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Meta (
  Key TEXT PRIMARY KEY,
  Value TEXT
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS Directories (
  Path TEXT PRIMARY KEY,
  CreationTimeUtc INTEGER NULL,
  LastWriteTimeUtc INTEGER NULL,
  FileAttributes INTEGER NULL
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS Files (
  Path TEXT PRIMARY KEY,
  CreationTimeUtc INTEGER NULL,
  LastWriteTimeUtc INTEGER NULL,
  OriginSize INTEGER NOT NULL,
  FileAttributes INTEGER NULL,
  Sha1 TEXT NULL
) WITHOUT ROWID;";
        cmd.ExecuteNonQuery();
    }

    private void EnsureMeta()
    {
        using var tx = _connection.BeginTransaction();
        var meta = ReadMeta(tx);
        ValidateIdentityMeta(meta, "OriginBaseDirPath", OriginBaseDirPath, !_isNewDatabase);
        ValidateIdentityMeta(meta, "DestBaseDirPath", DestBaseDirPath, !_isNewDatabase);
        ValidateSchemaVersion(meta, !_isNewDatabase);
        UpsertMetaInternal("OriginBaseDirPath", OriginBaseDirPath, tx);
        UpsertMetaInternal("DestBaseDirPath", DestBaseDirPath, tx);
        UpsertMetaInternal("SchemaVersion", SchemaVersion.ToString(), tx);
        var fullScanPending = meta.TryGetValue(FullScanPendingMetaKey, out var fullScanPendingValue) && fullScanPendingValue == "1";
        if (_isNewDatabase && !meta.ContainsKey(FullScanPendingMetaKey))
        {
            // 新しく作られる空のDBには、作成の印(ペア情報)と同じトランザクションでFullScanPendingを刻む。
            // 作成後の別書き込みでマーカーを立てると、その間の電源断で「マーカーの無い空DB」が信頼できるDBとして
            // 残り得るため、DBファイルが存在する時点でマーカーも必ず存在するようにする
            // (旧JSONからの移行では、取り込み成功時にReplaceStateが同一トランザクションで解除する)
            UpsertMetaInternal(FullScanPendingMetaKey, "1", tx);
            fullScanPending = true;
        }

        // キーの無い既存DBはこの記録より前のビルドが作ったものなので、従来どおり完走済み扱いにする
        var backupCompleted = meta.TryGetValue(BackupCompletedMetaKey, out var backupCompletedValue)
            ? backupCompletedValue == "1"
            : !_isNewDatabase;
        if (_isNewDatabase && !meta.ContainsKey(BackupCompletedMetaKey))
        {
            // 空で作られたDBは「完走したバックアップの記録を持たない」状態から始める(移行の取り込みはReplaceStateで完走済みにする)
            UpsertMetaInternal(BackupCompletedMetaKey, "0", tx);
            backupCompleted = false;
        }

        tx.Commit();
        NeedsFullScan = fullScanPending;
        HasCompletedBackup = backupCompleted;
    }

    private void ValidateExistingDatabaseBeforeSchemaChanges()
    {
        // 将来バージョンのDBはSchemaVersionと共にテーブル形状も変わっている可能性が高く、先に形状を検証すると
        // SqliteStoreValidationException(自己修復＝作り直しの対象)になって正当な新DBを退避してしまう。
        // そのためSchemaVersionの新旧判定を必ず形状検証より先に行う。
        ThrowIfNewerSchemaVersion();
        ValidateExistingTable("Meta", MetaColumns);
        ValidateExistingTable("Directories", DirectoryColumns);
        ValidateExistingTable("Files", FileColumns);

        var meta = ReadMeta(null);
        ValidateIdentityMeta(meta, "OriginBaseDirPath", OriginBaseDirPath, true);
        ValidateIdentityMeta(meta, "DestBaseDirPath", DestBaseDirPath, true);
        ValidateSchemaVersion(meta, true);
    }

    /// <summary>
    /// Metaに記録されたSchemaVersionがこのアプリより新しい場合に<see cref="NotSupportedException" />を投げる。
    /// 将来のスキーマ変更に対しても機能するよう、Metaテーブルの形状(Key/Value列)とSchemaVersionキーは
    /// 今後のバージョンでも変更しないこと。Metaが読めない場合は判定せず、後続の通常検証に委ねる。
    /// </summary>
    private void ThrowIfNewerSchemaVersion()
    {
        string? schemaVersionText;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Meta WHERE Key='SchemaVersion' LIMIT 1";
            schemaVersionText = cmd.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            // Metaテーブルが無い・読めない場合(移行途中の残骸や無関係なsqliteファイル)は後続の検証に委ねる
            return;
        }

        if (int.TryParse(schemaVersionText, out var currentSchemaVersion) && currentSchemaVersion > SchemaVersion)
        {
            // 新しいバージョンのアプリが使う正当なDBなので、自己修復(作り直し)の対象にしないようNotSupportedExceptionを投げる
            throw new NotSupportedException(
                $"SQLiteストアのSchemaVersionがこのアプリケーションより新しいため利用できません。 current: {SchemaVersion}, actual: {currentSchemaVersion}");
        }
    }

    private void ValidateExistingTable(string tableName, IReadOnlyList<ExpectedColumn> expectedColumns)
    {
        var sql = ReadTableSql(tableName);
        if (sql == null)
            throw new SqliteStoreValidationException($"SQLiteストアの{tableName}テーブルが見つかりません。");
        if (!sql.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase))
            throw new SqliteStoreValidationException($"SQLiteストアの{tableName}テーブルが現在のスキーマではありません。");

        var actualColumns = ReadTableColumns(tableName);
        if (actualColumns.Count != expectedColumns.Count)
            throw new SqliteStoreValidationException($"SQLiteストアの{tableName}テーブルが現在のスキーマではありません。");
        for (var i = 0; i < expectedColumns.Count; i++)
        {
            if (!actualColumns[i].Equals(expectedColumns[i]))
                throw new SqliteStoreValidationException($"SQLiteストアの{tableName}テーブルが現在のスキーマではありません。");
        }
    }

    private string? ReadTableSql(string tableName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() as string;
    }

    private List<ExpectedColumn> ReadTableColumns(string tableName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var reader = cmd.ExecuteReader();
        var columns = new List<ExpectedColumn>();
        while (reader.Read())
        {
            columns.Add(new ExpectedColumn(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) != 0,
                reader.GetInt32(5)));
        }

        return columns;
    }

    private static string QuoteIdentifier(string identifier)
    {
        var builder = new StringBuilder(identifier.Length + 2);
        builder.Append('"');
        foreach (var ch in identifier)
        {
            if (ch == '"')
                builder.Append('"');
            builder.Append(ch);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private Dictionary<string, string?> ReadMeta(SqliteTransaction? tx)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Key, Value FROM Meta";
        using var reader = cmd.ExecuteReader();
        var meta = new Dictionary<string, string?>();
        while (reader.Read())
            meta[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return meta;
    }

    private void ValidateIdentityMeta(IReadOnlyDictionary<string, string?> meta, string key, string expectedValue, bool requireExisting)
    {
        if (!meta.TryGetValue(key, out var actualValue))
        {
            if (requireExisting)
                throw new SqliteStoreValidationException($"SQLiteストアの{key}が記録されていません。");
        }
        else if (actualValue != expectedValue)
        {
            throw new SqliteStoreValidationException(
                $"SQLiteストアの{key}がバックアップペアと一致しません。 expected: '{expectedValue}', actual: '{actualValue}'");
        }
    }

    private void ValidateSchemaVersion(IReadOnlyDictionary<string, string?> meta, bool requireExisting)
    {
        if (!meta.TryGetValue("SchemaVersion", out var schemaVersionText))
        {
            if (requireExisting)
                throw new SqliteStoreValidationException("SQLiteストアのSchemaVersionが記録されていません。");
        }
        else
        {
            if (!int.TryParse(schemaVersionText, out var currentSchemaVersion))
                throw new SqliteStoreValidationException($"SQLiteストアのSchemaVersionが不正です。 actual: '{schemaVersionText}'");
            if (currentSchemaVersion < 1)
                throw new SqliteStoreValidationException($"SQLiteストアのSchemaVersionが不正です。 actual: {currentSchemaVersion}");
            // 新しいバージョンのアプリが使う正当なDBなので、自己修復(作り直し)の対象にしないようNotSupportedExceptionを投げる
            if (currentSchemaVersion > SchemaVersion)
                throw new NotSupportedException(
                    $"SQLiteストアのSchemaVersionがこのアプリケーションより新しいため利用できません。 current: {SchemaVersion}, actual: {currentSchemaVersion}");
        }
    }

    private void UpsertMetaInternal(string key, string value, SqliteTransaction tx)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Meta(Key,Value) VALUES(@k,@v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    private static object ToUtcTicksValue(DateTime? value) => value.HasValue ? value.Value.ToUniversalTime().Ticks : DBNull.Value;

    private static object ToFileAttributesValue(FileAttributes? value) => value.HasValue ? (int)value.Value : DBNull.Value;

    private static DateTime? ReadLocalTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new DateTime(reader.GetInt64(ordinal), DateTimeKind.Utc).ToLocalTime();

    private void Checkpoint()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        cmd.ExecuteNonQuery();
    }

    /// <returns>旧JSONから実際に状態を取り込めたら true</returns>
    private bool MigrateFromJson()
    {
        BackupDatabase? legacy;
        try
        {
            legacy = ReadLegacyJson();
        }
        catch (Exception e) when (IsDeserializationFailure(e))
        {
            // 内容が破損していて.bacでも救えない場合。読めない旧JSONを理由にストア作成ごと失敗させると、
            // 壊れたJSONが残る限りSQLite運用へ永久に進めなくなるため、空DB扱いにして実走査の再構築へ進める。
            // ロックやアクセス権などの一時的なIOエラーはここに届かず伝播する
            // (空DB扱いにすると直後の掃除でまだ有効な旧JSONを失うため、今回はDBなしで実行し、次回の移行に持ち越す)
            Logger.Warn(e, "旧JSONデータベースを読み込めないため取り込まずに再構築します");
            return false;
        }

        if (legacy == null)
            return false; // 読めなかった場合は空DB扱い
        if (legacy.OriginBaseDirPath != OriginBaseDirPath || legacy.DestBaseDirPath != DestBaseDirPath)
            return false; // 現在のバックアップペアと一致しない旧JSONは空DB扱い
        // 取り込みに成功したDBは移行元JSON(=完走済みバックアップの記録)と同等に信頼できるため、
        // 行の投入と同一トランザクションで作成時のFullScanPendingを解除し、BackupCompletedを付与する
        ReplaceState(legacy.BackedUpDirectoriesDict, legacy.BackedUpFilesDict, asTrustedSnapshot: true);
        return true;
    }

    /// <summary>
    /// 移行用に旧JSONを読む。本体の内容が破損している場合のみバックアップ(.bac)へフォールバックする。
    /// ロック等の一時的なIOエラーでは.bacに乗り換えない: 1世代古い.bacを取り込んで確定すると、
    /// ロック解除後も有効な本体が二度と移行されず、マーカーも立たないまま実態とずれた記録を信頼してしまうため。
    /// </summary>
    private BackupDatabase? ReadLegacyJson()
    {
        var fileName = BackupDatabase.GetDatabaseFileName(OriginBaseDirPath, DestBaseDirPath);
        try
        {
            return DataFileWriter.ReadWithoutBackupFallback<BackupDatabase>(fileName);
        }
        catch (Exception e) when (IsDeserializationFailure(e))
        {
            var bacFileName = fileName + DataFileWriter.BackupFileExtension;
            if (!File.Exists(DataFileWriter.GetPath(bacFileName)))
                throw;
            Logger.Warn(e, "旧JSONデータベース本体が破損しているため、バックアップ(.bac)から移行を試みます");
            return DataFileWriter.ReadWithoutBackupFallback<BackupDatabase>(bacFileName);
        }
    }

    internal IDisposable BeginBulkWrite()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            _bulkWriteDepth++;
        }

        return new BulkWriteScope(this);
    }

    internal void Flush()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            CommitWriteTransaction();
        }
    }

    /// <param name="asTrustedSnapshot">
    /// 取り込む状態を「完走済みバックアップの記録」として信頼し、FullScanPendingの解除と
    /// BackupCompletedの付与を行の投入と同一トランザクションで行う(旧JSONからの移行用)
    /// </param>
    internal void ReplaceState(IEnumerable<KeyValuePair<string, BackedUpDirectoryData>> directories,
        IEnumerable<KeyValuePair<string, BackedUpFileData>> files,
        bool asTrustedSnapshot = false)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            CommitWriteTransaction();
            using var tx = _connection.BeginTransaction();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Directories";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM Files";
                cmd.ExecuteNonQuery();
            }

            using var upsertDirectoryCommand = CreateUpsertDirectoryCommand(tx);
            using var upsertFileCommand = CreateUpsertFileCommand(tx);
            foreach (var kv in directories)
            {
                BindDirectory(upsertDirectoryCommand, kv.Key, kv.Value);
                upsertDirectoryCommand.ExecuteNonQuery();
            }

            foreach (var kv in files)
            {
                BindFile(upsertFileCommand, kv.Key, kv.Value);
                upsertFileCommand.ExecuteNonQuery();
            }

            // 取り込んだ状態と同一トランザクションで確定し、「状態はあるのにマーカーが残る/消える」順序の隙間を作らない
            if (asTrustedSnapshot)
            {
                UpsertMetaInternal(FullScanPendingMetaKey, "0", tx);
                UpsertMetaInternal(BackupCompletedMetaKey, "1", tx);
            }

            tx.Commit();
            if (asTrustedSnapshot)
            {
                NeedsFullScan = false;
                HasCompletedBackup = true;
            }
        }
    }

    public BackedUpDirectoryData? GetDirectory(string path)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var cmd = GetDirectoryCommand();
            cmd.Parameters["@p"].Value = path;
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;
            return new BackedUpDirectoryData(
                ReadLocalTime(r, 0),
                ReadLocalTime(r, 1),
                r.IsDBNull(2) ? null : (FileAttributes)r.GetInt32(2));
        }
    }

    public IEnumerable<KeyValuePair<string, BackedUpDirectoryData>> EnumerateDirectories()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var directories = new List<KeyValuePair<string, BackedUpDirectoryData>>();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = _writeTransaction;
            cmd.CommandText = "SELECT Path,CreationTimeUtc,LastWriteTimeUtc,FileAttributes FROM Directories";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var path = r.GetString(0);
                directories.Add(new KeyValuePair<string, BackedUpDirectoryData>(path, new BackedUpDirectoryData(
                    ReadLocalTime(r, 1),
                    ReadLocalTime(r, 2),
                    r.IsDBNull(3) ? null : (FileAttributes)r.GetInt32(3))
                ));
            }

            return directories;
        }
    }

    public ICollection<string> EnumerateDirectoryKeys()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return EnumerateKeys("SELECT Path FROM Directories");
        }
    }

    public long GetDirectoryCount()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = _writeTransaction;
            cmd.CommandText = "SELECT COUNT(*) FROM Directories";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
    }

    public void ClearDirectories()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            BeginWriteIfNeeded();
            var cmd = GetClearDirectoriesCommand();
            cmd.ExecuteNonQuery();
            RecordWrite();
        }
    }

    public BackedUpFileData? GetFile(string path)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var cmd = GetFileCommand();
            cmd.Parameters["@p"].Value = path;
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;
            return new BackedUpFileData(
                ReadLocalTime(r, 0),
                ReadLocalTime(r, 1),
                r.GetInt64(2),
                r.IsDBNull(3) ? null : (FileAttributes)r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4));
        }
    }

    public IEnumerable<KeyValuePair<string, BackedUpFileData>> EnumerateFiles()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var files = new List<KeyValuePair<string, BackedUpFileData>>();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = _writeTransaction;
            cmd.CommandText = "SELECT Path,CreationTimeUtc,LastWriteTimeUtc,OriginSize,FileAttributes,Sha1 FROM Files";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var path = r.GetString(0);
                files.Add(new KeyValuePair<string, BackedUpFileData>(path, new BackedUpFileData(
                    ReadLocalTime(r, 1),
                    ReadLocalTime(r, 2),
                    r.GetInt64(3),
                    r.IsDBNull(4) ? null : (FileAttributes)r.GetInt32(4),
                    r.IsDBNull(5) ? null : r.GetString(5))
                ));
            }

            return files;
        }
    }

    public ICollection<string> EnumerateFileKeys()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return EnumerateKeys("SELECT Path FROM Files");
        }
    }

    public long GetFileCount()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = _writeTransaction;
            cmd.CommandText = "SELECT COUNT(*) FROM Files";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
    }

    public void ClearFiles()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            BeginWriteIfNeeded();
            var cmd = GetClearFilesCommand();
            cmd.ExecuteNonQuery();
            RecordWrite();
        }
    }

    public void UpsertDirectory(string path, BackedUpDirectoryData data)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            BeginWriteIfNeeded();
            var cmd = GetUpsertDirectoryCommand();
            BindDirectory(cmd, path, data);
            cmd.ExecuteNonQuery();
            RecordWrite();
        }
    }

    public void UpsertFile(string path, BackedUpFileData data)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            BeginWriteIfNeeded();
            var cmd = GetUpsertFileCommand();
            BindFile(cmd, path, data);
            cmd.ExecuteNonQuery();
            RecordWrite();
        }
    }

    public bool RemoveDirectory(string path)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            BeginWriteIfNeeded();
            var cmd = GetRemoveDirectoryCommand();
            cmd.Parameters["@p"].Value = path;
            var removed = cmd.ExecuteNonQuery() > 0;
            RecordWrite();
            return removed;
        }
    }

    public bool RemoveFile(string path)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            BeginWriteIfNeeded();
            var cmd = GetRemoveFileCommand();
            cmd.Parameters["@p"].Value = path;
            var removed = cmd.ExecuteNonQuery() > 0;
            RecordWrite();
            return removed;
        }
    }

    public bool ContainsDirectory(string path)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var cmd = GetDirectoryExistsCommand();
            cmd.Parameters["@p"].Value = path;
            return cmd.ExecuteScalar() != null;
        }
    }

    public bool ContainsFile(string path)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var cmd = GetFileExistsCommand();
            cmd.Parameters["@p"].Value = path;
            return cmd.ExecuteScalar() != null;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                CommitWriteTransaction();
                _bulkWriteDepth = 0;
            }
            finally
            {
                DisposeCommands();
                _connection.Dispose();
            }
        }
    }

    private ICollection<string> EnumerateKeys(string commandText)
    {
        var keys = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = _writeTransaction;
        cmd.CommandText = commandText;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            keys.Add(r.GetString(0));
        return keys;
    }

    private SqliteCommand GetDirectoryCommand()
    {
        _getDirectoryCommand ??= CreateCommandWithPathParameter(
            "SELECT CreationTimeUtc,LastWriteTimeUtc,FileAttributes FROM Directories WHERE Path=@p");
        _getDirectoryCommand.Transaction = _writeTransaction;
        return _getDirectoryCommand;
    }

    private SqliteCommand GetFileCommand()
    {
        _getFileCommand ??= CreateCommandWithPathParameter(
            "SELECT CreationTimeUtc,LastWriteTimeUtc,OriginSize,FileAttributes,Sha1 FROM Files WHERE Path=@p");
        _getFileCommand.Transaction = _writeTransaction;
        return _getFileCommand;
    }

    private SqliteCommand GetDirectoryExistsCommand()
    {
        _directoryExistsCommand ??= CreateCommandWithPathParameter("SELECT 1 FROM Directories WHERE Path=@p LIMIT 1");
        _directoryExistsCommand.Transaction = _writeTransaction;
        return _directoryExistsCommand;
    }

    private SqliteCommand GetFileExistsCommand()
    {
        _fileExistsCommand ??= CreateCommandWithPathParameter("SELECT 1 FROM Files WHERE Path=@p LIMIT 1");
        _fileExistsCommand.Transaction = _writeTransaction;
        return _fileExistsCommand;
    }

    private SqliteCommand GetUpsertDirectoryCommand()
    {
        _upsertDirectoryCommand ??= CreateUpsertDirectoryCommand(_writeTransaction);
        _upsertDirectoryCommand.Transaction = _writeTransaction;
        return _upsertDirectoryCommand;
    }

    private SqliteCommand GetUpsertFileCommand()
    {
        _upsertFileCommand ??= CreateUpsertFileCommand(_writeTransaction);
        _upsertFileCommand.Transaction = _writeTransaction;
        return _upsertFileCommand;
    }

    private SqliteCommand GetRemoveDirectoryCommand()
    {
        _removeDirectoryCommand ??= CreateCommandWithPathParameter("DELETE FROM Directories WHERE Path=@p");
        _removeDirectoryCommand.Transaction = _writeTransaction;
        return _removeDirectoryCommand;
    }

    private SqliteCommand GetRemoveFileCommand()
    {
        _removeFileCommand ??= CreateCommandWithPathParameter("DELETE FROM Files WHERE Path=@p");
        _removeFileCommand.Transaction = _writeTransaction;
        return _removeFileCommand;
    }

    private SqliteCommand GetClearDirectoriesCommand()
    {
        _clearDirectoriesCommand ??= CreatePreparedCommand("DELETE FROM Directories");
        _clearDirectoriesCommand.Transaction = _writeTransaction;
        return _clearDirectoriesCommand;
    }

    private SqliteCommand GetClearFilesCommand()
    {
        _clearFilesCommand ??= CreatePreparedCommand("DELETE FROM Files");
        _clearFilesCommand.Transaction = _writeTransaction;
        return _clearFilesCommand;
    }

    private SqliteCommand CreatePreparedCommand(string commandText)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = commandText;
        cmd.Prepare();
        return cmd;
    }

    private SqliteCommand CreateCommandWithPathParameter(string commandText)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = commandText;
        cmd.Parameters.Add("@p", SqliteType.Text);
        cmd.Prepare();
        return cmd;
    }

    private SqliteCommand CreateUpsertDirectoryCommand(SqliteTransaction? tx)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Directories(Path,CreationTimeUtc,LastWriteTimeUtc,FileAttributes) VALUES(@p,@c,@w,@a) " +
                          "ON CONFLICT(Path) DO UPDATE SET CreationTimeUtc=excluded.CreationTimeUtc,LastWriteTimeUtc=excluded.LastWriteTimeUtc,FileAttributes=excluded.FileAttributes";
        cmd.Transaction = tx;
        cmd.Parameters.Add("@p", SqliteType.Text);
        cmd.Parameters.Add("@c", SqliteType.Integer);
        cmd.Parameters.Add("@w", SqliteType.Integer);
        cmd.Parameters.Add("@a", SqliteType.Integer);
        cmd.Prepare();
        return cmd;
    }

    private SqliteCommand CreateUpsertFileCommand(SqliteTransaction? tx)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Files(Path,CreationTimeUtc,LastWriteTimeUtc,OriginSize,FileAttributes,Sha1) VALUES(@p,@c,@w,@s,@a,@h) " +
                          "ON CONFLICT(Path) DO UPDATE SET CreationTimeUtc=excluded.CreationTimeUtc,LastWriteTimeUtc=excluded.LastWriteTimeUtc,OriginSize=excluded.OriginSize,FileAttributes=excluded.FileAttributes,Sha1=excluded.Sha1";
        cmd.Transaction = tx;
        cmd.Parameters.Add("@p", SqliteType.Text);
        cmd.Parameters.Add("@c", SqliteType.Integer);
        cmd.Parameters.Add("@w", SqliteType.Integer);
        cmd.Parameters.Add("@s", SqliteType.Integer);
        cmd.Parameters.Add("@a", SqliteType.Integer);
        cmd.Parameters.Add("@h", SqliteType.Text);
        cmd.Prepare();
        return cmd;
    }

    private static void BindDirectory(SqliteCommand cmd, string path, BackedUpDirectoryData data)
    {
        cmd.Parameters["@p"].Value = path;
        cmd.Parameters["@c"].Value = ToUtcTicksValue(data.CreationTime);
        cmd.Parameters["@w"].Value = ToUtcTicksValue(data.LastWriteTime);
        cmd.Parameters["@a"].Value = ToFileAttributesValue(data.FileAttributes);
    }

    private static void BindFile(SqliteCommand cmd, string path, BackedUpFileData data)
    {
        cmd.Parameters["@p"].Value = path;
        cmd.Parameters["@c"].Value = ToUtcTicksValue(data.CreationTime);
        cmd.Parameters["@w"].Value = ToUtcTicksValue(data.LastWriteTime);
        cmd.Parameters["@s"].Value = data.OriginSize;
        cmd.Parameters["@a"].Value = ToFileAttributesValue(data.FileAttributes);
        cmd.Parameters["@h"].Value = (object?)data.Sha1 ?? DBNull.Value;
    }

    private void BeginWriteIfNeeded()
    {
        if (_bulkWriteDepth == 0 || _writeTransaction != null)
            return;

        _writeTransaction = _connection.BeginTransaction();
        _writeOperationsSinceCommit = 0;
        _lastCommitTimestamp = Environment.TickCount64;
    }

    private void RecordWrite()
    {
        if (_writeTransaction == null)
            return;

        _writeOperationsSinceCommit++;
        // 強制終了や電源断でも失われる進捗が一定時間分に収まるよう、件数か経過時間のどちらかでコミットする
        // (経過時間の判定は次の書き込み時にしか行われないため、保証されるのは書き込みが継続している限り約30秒毎のコミット)
        if (_writeOperationsSinceCommit >= WriteBatchSize ||
            Environment.TickCount64 - _lastCommitTimestamp >= CommitIntervalMilliseconds)
            CommitWriteTransaction();
    }

    private void CommitWriteTransaction()
    {
        if (_writeTransaction == null)
            return;

        var tx = _writeTransaction;
        try
        {
            tx.Commit();
        }
        catch
        {
            try
            {
                tx.Rollback();
            }
            catch { }

            throw;
        }
        finally
        {
            _writeTransaction = null;
            _writeOperationsSinceCommit = 0;
            _lastCommitTimestamp = Environment.TickCount64;
            tx.Dispose();
        }
    }

    private void DisposeCommands()
    {
        _getDirectoryCommand?.Dispose();
        _getFileCommand?.Dispose();
        _directoryExistsCommand?.Dispose();
        _fileExistsCommand?.Dispose();
        _upsertDirectoryCommand?.Dispose();
        _upsertFileCommand?.Dispose();
        _removeDirectoryCommand?.Dispose();
        _removeFileCommand?.Dispose();
        _clearDirectoriesCommand?.Dispose();
        _clearFilesCommand?.Dispose();
    }

    private void EndBulkWrite()
    {
        lock (_syncRoot)
        {
            if (_disposed || _bulkWriteDepth == 0)
                return;

            _bulkWriteDepth--;
            if (_bulkWriteDepth == 0)
                CommitWriteTransaction();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);
    }

    private sealed class BulkWriteScope : IDisposable
    {
        private SqliteBackupStateStore? _store;

        public BulkWriteScope(SqliteBackupStateStore store) => _store = store;

        public void Dispose()
        {
            var store = _store;
            if (store == null)
                return;

            _store = null;
            store.EndBulkWrite();
        }
    }

    private readonly record struct ExpectedColumn(string Name, string Type, bool IsNotNull, int PrimaryKeyOrdinal);

    /// <summary>
    /// 自前バリデーション(テーブル・スキーマ・Meta検証)の失敗を表す例外。
    /// <see cref="IsPermanentlyUnusable" />が自己修復(作り直し)の対象として意図的に識別するための型で、
    /// BCL由来の<see cref="InvalidOperationException" />を誤って作り直し対象にしないために分けている。
    /// </summary>
    private sealed class SqliteStoreValidationException : InvalidOperationException
    {
        public SqliteStoreValidationException(string message) : base(message) { }
    }
}
