﻿using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using NLog;
using Skyzi000.Cryptography;
using SkyziBackup.Properties;

namespace SkyziBackup
{
    /// <summary>
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private static readonly Regex NonNumber = new(@"\D", RegexOptions.Compiled);
        private BackupSettings settings;

        public SettingsWindow()
        {
            InitializeComponent();
            dataPath.Text = Settings.Default.AppDataPath;
            settings = BackupSettings.Default;
            ContentRendered += (_, _) =>
            {
                NoComparisonLBI.Selected += (_, _) => ComparisonMethodListBox.SelectedIndex = 0;
                VersioningPanel.IsEnabled = (int) settings.Versioning >= (int) VersioningMethod.Replace;
                if ((SymbolicLinkHandling) SymbolicLinkHandlingBox.SelectedIndex == SymbolicLinkHandling.Direct)
                    SymbolicLinkHandlingBox.IsEnabled = false;
            };
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
                VersioningMethodBox.SelectedValue = ((int) settings.Versioning).ToString();
            }

            isCopyAttributesCheckBox.IsChecked = settings.IsCopyAttributes;
            RetryCountTextBox.Text = settings.RetryCount.ToString();
            RetryWaitTimeTextBox.Text = settings.RetryWaitMilliSec.ToString();
            CompressAlgorithmComboBox.SelectedIndex = (int) settings.CompressAlgorithm;
            CompressionLevelSlider.Value = -((int) settings.CompressionLevel - 2); // 最大値で引いてから符号を反転することでSliderの表示に合わせている
            PasswordScopeComboBox.SelectedIndex = (int) settings.PasswordProtectionScope;
            NoComparisonLBI.IsSelected = settings.ComparisonMethod == ComparisonMethod.NoComparison;
            ArchiveAttributeLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.ArchiveAttribute);
            WriteTimeLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.WriteTime);
            SizeLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.Size);
            SHA1LBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsSHA1);
            BinaryLBI.IsSelected = settings.ComparisonMethod.HasFlag(ComparisonMethod.FileContentsBinary);
            SymbolicLinkHandlingBox.SelectedIndex = (int) settings.SymbolicLink;
            IsCancelableBox.IsChecked = settings.IsCancelable;
        }

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs args) => args.Handled = NonNumber.IsMatch(args.Text);

        private void TextBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs args)
        {
            if (args.Command == ApplicationCommands.Paste)
                args.Handled = true;
        }

        private BackupSettings GetNewSettings()
        {
            BackupSettings newSettings = settings.OriginBaseDirPath is null || settings.DestBaseDirPath is null
                ? new BackupSettings()
                : new BackupSettings(settings.OriginBaseDirPath, settings.DestBaseDirPath);
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
                newSettings.Versioning = (VersioningMethod) int.Parse(VersioningMethodBox.SelectedValue.ToString() ?? "0");
            else
                newSettings.Versioning = settings.Versioning;
            newSettings.RevisionsDirPath = RevisionDirectory.Text;
            newSettings.IsCopyAttributes = isCopyAttributesCheckBox.IsChecked ?? settings.IsCopyAttributes;
            newSettings.RetryCount = int.TryParse(RetryCountTextBox.Text, out var rcount) ? rcount : settings.RetryCount;
            newSettings.RetryWaitMilliSec = int.TryParse(RetryWaitTimeTextBox.Text, out var wait) ? wait : settings.RetryWaitMilliSec;
            newSettings.CompressAlgorithm = (CompressAlgorithm) CompressAlgorithmComboBox.SelectedIndex;
            newSettings.CompressionLevel = (CompressionLevel) (-(CompressionLevelSlider.Value - 2));
            newSettings.PasswordProtectionScope = (DataProtectionScope) PasswordScopeComboBox.SelectedIndex;
            if (newSettings.IsRecordPassword)
                newSettings.ProtectedPassword = settings.ProtectedPassword;
            newSettings.ComparisonMethod = 0;
            foreach (var item in ComparisonMethodListBox.SelectedItems)
            {
                var i = ComparisonMethodListBox.Items.IndexOf(item);
                if (i == 0)
                {
                    newSettings.ComparisonMethod = ComparisonMethod.NoComparison;
                    break;
                }

                newSettings.ComparisonMethod |= (ComparisonMethod) (1 << (i - 1));
            }

            newSettings.SymbolicLink = (SymbolicLinkHandling) SymbolicLinkHandlingBox.SelectedIndex;
            newSettings.IsCancelable = IsCancelableBox.IsChecked ?? newSettings.IsCancelable;
            return newSettings;
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs args)
        {
            if (MessageBox.Show("設定を初期値にリセットします。よろしいですか？", "設定リセットの確認", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                settings = settings.IsDefault || settings.OriginBaseDirPath is null || settings.DestBaseDirPath is null
                    ? new BackupSettings()
                    : new BackupSettings(settings.OriginBaseDirPath, settings.DestBaseDirPath);
                DataFileWriter.Write(settings);
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

            settings = newSettings;
            settings.Save();
        }

        private void OkButton_Click(object sender, RoutedEventArgs args)
        {
            BackupSettings newSettings = GetNewSettings();
            if ((int) newSettings.Versioning >= (int) VersioningMethod.Replace && !Directory.Exists(newSettings.RevisionsDirPath))
            {
                MessageBox.Show("バージョン管理の移動先ディレクトリが存在しません。\n正しいパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (settings.SymbolicLink != newSettings.SymbolicLink && newSettings.SymbolicLink == SymbolicLinkHandling.Direct)
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
            BackupSettings newSettings = GetNewSettings();
            if (!File.Exists(DataFileWriter.GetPath(newSettings)) || settings.ToString() != newSettings.ToString()) // TODO: できればもうちょっとましな比較方法にしたい
            {
                var r = MessageBox.Show("設定を保存しますか？", "設定変更の確認", MessageBoxButton.YesNoCancel);
                if (r == MessageBoxResult.Yes)
                {
                    if ((int) newSettings.Versioning >= (int) VersioningMethod.Replace && !Directory.Exists(newSettings.RevisionsDirPath))
                    {
                        MessageBox.Show("バージョン管理の移動先ディレクトリが存在しません。\n正しいパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        args.Cancel = true;
                        return;
                    }

                    if (settings.SymbolicLink != newSettings.SymbolicLink && newSettings.SymbolicLink == SymbolicLinkHandling.Direct)
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
