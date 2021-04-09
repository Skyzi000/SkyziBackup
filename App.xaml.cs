using NLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
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
        public static bool IsRunning { get => _isRunning; set { _isRunning = value; NotifyIcon.Text = _isRunning ? $"{AssemblyName.Name} - バックアップ中" : AssemblyName.Name; } }
        private static bool _isRunning;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (string.IsNullOrEmpty(SkyziBackup.Properties.Settings.Default.AppDataPath))
            {
                SkyziBackup.Properties.Settings.Default.AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Skyzi000", "SkyziBackup");
                SkyziBackup.Properties.Settings.Default.Save();
                Directory.CreateDirectory(SkyziBackup.Properties.Settings.Default.AppDataPath);
            }
            var icon = GetResourceStream(new Uri("SkyziBackup.ico", UriKind.Relative)).Stream;
            var menu = new ContextMenuStrip();
            menu.Items.Add("メイン画面を表示する", null, MainShow_Click);
            menu.Items.Add("終了する", null, Exit_Click);
            NotifyIcon = new NotifyIcon
            {
                Visible = true,
                Icon = new System.Drawing.Icon(icon),
                Text = AssemblyName.Name,
                ContextMenuStrip = menu
            };
            NotifyIcon.MouseClick += new MouseEventHandler(NotifyIcon_Click);
            if (!e.Args.Any())
            {
                ShowMainWindowIfClosed();
            }
            else if (e.Args.Length == 2)
            {
                var originPath = Path.TrimEndingDirectorySeparator(e.Args[0].Trim());
                var destPath = Path.TrimEndingDirectorySeparator(e.Args[1].Trim());
                if (!Directory.Exists(originPath))
                {
                    Logger.Error($"'{originPath}'は存在しません。");
                    MessageBox.Show($"{originPath}は存在しません。\n正しいディレクトリパスを入力してください。", $"{AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown();
                    return;
                }
                var settings = BackupSettings.LoadLocalSettingsOrNull(originPath, destPath) ?? BackupSettings.GetGlobalSettings();
                var results = await StartBackupAsync(originPath, destPath, settings.isRecordPassword ? settings.GetRawPassword() : null, settings);
                if (results.isSuccess)
                {
                    NotifyIcon.Visible = false;
                    Shutdown();
                }
                else
                {
                    NotifyIcon.ShowBalloonTip(10000, $"{AssemblyName.Name} - エラー", "バックアップに失敗しました。", ToolTipIcon.Error);
                }
            }
        }
        public async Task<BackupResults> StartBackupAsync(string originPath, string destPath, string password, BackupSettings settings)
        {            
            IsRunning = true;
            var db = new BackupDirectory(originPath, destPath, password, settings);
            var result = await Task.Run(() => db.StartBackup());
            IsRunning = false;
            return result;
        }
        private void ShowMainWindowIfClosed()
        {
            if (MainWindow == null)
            {
                MainWindow = new MainWindow();
                MainWindow.Closed += (s, e) =>
                {
                    NotifyIcon.ShowBalloonTip(10000, $"{AssemblyName.Name}", "終了するには通知アイコンを右クリックしてください。", ToolTipIcon.Info);
                    MainWindow = null;
                };
                MainWindow.Show();
            }
            MainWindow.Activate();
        }
        private void MainShow_Click(object sender, EventArgs e)
        {
            ShowMainWindowIfClosed();
        }
        private void NotifyIcon_Click(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindowIfClosed();
            }
        }
        private void Exit_Click(object sender, EventArgs e)
        {
            // TODO: バックアップ中は確認する
            NotifyIcon.Visible = false;
            Shutdown();
        }
    }
}
