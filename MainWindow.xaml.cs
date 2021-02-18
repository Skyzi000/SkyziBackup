using System;
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
using NLog;
using Skyzi000.Cryptography;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly BackupSettings globalSettings = BackupSettings.InitOrLoadGlobalSettings();
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly SynchronizationContext _mainContext;

        public MainWindow()
        {
            InitializeComponent();
            _mainContext = SynchronizationContext.Current;
            ContentRendered += (s, e) =>
            {
                dataPath.TextChanged += DataPath_TextChanged;
                dataPath.Text = Properties.Settings.Default.AppDataPath;
                ignorePatternBox.Text = globalSettings.IgnorePattern;
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
                logger.Debug("DataPath : " + dataPath.Text);
                NLog.GlobalDiagnosticsContext.Set("AppDataPath", dataPath.Text);
            }
        }

        private async void EncryptButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            encryptButton.IsEnabled = false;
            globalSettings.IgnorePattern = ignorePatternBox.Text;
            message.Text = $"設定を保存: '{DataContractWriter.GetPath(globalSettings)}'";
            logger.Info(message.Text);
            DataContractWriter.Write(globalSettings);
            message.Text += $"\n'{originPath.Text}' => '{destPath.Text}'";
            message.Text += $"\nバックアップ開始: {DateTime.Now}\n";
            var db = new DirectoryBackup(originPath.Text, destPath.Text, password.Password, globalSettings);
            string m = message.Text;
            db.Results.MessageChanged += (_s, _e) => { _mainContext.Post((d) => { message.Text = m + db.Results.Message + "\n"; }, null); };
            await Task.Run(() => db.StartBackup());
            encryptButton.IsEnabled = true;
        }
    }
}