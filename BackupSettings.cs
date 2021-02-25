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
    [KnownType(typeof(DataProtectionScope))]
    [KnownType(typeof(ComparisonMethod))]
    [KnownType(typeof(IDataContractSerializable))]
    [KnownType(typeof(CompressionLevel))]
    [KnownType(typeof(CompressAlgorithm))]
    public class BackupSettings : IDataContractSerializable
    {
        [DataMember]
        public bool isUseDatabase;
        [DataMember]
        public bool isCopyAttributes;
        [DataMember]
        public bool isOverwriteReadonly;
        [DataMember]
        public bool isEnableDeletion;
        [DataMember]
        public bool isRecordPassword;
        [DataMember]
        public DataProtectionScope passwordProtectionScope;
        [DataMember]
        private string protectedPassword;
        [DataMember]
        public int retryCount;
        [DataMember]
        public int retryWaitMilliSec;
        [DataMember]
        public ComparisonMethod comparisonMethod;
        [DataMember]
        private string ignorePattern;
        [DataMember]
        public CompressionLevel compressionLevel;
        [DataMember]
        public CompressAlgorithm compressAlgorithm;
        [IgnoreDataMember]
        private string localFileName;
        [IgnoreDataMember]
        public HashSet<Regex> Regices { get; private set; }
        [IgnoreDataMember]
        public static readonly string FileName = nameof(BackupSettings) + ".xml";

        public bool IsGlobal => string.IsNullOrEmpty(localFileName);
        public string SaveFileName => IsGlobal ? FileName : localFileName;

        public string IgnorePattern { get => ignorePattern; set { ignorePattern = value; UpdateRegices(); } }
        /// <summary>
        /// 暗号化されたパスワード。代入した場合自動的に暗号化される。(予め暗号化する必要はない)
        /// </summary>
        public string ProtectedPassword { get => protectedPassword; set { protectedPassword = (string.IsNullOrEmpty(value)) ? null : PasswordManager.Encrypt(value, passwordProtectionScope); } }

        public BackupSettings()
        {
            isUseDatabase = true;
            isCopyAttributes = true;
            isOverwriteReadonly = false;
            isEnableDeletion = false;
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

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("設定ファイルのパス---------------: {0}\n", DataContractWriter.GetPath(this));
            sb.AppendFormat("グローバル設定である-------------: {0}\n", IsGlobal);
            sb.AppendFormat("データベースを利用する-----------: {0}\n", isUseDatabase);
            sb.AppendFormat("属性をコピーする-----------------: {0}\n", isCopyAttributes);
            sb.AppendFormat("読み取り専用ファイルを上書きする-: {0}\n", isOverwriteReadonly);
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