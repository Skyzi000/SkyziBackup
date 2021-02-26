using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static NotifyIcon NotifyIcon { get; private set; }
        public static AssemblyName AssemblyName = Assembly.GetExecutingAssembly().GetName();
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (string.IsNullOrEmpty(SkyziBackup.Properties.Settings.Default.AppDataPath))
            {
                SkyziBackup.Properties.Settings.Default.AppDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Skyzi000", "SkyziBackup");
                System.IO.Directory.CreateDirectory(SkyziBackup.Properties.Settings.Default.AppDataPath);
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
            if (!e.Args.Any())
            {
                ShowMainWindowIfClosed();
            }
            NotifyIcon.MouseClick += new MouseEventHandler(NotifyIcon_Click);
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
