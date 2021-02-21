using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Security.Cryptography;
using NLog;
using Skyzi000.Cryptography;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static BackupSettings GlobalSettings = BackupSettings.InitOrLoadGlobalSettings();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly SynchronizationContext _mainContext;

        public MainWindow()
        {
            InitializeComponent();
            _mainContext = SynchronizationContext.Current;
            ContentRendered += (s, e) =>
            {
                dataPath.TextChanged += DataPath_TextChanged;
                dataPath.Text = Properties.Settings.Default.AppDataPath;
                ignorePatternBox.Text = GlobalSettings.IgnorePattern;
                if (GlobalSettings.isRecordPassword || !string.IsNullOrEmpty(GlobalSettings.protectedPassword))
                {
                    try
                    {
                        password.Password = PasswordManager.GetRawPassword(GlobalSettings);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "パスワード読み込みエラー");
                        password.Password = string.Empty;
                        MessageBox.Show("パスワードを入力してください。", "パスワード読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                    password.Password = string.Empty;
            };
        }

        private void DataPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (dataPath.Text == "")
            {
                Properties.Settings.Default.AppDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Skyzi000", "SkyziBackup");
                System.IO.Directory.CreateDirectory(Properties.Settings.Default.AppDataPath);
                dataPath.Text = Properties.Settings.Default.AppDataPath;
            }
            if (System.IO.Directory.Exists(dataPath.Text))
            {
                Properties.Settings.Default.AppDataPath = dataPath.Text;
                Properties.Settings.Default.Save();
                dataPath.Text = Properties.Settings.Default.AppDataPath;
                Logger.Debug("DataPath : " + dataPath.Text);
                NLog.GlobalDiagnosticsContext.Set("AppDataPath", dataPath.Text);
            }
        }

        private async void EncryptButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(originPath.Text.Trim()))
            {
                MessageBox.Show($"{originPathLabel.Content}が不正です。\n正しいディレクトリパスを入力してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            encryptButton.IsEnabled = false;
            GlobalSettings = BackupSettings.InitOrLoadGlobalSettings();
            GlobalSettings.IgnorePattern = ignorePatternBox.Text;
            GlobalSettings.comparisonMethod = ComparisonMethod.WriteTime | ComparisonMethod.Size | ComparisonMethod.FileContentsSHA1;
            message.Text = $"設定を保存: '{DataContractWriter.GetPath(GlobalSettings)}'";
            Logger.Info(message.Text);
            DataContractWriter.Write(GlobalSettings);
            message.Text += $"\n'{originPath.Text.Trim()}' => '{destPath.Text.Trim()}'";
            message.Text += $"\nバックアップ開始: {DateTime.Now}\n";
            var db = new DirectoryBackup(originPath.Text.Trim(), destPath.Text.Trim(), password.Password, GlobalSettings);
            string m = message.Text;
            db.Results.MessageChanged += (_s, _e) => { _mainContext.Post((d) => { message.Text = m + db.Results.Message + "\n"; }, null); };
            await Task.Run(() => db.StartBackup());
            encryptButton.IsEnabled = true;
        }
    }
}