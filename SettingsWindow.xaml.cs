using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private static readonly Regex NonNumber = new Regex(@"\D", RegexOptions.Compiled);
        private BackupSettings settings;
        public SettingsWindow()
        {
            InitializeComponent();
            dataPath.Text = Properties.Settings.Default.AppDataPath;
            ContentRendered += (s, e) =>
            {
                NoComparisonLBI.Selected += (s, e) => ComparisonMethodListBox.SelectedIndex = 0;
                VersioningPanel.IsEnabled = (int)settings.Versioning >= (int)VersioningMethod.Replace;
            };
            settings = BackupSettings.Default;
        }
        /// <summary>
        /// 受け取った設定をもとに設定画面を開く。設定画面を閉じた後にリロードする必要がある。
        /// </summary>
        public SettingsWindow(BackupSettings settings) : this()
        {
            this.settings = settings;
            DisplaySettings();
        }
        /// <summary>
        /// ローカル設定をファイルから読み込んで変更する。対応するローカル設定が存在していなければ新規作成する。
        /// </summary>
        public SettingsWindow(string originBaseDirPath, string destBaseDirPath) : this()
        {
            this.settings = BackupSettings.TryLoadLocalSettings(originBaseDirPath, destBaseDirPath, out var settings)
                ? settings
                : BackupSettings.Default.ConvertToLocalSettings(originBaseDirPath, destBaseDirPath);
            this.settings.OriginBaseDirPath = originBaseDirPath;
            this.settings.DestBaseDirPath = destBaseDirPath;
            DisplaySettings();
        }

        private void DisplaySettings()
        {
            settingsPath.Text = DataFileWriter.GetPath(settings);
            ignorePatternBox.Text = settings.IgnorePattern;
            isUseDatabaseCheckBox.IsChecked = settings.IsUseDatabase;
            isRecordPasswordCheckBox.IsChecked = settings.IsRecordPassword;
            isOverwriteReadonlyCheckBox.IsChecked = settings.IsOverwriteReadonly;
            //isEnableTempFileCheckBox = 
            isEnableDeletionCheckBox.IsChecked = settings.IsEnableDeletion;
            if (settings.Versioning == VersioningMethod.RecycleBin)
                RecycleButton.IsChecked = true;
            else if (settings.Versioning == VersioningMethod.PermanentDeletion)
                PermanentButton.IsChecked = true;
            else
            {
                VersioningButton.IsChecked = true;
                RevisionDirectory.Text = settings.RevisionsDirPath;
                VersioningMethodBox.SelectedValue = ((int)settings.Versioning).ToString();
            }
            isCopyAttributesCheckBox.IsChecked = settings.IsCopyAttributes;
            RetryCountTextBox.Text = settings.RetryCount.ToString();
            RetryWaitTimeTextBox.Text = settings.RetryWaitMilliSec.ToString();
            CompressAlgorithmComboBox.SelectedIndex = (int)settings.CompressAlgorithm;
            CompressionLevelSlider.Value = (double)settings.CompressionLevel;
            PasswordScopeComboBox.SelectedIndex = (int)settings.PasswordProtectionScope;
            NoComparisonLBI.IsSelected = settings.ComparisonMethod == ComparisonMethod.NoComparison;
            ArchiveAttributeLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute);
            WriteTimeLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime);
            SizeLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.Size);
            SHA1LBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1);
            BynaryLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsBynary);
        }

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = NonNumber.IsMatch(e.Text);
        }

        private void TextBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                e.Handled = true;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            BackupSettings newSettings = GetNewSettings();
            if (!File.Exists(DataFileWriter.GetPath(newSettings)) || settings.ToString() != newSettings.ToString()) // TODO: できればもうちょっとましな比較方法にしたい
            {
                var r = MessageBox.Show("設定を保存しますか？", "設定変更の確認", MessageBoxButton.YesNoCancel);
                if (r == MessageBoxResult.Yes)
                {
                    if ((int)newSettings.Versioning >= (int)VersioningMethod.Replace && !Directory.Exists(newSettings.RevisionsDirPath))
                    {
                        MessageBox.Show("バージョン管理の移動先ディレクトリが存在しません。\n正しいパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        e.Cancel = true;
                        return;
                    }
                    if (Properties.Settings.Default.AppDataPath != dataPath.Text && System.IO.Directory.Exists(dataPath.Text))
                    {
                        Properties.Settings.Default.AppDataPath = dataPath.Text;
                        Properties.Settings.Default.Save();
                        dataPath.Text = Properties.Settings.Default.AppDataPath;
                        NLog.GlobalDiagnosticsContext.Set("AppDataPath", dataPath.Text);
                    }
                    settings = newSettings;
                    DataFileWriter.Write(settings);
                }
                else if (r == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        private BackupSettings GetNewSettings()
        {
            BackupSettings newSettings = settings.IsDefault ? new BackupSettings() : new BackupSettings(settings.OriginBaseDirPath, settings.DestBaseDirPath);
            newSettings.IgnorePattern = ignorePatternBox.Text;
            newSettings.IsUseDatabase = isUseDatabaseCheckBox.IsChecked ?? settings.IsUseDatabase;
            newSettings.IsRecordPassword = isRecordPasswordCheckBox.IsChecked ?? settings.IsRecordPassword;
            newSettings.IsOverwriteReadonly = isOverwriteReadonlyCheckBox.IsChecked ?? settings.IsOverwriteReadonly;
            newSettings.IsEnableDeletion = isEnableDeletionCheckBox.IsChecked ?? settings.IsEnableDeletion;
            if (RecycleButton.IsChecked ?? false)
                newSettings.Versioning = VersioningMethod.RecycleBin;
            else if (PermanentButton.IsChecked ?? false)
                newSettings.Versioning = VersioningMethod.PermanentDeletion;
            else if (VersioningButton.IsChecked ?? false)
            {
                newSettings.Versioning = (VersioningMethod)int.Parse(VersioningMethodBox.SelectedValue.ToString());
            }
            else 
                newSettings.Versioning = settings.Versioning;
            newSettings.RevisionsDirPath = RevisionDirectory.Text;
            newSettings.IsCopyAttributes = isCopyAttributesCheckBox.IsChecked ?? settings.IsCopyAttributes;
            newSettings.RetryCount = int.TryParse(RetryCountTextBox.Text, out int rcount) ? rcount : settings.RetryCount;
            newSettings.RetryWaitMilliSec = int.TryParse(RetryWaitTimeTextBox.Text, out int wait) ? wait : settings.RetryWaitMilliSec;
            newSettings.CompressAlgorithm = (Skyzi000.Cryptography.CompressAlgorithm)CompressAlgorithmComboBox.SelectedIndex;
            newSettings.CompressionLevel = (System.IO.Compression.CompressionLevel)CompressionLevelSlider.Value;
            newSettings.PasswordProtectionScope = (System.Security.Cryptography.DataProtectionScope)PasswordScopeComboBox.SelectedIndex;
            if (newSettings.IsRecordPassword)
            {
                newSettings.ProtectedPassword = settings.ProtectedPassword;
            }
            newSettings.ComparisonMethod = 0;
            foreach (var item in ComparisonMethodListBox.SelectedItems)
            {
                int i = ComparisonMethodListBox.Items.IndexOf(item);
                if (i == 0)
                {
                    newSettings.ComparisonMethod = ComparisonMethod.NoComparison;
                    break;
                }
                newSettings.ComparisonMethod |= (ComparisonMethod)(1 << (i - 1));
            }

            return newSettings;
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("設定を初期値にリセットします。よろしいですか？", "設定リセットの確認", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                settings = settings.IsDefault ? new BackupSettings() : new BackupSettings(settings.OriginBaseDirPath, settings.DestBaseDirPath);
                DataFileWriter.Write(settings);
                DisplaySettings();
            }
        }

        private void VersioningButton_Checked(object sender, RoutedEventArgs e)
        {
            if (VersioningPanel != null)
                VersioningPanel.IsEnabled = true;
        }

        private void PermanentOrRecycleBinButton_Checked(object sender, RoutedEventArgs e)
        {
            if (VersioningPanel != null)
                VersioningPanel.IsEnabled = false;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            BackupSettings newSettings = GetNewSettings();
            if (!File.Exists(DataFileWriter.GetPath(newSettings)) || settings.ToString() != newSettings.ToString()) // TODO: できればもうちょっとましな比較方法にしたい
            {
                if ((int)newSettings.Versioning >= (int)VersioningMethod.Replace && !Directory.Exists(newSettings.RevisionsDirPath))
                {
                    MessageBox.Show("バージョン管理の移動先ディレクトリが存在しません。\n正しいパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (Properties.Settings.Default.AppDataPath != dataPath.Text && System.IO.Directory.Exists(dataPath.Text))
                {
                    Properties.Settings.Default.AppDataPath = dataPath.Text;
                    Properties.Settings.Default.Save();
                    dataPath.Text = Properties.Settings.Default.AppDataPath;
                    NLog.GlobalDiagnosticsContext.Set("AppDataPath", dataPath.Text);
                }
                settings = newSettings;
                DataFileWriter.Write(settings);
            }
            DisplaySettings();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DisplaySettings();
            Close();
        }
    }
}
