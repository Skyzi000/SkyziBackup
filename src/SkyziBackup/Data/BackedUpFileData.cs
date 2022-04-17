using System;
using System.IO;
using System.Text.Json.Serialization;

namespace SkyziBackup.Data
{
    /// <summary>
    /// バックアップ済みファイルの詳細データ保管用クラス
    /// </summary>
    public class BackedUpFileData
    {
        [JsonPropertyName("c")]
        public DateTime? CreationTime { get; set; }

        [JsonPropertyName("w")]
        public DateTime? LastWriteTime { get; set; }

        [JsonPropertyName("o")]
        public long OriginSize { get; set; } = DefaultSize;

        [JsonPropertyName("a")]
        public FileAttributes? FileAttributes { get; set; }

        [JsonPropertyName("s")]
        public string? Sha1 { get; set; }

        public const long DefaultSize = -1;

        public BackedUpFileData() { }

        public BackedUpFileData(DateTime? creationTime = null,
            DateTime? lastWriteTime = null,
            long originSize = DefaultSize,
            FileAttributes? fileAttributes = null,
            string? sha1 = null)
        {
            CreationTime = creationTime;
            LastWriteTime = lastWriteTime;
            OriginSize = originSize;
            FileAttributes = fileAttributes;
            Sha1 = sha1;
        }
    }
}
