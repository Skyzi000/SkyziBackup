using NLog;
using Skyzi000;
using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SkyziBackup
{
    public class RestoreController
    {
        public BackupResults Results { get; private set; } = new BackupResults(false);
        public CompressiveAesCryptor? AesCryptor { get; set; }
        public BackupSettings Settings { get; set; }
        public BackupDatabase? Database { get; private set; } = null;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string sourceBaseDirPath;
        private readonly string destBaseDirPath;
        private readonly bool isRestoreAttributesFromDatabase = false;
        private readonly bool isCopyOnlyFileAttributes = false;
        private readonly bool isEnableWriteDatabase = false;
        public RestoreController(string sourceDirPath,
                                string destDirPath,
                                string? password = null,
                                BackupSettings? settings = null,
                                bool isCopyAttributesOnDatabase = false,
                                bool isCopyOnlyFileAttributes = false,
                                bool isEnableWriteDatabase = false)
        {
            this.sourceBaseDirPath = BackupController.GetQualifiedDirectoryPath(sourceDirPath);
            this.destBaseDirPath = BackupController.GetQualifiedDirectoryPath(destDirPath);
            Settings = settings ?? BackupSettings.LoadLocalSettings(destBaseDirPath, sourceBaseDirPath) ?? BackupSettings.Default;
            //if (Settings.isUseDatabase && Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            // TODO: データベースに記録されたSHA1と比較できるようにする
            if (isCopyAttributesOnDatabase && File.Exists(DataFileWriter.GetDatabasePath(destBaseDirPath, sourceBaseDirPath)))
            {
                try
                {
                    Database = DataFileWriter.Read<BackupDatabase>(DataFileWriter.GetDatabaseFileName(destBaseDirPath, sourceBaseDirPath));
                }
                catch (Exception) { }
            }
            this.isRestoreAttributesFromDatabase = isCopyAttributesOnDatabase;
            this.isEnableWriteDatabase = isEnableWriteDatabase;
            if (this.isEnableWriteDatabase && Database == null)
            {
                Database = File.Exists(DataFileWriter.GetDatabasePath(destBaseDirPath, sourceBaseDirPath))
                    ? DataFileWriter.Read<BackupDatabase>(DataFileWriter.GetDatabaseFileName(destBaseDirPath, sourceBaseDirPath))
                    : new BackupDatabase(destBaseDirPath, sourceBaseDirPath);
            }
            if (!string.IsNullOrEmpty(password))
            {
                AesCryptor = new CompressiveAesCryptor(password, compressionLevel: Settings.CompressionLevel, compressAlgorithm: Settings.CompressAlgorithm);
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
            if (isEnableWriteDatabase && Database != null)
                Database.BackedUpDirectoriesDict = CopyDirectoryStructure(sourceBaseDirPath: sourceBaseDirPath,
                                                                                           destBaseDirPath: destBaseDirPath,
                                                                                           results: Results,
                                                                                           isCopyAttributes: Settings.IsCopyAttributes,
                                                                                           backedUpDirectoriesDict: Database.BackedUpDirectoriesDict,
                                                                                           isForceCreateDirectoryAndReturnDictionary: true,
                                                                                           isRestoreAttributesFromDatabase: isRestoreAttributesFromDatabase);
            else
                CopyDirectoryStructure(sourceBaseDirPath: sourceBaseDirPath,
                                                        destBaseDirPath: destBaseDirPath,
                                                        results: Results,
                                                        isCopyAttributes: Settings.IsCopyAttributes,
                                                        backedUpDirectoriesDict: Database?.BackedUpDirectoriesDict,
                                                        isRestoreAttributesFromDatabase: isRestoreAttributesFromDatabase);

            foreach (string originFilePath in BackupController.EnumerateAllFilesIgnoreReparsePoints(sourceBaseDirPath))
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
                if (AesCryptor != null)
                {
                    AesCryptor.DecryptFile(originFilePath, destFilePath);
                    // 復号成功時処理
                    if (Settings.IsCopyAttributes)
                    {
                        if (isEnableWriteDatabase && Database != null) // この条件ならCopyFileAttributesはnullを返さないはず
                            Database.BackedUpFilesDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath)!;
                        else
                            CopyFileAttributes(originFilePath, destFilePath);
                    }
                    Results.successfulFiles.Add(originFilePath);
                    Results.failedFiles.Remove(originFilePath);
                }
                else
                {
                    // 暗号化されていないファイルの復元
                    using (FileStream origin = new FileStream(originFilePath, FileMode.Open, FileAccess.Read))
                    {
                        using (FileStream dest = new FileStream(destFilePath, FileMode.Create, FileAccess.ReadWrite))
                        {
                            origin.CopyWithCompressionTo(dest, Settings.CompressionLevel, CompressionMode.Decompress, Settings.CompressAlgorithm);
                        }
                    }
                    if (Settings.IsCopyAttributes)
                    {
                        if (isEnableWriteDatabase && Database != null)
                            Database.BackedUpFilesDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath)!;
                        else
                            CopyFileAttributes(originFilePath, destFilePath);
                    }
                    Results.successfulFiles.Add(originFilePath);
                    Results.failedFiles.Remove(originFilePath);
                }
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

        private BackedUpFileData? CopyFileAttributes(string originFilePath, string destFilePath)
        {
            FileInfo? originInfo = null;
            if (isEnableWriteDatabase && Database != null)
            {
                Logger.Info("属性をコピー'{0}' => '{1}'", originFilePath, destFilePath);
                var data = Database.BackedUpFilesDict.TryGetValue(originFilePath, out var d) ? d : new BackedUpFileData();
                new FileInfo(destFilePath)
                {
                    CreationTime = (data.CreationTime = (originInfo = new FileInfo(originFilePath)).CreationTime).Value,
                    LastWriteTime = (data.LastWriteTime = originInfo.LastWriteTime).Value,
                    Attributes = (data.FileAttributes = originInfo.Attributes).Value
                };
                return data;
            }
            else if (!isRestoreAttributesFromDatabase || Database is null || !Database.BackedUpFilesDict.TryGetValue(originFilePath, out var data))
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
                    CreationTime = data.CreationTime ?? (originInfo = new FileInfo(originFilePath)).CreationTime,
                    LastWriteTime = data.LastWriteTime ?? (originInfo ??= new FileInfo(originFilePath)).LastWriteTime,
                    Attributes = data.FileAttributes ?? (originInfo ?? new FileInfo(originFilePath)).Attributes
                };
            }
            return null;
        }

        private BackupResults CopyOnlyFileAttributes()
        {
            if (!Settings.IsCopyAttributes)
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
            if (isRestoreAttributesFromDatabase && Database != null)
            {
                var newDirDict = isEnableWriteDatabase ? new Dictionary<string, BackedUpDirectoryData>() : null;
                var newFileDict = isEnableWriteDatabase ? new Dictionary<string, BackedUpFileData>() : null;
                foreach (string originDirPath in Database.BackedUpDirectoriesDict.Keys)
                {
                    string destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    if (!Directory.Exists(destDirPath))
                    {
                        Logger.Warn("コピー先のディレクトリが見つかりません: {0}", destDirPath);
                        Results.failedFiles.Add(originDirPath);
                        continue;
                    }
                    DirectoryInfo? originInfo = null;
                    try
                    {
                        if (isEnableWriteDatabase) // newDirDict は null ではない
                        {
                            var data = Database.BackedUpDirectoriesDict.TryGetValue(originDirPath, out var d) ? d : new BackedUpDirectoryData();
                            new DirectoryInfo(destDirPath)
                            {
                                CreationTime = (data.CreationTime = (originInfo = new DirectoryInfo(originDirPath)).CreationTime).Value,
                                LastWriteTime = (data.LastWriteTime = originInfo.LastWriteTime).Value,
                                Attributes = (data.FileAttributes = originInfo.Attributes).Value
                            };
                            newDirDict![originDirPath] = data;
                        }
                        else
                        {
                            // データベースに記録されたディレクトリ属性をコピーする(もし記録されていないものがあれば実際のディレクトリを参照する)
                            BackedUpDirectoryData data = Database.BackedUpDirectoriesDict[originDirPath];
                            new DirectoryInfo(destDirPath)
                            {
                                CreationTime = data.CreationTime ?? (originInfo = new DirectoryInfo(originDirPath)).CreationTime,
                                LastWriteTime = data.LastWriteTime ?? (originInfo ??= new DirectoryInfo(originDirPath)).LastWriteTime,
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
                foreach (string originFilePath in Database.BackedUpFilesDict.Keys)
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
                        if (isEnableWriteDatabase && Database != null) // newFileDict は null ではない
                            newFileDict![originFilePath] = CopyFileAttributes(originFilePath, destFilePath)!;
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
                if (isEnableWriteDatabase && newDirDict != null && newFileDict != null)
                {
                    Database.BackedUpDirectoriesDict = newDirDict;
                    Database.BackedUpFilesDict = newFileDict;
                }
            }
            else
            {
                foreach (string originDirPath in BackupController.EnumerateAllDirectoriesIgnoreReparsePoints(sourceBaseDirPath))
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
                        if (isEnableWriteDatabase && Database != null)
                        {
                            var data = Database.BackedUpDirectoriesDict.TryGetValue(originDirPath, out var d) ? d : new BackedUpDirectoryData();
                            new DirectoryInfo(destDirPath)
                            {
                                CreationTime = (data.CreationTime = (originInfo = new DirectoryInfo(originDirPath)).CreationTime).Value,
                                LastWriteTime = (data.LastWriteTime = originInfo.LastWriteTime).Value,
                                Attributes = (data.FileAttributes = originInfo.Attributes).Value
                            };
                            Database.BackedUpDirectoriesDict[originDirPath] = data;
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
                foreach (string originFilePath in BackupController.EnumerateAllFilesIgnoreReparsePoints(sourceBaseDirPath))
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
                        var data = CopyFileAttributes(originFilePath, destFilePath);
                        if (isEnableWriteDatabase && Database != null && data != null)
                            Database.BackedUpFilesDict[originFilePath] = data;
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

        [return: NotNullIfNotNull("backedUpDirectoriesDict")]
        public Dictionary<string, BackedUpDirectoryData>? CopyDirectoryStructure(string sourceBaseDirPath,
                                                                                 string destBaseDirPath,
                                                                                 BackupResults results,
                                                                                 bool isCopyAttributes = true,
                                                                                 Dictionary<string, BackedUpDirectoryData>? backedUpDirectoriesDict = null,
                                                                                 bool isForceCreateDirectoryAndReturnDictionary = false,
                                                                                 bool isRestoreAttributesFromDatabase = false,
                                                                                 SymbolicLinkHandling symbolicLink = SymbolicLinkHandling.IgnoreOnlyDirectories,
                                                                                 VersioningMethod versioning = VersioningMethod.PermanentDeletion)
        {
            if (string.IsNullOrEmpty(sourceBaseDirPath))
            {
                throw new ArgumentException($"'{nameof(sourceBaseDirPath)}' を null または空にすることはできません", nameof(sourceBaseDirPath));
            }

            if (string.IsNullOrEmpty(destBaseDirPath))
            {
                throw new ArgumentException($"'{nameof(destBaseDirPath)}' を null または空にすることはできません", nameof(destBaseDirPath));
            }

            if (isRestoreAttributesFromDatabase && backedUpDirectoriesDict == null)
            {
                throw new ArgumentNullException(nameof(backedUpDirectoriesDict));
            }
            Logger.Info(results.Message = $"ディレクトリ構造をコピー");
            foreach (string originDirPath in symbolicLink == SymbolicLinkHandling.IgnoreOnlyDirectories || symbolicLink == SymbolicLinkHandling.IgnoreAll
                ? BackupController.EnumerateAllDirectoriesIgnoreReparsePoints(sourceBaseDirPath)
                : BackupController.EnumerateAllDirectories(sourceBaseDirPath))
            {
                backedUpDirectoriesDict = CopyDirectory(originDirPath: originDirPath,
                                                        sourceBaseDirPath: sourceBaseDirPath,
                                                        destBaseDirPath: destBaseDirPath,
                                                        results: results,
                                                        isCopyAttributes: isCopyAttributes,
                                                        backedUpDirectoriesDict: backedUpDirectoriesDict,
                                                        isForceCreateDirectoryAndReturnDictionary: isForceCreateDirectoryAndReturnDictionary,
                                                        isRestoreAttributesFromDatabase: isRestoreAttributesFromDatabase,
                                                        versioning: versioning);
            }
            return backedUpDirectoriesDict;
        }

        private Dictionary<string, BackedUpDirectoryData>? CopyDirectory(string originDirPath,
                                                                         string sourceBaseDirPath,
                                                                         string destBaseDirPath,
                                                                         BackupResults results,
                                                                         bool isCopyAttributes = true,
                                                                         Dictionary<string, BackedUpDirectoryData>? backedUpDirectoriesDict = null,
                                                                         bool isForceCreateDirectoryAndReturnDictionary = false,
                                                                         bool isRestoreAttributesFromDatabase = false,
                                                                         SymbolicLinkHandling symbolicLinkHandling = SymbolicLinkHandling.IgnoreOnlyDirectories,
                                                                         VersioningMethod versioning = VersioningMethod.PermanentDeletion)
        {
            try
            {
                DirectoryInfo? originDirInfo = isCopyAttributes ? new DirectoryInfo(originDirPath) : null;
                DirectoryInfo? destDirInfo = null;
                // シンボリックリンクをバックアップ先に再現する場合
                if (symbolicLinkHandling == SymbolicLinkHandling.Direct && (originDirInfo ??= new DirectoryInfo(originDirPath)).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    string destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    BackupController.CopyReparsePoint(originDirPath, destDirPath);
                }
                // データベースのデータを使わない場合
                else if (backedUpDirectoriesDict is null || !backedUpDirectoriesDict.TryGetValue(originDirPath, out var data) || (isForceCreateDirectoryAndReturnDictionary && !isRestoreAttributesFromDatabase))
                {
                    string destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    Logger.Debug($"存在しなければ作成: '{destDirPath}'");
                    destDirInfo = Directory.CreateDirectory(destDirPath);
                    if (isCopyAttributes) // originDirInfo は null ではない
                    {
                        Logger.Debug("ディレクトリ属性をコピー");
                        try
                        {
                            destDirInfo.CreationTime = originDirInfo!.CreationTime;
                            destDirInfo.LastWriteTime = originDirInfo.LastWriteTime;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Logger.Warn($"'{destDirPath}'のCreationTime/LastWriteTimeを変更できません");
                        }
                        destDirInfo.Attributes = originDirInfo!.Attributes;
                    }
                }
                // データベースのデータを元に属性を復元する場合
                else if (isRestoreAttributesFromDatabase)
                {
                    string destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    Logger.Debug($"存在しなければ作成: '{destDirPath}'");
                    destDirInfo = Directory.CreateDirectory(destDirPath);
                    Logger.Info("データベースからディレクトリ属性をリストア: '{0}'", destDirPath);
                    try
                    {
                        destDirInfo.CreationTime = data.CreationTime ?? (originDirInfo ??= new DirectoryInfo(originDirPath)).CreationTime;
                        destDirInfo.LastWriteTime = data.LastWriteTime ?? (originDirInfo ??= new DirectoryInfo(originDirPath)).LastWriteTime;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Warn($"'{destDirPath}'のCreationTime/LastWriteTimeを変更できません");
                    }
                    destDirInfo.Attributes = data.FileAttributes ?? (originDirInfo ?? new DirectoryInfo(originDirPath)).Attributes;
                }
                // 以前のバックアップデータがある場合、変更されたプロパティのみ更新する(変更なしなら何もしない)
                else if (isCopyAttributes) // originDirInfo は null ではない
                {
                    string destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    try
                    {
                        if (originDirInfo!.CreationTime != backedUpDirectoriesDict[originDirPath].CreationTime)
                            (destDirInfo = Directory.CreateDirectory(destDirPath)).CreationTime = originDirInfo.CreationTime;
                        if (originDirInfo.LastWriteTime != backedUpDirectoriesDict[originDirPath].LastWriteTime)
                            (destDirInfo ??= Directory.CreateDirectory(destDirPath)).LastWriteTime = originDirInfo.LastWriteTime;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Warn($"'{destDirPath}'のCreationTime/LastWriteTimeを変更できません");
                    }
                    if (originDirInfo!.Attributes != backedUpDirectoriesDict[originDirPath].FileAttributes)
                        (destDirInfo ?? Directory.CreateDirectory(destDirPath)).Attributes = originDirInfo.Attributes;
                }
                results.successfulDirectories.Add(originDirPath);
                results.failedDirectories.Remove(originDirPath);
                if (isForceCreateDirectoryAndReturnDictionary && backedUpDirectoriesDict == null)
                    backedUpDirectoriesDict = new Dictionary<string, BackedUpDirectoryData>();
                if (backedUpDirectoriesDict != null && !isRestoreAttributesFromDatabase)
                    backedUpDirectoriesDict[originDirPath] = new BackedUpDirectoryData(originDirInfo?.CreationTime, originDirInfo?.LastWriteTime, originDirInfo?.Attributes);
            }
            catch (Exception e)
            {
                Logger.Error(e, results.Message = $"'{originDirPath}' => '{originDirPath.Replace(sourceBaseDirPath, destBaseDirPath)}'のコピーに失敗");
                results.failedDirectories.Add(originDirPath);
            }
            return backedUpDirectoriesDict;
        }


        private void Results_Finished(object? sender, EventArgs e)
        {
            if (isEnableWriteDatabase && Database != null)
            {
                Logger.Info("データベースを保存: '{0}'", DataFileWriter.GetPath(Database));
                _ = DataFileWriter.WriteAsync(Database);
            }
            Results.Message = (Results.isSuccess ? "リストア完了: " : Results.Message + "\nリストア失敗: ") + DateTime.Now;
            Logger.Info("{0}\n=============================\n\n", Results.isSuccess ? "リストア完了" : "リストア失敗");
        }
    }
}