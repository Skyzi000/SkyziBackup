using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Linq;
using System.Text;

namespace SkyziBackup
{
    [DataContract]
    [KnownType(typeof(BackupSettings))]
    [KnownType(typeof(VersioningMethod))]
    [KnownType(typeof(DataProtectionScope))]
    [KnownType(typeof(ComparisonMethod))]
    [KnownType(typeof(IDataContractSerializable))]
    [KnownType(typeof(CompressionLevel))]
    [KnownType(typeof(CompressAlgorithm))]
    public class BackupSettings : IDataContractSerializable
    {
        /// <summary>
        /// データベースを利用する
        /// </summary>
        [DataMember]
        public bool isUseDatabase;
        /// <summary>
        /// 属性(作成日時や更新日時を含む)をコピーする
        /// </summary>
        [DataMember]
        public bool isCopyAttributes;
        /// <summary>
        /// 読み取り専用ファイルを上書きする
        /// </summary>
        [DataMember]
        public bool isOverwriteReadonly;
        /// <summary>
        /// ミラーリングする(バックアップ元ファイルの存在しないバックアップ先ファイルを削除する)
        /// </summary>
        [DataMember]
        public bool isEnableDeletion;
        /// <summary>
        /// 削除または上書きされたファイルのバージョン管理方法
        /// </summary>
        [DataMember]
        public VersioningMethod versioning;
        /// <summary>
        /// パスワードを記録する
        /// </summary>
        [DataMember]
        public bool isRecordPassword;
        /// <summary>
        /// 記録したパスワードのスコープ
        /// </summary>
        [DataMember]
        public DataProtectionScope passwordProtectionScope;
        /// <summary>
        /// 暗号化されたパスワード
        /// </summary>
        [DataMember]
        private string protectedPassword;
        /// <summary>
        /// リトライ回数
        /// </summary>
        [DataMember]
        public int retryCount;
        /// <summary>
        /// リトライ待機時間(ミリ秒)
        /// </summary>
        [DataMember]
        public int retryWaitMilliSec;
        /// <summary>
        /// ファイル比較方法
        /// </summary>
        [DataMember]
        public ComparisonMethod comparisonMethod;
        /// <summary>
        /// 除外パターン
        /// </summary>
        [DataMember]
        private string ignorePattern;
        /// <summary>
        /// 圧縮レベル
        /// </summary>
        [DataMember]
        public CompressionLevel compressionLevel;
        /// <summary>
        /// 圧縮アルゴリズム
        /// </summary>
        [DataMember]
        public CompressAlgorithm compressAlgorithm;
        /// <summary>
        /// <see cref="Properties.Settings.AppDataPath"/> から見たローカル設定の相対パス。グローバル設定の場合は null
        /// </summary>
        [IgnoreDataMember]
        private string localFileName;
        /// <summary>
        /// <see cref="IgnorePattern"/> を元に生成した除外用の正規表現セット
        /// </summary>
        [IgnoreDataMember]
        public HashSet<Regex> Regices { get; private set; }
        /// <summary>
        /// バックアップ設定のファイル名
        /// </summary>
        [IgnoreDataMember]
        public static readonly string FileName = nameof(BackupSettings) + ".xml";
        /// <summary>
        /// グローバル設定なら true
        /// </summary>
        public bool IsGlobal => string.IsNullOrEmpty(localFileName);
        /// <summary>
        /// グローバル設定であれば <see cref="FileName"/> 、そうでなければ <see cref="localFileName"/> 
        /// </summary>
        public string SaveFileName => IsGlobal ? FileName : localFileName;

        public string IgnorePattern { get => ignorePattern; set { ignorePattern = value; UpdateRegices(); } }
        /// <summary>
        /// 暗号化されたパスワード。代入した場合自動的に暗号化される。(予め暗号化する必要はない)
        /// </summary>
        public string ProtectedPassword { get => protectedPassword; set { protectedPassword = string.IsNullOrEmpty(value) ? null : PasswordManager.Encrypt(value, passwordProtectionScope); } }

