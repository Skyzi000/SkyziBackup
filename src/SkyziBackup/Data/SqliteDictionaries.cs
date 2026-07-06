using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SkyziBackup.Data;

// BackupDatabase の辞書型と互換の SQLite バッキング辞書の共通基底 (最小機能: Indexer, Keys, ContainsKey, Remove, Add)
// 派生クラスは SqliteBackupStateStore の対応するメソッドに委譲するだけの薄いラッパーにする
internal abstract class SqliteBackedDictionary<TValue> : IDictionary<string, TValue> where TValue : class, IEquatable<TValue>
{
    protected SqliteBackupStateStore Store { get; }
    protected SqliteBackedDictionary(SqliteBackupStateStore store) => Store = store;

    protected abstract TValue? GetValue(string key);
    protected abstract void UpsertValue(string key, TValue value);
    protected abstract bool RemoveValue(string key);
    protected abstract bool ContainsKeyCore(string key);
    protected abstract IEnumerable<KeyValuePair<string, TValue>> Enumerate();
    protected abstract ICollection<string> EnumerateKeys();
    protected abstract long GetCount();
    protected abstract void ClearValues();

    public TValue this[string key]
    {
        get => GetValue(key) ?? throw new KeyNotFoundException(key);
        set => UpsertValue(key, value);
    }
    public ICollection<string> Keys => EnumerateKeys();
    public ICollection<TValue> Values => Enumerate().Select(kv => kv.Value).ToList();
    public int Count => (int)Math.Min(int.MaxValue, GetCount());
    public bool IsReadOnly => false;
    public void Add(string key, TValue value)
    {
        if (ContainsKeyCore(key))
            throw new ArgumentException("同じキーの項目が既に追加されています。", nameof(key));
        UpsertValue(key, value);
    }
    public void Add(KeyValuePair<string, TValue> item) => Add(item.Key, item.Value);
    public void Clear() => ClearValues();
    public bool Contains(KeyValuePair<string, TValue> item) =>
        GetValue(item.Key) is { } value && value.Equals(item.Value);
    public bool ContainsKey(string key) => ContainsKeyCore(key);
    public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
    {
        foreach (var kv in Enumerate())
            array[arrayIndex++] = kv;
    }
    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => Enumerate().GetEnumerator();
    public bool Remove(string key) => RemoveValue(key);
    public bool Remove(KeyValuePair<string, TValue> item) => Contains(item) && Remove(item.Key);
    public bool TryGetValue(string key, out TValue value)
    { value = GetValue(key) ?? null!; return value != null; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class SqliteDirectoryDictionary : SqliteBackedDictionary<BackedUpDirectoryData>
{
    public SqliteDirectoryDictionary(SqliteBackupStateStore store) : base(store) { }
    protected override BackedUpDirectoryData? GetValue(string key) => Store.GetDirectory(key);
    protected override void UpsertValue(string key, BackedUpDirectoryData value) => Store.UpsertDirectory(key, value);
    protected override bool RemoveValue(string key) => Store.RemoveDirectory(key);
    protected override bool ContainsKeyCore(string key) => Store.ContainsDirectory(key);
    protected override IEnumerable<KeyValuePair<string, BackedUpDirectoryData>> Enumerate() => Store.EnumerateDirectories();
    protected override ICollection<string> EnumerateKeys() => Store.EnumerateDirectoryKeys();
    protected override long GetCount() => Store.GetDirectoryCount();
    protected override void ClearValues() => Store.ClearDirectories();
}

internal sealed class SqliteFileDictionary : SqliteBackedDictionary<BackedUpFileData>
{
    public SqliteFileDictionary(SqliteBackupStateStore store) : base(store) { }
    protected override BackedUpFileData? GetValue(string key) => Store.GetFile(key);
    protected override void UpsertValue(string key, BackedUpFileData value) => Store.UpsertFile(key, value);
    protected override bool RemoveValue(string key) => Store.RemoveFile(key);
    protected override bool ContainsKeyCore(string key) => Store.ContainsFile(key);
    protected override IEnumerable<KeyValuePair<string, BackedUpFileData>> Enumerate() => Store.EnumerateFiles();
    protected override ICollection<string> EnumerateKeys() => Store.EnumerateFileKeys();
    protected override long GetCount() => Store.GetFileCount();
    protected override void ClearValues() => Store.ClearFiles();
}
