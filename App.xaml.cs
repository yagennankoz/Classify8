using System;
using System.Threading;
using System.Windows;

namespace Classify8
{
    public partial class App : Application
    {
        private Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "Classify8_SingleInstance_Mutex";

            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Classify8 は既に起動しています。\nタスクトレイ（画面右下のアイコン）を確認してください。",
                                "お知らせ", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // アプリが終了する際に、Mutexを解放してOSに返す
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }

            base.OnExit(e);
        }
    }
}