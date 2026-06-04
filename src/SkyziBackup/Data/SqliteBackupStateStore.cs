using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Skyzi000.Data;

// DataFileWriter

namespace SkyziBackup.Data;

/// <summary>
/// JSONで保持していた BackupDatabase の内容(二つの辞書)をSQLiteにほぼ1:1で保持する軽量ストア。
/// ユーザには透過。既存JSONがあれば初回アクセス時に自動移行する。
/// </summary>
public sealed class SqliteBackupStateStore : IDisposable
{
    public const string FileName = "database.sqlite";

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public string OriginBaseDirPath { get; }
    public string DestBaseDirPath { get; }

    private SqliteBackupStateStore(string dbPath, string originBaseDirPath, string destBaseDirPath)
    {
        _dbPath = dbPath;
        OriginBaseDirPath = originBaseDirPath;
        DestBaseDirPath = destBaseDirPath;
        _connection = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
        _connection.Open();
        InitPragmas();
        InitSchema();
        EnsureMeta();
    }

    public static SqliteBackupStateStore OpenOrMigrate(string originBaseDirPath, string destBaseDirPath)
    {
        // JSONの絶対パス
        var jsonPath = BackupDatabase.GetDatabasePath(originBaseDirPath, destBaseDirPath);
        var sqlitePath = GetDatabasePath(originBaseDirPath, destBaseDirPath);
        var dir = Path.GetDirectoryName(sqlitePath)!;
        Directory.CreateDirectory(dir);
        var needMigration = File.Exists(jsonPath) && !File.Exists(sqlitePath);
        var store = new SqliteBackupStateStore(sqlitePath, originBaseDirPath, destBaseDirPath);
        if (needMigration)
        {
            try
            {
                store.MigrateFromJson(jsonPath);
            }
            catch
            {
                // 失敗時はSQLiteファイル削除して再throw。呼び出し側でJSONフォールバック可。
                try
                {
                    store.Dispose();
                }
                catch { }

                try
                {
                    File.Delete(sqlitePath);
                }
                catch { }

                throw;
            }
        }

        return store;
    }

    public static string GetDatabasePath(string originBaseDirPath, string destBaseDirPath)
    {
        var jsonPath = BackupDatabase.GetDatabasePath(originBaseDirPath, destBaseDirPath);
        return Path.Combine(Path.GetDirectoryName(jsonPath) ?? throw new InvalidOperationException($"Path.GetDirectoryName(jsonPath) is null. (path: {jsonPath})"),
            FileName);
    }

    public static bool Exists(string originBaseDirPath, string destBaseDirPath) => File.Exists(GetDatabasePath(originBaseDirPath, destBaseDirPath));

    public static IEnumerable<string> GetDatabaseFilePaths(string originBaseDirPath, string destBaseDirPath)
    {
        var sqlitePath = GetDatabasePath(originBaseDirPath, destBaseDirPath);
        yield return sqlitePath;
        yield return sqlitePath + "-wal";
        yield return sqlitePath + "-shm";
    }

    public static void DeleteDatabase(string originBaseDirPath, string destBaseDirPath)
    {
        foreach (var path in GetDatabaseFilePaths(originBaseDirPath, destBaseDirPath))
            File.Delete(path);
    }

    internal BackupDatabase ToBackupDatabase() => new(OriginBaseDirPath, DestBaseDirPath)
    {
        BackedUpDirectoriesDict = new SqliteDirectoryDictionary(this),
        BackedUpFilesDict = new SqliteFileDictionary(this),
    };

