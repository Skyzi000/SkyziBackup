using NLog;
using Skyzi000;
using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SkyziBackup
{
    public class RestoreDirectory
    {
        public BackupResults Results { get; private set; } = new BackupResults(false);
        public OpensslCompatibleAesCrypter AesCrypter { get; set; }
        public BackupSettings Settings { get; set; }
        public BackupDatabase Database { get; private set; } = null;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string originBaseDirPath;
        private readonly string destBaseDirPath;
        private readonly bool isCopyAttributesOnDatabase = false;
        private readonly bool isCopyOnlyFileAttributes = false;
        public RestoreDirectory(string sourceDirPath, string destDirPath, string password = null, BackupSettings settings = null, bool isCopyAttributesOnDatabase = false, bool isCopyOnlyFileAttributes = false)
        {
            this.originBaseDirPath = Path.TrimEndingDirectorySeparator(sourceDirPath);
            this.destBaseDirPath = Path.TrimEndingDirectorySeparator(destDirPath);
            Settings = settings ?? BackupSettings.LoadLocalSettingsOrNull(destBaseDirPath, originBaseDirPath) ?? BackupSettings.GetGlobalSettings();
            //if (Settings.isUseDatabase && Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            // TODO: データベースに記録されたSHA1と比較できるようにする
            if (isCopyAttributesOnDatabase && File.Exists(DataContractWriter.GetDatabasePath(destBaseDirPath, originBaseDirPath)))
            {
                try
                {
                    Database = DataContractWriter.Read<BackupDatabase>(DataContractWriter.GetDatabaseFileName(destBaseDirPath, originBaseDirPath));
                    this.isCopyAttributesOnDatabase = isCopyAttributesOnDatabase;
                }
                catch (Exception) { }
            }
            if (!string.IsNullOrEmpty(password))
            {
                AesCrypter = new OpensslCompatibleAesCrypter(password, compressionLevel: Settings.compressionLevel, compressAlgorithm: Settings.compressAlgorithm);
            }
            Results.Finished += Results_Finished;
            this.isCopyOnlyFileAttributes = isCopyOnlyFileAttributes;
        }
        public BackupResults StartRestore()
        {
            if (Results.IsFinished)
            {
                throw new NotImplementedException("現在、このクラスのインスタンスは再利用されることを想定していません。");
            }
            Logger.Info("バックアップ設定:\n{0}", Settings);
            Logger.Info("リストアを開始'{0}' => '{1}'", originBaseDirPath, destBaseDirPath);

            Results.successfulFiles = new HashSet<string>();
            Results.failedFiles = new HashSet<string>();

            if (isCopyOnlyFileAttributes)
                return CopyOnlyFileAttributes();

            if (!Directory.Exists(originBaseDirPath) || (Directory.Exists(destBaseDirPath) && !Directory.EnumerateFileSystemEntries(destBaseDirPath).Any()))
            {
                Logger.Error(Results.Message = !Directory.Exists(originBaseDirPath)
                    ? $"リストアを中止: リストア元のディレクトリ'{originBaseDirPath}'が見つかりません。"
                    : $"リストアを中止: リストア先のディレクトリ'{destBaseDirPath}'が空ではありません。");
                Results.IsFinished = true;
                return Results;
            }

            BackupDirectory.CopyDirectoryStructure(originBaseDirPath, destBaseDirPath, Settings.isCopyAttributes);

            foreach (string originFilePath in Directory.EnumerateFiles(originBaseDirPath, "*", SearchOption.AllDirectories))
            {
                string destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                RestoreFile(originFilePath, destFilePath);
            }
            return Results;
        }

        private void RestoreFile(string originFilePath, string destFilePath)
        {
            Logger.Info(Results.Message = $"ファイルをリストア: '{originFilePath}' => '{destFilePath}'");
            try
            {
                if (AesCrypter != null)
                {

                    if (AesCrypter.DecryptFile(originFilePath, destFilePath))
                    {
                        // 復号成功時処理
                        if (Settings.isCopyAttributes)
                        {
                            CopyFileAttributes(originFilePath, destFilePath);
                        }
                        Results.successfulFiles.Add(originFilePath);
                        Results.failedFiles.Remove(originFilePath);
                    }
                    else
                    {
                        Logger.Error(AesCrypter.Error, Results.Message = $"復号に失敗しました '{originFilePath}' => '{destFilePath}'\n");
                        Results.failedFiles.Add(originFilePath);
                    }
                }
                else
                {
                    // 暗号化されていないファイルの復元
                    using FileStream origin = new FileStream(originFilePath, FileMode.Open, FileAccess.Read);
                    using FileStream dest = new FileStream(destFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                    origin.CopyWithCompressionTo(dest, Settings.compressionLevel, CompressionMode.Decompress, Settings.compressAlgorithm);
                    if (Settings.isCopyAttributes)
                    {
                        CopyFileAttributes(originFilePath, destFilePath);
                    }
                    Results.successfulFiles.Add(originFilePath);
                    Results.failedFiles.Remove(originFilePath);
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

        private void CopyFileAttributes(string originFilePath, string destFilePath)
        {
            FileInfo originInfo = null;
            Logger.Debug($"属性をコピー");
            if (!isCopyAttributesOnDatabase || !Database.backedUpFilesDict.TryGetValue(originFilePath, out var data))
            {
                new FileInfo(destFilePath)
                {
                    CreationTime = (originInfo = new FileInfo(originFilePath)).CreationTime,
                    LastWriteTime = originInfo.LastWriteTime,
                    Attributes = originInfo.Attributes
                };
            }
            // データベースに記録されたファイル属性をコピーする(記録されていないものがあれば実際のファイルを参照する)
            else
            {
                new FileInfo(destFilePath)
                {
                    CreationTime = data.creationTime ?? (originInfo = new FileInfo(originFilePath)).CreationTime,
                    LastWriteTime = data.lastWriteTime ?? (originInfo ??= new FileInfo(originFilePath)).LastWriteTime,
                    Attributes = data.fileAttributes ?? (originInfo ?? new FileInfo(originFilePath)).Attributes
                };
            }
        }

        private BackupResults CopyOnlyFileAttributes()
        {
            if (!isCopyAttributesOnDatabase && !Directory.Exists(originBaseDirPath))
            {
                Logger.Error(Results.Message = $"リストアを中止: リストア元のディレクトリ'{originBaseDirPath}'が見つかりません。");
                Results.IsFinished = true;
                return Results;
            }
            if (isCopyAttributesOnDatabase)
            {
                foreach (string originDirPath in Database.backedUpDirectoriesDict.Keys)
                {
                    string destDirPath = originDirPath.Replace(originBaseDirPath, destBaseDirPath);
                    DirectoryInfo originInfo = null;
                    BackedUpDirectoryData data = Database.backedUpDirectoriesDict[originDirPath];
                    try
                    {
                        // データベースに記録されたディレクトリ属性をコピーする(もし記録されていないものがあれば実際のディレクトリを参照する)
                        new DirectoryInfo(destDirPath)
                        {
                            CreationTime = data.creationTime ?? (originInfo = new DirectoryInfo(originDirPath)).CreationTime,
                            LastWriteTime = data.lastWriteTime ?? (originInfo ??= new DirectoryInfo(originDirPath)).LastWriteTime,
                            Attributes = data.fileAttributes ?? (originInfo ?? new DirectoryInfo(originDirPath)).Attributes
                        };
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Logger.Error(ex, Results.Message = $"アクセスが拒否されました '{originDirPath}' => '{destDirPath}'\n");
                        Results.failedFiles.Add(originDirPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originDirPath}' => '{destDirPath}'\n");
                        Results.failedFiles.Add(originDirPath);
                    }
                }
            }
            else
                BackupDirectory.CopyDirectoryStructure(originBaseDirPath, destBaseDirPath, true);
            foreach (string originFilePath in Directory.EnumerateFiles(originBaseDirPath, "*", SearchOption.AllDirectories))
            {
                string destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                try
                {
                    if (Settings.isCopyAttributes)
                    {
                        CopyFileAttributes(originFilePath, destFilePath);
                    }
                    Results.successfulFiles.Add(originFilePath);
                    Results.failedFiles.Remove(originFilePath);
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
            return Results;
        }
        private void Results_Finished(object sender, EventArgs e)
        {
            Results.Message = (Results.isSuccess ? "リストア完了: " : Results.Message + "\nリストア失敗: ") + DateTime.Now;
            Logger.Info("{0}\n=============================\n\n", Results.isSuccess ? "リストア完了" : "リストア失敗");
        }
    }
}