        public BackupSettings()
        {
            isUseDatabase = true;
            isCopyAttributes = true;
            isOverwriteReadonly = false;
            isEnableDeletion = false;
            versioning = VersioningMethod.PermanentDeletion;
            isRecordPassword = true;
            passwordProtectionScope = DataProtectionScope.LocalMachine;
            protectedPassword = null;
            retryCount = 10;
            retryWaitMilliSec = 10000;
            comparisonMethod = ComparisonMethod.WriteTime | ComparisonMethod.Size;
            ignorePattern = string.Empty;
            compressionLevel = CompressionLevel.NoCompression;
            compressAlgorithm = CompressAlgorithm.Deflate;
        }
        /// <summary>
        /// ローカル設定用のコンストラクタ
        /// </summary>
        public BackupSettings(string originBaseDirPath, string destBaseDirPath) : this()
        {
            localFileName = Path.Combine(DataContractWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName);
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
            sb.AppendFormat("設定ファイルのパス---------------: {0}\n", DataContractWriter.GetPath(this));
            sb.AppendFormat("グローバル設定である-------------: {0}\n", IsGlobal);
            sb.AppendFormat("データベースを利用する-----------: {0}\n", isUseDatabase);
            sb.AppendFormat("属性をコピーする-----------------: {0}\n", isCopyAttributes);
            sb.AppendFormat("読み取り専用ファイルを上書きする-: {0}\n", isOverwriteReadonly);
            sb.AppendFormat("ミラーリングする-----------------: {0}\n", isEnableDeletion);
            sb.AppendFormat("削除・上書き時のバージョン管理法-: {0}\n", versioning);
            sb.AppendFormat("パスワードを記録する-------------: {0}\n", isRecordPassword);
            sb.AppendFormat("記録したパスワードのスコープ-----: {0}\n", passwordProtectionScope);
            sb.AppendFormat("リトライ回数---------------------: {0}\n", retryCount);
            sb.AppendFormat("リトライ待機時間(ミリ秒)---------: {0}\n", retryWaitMilliSec);
            sb.AppendFormat("ファイル比較方法-----------------: {0}\n", comparisonMethod);
            sb.AppendFormat("圧縮レベル-----------------------: {0}\n", compressionLevel);
            sb.AppendFormat("圧縮アルゴリズム-----------------: {0}\n", compressAlgorithm);
            sb.AppendFormat("除外パターン---------------------: \n{0}\n", IgnorePattern);
            return sb.ToString();
        }

        /// <summary>
        /// グローバル設定をファイルから読み込む。読み込めない場合は新規インスタンスを返す。
        /// </summary>
        public static BackupSettings GetGlobalSettings() => LoadGlobalSettingsOrNull() ?? new BackupSettings();

        public static bool TryLoadGlobalSettings(out BackupSettings globalSettings)
        {
            if (File.Exists(DataContractWriter.GetPath(FileName)))
            {
                try
                {
                    globalSettings = DataContractWriter.Read<BackupSettings>(FileName);
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
                if (File.Exists(DataContractWriter.GetPath(lfName = Path.Combine(DataContractWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName))))
                {
                    localSettings = DataContractWriter.Read<BackupSettings>(lfName);
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
            localFileName = Path.Combine(DataContractWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName);
            return this;
        }
        public void UpdateRegices()
        {
            string[] patStrArr = IgnorePattern.Split(new[] { "\r\n", "\n", "\r", "|" }, StringSplitOptions.None);
            Regices = new HashSet<Regex>(patStrArr.Select(s => ShapePattern(s)));
        }
        internal string GetRawPassword()
        {
            if (!isRecordPassword || string.IsNullOrEmpty(protectedPassword)) return string.Empty;
            return PasswordManager.Decrypt(protectedPassword, passwordProtectionScope);
        }
        public bool IsDifferentPassword(string newPassword)
        {
            return GetRawPassword() != newPassword;
        }
        private Regex ShapePattern(string strPattern)
        {
            var sb = new StringBuilder("^");
            if (!strPattern.StartsWith(Path.DirectorySeparatorChar) && !strPattern.StartsWith('*')) sb.Append(Path.DirectorySeparatorChar, 2);
            sb.Append(Regex.Escape(strPattern).Replace(@"\*", ".*").Replace(@"\?", ".?"));
            sb.Append(Path.EndsInDirectorySeparator(strPattern) ? @".*$" : @"$");
            return new Regex(sb.ToString(), RegexOptions.Compiled);
        }
    }
}