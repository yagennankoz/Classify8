using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace Classify8.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            var assembly = Assembly.GetExecutingAssembly();
            var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string versionText = infoVersionAttr?.InformationalVersion ?? "1.0.0";

            int plusIndex = versionText.IndexOf('+');
            if (plusIndex > 0)
            {
                versionText = versionText.Substring(0, plusIndex);
            }

            txtVersion.Text = $"バージョン: {versionText}";
        }

        // ==========================================
        // リンクがクリックされた時の処理
        // ==========================================
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // .NET Core 以降でURLを開くための書き方 (UseShellExecute = true が必須)
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}