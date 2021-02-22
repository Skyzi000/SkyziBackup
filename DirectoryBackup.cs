using NLog;
using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Linq;
using System.Text;

namespace SkyziBackup
{
    [Flags]
    public enum ComparisonMethod
    {
        /// <summary>
        /// 比較無し
        /// </summary>
        NoComparison            = 0,
        /// <summary>
        /// Archive属性による比較(バックアップ時に元ファイルのArchive属性を変更する点に注意)
        /// </summary>
        ArchiveAttribute        = 1,
        /// <summary>
        /// 更新日時による比較
        /// </summary>
        WriteTime               = 1 << 1,
        /// <summary>
        /// サイズによる比較
        /// </summary>
        Size                    = 1 << 2,
        /// <summary>
        /// SHA1による比較
        /// </summary>
        FileContentsSHA1        = 1 << 3,
        /// <summary>
        /// 生データによる比較(データベースを利用出来ず、暗号化や圧縮と併用できない点に注意)
        /// </summary>
        FileContentsRaw         = 1 << 4,
    }

    [DataContract]
    [KnownType(typeof(BackupSettings))]
    [KnownType(typeof(DataProtectionScope))]
    [KnownType(typeof(ComparisonMethod))]
    [KnownType(typeof(IDataContractSerializable))]
    [KnownType(typeof(CompressionLevel))]
    [KnownType(typeof(CompressAlgorithm))]
    public class BackupSettings : IDataContractSerializable
    {
        [DataMember]
        public bool isUseDatabase;
        [DataMember]
        public bool isCopyAttributes;
        [DataMember]
        public bool isOverwriteReadonly;
        [DataMember]
        public bool isEnableDeletion;
        [DataMember]
        public bool isRecordPassword;
        [DataMember]
        public DataProtectionScope passwordProtectionScope;
        [DataMember]
        private string protectedPassword;
        [DataMember]
        public int retryCount;
        [DataMember]
        public int retryWaitMilliSec;
        [DataMember]
        public ComparisonMethod comparisonMethod;
        [DataMember]
        private string ignorePattern;
        [DataMember]
        public CompressionLevel compressionLevel;
        [DataMember]
        public CompressAlgorithm compressAlgorithm;
        [IgnoreDataMember]
        private string directory;
        [IgnoreDataMember]
        public HashSet<Regex> Regices { get; private set; }
        public bool IsGlobal => string.IsNullOrEmpty(directory);
        public string SaveFileName => IsGlobal ? nameof(BackupSettings) : Path.Combine(directory, nameof(BackupSettings));

        public string IgnorePattern { get => ignorePattern; set { ignorePattern = value; UpdateRegices(); } }
        /// <summary>
        /// 暗号化されたパスワード。代入した場合自動的に暗号化される。(予め暗号化する必要はない)
        /// </summary>
        public string ProtectedPassword { get => protectedPassword; set { protectedPassword = (string.IsNullOrEmpty(value)) ? null : PasswordManager.Encrypt(value, passwordProtectionScope); } }

