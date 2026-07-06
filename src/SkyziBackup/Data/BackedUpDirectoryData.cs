using System;
using System.IO;
using System.Text.Json.Serialization;

namespace SkyziBackup.Data
{
    /// <summary>
    /// バックアップ済みディレクトリの詳細データ保管用クラス
    /// </summary>
    public class BackedUpDirectoryData : IEquatable<BackedUpDirectoryData>
    {
        [JsonPropertyName("c")]
        public DateTime? CreationTime { get; set; }

        [JsonPropertyName("w")]
        public DateTime? LastWriteTime { get; set; }

        [JsonPropertyName("a")]
        public FileAttributes? FileAttributes { get; set; }

        public BackedUpDirectoryData() { }

        public BackedUpDirectoryData(DateTime? creationTime = null, DateTime? lastWriteTime = null, FileAttributes? fileAttributes = null)
        {
            CreationTime = creationTime;
            LastWriteTime = lastWriteTime;
            FileAttributes = fileAttributes;
        }

        /// <summary>
        /// 全フィールドの値で等価比較する
        /// </summary>
        /// <remarks>ミュータブルなクラスなので、ハッシュベースのコレクションのキーには使用しないこと</remarks>
        public bool Equals(BackedUpDirectoryData? other) =>
            other is not null &&
            (ReferenceEquals(this, other) ||
             CreationTime == other.CreationTime &&
             LastWriteTime == other.LastWriteTime &&
             FileAttributes == other.FileAttributes);

        public override bool Equals(object? obj) => Equals(obj as BackedUpDirectoryData);

        public override int GetHashCode() => HashCode.Combine(CreationTime, LastWriteTime, FileAttributes);
    }
}
