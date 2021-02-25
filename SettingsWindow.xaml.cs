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
            NoComparisonLBI.Selected += (s, e) => ComparisonMethodListBox.SelectedIndex = 0;
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
            settings.IgnorePattern = ignorePatternBox.Text;
            settings.isUseDatabase = isUseDatabaseCheckBox.IsChecked ?? settings.isUseDatabase;
            settings.isRecordPassword = isRecordPasswordCheckBox.IsChecked ?? settings.isRecordPassword;
            settings.isOverwriteReadonly = isOverwriteReadonlyCheckBox.IsChecked ?? settings.isOverwriteReadonly;
            settings.isEnableDeletion = isEnableDeletionCheckBox.IsChecked ?? settings.isEnableDeletion;
            settings.isCopyAttributes = isCopyAttributesCheckBox.IsChecked ?? settings.isCopyAttributes;
            settings.retryCount = int.TryParse(RetryCountTextBox.Text, out int rcount) ? rcount : settings.retryCount;
            settings.retryWaitMilliSec = int.TryParse(RetryWaitTimeTextBox.Text, out int wait) ? wait : settings.retryWaitMilliSec;
            settings.compressAlgorithm = (Skyzi000.Cryptography.CompressAlgorithm)CompressAlgorithmComboBox.SelectedIndex;
            settings.compressionLevel = (System.IO.Compression.CompressionLevel)CompressionLevelSlider.Value;
            settings.passwordProtectionScope = (System.Security.Cryptography.DataProtectionScope)PasswordScopeComboBox.SelectedIndex;
            settings.comparisonMethod = 0;
            foreach (var item in ComparisonMethodListBox.SelectedItems)
            {
                int i = ComparisonMethodListBox.Items.IndexOf(item);
                if (i == 0)
                {
                    settings.comparisonMethod = ComparisonMethod.NoComparison;
                    break;
                }
                settings.comparisonMethod |= (ComparisonMethod)(1 << (i - 1));
            }
            MessageBox.Show(settings.ToString());
        }
    }
}
