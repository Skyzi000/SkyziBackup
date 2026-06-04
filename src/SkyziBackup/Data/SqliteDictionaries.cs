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
    public ICollection<string> Keys => _store.EnumerateDirectories().Select(kv => kv.Key).ToList();
    public ICollection<BackedUpDirectoryData> Values => _store.EnumerateDirectories().Select(kv => kv.Value).ToList();
    public int Count => (int)Math.Min(int.MaxValue, _store.GetDirectoryCount());
    public bool IsReadOnly => false;
    public void Add(string key, BackedUpDirectoryData value) => _store.UpsertDirectory(key, value);
    public void Add(KeyValuePair<string, BackedUpDirectoryData> item) => Add(item.Key, item.Value);
    public void Clear() => _store.ClearDirectories();
    public bool Contains(KeyValuePair<string, BackedUpDirectoryData> item) => ContainsKey(item.Key);
    public bool ContainsKey(string key) => _store.GetDirectory(key) != null;
    public void CopyTo(KeyValuePair<string, BackedUpDirectoryData>[] array, int arrayIndex)
    {
        foreach (var kv in _store.EnumerateDirectories())
            array[arrayIndex++] = kv;
    }
    public IEnumerator<KeyValuePair<string, BackedUpDirectoryData>> GetEnumerator() => _store.EnumerateDirectories().GetEnumerator();
    public bool Remove(string key) { _store.RemoveDirectory(key); return true; }
    public bool Remove(KeyValuePair<string, BackedUpDirectoryData> item) => Remove(item.Key);
    public bool TryGetValue(string key, out BackedUpDirectoryData value)
    { value = _store.GetDirectory(key) ?? null!; return value != null; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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
    public ICollection<string> Keys => _store.EnumerateFiles().Select(kv => kv.Key).ToList();
    public ICollection<BackedUpFileData> Values => _store.EnumerateFiles().Select(kv => kv.Value).ToList();
    public int Count => (int)Math.Min(int.MaxValue, _store.GetFileCount());
    public bool IsReadOnly => false;
    public void Add(string key, BackedUpFileData value) => _store.UpsertFile(key, value);
    public void Add(KeyValuePair<string, BackedUpFileData> item) => Add(item.Key, item.Value);
    public void Clear() => _store.ClearFiles();
    public bool Contains(KeyValuePair<string, BackedUpFileData> item) => ContainsKey(item.Key);
    public bool ContainsKey(string key) => _store.GetFile(key) != null;
    public void CopyTo(KeyValuePair<string, BackedUpFileData>[] array, int arrayIndex)
    {
        foreach (var kv in _store.EnumerateFiles())
            array[arrayIndex++] = kv;
    }
    public IEnumerator<KeyValuePair<string, BackedUpFileData>> GetEnumerator() => _store.EnumerateFiles().GetEnumerator();
    public bool Remove(string key) { _store.RemoveFile(key); return true; }
    public bool Remove(KeyValuePair<string, BackedUpFileData> item) => Remove(item.Key);
    public bool TryGetValue(string key, out BackedUpFileData value)
    { value = _store.GetFile(key) ?? null!; return value != null; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
