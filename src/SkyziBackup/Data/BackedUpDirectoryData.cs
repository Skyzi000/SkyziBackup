using System;
using System.IO;
using System.Text.Json.Serialization;

namespace SkyziBackup
{
    /// <summary>
    /// バックアップ済みディレクトリの詳細データ保管用クラス
    /// </summary>
    public class BackedUpDirectoryData
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
    }
}
