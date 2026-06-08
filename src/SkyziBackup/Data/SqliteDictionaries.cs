using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SkyziBackup.Data;

// BackupDatabase の辞書型と互換の SQLite バッキング辞書 (最小機能: Indexer, Keys, ContainsKey, Remove, Add)
internal sealed class SqliteDirectoryDictionary : IDictionary<string, BackedUpDirectoryData>
{
    private readonly SqliteBackupStateStore _store;
    public SqliteDirectoryDictionary(SqliteBackupStateStore store) => _store = store;
    public BackedUpDirectoryData this[string key]
    {
        get => _store.GetDirectory(key) ?? throw new KeyNotFoundException(key);
        set => _store.UpsertDirectory(key, value);
    }
    public ICollection<string> Keys => _store.EnumerateDirectoryKeys();
    public ICollection<BackedUpDirectoryData> Values => _store.EnumerateDirectories().Select(kv => kv.Value).ToList();
    public int Count => (int)Math.Min(int.MaxValue, _store.GetDirectoryCount());
    public bool IsReadOnly => false;
    public void Add(string key, BackedUpDirectoryData value)
    {
        if (_store.ContainsDirectory(key))
            throw new ArgumentException("同じキーの項目が既に追加されています。", nameof(key));
        _store.UpsertDirectory(key, value);
    }
    public void Add(KeyValuePair<string, BackedUpDirectoryData> item) => Add(item.Key, item.Value);
    public void Clear() => _store.ClearDirectories();
    public bool Contains(KeyValuePair<string, BackedUpDirectoryData> item) =>
        _store.GetDirectory(item.Key) is { } value && DirectoryDataEquals(value, item.Value);
    public bool ContainsKey(string key) => _store.ContainsDirectory(key);
    public void CopyTo(KeyValuePair<string, BackedUpDirectoryData>[] array, int arrayIndex)
    {
        foreach (var kv in _store.EnumerateDirectories())
            array[arrayIndex++] = kv;
    }
    public IEnumerator<KeyValuePair<string, BackedUpDirectoryData>> GetEnumerator() => _store.EnumerateDirectories().GetEnumerator();
    public bool Remove(string key) => _store.RemoveDirectory(key);
    public bool Remove(KeyValuePair<string, BackedUpDirectoryData> item) => Contains(item) && Remove(item.Key);
    public bool TryGetValue(string key, out BackedUpDirectoryData value)
    { value = _store.GetDirectory(key) ?? null!; return value != null; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static bool DirectoryDataEquals(BackedUpDirectoryData x, BackedUpDirectoryData y) =>
        x.CreationTime == y.CreationTime &&
        x.LastWriteTime == y.LastWriteTime &&
        x.FileAttributes == y.FileAttributes;
}

internal sealed class SqliteFileDictionary : IDictionary<string, BackedUpFileData>
{
    private readonly SqliteBackupStateStore _store;
    public SqliteFileDictionary(SqliteBackupStateStore store) => _store = store;
    public BackedUpFileData this[string key]
    {
        get => _store.GetFile(key) ?? throw new KeyNotFoundException(key);
        set => _store.UpsertFile(key, value);
    }
    public ICollection<string> Keys => _store.EnumerateFileKeys();
    public ICollection<BackedUpFileData> Values => _store.EnumerateFiles().Select(kv => kv.Value).ToList();
    public int Count => (int)Math.Min(int.MaxValue, _store.GetFileCount());
    public bool IsReadOnly => false;
    public void Add(string key, BackedUpFileData value)
    {
        if (_store.ContainsFile(key))
            throw new ArgumentException("同じキーの項目が既に追加されています。", nameof(key));
        _store.UpsertFile(key, value);
    }
    public void Add(KeyValuePair<string, BackedUpFileData> item) => Add(item.Key, item.Value);
    public void Clear() => _store.ClearFiles();
    public bool Contains(KeyValuePair<string, BackedUpFileData> item) =>
        _store.GetFile(item.Key) is { } value && FileDataEquals(value, item.Value);
    public bool ContainsKey(string key) => _store.ContainsFile(key);
    public void CopyTo(KeyValuePair<string, BackedUpFileData>[] array, int arrayIndex)
    {
        foreach (var kv in _store.EnumerateFiles())
            array[arrayIndex++] = kv;
    }
    public IEnumerator<KeyValuePair<string, BackedUpFileData>> GetEnumerator() => _store.EnumerateFiles().GetEnumerator();
    public bool Remove(string key) => _store.RemoveFile(key);
    public bool Remove(KeyValuePair<string, BackedUpFileData> item) => Contains(item) && Remove(item.Key);
    public bool TryGetValue(string key, out BackedUpFileData value)
    { value = _store.GetFile(key) ?? null!; return value != null; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static bool FileDataEquals(BackedUpFileData x, BackedUpFileData y) =>
        x.CreationTime == y.CreationTime &&
        x.LastWriteTime == y.LastWriteTime &&
        x.OriginSize == y.OriginSize &&
        x.FileAttributes == y.FileAttributes &&
        x.Sha1 == y.Sha1;
}
