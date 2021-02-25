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
        private static Regex nonNumber = new Regex(@"\D", RegexOptions.Compiled);
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
            ContentRendered += (s, e) =>
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
                PasswordScopeComboBox.SelectedItem = settings.passwordProtectionScope;
                NoComparisonLBI.IsSelected = settings.comparisonMethod == ComparisonMethod.NoComparison;
                NoComparisonLBI.Selected += (s, e) => ComparisonMethodListBox.SelectedIndex = 0;
                ArchiveAttributeLBI.IsSelected = settings.comparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute);
                WriteTimeLBI.IsSelected = settings.comparisonMethod.HasFlag(ComparisonMethod.WriteTime);
            };
        }
        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = nonNumber.IsMatch(e.Text);
        }

        private void TextBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                e.Handled = true;
            }
        }
    }
}
