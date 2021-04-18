using NLog;
using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SkyziBackup
{
    /// <summary>
    /// RestoreWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class RestoreWindow : Window
    {
        private BackupSettings LoadCurrentSettings => BackupSettings.LoadLocalSettingsOrNull(destPath.Text.Trim(), originPath.Text.Trim()) ?? BackupSettings.Default;

        public RestoreWindow()
        {
            InitializeComponent();
            ContentRendered += (s, e) =>
            {
                password.Password = PasswordManager.LoadPasswordOrNull(LoadCurrentSettings) ?? string.Empty;
            };
        }
        public RestoreWindow(string restoreSourcePath, string restoreDestinationPath) : this()
        {
            originPath.Text = restoreSourcePath;
            destPath.Text = restoreDestinationPath;
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
                var changePassword = MessageBox.Show("入力されたパスワードが保存されているパスワードと異なります。\nこのまま続行しますか？",
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
            RestoreController restore = new RestoreController(originPath.Text.Trim(),
                                                            destPath.Text.Trim(),
                                                            password.Password,
                                                            settings,
                                                            copyAttributesOnDatabaseCheck.IsChecked,
                                                            copyOnlyAttributesCheck.IsChecked,
                                                            isEnableWriteDatabaseCheck.IsChecked);
            string m = Message.Text;
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void GlobalSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(BackupSettings.Default).ShowDialog();
            BackupSettings.ReloadDefault();
            password.Password = PasswordManager.LoadPasswordOrNull(LoadCurrentSettings) ?? string.Empty;
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
    }
}
