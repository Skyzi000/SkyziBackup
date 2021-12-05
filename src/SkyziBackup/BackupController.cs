﻿using NLog;
using Skyzi000;
using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Skyzi000.IO.FileSystem;

namespace SkyziBackup
{
    public class BackupController : IDisposable
    {
        public BackupResults Results { get; private set; }
        public CompressiveAesCryptor? AesCryptor { get; private set; }
        public BackupSettings Settings { get; set; }
        public BackupDatabase? Database { get; private set; } = null;
        public CancellationTokenSource? CTS { get; private set; } = null;
        public readonly string originBaseDirPath;
        public readonly string destBaseDirPath;
        public DateTime StartTime { get; private set; }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private int currentRetryCount = 0;
        private Task<BackupDatabase>? loadBackupDatabaseTask = null;
        private bool disposedValue;

        public BackupController(string originDirectoryPath, string destDirectoryPath, string? password = null, BackupSettings? settings = null)
        {
            originBaseDirPath = GetQualifiedDirectoryPath(originDirectoryPath);
            destBaseDirPath = GetQualifiedDirectoryPath(destDirectoryPath);
            Settings = settings ?? BackupSettings.LoadLocalSettings(originBaseDirPath, destBaseDirPath) ?? BackupSettings.Default;
            Results = new BackupResults(originDirectoryPath, destDirectoryPath);
            if (Settings.IsDefault)
                Settings = new BackupSettings(Settings).ConvertToLocalSettings(originBaseDirPath, destBaseDirPath);
            if (Settings.IsUseDatabase)
            {
                loadBackupDatabaseTask = LoadOrCreateDatabaseAsync();
            }
            if (Settings.IsCancelable)
            {
                CTS = new CancellationTokenSource();
            }
            if (!string.IsNullOrEmpty(password))
            {
                AesCryptor = new CompressiveAesCryptor(password, compressionLevel: Settings.CompressionLevel, compressAlgorithm: Settings.CompressAlgorithm);
            }
        }

        private async Task InitializeAsync()
        {
            if (Settings.IsDefault)
                Settings = new BackupSettings(Settings).ConvertToLocalSettings(originBaseDirPath, destBaseDirPath);
            Task? saveTask = Settings.SaveAsync();
            if (Settings.IsUseDatabase)
            {
                Database = await (loadBackupDatabaseTask ?? LoadOrCreateDatabaseAsync());
                // 万が一データベースと一致しない場合は読み込みなおす(データベースファイルを一旦削除かリネームする処理を入れても良いかも)
                if (Database.DestBaseDirPath != destBaseDirPath)
                {
                    Database = await LoadOrCreateDatabaseAsync();
                    if (Database.DestBaseDirPath != destBaseDirPath)
                    {
                        Logger.Error(Results.Message = $"データベースの読み込み失敗: 既存のデータベース'{DataFileWriter.GetPath(Database)}'を利用できません。");
                        Database = new BackupDatabase(originBaseDirPath, destBaseDirPath);
                    }
                }
                Database.StartAutoSave(60000);
                Database.SaveTimer.Elapsed += (s, e) => { Logger.Info("現時点のデータベースを保存: '{0}'", DataFileWriter.GetPath(Database)); };
            }
            else
            {
                Database = null;
            }
            Results.successfulFiles = new HashSet<string>();
            Results.successfulDirectories = new HashSet<string>();
            Results.failedFiles = new HashSet<string>();
            Results.failedDirectories = new HashSet<string>();
            if (Settings.ComparisonMethod != ComparisonMethod.NoComparison)
                Results.unchangedFiles = new HashSet<string>();
            if (Settings.IsEnableDeletion)
            {
                Results.deletedFiles = new HashSet<string>();
                Results.deletedDirectories = new HashSet<string>();
            }
            await saveTask;
        }

        public static string GetQualifiedDirectoryPath(string directoryPath)
        {
            var s = directoryPath.Trim();
            return Path.GetFullPath(s.EndsWith(Path.DirectorySeparatorChar) ? s : s + Path.DirectorySeparatorChar);
        }

        private async Task<BackupDatabase> LoadOrCreateDatabaseAsync()
        {
            string databasePath;
            var isExists = File.Exists(databasePath = DataFileWriter.GetDatabasePath(originBaseDirPath, destBaseDirPath));
            Logger.Info(Results.Message = isExists ? $"既存のデータベースをロード: '{databasePath}'" : "新規データベースを初期化");
            return isExists
                ? await DataFileWriter.ReadAsync<BackupDatabase>(DataFileWriter.GetDatabaseFileName(originBaseDirPath, destBaseDirPath))
                ?? new BackupDatabase(originBaseDirPath, destBaseDirPath)
                : new BackupDatabase(originBaseDirPath, destBaseDirPath);
        }

        private void CleanUpDatabase()
        {
            Database?.Dispose();
            Database = null;
        }

