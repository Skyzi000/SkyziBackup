using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using SkyziBackup;

namespace Skyzi000.Cryptography
{
    class PasswordManager
    {
        private static readonly byte[] additionalEntropy = new byte[] { 53, 249, 144, 108, 147, 203, 197, 218 };
        internal static byte[] Encrypt(byte[] data, DataProtectionScope scope) => ProtectedData.Protect(data, additionalEntropy, scope);
        internal static string EncryptString(string data, DataProtectionScope scope) => Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(data), scope));

        internal static byte[] Decrypt(byte[] data, DataProtectionScope scope) => ProtectedData.Unprotect(data, additionalEntropy, scope);
        internal static char[] DecryptChars(char[] data, DataProtectionScope scope) => Encoding.UTF8.GetChars(Decrypt(Convert.FromBase64CharArray(data, 0, data.Length), scope));
        internal static string GetRawPasswordString(BackupSettings settings)
        {
            if (!settings.isRecordPassword || string.IsNullOrEmpty(settings.protectedPassword)) throw new ArgumentException($"パスワードが保存されていません。: {settings}");
            return new string(DecryptChars(settings.protectedPassword.ToCharArray(), settings.passwordProtectionScope));
        }
        internal static char[] GetRawPasswordChars(BackupSettings settings)
        {
            if (!settings.isRecordPassword || string.IsNullOrEmpty(settings.protectedPassword)) throw new ArgumentException($"パスワードが保存されていません。: {settings}");
            return DecryptChars(settings.protectedPassword.ToCharArray(), settings.passwordProtectionScope);
        }
    }
}
