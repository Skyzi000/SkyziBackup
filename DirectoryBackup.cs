﻿using NLog;
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
        public bool isEnableDeletion;
        [DataMember]
        public bool isRecordPassword;
        [DataMember]
        public DataProtectionScope passwordProtectionScope;
        [DataMember]
        public string protectedPassword;
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
        

        public HashSet<Regex> Regices { get; private set; }
        public string SaveFileName => nameof(BackupSettings);

        public string IgnorePattern { get => ignorePattern; set { ignorePattern = value; UpdateRegices(); } }

        public BackupSettings()
        {
            isUseDatabase = true;
            isCopyAttributes = true;
            isEnableDeletion = false;
            isRecordPassword = true;
            passwordProtectionScope = DataProtectionScope.LocalMachine;
            protectedPassword = null;
            retryCount = 10;
            retryWaitMilliSec = 10000;
            comparisonMethod = ComparisonMethod.WriteTime;
            ignorePattern = string.Empty;
            compressionLevel = CompressionLevel.NoCompression;
            compressAlgorithm = CompressAlgorithm.Deflate;
        }

        public override string ToString() => 
            $"データベースを利用する-----------\t: {isUseDatabase}\n" +
            $"属性をコピーする-----------------\t: {isCopyAttributes}\n" +
            $"パスワードを記録する-------------\t: {isRecordPassword}\n" +
            $"記録したパスワードのスコープ-----\t: {passwordProtectionScope}\n" +
            $"リトライ回数---------------------\t: {retryCount}\n" +
            $"リトライ待機時間(ミリ秒)---------\t: {retryWaitMilliSec}\n" +
            $"ファイル比較方法-----------------\t: {comparisonMethod}\n" +
            $"圧縮レベル-----------------------\t: {compressionLevel}\n" +
            $"圧縮アルゴリズム-----------------\t: {compressAlgorithm}\n" +
            $"除外パターン---------------------\t: \n{IgnorePattern}\n";

        public static BackupSettings InitOrLoadGlobalSettings()
        {
            bool isExists = File.Exists(DataContractWriter.GetPath<BackupSettings>(nameof(BackupSettings)));
            return isExists
                ? DataContractWriter.Read<BackupSettings>(nameof(BackupSettings))
                : new BackupSettings();
        }
        public void UpdateRegices()
        {
            string[] patStrArr = IgnorePattern.Split(new[] { "\r\n", "\n", "\r", "|" }, StringSplitOptions.None);
            Regices = new HashSet<Regex>(patStrArr.Select(s => ShapePattern(s)));
        }
        private Regex ShapePattern(string strPattern)
        {
            return new Regex("^" + Regex.Escape(strPattern).Replace(@"\*", ".*").Replace(@"\?", ".?") + (Regex.IsMatch(strPattern, @"\\$") ? @".*$" : @"$"), RegexOptions.Compiled);
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
        public BackupSettings Settings { get { return Database?.localSettings ?? _globalSettings; } set { _globalSettings = value; } }
        public BackupDatabase Database { get; private set; } = null;

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly HashAlgorithm sha1Provider = new SHA1CryptoServiceProvider();
        private readonly string originBaseDirPath;
        private readonly string destBaseDirPath;
        private int currentRetryCount = 0;
        private BackupSettings _globalSettings;

        public DirectoryBackup(string originPath, string destPath, string password = "", BackupSettings globalSettings = null)
        {
            originBaseDirPath = originPath;
            destBaseDirPath = destPath;
            Settings = globalSettings ?? BackupSettings.InitOrLoadGlobalSettings();
            if (Settings.isUseDatabase)
            {
                InitOrLoadDatabase();
            }
            if (password != "")
            {
                AesCrypter = new OpensslCompatibleAesCrypter(password, compressionLevel: Settings.compressionLevel, compressAlgorithm: Settings.compressAlgorithm);
            }
            Results.Finished += Results_Finished;
        }

        private void InitOrLoadDatabase()
        {
            string databasePath;
            bool isExists = File.Exists(databasePath = DataContractWriter.GetPath<BackupDatabase>(ComputeStringSHA1(originBaseDirPath + destBaseDirPath)));
            logger.Info(Results.Message = isExists ? $"既存のデータベースをロード: '{databasePath}'" : "新規データベースを初期化");
            Database = isExists
                ? DataContractWriter.Read<BackupDatabase>(ComputeStringSHA1(originBaseDirPath + destBaseDirPath))
                : new BackupDatabase(originBaseDirPath, destBaseDirPath);
        }

        private void Results_Finished(object sender, EventArgs e)
        {
            if (Settings.isUseDatabase)
            {
                logger.Info(Results.Message = $"データベースを保存: {DataContractWriter.GetPath(Database)}");
                Database.failedFiles = Results.failedFiles;
                DataContractWriter.Write(Database);
            }
            Results.Message = (Results.isSuccess ? "バックアップ完了: " : Results.Message + "\nバックアップ失敗: ") + DateTime.Now;
            logger.Info("{0}\n________________________________________\n\n", Results.isSuccess ? "バックアップ完了" : "バックアップ失敗");
        }

        public BackupResults StartBackup()
        {
            if (Results.IsFinished)
            {
                throw new NotImplementedException("現在、このクラスのインスタンスは再利用されることを想定していません。");
            }
            if (Settings.isEnableDeletion)
            {
                logger.Error(Results.Message = "削除機能は未実装です。");
                throw new NotImplementedException(Results.Message);
            }

            logger.Info("バックアップ設定:\n{0}", Settings);
            logger.Info("バックアップを開始'{0}' => '{1}'", originBaseDirPath, destBaseDirPath);
            
            // 初期化処理
            if (Settings.isUseDatabase)
            {
                // 万が一データベースと一致しない場合は読み込みなおす(データベースファイルを一旦削除かリネームする処理を入れても良いかも)
                if (Database.destBaseDirPath != destBaseDirPath)
                {
                    InitOrLoadDatabase();
                    if (Database.destBaseDirPath != destBaseDirPath)
                    {
                        logger.Error(Results.Message = $"データベースの読み込み失敗: データベース'{DataContractWriter.GetPath(Database)}'を利用できません。");
                        throw new FormatException($"The database value 'destBaseDirPath' is invalid.({Database.destBaseDirPath})");
                    }
                }
                Database.failedFiles = new HashSet<string>();
                Database.ignoreFiles = new HashSet<string>();
                Database.deletedFiles = new HashSet<string>();
                Database.backedUpDirectories = new HashSet<string>();
            }
            else
            {
                Results.backedUpFiles = new HashSet<string>();
            }

            if (!Directory.Exists(originBaseDirPath))
            {
                logger.Error(Results.Message = $"バックアップ元のディレクトリ'{originBaseDirPath}'が見つかりません。");
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
                        if (reg.IsMatch((originDirPath + @"\").Substring(originBaseDirPath.Length)))
                        {
                            logger.Info("除外パターン '{0}' に一致 : '{1}'", reg.ToString(), originDirPath);
                            isIgnore = true;
                            break;
                        }
                    }
                    if (isIgnore) continue;
                }
                string destDirPath = originDirPath.Replace(originBaseDirPath, destBaseDirPath);
                logger.Info(Results.Message = $"存在しなければ作成: '{destDirPath}'");
                var dir = Directory.CreateDirectory(destDirPath);
                if (Settings.isCopyAttributes)
                {
                    logger.Debug("ディレクトリ属性をコピー");
                    dir.Attributes = File.GetAttributes(originDirPath);
                    dir.CreationTime = Directory.GetCreationTime(originDirPath);
                    dir.LastWriteTime = Directory.GetLastWriteTime(originDirPath);
                }
                if (Settings.isUseDatabase) Database.backedUpDirectories.Add(originDirPath);
            }

            // ファイルの処理
            foreach (string originFilePath in Directory.EnumerateFiles(originBaseDirPath, "*", SearchOption.AllDirectories))
            {
                string destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                if (!IsIgnoredFile(originFilePath, destFilePath))
                {
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
                logger.Info($"{Settings.retryWaitMilliSec} ミリ秒毎に {Settings.retryCount} 回リトライ");
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
                        logger.Info("ファイルをスキップ(除外パターンに一致 '{0}') : '{1}'", reg.ToString(), originFilePath);
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
                        logger.Error($"データベースに更新日時が記録されていません。バックアップ先にファイルが存在しません。: '{destFilePath}'");
                        return false;
                    }
                    logger.Warn($"データベースに更新日時が記録されていません。バックアップ先の更新日時を記録します。: {destFileData.lastWriteTime = File.GetLastWriteTime(destFilePath)}");
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
                    logger.Warn("ファイルサイズの比較ができません: データベースにファイルサイズが記録されていません。");
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
                    logger.Warn("ハッシュの比較ができません: データベースにSHA1が記録されていません。");
                    return false;
                }
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
                    logger.Error("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }
                else if (Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    logger.Error("生データの比較ができません: 圧縮有効時に生データを比較することはできません。");
                    return false;
                }
                if (!File.Exists(destFilePath))
                {
                    logger.Error($"バックアップ先にファイルが存在しません。: '{destFilePath}'");
                    return false;
                }
                logger.Warn(Results.Message = $"生データの比較にはデータベースを利用できません。直接比較します。'{originFilePath}' = '{destFilePath}'");
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
            logger.Info("ファイルをスキップ(バックアップ済み): '{0}'", originFilePath);
            return true;
        }
        private bool IsMatchFileWithoutDatabase(string originFilePath, string destFilePath)
        {
            if (!File.Exists(destFilePath)) return false;
            if (Settings.comparisonMethod == ComparisonMethod.NoComparison) return false;
            if (!Settings.isCopyAttributes ) logger.Error("ファイルを正しく比較出来ません: ファイル属性のコピーが無効になっています。ファイルを比較するにはデータベースを利用するか、ファイル属性のコピーを有効にしてください。");
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
                    logger.Error("ファイルサイズの比較ができません: 暗号化有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
                    return false;
                }
                else if(Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    logger.Error("ファイルサイズの比較ができません: 圧縮有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
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
                    logger.Error("ハッシュの比較ができません: 暗号化有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }
                else if (Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    logger.Error("ハッシュの比較ができません: 圧縮有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }
                logger.Warn("データベースを利用しない場合、ハッシュでの比較は非効率的です。データベースを利用するか、生データでの比較を検討してください。");
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
                    logger.Error("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }
                else if (Settings.compressionLevel != CompressionLevel.NoCompression)
                {
                    logger.Error("生データの比較ができません: 圧縮有効時に生データを比較することはできません。");
                    return false;
                }
                logger.Debug(Results.Message = $"生データの比較: '{originFilePath}' = '{destFilePath}'");
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
            logger.Info("ファイルをスキップ(バックアップ済み): '{0}'", originFilePath);
            return true;
        }

        private void FileBackup(string originFilePath, string destFilePath)
        {
            logger.Info(Results.Message = $"ファイルをバックアップ: '{originFilePath}' => '{destFilePath}'");
            if (AesCrypter != null)
            {
                try
                {
                    if (AesCrypter.EncryptFile(originFilePath, destFilePath))
                    {
                        // 暗号化成功時処理
                        FileInfo originInfo = null;
                        BackedUpFileData data = null;
                        if (Settings.isUseDatabase)
                        {
                            data = new BackedUpFileData(
                                destFilePath: destFilePath,
                                lastWriteTime: Settings.comparisonMethod.HasFlag(ComparisonMethod.WriteTime) ? (originInfo = new FileInfo(originFilePath)).LastWriteTime : (DateTime?)null,
                                originSize: Settings.comparisonMethod.HasFlag(ComparisonMethod.Size) ? (originInfo = new FileInfo(originFilePath)).Length : BackedUpFileData.DefaultSize,
                                fileAttributes: Settings.comparisonMethod != ComparisonMethod.NoComparison ? (originInfo = new FileInfo(originFilePath)).Attributes : (FileAttributes?)null,
                                sha1: Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1) ? ComputeFileSHA1(originFilePath) : null
                                );
                        }
                        if (Settings.isCopyAttributes)
                        {
                            if (Settings.isUseDatabase)
                            {
                                if (Database.backedUpFilesDict.ContainsKey(originFilePath) && Database.backedUpFilesDict[originFilePath].fileAttributes != (originInfo ??= new FileInfo(originFilePath)).Attributes)
                                {
                                    logger.Info($"属性をコピー: '{originFilePath}'");
                                    var destInfo = new FileInfo(destFilePath);
                                    destInfo.CreationTime = originInfo.CreationTime;
                                    destInfo.LastWriteTime = originInfo.LastWriteTime;
                                    destInfo.Attributes = originInfo.Attributes;
                                }
                            }
                            if (Settings.comparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute) && (originInfo ??= new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                            {
                                originInfo.Attributes = RemoveAttribute(originInfo.Attributes, FileAttributes.Archive);
                            }
                        }
                        logger.Info("成功!");
                        if (Settings.isUseDatabase)
                        {
                            Database.backedUpFilesDict[originFilePath] = data;
                        }
                        else
                        {
                            Results.backedUpFiles.Add(originFilePath);
                        }
                        Results.failedFiles.Remove(originFilePath);
                    }
                    else
                    {
                        logger.Error(Results.Message = $"暗号化失敗: {AesCrypter.Error}");
                        Results.failedFiles.Add(originFilePath);
                    }
                }
                catch (Exception e)
                {
                    logger.Error(Results.Message = $"失敗: {e}");
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
            logger.Debug(Results.Message = $"リトライ待機中...({currentRetryCount + 1}/{Settings.retryCount}回目)");

            System.Threading.Thread.Sleep(Settings.retryWaitMilliSec);

            currentRetryCount++;
            logger.Info(Results.Message = $"リトライ {currentRetryCount}/{Settings.retryCount} 回目");
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
            var bs = sha1Provider.ComputeHash(stream);
            return BitConverter.ToString(bs).ToLower().Replace("-", "");
        }
        public static string ComputeStringSHA1(string value) => ComputeStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(value)));
    }
}