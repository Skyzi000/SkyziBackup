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
using Microsoft.VisualBasic.FileIO;
using NLog;
using Skyzi000;
using Skyzi000.Cryptography;
using SkyziBackup.Data;
using static Skyzi000.IO.FileSystem;

namespace SkyziBackup
{
    public class BackupController : IDisposable
    {
        public BackupResults Results { get; }
        public CompressiveAesCryptor? AesCryptor { get; }
        public BackupSettings Settings { get; set; }
        public BackupDatabase? Database { get; private set; }
        private SqliteBackupStateStore? _sqliteStore;
        public CancellationTokenSource? Cts { get; private set; }
        public readonly string OriginBaseDirPath;
        public readonly string DestBaseDirPath;
        public DateTime StartTime { get; private set; }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private int _currentRetryCount;
        private bool _hadDeletionError;
        private bool _disposedValue;

        public BackupController(string originDirectoryPath, string destDirectoryPath, string? password = null, BackupSettings? settings = null)
        {
            OriginBaseDirPath = GetQualifiedDirectoryPath(originDirectoryPath);
            DestBaseDirPath = GetQualifiedDirectoryPath(destDirectoryPath);
            Settings = settings ?? BackupSettings.LoadLocalSettings(OriginBaseDirPath, DestBaseDirPath) ?? BackupSettings.Default;
            Results = new BackupResults(originDirectoryPath, destDirectoryPath);
            if (Settings.IsDefault)
                Settings = new BackupSettings(Settings).ConvertToLocalSettings(OriginBaseDirPath, DestBaseDirPath);
            if (Settings.IsCancelable)
                Cts = new CancellationTokenSource();
            if (!string.IsNullOrEmpty(password))
                AesCryptor = new CompressiveAesCryptor(password, compressionLevel: Settings.CompressionLevel, compressAlgorithm: Settings.CompressAlgorithm);
        }

        private async Task InitializeAsync()
        {
            if (Settings.IsDefault)
                Settings = new BackupSettings(Settings).ConvertToLocalSettings(OriginBaseDirPath, DestBaseDirPath);
            var saveTask = Settings.SaveAsync();
            if (Settings.IsUseDatabase)
            {
                Database = await LoadOrCreateDatabaseAsync();
            }
            else
                Database = null;

            Results.SuccessfulFiles = new HashSet<string>();
            Results.SuccessfulDirectories = new HashSet<string>();
            Results.FailedFiles = new HashSet<string>();
            Results.FailedDirectories = new HashSet<string>();
            if (Settings.ComparisonMethod != ComparisonMethod.NoComparison)
                Results.UnchangedFiles = new HashSet<string>();
            if (Settings.IsEnableDeletion)
            {
                Results.DeletedFiles = new HashSet<string>();
                Results.DeletedDirectories = new HashSet<string>();
            }

            await saveTask;
        }

        public static string GetQualifiedDirectoryPath(string directoryPath)
        {
            var s = directoryPath.Trim();
            return Path.GetFullPath(s.EndsWith(Path.DirectorySeparatorChar) ? s : s + Path.DirectorySeparatorChar);
        }

