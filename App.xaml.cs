using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Classify8.Core;

namespace Classify8
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private bool _ownsMutex; // この起動で Mutex を取得できたか (2つ目のインスタンスは所有していない)

        protected override void OnStartup(StartupEventArgs e)
        {
            // 表示言語を決める (設定が読めなければ OS の言語に合わせる)。メッセージを出す前に行う
            Loc.Initialize(DataManager.LoadSettings().Language);

            // タスクトレイに常駐するアプリなので、予期しない例外で無言のまま落ちないようにログに残す
            DispatcherUnhandledException += (s, args) =>
            {
                AppLog.Error("Unhandled exception (UI thread)", args.Exception);
                MessageBox.Show(
                    Loc.F("app.unhandled_error", args.Exception.Message),
                    "Classify8", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; // アプリは継続する
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                AppLog.Error("Unhandled exception", args.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                AppLog.Error("Unhandled exception (task)", args.Exception);
                args.SetObserved();
            };

            const string mutexName = "Classify8_SingleInstance_Mutex";

            _mutex = new Mutex(true, mutexName, out bool createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                MessageBox.Show(Loc.T("app.already_running"),
                                Loc.T("title.info"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                try
                {
                    // 所有していない Mutex を ReleaseMutex() すると例外になるため、所有時のみ解放する
                    if (_ownsMutex) _mutex.ReleaseMutex();
                }
                catch (ApplicationException) { }

                _mutex.Dispose();
                _mutex = null;
            }

            base.OnExit(e);
        }
    }
}
