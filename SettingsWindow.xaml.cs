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
                VersioningPanel.IsEnabled = (int)settings.versioning >= (int)VersioningMethod.Replace;
            };
        }
        public SettingsWindow(ref BackupSettings settings) : this()
        {
            this.settings = settings;
            DisplaySettings();
        }
        /// <summary>
        /// ローカル設定をファイルから読み込んで変更する。対応するローカル設定が存在していなければ新規作成する。
        /// </summary>
        public SettingsWindow(string originBaseDirPath, string destBaseDirPath) : this()
        {
            if (BackupSettings.TryLoadLocalSettings(originBaseDirPath, destBaseDirPath, out var settings))
            {
                this.settings = settings;
            }
            else
            {
                this.settings = BackupSettings.GetGlobalSettings().ConvertToLocalSettings(originBaseDirPath, destBaseDirPath);
            }
            DisplaySettings();
        }

        private void DisplaySettings()
        {
            settingsPath.Text = DataContractWriter.GetPath(settings);
            ignorePatternBox.Text = settings.IgnorePattern;
            isUseDatabaseCheckBox.IsChecked = settings.isUseDatabase;
            isRecordPasswordCheckBox.IsChecked = settings.isRecordPassword;
            isOverwriteReadonlyCheckBox.IsChecked = settings.isOverwriteReadonly;
            //isEnableTempFileCheckBox = 
            isEnableDeletionCheckBox.IsChecked = settings.isEnableDeletion;
            if (settings.versioning == VersioningMethod.RecycleBin)
                RecycleButton.IsChecked = true;
            else if (settings.versioning == VersioningMethod.PermanentDeletion)
                PermanentButton.IsChecked = true;
            else
            {
                VersioningButton.IsChecked = true;
                RevisionDirectory.Text = settings.revisionsDirPath;
                VersioningMethodBox.SelectedValue = ((int)settings.versioning).ToString();
            }
            isCopyAttributesCheckBox.IsChecked = settings.isCopyAttributes;
            RetryCountTextBox.Text = settings.retryCount.ToString();
            RetryWaitTimeTextBox.Text = settings.retryWaitMilliSec.ToString();
            CompressAlgorithmComboBox.SelectedIndex = (int)settings.compressAlgorithm;
            CompressionLevelSlider.Value = (double)settings.compressionLevel;
            PasswordScopeComboBox.SelectedIndex = (int)settings.passwordProtectionScope;
            NoComparisonLBI.IsSelected = settings.comparisonMethod == ComparisonMethod.NoComparison;
            ArchiveAttributeLBI.IsSelected = settings.comparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute);
            WriteTimeLBI.IsSelected = settings.comparisonMethod.HasFlag(ComparisonMethod.WriteTime);
            SizeLBI.IsSelected = settings.comparisonMethod.HasFlag(ComparisonMethod.Size);
            SHA1LBI.IsSelected = settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1);
            BynaryLBI.IsSelected = settings.comparisonMethod.HasFlag(ComparisonMethod.FileContentsBynary);
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
            if (!File.Exists(DataContractWriter.GetPath(newSettings)) || settings.ToString() != newSettings.ToString()) // TODO: できればもうちょっとましな比較方法にしたい
            {
                var r = MessageBox.Show("設定を保存しますか？", "設定変更の確認", MessageBoxButton.YesNoCancel);
                if (r == MessageBoxResult.Yes)
                {
                    if ((int)newSettings.versioning >= (int)VersioningMethod.Replace && !Directory.Exists(newSettings.revisionsDirPath))
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
                    DataContractWriter.Write(settings);
                }
                else if (r == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        private BackupSettings GetNewSettings()
        {
            BackupSettings newSettings = settings.IsGlobal ? new BackupSettings() : new BackupSettings(settings.SaveFileName);
            newSettings.IgnorePattern = ignorePatternBox.Text;
            newSettings.isUseDatabase = isUseDatabaseCheckBox.IsChecked ?? settings.isUseDatabase;
            newSettings.isRecordPassword = isRecordPasswordCheckBox.IsChecked ?? settings.isRecordPassword;
            newSettings.isOverwriteReadonly = isOverwriteReadonlyCheckBox.IsChecked ?? settings.isOverwriteReadonly;
            newSettings.isEnableDeletion = isEnableDeletionCheckBox.IsChecked ?? settings.isEnableDeletion;
            if (RecycleButton.IsChecked ?? false)
                newSettings.versioning = VersioningMethod.RecycleBin;
            else if (PermanentButton.IsChecked ?? false)
                newSettings.versioning = VersioningMethod.PermanentDeletion;
            else if (VersioningButton.IsChecked ?? false)
            {
                newSettings.versioning = (VersioningMethod)int.Parse(VersioningMethodBox.SelectedValue.ToString());
            }
            else 
                newSettings.versioning = settings.versioning;
            newSettings.revisionsDirPath = RevisionDirectory.Text;
            newSettings.isCopyAttributes = isCopyAttributesCheckBox.IsChecked ?? settings.isCopyAttributes;
            newSettings.retryCount = int.TryParse(RetryCountTextBox.Text, out int rcount) ? rcount : settings.retryCount;
            newSettings.retryWaitMilliSec = int.TryParse(RetryWaitTimeTextBox.Text, out int wait) ? wait : settings.retryWaitMilliSec;
            newSettings.compressAlgorithm = (Skyzi000.Cryptography.CompressAlgorithm)CompressAlgorithmComboBox.SelectedIndex;
            newSettings.compressionLevel = (System.IO.Compression.CompressionLevel)CompressionLevelSlider.Value;
            newSettings.passwordProtectionScope = (System.Security.Cryptography.DataProtectionScope)PasswordScopeComboBox.SelectedIndex;
            if (newSettings.isRecordPassword)
            {
                newSettings.ProtectedPassword = settings.GetRawPassword();
            }
            newSettings.comparisonMethod = 0;
            foreach (var item in ComparisonMethodListBox.SelectedItems)
            {
                int i = ComparisonMethodListBox.Items.IndexOf(item);
                if (i == 0)
                {
                    newSettings.comparisonMethod = ComparisonMethod.NoComparison;
                    break;
                }
                newSettings.comparisonMethod |= (ComparisonMethod)(1 << (i - 1));
            }

            return newSettings;
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("設定を初期値にリセットします。よろしいですか？", "設定リセットの確認", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                settings = settings.IsGlobal ? new BackupSettings() : new BackupSettings(settings.SaveFileName);
                DataContractWriter.Write(settings);
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
            if (!File.Exists(DataContractWriter.GetPath(newSettings)) || settings.ToString() != newSettings.ToString()) // TODO: できればもうちょっとましな比較方法にしたい
            {
                if ((int)newSettings.versioning >= (int)VersioningMethod.Replace && !Directory.Exists(newSettings.revisionsDirPath))
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
                DataContractWriter.Write(settings);
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
