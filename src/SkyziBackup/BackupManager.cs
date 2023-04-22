using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NLog;
using SkyziBackup.Data;

namespace SkyziBackup
{
    public static class BackupManager
    {
        public static bool IsRunning => RunningBackups.Any();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Dictionary<(string, string), BackupController> RunningBackups = new();
        private static readonly HashAlgorithm SHA1Provider = SHA1.Create();

        public static async Task<BackupResults?> StartBackupAsync(string originPath, string destPath, string? password = null, BackupSettings? settings = null)
        {
            using var bc = new BackupController(originPath, destPath, password, settings);
            return await StartBackupAsync(bc);
        }

        public static async Task<BackupResults?> StartBackupAsync(BackupController backup)
        {
            BackupResults? result = null;
            Semaphore? semaphore = null;
            var createdNew = true;
            try
            {
                semaphore = new Semaphore(1, 1, App.AssemblyName.Name + ComputeStringSHA1(backup.OriginBaseDirPath + backup.DestBaseDirPath), out createdNew);
                if (!createdNew)
                {
                    string m;
                    Logger.Info(m = $"バックアップ('{backup.OriginBaseDirPath}' => '{backup.DestBaseDirPath}')の開始をキャンセル: 既に別のプロセスによって実行中です。");
                    return new BackupResults(true, false, m);
                }

                RunningBackups.Add((backup.OriginBaseDirPath, backup.DestBaseDirPath), backup);
                App.NotifyIcon.Text = IsRunning ? $"{App.AssemblyName.Name} - バックアップ中" : App.AssemblyName.Name;
                result = await backup.StartBackupAsync();
                if (!result.IsSuccess)
                    App.NotifyIcon.ShowBalloonTip(10000, $"{App.AssemblyName.Name} - エラー", "バックアップに失敗しました。", ToolTipIcon.Error);
                result = null;
            }
            finally
            {
                semaphore?.Dispose();
                RunningBackups.Remove((backup.OriginBaseDirPath, backup.DestBaseDirPath));
                if (createdNew)
                    backup.Dispose();
                App.NotifyIcon.Text = IsRunning ? $"{App.AssemblyName.Name} - バックアップ中" : App.AssemblyName.Name;
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2);
            }

            return result;
        }

        public static async Task CancelAllAsync()
        {
            await Task.WhenAll(RunningBackups.Values.Select(b => b.CancelAsync()).ToArray());
            RunningBackups.Values.ToList().ForEach(b => b.Dispose());
            RunningBackups.Clear();
        }

        public static BackupController? GetBackupIfRunning(string originPath, string destPath) =>
            RunningBackups.TryGetValue((originPath, destPath), out var backupDirectory) ? backupDirectory : null;

        public static BackupController[] GetRunningBackups() => RunningBackups.Values.ToArray();

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
