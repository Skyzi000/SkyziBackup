using NLog;
using Skyzi000.Cryptography;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Path = System.IO.Path;

namespace SkyziBackup
{
    /// <summary>
    /// RestoreWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class RestoreWindow : Window
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private BackupSettings LoadCurrentSettings => importedSettings ?? BackupSettings.LoadLocalSettings(destPath.Text, originPath.Text) ?? BackupSettings.Default;
        private BackupSettings? importedSettings;

        public RestoreWindow()
        {
            InitializeComponent();
        }
        public RestoreWindow(string restoreSourcePath, string restoreDestinationPath) : this()
        {
            originPath.Text = restoreSourcePath;
            destPath.Text = restoreDestinationPath;
            password.Password = PasswordManager.LoadPassword(LoadCurrentSettings) ?? string.Empty;
            if (LoadCurrentSettings.IsDefault)
                MessageBox.Show("ローカル設定を読み込めませんでした。\nローカル設定をインポートするか、手動でバックアップ時の設定に戻してください。", $"{App.AssemblyName.Name} - 情報", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void RestoreButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(originPath.Text.Trim()) && !(copyAttributesOnDatabaseCheck.IsChecked && copyOnlyAttributesCheck.IsChecked))
            {
                MessageBox.Show($"{originPathLabel.Content}が不正です。\n正しいディレクトリパスを入力してください。",
                                $"{App.AssemblyName.Name} - 警告",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }
            RestoreButton.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            BackupSettings settings = LoadCurrentSettings;
            if (settings.IsRecordPassword && settings.IsDifferentPassword(password.Password))
            {
                MessageBoxResult changePassword = MessageBox.Show("入力されたパスワードが保存されているパスワードと異なります。\nこのまま続行しますか？",
                                                     $"{App.AssemblyName.Name} - パスワードの確認",
                                                     MessageBoxButton.YesNo,
                                                     MessageBoxImage.Information);
                switch (changePassword)
                {
                    case MessageBoxResult.Yes:
                        break;
                    case MessageBoxResult.No:
                    default:
                        MessageBox.Show("リストアを中止します。");
                        RestoreButton.IsEnabled = true;
                        progressBar.Visibility = Visibility.Collapsed;
                        return;
                }
            }
            Message.Text = $"'{originPath.Text.Trim()}' => '{destPath.Text.Trim()}'";
            Message.Text += $"\nリストア開始: {DateTime.Now}\n";
            var restore = new RestoreController(originPath.Text.Trim(),
                                                            destPath.Text.Trim(),
                                                            password.Password,
                                                            settings,
                                                            copyAttributesOnDatabaseCheck.IsChecked,
                                                            copyOnlyAttributesCheck.IsChecked,
                                                            isEnableWriteDatabaseCheck.IsChecked);
            var m = Message.Text;
            restore.Results.MessageChanged += (_s, _e) =>
            {
                _ = Dispatcher.InvokeAsync(() =>
                  {
                      Message.Text = m + restore.Results.Message + "\n";
                  },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            await Task.Run(() => restore.StartRestore());
            RestoreButton.IsEnabled = true;
            progressBar.Visibility = Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void GlobalSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(BackupSettings.Default).ShowDialog();
            BackupSettings.ReloadDefault();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!RestoreButton.IsEnabled)
            {
                if (MessageBoxResult.Yes != MessageBox.Show("リストア実行中です。ウィンドウを閉じますか？",
                                                            "確認",
                                                            MessageBoxButton.YesNo,
                                                            MessageBoxImage.Information))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        private void LocalSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (LoadCurrentSettings.IsDefault)
                if (MessageBox.Show("現在のバックアップペアに対応するローカル設定を新規作成します。", $"{App.AssemblyName.Name} - 確認", MessageBoxButton.OKCancel) == MessageBoxResult.Cancel) return;
            new SettingsWindow(originPath.Text, destPath.Text).ShowDialog();
        }
        private void OpenDirectoryDialogButton_Click(object sender, RoutedEventArgs e)
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog() { FileName = "SelectFolder", Filter = "Folder|.", CheckFileExists = false };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                switch (((Button)sender).Tag)
                {
                    case "OriginPath":
                        originPath.Text = BackupController.GetQualifiedDirectoryPath(Path.GetDirectoryName(ofd.FileName) ?? string.Empty);
                        break;
                    case "DestPath":
                        destPath.Text = BackupController.GetQualifiedDirectoryPath(Path.GetDirectoryName(ofd.FileName) ?? string.Empty);
                        break;
                }
            }
        }
        private void ImportSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog()
            {
                FileName = BackupSettings.FileName,
                Filter = $"{nameof(BackupSettings)} files(*{DataFileWriter.JsonExtension})|*{DataFileWriter.JsonExtension}",
                CheckFileExists = true,
                InitialDirectory = Path.Combine(Properties.Settings.Default.AppDataPath,
                DataFileWriter.ParentDirectoryName)
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    importedSettings = DataFileWriter.Read<BackupSettings>(ofd.FileName);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "設定のインポートに失敗しました。");
                    importedSettings = null;
                }
                if (importedSettings is null)
                    MessageBox.Show("インポートに失敗しました。", $"{App.AssemblyName.Name} - エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                else if (importedSettings.IsDefault)
                    MessageBox.Show("デフォルト設定がインポートされました。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                DiscardImportedSettings.IsEnabled = importedSettings is not null;
            }
        }
        private void ShowCurrentSettings_Click(object sender, RoutedEventArgs e) => MessageBox.Show(LoadCurrentSettings.ToString(), $"{App.AssemblyName.Name} - 現在の設定");
        private void DiscardImportedSettings_Click(object sender, RoutedEventArgs e)
        {
            importedSettings = null;
            MessageBox.Show("インポートされた設定を破棄しました。", $"{App.AssemblyName.Name} - 現在の設定");
            DiscardImportedSettings.IsEnabled = false;
        }
    }
}
