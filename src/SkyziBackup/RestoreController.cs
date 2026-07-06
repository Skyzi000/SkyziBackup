using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NLog;
using Skyzi000;
using Skyzi000.Cryptography;
using SkyziBackup.Data;
using static Skyzi000.IO.FileSystem;

namespace SkyziBackup
{
    public class RestoreController : IDisposable
    {
        public BackupResults Results { get; } = new(false);
        public CompressiveAesCryptor? AesCryptor { get; set; }
        public BackupSettings Settings { get; set; }
        public BackupDatabase? Database { get; }
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _sourceBaseDirPath;
        private readonly string _destBaseDirPath;
        private readonly bool _isRestoreAttributesFromDatabase;
        private readonly bool _isCopyOnlyFileAttributes;
        private readonly bool _isEnableWriteDatabase;
        private SqliteBackupStateStore? _sqliteStore;
        private bool _disposedValue;

        public RestoreController(string sourceDirPath,
            string destDirPath,
            string? password = null,
            BackupSettings? settings = null,
            bool isCopyAttributesOnDatabase = false,
            bool isCopyOnlyFileAttributes = false,
            bool isEnableWriteDatabase = false)
        {
            _sourceBaseDirPath = BackupController.GetQualifiedDirectoryPath(sourceDirPath);
            _destBaseDirPath = BackupController.GetQualifiedDirectoryPath(destDirPath);
            Settings = settings ?? BackupSettings.LoadLocalSettings(_destBaseDirPath, _sourceBaseDirPath) ?? BackupSettings.Default;
            //if (Settings.isUseDatabase && Settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1))
            // TODO: データベースに記録されたSHA1と比較できるようにする
            _isEnableWriteDatabase = isEnableWriteDatabase;
            if (isCopyAttributesOnDatabase || _isEnableWriteDatabase)
                Database = LoadOrCreateDatabase(_isEnableWriteDatabase);
            if (isCopyAttributesOnDatabase && Database == null)
                Logger.Warn("データベースから属性をリストアできません: データベースが見つからないか読み込めません。");
            else if (isCopyAttributesOnDatabase && _sqliteStore?.WasRecreatedAfterQuarantine == true)
                Logger.Warn("以前のデータベース状態が失われているため、データベースからの属性リストアは不完全な可能性があります。");
            _isRestoreAttributesFromDatabase = isCopyAttributesOnDatabase && Database != null;

            if (!string.IsNullOrEmpty(password))
                AesCryptor = new CompressiveAesCryptor(password, compressionLevel: Settings.CompressionLevel, compressAlgorithm: Settings.CompressAlgorithm);
            Results.Finished += Results_Finished;
            _isCopyOnlyFileAttributes = isCopyOnlyFileAttributes;
        }

        private BackupDatabase? LoadOrCreateDatabase(bool createIfMissing)
        {
            var jsonPath = BackupDatabase.GetDatabasePath(_destBaseDirPath, _sourceBaseDirPath);
            var hasDatabase = SqliteBackupStateStore.Exists(_destBaseDirPath, _sourceBaseDirPath) || File.Exists(jsonPath);
            if (!hasDatabase && !createIfMissing)
                return null;

            try
            {
                _sqliteStore = SqliteBackupStateStore.OpenOrMigrate(_destBaseDirPath, _sourceBaseDirPath);
                Logger.Info("SQLiteストアを利用: '{0}'", SqliteBackupStateStore.GetDatabasePath(_destBaseDirPath, _sourceBaseDirPath));
                return _sqliteStore.ToBackupDatabase();
            }
            catch (Exception e)
            {
                try
                {
                    _sqliteStore?.Dispose();
                }
                catch { }

                _sqliteStore = null;
                // SQLiteストアはキャッシュ扱いなので、利用できなくてもリストア自体はデータベースなしで続行する。
                // 空のインメモリデータベースにフォールバックすると、属性復元(CopyOnlyFileAttributes)が
                // 空の辞書を列挙して何もせず成功扱いになるため、nullにして非データベースモードの経路に乗せる。
                if (createIfMissing)
                    Logger.Error(e, "SQLiteストアの初期化またはJSONからの移行に失敗: 今回はデータベースなしで実行します。(実行結果は保存されません)");
                else
                    Logger.Warn(e, "データベースから属性をリストアできません: SQLiteストアの初期化またはJSONからの移行に失敗");
                return null;
            }
        }