        public async Task SaveDatabaseAsync()
        {
            if (Settings.IsUseDatabase && Database != null)
            {
                Database.SaveTimer.Stop();
                Logger.Info("データベースを保存: '{0}'", DataFileWriter.GetPath(Database));
                await Database.SaveAsync().ConfigureAwait(false);
                Logger.Debug("データベース保存完了: '{0}'", DataFileWriter.GetPath(Database));
            }
        }

        /// <summary>
        /// データベースを利用している場合は非同期で現在のデータを保存してからキャンセルする。
        /// </summary>
        public async Task CancelAsync()
        {
            await SaveDatabaseAsync().ConfigureAwait(false);
            if (CTS is null)
                Logger.Info("キャンセル失敗: キャンセル機能が無効です");
            CTS?.Cancel();
            Results.isSuccess = false;
            Results.IsFinished = true;
        }

        public async Task<BackupResults> StartBackupAsync(CancellationToken cancellationToken = default)
        {
            if (Results.IsFinished)
                throw new InvalidOperationException("Backup already finished.");
            if (disposedValue)
                throw new ObjectDisposedException(GetType().FullName);
            if (cancellationToken != default)
            {
                CTS?.Dispose();
                CTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            CancellationToken cToken = CTS?.Token ?? default;

            Logger.Info("バックアップ設定:\n{0}", Settings);
            Logger.Info("バックアップを開始'{0}' => '{1}'", originBaseDirPath, destBaseDirPath);

            StartTime = DateTime.Now;
            await InitializeAsync().ConfigureAwait(false);

            if (!Directory.Exists(originBaseDirPath))
            {
                Logger.Warn(Results.Message = $"バックアップ元のディレクトリ'{originBaseDirPath}'が見つかりません。");
                Results.IsFinished = true;
                return Results;
            }

            try
            {
                // ディレクトリの処理
                if (Settings.IsUseDatabase && Database != null)
                    Database.BackedUpDirectoriesDict = await Task
                        .Run(
                            () => CopyDirectoryStructure(originBaseDirPath, destBaseDirPath, Results,
                                Settings.IsCopyAttributes, Settings.Regices, Database.BackedUpDirectoriesDict), cToken)
                        .ConfigureAwait(false) ?? throw new InvalidOperationException();
                else
                    _ = await Task
                        .Run(
                            () => CopyDirectoryStructure(originBaseDirPath, destBaseDirPath, Results,
                                Settings.IsCopyAttributes, Settings.Regices), cToken)
                        .ConfigureAwait(false);

                // ファイルの処理
                await Task.Run(async () =>
                {
                    foreach (var originFilePath in Settings.SymbolicLink is SymbolicLinkHandling.IgnoreAll or SymbolicLinkHandling.IgnoreOnlyDirectories
                        ? EnumerateAllFilesIgnoreReparsePoints(originBaseDirPath, Settings.Regices)
                        : EnumerateAllFiles(originBaseDirPath, Settings.Regices))
                    {
                        var destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                        // 除外パターンと一致せず、バックアップ済みファイルと一致しないファイルをバックアップする
                        if (!IsIgnoredFile(originFilePath) && !(Settings.IsUseDatabase
                            ? IsUnchangedFileOnDatabase(originFilePath, destFilePath)
                            : IsUnchangedFileWithoutDatabase(originFilePath, destFilePath)))
                            await Task.Run(() => BackupFile(originFilePath, destFilePath), cToken).ConfigureAwait(false);
                    }
                }, cToken).ConfigureAwait(false);

                // ミラーリング処理(バックアップ先ファイルの削除)
                if (Settings.IsEnableDeletion)
                {
                    await Task.Run(() => DeleteFiles(), cToken).ConfigureAwait(false);
                    await Task.Run(() => DeleteDirectories(), cToken).ConfigureAwait(false);
                }

                // 成功判定
                Results.isSuccess = !Results.failedDirectories.Any() && !Results.failedFiles.Any();

                // リトライ処理
                if ((Results.failedDirectories.Any() || Results.failedFiles.Any()) && Settings.RetryCount > 0)
                {
                    Logger.Info($"{Settings.RetryWaitMilliSec} ミリ秒毎に {Settings.RetryCount} 回リトライ");
                    await RetryStartAsync(cToken).ConfigureAwait(false);
                }
                else
                {
                    Results.IsFinished = true;
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("バックアップはキャンセルされました。");
                if (Results.SaveFileName is not null)
                    Results.Save();
                throw;
            }
            if (Database is not null)
            {
                await SaveDatabaseAsync();
                CleanUpDatabase();
            }
            Results.Message = (Results.isSuccess ? "バックアップ完了: " : Results.Message + "\nバックアップ失敗: ") + DateTime.Now;
            Logger.Info("{0}\n=============================\n\n", Results.isSuccess ? "バックアップ完了" : "バックアップ失敗");
            if (Results.SaveFileName is not null)
                await Results.SaveAsync().ConfigureAwait(false);
            return Results;
        }

        private void DeleteDirectories()
        {
            if (Settings.IsUseDatabase && Database != null)
            {
                foreach (var originDirPath in Database.BackedUpDirectoriesDict.Keys)
                {
                    if (Results.successfulDirectories.Contains(originDirPath) || Results.failedDirectories.Contains(originDirPath))
                    {
                        continue;
                    }
                    var destDirPath = originDirPath.Replace(originBaseDirPath, destBaseDirPath);
                    if (!Directory.Exists(destDirPath))
                    {
                        Database.BackedUpDirectoriesDict.Remove(originDirPath);
                        continue;
                    }
                    try
                    {
                        DeleteDirectory(destDirPath);
                        Results.deletedDirectories?.Add(originDirPath);
                        Database.BackedUpDirectoriesDict.Remove(originDirPath);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, Results.Message = $"'{destDirPath}'の削除に失敗");
                    }
                }
            }
            else // データベースを使わない
            {
                foreach (var destDirPath in Settings.SymbolicLink is SymbolicLinkHandling.IgnoreOnlyDirectories or SymbolicLinkHandling.IgnoreAll
                    ? EnumerateAllDirectoriesIgnoreReparsePoints(destBaseDirPath, Settings.Regices)
                    : EnumerateAllDirectories(destBaseDirPath, Settings.Regices))
                {
                    var originDirPath = destDirPath.Replace(destBaseDirPath, originBaseDirPath);
                    if (Results.successfulDirectories.Contains(originDirPath) || Results.failedDirectories.Contains(originDirPath))
                    {
                        continue;
                    }
                    try
                    {
                        DeleteDirectory(destDirPath);
                        Results.deletedDirectories?.Add(originDirPath);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, Results.Message = $"'{destDirPath}'の削除に失敗");
                    }
                }
            }
        }

