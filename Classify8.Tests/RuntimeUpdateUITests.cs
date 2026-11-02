using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using FlaUI.Core.AutomationElements;

namespace Classify8.Tests
{
    // 起動中に .NET ランタイムが更新された (古いバージョンのファイルが消えた) ときの動作を確認するテスト。
    // .NET ランタイムを一時フォルダにコピーし、アプリをそのコピーで起動してから DLL を1つ隠すことで、
    // Windows Update による更新と同じ状況を作る。本物のランタイムには触れない。
    // 操作中は他のウィンドウをクリックしたりキーボードを触ったりしないでください。
    [Collection("UI")]
    public class RuntimeUpdateUITests : UITestBase
    {
        // 振り分けの並列処理で初めて読み込まれる DLL (実際の不具合で見つからなかったもの)
        private const string HiddenDllName = "System.Threading.Tasks.Parallel.dll";

        private string _runtimeCopyDir;
        private string _coreRuntimeDir;

        protected override void ConfigureLaunch(ProcessStartInfo startInfo)
        {
            _runtimeCopyDir = Path.Combine(Path.GetTempPath(), "c8ui_dotnet_" + Guid.NewGuid().ToString("N"));
            _coreRuntimeDir = CopyRuntime(_runtimeCopyDir);

            // アプリの起動プログラム (Classify8.exe) は、この環境変数のフォルダにある .NET を使う
            startInfo.Environment["DOTNET_ROOT"] = _runtimeCopyDir;
            startInfo.Environment["DOTNET_ROOT_X64"] = _runtimeCopyDir;
        }

        protected override void OnAppClosed()
        {
            KillRestartedInstances();
            for (int i = 0; i < 5 && Directory.Exists(_runtimeCopyDir); i++)
            {
                try { Directory.Delete(_runtimeCopyDir, true); }
                catch { Thread.Sleep(500); }
            }
        }

        [Fact]
        public void 起動中にNETのファイルが消えたら振り分けを止めて再起動を勧め_はいで再起動する()
        {
            int firstProcessId = App.ProcessId;
            Assert.True(UsesRuntimeCopy(firstProcessId), "アプリがコピーした .NET で起動していません。");

            string dllPath = Path.Combine(_coreRuntimeDir, HiddenDllName);
            string hiddenPath = dllPath + ".hidden";
            Assert.True(File.Exists(dllPath), $"{HiddenDllName} が見つかりません。");

            // .NET の更新で古いファイルが消えた状態にする
            File.Move(dllPath, hiddenPath);

            // 1回目: 振り分けを実行しようとすると、再起動を勧められる。「いいえ」なら、アプリはそのまま動き続ける
            ClickRunAll();
            var dialog1 = FindRestartDialog();
            Assert.NotNull(dialog1);
            Assert.Contains(".NET", GetDialogText(dialog1));
            ClickButton(dialog1, "7"); // いいえ
            Thread.Sleep(1500);
            Assert.False(Process.GetProcessById(firstProcessId).HasExited, "「いいえ」を選んだのにアプリが終了しました。");

            // 2回目: もう一度実行しようとしても、再起動を勧められる (振り分けは始まらない)
            ClickRunAll();
            var dialog2 = FindRestartDialog();
            Assert.NotNull(dialog2);

            // 更新後のランタイムが揃った状態 (再起動すれば正しく動く状態) に戻してから「はい」を押す
            File.Move(hiddenPath, dllPath);
            ClickButton(dialog2, "6"); // はい

            // 元のアプリが終了し、新しいアプリが起動する
            Assert.True(WaitUntil(() => HasExited(firstProcessId)), "「はい」を選んだのにアプリが終了しませんでした。");
            Assert.True(WaitUntil(() => FindRestartedInstances(firstProcessId).Length > 0),
                "アプリが再起動しませんでした。");
        }

        // --- 補助 ----------------------------------------------------------------

        // 実行中の .NET (このテスト自身と同じバージョン) のうち、アプリの起動に必要な部分をコピーする。
        // 戻り値は、コピー先の基本ランタイムのフォルダ
        private static string CopyRuntime(string destRoot)
        {
            string coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            string desktopDir = Path.GetDirectoryName(typeof(System.Windows.Window).Assembly.Location);
            string version = Path.GetFileName(coreDir);
            string installRoot = Path.GetFullPath(Path.Combine(coreDir, "..", "..", ".."));

            CopyDirectory(Path.Combine(installRoot, "host"), Path.Combine(destRoot, "host"));
            string coreDest = Path.Combine(destRoot, "shared", "Microsoft.NETCore.App", version);
            CopyDirectory(coreDir, coreDest);
            CopyDirectory(desktopDir, Path.Combine(destRoot, "shared", "Microsoft.WindowsDesktop.App", Path.GetFileName(desktopDir)));
            return coreDest;
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private bool UsesRuntimeCopy(int processId)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                return p.Modules.Cast<ProcessModule>().Any(m =>
                    m.FileName.StartsWith(_runtimeCopyDir, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        // サンドボックスの exe から起動した、元のアプリ以外のプロセス
        private Process[] FindRestartedInstances(int firstProcessId)
        {
            string sandbox = SandboxDir;
            return Process.GetProcessesByName("Classify8").Where(p =>
            {
                try
                {
                    return p.Id != firstProcessId && sandbox != null &&
                           p.MainModule.FileName.StartsWith(sandbox, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }).ToArray();
        }

        private void KillRestartedInstances()
        {
            foreach (var p in FindRestartedInstances(-1))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch { }
            }
        }

        private static bool HasExited(int processId)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                return p.HasExited;
            }
            catch (ArgumentException)
            {
                return true; // もう存在しない
            }
        }

        private void ClickRunAll()
        {
            var button = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btnRunAll"))?.AsButton();
            Assert.NotNull(button);
            button.Click();
        }

        // 再起動を勧めるメッセージ (「はい」「いいえ」のあるメッセージボックス) を探す
        private Window FindRestartDialog(int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var dialog = MainWindow.ModalWindows.FirstOrDefault(w =>
                                 w.FindFirstDescendant(cf => cf.ByAutomationId("6")) != null)
                             ?? Automation.GetDesktop().FindFirstChild(cf => cf.ByName("確認"))?.AsWindow();
                if (dialog != null) return dialog;
                Thread.Sleep(200);
            }
            return null;
        }

        private static string GetDialogText(Window dialog)
        {
            var texts = dialog.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
            return string.Join("\n", texts.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)));
        }

        // メッセージボックスの標準ID (はい=6, いいえ=7) でボタンを押す
        private static void ClickButton(Window dialog, string standardId)
        {
            var button = dialog.FindFirstDescendant(cf => cf.ByAutomationId(standardId))?.AsButton();
            Assert.NotNull(button);
            button.Click();
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 20000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(200);
            }
            return condition();
        }
    }
}
