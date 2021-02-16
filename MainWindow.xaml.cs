using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using Skyzi000.Cryptography;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ContentRendered += (s, e) =>
            {
                dataPath.TextChanged += LogPath_TextChanged;
                dataPath.Text = Properties.Settings.Default.AppDataPath;
            };
        }

        private void LogPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            Properties.Settings.Default.AppDataPath = dataPath.Text == "" ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Skyzi000", "SkyziBackup") : dataPath.Text;
            Properties.Settings.Default.Save();
            dataPath.Text = Properties.Settings.Default.AppDataPath;
            System.Diagnostics.Debug.WriteLine("DataPath : " + dataPath.Text);
        }

        private void encryptButton_Click(object sender, RoutedEventArgs e)
        {
            //OpensslCompatibleAesCrypter crypter = new OpensslCompatibleAesCrypter(password.Text);
            //crypter.EncryptFile(inputPath.Text, inputPath.Text + ".gui.aes256");
            var db = new DirectoryBackup(originPath.Text, destPath.Text, password.Text);
            db.Settings = new BackupSettings();
            db.StartBackup();
            message.Text = db.Results.message;
        }
    }
}
