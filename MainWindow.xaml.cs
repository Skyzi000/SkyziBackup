using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using System.Diagnostics;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static BackupSettings GlobalBackupSettings = BackupSettings.GetGlobalSettings();
        public static AssemblyName AssemblyName = Assembly.GetExecutingAssembly().GetName();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly SynchronizationContext _mainContext;

        public MainWindow()
        {
            InitializeComponent();
            _mainContext = SynchronizationContext.Current;
            ContentRendered += (s, e) =>
            {
                password.Password = LoadPasswordOrNull() ?? string.Empty;
            };
        }

        public static bool TryLoadPassword(out string password)
        {
            if (GlobalBackupSettings.isRecordPassword && !string.IsNullOrEmpty(GlobalBackupSettings.ProtectedPassword))
            {
                try
                {
                    password = GlobalBackupSettings.GetRawPassword();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "パスワード読み込みエラー");
                    password = null;
                    MessageBox.Show("パスワードを読み込めませんでした。\nパスワードを再度入力してください。", $"{AssemblyName.Name} - 読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                return true;
            }
            else
            {
                password = null;
                return false;
            }
        }
        public static string LoadPasswordOrNull() => TryLoadPassword(out string password) ? password : null;

        private async void EncryptButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(originPath.Text.Trim()))
            {
                MessageBox.Show($"{originPathLabel.Content}が不正です。\n正しいディレクトリパスを入力してください。", $"{AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            encryptButton.IsEnabled = false;
            GlobalBackupSettings = BackupSettings.LoadGlobalSettingsOrNull() ?? GlobalBackupSettings; // TODO: ローカル設定を取り扱えるようにする
            if (GlobalBackupSettings.isRecordPassword && GlobalBackupSettings.IsDifferentPassword(password.Password))
            {
                var changePassword = MessageBox.Show("前回のパスワードと異なります。\nパスワードを変更しますか？\n\n※パスワードを変更する場合、既存のバックアップやデータベースを削除し、\n　再度初めからバックアップし直すことをおすすめします。\n　異なるパスワードでバックアップされたファイルが共存する場合、\n　復元が難しくなります。", $"{AssemblyName.Name} - パスワード変更の確認", MessageBoxButton.YesNo, MessageBoxImage.Information);
                switch (changePassword)
                {
                    case MessageBoxResult.Yes:
                        Logger.Info("パスワードを保存");
                        SavePassword();
                        DeleteDatabase();
                        break;
                    case MessageBoxResult.No:
                    default:
                        MessageBox.Show("保存されたパスワードを使用します。");
                        if (TryLoadPassword(out string pass))
                        {
                            password.Password = pass;
                        }
                        else
                        {
                            encryptButton.IsEnabled = true;
                            return;
                        }
                        break;
                }
            }
            GlobalBackupSettings.comparisonMethod = ComparisonMethod.WriteTime | ComparisonMethod.Size | ComparisonMethod.FileContentsSHA1;
            message.Text = $"設定を保存: '{DataContractWriter.GetPath(GlobalBackupSettings)}'";
            Logger.Info(message.Text);
            DataContractWriter.Write(GlobalBackupSettings);
            message.Text += $"\n'{originPath.Text.Trim()}' => '{destPath.Text.Trim()}'";
            message.Text += $"\nバックアップ開始: {DateTime.Now}\n";
            progressBar.Visibility = Visibility.Visible;
            var db = new BackupDirectory(originPath.Text.Trim(), destPath.Text.Trim(), password.Password, GlobalBackupSettings);
            string m = message.Text;
            db.Results.MessageChanged += (_s, _e) => { _mainContext.Post((d) => { message.Text = m + db.Results.Message + "\n"; }, null); };
            await Task.Run(() => db.StartBackup());
            progressBar.Visibility = Visibility.Hidden;
            encryptButton.IsEnabled = true;
        }

        private void SavePassword()
        {
            try
            {
                GlobalBackupSettings.ProtectedPassword = password.Password;
                DataContractWriter.Write(GlobalBackupSettings);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "パスワードの保存に失敗");
            }
        }

        /// <summary>
        /// データベースを削除するかどうかの確認ウィンドウを出してから削除する。
        /// </summary>
        /// <returns>削除したなら true</returns>
        private bool DeleteDatabase()
        {
            string databasePath;
            if (GlobalBackupSettings.isUseDatabase && File.Exists(databasePath = DataContractWriter.GetDatabasePath(originPath.Text.Trim(), destPath.Text.Trim())))
            {
                var deleteDatabase = MessageBox.Show($"{databasePath}\n上記データベースを削除しますか？\nなお、データベースを削除すると全てのファイルを初めからバックアップすることになります。", $"{AssemblyName.Name} - データベース削除", MessageBoxButton.YesNo);
                switch (deleteDatabase)
                {
                    case MessageBoxResult.Yes:
                        DataContractWriter.DeleteDatabase(originPath.Text.Trim(), destPath.Text.Trim());
                        MessageBox.Show("データベースを削除しました。");
                        return true;
                    case MessageBoxResult.No:
                    default:
                        return false;
                }
            }
            else
                return false;
        }

        private void RestoreWindowMenu_Click(object sender, RoutedEventArgs e)
        {
            var restoreWindow = new RestoreWindow(destPath.Text.Trim(), originPath.Text.Trim());
            restoreWindow.Show();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GlobalSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(ref GlobalBackupSettings).ShowDialog();
            GlobalBackupSettings = BackupSettings.LoadGlobalSettingsOrNull() ?? GlobalBackupSettings;
        }
    }
}

