using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Classify8.Core;

namespace Classify8.Views
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();

            var histories = HistoryManager.LoadHistories();
            dgHistory.ItemsSource = histories.OrderByDescending(h => h.Timestamp).ToList();
        }

        private void MenuOpenSource_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is SortHistory selected && Directory.Exists(selected.SourceDir))
            {
                Process.Start("explorer.exe", $"\"{selected.SourceDir}\"");
            }
        }

        private void MenuOpenDest_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is SortHistory selected && Directory.Exists(selected.DestDir))
            {
                Process.Start("explorer.exe", $"\"{selected.DestDir}\"");
            }
        }

        private void MenuOpenRule_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is SortHistory selected)
            {
                // 親画面(MainWindow)に、該当するルールの編集画面を開くよう依頼する
                if (this.Owner is MainWindow mainWindow)
                {
                    mainWindow.OpenRuleEditWindow(selected.RuleName);
                }
            }
        }
    }
}