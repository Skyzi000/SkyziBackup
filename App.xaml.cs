using NLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static NotifyIcon NotifyIcon { get; private set; }
        public static AssemblyName AssemblyName = Assembly.GetExecutingAssembly().GetName();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            if (SkyziBackup.Properties.Settings.Default.IsUpgradeRequired)
            {
                SkyziBackup.Properties.Settings.Default.Upgrade();
                SkyziBackup.Properties.Settings.Default.IsUpgradeRequired = false;
                SkyziBackup.Properties.Settings.Default.Save();
            }
            if (string.IsNullOrEmpty(SkyziBackup.Properties.Settings.Default.AppDataPath))
            {
                SkyziBackup.Properties.Settings.Default.AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Skyzi000", "SkyziBackup");
                SkyziBackup.Properties.Settings.Default.Save();
                Directory.CreateDirectory(SkyziBackup.Properties.Settings.Default.AppDataPath);
            }

            // NLog.configの読み取り
            using (var nlogConfigStream = GetResourceStream(new Uri("NLog.config", UriKind.Relative)).Stream)
            {
                using var xmlReader = System.Xml.XmlReader.Create(nlogConfigStream);
                LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration(xmlReader);
            }

            // NotifyIconの作成
            var menu = new ContextMenuStrip();
            menu.Items.Add("メイン画面を表示する", null, MainShow_Click);
            menu.Items.Add("最新のログファイルを開く", null, OpenLog_Click);
            menu.Items.Add("終了する", null, Exit_Click);
            using (var icon = GetResourceStream(new Uri("SkyziBackup.ico", UriKind.Relative)).Stream)
                NotifyIcon = new NotifyIcon
                {
                    Visible = true,
                    Icon = new System.Drawing.Icon(icon),
                    Text = AssemblyName.Name,
                    ContextMenuStrip = menu
                };
            NotifyIcon.MouseClick += new MouseEventHandler(NotifyIcon_Click);

            // アプリケーションの実行
            if (!e.Args.Any())
            {
                ShowMainWindowIfClosed();
            }
            // 引数2個の場合、バックアップを実行して終了する
            else if (e.Args.Length == 2)
            {
                var originPath = e.Args[0];
                var destPath = e.Args[1];
                if (Directory.Exists(originPath))
                {
                    var settings = BackupSettings.LoadLocalSettingsOrNull(originPath, destPath) ?? BackupSettings.Default;
                    var results = await BackupManager.StartBackupAsync(originPath, destPath, settings.IsRecordPassword ? settings.GetRawPassword() : null, settings);
                }
                else
                {
                    Logger.Warn($"'{BackupController.GetQualifiedDirectoryPath(originPath)}'は存在しません。");
                    MessageBox.Show($"{BackupController.GetQualifiedDirectoryPath(originPath)}は存在しません。\n正しいディレクトリパスを入力してください。", $"{AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Quit();
            }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "バックグラウンドタスクで予期しない例外が発生しました");
            if (MessageBoxResult.Yes == MessageBox.Show($"バックグラウンドタスクで予期しない例外({e.Exception?.InnerException?.GetType().Name})が発生しました。プログラムを継続しますか？\nエラーメッセージ: {e.Exception?.InnerException?.Message}\nスタックトレース: {e.Exception?.InnerException?.StackTrace}", $"{AssemblyName.Name} - エラー", MessageBoxButton.YesNo, MessageBoxImage.Error))
                e.SetObserved();
            else
                Quit();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "予期しない例外が発生しました: {3}", e.Exception?.TargetSite?.Name, e.Exception?.GetType().Name, e.Exception?.Message);
            if (MessageBoxResult.Yes == MessageBox.Show($"予期しない例外({e.Exception?.GetType().Name})が発生しました。プログラムを継続しますか？\nエラーメッセージ: {e.Exception?.Message}\nスタックトレース: {e.Exception?.StackTrace}", $"{AssemblyName.Name} - エラー", MessageBoxButton.YesNo, MessageBoxImage.Error))
                e.Handled = true;
            else
                Quit();
        }

        public bool OpenLatestLog(bool showDialog = true)
        {
            var logsDirectory = new DirectoryInfo(Path.Combine(SkyziBackup.Properties.Settings.Default.AppDataPath, "Logs"));
            if (logsDirectory.Exists && logsDirectory.EnumerateFiles("*.log").Any())
            {
                Process.Start("explorer.exe", logsDirectory.EnumerateFiles("*.log").OrderByDescending((x) => x.LastWriteTime).First().FullName);
                return true;
            }
            if (showDialog)
                MessageBox.Show("ログファイルが存在しません。", $"{AssemblyName.Name} - 情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void OpenLog_Click(object sender, EventArgs e) => OpenLatestLog();

        /// <summary>
        /// MainWindowが閉じられている場合は新規作成して開き、既に存在する場合はアクティベートする
        /// </summary>
        public void ShowMainWindowIfClosed()
        {
            if (MainWindow == null)
            {
                MainWindow = new MainWindow();
                MainWindow.Closed += (s, e) => MainWindow = null;
                MainWindow.Show();
            }
            MainWindow.Activate();
        }

        private void MainShow_Click(object sender, EventArgs e) => ShowMainWindowIfClosed();

        private void NotifyIcon_Click(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindowIfClosed();
            }
        }

        public void Quit()
        {
            if (AcceptExit())
                Shutdown();
        }

        public bool AcceptExit()
        {
            if (BackupManager.IsRunning)
            {
                if (MessageBoxResult.Yes != MessageBox.Show("バックアップ実行中です。アプリケーションを終了しますか？",
                                                            $"{AssemblyName.Name} - 確認",
                                                            MessageBoxButton.YesNo,
                                                            MessageBoxImage.Warning))
                {
                    return false;
                }
            }
            return true;
        }

        private void Exit_Click(object sender, EventArgs e) => Quit();

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);
            if (!AcceptExit())
            {
                e.Cancel = true;
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            e.ApplicationExitCode = 2;
            NotifyIcon.Visible = false;
            if (BackupManager.IsRunning)
            {
                await BackupManager.CancelAllAsync();
                Logger.Warn("バックアップ実行中にアプリケーションを強制終了しました。\n=============================\n\n");
            }
            e.ApplicationExitCode = 0;
            LogManager.Shutdown();
        }
    }
}
