using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SkyziBackup.Properties;
using Timer = System.Timers.Timer;

namespace SkyziBackup
{
    public abstract class SaveableData : IDisposable
    {
        [JsonIgnore]
        public virtual string? SaveFileName { get; }

        [JsonIgnore]
        public Timer SaveTimer
        {
            get => _saveTimer ??= new Timer();
            set => _saveTimer = value;
        }

        private Timer? _saveTimer;

        [JsonIgnore]
        public SemaphoreSlim Semaphore
        {
            get => _semaphore ??= new SemaphoreSlim(1, 1);
            set => _semaphore = value;
        }

        private SemaphoreSlim? _semaphore;
        private bool _disposedValue;

        public virtual void StartAutoSave(double intervalMsec)
        {
            _saveTimer?.Stop();
            _saveTimer?.Dispose();
            _saveTimer = null;
            SaveTimer.Interval = intervalMsec;
            SaveTimer.Elapsed += (s, e) =>
            {
                if (Semaphore.CurrentCount != 0)
                    AutoSave();
            };
            SaveTimer.Start();
        }

        public virtual void AutoSave() => Save();

        public virtual void Save(string? filePath = null)
        {
            Semaphore.Wait();
            try
            {
                DataFileWriter.Write(this, filePath, true);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual async Task SaveAsync(string? filePath = null, CancellationToken cancellationToken = default)
        {
            await Semaphore.WaitAsync();
            try
            {
                await DataFileWriter.WriteAsync(this, filePath, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual void Delete()
        {
            Semaphore.Wait();
            try
            {
                DataFileWriter.Delete(this);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _semaphore?.Dispose();
                    _saveTimer?.Stop();
                    _saveTimer?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

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

    public class DataFileWriter
    {
        public const string JsonExtension = ".json";
        public static readonly string DefaultExtension = JsonExtension;
        public static readonly string ParentDirectoryName = "Data";
        public static readonly string TempFileExtension = ".tmp";
        public static readonly string BackupFileExtension = ".bac";

        private static JsonSerializerOptions SerializerOptions { get; } = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            IgnoreNullValues = true,
        };

        public static string GetPath(SaveableData obj) => GetPath(obj.SaveFileName ?? throw new ArgumentException(nameof(obj.SaveFileName)));
        public static string GetPath(string fileName) => Path.Combine(Settings.Default.AppDataPath, fileName);

        /// <summary>
        /// <see cref="ParentDirectoryName" />とSHA1ハッシュ値でAppDataPathからの相対ディレクトリパスを求める。
        /// </summary>
        /// <remarks>GetQualifiedもついでに呼んでるので予めTrim()したりする必要はないよ♡</remarks>
        /// <returns>AppDataPathからの相対パス</returns>
        public static string GetDatabaseDirectoryName(string originBaseDirPath, string destBaseDirPath) => Path.Combine(ParentDirectoryName,
            BackupManager.ComputeStringSHA1(
                BackupController.GetQualifiedDirectoryPath(originBaseDirPath) +
                BackupController.GetQualifiedDirectoryPath(destBaseDirPath)
            )
        );

        // TODO: ここ以下のデータベース関連メソッドはBackupDatabaseクラスの方に移動させる
        /// <summary>
        /// <see cref="BackupDatabase.FileName" />と<see cref="GetDatabaseDirectoryName(string, string)" />でAppDataPathからの相対ファイルパスを求める。
        /// </summary>
        /// <remarks>引数はもちろん予めTrim()したりする必要はない</remarks>
        /// <returns>AppDataPathからの相対パス</returns>
        public static string GetDatabaseFileName(string originBaseDirPath, string destBaseDirPath) =>
            Path.Combine(GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), BackupDatabase.FileName);

        /// <summary>
        /// <see cref="GetDatabaseFileName(string, string)" />と<see cref="GetPath(string)" />で絶対パスを得る。
        /// </summary>
        /// <remarks>引数は生で良い</remarks>
        /// <returns>データベースの絶対パス</returns>
        public static string GetDatabasePath(string originBaseDirPath, string destBaseDirPath) =>
            GetPath(GetDatabaseFileName(originBaseDirPath, destBaseDirPath));

        public static async Task WriteAsync(SaveableData obj, string? filePath = null, bool makeBackup = false, CancellationToken cancellationToken = default)
        {
            if (filePath is null && obj.SaveFileName is null)
                throw new ArgumentNullException(nameof(filePath));
            filePath ??= GetPath(obj);
            var tmpPath = filePath + TempFileExtension;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? throw new ArgumentException(nameof(filePath)));
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, obj, obj.GetType(), SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            Replace(tmpPath, filePath, makeBackup);
        }

        public static void Replace(string sourceFileName, string destinationFileName, bool makeBackup = false)
        {
            try
            {
                File.Replace(sourceFileName, destinationFileName, makeBackup ? destinationFileName + BackupFileExtension : null, true);
            }
            catch (Exception)
            {
                if (makeBackup && File.Exists(destinationFileName))
                    File.Move(destinationFileName, destinationFileName + BackupFileExtension, true);
                File.Move(sourceFileName, destinationFileName, true);
            }
        }

        public static void Write(SaveableData obj, string? filePath = null, bool makeBackup = false) => WriteAsync(obj, filePath, makeBackup).Wait();

        public static async Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default) where T : SaveableData
        {
            var filePath = GetPath(fileName);
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await JsonSerializer.DeserializeAsync<T>(fs, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // デシリアライズに失敗した場合、バックアップがあればそっちを読みに行く
                var bacFilePath = filePath + BackupFileExtension;
                if (!File.Exists(bacFilePath))
                    throw;
                using var bfs = new FileStream(bacFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await JsonSerializer.DeserializeAsync<T>(bfs, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        public static T? Read<T>(string fileName) where T : SaveableData => ReadAsync<T>(fileName).Result;
        public static void Delete(SaveableData obj) => File.Delete(GetPath(obj));
        public static void Delete<T>(string fileName) where T : SaveableData => File.Delete(GetPath(fileName));

        public static void DeleteDatabase(string originBaseDirPath, string destBaseDirPath) =>
            Delete<BackupDatabase>(GetDatabaseFileName(originBaseDirPath, destBaseDirPath));
    }
}
