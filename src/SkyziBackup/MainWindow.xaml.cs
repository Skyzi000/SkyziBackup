using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using NLog;
using Skyzi000.Cryptography;
using SkyziBackup.Properties;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private BackupSettings LoadCurrentSettings => BackupSettings.LoadLocalSettings(originPath.Text, destPath.Text) ?? BackupSettings.Default;

        private bool ButtonsIsEnabled
        {
            set
            {
                StartBackupButton.IsEnabled = value;
                LocalSettingsMenu.IsEnabled = value;
                DeleteLocalSettings.IsEnabled = value;
                StartBackupMenu.IsEnabled = value;
                CancelBackupMenu.IsEnabled = !value;
                OriginPathGrid.IsEnabled = value;
                DestPathGrid.IsEnabled = value;
                password.IsEnabled = value;
                DeleteDatabaseMenu.IsEnabled = value;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            if (Settings.Default.IsUpgradeRequired)
            {
                Settings.Default.Upgrade();
                Settings.Default.IsUpgradeRequired = false;
                Settings.Default.Save();
            }

            originPath.Text = Settings.Default.OriginPath;
            destPath.Text = Settings.Default.DestPath;
            password.Password = PasswordManager.LoadPassword(LoadCurrentSettings) ?? string.Empty;
            CancelBackupMenu.IsEnabled = false;
            if (BackupManager.IsRunning)
            {
                BackupController running;
                try
                {
                    running = BackupManager.GetRunningBackups().First();
                }
                catch (Exception)
                {
                    return;
                }

                ButtonsIsEnabled = false;
                progressBar.Visibility = Visibility.Visible;
                originPath.Text = running.originBaseDirPath;
                destPath.Text = running.destBaseDirPath;
                var m = message.Text = $"\n'{originPath.Text}' => '{destPath.Text}'\nバックアップ実行中 ({running.StartTime}開始)\n";
                running.Results.MessageChanged += (_, _) =>
                {
                    _ = Dispatcher.InvokeAsync(() => { message.Text = m + running.Results.Message + "\n"; },
                        DispatcherPriority.ApplicationIdle);
                };
                running.Results.Finished += (_, _) =>
                {
                    Dispatcher.Invoke(() =>
                        {
                            progressBar.Visibility = Visibility.Collapsed;
                            ButtonsIsEnabled = true;
                        },
                        DispatcherPriority.Normal);
                };
            }
        }


        private async void StartBackupButton_ClickAsync(object sender, RoutedEventArgs args)
        {
            SaveStates();
            if (BackupManager.GetBackupIfRunning(originPath.Text.Trim(), destPath.Text.Trim()) != null)
            {
                MessageBox.Show("バックアップは既に実行中です。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(originPath.Text.Trim()))
            {
                MessageBox.Show($"{originPath.Text.Trim()}は存在しません。\n正しいディレクトリパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ButtonsIsEnabled = false;
            var settings = LoadCurrentSettings;
            if (settings.IsRecordPassword)
            {
                if (settings.IsDifferentPassword(password.Password) && settings.ProtectedPassword != null)
                {
                    var changePassword =
                        MessageBox.Show(
                            "前回のパスワードと異なります。\nパスワードを変更しますか？\n\n※パスワードを変更する場合、既存のバックアップやデータベースを削除し、\n　再度初めからバックアップし直すことをおすすめします。\n　異なるパスワードでバックアップされたファイルが共存する場合、\n　復元が難しくなります。",
                            $"{App.AssemblyName.Name} - パスワード変更の確認", MessageBoxButton.YesNoCancel);
                    switch (changePassword)
                    {
                        case MessageBoxResult.Yes:
                            // TODO: パスワード確認入力ウィンドウを出す
                            Logger.Info("パスワードを更新");
                            PasswordManager.SavePassword(settings, password.Password);
                            DeleteDatabase();
                            break;
                        case MessageBoxResult.No:
                            if (MessageBox.Show("前回のパスワードを使用します。", App.AssemblyName.Name, MessageBoxButton.OKCancel, MessageBoxImage.Information) ==
                                MessageBoxResult.OK && PasswordManager.TryLoadPassword(settings, out var pass))
                                password.Password = pass;
                            else
                                goto case MessageBoxResult.Cancel;
                            break;
                        case MessageBoxResult.Cancel:
                            ButtonsIsEnabled = true;
                            return;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(password.Password))
                {
                    // TODO: パスワード確認入力ウィンドウを出す
                    PasswordManager.SavePassword(settings, password.Password);
                }
            }

            message.Text = $"'{originPath.Text.Trim()}' => '{destPath.Text.Trim()}'";
            message.Text += $"\nバックアップ開始: {DateTime.Now}\n";
            progressBar.Visibility = Visibility.Visible;
            var bc = new BackupController(originPath.Text.Trim(), destPath.Text.Trim(), password.Password, settings);
            var m = message.Text;
            bc.Results.MessageChanged += (_, _) =>
            {
                _ = Dispatcher.InvokeAsync(() => { message.Text = m + bc.Results.Message + "\n"; },
                    DispatcherPriority.ApplicationIdle);
            };
            try
            {
                using var results = await BackupManager.StartBackupAsync(bc);
                if (results != null)
                    message.Text = m + results.Message + "\n";
            }
            catch (OperationCanceledException)
            {
                message.Text = m + "バックアップはキャンセルされました。" + "\n";
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                ButtonsIsEnabled = true;
            }
        }

        /// <summary>
        /// データベースを削除するかどうかの確認ウィンドウを出してから削除する。
        /// </summary>
        /// <returns>削除したなら true</returns>
        private bool DeleteDatabase()
        {
            string databasePath;
            if (LoadCurrentSettings.IsUseDatabase && File.Exists(databasePath = DataFileWriter.GetDatabasePath(originPath.Text, destPath.Text)))
            {
                var deleteDatabase = MessageBox.Show($"{databasePath}\n上記データベースを削除しますか？", $"{App.AssemblyName.Name} - 確認", MessageBoxButton.YesNo);
                switch (deleteDatabase)
                {
                    case MessageBoxResult.Yes:
                        DataFileWriter.DeleteDatabase(originPath.Text, destPath.Text);
                        MessageBox.Show("データベースを削除しました。");
                        return true;
                    case MessageBoxResult.No:
                    default:
                        return false;
                }
            }

            return false;
        }

        private void RestoreWindowMenu_Click(object sender, RoutedEventArgs args)
        {
            var restoreWindow = new RestoreWindow(destPath.Text.Trim(), originPath.Text.Trim());
            restoreWindow.Show();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs args) => Close();

        private void GlobalSettingsMenu_Click(object sender, RoutedEventArgs args)
        {
            new SettingsWindow(BackupSettings.Default).ShowDialog();
            BackupSettings.ReloadDefault();
        }

        private void LocalSettingsMenu_Click(object sender, RoutedEventArgs args)
        {
            if (LoadCurrentSettings.IsDefault)
            {
                if (MessageBox.Show("現在のバックアップペアに対応するローカル設定を新規作成します。", $"{App.AssemblyName.Name} - 確認", MessageBoxButton.OKCancel) == MessageBoxResult.Cancel)
                    return;
            }

            new SettingsWindow(originPath.Text, destPath.Text).ShowDialog();
        }

        private void ShowCurrentSettings_Click(object sender, RoutedEventArgs args) =>
            MessageBox.Show(LoadCurrentSettings.ToString(), $"{App.AssemblyName.Name} - 現在の設定");

        private void DeleteLocalSettings_Click(object sender, RoutedEventArgs args)
        {
            var currentSettings = LoadCurrentSettings;
            if (currentSettings.IsDefault)
                MessageBox.Show("現在のバックアップペアに対応するローカル設定は存在しません。", App.AssemblyName.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            else
            {
                if (MessageBox.Show("現在のバックアップペアに対応するローカル設定を削除します。", $"{App.AssemblyName.Name} - 確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning) ==
                    MessageBoxResult.Cancel)
                    return;
                DataFileWriter.Delete(currentSettings);
                password.Password = PasswordManager.LoadPassword(LoadCurrentSettings) ?? string.Empty;
            }
        }

        private void OpenLog_Click(object sender, RoutedEventArgs args) => App.OpenLatestLog();

        private void Exit_Click(object sender, RoutedEventArgs args) => ((App) Application.Current).Quit();

        private void Window_Closing(object sender, CancelEventArgs args) => SaveStates();

        private void SaveStates()
        {
            Settings.Default.OriginPath = originPath.Text;
            Settings.Default.DestPath = destPath.Text;
            Settings.Default.Save();
        }

        private void OpenDirectoryDialogButton_Click(object sender, RoutedEventArgs args)
        {
            using var ofd = new OpenFileDialog { FileName = "SelectFolder", Filter = "Folder|.", CheckFileExists = false };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                switch (((Button) sender).Tag)
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

        private void OpenAppDataButton_Click(object sender, RoutedEventArgs args) => Process.Start("explorer.exe", Settings.Default.AppDataPath);

        private void DeleteDatabaseMenu_Click(object sender, RoutedEventArgs args) => DeleteDatabase();

        private async void CancelBackupMenu_Click(object sender, RoutedEventArgs args)
        {
            if (BackupManager.IsRunning)
                await BackupManager.CancelAllAsync();
        }

        private void AboutAppMenu_Click(object sender, RoutedEventArgs args) => new AboutWindow().ShowDialog();

        private void RepositoryLinkMenu_Click(object sender, RoutedEventArgs args) => Process.Start("explorer.exe", @"https://github.com/skyzi000/SkyziBackup");
    }
}
