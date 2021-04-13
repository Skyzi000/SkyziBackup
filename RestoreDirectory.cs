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
        private readonly string sourceBaseDirPath;
        private readonly string destBaseDirPath;
        private readonly bool isRestoreAttributesFromDatabase = false;
        private readonly bool isCopyOnlyFileAttributes = false;
        private readonly bool isEnableWriteDatabase = false;
        public RestoreDirectory(string sourceDirPath,
                                string destDirPath,
                                string password = null,
                                BackupSettings settings = null,
                                bool isCopyAttributesOnDatabase = false,
                                bool isCopyOnlyFileAttributes = false,
                                bool isEnableWriteDatabase = false)
        {
            this.sourceBaseDirPath = Path.TrimEndingDirectorySeparator(sourceDirPath);
            this.destBaseDirPath = Path.TrimEndingDirectorySeparator(destDirPath);
            Settings = settings ?? BackupSettings.LoadLocalSettingsOrNull(destBaseDirPath, sourceBaseDirPath) ?? BackupSettings.GetGlobalSettings();
            //if (Settings.isUseDatabase && Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            // TODO: データベースに記録されたSHA1と比較できるようにする
            if (isCopyAttributesOnDatabase && File.Exists(DataContractWriter.GetDatabasePath(destBaseDirPath, sourceBaseDirPath)))
            {
                try
                {
                    Database = DataContractWriter.Read<BackupDatabase>(DataContractWriter.GetDatabaseFileName(destBaseDirPath, sourceBaseDirPath));
                }
                catch (Exception) { }
            }
            this.isRestoreAttributesFromDatabase = isCopyAttributesOnDatabase;
            this.isEnableWriteDatabase = isEnableWriteDatabase;
            if (this.isEnableWriteDatabase && Database == null)
            {
                Database = File.Exists(DataContractWriter.GetDatabasePath(destBaseDirPath, sourceBaseDirPath))
                    ? DataContractWriter.Read<BackupDatabase>(DataContractWriter.GetDatabaseFileName(destBaseDirPath, sourceBaseDirPath))
                    : new BackupDatabase(destBaseDirPath, sourceBaseDirPath);
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
            Logger.Info(@"リストア設定:
データベースからファイル属性をリストアする: {0}
ファイル属性だけをコピーする: {1}
ファイル属性をデータベースに保存する: {2}",
                isRestoreAttributesFromDatabase, isCopyOnlyFileAttributes, isEnableWriteDatabase);
            Logger.Info("リストアを開始'{0}' => '{1}'", sourceBaseDirPath, destBaseDirPath);

            Results.successfulFiles = new HashSet<string>();
            Results.successfulDirectories = new HashSet<string>();
            Results.failedFiles = new HashSet<string>();
            Results.failedDirectories = new HashSet<string>();

            if (isCopyOnlyFileAttributes)
            {
                return CopyOnlyFileAttributes();
            }

            if (!Directory.Exists(sourceBaseDirPath) || (Directory.Exists(destBaseDirPath) && Directory.EnumerateFileSystemEntries(destBaseDirPath).Any()))
            {
                Logger.Error(Results.Message = !Directory.Exists(sourceBaseDirPath)
                    ? $"リストアを中止: リストア元のディレクトリ'{sourceBaseDirPath}'が見つかりません。"
                    : $"リストアを中止: リストア先のディレクトリ'{destBaseDirPath}'が空ではありません。");
                Results.IsFinished = true;
                return Results;
            }
            if (isEnableWriteDatabase)
                Database.backedUpDirectoriesDict = BackupController.CopyDirectoryStructure(sourceBaseDirPath: sourceBaseDirPath,
                                                                                           destBaseDirPath: destBaseDirPath,
                                                                                           results: Results,
                                                                                           isCopyAttributes: Settings.isCopyAttributes,
                                                                                           regices: null,
                                                                                           backedUpDirectoriesDict: Database.backedUpDirectoriesDict,
                                                                                           isForceCreateDirectoryAndReturnDictionary: true,
                                                                                           isRestoreAttributesFromDatabase: isRestoreAttributesFromDatabase);
            else
                BackupController.CopyDirectoryStructure(sourceBaseDirPath: sourceBaseDirPath,
                                                        destBaseDirPath: destBaseDirPath,
                                                        results: Results,
                                                        isCopyAttributes: Settings.isCopyAttributes,
                                                        backedUpDirectoriesDict: Database?.backedUpDirectoriesDict,
                                                        isRestoreAttributesFromDatabase: isRestoreAttributesFromDatabase);

            foreach (string originFilePath in Directory.EnumerateFiles(sourceBaseDirPath, "*", SearchOption.AllDirectories))
            {
                string destFilePath = originFilePath.Replace(sourceBaseDirPath, destBaseDirPath);
                RestoreFile(originFilePath, destFilePath);
            }
            Results.isSuccess = !Results.failedFiles.Any() && !Results.failedDirectories.Any();
            Results.IsFinished = true;
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
                            if(isEnableWriteDatabase)
                                Database.backedUpFilesDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath);
                            else
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
                    using (FileStream origin = new FileStream(originFilePath, FileMode.Open, FileAccess.Read))
                    {
                        using (FileStream dest = new FileStream(destFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                        {
                            origin.CopyWithCompressionTo(dest, Settings.compressionLevel, CompressionMode.Decompress, Settings.compressAlgorithm);
                        }
                    }
                    if (Settings.isCopyAttributes)
                    {
                        if (isEnableWriteDatabase)
                            Database.backedUpFilesDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath);
                        else
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

        private BackedUpFileData CopyFileAttributes(string originFilePath, string destFilePath)
        {
            FileInfo originInfo = null;
            if (isEnableWriteDatabase)
            {
                Logger.Info("属性をコピー'{0}' => '{1}'", originFilePath, destFilePath);
                var data = Database.backedUpFilesDict.TryGetValue(originFilePath, out var d) ? d : new BackedUpFileData();
                new FileInfo(destFilePath)
                {
                    CreationTime = (data.creationTime = (originInfo = new FileInfo(originFilePath)).CreationTime).Value,
                    LastWriteTime = (data.lastWriteTime = originInfo.LastWriteTime).Value,
                    Attributes = (data.FileAttributes = originInfo.Attributes).Value
                };
                return data;
            }
            else if (!isRestoreAttributesFromDatabase || !Database.backedUpFilesDict.TryGetValue(originFilePath, out var data))
            {
                Logger.Info("属性をコピー'{0}' => '{1}'", originFilePath, destFilePath);
                new FileInfo(destFilePath)
                {
                    CreationTime = (originInfo = new FileInfo(originFilePath)).CreationTime,
                    LastWriteTime = originInfo.LastWriteTime,
                    Attributes = originInfo.Attributes
                };
            }
            // データベースに記録されたファイル属性をリストアする(もし記録されていないものがあれば実際のファイルを参照する)
            else
            {
                Logger.Info("データベースからファイル属性をリストア '{0}'", destFilePath);
                new FileInfo(destFilePath)
                {
                    CreationTime = data.creationTime ?? (originInfo = new FileInfo(originFilePath)).CreationTime,
                    LastWriteTime = data.lastWriteTime ?? (originInfo ??= new FileInfo(originFilePath)).LastWriteTime,
                    Attributes = data.FileAttributes ?? (originInfo ?? new FileInfo(originFilePath)).Attributes
                };
            }
            return null;
        }

        private BackupResults CopyOnlyFileAttributes()
        {
            if (!Settings.isCopyAttributes)
            {
                Logger.Warn(Results.Message = $"リストアを中止: ファイル属性をコピーしない設定になっています。");
                Results.IsFinished = true;
                return Results;
            }
            if (!isRestoreAttributesFromDatabase && !Directory.Exists(sourceBaseDirPath))
            {
                Logger.Error(Results.Message = $"リストアを中止: リストア元のディレクトリ'{sourceBaseDirPath}'が見つかりません。");
                Results.IsFinished = true;
                return Results;
            }
            if (isRestoreAttributesFromDatabase)
            {
                var newDirDict = isEnableWriteDatabase ? new Dictionary<string, BackedUpDirectoryData>() : null;
                var newFileDict = isEnableWriteDatabase ? new Dictionary<string, BackedUpFileData>() : null;
                foreach (string originDirPath in Database.backedUpDirectoriesDict.Keys)
                {
                    string destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    if (!Directory.Exists(destDirPath))
                    {
                        Logger.Warn("コピー先のディレクトリが見つかりません: {0}", destDirPath);
                        Results.failedFiles.Add(originDirPath);
                        continue;
                    }
                    DirectoryInfo originInfo = null;
                    try
                    {
                        if (isEnableWriteDatabase)
                        {
                            var data = Database.backedUpDirectoriesDict.TryGetValue(originDirPath, out var d) ? d : new BackedUpDirectoryData();
                            new DirectoryInfo(destDirPath)
                            {
                                CreationTime = (data.creationTime = (originInfo = new DirectoryInfo(originDirPath)).CreationTime).Value,
                                LastWriteTime = (data.lastWriteTime = originInfo.LastWriteTime).Value,
                                Attributes = (data.FileAttributes = originInfo.Attributes).Value
                            };
                            newDirDict[originDirPath] = data;
                        }
                        else
                        {
                            // データベースに記録されたディレクトリ属性をコピーする(もし記録されていないものがあれば実際のディレクトリを参照する)
                            BackedUpDirectoryData data = Database.backedUpDirectoriesDict[originDirPath];
                            new DirectoryInfo(destDirPath)
                            {
                                CreationTime = data.creationTime ?? (originInfo = new DirectoryInfo(originDirPath)).CreationTime,
                                LastWriteTime = data.lastWriteTime ?? (originInfo ??= new DirectoryInfo(originDirPath)).LastWriteTime,
                                Attributes = data.FileAttributes ?? (originInfo ?? new DirectoryInfo(originDirPath)).Attributes
                            };
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originDirPath}' => '{destDirPath}'");
                        Results.failedFiles.Add(originDirPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originDirPath}' => '{destDirPath}'\n");
                        Results.failedFiles.Add(originDirPath);
                    }
                }
                foreach (string originFilePath in Database.backedUpFilesDict.Keys)
                {
                    string destFilePath = originFilePath.Replace(sourceBaseDirPath, destBaseDirPath);
                    if (!File.Exists(destFilePath))
                    {
                        Logger.Warn(Results.Message = $"コピー先のファイルが見つかりません '{destFilePath}'");
                        Results.failedFiles.Add(originFilePath);
                        continue;
                    }
                    try
                    {
                        if (isEnableWriteDatabase)
                            newFileDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath);
                        else
                            CopyFileAttributes(originFilePath, destFilePath);
                        Results.successfulFiles.Add(originFilePath);
                        Results.failedFiles.Remove(originFilePath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originFilePath}' => '{destFilePath}'\n");
                        Results.failedFiles.Add(originFilePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originFilePath}' => '{destFilePath}'\n");
                        Results.failedFiles.Add(originFilePath);
                    }
                }
                if (isEnableWriteDatabase)
                {
                    Database.backedUpDirectoriesDict = newDirDict;
                    Database.backedUpFilesDict = newFileDict;
                }
            }
            else
            {
                foreach (string originDirPath in Directory.EnumerateDirectories(sourceBaseDirPath, "*", SearchOption.AllDirectories))
                {
                    string destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    if (!Directory.Exists(destDirPath))
                    {
                        Logger.Warn("コピー先のディレクトリが見つかりません: {0}", destDirPath);
                        Results.failedFiles.Add(originDirPath);
                        continue;
                    }
                    try
                    {
                        DirectoryInfo originInfo;
                        if (isEnableWriteDatabase)
                        {
                            var data = Database.backedUpDirectoriesDict.TryGetValue(originDirPath, out var d) ? d : new BackedUpDirectoryData();
                            new DirectoryInfo(destDirPath)
                            {
                                CreationTime = (data.creationTime = (originInfo = new DirectoryInfo(originDirPath)).CreationTime).Value,
                                LastWriteTime = (data.lastWriteTime = originInfo.LastWriteTime).Value,
                                Attributes = (data.FileAttributes = originInfo.Attributes).Value
                            };
                            Database.backedUpDirectoriesDict[originDirPath] = data;
                        }
                        else
                        {
                            new DirectoryInfo(destDirPath)
                            {
                                CreationTime = (originInfo = new DirectoryInfo(originDirPath)).CreationTime,
                                LastWriteTime = originInfo.LastWriteTime,
                                Attributes = originInfo.Attributes
                            };
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originDirPath}' => '{destDirPath}'\n");
                        Results.failedFiles.Add(originDirPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originDirPath}' => '{destDirPath}'\n");
                        Results.failedFiles.Add(originDirPath);
                    }
                }
                foreach (string originFilePath in Directory.EnumerateFiles(sourceBaseDirPath, "*", SearchOption.AllDirectories))
                {
                    string destFilePath = originFilePath.Replace(sourceBaseDirPath, destBaseDirPath);
                    if (!File.Exists(destFilePath))
                    {
                        Logger.Warn("コピー先のファイルが見つかりません: '{0}'", destFilePath);
                        Results.failedFiles.Add(originFilePath);
                        continue;
                    }
                    try
                    {
                        if (isEnableWriteDatabase)
                            Database.backedUpFilesDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath);
                        else
                            CopyFileAttributes(originFilePath, destFilePath);
                        Results.successfulFiles.Add(originFilePath);
                        Results.failedFiles.Remove(originFilePath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originFilePath}' => '{destFilePath}'\n");
                        Results.failedFiles.Add(originFilePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originFilePath}' => '{destFilePath}'\n");
                        Results.failedFiles.Add(originFilePath);
                    }
                }
            }
            Results.isSuccess = !Results.failedFiles.Any();
            Results.IsFinished = true;
            return Results;
        }
        private void Results_Finished(object sender, EventArgs e)
        {
            if (isEnableWriteDatabase)
            {
                Logger.Info("データベースを保存: '{0}'", DataContractWriter.GetPath(Database));
                DataContractWriter.Write(Database);
            }
            Results.Message = (Results.isSuccess ? "リストア完了: " : Results.Message + "\nリストア失敗: ") + DateTime.Now;
            Logger.Info("{0}\n=============================\n\n", Results.isSuccess ? "リストア完了" : "リストア失敗");
        }
    }
}