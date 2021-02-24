using NLog;
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
    /// Restore.xaml の相互作用ロジック
    /// </summary>
    public partial class Restore : Window
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly SynchronizationContext _mainContext;
        public Restore()
        {
            InitializeComponent();
            _mainContext = SynchronizationContext.Current;
            ContentRendered += (s, e) =>
            {
                password.Password = MainWindow.LoadPasswordOrNull() ?? string.Empty;
            };
        }
        public Restore(string restoreSourcePath, string restoreDestinationPath) : this()
        {
            originPath.Text = restoreSourcePath;
            destPath.Text = restoreDestinationPath;
        }

        private async void restoreButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(originPath.Text.Trim()))
            {
                MessageBox.Show($"{originPathLabel.Content}が不正です。\n正しいディレクトリパスを入力してください。",
                                $"{MainWindow.AssemblyName.Name} - 警告",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }
            restoreButton.IsEnabled = false;
            BackupSettings settings = BackupSettings.LoadLocalSettingsOrNull(destPath.Text.Trim(), originPath.Text.Trim()) ?? BackupSettings.LoadGlobalSettingsOrNull() ?? MainWindow.GlobalBackupSettings;
            if (settings.isRecordPassword && settings.IsDifferentPassword(password.Password))
            {
                var changePassword = MessageBox.Show("入力されたパスワードが保存されているパスワードと異なります。\nこのまま続行しますか？",
                                                     $"{MainWindow.AssemblyName.Name} - パスワードの確認",
                                                     MessageBoxButton.YesNo,
                                                     MessageBoxImage.Information);
                switch (changePassword)
                {
                    case MessageBoxResult.Yes:
                        break;
                    case MessageBoxResult.No:
                    default:
                        MessageBox.Show("リストアを中止します。");
                        return;
                }
            }
            message.Text = $"'{originPath.Text.Trim()}' => '{destPath.Text.Trim()}'";
            message.Text += $"\nリストア開始: {DateTime.Now}\n";
            RestoreDirectory restore = new RestoreDirectory(originPath.Text.Trim(),
                                                            destPath.Text.Trim(),
                                                            password.Password,
                                                            settings,
                                                            copyAttributesOnDatabaseCheck.IsChecked,
                                                            copyOnlyAttributesCheck.IsChecked,
                                                            isEnableWriteDatabaseCheck.IsChecked);
            string m = message.Text;
            restore.Results.MessageChanged += (_s, _e) => { _mainContext.Post((d) => { message.Text = m + restore.Results.Message + "\n"; }, null); };
            await Task.Run(() => restore.StartRestore());
            restoreButton.IsEnabled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!restoreButton.IsEnabled)
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
