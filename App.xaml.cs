using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

                // .NET の更新で DLL が消えた場合は、何度でも同じエラーになるため、再起動を勧める
                if (RuntimeFileCheck.IsAssemblyLoadError(args.Exception) && !IsRuntimeIntact())
                {
                    args.Handled = true;
                    PromptRestartForRuntimeUpdate();
                    return;
                }

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

            // 起動時点の .NET ランタイムのファイルを記録しておき、後で消えていないかを確かめる
            _runtimeFiles = RuntimeFileCheck.CaptureCurrentRuntime(
                typeof(Window).Assembly, typeof(System.Windows.Forms.Form).Assembly);

            // 再起動に使う DLL は、ランタイムのファイルが消える前に読み込んでおく
            PreloadRestartAssemblies();

            // テキストボックスで文字を選択しているときは、右クリックしても選択を解除しない。
            // WPF の標準では、選択範囲の外を右クリックすると右クリックメニューを開く際にカーソルが移動して選択が外れ、
            // 切り取り・コピーが効かなくなるため。右ボタンを押した時点の選択範囲を覚えておき、メニューを開いた後に戻す。
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseRightButtonDownEvent,
                new MouseButtonEventHandler(RememberSelectionOnRightClick));
            EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.ContextMenuOpeningEvent,
                new ContextMenuEventHandler(RestoreSelectionOnContextMenu), handledEventsToo: true);

            // 文字が欄に収まらない (横にスクロールする) テキストボックスをダブルクリックすると、WPF の標準では
            // ボタンを離した後もテキストボックスがマウスを捕まえたままになり、次のクリックが1回効かなくなる。
            // ボタンを離した後に捕まえたままなら解放する。
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler(ReleaseCaptureAfterClick), handledEventsToo: true);

            base.OnStartup(e);
        }

        private static void ReleaseCaptureAfterClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is TextBox box)) return;

            box.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Mouse.LeftButton == MouseButtonState.Released &&
                    Mouse.Captured is DependencyObject captured &&
                    (captured == box || box.IsAncestorOf(captured)))
                {
                    Mouse.Capture(null);
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private static TextBox _rightClickedBox;
        private static int _rightClickSelectionStart;
        private static int _rightClickSelectionLength;

        private static void RememberSelectionOnRightClick(object sender, MouseButtonEventArgs e)
        {
            _rightClickedBox = null;
            if (sender is TextBox box && box.IsKeyboardFocusWithin && box.SelectionLength > 0)
            {
                _rightClickedBox = box;
                _rightClickSelectionStart = box.SelectionStart;
                _rightClickSelectionLength = box.SelectionLength;
                e.Handled = true; // 右ボタンを押した時点でのカーソル移動を止める
            }
        }

        private static void RestoreSelectionOnContextMenu(object sender, ContextMenuEventArgs e)
        {
            if (!(sender is TextBox box) || box != _rightClickedBox) return;

            int start = _rightClickSelectionStart;
            int length = _rightClickSelectionLength;
            _rightClickedBox = null;

            // メニューを開く処理の中でカーソルが移動するため、その後に選択範囲を戻す
            box.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (start + length <= box.Text.Length) box.Select(start, length);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // ==========================================
        // .NET ランタイムの更新への対応
        // ==========================================
        private static RuntimeFileCheck _runtimeFiles;
        private static bool _runtimeErrorLogged;
        private static bool _restartPromptOpen;
        private static bool _restartRequested;

        /// <summary>起動時に使っていた .NET ランタイムのファイルがすべて残っているか</summary>
        public static bool IsRuntimeIntact()
        {
            string missing = _runtimeFiles?.FindMissingFile();
            if (missing == null) return true;

            if (!_runtimeErrorLogged)
            {
                _runtimeErrorLogged = true;
                AppLog.Error("The .NET runtime was updated while running. Missing file: " + missing, null);
            }
            return false;
        }

        /// <summary>.NET が更新されたことを知らせ、再起動するかを尋ねる。「はい」なら再起動する</summary>
        public static void PromptRestartForRuntimeUpdate()
        {
            if (_restartPromptOpen || _restartRequested) return;
            _restartPromptOpen = true;
            try
            {
                var owner = Current?.MainWindow;
                string message = Loc.T("msg.runtime_updated");
                var answer = owner != null && owner.IsVisible
                    ? MessageBox.Show(owner, message, Loc.T("title.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    : MessageBox.Show(message, Loc.T("title.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (answer == MessageBoxResult.Yes)
                {
                    // メッセージの元になった操作 (メニューのクリックなど) が終わってから閉じる
                    Current.Dispatcher.BeginInvoke(new Action(RequestRestart));
                }
            }
            finally
            {
                _restartPromptOpen = false;
            }
        }

        private static void RequestRestart()
        {
            if (!(Current?.MainWindow is MainWindow main)) return;

            _restartRequested = true;

            // 開いている画面 (設定画面など) を閉じる。振り分けの実行中は、メイン画面の終了処理に任せる
            if (!main.IsSorting)
            {
                foreach (Window w in Current.Windows)
                {
                    if (w != main) w.Close();
                }
            }

            // メイン画面を閉じると、保存などの終了処理の後にアプリが終了し、OnExit で起動し直す
            main.Close();

            // 閉じるのを取りやめた場合 (振り分けの中止を断った場合) は、再起動もしない
            if (PresentationSource.FromVisual(main) != null && !main.IsExitPending)
            {
                _restartRequested = false;
            }
        }

        private static void PreloadRestartAssemblies()
        {
            _ = typeof(System.Diagnostics.Process).TypeHandle;
            _ = typeof(System.Diagnostics.ProcessStartInfo).TypeHandle;
        }

        private static void StartNewInstance()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to restart the application.", ex);
            }
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

            // Mutex を解放した後に起動する (先に起動すると、多重起動として止められる)
            if (_restartRequested && _ownsMutex) StartNewInstance();

            base.OnExit(e);
        }
    }
}
