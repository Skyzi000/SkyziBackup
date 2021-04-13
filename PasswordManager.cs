using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using SkyziBackup;
using NLog;
using System.Windows;

namespace Skyzi000.Cryptography
{
    class PasswordManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly byte[] additionalEntropy = new byte[] { 53, 249, 144, 108, 147, 203, 197, 218, 73, 200 };
        internal static byte[] Encrypt(byte[] data, DataProtectionScope scope) => ProtectedData.Protect(data, additionalEntropy, scope);
        internal static string Encrypt(string data, DataProtectionScope scope) => Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(data), scope));

        internal static byte[] Decrypt(byte[] data, DataProtectionScope scope) => ProtectedData.Unprotect(data, additionalEntropy, scope);
        internal static string Decrypt(string data, DataProtectionScope scope) => Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(data), scope));

        public static void SavePassword(BackupSettings settings, string password)
        {
            try
            {
                settings.ProtectedPassword = password;
                DataFileWriter.Write(settings);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "パスワードの保存に失敗");
            }
        }
        public static bool TryLoadPassword(BackupSettings settings, out string password)
        {
            if (settings.IsRecordPassword && !string.IsNullOrEmpty(settings.ProtectedPassword))
            {
                try
                {
                    password = settings.GetRawPassword();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "パスワード読み込みエラー");
                    password = null;
                    MessageBox.Show("パスワードを読み込めませんでした。\nパスワードを再度入力してください。", $"{App.AssemblyName.Name} - 読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                return true;
            }
            else
            {
                password = null;
                return false;
            }
        }
        public static string LoadPasswordOrNull(BackupSettings settings) => TryLoadPassword(settings, out string password) ? password : null;
    }
}
