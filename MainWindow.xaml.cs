using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public MainWindow()
        {
            InitializeComponent();
            ContentRendered += (s, e) =>
            {
                dataPath.TextChanged += DataPath_TextChanged;
                dataPath.Text = Properties.Settings.Default.AppDataPath;
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

        private void encryptButton_Click(object sender, RoutedEventArgs e)
        {
            var patStrArr = ignorePatternBox.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            List<Regex> regices = new List<Regex>(patStrArr.Select(s => ShapePattern(s)));

            message.Text = "";
            foreach (var patReg in regices)
            {
                message.Text += patReg.ToString() + "\n";
            }

            //message.Text = "バックアップ開始\n";
            //var db = new DirectoryBackup(originPath.Text, destPath.Text, password.Password)
            //{
            //    Settings = new BackupSettings() { }
            //};
            //db.Results.MessageChanged += (_s, _e) => { message.Text += db.Results.Message + "\n"; };
            //db.StartBackup();
        }

        private Regex ShapePattern(string strPattern)
        {
            return new Regex("^" + Regex.Escape(strPattern).Replace(@"\*", "*").Replace(@"\?", "?") + (Regex.IsMatch(strPattern, @"\\$") ? @"*$" : @"$"), RegexOptions.Compiled);
        }
    }
}