        private Task<BackupDatabase?> LoadOrCreateDatabaseAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    _sqliteStore = SqliteBackupStateStore.OpenOrMigrate(OriginBaseDirPath, DestBaseDirPath);
                    Logger.Info(Results.Message = $"SQLiteストアを利用: '{SqliteBackupStateStore.GetDatabasePath(OriginBaseDirPath, DestBaseDirPath)}'");
                    return _sqliteStore.ToBackupDatabase();
                }
                catch (Exception e)
                {
                    // SQLiteストアはキャッシュ扱いなので、利用できなくてもバックアップ自体はデータベースなしで続行する。
                    // 空のインメモリデータベースにフォールバックすると、削除同期(DeleteFiles/DeleteDirectories)が
                    // 空の辞書だけを見てバックアップ先の走査をしなくなるため、nullにして非データベースモードの経路に乗せる。
                    Logger.Error(e,
                        Results.Message = "データベースの読み込み失敗: SQLiteストアを利用できないため、今回はデータベースなしで実行します。");
                    try
                    {
                        _sqliteStore?.Dispose();
                    }
                    catch { }

                    _sqliteStore = null;
                    return (BackupDatabase?)null;
                }
            });
        }

        private void CleanUpDatabase()
        {
            Database?.Dispose();
            _sqliteStore?.Dispose();
            _sqliteStore = null;
            Database = null;
        }

        public Task SaveDatabaseAsync()
        {
            if (Settings.IsUseDatabase && _sqliteStore != null)
            {
                _sqliteStore.Flush();
                Logger.Debug("SQLiteストア保存完了: '{0}'", SqliteBackupStateStore.GetDatabasePath(OriginBaseDirPath, DestBaseDirPath));
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// データベースを利用している場合は非同期で現在のデータを保存してからキャンセルする。
        /// </summary>
        public async Task CancelAsync()
        {
            await SaveDatabaseAsync().ConfigureAwait(false);
            if (Cts is null)
                Logger.Info("キャンセル失敗: キャンセル機能が無効です");
            Cts?.Cancel();
            Results.IsSuccess = false;
            Results.IsFinished = true;
        }

        public async Task<BackupResults> StartBackupAsync(CancellationToken cancellationToken = default)
        {
            if (Results.IsFinished)
                throw new InvalidOperationException("Backup already finished.");
            if (_disposedValue)
                throw new ObjectDisposedException(GetType().FullName);
            if (cancellationToken != default)
            {
                Cts?.Dispose();
                Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            var cToken = Cts?.Token ?? default;

            Logger.Info("バックアップ設定:\n{0}", Settings);
            Logger.Info("バックアップを開始'{0}' => '{1}'", OriginBaseDirPath, DestBaseDirPath);

            StartTime = DateTime.Now;
            await InitializeAsync().ConfigureAwait(false);
            using var sqliteBulkWrite = _sqliteStore?.BeginBulkWrite();

            if (!Directory.Exists(OriginBaseDirPath))
            {
                Logger.Warn(Results.Message = $"バックアップ元のディレクトリ'{OriginBaseDirPath}'が見つかりません。");
                Results.IsFinished = true;
                return Results;
            }

            try
            {
                // ディレクトリの処理
                if (Settings.IsUseDatabase && Database != null)
                {
                    Database.BackedUpDirectoriesDict = await Task
                        .Run(
                            () => CopyDirectoryStructure(OriginBaseDirPath, DestBaseDirPath, Results,
                                Settings.IsCopyAttributes, Settings.Regexes, Database.BackedUpDirectoriesDict), cToken)
                        .ConfigureAwait(false) ?? throw new InvalidOperationException();
                }
                else
                {
                    _ = await Task
                        .Run(
                            () => CopyDirectoryStructure(OriginBaseDirPath, DestBaseDirPath, Results,
                                Settings.IsCopyAttributes, Settings.Regexes), cToken)
                        .ConfigureAwait(false);
                }

                // ファイルの処理
                await Task.Run(async () =>
                {
                    foreach (var originFilePath in Settings.SymbolicLink is SymbolicLinkHandling.IgnoreAll or SymbolicLinkHandling.IgnoreOnlyDirectories
                                 ? EnumerateAllFilesIgnoringReparsePoints(OriginBaseDirPath, Settings.Regexes)
                                 : EnumerateAllFiles(OriginBaseDirPath, Settings.Regexes))
                    {
                        var destFilePath = originFilePath.Replace(OriginBaseDirPath, DestBaseDirPath);
                        if (IsIgnoredFile(originFilePath))
                            continue;
                        // 除外パターンと一致せず、バックアップ済みファイルと一致しないファイルをバックアップする
                        // (データベース照会の結果を BackupFile に引き継いで同じキーの再クエリを避ける)
                        bool? existsInDatabase = null;
                        var isUnchanged = Settings.IsUseDatabase
                            ? IsUnchangedFileOnDatabase(originFilePath, destFilePath, out existsInDatabase)
                            : IsUnchangedFileWithoutDatabase(originFilePath, destFilePath);
                        if (!isUnchanged)
                            await Task.Run(() => BackupFile(originFilePath, destFilePath, existsInDatabase), cToken).ConfigureAwait(false);
                    }
                }, cToken).ConfigureAwait(false);

                // ミラーリング処理(バックアップ先ファイルの削除)
                if (Settings.IsEnableDeletion)
                {
                    await Task.Run(DeleteFiles, cToken).ConfigureAwait(false);
                    await Task.Run(DeleteDirectories, cToken).ConfigureAwait(false);
                }

                // 成功判定
                Results.IsSuccess = !Results.FailedDirectories.Any() && !Results.FailedFiles.Any();

                // リトライ処理
                if ((Results.FailedDirectories.Any() || Results.FailedFiles.Any()) && Settings.RetryCount > 0)
                {
                    Logger.Info($"{Settings.RetryWaitMilliSec} ミリ秒毎に {Settings.RetryCount} 回リトライ");
                    await RetryStartAsync(cToken).ConfigureAwait(false);
                }
                else
                    Results.IsFinished = true;
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
                // 実走査での削除同期を含むバックアップが完走したら、DBがバックアップ先を網羅した状態に戻ったとみなす。
                // 削除に失敗したエントリが残っている間は解除しない(DBキー方式に戻るとそれらを再走査しなくなるため)
                if (Results.IsSuccess && Settings.IsEnableDeletion && !_hadDeletionError && _sqliteStore?.NeedsFullScan == true)
                    _sqliteStore.MarkFullScanCompleted();
                // 属性リストアがDBを情報源にできるかは「作成後に一度でも完走したか」で決まるため、
                // 削除同期の設定(FullScanPendingの解除条件)に関わらず完走を記録する
                if (Results.IsSuccess && _sqliteStore is { HasCompletedBackup: false })
                    _sqliteStore.MarkBackupCompleted();
                await SaveDatabaseAsync();
                CleanUpDatabase();
            }

            Results.Message = (Results.IsSuccess ? "バックアップ完了: " : Results.Message + "\nバックアップ失敗: ") + DateTime.Now;
            Logger.Info("{0}\n=============================\n\n", Results.IsSuccess ? "バックアップ完了" : "バックアップ失敗");
            if (Results.SaveFileName is not null)
                await Results.SaveAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return Results;
        }

        private void DeleteDirectories()
        {
            // DBがバックアップ先を網羅している保証がない回(空で作成後、実走査の削除同期がまだ完走していない)は、
            // DBキーではなくバックアップ先の実走査で削除同期する
            if (Settings.IsUseDatabase && Database != null && _sqliteStore?.NeedsFullScan != true)
            {
                foreach (var originDirPath in Database.BackedUpDirectoriesDict.Keys)
                {
                    if (Results.SuccessfulDirectories.Contains(originDirPath) || Results.FailedDirectories.Contains(originDirPath))
                        continue;
                    var destDirPath = originDirPath.Replace(OriginBaseDirPath, DestBaseDirPath);
                    if (!Directory.Exists(destDirPath))
                    {
                        Database.BackedUpDirectoriesDict.Remove(originDirPath);
                        continue;
                    }

                    try
                    {
                        DeleteDirectory(destDirPath);
                        Results.DeletedDirectories?.Add(originDirPath);
                        Database.BackedUpDirectoriesDict.Remove(originDirPath);
                    }
                    catch (Exception e)
                    {
                        _hadDeletionError = true;
                        Logger.Error(e, Results.Message = $"'{destDirPath}'の削除に失敗");
                    }
                }
            }
            else // データベースを使わない
            {
                // 列挙は親が先に来るため、そのまま親を再帰削除すると配下が列挙されず、配下のDB行の除去が漏れる。
                // 削除対象を確定してから深い方(子)から順に処理し、削除したディレクトリごとにDB行も除去する
                var destDirPaths = (Settings.SymbolicLink is SymbolicLinkHandling.IgnoreOnlyDirectories or SymbolicLinkHandling.IgnoreAll
                    ? EnumerateAllDirectoriesIgnoringReparsePoints(DestBaseDirPath, Settings.Regexes,
                        onEnumerationError: OnDeletionEnumerationError)
                    : EnumerateAllDirectories(DestBaseDirPath, Settings.Regexes, onEnumerationError: OnDeletionEnumerationError)).ToList();
                for (var i = destDirPaths.Count - 1; i >= 0; i--)
                {
                    var destDirPath = destDirPaths[i];
                    var originDirPath = destDirPath.Replace(DestBaseDirPath, OriginBaseDirPath);
                    if (Results.SuccessfulDirectories.Contains(originDirPath) || Results.FailedDirectories.Contains(originDirPath))
                        continue;
                    try
                    {
                        DeleteDirectory(destDirPath);
                        Results.DeletedDirectories?.Add(originDirPath);
                        Database?.BackedUpDirectoriesDict.Remove(originDirPath);
                    }
                    catch (Exception e)
                    {
                        _hadDeletionError = true;
                        Logger.Error(e, Results.Message = $"'{destDirPath}'の削除に失敗");
                    }
                }
            }
        }

        /// <summary>
        /// 削除同期の実走査でバックアップ先の一部を列挙できなかった場合に呼ばれる。
        /// 列挙できなかった配下は削除同期を確認できていないため、実走査を完走扱いにしない
        /// (FullScanPendingマーカーを解除させず、次回も実走査を継続させる)。
        /// </summary>
        private void OnDeletionEnumerationError(Exception e) => _hadDeletionError = true;

        private void DeleteDirectory(string directoryPath)
        {
            string revDirPath;
            switch (Settings.Versioning)
            {
                case VersioningMode.PermanentDeletion:
                    Directory.Delete(directoryPath, true);
                    break;
                case VersioningMode.RecycleBin:
                    FileSystem.DeleteDirectory(directoryPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    break;
                case VersioningMode.Replace:
                case VersioningMode.FileTimeStamp:
                    revDirPath = directoryPath.Replace(DestBaseDirPath,
                        Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)));
                    CopyDirectoryAttributes(directoryPath, revDirPath);
                    break;
                case VersioningMode.DirectoryTimeStamp:
                    revDirPath = Path.Combine(Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)),
                        StartTime.ToString("yyyy-MM-dd_HHmmss"), directoryPath.Replace(DestBaseDirPath, null));
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
            var destDirInfo = Directory.CreateDirectory(destDirPath);
            if (!Settings.IsCopyAttributes)
                return;
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

        private void DeleteFiles()
        {
            // DBがバックアップ先を網羅している保証がない回(空で作成後、実走査の削除同期がまだ完走していない)は、
            // DBキーではなくバックアップ先の実走査で削除同期する
            if (Settings.IsUseDatabase && Database != null && _sqliteStore?.NeedsFullScan != true)
            {
                foreach (var originFilePath in Database.BackedUpFilesDict.Keys)
                {
                    if (Results.SuccessfulFiles.Contains(originFilePath) || Results.FailedFiles.Contains(originFilePath) ||
                        (Results.UnchangedFiles?.Contains(originFilePath) ?? false))
                        continue;
                    var destFilePath = originFilePath.Replace(OriginBaseDirPath, DestBaseDirPath);
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
                             ? EnumerateAllFilesIgnoringReparsePoints(DestBaseDirPath, Settings.Regexes,
                                 onEnumerationError: OnDeletionEnumerationError)
                             : EnumerateAllFiles(DestBaseDirPath, Settings.Regexes, onEnumerationError: OnDeletionEnumerationError))
                {
                    var originFilePath = destFilePath.Replace(DestBaseDirPath, OriginBaseDirPath);
                    if (Results.SuccessfulFiles.Contains(originFilePath) || Results.FailedFiles.Contains(originFilePath) ||
                        (Results.UnchangedFiles?.Contains(originFilePath) ?? false))
                        continue;
                    DeleteFile(destFilePath, originFilePath);
                }
            }
        }

        private void DeleteFile(string destFilePath, string originFilePath)
        {
            if (Settings.IsOverwriteReadonly)
                RemoveReadonlyAttribute(destFilePath);
            try
            {
                Logger.Info(Results.Message = $"ファイルを削除: '{destFilePath}'");
                DeleteFile(destFilePath);
                Results.DeletedFiles?.Add(originFilePath);
                Database?.BackedUpFilesDict.Remove(originFilePath);
            }
            catch (Exception e)
            {
                _hadDeletionError = true;
                Logger.Error(e, Results.Message = $"'{destFilePath}'の削除に失敗");
            }
        }

        private void DeleteFile(string filePath)
        {
            string revisionFilePath;
            switch (Settings.Versioning)
            {
                case VersioningMode.PermanentDeletion:
                    File.Delete(filePath);
                    return;
                case VersioningMode.RecycleBin:
                    FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    return;
                case VersioningMode.Replace:
                    revisionFilePath = filePath.Replace(DestBaseDirPath,
                        Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)));
                    break;
                case VersioningMode.DirectoryTimeStamp:
                    revisionFilePath = Path.Combine(Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)),
                        StartTime.ToString("yyyy-MM-dd_HHmmss"), filePath.Replace(DestBaseDirPath, "").TrimStart(Path.DirectorySeparatorChar));
                    break;
                case VersioningMode.FileTimeStamp:
                    revisionFilePath =
                        filePath.Replace(DestBaseDirPath, Settings.RevisionsDirPath ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath))) +
                        StartTime.ToString("_yyyy-MM-dd_HHmmss") + Path.GetExtension(filePath);
                    break;
                default:
                    throw new InvalidOperationException(nameof(Settings.Versioning));
            }

            var removedReadonlyAttributeFromRevisionFile = false;
            FileAttributes? fileAttributes = null;
            if (Settings.IsOverwriteReadonly && File.Exists(revisionFilePath) &&
                (removedReadonlyAttributeFromRevisionFile = RemoveReadonlyAttribute(revisionFilePath)))
                fileAttributes = File.GetAttributes(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(revisionFilePath) ?? throw new NullReferenceException(nameof(Settings.RevisionsDirPath)));
            File.Move(filePath, revisionFilePath, true);
            if (removedReadonlyAttributeFromRevisionFile && fileAttributes.HasValue)
                File.SetAttributes(revisionFilePath, fileAttributes.Value);
        }

        // TODO: これをstaticにしたのは失敗と思われる。RestoreControllerのとは共通化せず、それぞれインスタンスメソッドに書き直す。
        [return: NotNullIfNotNull("backedUpDirectoriesDict")]
        public IDictionary<string, BackedUpDirectoryData>? CopyDirectoryStructure(string sourceBaseDirPath,
            string destBaseDirPath,
            BackupResults results,
            bool isCopyAttributes = true,
            IEnumerable<Regex>? regices = null,
            IDictionary<string, BackedUpDirectoryData>? backedUpDirectoriesDict = null,
            bool isForceCreateDirectoryAndReturnDictionary = false,
            bool isRestoreAttributesFromDatabase = false,
            SymbolicLinkHandling symbolicLink = SymbolicLinkHandling.IgnoreOnlyDirectories,
            VersioningMode versioning = VersioningMode.PermanentDeletion)
        {
            if (isRestoreAttributesFromDatabase && backedUpDirectoriesDict == null)
                throw new ArgumentNullException(nameof(backedUpDirectoriesDict));
            Logger.Info(results.Message = "ディレクトリ構造をコピー");
            return (symbolicLink is SymbolicLinkHandling.IgnoreOnlyDirectories or SymbolicLinkHandling.IgnoreAll
                ? EnumerateAllDirectoriesIgnoringReparsePoints(sourceBaseDirPath, regices)
                : EnumerateAllDirectories(sourceBaseDirPath, regices)).Aggregate(backedUpDirectoriesDict,
                (current, originDirPath) => CopyDirectory(originDirPath,
                    sourceBaseDirPath, destBaseDirPath, results,
                    isCopyAttributes, current,
                    isForceCreateDirectoryAndReturnDictionary,
                    isRestoreAttributesFromDatabase,
                    symbolicLink, versioning));
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
                UseShellExecute = false,
            };
            var linkType = RunProcess(startInfo);
            startInfo.Arguments = getItemArg + sourcePath + @" | Select-Object -ExpandProperty Target";
            if (linkType is junction or symbolicLink)
            {
                var target = RunProcess(startInfo);
                if (string.IsNullOrEmpty(target))
                    throw new IOException($"'{sourcePath}'のTargetを取得できません。");
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
            else
                throw new IOException($"'{sourcePath}'のLinkType({linkType})を識別できません。");

            static string? RunProcess(ProcessStartInfo startInfo)
            {
                using var proc = Process.Start(startInfo);
                proc?.WaitForExit();
                return proc?.StandardOutput.ReadToEnd().Trim('\n', '\r', ' ');
            }
        }

        private IDictionary<string, BackedUpDirectoryData>? CopyDirectory(string originDirPath,
            string sourceBaseDirPath,
            string destBaseDirPath,
            BackupResults results,
            bool isCopyAttributes = true,
            IDictionary<string, BackedUpDirectoryData>? backedUpDirectoriesDict = null,
            bool isForceCreateDirectoryAndReturnDictionary = false,
            bool isRestoreAttributesFromDatabase = false,
            SymbolicLinkHandling symbolicLinkHandling = SymbolicLinkHandling.IgnoreOnlyDirectories,
            VersioningMode versioning = VersioningMode.PermanentDeletion)
        {
            try
            {
                var originDirInfo = isCopyAttributes ? new DirectoryInfo(originDirPath) : null;
                DirectoryInfo? destDirInfo = null;
                // シンボリックリンクをバックアップ先に再現する場合
                if (symbolicLinkHandling == SymbolicLinkHandling.Direct &&
                    (originDirInfo ??= new DirectoryInfo(originDirPath)).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
                    if (versioning != VersioningMode.PermanentDeletion &&
                        (destDirInfo = new DirectoryInfo(destDirPath)).Attributes.HasFlag(FileAttributes.ReparsePoint))
                        DeleteDirectory(destDirPath);
                    CopyReparsePoint(originDirPath, destDirPath, versioning == VersioningMode.PermanentDeletion);
                }
                // データベースのデータを使わない場合
                else if (backedUpDirectoriesDict is null || !backedUpDirectoriesDict.TryGetValue(originDirPath, out var data) ||
                         isForceCreateDirectoryAndReturnDictionary && !isRestoreAttributesFromDatabase || originDirPath == sourceBaseDirPath)
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
                        if (originDirInfo!.CreationTime != data.CreationTime)
                            (destDirInfo = Directory.CreateDirectory(destDirPath)).CreationTime = originDirInfo.CreationTime;
                        if (originDirInfo.LastWriteTime != data.LastWriteTime)
                            (destDirInfo ??= Directory.CreateDirectory(destDirPath)).LastWriteTime = originDirInfo.LastWriteTime;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Warn($"'{destDirPath}'のCreationTime/LastWriteTimeを変更できません");
                    }

                    if (originDirInfo!.Attributes != data.FileAttributes)
                        (destDirInfo ?? Directory.CreateDirectory(destDirPath)).Attributes = originDirInfo.Attributes;
                }

                results.SuccessfulDirectories.Add(originDirPath);
                results.FailedDirectories.Remove(originDirPath);
                if (isForceCreateDirectoryAndReturnDictionary && backedUpDirectoriesDict == null)
                    backedUpDirectoriesDict = new Dictionary<string, BackedUpDirectoryData>();
                if (backedUpDirectoriesDict != null && !isRestoreAttributesFromDatabase)
                {
                    backedUpDirectoriesDict[originDirPath] =
                        new BackedUpDirectoryData(originDirInfo?.CreationTime, originDirInfo?.LastWriteTime, originDirInfo?.Attributes);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, results.Message = $"'{originDirPath}' => '{originDirPath.Replace(sourceBaseDirPath, destBaseDirPath)}'のコピーに失敗");
                results.FailedDirectories.Add(originDirPath);
            }

            return backedUpDirectoriesDict;
        }

        private bool IsIgnoredFile(string originFilePath)
        {
            if (Settings.SymbolicLink == SymbolicLinkHandling.IgnoreAll)
            {
                if (File.GetAttributes(originFilePath).HasFlag(FileAttributes.ReparsePoint))
                    return true;
            }

            // 除外パターンとマッチング
            if (Settings.Regexes == null)
                return false;
            var s = originFilePath[(OriginBaseDirPath.Length - 1)..];
            return Settings.Regexes.Any(r => r.IsMatch(s));
        }

        /// <summary>
        /// データベースにデータが記録されている場合はバックアップ先ファイルにアクセスしない(比較に必要なデータが無い場合はアクセスしに行く)
        /// </summary>
        /// <param name="existsInDatabase">対象ファイルのデータがデータベースに記録されているかどうか(データベースを参照しなかった場合は null)</param>
        /// <returns>前回のバックアップから変更されていないことが確認できたら true</returns>
        private bool IsUnchangedFileOnDatabase(string originFilePath, string destFilePath, out bool? existsInDatabase)
        {
            existsInDatabase = null;
            if (Database is null)
                return IsUnchangedFileWithoutDatabase(originFilePath, destFilePath);
            if (!Database.BackedUpFilesDict.TryGetValue(originFilePath, out var destFileData))
            {
                existsInDatabase = false;
                // 再構築が完了していないDBでは「記録が無いだけ」の可能性があるため、実ファイル比較で確かめ、
                // 未変更なら再コピーせずに現在のメタデータをDBへ書き戻して再構築を進める
                if (_sqliteStore?.NeedsFullScan == true && IsUnchangedFileWithoutDatabase(originFilePath, destFilePath))
                {
                    UpdateFileDataOnDatabase(originFilePath);
                    return true;
                }

                return false;
            }

            existsInDatabase = true;
            if (Settings.ComparisonMethod == ComparisonMethod.NoComparison)
                return false;
            FileInfo? originFileInfo = null;

            // Archive属性
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute))
            {
                if ((originFileInfo = new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                    return false;
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

                    destFileData.LastWriteTime = File.GetLastWriteTime(destFilePath);
                    Database.BackedUpFilesDict[originFilePath] = destFileData;
                    Logger.Warn("データベースに更新日時が記録されていません。バックアップ先の更新日時を記録します。: '{0}'", destFileData.LastWriteTime);
                }

                if ((originFileInfo?.LastWriteTime ?? (originFileInfo = new FileInfo(originFilePath)).LastWriteTime) != destFileData.LastWriteTime)
                    return false;
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
                    return false;
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
                    return false;
            }

            // 生データ(データベースを用いず比較)
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsBinary))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }

                if (Settings.CompressionLevel != CompressionLevel.NoCompression)
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
                using var originStream = originFileInfo?.OpenRead() ??
                                         new FileStream(originFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var destStream = new FileStream(destFilePath, FileMode.Open, FileAccess.Read);
                do
                {
                    originByte = originStream.ReadByte();
                    destByte = destStream.ReadByte();
                } while (originByte == destByte && originByte != -1);

                if (originByte != destByte)
                    return false;
            }

            // 相違が検出されなかった
            Logger.Info("ファイルをスキップ(バックアップ済み): '{0}'", originFilePath);
            Results.UnchangedFiles?.Add(originFilePath);
            return true;
        }

        /// <summary>
        /// データベースを使わず、実際にファイルを比較する
        /// </summary>
        /// <returns>前回のバックアップから変更されていないことが確認できたら true</returns>
        private bool IsUnchangedFileWithoutDatabase(string originFilePath, string destFilePath)
        {
            if (!File.Exists(destFilePath))
                return false;
            if (Settings.ComparisonMethod == ComparisonMethod.NoComparison)
                return false;
            if (!Settings.IsCopyAttributes)
                Logger.Warn("ファイルを正しく比較出来ません: ファイル属性のコピーが無効になっています。ファイルを比較するにはデータベースを利用するか、ファイル属性のコピーを有効にしてください。");
            FileInfo? destFileInfo = null;
            FileInfo? originFileInfo = null;

            // Archive属性
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute))
            {
                if ((originFileInfo = new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                    return false;
            }

            // 更新日時
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime))
            {
                if ((originFileInfo?.LastWriteTime ?? (originFileInfo = new FileInfo(originFilePath)).LastWriteTime) !=
                    (destFileInfo = new FileInfo(destFilePath)).LastWriteTime)
                    return false;
            }

            // サイズ
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.Size))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("ファイルサイズの比較ができません: 暗号化有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
                    return false;
                }

                if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Warn("ファイルサイズの比較ができません: 圧縮有効時にデータベースを利用せずサイズを比較することはできません。データベースを利用してください。");
                    return false;
                }

                if ((originFileInfo?.Length ?? (originFileInfo = new FileInfo(originFilePath)).Length) !=
                    (destFileInfo?.Length ?? (destFileInfo = new FileInfo(destFilePath)).Length))
                    return false;
            }

            // SHA1
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("ハッシュの比較ができません: 暗号化有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }

                if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Warn("ハッシュの比較ができません: 圧縮有効時にデータベースを利用せずハッシュを比較することはできません。データベースを利用してください。");
                    return false;
                }

                Logger.Warn("データベースを利用しない場合、ハッシュでの比較は非効率的です。データベースを利用するか、生データでの比較を検討してください。");
                Logger.Debug(Results.Message = $"SHA1で比較: '{originFilePath}' = '{destFilePath}'");
                if (BackupManager.ComputeFileSHA1(originFilePath) != BackupManager.ComputeFileSHA1(destFilePath))
                    return false;
            }

            // 生データ
            if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsBinary))
            {
                if (AesCryptor != null)
                {
                    Logger.Warn("生データの比較ができません: 暗号化有効時に生データを比較することはできません。");
                    return false;
                }

                if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                {
                    Logger.Warn("生データの比較ができません: 圧縮有効時に生データを比較することはできません。");
                    return false;
                }

                Logger.Debug(Results.Message = $"生データの比較: '{originFilePath}' = '{destFilePath}'");
                int originByte;
                int destByte;
                using var originStream = originFileInfo?.OpenRead() ??
                                         new FileStream(originFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var destStream = destFileInfo?.OpenRead() ?? new FileStream(destFilePath, FileMode.Open, FileAccess.Read);
                do
                {
                    originByte = originStream.ReadByte();
                    destByte = destStream.ReadByte();
                } while (originByte == destByte && originByte != -1);

                if (originByte != destByte)
                    return false;
            }

            // 相違が検出されなかった
            Logger.Info("ファイルをスキップ(バックアップ済み): '{0}'", originFilePath);
            Results.UnchangedFiles?.Add(originFilePath);
            return true;
        }

        /// <summary>
        /// 指定の属性を持っていないとデータベースに記録されているファイルかどうか。
        /// </summary>
        /// <returns>前回のバックアップ時に指定の属性がなかった場合 true, 対象のファイルが指定の属性を持っていたり、データが無い場合は false</returns>
        private bool IsNotAttributesInDatabase(string originFilePath, FileAttributes fileAttributes)
        {
            if (Database is null)
                return false;
            return Database.BackedUpFilesDict.TryGetValue(originFilePath, out var data) && data.FileAttributes.HasValue &&
                   !data.FileAttributes.Value.HasFlag(fileAttributes);
        }

        /// <param name="existsInDatabase">対象ファイルのデータがデータベースに記録されているかどうか(呼び出し元が未照会の場合は null)</param>
        private void BackupFile(string originFilePath, string destFilePath, bool? existsInDatabase = null)
        {
            Logger.Info(Results.Message = $"ファイルをバックアップ: '{originFilePath}' => '{destFilePath}'");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destFilePath) ?? throw new ArgumentException("ディレクトリ名を取得できません。", nameof(destFilePath)));
                if (Settings.IsOverwriteReadonly)
                {
                    RemoveReadonlyAttribute(originFilePath, destFilePath);
                    RemoveHiddenAttribute(originFilePath, destFilePath);
                }

                // 再構築が完了していないDBは版管理の存在判定でも信用しない。
                // 通常時はDB行か実ファイルのどちらかが存在する場合に退避し、再構築未完了の回は実ファイルがある場合だけ退避する。
                var existsForVersioning = _sqliteStore is { NeedsFullScan: true }
                    ? File.Exists(destFilePath)
                    : (existsInDatabase ?? Database?.BackedUpFilesDict.ContainsKey(originFilePath) ?? false) || File.Exists(destFilePath);
                if (Settings.Versioning != VersioningMode.PermanentDeletion && existsForVersioning)
                {
                    try
                    {
                        DeleteFile(destFilePath);
                    }
                    catch (Exception e) when (e is NullReferenceException or InvalidOperationException)
                    {
                        Logger.Warn(e, Results.Message = "バージョン管理設定が正しくありません。");
                        throw;
                    }
                }

                if (Settings.SymbolicLink == SymbolicLinkHandling.Direct && File.GetAttributes(originFilePath).HasFlag(FileAttributes.ReparsePoint))
                {
                    CopyReparsePoint(originFilePath, destFilePath, Settings.Versioning == VersioningMode.PermanentDeletion);
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
                Results.FailedFiles.Add(originFilePath);
            }
        }

        /// <summary>
        /// <paramref name="destFilePath" /> が読み取り専用なら解除する(読み取り専用でないとデータベースに記録されているファイルの場合は確かめない)
        /// </summary>
        /// <remarks>
        /// IsNotReadOnlyInDatabase() の戻り値が true かつバックアップ先ファイルが読み取り専用属性を持つ場合は解除されないので、後で<see cref="UnauthorizedAccessException" />になる
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
            if (!fi.Exists || !fi.Attributes.HasFlag(FileAttributes.ReadOnly))
                return false;
            fi.Attributes = RemoveAttribute(fi.Attributes, FileAttributes.ReadOnly);
            return true;
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
            if (!fi.Exists || !fi.Attributes.HasFlag(FileAttributes.Hidden))
                return false;
            fi.Attributes = RemoveAttribute(fi.Attributes, FileAttributes.Hidden);
            fi.Refresh();
            return true;
        }

        private void CopyFileAttributesAndUpdateDatabase(string originFilePath, string destFilePath)
        {
            FileInfo? originInfo = null;
            FileInfo? destInfo = null;
            if (Settings.IsCopyAttributes)
            {
                if (Settings.IsUseDatabase && Database != null && Database.BackedUpFilesDict.TryGetValue(originFilePath, out var data))
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
                        if (Settings.IsOverwriteReadonly && (data.FileAttributes.Value.HasFlag(FileAttributes.ReadOnly) ||
                                                             data.FileAttributes.Value.HasFlag(FileAttributes.Hidden)))
                            (destInfo ?? new FileInfo(destFilePath)).Attributes = data.FileAttributes.Value;
                    }
                }
                else
                {
                    Logger.Debug("属性をコピー");
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
                if (Settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute) &&
                    (originInfo ??= new FileInfo(originFilePath)).Attributes.HasFlag(FileAttributes.Archive))
                    (originInfo ??= new FileInfo(originFilePath)).Attributes = RemoveAttribute(originInfo.Attributes, FileAttributes.Archive);
            }

            UpdateFileDataOnDatabase(originFilePath, originInfo);

            Results.SuccessfulFiles.Add(originFilePath);
            Results.FailedFiles.Remove(originFilePath);
        }

        /// <summary>
        /// 現在のバックアップ元ファイルのメタデータを設定に応じてデータベースへ記録する
        /// </summary>
        private void UpdateFileDataOnDatabase(string originFilePath, FileInfo? originInfo = null)
        {
            if (!Settings.IsUseDatabase || Database is null)
                return;
            // 必要なデータだけを保存
            Database.BackedUpFilesDict[originFilePath] = new BackedUpFileData(
                Settings.IsCopyAttributes ? (originInfo ??= new FileInfo(originFilePath)).CreationTime : null,
                Settings.IsCopyAttributes || Settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime)
                    ? (originInfo ??= new FileInfo(originFilePath)).LastWriteTime
                    : null,
                Settings.ComparisonMethod.HasFlag(ComparisonMethod.Size)
                    ? (originInfo ??= new FileInfo(originFilePath)).Length
                    : BackedUpFileData.DefaultSize,
                Settings.IsCopyAttributes || Settings.ComparisonMethod != ComparisonMethod.NoComparison
                    ? (originInfo ?? new FileInfo(originFilePath)).Attributes
                    : null,
                Settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1) ? BackupManager.ComputeFileSHA1(originFilePath) : null
            );
        }

        private async Task RetryStartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_currentRetryCount >= Settings.RetryCount)
            {
                Results.IsFinished = true;
                return;
            }

            Logger.Debug(Results.Message = $"リトライ待機中...({_currentRetryCount + 1}/{Settings.RetryCount}回目)");

            await Task.Delay(Settings.RetryWaitMilliSec, cancellationToken);

            _currentRetryCount++;
            Logger.Info(Results.Message = $"リトライ {_currentRetryCount}/{Settings.RetryCount} 回目");
            if (Results.FailedDirectories.Any())
            {
                foreach (var originDirPath in Results.FailedDirectories.ToArray())
                    if (Settings.IsUseDatabase && Database != null)
                    {
                        Database.BackedUpDirectoriesDict =
                            await Task.Run(
                                () => CopyDirectory(originDirPath, OriginBaseDirPath, DestBaseDirPath, Results, Settings.IsCopyAttributes,
                                    Database.BackedUpDirectoriesDict), cancellationToken) ?? Database.BackedUpDirectoriesDict;
                    }
                    else
                    {
                        _ = await Task.Run(() => CopyDirectory(originDirPath, OriginBaseDirPath, DestBaseDirPath, Results, Settings.IsCopyAttributes),
                            cancellationToken);
                    }
            }

            if (Results.FailedFiles.Any())
            {
                foreach (var originFilePath in Results.FailedFiles.ToArray())
                {
                    var destFilePath = originFilePath.Replace(OriginBaseDirPath, DestBaseDirPath);
                    await Task.Run(() => BackupFile(originFilePath, destFilePath), cancellationToken);
                }
            }

            Results.IsSuccess = !Results.FailedDirectories.Any() && !Results.FailedFiles.Any();

            if (Results.IsSuccess)
                Results.IsFinished = true;
            else
                await RetryStartAsync(cancellationToken);
        }

        private static FileAttributes RemoveAttribute(FileAttributes attributes, FileAttributes attributesToRemove) => attributes & ~attributesToRemove;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
                return;
            if (disposing)
            {
                Cts?.Dispose();
                AesCryptor?.Dispose();
                Database?.Dispose();
                _sqliteStore?.Dispose();
                // Settingsは借り物なので勝手にDisposeしない
            }

            _disposedValue = true;
        }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
