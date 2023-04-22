using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Xml;
using NLog;
using NLog.Config;
using Skyzi000.Data;
using SkyziBackup.Data;
using SkyziBackup.Properties;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

// ReSharper disable CheckNamespace

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static NotifyIcon NotifyIcon { get; private set; } = new();
        public static readonly AssemblyName AssemblyName = Assembly.GetExecutingAssembly().GetName();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            if (Settings.Default.IsUpgradeRequired)
            {
                Settings.Default.Upgrade();
                Settings.Default.IsUpgradeRequired = false;
                Settings.Default.Save();
            }

            if (string.IsNullOrEmpty(Settings.Default.AppDataPath))
                InitAppDataPath();

            DataFileWriter.BaseDirectoryPath = Settings.Default.AppDataPath;
            SetRegexDefaultMatchTimeout(TimeSpan.FromSeconds(10));
            LoadNLogConfig();
            CreateNotifyIcon();
        }

        private static void InitAppDataPath()
        {
            Settings.Default.AppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Skyzi000", "SkyziBackup");
            Settings.Default.Save();
            Directory.CreateDirectory(Settings.Default.AppDataPath);
        }

        private void CreateNotifyIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("メイン画面を表示する", null, MainShow_Click);
            menu.Items.Add("最新のログファイルを開く", null, OpenLog_Click);
            menu.Items.Add("終了する", null, Exit_Click);
            using Stream? icon = GetResourceStream(new Uri("SkyziBackup.ico", UriKind.Relative))?.Stream;
            if (icon is null)
            {
                Logger.Error("アイコンファイルが見つかりませんでした。");
                MessageBox.Show("アイコンファイルが見つかりませんでした。", $"{AssemblyName.Name} - エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            NotifyIcon = new NotifyIcon
            {
                Visible = true,
                Icon = new Icon(icon),
                Text = AssemblyName.Name,
                ContextMenuStrip = menu,
            };

            NotifyIcon.MouseClick += NotifyIcon_Click;
        }

        private static void LoadNLogConfig()
        {
            using Stream? stream = GetResourceStream(new Uri("NLog.config", UriKind.Relative))?.Stream;
            if (stream == null)
                return;
            using var xmlReader = XmlReader.Create(stream);
            LogManager.Configuration = new XmlLoggingConfiguration(xmlReader);
        }

        private static void SetRegexDefaultMatchTimeout(TimeSpan timeout) =>
            AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", timeout);

        protected override async void OnStartup(StartupEventArgs args)
        {
            base.OnStartup(args);

            // アプリケーションの実行
            if (!args.Args.Any())
                ShowMainWindowIfClosed();
            // 引数2個の場合、バックアップを実行して終了する
            else if (args.Args.Length == 2)
            {
                var originPath = args.Args[0];
                var destPath = args.Args[1];
                if (Directory.Exists(originPath))
                {
                    var settings = BackupSettings.LoadLocalSettings(originPath, destPath) ??
                                   BackupSettings.Default;
                    using var results = await BackupManager.StartBackupAsync(originPath, destPath,
                        settings.IsRecordPassword ? settings.GetRawPassword() : null, settings);
                    if (results is { IsSuccess: false })
                        await Task.Delay(10000);
                }
                else
                {
                    Logger.Warn($"'{BackupController.GetQualifiedDirectoryPath(originPath)}'は存在しません。");
                    MessageBox.Show(
                        $"{BackupController.GetQualifiedDirectoryPath(originPath)}は存在しません。\n正しいディレクトリパスを入力してください。",
                        $"{AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                Quit();
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            Logger.Error(args.Exception, "バックグラウンドタスクで予期しない例外が発生しました");
            if (MessageBoxResult.Yes == MessageBox.Show(
                    $"バックグラウンドタスクで予期しない例外({args.Exception?.InnerException?.GetType().Name})が発生しました。プログラムを継続しますか？\n" +
                    $"エラーメッセージ: {args.Exception?.InnerException?.Message}\nスタックトレース: {args.Exception?.InnerException?.StackTrace}",
                    $"{AssemblyName.Name} - エラー", MessageBoxButton.YesNo, MessageBoxImage.Error))
                args.SetObserved();
            else
                Quit();
        }

        private void App_DispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            Logger.Error(args.Exception, "予期しない例外が発生しました: {3}", args.Exception?.TargetSite?.Name, args.Exception?.GetType().Name,
                args.Exception?.Message);
            if (MessageBoxResult.Yes == MessageBox.Show(
                    $"予期しない例外({args.Exception?.GetType().Name})が発生しました。プログラムを継続しますか？\n" +
                    $"エラーメッセージ: {args.Exception?.Message}\nスタックトレース: {args.Exception?.StackTrace}",
                    $"{AssemblyName.Name} - エラー", MessageBoxButton.YesNo, MessageBoxImage.Error))
                args.Handled = true;
            else
                Quit();
        }

        public static bool OpenLatestLog(bool showDialog = true)
        {
            var logsDirectory =
                new DirectoryInfo(Path.Combine(Settings.Default.AppDataPath, "Logs"));
            if (logsDirectory.Exists && logsDirectory.EnumerateFiles("*.log").Any())
            {
                Process.Start("explorer.exe",
                    logsDirectory.EnumerateFiles("*.log").OrderByDescending(x => x.LastWriteTime).First().FullName);
                return true;
            }

            if (showDialog)
            {
                MessageBox.Show("ログファイルが存在しません。", $"{AssemblyName.Name} - 情報", MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        private void OpenLog_Click(object? sender, EventArgs args) => OpenLatestLog();

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

        private void MainShow_Click(object? sender, EventArgs args) => ShowMainWindowIfClosed();

        private void NotifyIcon_Click(object? sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left)
                ShowMainWindowIfClosed();
        }

        public void Quit()
        {
            if (AcceptExit())
                Shutdown();
        }

        public static bool AcceptExit() =>
            !BackupManager.IsRunning || MessageBoxResult.Yes == MessageBox.Show("バックアップ実行中です。アプリケーションを終了しますか？",
                $"{AssemblyName.Name} - 確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        private void Exit_Click(object? sender, EventArgs args) => Quit();

        protected override async void OnSessionEnding(SessionEndingCancelEventArgs args)
        {
            base.OnSessionEnding(args);
            if (!BackupManager.IsRunning)
                return;
            args.Cancel = true;
            NotifyIcon.Text = $"{AssemblyName.Name} - バックアップを中断しています";
            Logger.Warn("バックアップを中断します: (セッションの終了)\n=============================\n\n");
            await BackupManager.CancelAllAsync();
            NotifyIcon.Text = AssemblyName.Name;
            args.Cancel = false;
        }

        protected override async void OnExit(ExitEventArgs args)
        {
            NotifyIcon.Visible = false;
            if (BackupManager.IsRunning)
            {
                Logger.Warn("バックアップを中断します: (強制終了)\n=============================\n\n");
                await BackupManager.CancelAllAsync();
                args.ApplicationExitCode = 2;
            }

            LogManager.Shutdown();
            base.OnExit(args);
        }
    }
}