        public BackupSettings()
        {
            isUseDatabase = true;
            isCopyAttributes = true;
            isOverwriteReadonly = false;
            isEnableDeletion = false;
            isRecordPassword = true;
            passwordProtectionScope = DataProtectionScope.LocalMachine;
            protectedPassword = null;
            retryCount = 10;
            retryWaitMilliSec = 10000;
            comparisonMethod = ComparisonMethod.WriteTime | ComparisonMethod.Size;
            ignorePattern = string.Empty;
            compressionLevel = CompressionLevel.NoCompression;
            compressAlgorithm = CompressAlgorithm.Deflate;
        }
        /// <summary>
        /// ローカル設定用のコンストラクタ
        /// </summary>
        public BackupSettings(string originBaseDirPath, string destBaseDirPath) : this()
        {
            directory = DataContractWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("設定ファイルのパス---------------: {0}\n", DataContractWriter.GetPath(this));
            sb.AppendFormat("これはグローバル設定か-----------: {0}\n", IsGlobal);
            sb.AppendFormat("データベースを利用する-----------: {0}\n", isUseDatabase);
            sb.AppendFormat("属性をコピーする-----------------: {0}\n", isCopyAttributes);
            sb.AppendFormat("読み取り専用ファイルを上書きする-: {0}\n", isOverwriteReadonly);
            sb.AppendFormat("パスワードを記録する-------------: {0}\n", isRecordPassword);
            sb.AppendFormat("記録したパスワードのスコープ-----: {0}\n", passwordProtectionScope);
            sb.AppendFormat("リトライ回数---------------------: {0}\n", retryCount);
            sb.AppendFormat("リトライ待機時間(ミリ秒)---------: {0}\n", retryWaitMilliSec);
            sb.AppendFormat("ファイル比較方法-----------------: {0}\n", comparisonMethod);
            sb.AppendFormat("圧縮レベル-----------------------: {0}\n", compressionLevel);
            sb.AppendFormat("圧縮アルゴリズム-----------------: {0}\n", compressAlgorithm);
            sb.AppendFormat("除外パターン---------------------: \n{0}\n", IgnorePattern);
            return sb.ToString();
        }
        
