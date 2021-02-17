using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Timers;
using NLog;

namespace SkyziBackup
{
    public enum ComparisonMethod
    {
        NoComparison,
        ArchiveAttribute,
        WriteTime,
        FileContentsSHA1,
    }
    public class BackupSettings
    {
        public bool copyAttributes = true;
        public int retryCount = 10;
        public int retryWaitMilliSec = 10000;
        public ComparisonMethod comparisonMethod = ComparisonMethod.WriteTime;
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
        public BackupResults Results { get; private set; }
        public OpensslCompatibleAesCrypter AesCrypter { get; set; }
        public BackupSettings Settings { get; set; }

        private static Logger logger = LogManager.GetCurrentClassLogger();
        private string _originPath;
        private string _destPath;
        private int currentRetryCount;

        public DirectoryBackup(string originPath, string destPath, string password = "")
        {
            _originPath = originPath;
            _destPath = destPath;
            if (password != "")
            {
                AesCrypter = new OpensslCompatibleAesCrypter(password);
            }
            Settings = new BackupSettings();
            Results = new BackupResults(false);
            Results.Finished += Results_Finished;
        }

        private void Results_Finished(object sender, EventArgs e)
        {
            Results.Message = Results.isSuccess ? "バックアップ完了" : "バックアップ失敗";
            logger.Info(Results.Message);
        }

        public BackupResults StartBackup()
        {
            logger.Info("バックアップを開始'{0}' => '{1}'", _originPath, _destPath);
            currentRetryCount = 0;
            if (!Directory.Exists(_originPath))
            {
                Results.Message = $"バックアップ元のディレクトリ'{_originPath}'が見つかりません。";
                logger.Error(Results.Message);
                Results.IsFinished = true;
                return Results; 
            }
            Results.Message = "バックアップ中...";
            foreach (string originDirPath in Directory.EnumerateDirectories(_originPath, "*", SearchOption.AllDirectories))
            {
                string destDirPath = originDirPath.Replace(_originPath, _destPath);
                logger.Info($"存在しなければ作成: '{destDirPath}'");
                var dir = Directory.CreateDirectory(destDirPath);
                if (Settings.copyAttributes)
                {
                    logger.Info($"属性をコピー: '{originDirPath}'");
                    dir.CreationTime = Directory.GetCreationTime(originDirPath);
                    dir.LastWriteTime = Directory.GetLastWriteTime(originDirPath);
                }
            }

            foreach (string originFilePath in Directory.EnumerateFiles(_originPath, "*", SearchOption.AllDirectories))
            {
                string destFilePath = originFilePath.Replace(_originPath, _destPath);
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
                    if(File.Exists(destFilePath) && File.GetLastWriteTime(originFilePath) == File.GetLastWriteTime(destFilePath))
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
            logger.Info($"バックアップ開始 '{originFilePath}' => '{destFilePath}'");
            if (AesCrypter != null)
            {
                try
                {
                    if (AesCrypter.EncryptFile(originFilePath, destFilePath))
                    {
                        if (Settings.copyAttributes)
                        {
                            logger.Info($"属性をコピー: '{originFilePath}'");
                            var attributes = File.GetAttributes(originFilePath);
                            //if ((attributes & FileAttributes.Compressed) == FileAttributes.Compressed)
                            //{
                            //    attributes = RemoveAttribute(attributes, FileAttributes.Compressed);
                            //}
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
                        logger.Error($"暗号化失敗: {AesCrypter.Error}");
                        if (!Results.failedFileList.Contains(originFilePath))
                            Results.failedFileList.Add(originFilePath);
                    }
                }
                catch (Exception e)
                {
                    logger.Error($"失敗: {e}");
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
            var retryTimer = new Timer(Settings.retryWaitMilliSec);
            retryTimer.AutoReset = false;
            retryTimer.Elapsed += (sender, e) =>
            {
                logger.Debug(e.SignalTime);
                currentRetryCount++;
                //Results.Message = $"リトライ {currentRetryCount}/{Settings.retryCount} 回目";
                //logger.Info(Results.Message);
                foreach (string originFilePath in Results.failedFileList)
                {
                    string destFilePath = originFilePath.Replace(_originPath, _destPath);
                    FileBackup(originFilePath, destFilePath);
                }
                Results.isSuccess = Results.failedFileList.Count == 0;
                if (Results.isSuccess || currentRetryCount >= Settings.retryCount)
                {
                    Results.IsFinished = true;
                }
                else
                {
                    //Results.Message = $"リトライ待機中...({currentRetryCount + 1}/{Settings.retryCount}回目)";
                    RetryStart();
                    //retryTimer.Start();
                    //Results.Message = $"リトライ待機中...({currentRetryCount + 1}/{Settings.retryCount}回目)";
                    //logger.Debug(Results.Message);
                }
            };
            retryTimer.Enabled = true;
            Results.Message = $"リトライ待機中...({currentRetryCount+1}/{Settings.retryCount}回目)";
            logger.Debug(Results.Message);
        }

        private static FileAttributes RemoveAttribute(FileAttributes attributes, FileAttributes attributesToRemove)
        {
            return attributes & ~attributesToRemove;
        }
    }
}