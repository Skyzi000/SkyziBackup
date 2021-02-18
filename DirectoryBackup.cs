using NLog;
using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Linq;

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
        /// サイズによる比較(対応したデータベースが必須、データがない場合は常にスキップされない)
        /// </summary>
        Size                    = 1 << 2,
        /// <summary>
        /// SHA1による比較(対応したデータベースが必須、データがない場合は常にスキップされない)
        /// </summary>
        FileContentsSHA1        = 1 << 3,
    }

    [DataContract]
    [KnownType(typeof(BackupSettings))]
    [KnownType(typeof(DataProtectionScope))]
    [KnownType(typeof(ComparisonMethod))]
    [KnownType(typeof(IDataContractSerializable))]
    public class BackupSettings : IDataContractSerializable
    {
        [DataMember]
        public bool isUseDatabase;
        [DataMember]
        public bool isCopyAttributes;
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


        public List<Regex> Regices { get; private set; }
        public string SaveFileName => nameof(BackupSettings);

        public string IgnorePattern { get => ignorePattern; set { ignorePattern = value; UpdateRegices(); } }

        public BackupSettings()
        {
            isUseDatabase = true;
            isCopyAttributes = true;
            isRecordPassword = true;
            passwordProtectionScope = DataProtectionScope.LocalMachine;
            protectedPassword = null;
            retryCount = 10;
            retryWaitMilliSec = 10000;
            comparisonMethod = ComparisonMethod.WriteTime;
            ignorePattern = string.Empty;
        }

        public override string ToString() => 
            $"データベースを利用する-----------\t: {isUseDatabase}\n" +
            $"属性をコピーする-----------------\t: {isCopyAttributes}\n" +
            $"パスワードを記録する-------------\t: {isRecordPassword}\n" +
            $"記録したパスワードのスコープ-----\t: {passwordProtectionScope}\n" +
            $"リトライ回数---------------------\t: {retryCount}\n" +
            $"リトライ待機時間(ミリ秒)---------\t: {retryWaitMilliSec}\n" +
            $"ファイル比較方法-----------------\t: {comparisonMethod}\n" +
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
            Regices = new List<Regex>(patStrArr.Select(s => ShapePattern(s)));
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
        /// 新しくバックアップされたファイルのパス。
        /// </summary>
        public List<string> backedupFileList;

        /// <summary>
        /// バックアップ対象だが失敗したファイルのパス。
        /// </summary>
        public List<string> failedFileList;

        public BackupResults(bool isSuccess, bool isFinished = false, string message = "")
        {
            this.isSuccess = isSuccess;
            this.IsFinished = isFinished;
            this.Message = message;
            backedupFileList = new List<string>();
            failedFileList = new List<string>();
        }
    }

    internal class DirectoryBackup
    {
        public BackupResults Results { get; private set; } = new BackupResults(false);
        public OpensslCompatibleAesCrypter AesCrypter { get; set; }
        public BackupSettings Settings { get; set; }
        public BackupDatabase Database { get; private set; }

        private static Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly HashAlgorithm sha1Provider = new SHA1CryptoServiceProvider();
        private string originBaseDirPath;
        private string destBaseDirPath;
        private int currentRetryCount;

        public DirectoryBackup(string originPath, string destPath, string password = "", BackupSettings settings = null)
        {
            originBaseDirPath = originPath;
            destBaseDirPath = destPath;
            if (password != "")
            {
                AesCrypter = new OpensslCompatibleAesCrypter(password);
            }
            Settings = settings ?? BackupSettings.InitOrLoadGlobalSettings();
            if (Settings.isUseDatabase)
            {
                InitOrLoadDatabase();
            }
            Results.Finished += Results_Finished;
        }

        private void InitOrLoadDatabase()
        {
            bool isExists = File.Exists(DataContractWriter.GetPath<BackupDatabase>(destBaseDirPath));
            logger.Info(Results.Message = isExists ? "既存のデータベースをロード" : "新規データベースを初期化");
            Database = isExists
                ? DataContractWriter.Read<BackupDatabase>(destBaseDirPath)
                : new BackupDatabase(originBaseDirPath, destBaseDirPath);
        }

        private void Results_Finished(object sender, EventArgs e)
        {
            if (Settings.isUseDatabase)
            {
                logger.Info(Results.Message = $"データベースを保存: {DataContractWriter.GetPath(Database)}");
                DataContractWriter.Write(Database);
            }
            Results.Message = (Results.isSuccess ? "バックアップ完了: " : Results.Message + "\nバックアップ失敗: ") + DateTime.Now;
            logger.Info("{0}\n________________________________________\n\n", Results.isSuccess ? "バックアップ完了" : "バックアップ失敗");
        }

        public BackupResults StartBackup()
        {
            logger.Info("バックアップ設定:\n{0}", Settings);
            logger.Info("バックアップを開始'{0}' => '{1}'", originBaseDirPath, destBaseDirPath);
            currentRetryCount = 0;
            if (!Directory.Exists(originBaseDirPath))
            {
                logger.Error(Results.Message = $"バックアップ元のディレクトリ'{originBaseDirPath}'が見つかりません。");
                Results.IsFinished = true;
                return Results;
            }
            
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
            }

            foreach (string originFilePath in Directory.EnumerateFiles(originBaseDirPath, "*", SearchOption.AllDirectories))
            {
                if (Settings.Regices != null)
                {
                    bool isIgnore = false;
                    foreach (var reg in Settings.Regices)
                    {
                        if (reg.IsMatch(originFilePath.Substring(originBaseDirPath.Length)))
                        {
                            logger.Info("除外パターン '{0}' に一致 : '{1}'", reg.ToString(), originFilePath);
                            isIgnore = true;
                            break;
                        }
                    }
                    if (isIgnore) continue;
                }
                string destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                FileBackup(originFilePath, destFilePath);
            }

            Results.isSuccess = Results.failedFileList.Count == 0;

            // リトライ処理
            if (Results.failedFileList.Count != 0 && Settings.retryCount > 0)
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

        private void FileBackup(string originFilePath, string destFilePath)
        {
            switch (Settings.comparisonMethod)
            {
                case ComparisonMethod.NoComparison:
                    break;

                case ComparisonMethod.ArchiveAttribute:
                    if ((File.GetAttributes(originFilePath) & FileAttributes.Archive) == FileAttributes.Archive)
                    {
                        break;
                    }
                    else
                    {
                        // Archive属性のないファイルはスキップする
                        logger.Info($"Archive属性のないファイルをスキップ: '{originFilePath}'");
                        return;
                    }
                case ComparisonMethod.WriteTime:
                    if (File.Exists(destFilePath) && File.GetLastWriteTime(originFilePath) == File.GetLastWriteTime(destFilePath))
                    {
                        logger.Info($"更新日時の同じファイルをスキップ: '{originFilePath}'");
                        return;
                    }
                    else
                    {
                        // 本当はここでサイズの比較もしたかったけど暗号化したら若干サイズが変わってしまうので……AESのCBCモードだとパディングのせいで毎回増加量も変動するから面倒だし、やっぱデータベースが必要か
                        break;
                    }
                case ComparisonMethod.FileContentsSHA1:
                    if (!File.Exists(destFilePath)) { break; }
                    else
                    {
                        throw new NotImplementedException("これ前回バックアップ時のハッシュ値を控えておいて比較するか、もしくは複合しないとあかんので後回し。");
                    }
            }
            logger.Info(Results.Message = $"ファイルをバックアップ: '{originFilePath}' => '{destFilePath}'");
            if (AesCrypter != null)
            {
                try
                {
                    if (AesCrypter.EncryptFile(originFilePath, destFilePath))
                    {
                        if (Settings.isCopyAttributes)
                        {
                            logger.Info($"属性をコピー: '{originFilePath}'");
                            var attributes = File.GetAttributes(originFilePath);
                            //if ((attributes & FileAttributes.Compressed) == FileAttributes.Compressed)
                            //{
                            //    attributes = RemoveAttribute(attributes, FileAttributes.Compressed);
                            //}
                            if (attributes.HasFlag(FileAttributes.ReadOnly))
                            {
                                attributes = RemoveAttribute(attributes, FileAttributes.ReadOnly);
                            }
                            if (Settings.comparisonMethod == ComparisonMethod.ArchiveAttribute && ((attributes & FileAttributes.Archive) == FileAttributes.Archive))
                            {
                                attributes = RemoveAttribute(attributes, FileAttributes.Archive);
                            }
                            File.SetAttributes(destFilePath, attributes);

                            File.SetCreationTime(destFilePath, File.GetCreationTime(originFilePath));
                            File.SetLastWriteTime(destFilePath, File.GetLastWriteTime(originFilePath));
                        }
                        logger.Info("成功!");
                        Results.backedupFileList.Add(originFilePath);
                        if (Results.failedFileList.Contains(originFilePath))
                        {
                            Results.failedFileList.Remove(originFilePath);
                        }
                    }
                    else
                    {
                        logger.Error(Results.Message = $"暗号化失敗: {AesCrypter.Error}");
                        if (!Results.failedFileList.Contains(originFilePath))
                            Results.failedFileList.Add(originFilePath);
                    }
                }
                catch (Exception e)
                {
                    logger.Error(Results.Message = $"失敗: {e}");
                    if (!Results.failedFileList.Contains(originFilePath))
                        Results.failedFileList.Add(originFilePath);
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
            foreach (string originFilePath in Results.failedFileList.ToArray())
            {
                string destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                FileBackup(originFilePath, destFilePath);
            }
            Results.isSuccess = Results.failedFileList.Count == 0;
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

        public static string ComputeSHA1(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ComputeSHA1(fs);
        }

        public static string ComputeSHA1(Stream stream)
        {
            var bs = sha1Provider.ComputeHash(stream);
            return BitConverter.ToString(bs).ToLower().Replace("-", "");
        }
    }
}