        public static BackupSettings LoadOrCreateGlobalSettings()
        {
            bool isExists = File.Exists(DataContractWriter.GetPath(nameof(BackupSettings)));
            return isExists
                ? DataContractWriter.Read<BackupSettings>(nameof(BackupSettings))
                : new BackupSettings();
        }
        public static bool TryLoadLocalSettings(string originBaseDirPath, string destBaseDirPath, out BackupSettings localSettings)
        {
            string fileName;
            if (File.Exists(DataContractWriter.GetPath(fileName = Path.Combine(DataContractWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), nameof(BackupSettings)))))
            {
                localSettings = DataContractWriter.Read<BackupSettings>(fileName);
                return true;
            }
            localSettings = null;
            return false;
        }
        public static BackupSettings LoadLocalSettingsOrNull(string originBaseDirPath, string destBaseDirPath) => TryLoadLocalSettings(originBaseDirPath, destBaseDirPath, out BackupSettings localSettings) ? localSettings : null;
        public void UpdateRegices()
        {
            string[] patStrArr = IgnorePattern.Split(new[] { "\r\n", "\n", "\r", "|" }, StringSplitOptions.None);
            Regices = new HashSet<Regex>(patStrArr.Select(s => ShapePattern(s)));
        }
        internal string GetRawPassword()
        {
            if (!isRecordPassword || string.IsNullOrEmpty(protectedPassword)) throw new ArgumentException($"パスワードが保存されていません。");
            return PasswordManager.Decrypt(protectedPassword, passwordProtectionScope);
        }
        public bool IsDifferentPassword(string newPassword)
        {
            return string.IsNullOrEmpty(protectedPassword) || GetRawPassword() != newPassword;
        }
        private Regex ShapePattern(string strPattern)
        {
            var sb = new StringBuilder("^");
            if (!strPattern.StartsWith(Path.DirectorySeparatorChar) && !strPattern.StartsWith('*')) sb.Append(Path.DirectorySeparatorChar, 2);
            sb.Append(Regex.Escape(strPattern).Replace(@"\*", ".*").Replace(@"\?", ".?"));
            sb.Append(Path.EndsInDirectorySeparator(strPattern) ? @".*$" : @"$");
            return new Regex(sb.ToString(), RegexOptions.Compiled);
        }
    }
    public class BackupResults
    {
        /// <summary>
        /// 完了したかどうか。リトライ中はfalse。
        /// </summary>
        public bool IsFinished { get => _isFinished; set { _isFinished = value; if (_isFinished) OnFinished(EventArgs.Empty); } }

        private bool _isFinished;

        public event EventHandler Finished;

        protected virtual void OnFinished(EventArgs e) => Finished?.Invoke(this, e);

        /// <summary>
        /// 成功したかどうか。バックアップ進行中や一つでも失敗したファイルがある場合はfalse。
        /// </summary>
        public bool isSuccess;

        /// <summary>
        /// 全体的なメッセージ。
        /// </summary>
        public string Message { get => _message; set { _message = value; OnMessageChanged(EventArgs.Empty); } }

        private string _message;

        public event EventHandler MessageChanged;

        protected virtual void OnMessageChanged(EventArgs e) => MessageChanged?.Invoke(this, e);

        /// <summary>
        /// 新しくバックアップされたファイルのパス。(データベース利用時は常にnull)
        /// </summary>
        public HashSet<string> backedUpFiles = null;

        /// <summary>
        /// バックアップ対象だが失敗したファイルのパス。
        /// </summary>
        public HashSet<string> failedFiles;

        public BackupResults(bool isSuccess, bool isFinished = false, string message = "")
        {
            this.isSuccess = isSuccess;
            this.IsFinished = isFinished;
            this.Message = message;
            failedFiles = new HashSet<string>();
        }
    }

    internal class DirectoryBackup
    {
        public BackupResults Results { get; private set; } = new BackupResults(false);
        public OpensslCompatibleAesCrypter AesCrypter { get; set; }
        public BackupSettings Settings { get; set; }
        public BackupDatabase Database { get; private set; } = null;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly HashAlgorithm SHA1Provider = new SHA1CryptoServiceProvider();
        private readonly string originBaseDirPath;
        private readonly string destBaseDirPath;
        private int currentRetryCount = 0;

        public DirectoryBackup(string originPath, string destPath, string password = null, BackupSettings settings = null)
        {
            originBaseDirPath = Path.TrimEndingDirectorySeparator(originPath);
            destBaseDirPath = Path.TrimEndingDirectorySeparator(destPath);
            Settings = settings ?? BackupSettings.LoadLocalSettingsOrNull(originBaseDirPath, destBaseDirPath) ?? BackupSettings.LoadOrCreateGlobalSettings();
            if (Settings.isUseDatabase)
            {
                LoadOrCreateDatabase();
            }
            if (!string.IsNullOrEmpty(password))
            {
                AesCrypter = new OpensslCompatibleAesCrypter(password, compressionLevel: Settings.compressionLevel, compressAlgorithm: Settings.compressAlgorithm);
            }
            Results.Finished += Results_Finished;
        }

        private void LoadOrCreateDatabase()
        {
            string databasePath;
            bool isExists = File.Exists(databasePath = DataContractWriter.GetDatabasePath(originBaseDirPath, destBaseDirPath));
            Logger.Info(Results.Message = isExists ? $"既存のデータベースをロード: '{databasePath}'" : "新規データベースを初期化");
            Database = isExists
                ? DataContractWriter.Read<BackupDatabase>(DataContractWriter.GetDatabaseFileName(originBaseDirPath, destBaseDirPath))
                : new BackupDatabase(originBaseDirPath, destBaseDirPath);
        }

        private void Results_Finished(object sender, EventArgs e)
        {
            if (Settings.isUseDatabase)
            {
                Logger.Info("データベースを保存: '{0}'", DataContractWriter.GetPath(Database));
                Database.failedFiles = Results.failedFiles;
                DataContractWriter.Write(Database);
            }
            Results.Message = (Results.isSuccess ? "バックアップ完了: " : Results.Message + "\nバックアップ失敗: ") + DateTime.Now;
            Logger.Info("{0}\n=============================\n\n", Results.isSuccess ? "バックアップ完了" : "バックアップ失敗");
        }

        public BackupResults StartBackup()
        {
            if (Results.IsFinished)
            {
                throw new NotImplementedException("現在、このクラスのインスタンスは再利用されることを想定していません。");
            }
            if (Settings.isEnableDeletion)
            {
                Logger.Error(Results.Message = "削除機能は未実装です。");
                throw new NotImplementedException(Results.Message);
            }

            Logger.Info("バックアップ設定:\n{0}", Settings);
            Logger.Info("バックアップを開始'{0}' => '{1}'", originBaseDirPath, destBaseDirPath);
            
            // 初期化処理
            if (Settings.isUseDatabase)
            {
                // 万が一データベースと一致しない場合は読み込みなおす(データベースファイルを一旦削除かリネームする処理を入れても良いかも)
                if (Database.destBaseDirPath != destBaseDirPath)
                {
                    LoadOrCreateDatabase();
                    if (Database.destBaseDirPath != destBaseDirPath)
                    {
                        Logger.Error(Results.Message = $"データベースの読み込み失敗: データベース'{DataContractWriter.GetPath(Database)}'を利用できません。");
                        throw new FormatException($"The database value 'destBaseDirPath' is invalid.({Database.destBaseDirPath})");
                    }
                }
                Database.failedFiles = new HashSet<string>();
                Database.ignoreFiles = new HashSet<string>();
                Database.deletedFiles = new HashSet<string>();
            }
            else
            {
                Results.backedUpFiles = new HashSet<string>();
            }

            if (!Directory.Exists(originBaseDirPath))
            {
                Logger.Error(Results.Message = $"バックアップ元のディレクトリ'{originBaseDirPath}'が見つかりません。");
                Results.IsFinished = true;
                return Results;
            }

            // ディレクトリの処理
            foreach (string originDirPath in Directory.EnumerateDirectories(originBaseDirPath, "*", SearchOption.AllDirectories))
            {
                if (Settings.Regices != null)
                {
                    bool isIgnore = false;
                    foreach (var reg in Settings.Regices)
                    {
                        if (reg.IsMatch((originDirPath + Path.DirectorySeparatorChar).Substring(originBaseDirPath.Length)))
                        {
                            Logger.Info("ディレクトリをスキップ(除外パターン '{0}' に一致) : '{1}'", reg, originDirPath);
                            isIgnore = true;
                            break;
                        }
                    }
                    if (isIgnore) continue;
                }
                DirectoryInfo originDirInfo = Settings.isCopyAttributes ? new DirectoryInfo(originDirPath) : null;
                DirectoryInfo destDirInfo = null;
                if (!Settings.isUseDatabase || !Database.backedUpDirectoriesDict.ContainsKey(originDirPath))
                {
                    string destDirPath = originDirPath.Replace(originBaseDirPath, destBaseDirPath);
                    Logger.Debug(Results.Message = $"存在しなければ作成: '{destDirPath}'");
                    destDirInfo = Directory.CreateDirectory(destDirPath);
                    if (Settings.isCopyAttributes)
                    {
                        Logger.Debug("ディレクトリ属性をコピー");
                        destDirInfo.CreationTime = originDirInfo.CreationTime;
                        destDirInfo.LastWriteTime = originDirInfo.LastWriteTime;
                        destDirInfo.Attributes = originDirInfo.Attributes;
                    }
                }
                // 以前のバックアップデータがある場合、変更されたプロパティのみ更新する(変更なしなら何もしない)
                else if (Settings.isCopyAttributes)
                {
                    string destDirPath = originDirPath.Replace(originBaseDirPath, destBaseDirPath);
                    if (originDirInfo.CreationTime != Database.backedUpDirectoriesDict[originDirPath].creationTime)
                        (destDirInfo = Directory.CreateDirectory(destDirPath)).CreationTime = originDirInfo.CreationTime;
                    if (originDirInfo.LastWriteTime != Database.backedUpDirectoriesDict[originDirPath].lastWriteTime)
                        (destDirInfo ??= Directory.CreateDirectory(destDirPath)).LastWriteTime = originDirInfo.LastWriteTime;
                    if (originDirInfo.Attributes != Database.backedUpDirectoriesDict[originDirPath].fileAttributes)
                        (destDirInfo ?? Directory.CreateDirectory(destDirPath)).Attributes = originDirInfo.Attributes;
                }
                if (Settings.isUseDatabase)
                {
                    Database.backedUpDirectoriesDict[originDirPath] = new BackedUpDirectoryData(originDirInfo?.CreationTime, originDirInfo?.LastWriteTime, originDirInfo?.Attributes);
                }
            }

            // ファイルの処理
            foreach (string originFilePath in Directory.EnumerateFiles(originBaseDirPath, "*", SearchOption.AllDirectories))
            {
                string destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                if (!IsIgnoredFile(originFilePath, destFilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));
                    FileBackup(originFilePath, destFilePath);
                }
                else if (Settings.isUseDatabase)
                {
                    Database.ignoreFiles.Add(originFilePath);
                }
            }

            Results.isSuccess = Results.failedFiles.Count == 0;

            // リトライ処理
            if (Results.failedFiles.Count != 0 && Settings.retryCount > 0)
            {
                Logger.Info($"{Settings.retryWaitMilliSec} ミリ秒毎に {Settings.retryCount} 回リトライ");
                RetryStart();
            }
            else
            {
                Results.IsFinished = true;
            }
            return Results;
        }
        private bool IsIgnoredFile(string originFilePath, string destFilePath)
        {
            // 除外パターンとマッチング
            if (Settings.Regices != null)
            {
                foreach (var reg in Settings.Regices)
                {
                    if (reg.IsMatch(originFilePath.Substring(originBaseDirPath.Length)))
                    {
                        Logger.Info("ファイルをスキップ(除外パターンに一致 '{0}') : '{1}'", reg, originFilePath);
                        return true;
                    }
                }
            }
            // バックアップ済みのファイルと一致したら無視
            return Settings.isUseDatabase ? IsMatchFileOnDatabase(originFilePath, destFilePath) : IsMatchFileWithoutDatabase(originFilePath, destFilePath);
        }

        private bool IsMatchFileOnDatabase(string originFilePath, string destFilePath)
        {
            if (!Database.backedUpFilesDict.ContainsKey(originFilePath)) return false;
            if (Settings.comparisonMethod == ComparisonMethod.NoComparison) return false;
            FileInfo originFileInfo = null;
            BackedUpFileData destFileData = Database.backedUpFilesDict[originFilePath];

            // Archive属性
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute))
            {
                if ((originFileInfo = new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                {
                    return false;
                }
            }

            // 更新日時
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.WriteTime))
            {
                if (destFileData.lastWriteTime == null)
                {
                    if (!File.Exists(destFilePath))
                    {
                        Logger.Error("データベースに更新日時が記録されていません。バックアップ先にファイルが存在しません。: '{0}'", destFilePath);
                        return false;
                    }
                    Logger.Warn("データベースに更新日時が記録されていません。バックアップ先の更新日時を記録します。: '{0}'", destFileData.lastWriteTime = File.GetLastWriteTime(destFilePath));
                }
                if ((originFileInfo?.LastWriteTime ?? (originFileInfo = new FileInfo(originFilePath)).LastWriteTime) != destFileData.lastWriteTime)
                {
                    return false;
                }
            }

            // サイズ
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.Size))
            {
                if (destFileData.originSize == -1)
                {
                    Logger.Warn("ファイルサイズの比較ができません: データベースにファイルサイズが記録されていません。");
                    return false;
                }
                if ((originFileInfo?.Length ?? (originFileInfo = new FileInfo(originFilePath)).Length) != destFileData.originSize)
                {
                    return false;
                }
            }

            // SHA1
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            {
                if (destFileData.sha1 == null)
                {
                    Logger.Warn("ハッシュの比較ができません: データベースにSHA1が記録されていません。");
                    return false;
                }
                Logger.Debug(Results.Message = $"SHA1で比較: '{destFileData.sha1}' : '{originFilePath}'");
                if (ComputeFileSHA1(originFilePath) != destFileData.sha1)
                {
                    return false;
                }
            }

            // 生データ(データベースを用いず比較)
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsRaw))
            {
                if (AesCrypter != null)
                {
                    Logger.Error("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }
                else if (Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Error("生データの比較ができません: 圧縮有効時に生データを比較することはできません。");
                    return false;
                }
                if (!File.Exists(destFilePath))
                {
                    Logger.Error("バックアップ先にファイルが存在しません。: '{0}'", destFilePath);
                    return false;
                }
                Logger.Warn(Results.Message = $"生データの比較にはデータベースを利用できません。直接比較します。'{originFilePath}' = '{destFilePath}'");
                int originByte;
                int destByte;
                using var originStream = originFileInfo?.OpenRead() ?? new FileStream(originFilePath, FileMode.Open, FileAccess.Read);
                using var destStream = new FileStream(destFilePath, FileMode.Open, FileAccess.Read);
                do
                {
                    originByte = originStream.ReadByte();
                    destByte = destStream.ReadByte();
                } while ((originByte == destByte) && (originByte != -1));
                if (originByte != destByte)
                {
                    return false;
                }
            }

            // 相違が検出されなかった
            Logger.Info("ファイルをスキップ(バックアップ済み): '{0}'", originFilePath);
            return true;
        }
        private bool IsMatchFileWithoutDatabase(string originFilePath, string destFilePath)
        {
            if (!File.Exists(destFilePath)) return false;
            if (Settings.comparisonMethod == ComparisonMethod.NoComparison) return false;
            if (!Settings.isCopyAttributes ) Logger.Error("ファイルを正しく比較出来ません: ファイル属性のコピーが無効になっています。ファイルを比較するにはデータベースを利用するか、ファイル属性のコピーを有効にしてください。");
            FileInfo destFileInfo = null;
            FileInfo originFileInfo = null;

            // Archive属性
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute))
            {
                if ((originFileInfo = new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                {
                    return false;
                }
            }

            // 更新日時
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.WriteTime))
            {
                if ((originFileInfo?.LastWriteTime ?? (originFileInfo = new FileInfo(originFilePath)).LastWriteTime) != (destFileInfo = new FileInfo(destFilePath)).LastWriteTime)
                {
                    return false;
                }
            }

            // サイズ
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.Size))
            {
                if (AesCrypter != null)
                {
                    Logger.Error("ファイルサイズの比較ができません: 暗号化有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
                    return false;
                }
                else if(Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Error("ファイルサイズの比較ができません: 圧縮有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
                    return false;
                }
                if ((originFileInfo?.Length ?? (originFileInfo = new FileInfo(originFilePath)).Length) != (destFileInfo?.Length ?? (destFileInfo = new FileInfo(destFilePath)).Length))
                {
                    return false;
                }
            }

            // SHA1
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            {
                if (AesCrypter != null)
                {
                    Logger.Error("ハッシュの比較ができません: 暗号化有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }
                else if (Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Error("ハッシュの比較ができません: 圧縮有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }
                Logger.Warn("データベースを利用しない場合、ハッシュでの比較は非効率的です。データベースを利用するか、生データでの比較を検討してください。");
                Logger.Debug(Results.Message = $"SHA1で比較: '{originFilePath}' = '{destFilePath}'");
                if (ComputeFileSHA1(originFilePath) != ComputeFileSHA1(destFilePath))
                {
                    return false;
                }
            }

            // 生データ
            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsRaw))
            {
                if (AesCrypter != null)
                {
                    Logger.Error("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }
                else if (Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Error("生データの比較ができません: 圧縮有効時に生データを比較することはできません。");
                    return false;
                }
                Logger.Debug(Results.Message = $"生データの比較: '{originFilePath}' = '{destFilePath}'");
                int originByte;
                int destByte;
                using var originStream = originFileInfo?.OpenRead() ?? new FileStream(originFilePath, FileMode.Open, FileAccess.Read);
                using var destStream = destFileInfo?.OpenRead() ?? new FileStream(destFilePath, FileMode.Open, FileAccess.Read);
                do
                {
                    originByte = originStream.ReadByte();
                    destByte = destStream.ReadByte();
                } while ((originByte == destByte) && (originByte != -1));
                if (originByte != destByte)
                {
                    return false;
                }
            }

            // 相違が検出されなかった
            Logger.Info("ファイルをスキップ(バックアップ済み): '{0}'", originFilePath);
            return true;
        }
        /// <summary>
        /// 読み取り専用属性を持っていないとデータベースに記録されているファイルかどうか。
        /// </summary>
        /// <returns>前回のバックアップ時に読み取り専用属性がなかった場合 true, 対象のファイルが読み取り専用属性を持っていたり、データが無い場合は false</returns>
        private bool IsNotReadOnlyInDatabase(string originFilePath) => Database.backedUpFilesDict.TryGetValue(originFilePath, out BackedUpFileData data) && data.fileAttributes.HasValue && !data.fileAttributes.Value.HasFlag(FileAttributes.ReadOnly);
        private void FileBackup(string originFilePath, string destFilePath)
        {
            Logger.Info(Results.Message = $"ファイルをバックアップ: '{originFilePath}' => '{destFilePath}'");
            if (Settings.isOverwriteReadonly)
            {
                // バックアップ先ファイルが読み取り専用なら解除する(読み取り専用でないとデータベースに記録されているファイルは確かめない)
                if (!(Settings.isUseDatabase && IsNotReadOnlyInDatabase(originFilePath)))
                {
                    FileInfo destInfo = new FileInfo(destFilePath);
                    if (destInfo.Exists && destInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        destInfo.Attributes = RemoveAttribute(destInfo.Attributes, FileAttributes.ReadOnly);
                    }
                }
                // IsNotReadOnlyInDatabase()の戻り値が true かつバックアップ先ファイルが読み取り専用属性を持つ場合はSystem.UnauthorizedAccessExceptionで失敗する
            }
            if (AesCrypter != null)
            {
                try
                {
                    if (AesCrypter.EncryptFile(originFilePath, destFilePath))
                    {
                        // 暗号化成功時処理
                        FileInfo originInfo = null;
                        FileInfo destInfo = null;
                        if (Settings.isCopyAttributes)
                        {
                            if (!Settings.isUseDatabase || !Database.backedUpFilesDict.TryGetValue(originFilePath, out var data))
                            {
                                Logger.Debug($"属性をコピー");
                                destInfo = new FileInfo(destFilePath)
                                {
                                    CreationTime = (originInfo = new FileInfo(originFilePath)).CreationTime,
                                    LastWriteTime = originInfo.LastWriteTime,
                                    Attributes = originInfo.Attributes
                                };
                            }
                            // 以前のバックアップデータがある場合、変更されたプロパティのみ更新する(変更なしなら何もしない)
                            else
                            {
                                if (!data.creationTime.HasValue || (originInfo = new FileInfo(originFilePath)).CreationTime != data.creationTime)
                                    (destInfo = new FileInfo(destFilePath)).CreationTime = originInfo.CreationTime;
                                if (!data.lastWriteTime.HasValue || originInfo.LastWriteTime != data.lastWriteTime)
                                    (destInfo ??= new FileInfo(destFilePath)).LastWriteTime = originInfo.LastWriteTime;
                                if (!data.fileAttributes.HasValue || originInfo.Attributes != data.fileAttributes)
                                    (destInfo ?? new FileInfo(destFilePath)).Attributes = originInfo.Attributes;
                                // バックアップ先ファイルから取り除いた読み取り専用属性を戻す
                                else if (Settings.isOverwriteReadonly && data.fileAttributes.Value.HasFlag(FileAttributes.ReadOnly))
                                    (destInfo ?? new FileInfo(destFilePath)).Attributes = data.fileAttributes.Value;
                            }
                            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute) && originInfo.Attributes.HasFlag(FileAttributes.Archive))
                            {
                                (originInfo ??= new FileInfo(originFilePath)).Attributes = RemoveAttribute(originInfo.Attributes, FileAttributes.Archive);
                            }
                        }
                        if (Settings.isUseDatabase)
                        {
                            // 必要なデータだけを保存
                            Database.backedUpFilesDict[originFilePath] = new BackedUpFileData(
                                creationTime: Settings.isCopyAttributes ? (originInfo ??= new FileInfo(originFilePath)).CreationTime : (DateTime?)null,
                                lastWriteTime: Settings.isCopyAttributes || Settings.comparisonMethod.HasFlag(ComparisonMethod.WriteTime) ? (originInfo ??= new FileInfo(originFilePath)).LastWriteTime : (DateTime?)null,
                                originSize: Settings.comparisonMethod.HasFlag(ComparisonMethod.Size) ? (originInfo ??= new FileInfo(originFilePath)).Length : BackedUpFileData.DefaultSize,
                                fileAttributes: Settings.isCopyAttributes || Settings.comparisonMethod != ComparisonMethod.NoComparison ? (originInfo ??= new FileInfo(originFilePath)).Attributes : (FileAttributes?)null,
                                sha1: Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1) ? ComputeFileSHA1(originFilePath) : null
                                );
                        }
                        else
                        {
                            Results.backedUpFiles.Add(originFilePath);
                        }
                        Results.failedFiles.Remove(originFilePath);
                    }
                    else
                    {
                        Logger.Error(AesCrypter.Error, Results.Message = $"暗号化に失敗しました '{originFilePath}' => '{destFilePath}'\n");
                        Results.failedFiles.Add(originFilePath);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Logger.Error(ex, Results.Message = $"アクセスが拒否されました '{originFilePath}' => '{destFilePath}'\n");
                    Results.failedFiles.Add(originFilePath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originFilePath}' => '{destFilePath}'\n");
                    Results.failedFiles.Add(originFilePath);
                }
            }
            else
            {
                throw new NotImplementedException("非暗号化バックアップは未実装です。");
            }
        }

        private void RetryStart()
        {
            if (currentRetryCount >= Settings.retryCount)
            {
                Results.IsFinished = true;
                return;
            }
            Logger.Debug(Results.Message = $"リトライ待機中...({currentRetryCount + 1}/{Settings.retryCount}回目)");

            System.Threading.Thread.Sleep(Settings.retryWaitMilliSec);

            currentRetryCount++;
            Logger.Info(Results.Message = $"リトライ {currentRetryCount}/{Settings.retryCount} 回目");
            foreach (string originFilePath in Results.failedFiles.ToArray())
            {
                string destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                FileBackup(originFilePath, destFilePath);
            }
            Results.isSuccess = Results.failedFiles.Count == 0;
            if (Results.isSuccess)
            {
                Results.IsFinished = true;
                return;
            }
            else
            {
                RetryStart();
            }
        }

        private static FileAttributes RemoveAttribute(FileAttributes attributes, FileAttributes attributesToRemove)
        {
            return attributes & ~attributesToRemove;
        }

        public static string ComputeFileSHA1(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ComputeStreamSHA1(fs);
        }

        public static string ComputeStreamSHA1(Stream stream)
        {
            var bs = SHA1Provider.ComputeHash(stream);
            return BitConverter.ToString(bs).ToLower().Replace("-", "");
        }
        public static string ComputeStringSHA1(string value) => ComputeStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(value)));
    }
}