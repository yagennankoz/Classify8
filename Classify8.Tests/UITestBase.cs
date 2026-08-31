using System;
using System.IO;
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

        public UITestBase()
        {
            Automation = new UIA3Automation();
            string exePath = GetAppPath();

            if (!File.Exists(exePath))
                throw new FileNotFoundException($"テスト対象のEXEが見つかりません: {exePath}");

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
            App?.Close();
            Automation?.Dispose();
        }

        private string GetAppPath()
        {
            return @"..\..\bin\Debug\net8.0-windows\Classify8.exe";
        }
    }
}