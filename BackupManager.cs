using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyziBackup
{
    public static class BackupManager
    {
        public static bool IsRunning => runningBackups.Any();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Dictionary<(string, string), BackupController> runningBackups = new Dictionary<(string, string), BackupController>();
        private static readonly HashAlgorithm SHA1Provider = new SHA1CryptoServiceProvider();

        public static async Task<BackupResults?> StartBackupAsync(string originPath, string destPath, string? password = null, BackupSettings? settings = null)
        {
            using var bc = new BackupController(originPath, destPath, password, settings);
            return await StartBackupAsync(bc);
        }
        public static async Task<BackupResults?> StartBackupAsync(BackupController backup)
        {
            BackupResults? result = null;
            Semaphore? semaphore = null;
            try
            {
                semaphore = new Semaphore(1, 1, App.AssemblyName.Name + ComputeStringSHA1(backup.originBaseDirPath + backup.destBaseDirPath), out bool createdNew);
                if (!createdNew)
                {
                    string m;
                    Logger.Info(m = $"バックアップ('{backup.originBaseDirPath}' => '{backup.destBaseDirPath}')の開始をキャンセル: 既に別のプロセスによって実行中です。");
                    return new BackupResults(true, false, m);
                }
                runningBackups.Add((backup.originBaseDirPath, backup.destBaseDirPath), backup);
                App.NotifyIcon.Text = IsRunning ? $"{App.AssemblyName.Name} - バックアップ中" : App.AssemblyName.Name;
                result = await backup.StartBackupAsync();
                if (!result.isSuccess)
                    App.NotifyIcon.ShowBalloonTip(10000, $"{App.AssemblyName.Name} - エラー", "バックアップに失敗しました。", System.Windows.Forms.ToolTipIcon.Error);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "バックアップ中に予期しない例外が発生しました。");
            }
            finally
            {
                App.NotifyIcon.Text = IsRunning ? $"{App.AssemblyName.Name} - バックアップ中" : App.AssemblyName.Name;
                runningBackups.Remove((backup.originBaseDirPath, backup.destBaseDirPath));
                semaphore?.Dispose();
                backup?.Dispose();
            }
            return result;
        }
        public static async Task CancelAllAsync()
        {
            await Task.WhenAll(runningBackups.Values.Select(b => b.CancelAsync()).ToArray());
            runningBackups.Values.ToList().ForEach(b => b.Dispose());
            runningBackups.Clear();
        }
        
        public static BackupController? GetBackupIfRunning(string originPath, string destPath)
        {
            return runningBackups.TryGetValue((originPath, destPath), out var backupDirectory) ? backupDirectory : null;
        }
        public static BackupController[] GetRunningBackups() => runningBackups.Values.ToArray();
        public static string ComputeFileSHA1(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ComputeStreamSHA1(fs);
        }

        public static string ComputeStreamSHA1(Stream stream)
        {
            var bs = SHA1Provider.ComputeHash(stream);
            return BitConverter.ToString(bs).ToLower().Replace("-", "");
        }
        public static string ComputeStringSHA1(string value) => ComputeStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(value)));
    }
}
