using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SkyziBackup
{
    /// <summary>
    /// ファイルの変更を検知する方法
    /// </summary>
    [Flags]
    public enum ComparisonMethod
    {
        /// <summary>
        /// 比較しない
        /// </summary>
        /// <remarks>他の値と同時に指定することはできない</remarks>
        NoComparison = 0,
        /// <summary>
        /// Archive属性による比較
        /// </summary>
        /// <remarks>バックアップ時に元ファイルのArchive属性を変更する点に注意</remarks>
        ArchiveAttribute = 1,
        /// <summary>
        /// 更新日時による比較
        /// </summary>
        WriteTime = 1 << 1,
        /// <summary>
        /// サイズによる比較
        /// </summary>
        Size = 1 << 2,
        /// <summary>
        /// SHA1による比較
        /// </summary>
        FileContentsSHA1 = 1 << 3,
        /// <summary>
        /// 生データによるバイナリ比較
        /// </summary>
        /// <remarks>データベースを利用出来ず、暗号化や圧縮と併用できない点に注意</remarks>
        FileContentsBinary = 1 << 4,
    }

    /// <summary>
    /// 削除・上書き時の動作
    /// </summary>
    public enum VersioningMethod
    {
        /// <summary>
        /// 完全消去する(バージョン管理を行わない)
        /// </summary>
        PermanentDeletion = 0,
        /// <summary>
        /// ゴミ箱に送る(ゴミ箱が利用できない時は完全消去する)
        /// </summary>
        RecycleBin = 1,
        /// <summary>
        /// 指定されたディレクトリにそのまま移動し、既存のファイルを置き換える
        /// </summary>
        Replace = 2,
        /// <summary>
        /// 新規作成されたタイムスタンプ付きのディレクトリ以下に移動する
        /// <code>\YYYY-MM-DD_hhmmss\Directory\hoge.txt</code>
        /// </summary>
        DirectoryTimeStamp = 3,
        /// <summary>
        /// タイムスタンプを追加したファイル名で、指定されたディレクトリに移動する
        /// <code>\Directory\File.txt_YYYY-MM-DD_hhmmss.txt</code>
        /// </summary>
        FileTimeStamp = 4,
    }

    /// <summary>
    /// シンボリックリンクやジャンクション(リパースポイント)の取り扱い
    /// </summary>
    public enum SymbolicLinkHandling
    {
        /// <summary>
        /// リパースポイントのディレクトリを無視して、ファイルへのシンボリックリンクの場合はターゲットの実体をバックアップする(デフォルト動作)
        /// </summary>
        /// <remarks>ファイルの属性を確認しない分ちょっと速いかも</remarks>
        IgnoreOnlyDirectories = 0,
        /// <summary>
        /// ディレクトリだけでなくファイルのシンボリックリンクも無視する
        /// </summary>
        IgnoreAll = 1,
        /// <summary>
        /// ターゲットの実体をバックアップする
        /// </summary>
        /// <remarks>無限ループになる可能性があるので注意</remarks>
        Follow = 2,
        /// <summary>
        /// シンボリックリンク/ジャンクション自体をバックアップ先に可能な限り再現する(ターゲットパスは変更しない)
        /// </summary>
        /// <remarks>ミラーリング機能を有効にしている場合、リンク先の実体が削除される恐れがあるので注意</remarks>
        Direct = 3,
    }

    public class BackupSettings : SaveableData
    {
        private static BackupSettings? _default = null;
        /// <summary>
        /// デフォルト設定
        /// </summary>
        [JsonIgnore]
        public static BackupSettings Default => _default ??= LoadDefaultSettings() ?? new BackupSettings();
        /// <summary>
        /// バックアップ元ディレクトリパス
        /// </summary>
        /// <remarks>デフォルト設定では常にnull。セットするときは<see cref="BackupController.GetQualifiedDirectoryPath"/>が自動的に適用される。</remarks>
        public string? OriginBaseDirPath { get => originBaseDirPath; set => originBaseDirPath = value is null ? null : BackupController.GetQualifiedDirectoryPath(value); }
        private string? originBaseDirPath;
        /// <summary>
        /// バックアップ先ディレクトリパス
        /// </summary>
        /// <remarks>デフォルト設定では常にnull。セットするときは<see cref="BackupController.GetQualifiedDirectoryPath"/>が自動的に適用される。</remarks>
        public string? DestBaseDirPath { get => destBaseDirPath; set => destBaseDirPath = value is null ? null : BackupController.GetQualifiedDirectoryPath(value); }
        private string? destBaseDirPath;
        /// <summary>
        /// データベースを利用する
        /// </summary>
        public bool IsUseDatabase { get; set; }
        /// <summary>
        /// 属性(作成日時や更新日時を含む)をコピーする
        /// </summary>
        public bool IsCopyAttributes { get; set; }
        /// <summary>
        /// 読み取り専用ファイルを上書きする
        /// </summary>
        public bool IsOverwriteReadonly { get; set; }
        /// <summary>
        /// ミラーリングする(バックアップ元ファイルの存在しないバックアップ先ファイルを削除する)
        /// </summary>
        public bool IsEnableDeletion { get; set; }
        /// <summary>
        /// 削除または上書きされたファイルのバージョン管理方法
        /// </summary>
        public VersioningMethod Versioning { get; set; }
        /// <summary>
        /// 削除または上書きされたファイルの移動先ディレクトリ
        /// </summary>
        public string? RevisionsDirPath { get; set; }
        /// <summary>
        /// パスワードを記録する
        /// </summary>
        public bool IsRecordPassword { get; set; }
        /// <summary>
        /// 記録したパスワードのスコープ
        /// </summary>
        public DataProtectionScope PasswordProtectionScope { get; set; }
        /// <summary>
        /// 暗号化されたパスワード(保存用のプロパティ)
        /// </summary>
        [JsonPropertyName("ProtectedPassword")]
        public string? ProtectedPassword { get; set; }
        /// <summary>
        /// リトライ回数
        /// </summary>
        public int RetryCount { get; set; }
        /// <summary>
        /// リトライ待機時間(ミリ秒)
        /// </summary>
        public int RetryWaitMilliSec { get; set; }
        /// <summary>
        /// ファイル比較方法
        /// </summary>
        public ComparisonMethod ComparisonMethod { get; set; }
        /// <summary>
        /// 圧縮レベル
        /// </summary>
        public CompressionLevel CompressionLevel { get; set; }
        /// <summary>
        /// 圧縮アルゴリズム
        /// </summary>
        public CompressAlgorithm CompressAlgorithm { get; set; }
        /// <summary>
        /// バックアップなどの操作をキャンセル可能にする
        /// </summary>
        public bool IsCancelable { get; set; }
        /// <summary>
        /// シンボリックリンクやジャンクション(リパースポイント)の取り扱い
        /// </summary>
        public SymbolicLinkHandling SymbolicLink { get; set; }

        /// <summary>
        /// <see cref="Properties.Settings.AppDataPath"/> から見たローカル設定の相対パス。デフォルト設定の場合は null
        /// </summary>
        [JsonIgnore]
        private string? localFileName => (OriginBaseDirPath is null || DestBaseDirPath is null) ? null : GetLocalSettingsFileName(OriginBaseDirPath, DestBaseDirPath);

        private HashSet<Regex>? regexes;

        /// <summary>
        /// <see cref="IgnorePattern"/> を元に生成した除外用の正規表現セット
        /// </summary>
        [JsonIgnore]
        public HashSet<Regex>? Regexes { get => regexes ?? (string.IsNullOrEmpty(IgnorePattern) ? null : Regexes = PatternToRegices(IgnorePattern)); private set => regexes = value; }
        /// <summary>
        /// バックアップ設定のファイル名
        /// </summary>
        public static readonly string FileName = nameof(BackupSettings) + ".json";
        /// <summary>
        /// Default設定なら true
        /// </summary>
        [JsonIgnore]
        public bool IsDefault => string.IsNullOrEmpty(localFileName);
        /// <summary>
        /// デフォルト設定であれば <see cref="FileName"/> 、そうでなければ <see cref="localFileName"/> 
        /// </summary>
        [JsonIgnore]
        public override string SaveFileName => string.IsNullOrEmpty(localFileName) ? FileName : localFileName;
        /// <summary>
        /// 除外パターン
        /// </summary>
        public string IgnorePattern { get; set; }

        /// <summary>
        /// パスワードを暗号化して記録する
        /// </summary>
        public void SetProtectedPassword(string value) => ProtectedPassword = string.IsNullOrEmpty(value) ? null : PasswordManager.Encrypt(value, PasswordProtectionScope);

        public BackupSettings()
        {
            OriginBaseDirPath = null;
            DestBaseDirPath = null;
            IsUseDatabase = true;
            IsCopyAttributes = true;
            IsOverwriteReadonly = false;
            IsEnableDeletion = false;
            Versioning = VersioningMethod.PermanentDeletion;
            RevisionsDirPath = null;
            IsRecordPassword = true;
            PasswordProtectionScope = DataProtectionScope.LocalMachine;
            ProtectedPassword = null;
            RetryCount = 10;
            RetryWaitMilliSec = 10000;
            ComparisonMethod = ComparisonMethod.WriteTime | ComparisonMethod.Size;
            IgnorePattern = string.Empty;
            CompressionLevel = CompressionLevel.NoCompression;
            CompressAlgorithm = CompressAlgorithm.Deflate;
            IsCancelable = true;
            SymbolicLink = SymbolicLinkHandling.IgnoreOnlyDirectories;
        }
        /// <summary>
        /// ローカル設定用のコンストラクタ
        /// </summary>
        public BackupSettings(string originBaseDirPath, string destBaseDirPath) : this()
        {
            OriginBaseDirPath = originBaseDirPath;
            DestBaseDirPath = destBaseDirPath;
        }
        public BackupSettings(BackupSettings backupSettings) : this()
        {
            OriginBaseDirPath = backupSettings.OriginBaseDirPath;
            DestBaseDirPath = backupSettings.DestBaseDirPath;
            IsUseDatabase = backupSettings.IsUseDatabase;
            IsCopyAttributes = backupSettings.IsCopyAttributes;
            IsOverwriteReadonly = backupSettings.IsOverwriteReadonly;
            IsEnableDeletion = backupSettings.IsEnableDeletion;
            Versioning = backupSettings.Versioning;
            RevisionsDirPath = backupSettings.RevisionsDirPath;
            IsRecordPassword = backupSettings.IsRecordPassword;
            PasswordProtectionScope = backupSettings.PasswordProtectionScope;
            ProtectedPassword = backupSettings.ProtectedPassword;
            RetryCount = backupSettings.RetryCount;
            RetryWaitMilliSec = backupSettings.RetryWaitMilliSec;
            ComparisonMethod = backupSettings.ComparisonMethod;
            IgnorePattern = backupSettings.IgnorePattern;
            CompressionLevel = backupSettings.CompressionLevel;
            CompressAlgorithm = backupSettings.CompressAlgorithm;
            IsCancelable = backupSettings.IsCancelable;
            SymbolicLink = backupSettings.SymbolicLink;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("ローカル設定である---------------: {0}\n", !IsDefault);
            sb.AppendFormat("データベースを利用する-----------: {0}\n", IsUseDatabase);
            sb.AppendFormat("属性をコピーする-----------------: {0}\n", IsCopyAttributes);
            sb.AppendFormat("読み取り専用ファイルを上書きする-: {0}\n", IsOverwriteReadonly);
            sb.AppendFormat("ミラーリングする-----------------: {0}\n", IsEnableDeletion);
            sb.AppendFormat("削除・上書き時のバージョン管理法-: {0}\n", Versioning);
            sb.AppendFormat("パスワードを記録する-------------: {0}\n", IsRecordPassword);
            sb.AppendFormat("記録したパスワードのスコープ-----: {0}\n", PasswordProtectionScope);
            sb.AppendFormat("リトライ回数---------------------: {0}\n", RetryCount);
            sb.AppendFormat("リトライ待機時間(ミリ秒)---------: {0}\n", RetryWaitMilliSec);
            sb.AppendFormat("ファイル比較方法-----------------: {0}\n", ComparisonMethod);
            sb.AppendFormat("圧縮レベル-----------------------: {0}\n", CompressionLevel);
            sb.AppendFormat("圧縮アルゴリズム-----------------: {0}\n", CompressAlgorithm);
            sb.AppendFormat("キャンセル可能か-----------------: {0}\n", IsCancelable);
            sb.AppendFormat("シンボリックリンクの取り扱い-----: {0}\n", SymbolicLink);
            sb.AppendFormat("除外パターン---------------------: \n{0}\n", IgnorePattern);
            return sb.ToString();
        }
        public static string GetLocalSettingsFileName(string originBaseDirPath, string destBaseDirPath) => Path.Combine(DataFileWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName);
        /// <summary>
        /// デフォルト設定をファイルから読み込み直す。読み込めない場合は何もしない。
        /// </summary>
        public static BackupSettings ReloadDefault() => _default = LoadDefaultSettings() ?? Default;
        public static bool TryLoadDefaultSettings([NotNullWhen(true)] out BackupSettings? globalSettings)
        {
            if (File.Exists(DataFileWriter.GetPath(FileName)))
            {
                try
                {
                    globalSettings = DataFileWriter.Read<BackupSettings>(FileName) ?? new BackupSettings();
                    return true;
                }
                catch (Exception) { } // 握りつぶす
            }
            globalSettings = null;
            return false;
        }
        public static BackupSettings? LoadDefaultSettings() => TryLoadDefaultSettings(out BackupSettings? globalSettings) ? globalSettings : null;
        public static bool TryLoadLocalSettings(string originBaseDirPath, string destBaseDirPath, [NotNullWhen(true)] out BackupSettings? localSettings)
        {
            try
            {
                string lfName;
                if (File.Exists(DataFileWriter.GetPath(lfName = GetLocalSettingsFileName(originBaseDirPath, destBaseDirPath))))
                {
                    localSettings = DataFileWriter.Read<BackupSettings>(lfName);
                    if (localSettings is null)
                    {
                        localSettings = new BackupSettings(Default).ConvertToLocalSettings(originBaseDirPath, destBaseDirPath);
                        ReloadDefault();
                    }
                    return true;
                }
            }
            catch (Exception) { } // 握りつぶす
            localSettings = null;
            return false;
        }
        public static BackupSettings? LoadLocalSettings(string originBaseDirPath, string destBaseDirPath) => TryLoadLocalSettings(originBaseDirPath, destBaseDirPath, out BackupSettings? localSettings) ? localSettings : null;
        public BackupSettings ConvertToLocalSettings(string originBaseDirPath, string destBaseDirPath)
        {
            OriginBaseDirPath = originBaseDirPath;
            DestBaseDirPath = destBaseDirPath;
            return this;
        }
        public HashSet<Regex> PatternToRegices(string pattern)
        {
            var patStrArr = pattern.Split(new[] { "\r\n", "\n", "\r", "|" }, StringSplitOptions.None);
            return new HashSet<Regex>(patStrArr.Select(s => ConvertToRegex(s)));
        }
        internal string GetRawPassword()
        {
            if (!IsRecordPassword || string.IsNullOrEmpty(ProtectedPassword)) return string.Empty;
            return PasswordManager.Decrypt(ProtectedPassword, PasswordProtectionScope);
        }
        public bool IsDifferentPassword(string newPassword) => GetRawPassword() != newPassword;
        private Regex ConvertToRegex(string strPattern)
        {
            var sb = new StringBuilder("^");
            if (Path.IsPathFullyQualified(strPattern) && !IsDefault && OriginBaseDirPath != null)
                strPattern = Path.DirectorySeparatorChar + Path.GetRelativePath(OriginBaseDirPath, strPattern);
            if (!strPattern.StartsWith(Path.DirectorySeparatorChar) && !strPattern.StartsWith('*')) sb.Append(Path.DirectorySeparatorChar, 2);
            sb.Append(Regex.Escape(strPattern).Replace(@"\*", ".*").Replace(@"\?", ".?"));
            sb.Append(Path.EndsInDirectorySeparator(strPattern) ? @".*$" : @"$");
            return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }
    }
}