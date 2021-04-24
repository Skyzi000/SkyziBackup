using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                using StreamReader sr = new StreamReader(
                    Application.GetResourceStream(new Uri("LICENSE", UriKind.Relative)).Stream,
                    Encoding.UTF8);
                ThisLicenseBlock.Text = sr.ReadToEnd();
            }
            catch(IOException e)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(e, "ライセンス情報が見つかりません。");
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
