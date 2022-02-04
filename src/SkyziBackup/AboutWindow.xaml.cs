using System;
using System.IO;
using System.Text;
using System.Windows;
using NLog;

namespace SkyziBackup
{
    /// <summary>
    /// AboutWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            AppName.Content = App.AssemblyName.Name;
            VersionText.Text = $"Version {App.AssemblyName.Version}";
            try
            {
                using var sr = new StreamReader(
                    Application.GetResourceStream(new Uri("LICENSE", UriKind.Relative))?.Stream ?? throw new IOException("ResourceStream is null."),
                    Encoding.UTF8);
                ThisLicenseBlock.Text = sr.ReadToEnd();
            }
            catch (IOException e)
            {
                LogManager.GetCurrentClassLogger().Error(e, "ライセンス情報が見つかりません。");
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs args) => Close();
    }
}
