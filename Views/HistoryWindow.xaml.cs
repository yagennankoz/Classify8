using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Runtime.InteropServices;
using Classify8.Core;

namespace Classify8.Views
{
    public partial class HistoryWindow : Window
    {
        // ==========================================
        // 最小化ボタンを無効化するための Windows API
        // ==========================================
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        private const int GWL_STYLE = -16;
        private const int WS_MINIMIZEBOX = 0x00020000;

        // ==========================================
        // 確実に選択＆スクロールさせるための Windows API
        // ==========================================
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPath(string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        public HistoryWindow()
        {
            InitializeComponent();

            var histories = HistoryManager.LoadHistories();
            dgHistory.ItemsSource = histories.OrderByDescending(h => h.Timestamp).ToList();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style & ~WS_MINIMIZEBOX);
        }

        private void OpenExplorerAndSelectFile(string filePath)
        {
            // ファイルのパスからWindows内部の識別ID(PIDL)を作成
            IntPtr pidl = ILCreateFromPath(filePath);
            if (pidl != IntPtr.Zero)
            {
                try
                {
                    // APIを直接叩いて、「ファイルを選択状態にし、必ず見える位置までスクロールする」ようにOSへ命令
                    SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
                }
                finally
                {
                    // メモリ解放
                    ILFree(pidl);
                }
            }
            else
            {
                // 万が一APIの呼び出しに失敗した場合は、従来のコマンドラインで開く
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
        }

        // ==========================================
        // イベントハンドラ
        // ==========================================
        private void MenuOpenSource_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is SortHistory selected && Directory.Exists(selected.SourceDir))
            {
                string fullPath = Path.Combine(selected.SourceDir, selected.FileName);

                if (File.Exists(fullPath))
                {
                    OpenExplorerAndSelectFile(fullPath);
                }
                else
                {
                    Process.Start("explorer.exe", $"\"{selected.SourceDir}\"");
                }
            }
        }

        private void MenuOpenDest_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is SortHistory selected && Directory.Exists(selected.DestDir))
            {
                string targetFile = string.IsNullOrEmpty(selected.NewFileName) ? selected.FileName : selected.NewFileName;
                string fullPath = Path.Combine(selected.DestDir, targetFile);

                if (File.Exists(fullPath))
                {
                    OpenExplorerAndSelectFile(fullPath);
                }
                else
                {
                    Process.Start("explorer.exe", $"\"{selected.DestDir}\"");
                }
            }
        }

        private void MenuOpenRule_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is SortHistory selected)
            {
                if (this.Owner is MainWindow mainWindow)
                {
                    mainWindow.OpenRuleEditWindow(selected.RuleId, selected.RuleName);
                }
            }
        }
    }
}