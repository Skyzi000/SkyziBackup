using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace SkyziBackup
{
    public class BackupDatabase : SaveableData
    {
        [JsonPropertyName("ob")]
        public string OriginBaseDirPath { get; set; }

        [JsonPropertyName("db")]
        public string DestBaseDirPath { get; set; }

        /// <summary>
        /// originDirPathをキーとするバックアップ済みディレクトリの辞書
        /// </summary>
        [JsonPropertyName("dd")]
        public Dictionary<string, BackedUpDirectoryData> BackedUpDirectoriesDict { get; set; } = new();

        /// <summary>
        /// originFilePathをキーとするバックアップ済みファイルの辞書
        /// </summary>
        [JsonPropertyName("fd")]
        public Dictionary<string, BackedUpFileData> BackedUpFilesDict { get; set; } = new();

        /// <summary>
        /// ファイル名は(<see cref="OriginBaseDirPath" /> + <see cref="DestBaseDirPath" />)のSHA1
        /// </summary>
        [JsonIgnore]
        public override string SaveFileName => DataFileWriter.GetDatabaseFileName(OriginBaseDirPath, DestBaseDirPath);

        public static readonly string FileName = "Database" + DataFileWriter.DefaultExtension;

        private readonly int _tempCount = 0;

        public BackupDatabase()
        {
            OriginBaseDirPath = DestBaseDirPath = string.Empty;
        }

        public BackupDatabase(string originBaseDirPath, string destBaseDirPath)
        {
            OriginBaseDirPath = originBaseDirPath;
            DestBaseDirPath = destBaseDirPath;
        }

        public override void AutoSave()
        {
            ThrowIfDisposed();
            Semaphore.Wait();
            try
            {
                using var temp = new BackupDatabase(OriginBaseDirPath, DestBaseDirPath)
                {
                    BackedUpDirectoriesDict = new Dictionary<string, BackedUpDirectoryData>(BackedUpDirectoriesDict),
                    BackedUpFilesDict = new Dictionary<string, BackedUpFileData>(BackedUpFilesDict),
                };
                var path = DataFileWriter.GetPath(temp);
                var tempDirPath =
                    Path.Combine(Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"Path.GetDirectoryName(path) is null. (path: {path})"),
                        "Temp");
                var tempPath = Path.Combine(tempDirPath, $"Database{_tempCount}{DataFileWriter.TempFileExtension}");
                DataFileWriter.Write(temp, tempPath);
                DataFileWriter.Replace(tempPath, path, true);
            }
            finally
            {
                Semaphore.Release();
            }
        }
    }
}