        public BackupResults StartRestore()
        {
            if (Results.IsFinished)
                throw new NotImplementedException("現在、このクラスのインスタンスは再利用されることを想定していません。");
            if (_disposedValue)
                throw new ObjectDisposedException(GetType().FullName);
            Logger.Info("バックアップ設定:\n{0}", Settings);
            Logger.Info(@"リストア設定:
データベースからファイル属性をリストアする: {0}
ファイル属性だけをコピーする: {1}
ファイル属性をデータベースに保存する: {2}",
                _isRestoreAttributesFromDatabase, _isCopyOnlyFileAttributes, _isEnableWriteDatabase);
            Logger.Info("リストアを開始'{0}' => '{1}'", _sourceBaseDirPath, _destBaseDirPath);

            Results.SuccessfulFiles = new HashSet<string>();
            Results.SuccessfulDirectories = new HashSet<string>();
            Results.FailedFiles = new HashSet<string>();
            Results.FailedDirectories = new HashSet<string>();
            using var sqliteBulkWrite = _isEnableWriteDatabase ? _sqliteStore?.BeginBulkWrite() : null;

            if (_isCopyOnlyFileAttributes)
                return CopyOnlyFileAttributes();

            if (!Directory.Exists(_sourceBaseDirPath) || Directory.Exists(_destBaseDirPath) && Directory.EnumerateFileSystemEntries(_destBaseDirPath).Any())
            {
                Logger.Error(Results.Message = !Directory.Exists(_sourceBaseDirPath)
                    ? $"リストアを中止: リストア元のディレクトリ'{_sourceBaseDirPath}'が見つかりません。"
                    : $"リストアを中止: リストア先のディレクトリ'{_destBaseDirPath}'が空ではありません。");
                Results.IsFinished = true;
                return Results;
            }

            if (_isEnableWriteDatabase && Database != null)
            {
                Database.BackedUpDirectoriesDict = CopyDirectoryStructure(_sourceBaseDirPath,
                    _destBaseDirPath,
                    Results,
                    Settings.IsCopyAttributes,
                    Database.BackedUpDirectoriesDict,
                    true,
                    _isRestoreAttributesFromDatabase);
            }
            else
            {
                CopyDirectoryStructure(_sourceBaseDirPath,
                    _destBaseDirPath,
                    Results,
                    Settings.IsCopyAttributes,
                    Database?.BackedUpDirectoriesDict,
                    isRestoreAttributesFromDatabase: _isRestoreAttributesFromDatabase);
            }

            foreach (var originFilePath in EnumerateAllFilesIgnoringReparsePoints(_sourceBaseDirPath))
            {
                var destFilePath = originFilePath.Replace(_sourceBaseDirPath, _destBaseDirPath);
                RestoreFile(originFilePath, destFilePath);
            }

            Results.IsSuccess = !Results.FailedFiles.Any() && !Results.FailedDirectories.Any();
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
                        if (_isEnableWriteDatabase && Database != null) // この条件ならCopyFileAttributesはnullを返さないはず
                            Database.BackedUpFilesDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath)!;
                        else
                            CopyFileAttributes(originFilePath, destFilePath);
                    }

