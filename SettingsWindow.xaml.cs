using System;
using System.Collections.Generic;
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
            ContentRendered += (s, e) =>
            {
                dataPath.Text = Properties.Settings.Default.AppDataPath;
            };
        }
        public SettingsWindow(BackupSettings settings) : this()
        {
            this.settings = settings;
            ContentRendered += (s, e) =>
            {
                DisplaySettings();
                NoComparisonLBI.Selected += (s, e) => ComparisonMethodListBox.SelectedIndex = 0;
            };
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
            var newSettings = new BackupSettings();
            newSettings.IgnorePattern = ignorePatternBox.Text;
            newSettings.isUseDatabase = isUseDatabaseCheckBox.IsChecked ?? settings.isUseDatabase;
            newSettings.isRecordPassword = isRecordPasswordCheckBox.IsChecked ?? settings.isRecordPassword;
            newSettings.isOverwriteReadonly = isOverwriteReadonlyCheckBox.IsChecked ?? settings.isOverwriteReadonly;
            newSettings.isEnableDeletion = isEnableDeletionCheckBox.IsChecked ?? settings.isEnableDeletion;
            newSettings.isCopyAttributes = isCopyAttributesCheckBox.IsChecked ?? settings.isCopyAttributes;
            newSettings.retryCount = int.TryParse(RetryCountTextBox.Text, out int rcount) ? rcount : settings.retryCount;
            newSettings.retryWaitMilliSec = int.TryParse(RetryWaitTimeTextBox.Text, out int wait) ? wait : settings.retryWaitMilliSec;
            newSettings.compressAlgorithm = (Skyzi000.Cryptography.CompressAlgorithm)CompressAlgorithmComboBox.SelectedIndex;
            newSettings.compressionLevel = (System.IO.Compression.CompressionLevel)CompressionLevelSlider.Value;
            newSettings.passwordProtectionScope = (System.Security.Cryptography.DataProtectionScope)PasswordScopeComboBox.SelectedIndex;
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
            if (settings.ToString() != newSettings.ToString()) // TODO: できればもうちょっとましな比較方法にしたい
            {
                var r = MessageBox.Show("設定を保存しますか？", "設定変更の確認", MessageBoxButton.YesNoCancel);
                if (r == MessageBoxResult.Yes)
                {
                    settings = newSettings;
                    DataContractWriter.Write(settings);
                }
                else if (r == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
