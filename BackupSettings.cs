using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace SkyziBackup
{
    public class BackupSettings : SaveableData
    {
        private static BackupSettings _default = null;
        /// <summary>
        /// グローバル設定を返す
        /// </summary>
        [JsonIgnore]
        public static BackupSettings Default => _default ??= LoadGlobalSettingsOrNull() ?? new BackupSettings();
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
        public string RevisionsDirPath { get; set; }
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
        public string SavedProtectedPassword { get; set; }
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
        /// <see cref="Properties.Settings.AppDataPath"/> から見たローカル設定の相対パス。グローバル設定の場合は null
        /// </summary>
        private string localFileName;

        private HashSet<Regex> regices;

        /// <summary>
        /// <see cref="IgnorePattern"/> を元に生成した除外用の正規表現セット
        /// </summary>
        [JsonIgnore]
        public HashSet<Regex> Regices { get => regices ?? (string.IsNullOrEmpty(IgnorePattern) ? null : Regices = Pattern2Regices(IgnorePattern)); private set => regices = value; }
        /// <summary>
        /// バックアップ設定のファイル名
        /// </summary>
        public static readonly string FileName = nameof(BackupSettings) + ".json";
        /// <summary>
        /// グローバル設定なら true
        /// </summary>
        [JsonIgnore]
        public bool IsGlobal => string.IsNullOrEmpty(localFileName);
        /// <summary>
        /// グローバル設定であれば <see cref="FileName"/> 、そうでなければ <see cref="localFileName"/> 
        /// </summary>
        [JsonIgnore]
        public override string SaveFileName => IsGlobal ? FileName : localFileName;
        /// <summary>
        /// 除外パターン
        /// </summary>
        public string IgnorePattern { get => ignorePattern; set { ignorePattern = value; Regices = Pattern2Regices(IgnorePattern); } }
        private string ignorePattern;
        /// <summary>
        /// 暗号化されたパスワード。代入した場合自動的に暗号化される。(予め暗号化する必要はない)
        /// </summary>
        [JsonIgnore]
        public string ProtectedPassword { get => SavedProtectedPassword; set { SavedProtectedPassword = string.IsNullOrEmpty(value) ? null : PasswordManager.Encrypt(value, PasswordProtectionScope); } }

        public BackupSettings()
        {
            IsUseDatabase = true;
            IsCopyAttributes = true;
            IsOverwriteReadonly = false;
            IsEnableDeletion = false;
            Versioning = VersioningMethod.PermanentDeletion;
            RevisionsDirPath = null;
            IsRecordPassword = true;
            PasswordProtectionScope = DataProtectionScope.LocalMachine;
            SavedProtectedPassword = null;
            RetryCount = 10;
            RetryWaitMilliSec = 10000;
            ComparisonMethod = ComparisonMethod.WriteTime | ComparisonMethod.Size;
            ignorePattern = string.Empty;
            CompressionLevel = CompressionLevel.NoCompression;
            CompressAlgorithm = CompressAlgorithm.Deflate;
            IsCancelable = true;
        }
        /// <summary>
        /// ローカル設定用のコンストラクタ
        /// </summary>
        public BackupSettings(string originBaseDirPath, string destBaseDirPath) : this()
        {
            localFileName = Path.Combine(DataFileWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName);
        }
        /// <summary>
        /// ローカル設定用のコンストラクタ(直接 <see cref="localFileName"/> を指定する)
        /// </summary>
        public BackupSettings(string localFileName) : this()
        {
            this.localFileName = localFileName;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("設定ファイルのパス---------------: {0}\n", DataFileWriter.GetPath(this));
            sb.AppendFormat("グローバル設定である-------------: {0}\n", IsGlobal);
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
            sb.AppendFormat("キャンセル可能-------------------: {0}\n", IsCancelable);
            sb.AppendFormat("除外パターン---------------------: \n{0}\n", IgnorePattern);
            return sb.ToString();
        }

        /// <summary>
        /// デフォルト設定をファイルから読み込み直す。読み込めない場合は何もしない。
        /// </summary>
        public static BackupSettings ReloadDefault() => _default = LoadGlobalSettingsOrNull() ?? Default;
        public static bool TryLoadGlobalSettings(out BackupSettings globalSettings)
        {
            if (File.Exists(DataFileWriter.GetPath(FileName)))
            {
                try
                {
                    globalSettings = DataFileWriter.Read<BackupSettings>(FileName);
                    return true;
                }
                catch (Exception) { } // 握りつぶす
            }
            globalSettings = null;
            return false;
        }
        public static BackupSettings LoadGlobalSettingsOrNull() => TryLoadGlobalSettings(out BackupSettings globalSettings) ? globalSettings : null;
        public static bool TryLoadLocalSettings(string originBaseDirPath, string destBaseDirPath, out BackupSettings localSettings)
        {
            string lfName;
            try
            {
                if (File.Exists(DataFileWriter.GetPath(lfName = Path.Combine(DataFileWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName))))
                {
                    localSettings = DataFileWriter.Read<BackupSettings>(lfName);
                    localSettings.localFileName = lfName;
                    return true;
                }
            }
            catch (Exception) { } // 握りつぶす
            localSettings = null;
            return false;
        }
        public static BackupSettings LoadLocalSettingsOrNull(string originBaseDirPath, string destBaseDirPath) => TryLoadLocalSettings(originBaseDirPath, destBaseDirPath, out BackupSettings localSettings) ? localSettings : null;
        public BackupSettings ConvertToLocalSettings(string originBaseDirPath, string destBaseDirPath)
        {
            localFileName = Path.Combine(DataFileWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName);
            return this;
        }
        public static HashSet<Regex> Pattern2Regices(string pattern)
        {
            string[] patStrArr = pattern.Split(new[] { "\r\n", "\n", "\r", "|" }, StringSplitOptions.None);
            return new HashSet<Regex>(patStrArr.Select(s => ShapePattern(s)));
        }
        internal string GetRawPassword()
        {
            if (!IsRecordPassword || string.IsNullOrEmpty(SavedProtectedPassword)) return string.Empty;
            return PasswordManager.Decrypt(SavedProtectedPassword, PasswordProtectionScope);
        }
        public bool IsDifferentPassword(string newPassword)
        {
            return GetRawPassword() != newPassword;
        }
        private static Regex ShapePattern(string strPattern)
        {
            var sb = new StringBuilder("^");
            if (!strPattern.StartsWith(Path.DirectorySeparatorChar) && !strPattern.StartsWith('*')) sb.Append(Path.DirectorySeparatorChar, 2);
            sb.Append(Regex.Escape(strPattern).Replace(@"\*", ".*").Replace(@"\?", ".?"));
            sb.Append(Path.EndsInDirectorySeparator(strPattern) ? @".*$" : @"$");
            return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }
    }
}