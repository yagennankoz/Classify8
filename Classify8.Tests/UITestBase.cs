using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Classify8.Tests
{
    // すべてのUIテストの土台となるクラス
    public abstract class UITestBase : IDisposable
    {
        protected Application App { get; private set; }
        protected UIA3Automation Automation { get; private set; }
        protected Window MainWindow { get; private set; }

        private string _sandboxDir;

        public UITestBase()
        {
            Automation = new UIA3Automation();
            string builtExe = GetAppPath();

            if (!File.Exists(builtExe))
                throw new FileNotFoundException($"テスト対象のEXEが見つかりません: {builtExe}");

            // ビルド出力を一時フォルダにコピーして、そこから起動する。
            // ・本物の設定・ルール・履歴にテスト用のデータを書き込まない
            // ・前回のテストの残りがなく、毎回まっさらな状態で始まる
            string exePath = CreateSandbox(builtExe);

            // アプリ起動
            App = Application.Launch(exePath);
            Thread.Sleep(2000); // 起動待機

            MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(5));
            if (MainWindow.Patterns.Window.IsSupported)
            {
                MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            }
        }

        public void Dispose()
        {
            // テストが1つ終わるたびにアプリを確実に閉じる
            try { App?.Close(); } catch { }

            // 次のテストが起動したときに「既に起動しています」(多重起動防止)で止まらないよう、
            // プロセスが完全に終了するまで待つ。閉じられなければ強制終了する。
            if (App != null)
            {
                var sw = Stopwatch.StartNew();
                while (!HasExited() && sw.ElapsedMilliseconds < 5000) Thread.Sleep(100);
                if (!HasExited())
                {
                    try { App.Kill(); } catch { }
                }
            }

            Automation?.Dispose();
            DeleteSandbox();
        }

        // --- サンドボックス (アプリのコピー) ---------------------------------------

        // コピーしないファイル (アプリが保存するデータ)
        private static readonly string[] DataFileNames =
        {
            "settings.json", "presets.json", "rules.csv", "history.json",
            "history.json.corrupt", "error.log", "error.log.1"
        };

        private string CreateSandbox(string builtExe)
        {
            string sourceDir = Path.GetDirectoryName(builtExe);
            _sandboxDir = Path.Combine(Path.GetTempPath(), "c8ui_app_" + Guid.NewGuid().ToString("N"));
            CopyAppFiles(sourceDir, _sandboxDir);
            return Path.Combine(_sandboxDir, Path.GetFileName(builtExe));
        }

        private static void CopyAppFiles(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string name = Path.GetFileName(file);
                if (DataFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                File.Copy(file, Path.Combine(destDir, name), true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyAppFiles(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }

        private void DeleteSandbox()
        {
            if (_sandboxDir == null) return;

            // プロセスが終了した直後はファイルが掴まれていることがあるので、数回リトライする
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Directory.Delete(_sandboxDir, true);
                    return;
                }
                catch
                {
                    Thread.Sleep(300);
                }
            }
        }

        private bool HasExited()
        {
            try { return App.HasExited; }
            catch { return true; }
        }

        private static string GetAppPath()
        {
            // 相対パス (カレントディレクトリ基準) が使えるなら、それを最優先にする
            string legacy = Path.GetFullPath(@"..\..\bin\Debug\net8.0-windows\Classify8.exe");
            if (File.Exists(legacy)) return legacy;

            // テストの実行フォルダから親フォルダをたどって、本体の出力フォルダを探す
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                foreach (var config in new[] { "Debug", "Release" })
                {
                    string candidate = Path.Combine(dir.FullName, "bin", config, "net8.0-windows", "Classify8.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                dir = dir.Parent;
            }

            // テストの出力フォルダに本体が一緒にコピーされている場合
            return Path.Combine(AppContext.BaseDirectory, "Classify8.exe");
        }
    }
}
