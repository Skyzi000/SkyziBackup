using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Skyzi000.Data;

namespace SkyziBackup.Data;

/// <summary>
/// JSONで保持していた BackupDatabase の内容(二つの辞書)をSQLiteにほぼ1:1で保持する軽量ストア。
/// ユーザには透過。既存JSONがあれば初回アクセス時に自動移行する。
/// </summary>
public sealed class SqliteBackupStateStore : IDisposable
{
    public const string FileName = "database.sqlite";
    private const int SchemaVersion = 1;
    private const int WriteBatchSize = 8192;
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
        var hasExistingSqlite = HasDatabaseFile(sqlitePath);
        Directory.CreateDirectory(dir);
        if (!hasExistingSqlite)
            DeleteSqliteRelatedFiles(sqlitePath);
        try
        {
            if (File.Exists(jsonPath) && !hasExistingSqlite)
                MigrateJsonToNewSqlite(sqlitePath, originBaseDirPath, destBaseDirPath);

            return new SqliteBackupStateStore(sqlitePath, originBaseDirPath, destBaseDirPath, !hasExistingSqlite);
        }
        catch
        {
            if (!hasExistingSqlite)
            {
                try
                {
                    DeleteSqliteRelatedFiles(sqlitePath);
                }
                catch { }
            }

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
    }

    public static void DeleteDatabase(string originBaseDirPath, string destBaseDirPath)
    {
        foreach (var path in GetDatabaseFilePaths(originBaseDirPath, destBaseDirPath))
            File.Delete(path);
    }

    private static IEnumerable<string> GetSqliteRelatedFilePaths(string sqlitePath)
    {
        yield return sqlitePath;
        yield return sqlitePath + "-wal";
        yield return sqlitePath + "-shm";
    }

    private static void DeleteSqliteRelatedFiles(string sqlitePath)
    {
        foreach (var path in GetSqliteRelatedFilePaths(sqlitePath))
            File.Delete(path);
    }

    private static void MigrateJsonToNewSqlite(string sqlitePath, string originBaseDirPath, string destBaseDirPath)
    {
        var tempSqlitePath = sqlitePath + ".migrating";
        DeleteSqliteRelatedFiles(tempSqlitePath);

        SqliteBackupStateStore? store = null;
        try
        {
            store = new SqliteBackupStateStore(tempSqlitePath, originBaseDirPath, destBaseDirPath, true);
            store.MigrateFromJson();
            store.Checkpoint();
            store.Dispose();
            store = null;
            File.Move(tempSqlitePath, sqlitePath, false);
            try
            {
                DeleteSqliteRelatedFiles(tempSqlitePath);
            }
            catch { }
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
        tx.Commit();
    }

    private void ValidateExistingDatabaseBeforeSchemaChanges()
    {
        ValidateExistingTable("Meta", MetaColumns);
        ValidateExistingTable("Directories", DirectoryColumns);
        ValidateExistingTable("Files", FileColumns);

        var meta = ReadMeta(null);
        ValidateIdentityMeta(meta, "OriginBaseDirPath", OriginBaseDirPath, true);
        ValidateIdentityMeta(meta, "DestBaseDirPath", DestBaseDirPath, true);
        ValidateSchemaVersion(meta, true);
    }

    private void ValidateExistingTable(string tableName, IReadOnlyList<ExpectedColumn> expectedColumns)
    {
        var sql = ReadTableSql(tableName);
        if (sql == null)
            throw new InvalidOperationException($"SQLiteストアの{tableName}テーブルが見つかりません。");
        if (!sql.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLiteストアの{tableName}テーブルが現在のスキーマではありません。");

        var actualColumns = ReadTableColumns(tableName);
        if (actualColumns.Count != expectedColumns.Count)
            throw new InvalidOperationException($"SQLiteストアの{tableName}テーブルが現在のスキーマではありません。");
        for (var i = 0; i < expectedColumns.Count; i++)
        {
            if (!actualColumns[i].Equals(expectedColumns[i]))
                throw new InvalidOperationException($"SQLiteストアの{tableName}テーブルが現在のスキーマではありません。");
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
                throw new InvalidOperationException($"SQLiteストアの{key}が記録されていません。");
        }
        else if (actualValue != expectedValue)
        {
            throw new InvalidOperationException(
                $"SQLiteストアの{key}がバックアップペアと一致しません。 expected: '{expectedValue}', actual: '{actualValue}'");
        }
    }

    private void ValidateSchemaVersion(IReadOnlyDictionary<string, string?> meta, bool requireExisting)
    {
        if (!meta.TryGetValue("SchemaVersion", out var schemaVersionText))
        {
            if (requireExisting)
                throw new InvalidOperationException("SQLiteストアのSchemaVersionが記録されていません。");
        }
        else
        {
            if (!int.TryParse(schemaVersionText, out var currentSchemaVersion))
                throw new InvalidOperationException($"SQLiteストアのSchemaVersionが不正です。 actual: '{schemaVersionText}'");
            if (currentSchemaVersion < 1)
                throw new InvalidOperationException($"SQLiteストアのSchemaVersionが不正です。 actual: {currentSchemaVersion}");
            if (currentSchemaVersion > SchemaVersion)
                throw new InvalidOperationException(
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

    private void MigrateFromJson()
    {
        var legacy = DataFileWriter.Read<BackupDatabase>(BackupDatabase.GetDatabaseFileName(OriginBaseDirPath, DestBaseDirPath));
        if (legacy == null)
            return; // 読めなかった場合は空DB扱い
        ReplaceState(legacy.BackedUpDirectoriesDict, legacy.BackedUpFilesDict);
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

    internal void ReplaceState(IEnumerable<KeyValuePair<string, BackedUpDirectoryData>> directories,
        IEnumerable<KeyValuePair<string, BackedUpFileData>> files)
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

            tx.Commit();
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
            CommitWriteTransaction();
            _bulkWriteDepth = 0;
            DisposeCommands();
            _connection.Dispose();
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
    }

    private void RecordWrite()
    {
        if (_writeTransaction == null)
            return;

        _writeOperationsSinceCommit++;
        if (_writeOperationsSinceCommit >= WriteBatchSize)
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
}
