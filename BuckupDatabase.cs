using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SkyziBackup
{
    public interface ISaveableData
    {
        public string SaveFileName { get; }
    }

    public class BackupDatabase : ISaveableData
    {
        [JsonPropertyName("ob")]
        public string OriginBaseDirPath { get; set; }
        [JsonPropertyName("db")]
        public string DestBaseDirPath { get; set; }

        /// <summary>
        /// originFilePathをキーとするバックアップ済みファイルの辞書
        /// </summary>
        [JsonPropertyName("fd")]
        public Dictionary<string, BackedUpFileData> BackedUpFilesDict { get; set; } = new Dictionary<string, BackedUpFileData>();

        /// <summary>
        /// バックアップ済みディレクトリの辞書
        /// </summary>
        [JsonPropertyName("dd")]
        public Dictionary<string, BackedUpDirectoryData> BackedUpDirectoriesDict { get; set; } = new Dictionary<string, BackedUpDirectoryData>();


        /// <summary>
        /// ファイル名は(OriginBaseDirPath + DestBaseDirPath)のSHA1
        /// </summary>
        [JsonIgnore]
        public string SaveFileName => DataFileWriter.GetDatabaseFileName(OriginBaseDirPath, DestBaseDirPath);

        public BackupDatabase(string originBaseDirPath, string destBaseDirPath)
        {
            this.OriginBaseDirPath = originBaseDirPath;
            this.DestBaseDirPath = destBaseDirPath;
        }
    }

    /// <summary>
    /// バックアップ済みファイルの詳細データ保管用クラス
    /// </summary>
    public class BackedUpFileData
    {
        [JsonPropertyName("c")]
        public DateTime? CreationTime { get; set; } = null;
        [JsonPropertyName("w")]
        public DateTime? LastWriteTime { get; set; } = null;
        [JsonPropertyName("o")]
        public long OriginSize { get; set; } = DefaultSize;
        [JsonPropertyName("a")]
        private int? FileAttributesInt { get; set; } = null;
        [JsonPropertyName("s")]
        public string Sha1 { get; set; } = null;
        [JsonIgnore]
        public FileAttributes? FileAttributes { get => (FileAttributes?)FileAttributesInt; set => FileAttributesInt = (int?)value; }

        public const long DefaultSize = -1;

        public BackedUpFileData(DateTime? creationTime = null, DateTime? lastWriteTime = null, long originSize = DefaultSize, FileAttributes? fileAttributes = null, string sha1 = null)
        {
            this.CreationTime = creationTime;
            this.LastWriteTime = lastWriteTime;
            this.OriginSize = originSize;
            this.FileAttributes = fileAttributes;
            this.Sha1 = sha1;
        }
    }
    /// <summary>
    /// バックアップ済みディレクトリの詳細データ保管用クラス
    /// </summary>
    public class BackedUpDirectoryData
    {
        [JsonPropertyName("c")]
        public DateTime? CreationTime { get; set; } = null;
        [JsonPropertyName("w")]
        public DateTime? LastWriteTime { get; set; } = null;
        [JsonPropertyName("a")]
        private int? FileAttributesInt { get; set; } = null;
        [JsonIgnore]
        public FileAttributes? FileAttributes { get => (FileAttributes?)FileAttributesInt; set => FileAttributesInt = (int?)value; }

        public BackedUpDirectoryData(DateTime? creationTime = null, DateTime? lastWriteTime = null, FileAttributes? fileAttributes = null)
        {
            this.CreationTime = creationTime;
            this.LastWriteTime = lastWriteTime;
            this.FileAttributes = fileAttributes;
        }
    }

    public class DataFileWriter
    {
        public static readonly string ParentDirectoryName = "Data";
        public static readonly string DatabaseFileName = "Database.json";

        private static JsonSerializerOptions SerializerOptions { get; } = new JsonSerializerOptions()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            IgnoreNullValues = true,
        };
        public static string GetPath(object obj)
        {
            switch (obj)
            {
                case ISaveableData data:
                    return GetPath(data.SaveFileName);
                default:
                    throw new ArgumentException($"'{obj.GetType()}'型に対応するパス設定はありません。");
            }
        }
        public static string GetPath(string fileName) => Path.Combine(Properties.Settings.Default.AppDataPath, fileName);
        public static string GetDatabaseDirectoryName(string originBaseDirPath, string destBaseDirPath) => Path.Combine(ParentDirectoryName, BackupController.ComputeStringSHA1(originBaseDirPath + destBaseDirPath));
        public static string GetDatabaseFileName(string originBaseDirPath, string destBaseDirPath) => Path.Combine(GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), DatabaseFileName);
        public static string GetDatabasePath(string originBaseDirPath, string destBaseDirPath) => GetPath(GetDatabaseFileName(originBaseDirPath, destBaseDirPath));
        // TODO: エラー処理を追加する
        public static async Task WriteAsync<T>(T obj, CancellationToken cancellationToken = default) where T : ISaveableData
        {
            string filePath = GetPath(obj);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(fs, obj, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        public static void Write<T>(T obj) where T : ISaveableData
        {
            WriteAsync(obj).Wait();
        }
        public static async Task<T> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default) where T : ISaveableData
        {
            using var fs = new FileStream(GetPath(fileName), FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<T>(fs, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        public static T Read<T>(string fileName) where T : ISaveableData
        {
            return ReadAsync<T>(fileName).Result;
        }
        public static void Delete(object obj)
        {
            File.Delete(GetPath(obj));
        }
        public static void Delete<T>(string fileName) where T : ISaveableData
        {
            File.Delete(GetPath(fileName));
        }
        public static void DeleteDatabase(string originBaseDirPath, string destBaseDirPath) => Delete<BackupDatabase>(GetDatabaseFileName(originBaseDirPath, destBaseDirPath));
    }
}