                    Results.SuccessfulFiles.Add(originFilePath);
                    Results.FailedFiles.Remove(originFilePath);
                }
                else
                {
                    // 暗号化されていないファイルの復元
                    using (var origin = new FileStream(originFilePath, FileMode.Open, FileAccess.Read))
                    {
                        using (var dest = new FileStream(destFilePath, FileMode.Create, FileAccess.ReadWrite))
                        {
                            origin.CopyWithCompressionTo(dest, Settings.CompressionLevel, CompressionMode.Decompress, Settings.CompressAlgorithm);
                        }
                    }

                    if (Settings.IsCopyAttributes)
                    {
                        if (_isEnableWriteDatabase && Database != null)
                            Database.BackedUpFilesDict[originFilePath] = CopyFileAttributes(originFilePath, destFilePath)!;
                        else
                            CopyFileAttributes(originFilePath, destFilePath);
                    }

                    Results.SuccessfulFiles.Add(originFilePath);
                    Results.FailedFiles.Remove(originFilePath);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Logger.Error(Results.Message = $"アクセスが拒否されました '{originFilePath}' => '{destFilePath}'\n");
                Results.FailedFiles.Add(originFilePath);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originFilePath}' => '{destFilePath}'\n");
                Results.FailedFiles.Add(originFilePath);
            }
        }

        /// <param name="knownFileData">呼び出し元が既に取得済みのデータベース上のデータ(あれば再クエリしない)</param>
        private BackedUpFileData? CopyFileAttributes(string originFilePath, string destFilePath, BackedUpFileData? knownFileData = null)
        {
            FileInfo? originInfo = null;
            if (_isEnableWriteDatabase && Database != null)
            {
                Logger.Info("属性をコピー'{0}' => '{1}'", originFilePath, destFilePath);
                // OriginSize/Sha1を保持するため既存データを引き継ぐ(取得済みのデータがあれば再クエリしない)
                var data = knownFileData ?? (Database.BackedUpFilesDict.TryGetValue(originFilePath, out var d) ? d : new BackedUpFileData());
                var _ = new FileInfo(destFilePath)
                {
                    CreationTime = (data.CreationTime = (originInfo = new FileInfo(originFilePath)).CreationTime).Value,
                    LastWriteTime = (data.LastWriteTime = originInfo.LastWriteTime).Value,
                    Attributes = (data.FileAttributes = originInfo.Attributes).Value,
                };
                return data;
            }

            // 取得済みのデータがあれば再クエリしない
            var fileData = knownFileData;
            if (fileData is null && _isRestoreAttributesFromDatabase && Database is not null)
                Database.BackedUpFilesDict.TryGetValue(originFilePath, out fileData);
            if (!_isRestoreAttributesFromDatabase || fileData is null)
            {
                Logger.Info("属性をコピー'{0}' => '{1}'", originFilePath, destFilePath);
                var _ = new FileInfo(destFilePath)
                {
                    CreationTime = (originInfo = new FileInfo(originFilePath)).CreationTime,
                    LastWriteTime = originInfo.LastWriteTime,
                    Attributes = originInfo.Attributes,
                };
            }
            // データベースに記録されたファイル属性をリストアする(もし記録されていないものがあれば実際のファイルを参照する)
            else
            {
                Logger.Info("データベースからファイル属性をリストア '{0}'", destFilePath);
                var _ = new FileInfo(destFilePath)
                {
                    CreationTime = fileData.CreationTime ?? (originInfo = new FileInfo(originFilePath)).CreationTime,
                    LastWriteTime = fileData.LastWriteTime ?? (originInfo ??= new FileInfo(originFilePath)).LastWriteTime,
                    Attributes = fileData.FileAttributes ?? (originInfo ?? new FileInfo(originFilePath)).Attributes,
                };
            }

            return null;
        }

        private BackupResults CopyOnlyFileAttributes()
        {
            if (!Settings.IsCopyAttributes)
            {
                Logger.Warn(Results.Message = "リストアを中止: ファイル属性をコピーしない設定になっています。");
                Results.IsFinished = true;
                return Results;
            }

            if (!_isRestoreAttributesFromDatabase && !Directory.Exists(_sourceBaseDirPath))
            {
                Logger.Error(Results.Message = $"リストアを中止: リストア元のディレクトリ'{_sourceBaseDirPath}'が見つかりません。");
                Results.IsFinished = true;
                return Results;
            }

            if (_isRestoreAttributesFromDatabase && Database != null)
            {
                var newDirDict = _isEnableWriteDatabase ? new Dictionary<string, BackedUpDirectoryData>() : null;
                var newFileDict = _isEnableWriteDatabase ? new Dictionary<string, BackedUpFileData>() : null;
                // 列挙一回分のスキャンで済ませる(キー列挙+キー毎の再取得はSQLiteバッキング時にN+1クエリになる)
                foreach (var (originDirPath, dirData) in Database.BackedUpDirectoriesDict)
                {
                    var destDirPath = originDirPath.Replace(_sourceBaseDirPath, _destBaseDirPath);
                    if (!Directory.Exists(destDirPath))
                    {
                        Logger.Warn("コピー先のディレクトリが見つかりません: {0}", destDirPath);
                        Results.FailedFiles.Add(originDirPath);
                        continue;
                    }

                    DirectoryInfo? originInfo = null;
                    try
                    {
                        if (_isEnableWriteDatabase) // newDirDict は null ではない
                        {
                            // 全フィールドを実際のディレクトリの値で上書きするため、既存データを取得せず直接構築する
                            originInfo = new DirectoryInfo(originDirPath);
                            var _ = new DirectoryInfo(destDirPath)
                            {
                                CreationTime = originInfo.CreationTime,
                                LastWriteTime = originInfo.LastWriteTime,
                                Attributes = originInfo.Attributes,
                            };
                            newDirDict![originDirPath] = new BackedUpDirectoryData(originInfo.CreationTime, originInfo.LastWriteTime, originInfo.Attributes);
                        }
                        else
                        {
                            // データベースに記録されたディレクトリ属性をコピーする(もし記録されていないものがあれば実際のディレクトリを参照する)
                            var _ = new DirectoryInfo(destDirPath)
                            {
                                CreationTime = dirData.CreationTime ?? (originInfo = new DirectoryInfo(originDirPath)).CreationTime,
                                LastWriteTime = dirData.LastWriteTime ?? (originInfo ??= new DirectoryInfo(originDirPath)).LastWriteTime,
                                Attributes = dirData.FileAttributes ?? (originInfo ?? new DirectoryInfo(originDirPath)).Attributes,
                            };
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originDirPath}' => '{destDirPath}'");
                        Results.FailedFiles.Add(originDirPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originDirPath}' => '{destDirPath}'\n");
                        Results.FailedFiles.Add(originDirPath);
                    }
                }

                // 列挙一回分のスキャンで済ませ、取得済みのデータを CopyFileAttributes に渡して再クエリを避ける
                foreach (var (originFilePath, fileData) in Database.BackedUpFilesDict)
                {
                    var destFilePath = originFilePath.Replace(_sourceBaseDirPath, _destBaseDirPath);
                    if (!File.Exists(destFilePath))
                    {
                        Logger.Warn(Results.Message = $"コピー先のファイルが見つかりません '{destFilePath}'");
                        Results.FailedFiles.Add(originFilePath);
                        continue;
                    }

                    try
                    {
                        if (_isEnableWriteDatabase && Database != null) // newFileDict は null ではない
                            newFileDict![originFilePath] = CopyFileAttributes(originFilePath, destFilePath, fileData)!;
                        else
                            CopyFileAttributes(originFilePath, destFilePath, fileData);
                        Results.SuccessfulFiles.Add(originFilePath);
                        Results.FailedFiles.Remove(originFilePath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originFilePath}' => '{destFilePath}'\n");
                        Results.FailedFiles.Add(originFilePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originFilePath}' => '{destFilePath}'\n");
                        Results.FailedFiles.Add(originFilePath);
                    }
                }

                if (_isEnableWriteDatabase && newDirDict != null && newFileDict != null && Database != null)
                {
                    if (_sqliteStore != null)
                    {
                        _sqliteStore.ReplaceState(newDirDict, newFileDict);
                    }
                    else
                    {
                        // SQLiteストアを利用できない場合はインメモリの辞書だけ更新する(永続化はされない)
                        Logger.Warn("SQLiteストアを利用できないため、データベースの変更は保存されません。");
                        Database.BackedUpDirectoriesDict = newDirDict;
                        Database.BackedUpFilesDict = newFileDict;
                    }
                }
            }
            else
            {
                foreach (var originDirPath in EnumerateAllDirectoriesIgnoringReparsePoints(_sourceBaseDirPath))
                {
                    var destDirPath = originDirPath.Replace(_sourceBaseDirPath, _destBaseDirPath);
                    if (!Directory.Exists(destDirPath))
                    {
                        Logger.Warn("コピー先のディレクトリが見つかりません: {0}", destDirPath);
                        Results.FailedFiles.Add(originDirPath);
                        continue;
                    }

                    try
                    {
                        DirectoryInfo originInfo;
                        if (_isEnableWriteDatabase && Database != null)
                        {
                            // 全フィールドを実際のディレクトリの値で上書きするため、既存データを取得せず直接構築する
                            originInfo = new DirectoryInfo(originDirPath);
                            var _ = new DirectoryInfo(destDirPath)
                            {
                                CreationTime = originInfo.CreationTime,
                                LastWriteTime = originInfo.LastWriteTime,
                                Attributes = originInfo.Attributes,
                            };
                            Database.BackedUpDirectoriesDict[originDirPath] =
                                new BackedUpDirectoryData(originInfo.CreationTime, originInfo.LastWriteTime, originInfo.Attributes);
                        }
                        else
                        {
                            var _ = new DirectoryInfo(destDirPath)
                            {
                                CreationTime = (originInfo = new DirectoryInfo(originDirPath)).CreationTime,
                                LastWriteTime = originInfo.LastWriteTime,
                                Attributes = originInfo.Attributes,
                            };
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originDirPath}' => '{destDirPath}'\n");
                        Results.FailedFiles.Add(originDirPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originDirPath}' => '{destDirPath}'\n");
                        Results.FailedFiles.Add(originDirPath);
                    }
                }

                foreach (var originFilePath in EnumerateAllFilesIgnoringReparsePoints(_sourceBaseDirPath))
                {
                    var destFilePath = originFilePath.Replace(_sourceBaseDirPath, _destBaseDirPath);
                    if (!File.Exists(destFilePath))
                    {
                        Logger.Warn("コピー先のファイルが見つかりません: '{0}'", destFilePath);
                        Results.FailedFiles.Add(originFilePath);
                        continue;
                    }

                    try
                    {
                        var data = CopyFileAttributes(originFilePath, destFilePath);
                        if (_isEnableWriteDatabase && Database != null && data != null)
                            Database.BackedUpFilesDict[originFilePath] = data;
                        Results.SuccessfulFiles.Add(originFilePath);
                        Results.FailedFiles.Remove(originFilePath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error(Results.Message = $"アクセスが拒否されました '{originFilePath}' => '{destFilePath}'\n");
                        Results.FailedFiles.Add(originFilePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, Results.Message = $"予期しない例外が発生しました '{originFilePath}' => '{destFilePath}'\n");
                        Results.FailedFiles.Add(originFilePath);
                    }
                }
            }

            Results.IsSuccess = !Results.FailedFiles.Any();
            Results.IsFinished = true;
            return Results;
        }

        [return: NotNullIfNotNull("backedUpDirectoriesDict")]
        public static IDictionary<string, BackedUpDirectoryData>? CopyDirectoryStructure(string sourceBaseDirPath,
            string destBaseDirPath,
            BackupResults results,
            bool isCopyAttributes = true,
            IDictionary<string, BackedUpDirectoryData>? backedUpDirectoriesDict = null,
            bool isForceCreateDirectoryAndReturnDictionary = false,
            bool isRestoreAttributesFromDatabase = false,
            SymbolicLinkHandling symbolicLink = SymbolicLinkHandling.IgnoreOnlyDirectories,
            VersioningMode versioning = VersioningMode.PermanentDeletion)
        {
            if (string.IsNullOrEmpty(sourceBaseDirPath))
                throw new ArgumentException($"'{nameof(sourceBaseDirPath)}' を null または空にすることはできません", nameof(sourceBaseDirPath));

            if (string.IsNullOrEmpty(destBaseDirPath))
                throw new ArgumentException($"'{nameof(destBaseDirPath)}' を null または空にすることはできません", nameof(destBaseDirPath));

            if (isRestoreAttributesFromDatabase && backedUpDirectoriesDict == null)
                throw new ArgumentNullException(nameof(backedUpDirectoriesDict));
            Logger.Info(results.Message = "ディレクトリ構造をコピー");
            return (symbolicLink is SymbolicLinkHandling.IgnoreOnlyDirectories or SymbolicLinkHandling.IgnoreAll
                ? EnumerateAllDirectoriesIgnoringReparsePoints(sourceBaseDirPath)
                : EnumerateAllDirectories(sourceBaseDirPath)).Aggregate(backedUpDirectoriesDict,
                (current, originDirPath) => CopyDirectory(originDirPath,
                    sourceBaseDirPath, destBaseDirPath, results,
                    isCopyAttributes, current,
                    isForceCreateDirectoryAndReturnDictionary,
                    isRestoreAttributesFromDatabase,
                    symbolicLink, versioning));
        }

        private static IDictionary<string, BackedUpDirectoryData>? CopyDirectory(string originDirPath,
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
                    BackupController.CopyReparsePoint(originDirPath, destDirPath);
                }
                // データベースのデータを使わない場合
                else if (backedUpDirectoriesDict is null || !backedUpDirectoriesDict.TryGetValue(originDirPath, out var data) ||
                         isForceCreateDirectoryAndReturnDictionary && !isRestoreAttributesFromDatabase)
                {
                    var destDirPath = originDirPath.Replace(sourceBaseDirPath, destBaseDirPath);
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


        private void Results_Finished(object? sender, EventArgs args)
        {
            DisposeSqliteStore(_isEnableWriteDatabase);

            Results.Message = (Results.IsSuccess ? "リストア完了: " : Results.Message + "\nリストア失敗: ") + DateTime.Now;
            Logger.Info("{0}\n=============================\n\n", Results.IsSuccess ? "リストア完了" : "リストア失敗");
        }

        private void DisposeSqliteStore(bool flush)
        {
            var sqliteStore = _sqliteStore;
            if (sqliteStore == null)
                return;

            try
            {
                if (flush)
                    sqliteStore.Flush();
            }
            finally
            {
                _sqliteStore = null;
                sqliteStore.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
                return;
            try
            {
                if (disposing)
                {
                    Results.Finished -= Results_Finished;
                    try
                    {
                        DisposeSqliteStore(_isEnableWriteDatabase);
                    }
                    finally
                    {
                        AesCryptor?.Dispose();
                        Database?.Dispose();
                        // Settingsは借り物なので勝手にDisposeしない
                    }
                }
            }
            finally
            {
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
}