        private void DeleteDirectory(string directoryPath)
        {
            string revDirPath;
            switch (Settings.Versioning)
            {
                case VersioningMethod.PermanentDeletion:
                    Directory.Delete(directoryPath);
                    break;
                case VersioningMethod.RecycleBin:
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(directoryPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    break;
                case VersioningMethod.Replace:
                case VersioningMethod.FileTimeStamp:
                    revDirPath = directoryPath.Replace(destBaseDirPath, Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)));
                    CopyDirectoryAttributes(directoryPath, revDirPath);
                    break;
                case VersioningMethod.DirectoryTimeStamp:
                    revDirPath = Path.Combine(Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)), StartTime.ToString("yyyy-MM-dd_HHmmss"), directoryPath.Replace(destBaseDirPath, null));
                    CopyDirectoryAttributes(directoryPath, revDirPath);
                    break;
                default:
                    return;
            }
        }

        private void CopyDirectoryAttributes(string originDirPath, string destDirPath)
        {
            var originDirInfo = new DirectoryInfo(originDirPath);
            Logger.Debug($"存在しなければ作成: '{destDirPath}'");
            DirectoryInfo? destDirInfo = Directory.CreateDirectory(destDirPath);
            if (Settings.IsCopyAttributes)
            {
                Logger.Debug("ディレクトリ属性をコピー");
                try
                {
                    destDirInfo.CreationTime = originDirInfo.CreationTime;
                    destDirInfo.LastWriteTime = originDirInfo.LastWriteTime;
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Warn($"'{destDirPath}'のCreationTime/LastWriteTimeを変更できません");
                }
                destDirInfo.Attributes = originDirInfo.Attributes;
            }
        }

        private void DeleteFiles()
        {
            if (Settings.IsUseDatabase && Database != null)
            {
                foreach (var originFilePath in Database.BackedUpFilesDict.Keys)
                {
                    if (Results.successfulFiles.Contains(originFilePath) || Results.failedFiles.Contains(originFilePath) || (Results.unchangedFiles?.Contains(originFilePath) ?? false))
                    {
                        continue;
                    }
                    var destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                    if (!File.Exists(destFilePath))
                    {
                        Database.BackedUpFilesDict.Remove(originFilePath);
                        continue;
                    }
                    DeleteFile(destFilePath, originFilePath);
                }
            }
            else // データベースを使わない
            {
                foreach (var destFilePath in Settings.SymbolicLink is SymbolicLinkHandling.IgnoreOnlyDirectories or SymbolicLinkHandling.IgnoreAll
                    ? EnumerateAllFilesIgnoreReparsePoints(destBaseDirPath, Settings.Regices)
                    : EnumerateAllFiles(destBaseDirPath, Settings.Regices))
                {
                    var originFilePath = destFilePath.Replace(destBaseDirPath, originBaseDirPath);
                    if (Results.successfulFiles.Contains(originFilePath) || Results.failedFiles.Contains(originFilePath) || (Results.unchangedFiles?.Contains(originFilePath) ?? false))
                    {
                        continue;
                    }
                    DeleteFile(destFilePath, originFilePath);
                }
            }
        }

        private void DeleteFile(string destFilePath, string originFilePath)
        {
            if (Settings.IsOverwriteReadonly)
            {
                RemoveReadonlyAttribute(destFilePath);
            }
            try
            {
                Logger.Info(Results.Message = $"ファイルを削除: '{destFilePath}'");
                DeleteFile(destFilePath);
                Results.deletedFiles?.Add(originFilePath);
                Database?.BackedUpFilesDict.Remove(originFilePath);
            }
            catch (Exception e)
            {
                Logger.Error(e, Results.Message = $"'{destFilePath}'の削除に失敗");
            }
        }

        private void DeleteFile(string filePath)
        {
            string revisionFilePath;
            switch (Settings.Versioning)
            {
                case VersioningMethod.PermanentDeletion:
                    File.Delete(filePath);
                    return;
                case VersioningMethod.RecycleBin:
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(filePath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    return;
                case VersioningMethod.Replace:
                    revisionFilePath = filePath.Replace(destBaseDirPath, Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)));
                    break;
                case VersioningMethod.DirectoryTimeStamp:
                    revisionFilePath = Path.Combine(Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)), StartTime.ToString("yyyy-MM-dd_HHmmss"), filePath.Replace(destBaseDirPath, "").TrimStart(Path.DirectorySeparatorChar));
                    break;
                case VersioningMethod.FileTimeStamp:
                    revisionFilePath = filePath.Replace(destBaseDirPath, Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath))) + StartTime.ToString("_yyyy-MM-dd_HHmmss") + Path.GetExtension(filePath);
                    break;
                default:
                    throw new InvalidOperationException(nameof(Settings.Versioning));
            }
            var removedReadonlyAttributeFromRevisionFile = false;
            FileAttributes? fileAttributes = null;
            if (Settings.IsOverwriteReadonly && File.Exists(revisionFilePath) && (removedReadonlyAttributeFromRevisionFile = RemoveReadonlyAttribute(revisionFilePath)))
                fileAttributes = File.GetAttributes(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(revisionFilePath) ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)));
            File.Move(filePath, revisionFilePath, true);
            if (removedReadonlyAttributeFromRevisionFile && fileAttributes.HasValue)
                File.SetAttributes(revisionFilePath, fileAttributes.Value);
        }

        // TODO: これをstaticにしたのは失敗と思われる。RestoreControllerのとは共通化せず、それぞれインスタンスメソッドに書き直す。
        [return: NotNullIfNotNull("backedUpDirectoriesDict")]
        public Dictionary<string, BackedUpDirectoryData>? CopyDirectoryStructure(string sourceBaseDirPath,
                                                                                 string destBaseDirPath,
                                                                                 BackupResults results,
                                                                                 bool isCopyAttributes = true,
                                                                                 IEnumerable<Regex>? regices = null,
                                                                                 Dictionary<string, BackedUpDirectoryData>? backedUpDirectoriesDict = null,
                                                                                 bool isForceCreateDirectoryAndReturnDictionary = false,
                                                                                 bool isRestoreAttributesFromDatabase = false,
                                                                                 SymbolicLinkHandling symbolicLink = SymbolicLinkHandling.IgnoreOnlyDirectories,
                                                                                 VersioningMethod versioning = VersioningMethod.PermanentDeletion)
        {
            if (isRestoreAttributesFromDatabase && backedUpDirectoriesDict == null)
            {
                throw new ArgumentNullException(nameof(backedUpDirectoriesDict));
            }
            Logger.Info(results.Message = $"ディレクトリ構造をコピー");
            return (symbolicLink is SymbolicLinkHandling.IgnoreOnlyDirectories or SymbolicLinkHandling.IgnoreAll
                ? EnumerateAllDirectoriesIgnoreReparsePoints(sourceBaseDirPath, regices)
                : EnumerateAllDirectories(sourceBaseDirPath, regices)).Aggregate(backedUpDirectoriesDict,
                (current, originDirPath) => CopyDirectory(originDirPath: originDirPath,
                    sourceBaseDirPath: sourceBaseDirPath, destBaseDirPath: destBaseDirPath, results: results,
                    isCopyAttributes: isCopyAttributes, backedUpDirectoriesDict: current,
                    isForceCreateDirectoryAndReturnDictionary: isForceCreateDirectoryAndReturnDictionary,
                    isRestoreAttributesFromDatabase: isRestoreAttributesFromDatabase,
                    symbolicLinkHandling: symbolicLink, versioning: versioning));
        }
        public static void CopyReparsePoint(string sourcePath, string destPath, bool overwrite = false)
        {
            // TODO: ネイティブAPIを利用するようにする
            const string junction = "Junction";
            const string symbolicLink = "SymbolicLink";
            var powershell = "powershell.exe";
            var getItemArg = "Get-ItemProperty ";
            var startInfo = new ProcessStartInfo(powershell, getItemArg + sourcePath + @" | Select-Object -ExpandProperty LinkType")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var linkType = RunProcess(startInfo);
            startInfo.Arguments = getItemArg + sourcePath + @" | Select-Object -ExpandProperty Target";
            if (linkType is junction or symbolicLink)
            {
                var target = RunProcess(startInfo);
                if (string.IsNullOrEmpty(target)) throw new IOException($"'{sourcePath}'のTargetを取得できません。");
                if (overwrite)
                {
                    if (Directory.Exists(destPath))
                        Directory.Delete(destPath);
                    else if (File.Exists(destPath))
                        File.Delete(destPath);
                }
                startInfo.Arguments = string.Join(' ', "New-Item", "-Type", linkType, destPath, "-Value", target);
                var wd = Path.GetDirectoryName(sourcePath);
                if (!Path.IsPathFullyQualified(target) && wd != null)
                    startInfo.WorkingDirectory = wd;
                RunProcess(startInfo);
            }
            else throw new IOException($"'{sourcePath}'のLinkType({linkType})を識別できません。");

            static string? RunProcess(ProcessStartInfo startInfo)
            {
                using var proc = Process.Start(startInfo);
                proc?.WaitForExit();
                return proc?.StandardOutput.ReadToEnd().Trim('\n', '\r', ' ');
            }
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
                    var destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    if (versioning != VersioningMethod.PermanentDeletion && (destDirInfo = new DirectoryInfo(destDirPath)).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        DeleteDirectory(destDirPath);
                    }
                    CopyReparsePoint(originDirPath, destDirPath, versioning == VersioningMethod.PermanentDeletion);
                }
                // データベースのデータを使わない場合
                else if (backedUpDirectoriesDict is null || !backedUpDirectoriesDict.TryGetValue(originDirPath, out BackedUpDirectoryData? data) || (isForceCreateDirectoryAndReturnDictionary && !isRestoreAttributesFromDatabase) || originDirPath == sourceBaseDirPath)
                {
                    var destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    Logger.Debug($"存在しなければ作成: '{destDirPath}'");
                    destDirInfo = Directory.CreateDirectory(destDirPath);
                    // ベースディレクトリは属性をコピーしない
                    if (isCopyAttributes && originDirPath != sourceBaseDirPath) // originDirInfo は null ではない
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
                    var destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
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
                    var destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
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

        private bool IsIgnoredFile(string originFilePath)
        {
            if (Settings.SymbolicLink == SymbolicLinkHandling.IgnoreAll)
                if (File.GetAttributes(originFilePath).HasFlag(FileAttributes.ReparsePoint))
                    return true;
            // 除外パターンとマッチング
            if (Settings.Regices != null)
            {
                var s = originFilePath[(originBaseDirPath.Length - 1)..];
                if (Settings.Regices.Any(r => r.IsMatch(s)))
                    return true;
            }
            return false;
        }
        /// <summary>
        /// データベースにデータが記録されている場合はバックアップ先ファイルにアクセスしない(比較に必要なデータが無い場合はアクセスしに行く)
        /// </summary>
        /// <returns>前回のバックアップから変更されていることが確認できたら true</returns>
        private bool IsUnchangedFileOnDatabase(string originFilePath, string destFilePath)
        {
            if (Database is null)
                return IsUnchangedFileWithoutDatabase(originFilePath, destFilePath);
            if (!Database.BackedUpFilesDict.ContainsKey(originFilePath)) return false;
            if (Settings.ComparisonMethod == ComparisonMethod.NoComparison) return false;
            FileInfo? originFileInfo = null;
            BackedUpFileData destFileData = Database.BackedUpFilesDict[originFilePath];

            // Archive属性
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute))
            {
                if ((originFileInfo = new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                {
                    return false;
                }
            }

            // 更新日時
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime))
            {
                if (destFileData.LastWriteTime == null)
                {
                    if (!File.Exists(destFilePath))
                    {
                        Logger.Warn("データベースに更新日時が記録されていません。バックアップ先にファイルが存在しません。: '{0}'", destFilePath);
                        return false;
                    }
                    Logger.Warn("データベースに更新日時が記録されていません。バックアップ先の更新日時を記録します。: '{0}'", destFileData.LastWriteTime = File.GetLastWriteTime(destFilePath));
                }
                if ((originFileInfo?.LastWriteTime ?? (originFileInfo = new FileInfo(originFilePath)).LastWriteTime) != destFileData.LastWriteTime)
                {
                    return false;
                }
            }

            // サイズ
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.Size))
            {
                if (destFileData.OriginSize == -1)
                {
                    Logger.Warn("ファイルサイズの比較ができません: データベースにファイルサイズが記録されていません。");
                    return false;
                }
                if ((originFileInfo?.Length ?? (originFileInfo = new FileInfo(originFilePath)).Length) != destFileData.OriginSize)
                {
                    return false;
                }
            }

            // SHA1
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            {
                if (destFileData.Sha1 == null)
                {
                    Logger.Warn("ハッシュの比較ができません: データベースにSHA1が記録されていません。");
                    return false;
                }
                Logger.Debug(Results.Message = $"SHA1で比較: '{destFileData.Sha1}' : '{originFilePath}'");
                if (BackupManager.ComputeFileSHA1(originFilePath) != destFileData.Sha1)
                {
                    return false;
                }
            }

            // 生データ(データベースを用いず比較)
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsBynary))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }
                else if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Warn("生データの比較ができません: 圧縮有効時に生データを比較することはできません。");
                    return false;
                }
                if (!File.Exists(destFilePath))
                {
                    Logger.Warn("バックアップ先にファイルが存在しません。: '{0}'", destFilePath);
                    return false;
                }
                Logger.Warn(Results.Message = $"生データの比較にはデータベースを利用できません。直接比較します。'{originFilePath}' = '{destFilePath}'");
                int originByte;
                int destByte;
                using FileStream? originStream = originFileInfo?.OpenRead() ?? new FileStream(originFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
            Results.unchangedFiles?.Add(originFilePath);
            return true;
        }
        /// <summary>
        /// データベースを使わず、実際にファイルを比較する
        /// </summary>
        /// <returns>前回のバックアップから変更されていることが確認できたら true</returns>
        private bool IsUnchangedFileWithoutDatabase(string originFilePath, string destFilePath)
        {
            if (!File.Exists(destFilePath)) return false;
            if (Settings.ComparisonMethod == ComparisonMethod.NoComparison) return false;
            if (!Settings.IsCopyAttributes) Logger.Warn("ファイルを正しく比較出来ません: ファイル属性のコピーが無効になっています。ファイルを比較するにはデータベースを利用するか、ファイル属性のコピーを有効にしてください。");
            FileInfo? destFileInfo = null;
            FileInfo? originFileInfo = null;

            // Archive属性
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute))
            {
                if ((originFileInfo = new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                {
                    return false;
                }
            }

            // 更新日時
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime))
            {
                if ((originFileInfo?.LastWriteTime ?? (originFileInfo = new FileInfo(originFilePath)).LastWriteTime) != (destFileInfo = new FileInfo(destFilePath)).LastWriteTime)
                {
                    return false;
                }
            }

            // サイズ
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.Size))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("ファイルサイズの比較ができません: 暗号化有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
                    return false;
                }
                else if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Warn("ファイルサイズの比較ができません: 圧縮有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
                    return false;
                }
                if ((originFileInfo?.Length ?? (originFileInfo = new FileInfo(originFilePath)).Length) != (destFileInfo?.Length ?? (destFileInfo = new FileInfo(destFilePath)).Length))
                {
                    return false;
                }
            }

            // SHA1
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("ハッシュの比較ができません: 暗号化有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }
                else if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Warn("ハッシュの比較ができません: 圧縮有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }
                Logger.Warn("データベースを利用しない場合、ハッシュでの比較は非効率的です。データベースを利用するか、生データでの比較を検討してください。");
                Logger.Debug(Results.Message = $"SHA1で比較: '{originFilePath}' = '{destFilePath}'");
                if (BackupManager.ComputeFileSHA1(originFilePath) != BackupManager.ComputeFileSHA1(destFilePath))
                {
                    return false;
                }
            }

            // 生データ
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsBynary))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }
                else if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Warn("生データの比較ができません: 圧縮有効時に生データを比較することはできません。");
                    return false;
                }
                Logger.Debug(Results.Message = $"生データの比較: '{originFilePath}' = '{destFilePath}'");
                int originByte;
                int destByte;
                using FileStream? originStream = originFileInfo?.OpenRead() ?? new FileStream(originFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using FileStream? destStream = destFileInfo?.OpenRead() ?? new FileStream(destFilePath, FileMode.Open, FileAccess.Read);
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
            Results.unchangedFiles?.Add(originFilePath);
            return true;
        }
        /// <summary>
        /// 読み取り専用属性を持っていないとデータベースに記録されているファイルかどうか。
        /// </summary>
        /// <returns>前回のバックアップ時に読み取り専用属性がなかった場合 true, 対象のファイルが読み取り専用属性を持っていたり、データが無い場合は false</returns>
        private bool IsNotAttributesInDatabase(string originFilePath, FileAttributes fileAttributes)
        {
            if (Database is null) return false;
            return Database.BackedUpFilesDict.TryGetValue(originFilePath, out BackedUpFileData? data) && data.FileAttributes.HasValue && !data.FileAttributes.Value.HasFlag(fileAttributes);
        }
        private void BackupFile(string originFilePath, string destFilePath)
        {
            Logger.Info(Results.Message = $"ファイルをバックアップ: '{originFilePath}' => '{destFilePath}'");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destFilePath) ?? throw new ArgumentException(nameof(destFilePath)));
                if (Settings.IsOverwriteReadonly)
                {
                    RemoveReadonlyAttribute(originFilePath, destFilePath);
                    RemoveHiddenAttribute(originFilePath, destFilePath);
                }
                if (Settings.Versioning != VersioningMethod.PermanentDeletion && (Database?.BackedUpFilesDict.ContainsKey(originFilePath) ?? File.Exists(destFilePath)))
                {
                    try
                    {
                        DeleteFile(destFilePath);
                    }
                    catch (Exception e) when (e is NullReferenceException or InvalidOperationException)
                    {
                        Logger.Warn(e, Results.Message = $"バージョン管理設定が正しくありません。");
                        throw;
                    }
                }
                if (Settings.SymbolicLink == SymbolicLinkHandling.Direct && File.GetAttributes(originFilePath).HasFlag(FileAttributes.ReparsePoint))
                {
                    CopyReparsePoint(originFilePath, destFilePath, Settings.Versioning == VersioningMethod.PermanentDeletion);
                    CopyFileAttributesAndUpdateDatabase(originFilePath, destFilePath);
                }
                else if (AesCryptor != null)
                {
                    AesCryptor.EncryptFile(originFilePath, destFilePath);
                    CopyFileAttributesAndUpdateDatabase(originFilePath, destFilePath);
                }
                else
                {
                    // 暗号化しないでバックアップ
                    using (var origin = new FileStream(originFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var dest = new FileStream(destFilePath, FileMode.Create, FileAccess.ReadWrite))
                    {
                        origin.CopyWithCompressionTo(dest, Settings.CompressionLevel, CompressionMode.Compress, Settings.CompressAlgorithm);
                    }
                    CopyFileAttributesAndUpdateDatabase(originFilePath, destFilePath);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, Results.Message = $"'{originFilePath}' => '{destFilePath}'のバックアップに失敗");
                Results.failedFiles.Add(originFilePath);
            }
        }
        /// <summary>
        /// <paramref name="destFilePath"/> が読み取り専用なら解除する(読み取り専用でないとデータベースに記録されているファイルの場合は確かめない)
        /// </summary>
        /// <remarks>
        /// IsNotReadOnlyInDatabase() の戻り値が true かつバックアップ先ファイルが読み取り専用属性を持つ場合は解除されないので、後で<see cref="UnauthorizedAccessException"/>になる
        /// </remarks>
        /// <returns>読み取り専用を解除したら true</returns>
        private bool RemoveReadonlyAttribute(string originFilePath, string destFilePath)
        {
            if (Settings.IsUseDatabase && IsNotAttributesInDatabase(originFilePath, FileAttributes.ReadOnly))
                return false;
            return RemoveReadonlyAttribute(destFilePath);
        }
        private bool RemoveReadonlyAttribute(string filePath)
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists && fi.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                fi.Attributes = RemoveAttribute(fi.Attributes, FileAttributes.ReadOnly);
                return true;
            }
            return false;
        }
        private bool RemoveHiddenAttribute(string originFilePath, string destFilePath)
        {
            if (Settings.IsUseDatabase && IsNotAttributesInDatabase(originFilePath, FileAttributes.Hidden))
                return false;
            return RemoveHiddenAttribute(destFilePath);
        }
        private bool RemoveHiddenAttribute(string filePath)
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists && fi.Attributes.HasFlag(FileAttributes.Hidden))
            {
                fi.Attributes = RemoveAttribute(fi.Attributes, FileAttributes.Hidden);
                fi.Refresh();
                return true;
            }
            return false;
        }

        private void CopyFileAttributesAndUpdateDatabase(string originFilePath, string destFilePath)
        {
            FileInfo? originInfo = null;
            FileInfo? destInfo = null;
            if (Settings.IsCopyAttributes)
            {
                if (Settings.IsUseDatabase && Database != null && Database.BackedUpFilesDict.TryGetValue(originFilePath, out BackedUpFileData? data))
                {
                    // 以前のバックアップデータがある場合、変更されたプロパティのみ更新する(変更なしなら何もしない)
                    try
                    {
                        if (!data.CreationTime.HasValue || (originInfo = new FileInfo(originFilePath)).CreationTime != data.CreationTime)
                            (destInfo = new FileInfo(destFilePath)).CreationTime = (originInfo ??= new FileInfo(originFilePath)).CreationTime;
                        if (!data.LastWriteTime.HasValue || originInfo.LastWriteTime != data.LastWriteTime)
                            (destInfo ??= new FileInfo(destFilePath)).LastWriteTime = originInfo.LastWriteTime;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Warn($"'{destFilePath}'のCreationTime/LastWriteTimeを変更できません");
                    }
                    if (!data.FileAttributes.HasValue || (originInfo ??= new FileInfo(originFilePath)).Attributes != data.FileAttributes)
                        (destInfo ?? new FileInfo(destFilePath)).Attributes = (originInfo ??= new FileInfo(originFilePath)).Attributes;
                    // バックアップ先ファイルから取り除いた読み取り専用/隠し属性を戻す
                    else
                    {
                        if (Settings.IsOverwriteReadonly && (data.FileAttributes.Value.HasFlag(FileAttributes.ReadOnly) || data.FileAttributes.Value.HasFlag(FileAttributes.Hidden)))
                            (destInfo ?? new FileInfo(destFilePath)).Attributes = data.FileAttributes.Value;
                    }
                }
                else
                {
                    Logger.Debug($"属性をコピー");
                    destInfo = new FileInfo(destFilePath);
                    try
                    {
                        destInfo.CreationTime = (originInfo = new FileInfo(originFilePath)).CreationTime;
                        destInfo.LastWriteTime = originInfo.LastWriteTime;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Warn($"'{destFilePath}'のCreationTime/LastWriteTimeを変更できません");
                    }
                    destInfo.Attributes = (originInfo ?? new FileInfo(originFilePath)).Attributes;
                }
                // Archive属性
                if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute) && (originInfo ??= new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                {
                    (originInfo ??= new FileInfo(originFilePath)).Attributes = RemoveAttribute(originInfo.Attributes, FileAttributes.Archive);
                }
            }
            if (Settings.IsUseDatabase && Database != null)
            {
                // 必要なデータだけを保存
                Database.BackedUpFilesDict[originFilePath] = new BackedUpFileData(
                    creationTime: Settings.IsCopyAttributes ? (originInfo ??= new FileInfo(originFilePath)).CreationTime : null,
                    lastWriteTime: Settings.IsCopyAttributes || Settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime) ? (originInfo ??= new FileInfo(originFilePath)).LastWriteTime : null,
                    originSize: Settings.ComparisonMethod.HasFlag(ComparisonMethod.Size) ? (originInfo ??= new FileInfo(originFilePath)).Length : BackedUpFileData.DefaultSize,
                    fileAttributes: Settings.IsCopyAttributes || Settings.ComparisonMethod != ComparisonMethod.NoComparison ? (originInfo ?? new FileInfo(originFilePath)).Attributes : null,
                    sha1: Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1) ? BackupManager.ComputeFileSHA1(originFilePath) : null
                    );
            }
            Results.successfulFiles.Add(originFilePath);
            Results.failedFiles.Remove(originFilePath);
        }

        private async Task RetryStartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentRetryCount >= Settings.RetryCount)
            {
                Results.IsFinished = true;
                return;
            }
            Logger.Debug(Results.Message = $"リトライ待機中...({currentRetryCount + 1}/{Settings.RetryCount}回目)");

            await Task.Delay(Settings.RetryWaitMilliSec, cancellationToken);

            currentRetryCount++;
            Logger.Info(Results.Message = $"リトライ {currentRetryCount}/{Settings.RetryCount} 回目");
            if (Results.failedDirectories.Any())
            {
                foreach (var originDirPath in Results.failedDirectories.ToArray())
                {
                    if (Settings.IsUseDatabase && Database != null)
                        Database.BackedUpDirectoriesDict = await Task.Run(() => CopyDirectory(originDirPath, originBaseDirPath, destBaseDirPath, Results, Settings.IsCopyAttributes, Database.BackedUpDirectoriesDict), cancellationToken) ?? Database.BackedUpDirectoriesDict;
                    else
                        _ = await Task.Run(() => CopyDirectory(originDirPath, originBaseDirPath, destBaseDirPath, Results, Settings.IsCopyAttributes), cancellationToken);
                }
            }
            if (Results.failedFiles.Any())
            {
                foreach (var originFilePath in Results.failedFiles.ToArray())
                {
                    var destFilePath = originFilePath.Replace(originBaseDirPath, destBaseDirPath);
                    await Task.Run(() => BackupFile(originFilePath, destFilePath), cancellationToken);
                }
            }

            Results.isSuccess = !Results.failedDirectories.Any() && !Results.failedFiles.Any();

            if (Results.isSuccess)
            {
                Results.IsFinished = true;
                return;
            }
            else
            {
                await RetryStartAsync(cancellationToken);
            }
        }

        private static FileAttributes RemoveAttribute(FileAttributes attributes, FileAttributes attributesToRemove) => attributes & ~attributesToRemove;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    CTS?.Dispose();
                    AesCryptor?.Dispose();
                    Database?.Dispose();
                    loadBackupDatabaseTask?.Dispose();
                    // Settingsは借り物なので勝手にDisposeしない
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}