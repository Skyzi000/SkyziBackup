using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using NLog;
using Skyzi000.Cryptography;
using SkyziBackup.Data;
using SkyziBackup.Properties;

namespace SkyziBackup
{
    /// <summary>
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private static readonly Regex NonNumber = new(@"\D", RegexOptions.Compiled);
        private BackupSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();
            dataPath.Text = Settings.Default.AppDataPath;
            _settings = BackupSettings.Default;
            ContentRendered += (_, _) =>
            {
                NoComparisonLBI.Selected += (_, _) => ComparisonMethodListBox.SelectedIndex = 0;
                VersioningPanel.IsEnabled = (int)_settings.Versioning >= (int)VersioningMethod.Replace;
                if ((SymbolicLinkHandling)SymbolicLinkHandlingBox.SelectedIndex == SymbolicLinkHandling.Direct)
                    SymbolicLinkHandlingBox.IsEnabled = false;
            };
        }

        /// <summary>
        /// 受け取った設定をもとに設定画面を開く。設定画面を閉じた後にリロードする必要がある。
        /// </summary>
        public SettingsWindow(BackupSettings settings) : this()
        {
            _settings = settings;
            DisplaySettings();
        }

        /// <summary>
        /// ローカル設定をファイルから読み込んで変更する。対応するローカル設定が存在していなければ新規作成する。
        /// </summary>
        public SettingsWindow(string originBaseDirPath, string destBaseDirPath) : this()
        {
            _settings = BackupSettings.TryLoadLocalSettings(originBaseDirPath, destBaseDirPath, out var settings)
                ? settings
                : BackupSettings.Default.ConvertToLocalSettings(originBaseDirPath, destBaseDirPath);
            _settings.OriginBaseDirPath = originBaseDirPath;
            _settings.DestBaseDirPath = destBaseDirPath;
            DisplaySettings();
        }

        private void DisplaySettings()
        {
            settingsPath.Text = DataFileWriter.GetPath(_settings);
            ignorePatternBox.Text = _settings.IgnorePattern;
            isUseDatabaseCheckBox.IsChecked = _settings.IsUseDatabase;
            isRecordPasswordCheckBox.IsChecked = _settings.IsRecordPassword;
            isOverwriteReadonlyCheckBox.IsChecked = _settings.IsOverwriteReadonly;
            //isEnableTempFileCheckBox = 
            isEnableDeletionCheckBox.IsChecked = _settings.IsEnableDeletion;
            if (_settings.Versioning == VersioningMethod.RecycleBin)
                RecycleButton.IsChecked = true;
            else if (_settings.Versioning == VersioningMethod.PermanentDeletion)
                PermanentButton.IsChecked = true;
            else
            {
                VersioningButton.IsChecked = true;
                RevisionDirectory.Text = _settings.RevisionsDirPath;
                VersioningMethodBox.SelectedValue = ((int)_settings.Versioning).ToString();
            }

            isCopyAttributesCheckBox.IsChecked = _settings.IsCopyAttributes;
            RetryCountTextBox.Text = _settings.RetryCount.ToString();
            RetryWaitTimeTextBox.Text = _settings.RetryWaitMilliSec.ToString();
            CompressAlgorithmComboBox.SelectedIndex = (int)_settings.CompressAlgorithm;
            CompressionLevelSlider.Value = -((int)_settings.CompressionLevel - 2); // 最大値で引いてから符号を反転することでSliderの表示に合わせている
            PasswordScopeComboBox.SelectedIndex = (int)_settings.PasswordProtectionScope;
            NoComparisonLBI.IsSelected = _settings.ComparisonMethod == ComparisonMethod.NoComparison;
            ArchiveAttributeLBI.IsSelected = _settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute);
            WriteTimeLBI.IsSelected = _settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime);
            SizeLBI.IsSelected = _settings.ComparisonMethod.HasFlag(ComparisonMethod.Size);
            SHA1LBI.IsSelected = _settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1);
            BinaryLBI.IsSelected = _settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsBinary);
            SymbolicLinkHandlingBox.SelectedIndex = (int)_settings.SymbolicLink;
            IsCancelableBox.IsChecked = _settings.IsCancelable;
        }

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs args) => args.Handled = NonNumber.IsMatch(args.Text);

        private void TextBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs args)
        {
            if (args.Command == ApplicationCommands.Paste)
                args.Handled = true;
        }

        private BackupSettings GetNewSettings()
        {
            var newSettings = _settings.OriginBaseDirPath is null || _settings.DestBaseDirPath is null
                ? new BackupSettings()
                : new BackupSettings(_settings.OriginBaseDirPath, _settings.DestBaseDirPath);
            newSettings.IgnorePattern = ignorePatternBox.Text;
            newSettings.IsUseDatabase = isUseDatabaseCheckBox.IsChecked ?? _settings.IsUseDatabase;
            newSettings.IsRecordPassword = isRecordPasswordCheckBox.IsChecked ?? _settings.IsRecordPassword;
            newSettings.IsOverwriteReadonly = isOverwriteReadonlyCheckBox.IsChecked ?? _settings.IsOverwriteReadonly;
            newSettings.IsEnableDeletion = isEnableDeletionCheckBox.IsChecked ?? _settings.IsEnableDeletion;
            if (RecycleButton.IsChecked ?? false)
                newSettings.Versioning = VersioningMethod.RecycleBin;
            else if (PermanentButton.IsChecked ?? false)
                newSettings.Versioning = VersioningMethod.PermanentDeletion;
            else if (VersioningButton.IsChecked ?? false)
                newSettings.Versioning = (VersioningMethod)int.Parse(VersioningMethodBox.SelectedValue.ToString() ?? "0");
            else
                newSettings.Versioning = _settings.Versioning;
            newSettings.RevisionsDirPath = RevisionDirectory.Text;
            newSettings.IsCopyAttributes = isCopyAttributesCheckBox.IsChecked ?? _settings.IsCopyAttributes;
            newSettings.RetryCount = int.TryParse(RetryCountTextBox.Text, out var rcount) ? rcount : _settings.RetryCount;
            newSettings.RetryWaitMilliSec = int.TryParse(RetryWaitTimeTextBox.Text, out var wait) ? wait : _settings.RetryWaitMilliSec;
            newSettings.CompressAlgorithm = (CompressAlgorithm)CompressAlgorithmComboBox.SelectedIndex;
            newSettings.CompressionLevel = (CompressionLevel)(-(CompressionLevelSlider.Value - 2));
            newSettings.PasswordProtectionScope = (DataProtectionScope)PasswordScopeComboBox.SelectedIndex;
            if (newSettings.IsRecordPassword)
                newSettings.ProtectedPassword = _settings.ProtectedPassword;
            newSettings.ComparisonMethod = 0;
            foreach (var item in ComparisonMethodListBox.SelectedItems)
            {
                var i = ComparisonMethodListBox.Items.IndexOf(item);
                if (i == 0)
                {
                    newSettings.ComparisonMethod = ComparisonMethod.NoComparison;
                    break;
                }

                newSettings.ComparisonMethod |= (ComparisonMethod)(1 << (i - 1));
            }

            newSettings.SymbolicLink = (SymbolicLinkHandling)SymbolicLinkHandlingBox.SelectedIndex;
            newSettings.IsCancelable = IsCancelableBox.IsChecked ?? newSettings.IsCancelable;
            return newSettings;
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs args)
        {
            if (MessageBox.Show("設定を初期値にリセットします。よろしいですか？", "設定リセットの確認", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                _settings = _settings.IsDefault || _settings.OriginBaseDirPath is null || _settings.DestBaseDirPath is null
                    ? new BackupSettings()
                    : new BackupSettings(_settings.OriginBaseDirPath, _settings.DestBaseDirPath);
                DataFileWriter.Write(_settings);
                DisplaySettings();
            }
        }

        private void VersioningButton_Checked(object sender, RoutedEventArgs args)
        {
            if (VersioningPanel != null)
                VersioningPanel.IsEnabled = true;
        }

        private void PermanentOrRecycleBinButton_Checked(object sender, RoutedEventArgs args)
        {
            if (VersioningPanel != null)
                VersioningPanel.IsEnabled = false;
        }

        private void SaveNewSettings(BackupSettings newSettings)
        {
            if (Settings.Default.AppDataPath != dataPath.Text && Directory.Exists(dataPath.Text))
            {
                Settings.Default.AppDataPath = dataPath.Text;
                Settings.Default.Save();
                dataPath.Text = Settings.Default.AppDataPath;
                GlobalDiagnosticsContext.Set("AppDataPath", dataPath.Text);
            }

            _settings = newSettings;
            _settings.Save();
        }

        private void OkButton_Click(object sender, RoutedEventArgs args)
        {
            var newSettings = GetNewSettings();
            if ((int)newSettings.Versioning >= (int)VersioningMethod.Replace && !Directory.Exists(newSettings.RevisionsDirPath))
            {
                MessageBox.Show("バージョン管理の移動先ディレクトリが存在しません。\n正しいパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_settings.SymbolicLink != newSettings.SymbolicLink && newSettings.SymbolicLink == SymbolicLinkHandling.Direct)
            {
                if (MessageBoxResult.OK != MessageBox.Show(
                        "リパースポイント(シンボリックリンク/ジャンクション)を直接コピーするモードを本当に有効にしますか？\n" +
                        "※設定や操作次第ではリンクターゲットを意図せず削除してしまう場合があります。\n" +
                        "　そういった危険を減らすために、これ以降この設定を変更できないようになります。",
                        $"{App.AssemblyName.Name} - 確認", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel))
                    return;
            }

            SaveNewSettings(newSettings);
            DisplaySettings();
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs args)
        {
            var newSettings = GetNewSettings();
            if (!File.Exists(DataFileWriter.GetPath(newSettings)) || _settings.ToString() != newSettings.ToString()) // TODO: できればもうちょっとましな比較方法にしたい
            {
                var r = MessageBox.Show("設定を保存しますか？", "設定変更の確認", MessageBoxButton.YesNoCancel);
                if (r == MessageBoxResult.Yes)
                {
                    if ((int)newSettings.Versioning >= (int)VersioningMethod.Replace && !Directory.Exists(newSettings.RevisionsDirPath))
                    {
                        MessageBox.Show("バージョン管理の移動先ディレクトリが存在しません。\n正しいパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        args.Cancel = true;
                        return;
                    }

                    if (_settings.SymbolicLink != newSettings.SymbolicLink && newSettings.SymbolicLink == SymbolicLinkHandling.Direct)
                    {
                        if (MessageBoxResult.OK != MessageBox.Show(
                                "リパースポイント(シンボリックリンク/ジャンクション)を直接コピーするモードを本当に有効にしますか？\n" +
                                "※設定や操作次第ではリンクターゲットを意図せず削除してしまう場合があります。\n" +
                                "　そういった危険を減らすために、これ以降この設定を変更できないようになります。",
                                $"{App.AssemblyName.Name} - 確認", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel))
                        {
                            args.Cancel = true;
                            return;
                        }
                    }

                    SaveNewSettings(newSettings);
                }
                else if (r == MessageBoxResult.Cancel)
                    args.Cancel = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs args)
        {
            DisplaySettings();
            Close();
        }
    }
}
