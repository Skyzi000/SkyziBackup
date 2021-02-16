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
        }

        private void encryptButton_Click(object sender, RoutedEventArgs e)
        {
            //OpensslCompatibleAesCrypter crypter = new OpensslCompatibleAesCrypter(password.Text);
            //crypter.EncryptFile(inputPath.Text, inputPath.Text + ".gui.aes256");
            var db = new DirectoryBackup(originPath.Text, destPath.Text, password.Text);
            db.Settings = new BackupSettings();
            db.StartBackup();
        }
    }
}
