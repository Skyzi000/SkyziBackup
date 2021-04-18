using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public static async Task<BackupResults> StartBackupAsync(string originPath, string destPath, string password = null, BackupSettings settings = null)
        {
            using var bc = new BackupController(originPath, destPath, password, settings);
            return await StartBackupAsync(bc);
        }
        public static async Task<BackupResults> StartBackupAsync(BackupController backup)
        {
            BackupResults result = null;
            Semaphore semaphore = null;
            try
            {
                semaphore = new Semaphore(1, 1, App.AssemblyName.Name + BackupController.ComputeStringSHA1(backup.originBaseDirPath + backup.destBaseDirPath), out bool createdNew);
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
                semaphore?.Dispose();
                backup?.Dispose();
                runningBackups.Remove((backup.originBaseDirPath, backup.destBaseDirPath));
                App.NotifyIcon.Text = IsRunning ? $"{App.AssemblyName.Name} - バックアップ中" : App.AssemblyName.Name;
            }
            return result;
        }
        public static async Task CancelAllAsync()
        {
            await Task.WhenAll(runningBackups.Values.Select(b => b.CancelAsync()).ToArray());
            runningBackups.Values.ToList().ForEach(b => b.Dispose());
            runningBackups.Clear();
        }
        public static BackupController GetBackupIfRunning(string originPath, string destPath)
        {
            return runningBackups.TryGetValue((originPath, destPath), out var backupDirectory) ? backupDirectory : null;
        }
        public static BackupController[] GetRunningBackups() => runningBackups.Values.ToArray();
    }
}