    private void InitPragmas()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    private void InitSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Meta (Key TEXT PRIMARY KEY, Value TEXT);
CREATE TABLE IF NOT EXISTS Directories (
  Path TEXT PRIMARY KEY,
  CreationTimeUtc INTEGER NULL,
  LastWriteTimeUtc INTEGER NULL,
  FileAttributes INTEGER NULL
);
CREATE TABLE IF NOT EXISTS Files (
  Path TEXT PRIMARY KEY,
  CreationTimeUtc INTEGER NULL,
  LastWriteTimeUtc INTEGER NULL,
  OriginSize INTEGER NOT NULL,
  FileAttributes INTEGER NULL,
  Sha1 TEXT NULL
);";
        cmd.ExecuteNonQuery();
    }

    private void EnsureMeta()
    {
        using var tx = _connection.BeginTransaction();
        UpsertMetaInternal("OriginBaseDirPath", OriginBaseDirPath, tx);
        UpsertMetaInternal("DestBaseDirPath", DestBaseDirPath, tx);
        UpsertMetaInternal("SchemaVersion", "1", tx);
        tx.Commit();
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

    private void MigrateFromJson(string jsonPath)
    {
        var legacy = DataFileWriter.Read<BackupDatabase>(BackupDatabase.GetDatabaseFileName(OriginBaseDirPath, DestBaseDirPath));
        if (legacy == null)
            return; // 読めなかった場合は空DB扱い
        using var tx = _connection.BeginTransaction();
        foreach (var kv in legacy.BackedUpDirectoriesDict)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO Directories(Path,CreationTimeUtc,LastWriteTimeUtc,FileAttributes) VALUES(@p,@c,@w,@a)";
            cmd.Parameters.AddWithValue("@p", kv.Key);
            cmd.Parameters.AddWithValue("@c", (object?)kv.Value.CreationTime?.Ticks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@w", (object?)kv.Value.LastWriteTime?.Ticks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@a", (object?)kv.Value.FileAttributes ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        foreach (var kv in legacy.BackedUpFilesDict)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO Files(Path,CreationTimeUtc,LastWriteTimeUtc,OriginSize,FileAttributes,Sha1) VALUES(@p,@c,@w,@s,@a,@h)";
            cmd.Parameters.AddWithValue("@p", kv.Key);
            cmd.Parameters.AddWithValue("@c", (object?)kv.Value.CreationTime?.Ticks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@w", (object?)kv.Value.LastWriteTime?.Ticks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@s", kv.Value.OriginSize);
            cmd.Parameters.AddWithValue("@a", (object?)kv.Value.FileAttributes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@h", (object?)kv.Value.Sha1 ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public BackedUpDirectoryData? GetDirectory(string path)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT CreationTimeUtc,LastWriteTimeUtc,FileAttributes FROM Directories WHERE Path=@p";
        cmd.Parameters.AddWithValue("@p", path);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new BackedUpDirectoryData(
            r.IsDBNull(0) ? null : new DateTime(r.GetInt64(0), DateTimeKind.Local),
            r.IsDBNull(1) ? null : new DateTime(r.GetInt64(1), DateTimeKind.Local),
            r.IsDBNull(2) ? null : (FileAttributes)r.GetInt32(2));
    }

    public IEnumerable<KeyValuePair<string, BackedUpDirectoryData>> EnumerateDirectories()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Path,CreationTimeUtc,LastWriteTimeUtc,FileAttributes FROM Directories";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var path = r.GetString(0);
            yield return new KeyValuePair<string, BackedUpDirectoryData>(path, new BackedUpDirectoryData(
                r.IsDBNull(1) ? null : new DateTime(r.GetInt64(1), DateTimeKind.Local),
                r.IsDBNull(2) ? null : new DateTime(r.GetInt64(2), DateTimeKind.Local),
                r.IsDBNull(3) ? null : (FileAttributes)r.GetInt32(3))
            );
        }
    }

    public long GetDirectoryCount()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Directories";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void ClearDirectories()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Directories";
        cmd.ExecuteNonQuery();
    }

    public BackedUpFileData? GetFile(string path)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT CreationTimeUtc,LastWriteTimeUtc,OriginSize,FileAttributes,Sha1 FROM Files WHERE Path=@p";
        cmd.Parameters.AddWithValue("@p", path);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new BackedUpFileData(
            r.IsDBNull(0) ? null : new DateTime(r.GetInt64(0), DateTimeKind.Local),
            r.IsDBNull(1) ? null : new DateTime(r.GetInt64(1), DateTimeKind.Local),
            r.GetInt64(2),
            r.IsDBNull(3) ? null : (FileAttributes)r.GetInt32(3),
            r.IsDBNull(4) ? null : r.GetString(4));
    }

    public IEnumerable<KeyValuePair<string, BackedUpFileData>> EnumerateFiles()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Path,CreationTimeUtc,LastWriteTimeUtc,OriginSize,FileAttributes,Sha1 FROM Files";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var path = r.GetString(0);
            yield return new KeyValuePair<string, BackedUpFileData>(path, new BackedUpFileData(
                r.IsDBNull(1) ? null : new DateTime(r.GetInt64(1), DateTimeKind.Local),
                r.IsDBNull(2) ? null : new DateTime(r.GetInt64(2), DateTimeKind.Local),
                r.GetInt64(3),
                r.IsDBNull(4) ? null : (FileAttributes)r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5))
            );
        }
    }

    public long GetFileCount()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Files";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void ClearFiles()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Files";
        cmd.ExecuteNonQuery();
    }

    public void UpsertDirectory(string path, BackedUpDirectoryData data)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Directories(Path,CreationTimeUtc,LastWriteTimeUtc,FileAttributes) VALUES(@p,@c,@w,@a) " +
                          "ON CONFLICT(Path) DO UPDATE SET CreationTimeUtc=excluded.CreationTimeUtc,LastWriteTimeUtc=excluded.LastWriteTimeUtc,FileAttributes=excluded.FileAttributes";
        cmd.Parameters.AddWithValue("@p", path);
        cmd.Parameters.AddWithValue("@c", (object?)data.CreationTime?.Ticks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@w", (object?)data.LastWriteTime?.Ticks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", (object?)data.FileAttributes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void UpsertFile(string path, BackedUpFileData data)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Files(Path,CreationTimeUtc,LastWriteTimeUtc,OriginSize,FileAttributes,Sha1) VALUES(@p,@c,@w,@s,@a,@h) " +
                          "ON CONFLICT(Path) DO UPDATE SET CreationTimeUtc=excluded.CreationTimeUtc,LastWriteTimeUtc=excluded.LastWriteTimeUtc,OriginSize=excluded.OriginSize,FileAttributes=excluded.FileAttributes,Sha1=excluded.Sha1";
        cmd.Parameters.AddWithValue("@p", path);
        cmd.Parameters.AddWithValue("@c", (object?)data.CreationTime?.Ticks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@w", (object?)data.LastWriteTime?.Ticks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", data.OriginSize);
        cmd.Parameters.AddWithValue("@a", (object?)data.FileAttributes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@h", (object?)data.Sha1 ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void RemoveDirectory(string path)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Directories WHERE Path=@p";
        cmd.Parameters.AddWithValue("@p", path);
        cmd.ExecuteNonQuery();
    }

    public void RemoveFile(string path)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Files WHERE Path=@p";
        cmd.Parameters.AddWithValue("@p", path);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _connection.Dispose();
    }
}
