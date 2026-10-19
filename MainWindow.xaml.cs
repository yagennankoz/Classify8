using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using Classify8.Core;
using Classify8.Views;

namespace Classify8
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<SortRuleViewModel> RulesList { get; set; }

        public ObservableCollection<Preset> PresetList { get; set; }

        public AppSettings CurrentSettings { get; set; }

        private System.Windows.Threading.DispatcherTimer _periodicTimer;
        private bool _isSorting = false;
        private ProgressWindow _progressWin = null;
        private System.Windows.Forms.NotifyIcon _notifyIcon;

        private readonly System.Threading.SemaphoreSlim _saveLock = new System.Threading.SemaphoreSlim(1, 1);

        private System.Windows.Forms.ToolStripMenuItem _trayShowItem; // 表示言語の切り替えで文字を更新するため保持する
        private System.Windows.Forms.ToolStripMenuItem _trayExitItem;
        private System.Drawing.Icon _normalIcon;
        private bool _normalIconIsShared = false; // SystemIcons など、Dispose してはいけない共有アイコンか

        // 実行中の振り分け処理のキャンセル用トークンと、終了要求の状態
        private System.Threading.CancellationTokenSource _currentCts;
        private bool _exitRequested = false;
        private bool _sessionEnding = false;
        private System.Drawing.Icon _blankIcon;
        private System.Windows.Threading.DispatcherTimer _blinkTimer;
        private bool _isIconBlank = false;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public MainWindow()
        {
            InitializeComponent();

            // データの読み込み/保存エラーを画面に通知する (Core 層が UI に依存しないようにするため)
            DataManager.ErrorReporter = msg => Dispatcher.InvokeAsync(
                () => MessageBox.Show(msg, Loc.T("title.error"), MessageBoxButton.OK, MessageBoxImage.Error));

            // 表示言語が切り替わったときに、コードで設定している文字列を更新する
            Loc.LanguageChanged += OnLanguageChanged;

            // Windows のシャットダウン/ログオフ時は、確認なしで処理を中止して終了できるようにする
            Application.Current.SessionEnding += (s, args) =>
            {
                _sessionEnding = true;
                _currentCts?.Cancel();
            };

            CurrentSettings = DataManager.LoadSettings();

            ApplyProcessPriority(CurrentSettings.ProcessPriority);

            PresetList = new ObservableCollection<Preset>(DataManager.LoadPresets());
            RulesList = new ObservableCollection<SortRuleViewModel>();
            var loadedRules = DataManager.LoadRules();
            foreach (var rule in loadedRules)
            {
                RulesList.Add(new SortRuleViewModel(rule, PresetList));
            }

            lvRules.ItemsSource = RulesList;
            UpdateMenuState();

            InitializeTimer();

            // タスクトレイアイコンの設定
            InitializeNotifyIcon();

            // オプション「最小化で起動する」の反映
            if (CurrentSettings.StartMinimized)
            {
                this.WindowState = WindowState.Minimized;
            }
        }

        private SortRule _copyBuffer = null;
        private bool _isCutBuffer = false;
        private SortRuleViewModel _cutSource = null; // 切り取り中のルール (貼り付けるまでリストに残す)

        // リストの選択状態が変わるたびに呼ばれ、メニューの有効/無効を切り替える
        private void LvRules_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMenuState();
        }

        // メニューのグレーアウト状態を計算して更新する専用メソッド
        private void UpdateMenuState()
        {
            bool isSingleSelected = lvRules.SelectedItems.Count == 1;
            bool hasSelection = lvRules.SelectedItems.Count > 0;

            if (miCopy != null) miCopy.IsEnabled = isSingleSelected;
            if (miCut != null) miCut.IsEnabled = isSingleSelected;
            if (miOpenSource != null) miOpenSource.IsEnabled = isSingleSelected;
            if (miOpenDest != null) miOpenDest.IsEnabled = isSingleSelected;

            if (miPaste != null) miPaste.IsEnabled = isSingleSelected && _copyBuffer != null;
            if (miRunSelected != null) miRunSelected.IsEnabled = hasSelection;

            if (ctxEdit != null) ctxEdit.IsEnabled = isSingleSelected;
            if (ctxDelete != null) ctxDelete.IsEnabled = hasSelection;
            if (ctxCopy != null) ctxCopy.IsEnabled = isSingleSelected;
            if (ctxCut != null) ctxCut.IsEnabled = isSingleSelected;
            if (ctxPaste != null) ctxPaste.IsEnabled = isSingleSelected && _copyBuffer != null;
            if (ctxRun != null) ctxRun.IsEnabled = hasSelection;
            if (ctxOpenSource != null) ctxOpenSource.IsEnabled = isSingleSelected;
            if (ctxOpenDest != null) ctxOpenDest.IsEnabled = isSingleSelected;
        }


        private void BtnAddRule_Click(object sender, RoutedEventArgs e)
        {
            var newRule = new SortRule(); // 空の新規ルールを作成
            var window = new RuleEditWindow(newRule, PresetList) { Owner = this };

            if (window.ShowDialog() == true)
            {
                var newRuleVm = new SortRuleViewModel(window.ResultRule, PresetList);

                if (lvRules.SelectedItem is SortRuleViewModel selectedItem)
                {
                    int index = RulesList.IndexOf(selectedItem);
                    RulesList.Insert(index, newRuleVm);
                }
                else
                {
                    RulesList.Add(newRuleVm);
                }

                lvRules.SelectedItem = newRuleVm; // 追加した行を選択状態にする
                SaveAllRules();
                Log(Loc.F("log.rule_added", window.ResultRule.RuleName));
            }
        }

        private void BtnEditRule_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItem is SortRuleViewModel selected)
            {
                // 選択中のルールを編集画面に渡す
                var window = new RuleEditWindow(selected.Rule, PresetList) { Owner = this };

                if (window.ShowDialog() == true)
                {
                    // 保存された場合は、元のリストの中身を新しい設定で差し替える
                    int index = RulesList.IndexOf(selected);
                    RulesList.RemoveAt(index);
                    RulesList.Insert(index, new SortRuleViewModel(window.ResultRule, PresetList));
                    SaveAllRules();

                    lvRules.SelectedIndex = index; // 選択状態を復元
                    Log(Loc.F("log.rule_updated", window.ResultRule.RuleName));
                }
            }
            else
            {
                MessageBox.Show(this, Loc.T("msg.select_rule_to_edit"), Loc.T("title.info"));
            }
        }

        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItems.Count > 0)
            {
                string msg = lvRules.SelectedItems.Count == 1
                    ? Loc.T("msg.confirm_delete_one")
                    : Loc.F("msg.confirm_delete_many", lvRules.SelectedItems.Count);

                if (MessageBox.Show(this, msg, Loc.T("title.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    // 選択された複数行をリスト化してから一気に削除
                    var selectedItems = lvRules.SelectedItems.Cast<SortRuleViewModel>().ToList();
                    foreach (var item in selectedItems)
                    {
                        RulesList.Remove(item);
                    }
                    SaveAllRules();
                }
            }
        }

        // ==========================================
        // キーボードショートカット (削除 / コピー / 切り取り / 貼り付け)
        // ==========================================
        private void LvRules_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrlキーが押されているかを判定
            bool isCtrlDown = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            if (e.Key == Key.Delete)
            {
                BtnDeleteRule_Click(sender, e);
                e.Handled = true;
            }
            else if (isCtrlDown && e.Key == Key.X)
            {
                if (miCut != null && miCut.IsEnabled)
                {
                    MenuCut_Click(sender, null);
                    e.Handled = true;
                }
            }
            else if (isCtrlDown && e.Key == Key.C)
            {
                if (miCopy != null && miCopy.IsEnabled)
                {
                    MenuCopy_Click(sender, null);
                    e.Handled = true;
                }
            }
            else if (isCtrlDown && e.Key == Key.V)
            {
                if (miPaste != null && miPaste.IsEnabled)
                {
                    MenuPaste_Click(sender, null);
                    e.Handled = true;
                }
            }
        }


        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            var window = new PresetManagerWindow(PresetList, RulesList)
            {
                Owner = this
            };

            if (window.ShowDialog() == true)
            {
                PresetList.Clear();
                foreach (var p in window.Presets)
                {
                    PresetList.Add(p);
                }

                DataManager.SavePresets(PresetList);

                // 「個別指定に変換」されたルールも保存し、名称やパスの変更を一覧に即座に反映する
                SaveAllRules();
                RefreshRuleList();

                Log(Loc.T("log.presets_updated"));
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow(CurrentSettings) { Owner = this };

            if (window.ShowDialog() == true)
            {
                CurrentSettings = window.ResultSettings;
                DataManager.SaveSettings(CurrentSettings);

                ApplyProcessPriority(CurrentSettings.ProcessPriority);

                InitializeTimer();

                Loc.Initialize(CurrentSettings.Language); // 表示言語を反映する (画面は再起動なしで切り替わる)
                Log(Loc.T("log.settings_saved"));
            }
        }



        // ==========================================
        // 順序入れ替え処理 (上下ボタン)
        // ==========================================
        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItems.Count == 0) return;

            var selectedItems = lvRules.SelectedItems.Cast<SortRuleViewModel>()
                                       .OrderBy(x => RulesList.IndexOf(x)).ToList();

            int firstIndex = RulesList.IndexOf(selectedItems.First());
            if (firstIndex == 0) return; // すでに一番上の要素が含まれていれば移動しない

            var targetItem = RulesList[firstIndex - 1]; // ブロック最上行のさらに上の要素を基準にする

            foreach (var item in selectedItems) RulesList.Remove(item);

            int newTargetIndex = RulesList.IndexOf(targetItem); // 削除後のターゲット位置
            foreach (var item in selectedItems)
            {
                RulesList.Insert(newTargetIndex, item);
                newTargetIndex++;
            }

            // 移動後も選択状態を維持
            lvRules.SelectedItems.Clear();
            foreach (var item in selectedItems) lvRules.SelectedItems.Add(item);

            lvRules.Focus();
            SaveAllRules();
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItems.Count == 0) return;

            var selectedItems = lvRules.SelectedItems.Cast<SortRuleViewModel>()
                                       .OrderBy(x => RulesList.IndexOf(x)).ToList();

            int lastIndex = RulesList.IndexOf(selectedItems.Last());
            if (lastIndex == RulesList.Count - 1) return; // すでに一番下の要素が含まれていれば移動しない

            var targetItem = RulesList[lastIndex + 1]; // ブロック最下行のさらに下の要素を基準にする

            foreach (var item in selectedItems) RulesList.Remove(item);

            int newTargetIndex = RulesList.IndexOf(targetItem) + 1; // 削除後のターゲット位置の直下
            foreach (var item in selectedItems)
            {
                RulesList.Insert(newTargetIndex, item);
                newTargetIndex++;
            }

            lvRules.SelectedItems.Clear();
            foreach (var item in selectedItems) lvRules.SelectedItems.Add(item);

            lvRules.Focus();
            SaveAllRules();
        }

        // ==========================================
        // ドラッグ＆ドロップ (DnD) 処理
        // ==========================================
        private Point? _dragStartPoint = null;
        private List<SortRuleViewModel> _draggingItems = null;

        private void LvRules_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rowItem = GetRowItemFromOriginalSource(e.OriginalSource as DependencyObject);

            if (e.ClickCount == 2 && rowItem != null)
            {
                e.Handled = true; // ドラッグ操作などに進むのを防ぐ

                // 誤ってダブルクリックしただけでファイルが移動されないよう、設定で確認を挟めるようにする
                if (CurrentSettings.ConfirmBeforeDoubleClickRun &&
                    MessageBox.Show(this, Loc.F("msg.confirm_run_rule", rowItem.RuleName), Loc.T("title.confirm"),
                                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                var targetRules = new List<SortRuleViewModel> { rowItem };
                _ = ExecuteSortingAsync(targetRules);
                return;
            }

            // 行以外(余白・スクロールバー・ヘッダー)を押した場合は、ドラッグを始めない
            if (rowItem == null)
            {
                _dragStartPoint = null;
                return;
            }

            // 有効/無効のチェックボックスの操作では、選択もドラッグも変えない
            // (複数行を選択したままチェックを押して、まとめて切り替える操作を壊さないため)
            if (IsOnToggleButton(e.OriginalSource as DependencyObject))
            {
                _dragStartPoint = null;
                return;
            }

            // Ctrl / Shift を押しながらの操作は、複数選択のためなので、ドラッグは始めない
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
            {
                _dragStartPoint = null;
                return;
            }

            // 押した行がまだ選択されていなければ、先にその行だけを選択する。
            // (選択されていない行も、クリックし直さずにそのまま掴んで並び替えられるようにする。
            //  すでに選択されている行を押した場合は、複数選択を保ったまま、まとめてドラッグできる)
            if (!lvRules.SelectedItems.Contains(rowItem))
            {
                lvRules.SelectedItem = rowItem;
            }

            _dragStartPoint = e.GetPosition(null);
        }

        // クリックされた場所が、行の中のチェックボックス(ボタン類)かどうか
        private static bool IsOnToggleButton(DependencyObject depObj)
        {
            while (depObj != null && !(depObj is ListViewItem))
            {
                if (depObj is System.Windows.Controls.Primitives.ToggleButton) return true;

                depObj = (depObj is System.Windows.Media.Visual || depObj is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(depObj)
                    : LogicalTreeHelper.GetParent(depObj);
            }
            return false;
        }


        private void LvRules_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint.HasValue)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint.Value - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _dragStartPoint = null;

                    if (lvRules.SelectedItems.Count > 0)
                    {
                        _draggingItems = lvRules.SelectedItems.Cast<SortRuleViewModel>().ToList();

                        DataObject dragData = new DataObject("Classify8Rule", "dummy");
                        DragDrop.DoDragDrop(lvRules, dragData, DragDropEffects.Move);

                        _draggingItems = null;
                    }
                }
            }
        }

        private void LvRules_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("Classify8Rule"))
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void LvRules_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Classify8Rule"))
            {
                if (_draggingItems == null || _draggingItems.Count == 0) return;

                var dropTarget = GetRowItemFromPoint(e.GetPosition(lvRules));
                bool appendToEnd = (dropTarget == null);

                var itemsToMove = _draggingItems.OrderBy(x => RulesList.IndexOf(x)).ToList();

                if (!appendToEnd && itemsToMove.Contains(dropTarget)) return;

                foreach (var item in itemsToMove)
                {
                    RulesList.Remove(item);
                }

                int insertIndex = appendToEnd ? RulesList.Count : RulesList.IndexOf(dropTarget);
                if (insertIndex < 0) insertIndex = RulesList.Count;

                foreach (var item in itemsToMove)
                {
                    RulesList.Insert(insertIndex, item);
                    insertIndex++;
                }

                lvRules.SelectedItems.Clear();
                foreach (var item in itemsToMove)
                {
                    lvRules.SelectedItems.Add(item);
                }

                SaveAllRules();
            }
        }

        private SortRuleViewModel GetRowItemFromPoint(Point point)
        {
            var hitTestResult = VisualTreeHelper.HitTest(lvRules, point);
            if (hitTestResult == null) return null;

            DependencyObject depObj = hitTestResult.VisualHit;
            while (depObj != null && !(depObj is ListViewItem))
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem row)
            {
                return row.Content as SortRuleViewModel;
            }
            return null;
        }


        // ==========================================
        // 振り分け実行処理
        // ==========================================
        private async void MenuRunSelected_Click(object sender, RoutedEventArgs e)
        {
            var targetRules = lvRules.SelectedItems.Cast<SortRuleViewModel>().ToList();
            await ExecuteSortingAsync(targetRules);
        }

        private async void MenuRunAll_Click(object sender, RoutedEventArgs e)
        {
            var targetRules = RulesList.ToList();
            await ExecuteSortingAsync(targetRules);
        }

        // 共通の実行エンジン
        private async System.Threading.Tasks.Task ExecuteSortingAsync(List<SortRuleViewModel> targetRules, bool isAutoExecution = false)
        {
            if (targetRules.Count == 0) return;

            if (_isSorting)
            {
                if (!isAutoExecution) Log(Loc.T("log.already_running"));
                return;
            }

            _isSorting = true;
            var cts = new System.Threading.CancellationTokenSource();
            _currentCts = cts;

            if (_notifyIcon != null)
            {
                _notifyIcon.Text = Loc.T("tray.running");
                _blinkTimer.Start();
            }

            _progressWin = new ProgressWindow(cts) { Owner = this };
            this.IsEnabled = false;

            if (this.IsVisible && this.WindowState != WindowState.Minimized)
            {
                _progressWin.Show();
            }

            try
            {
                txtStatus.Text = Loc.T("status.processing");
                progressBar.Value = 30;

                Log("=========================================");
                Log(Loc.F("log.start", targetRules.Count));

                int totalProcessedCount = 0;
                int totalErrorCount = 0;
                int totalMovedCount = 0;
                int totalCopiedCount = 0;
                int totalDeletedCount = 0;
                int totalWarningCount = 0;

                int totalRules = targetRules.Count;
                int currentRuleIndex = 0;

                var historyRecords = new System.Collections.Concurrent.ConcurrentBag<SortHistory>();
                var processor = new FileProcessor(CurrentSettings, msg => Dispatcher.InvokeAsync(() => Log(msg)));

                var folderScanCache = new Dictionary<string, List<FileSystemInfo>>();

                // 全体設定のNGキーワードを判定用の条件に変換する (1行 = 1キーワード。キーワードが無ければ null)
                var ngConditionNode = NgKeywordBuilder.BuildNode(CurrentSettings.GlobalNgKeywords);

                await System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        foreach (var ruleVm in targetRules)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            currentRuleIndex++;

                            if (!ruleVm.IsEnabled) continue;
                            var rule = ruleVm.Rule;

                            // マルチスレッドで衝突しないように、ループに入る前に1回だけ構文を解析しておく
                            try
                            {
                                rule.CompileCondition();
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => Log(Loc.F("log.syntax_error", rule.RuleName, ex.Message)));
                                Interlocked.Increment(ref totalErrorCount);
                                continue; // 構文が壊れている場合は安全にこのルールをスキップ
                            }

                            string displayRuleName = Loc.F("progress.rule_display", currentRuleIndex, totalRules, rule.RuleName);

                            bool isSkip = false;
                            string srcPath = rule.SourceMode == "Preset" ? PresetList.FirstOrDefault(p => p.Id == rule.SourcePresetId)?.DirectoryPath : rule.SourceCustomPath;

                            // (※ファイル特定前のため、ファイルタイムスタンプ系のタグは実行日時にフォールバックされます)
                            if (!string.IsNullOrWhiteSpace(srcPath))
                            {
                                srcPath = DynamicPathBuilder.BuildPath(srcPath, "", rule);
                            }

                            string destPath = rule.DestMode == "Preset" ? PresetList.FirstOrDefault(p => p.Id == rule.DestPresetId)?.DirectoryPath : rule.DestCustomPath;

                            if (string.IsNullOrWhiteSpace(srcPath) || string.IsNullOrWhiteSpace(destPath)) isSkip = true;

                            if (isSkip)
                            {
                                Dispatcher.Invoke(() => Log(Loc.F("log.folder_unknown", rule.RuleName)));
                                Interlocked.Increment(ref totalErrorCount);
                                continue;
                            }

                            if (!Directory.Exists(srcPath))
                            {
                                Dispatcher.Invoke(() => Log(Loc.F("log.source_missing", srcPath)));
                                Interlocked.Increment(ref totalErrorCount);
                                continue;
                            }

                            // ドライブ全体やシステムフォルダを対象にした振り分けは、安全のため実行しない
                            if (PathSafety.IsDangerousSource(srcPath, rule.SearchSubDirectories, out string dangerReason))
                            {
                                Dispatcher.Invoke(() => Log(Loc.F("log.dangerous_source", rule.RuleName, dangerReason, srcPath)));
                                Interlocked.Increment(ref totalErrorCount);
                                continue;
                            }

                            // キャッシュのキーを作成
                            string cacheKey = $"{srcPath}|{rule.TargetType}|{rule.SearchSubDirectories}";
                            List<FileSystemInfo> itemsToProcess = new List<FileSystemInfo>();

                            if (folderScanCache.ContainsKey(cacheKey))
                            {
                                itemsToProcess = folderScanCache[cacheKey];
                            }
                            else
                            {
                                if (_progressWin != null)
                                {
                                    _progressWin.UpdateState(displayRuleName, Loc.T("progress.scanning"), totalProcessedCount, totalErrorCount);
                                }

                                // エラーが起きても絶対に止まらない独自スキャナーを使用する
                                var skippedDirs = new List<string>();
                                itemsToProcess = SafeGetItemsToProcess(srcPath, rule.SearchSubDirectories, rule.TargetType, skippedDirs);

                                if (skippedDirs.Count > 0)
                                {
                                    string sample = string.Join(", ", skippedDirs.Take(3));
                                    Dispatcher.Invoke(() => Log(Loc.F("log.skipped_dirs", skippedDirs.Count, sample)));
                                }

                                folderScanCache[cacheKey] = itemsToProcess;
                            }

                            var parallelOptions = new ParallelOptions
                            {
                                MaxDegreeOfParallelism = Environment.ProcessorCount,
                                CancellationToken = cts.Token
                            };

                            var affectedDestDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            await Parallel.ForEachAsync(itemsToProcess, parallelOptions, async (item, ct) =>
                            {
                                try
                                {
                                    // 列挙時に取得済みの情報(属性・サイズ・更新日時)を使うので、ここではディスクにアクセスしない
                                    string itemPath = item.FullName;
                                    string itemName = item.Name;
                                    bool isDir = (item.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

                                    if (NgKeywordBuilder.IsExcluded(ngConditionNode, itemName)) return;

                                    if (!rule.IsMatch(item)) return;

                                    // (対象が既に存在しない場合は、ProcessItemAsync が null を返す)
                                    var history = await processor.ProcessItemAsync(itemPath, destPath, rule, ct, isDir);
                                    if (history == null) return;

                                    if (HistoryStatus.IsMoved(history.Status))
                                    {
                                        Interlocked.Increment(ref totalMovedCount);
                                        lock (affectedDestDirs) { affectedDestDirs.Add(history.DestDir); }
                                    }
                                    else if (HistoryStatus.IsCopied(history.Status))
                                    {
                                        Interlocked.Increment(ref totalCopiedCount);
                                        lock (affectedDestDirs) { affectedDestDirs.Add(history.DestDir); }
                                    }
                                    else if (HistoryStatus.IsDeleted(history.Status))
                                    {
                                        Interlocked.Increment(ref totalDeletedCount);
                                    }
                                    else if (HistoryStatus.IsError(history.Status))
                                    {
                                        Interlocked.Increment(ref totalErrorCount);
                                    }
                                    else if (HistoryStatus.IsWarning(history.Status))
                                    {
                                        Interlocked.Increment(ref totalWarningCount);
                                    }

                                    // スキップ・警告は履歴に残さない（「削除」は保存される）
                                    // (警告を残すと、定周期実行のたびに同じ記録が積み上がり、上限件数を埋めてしまうため)
                                    if (!rule.DoNotSaveHistory && !HistoryStatus.IsSkipped(history.Status) && !HistoryStatus.IsWarning(history.Status))
                                    {
                                        historyRecords.Add(history);
                                    }

                                    int currentCount = Interlocked.Increment(ref totalProcessedCount);

                                    if (_progressWin != null && currentCount % 50 == 0)
                                    {
                                        _progressWin.UpdateState(displayRuleName, itemName, currentCount, totalErrorCount);
                                    }
                                }
                                catch
                                {
                                    Interlocked.Increment(ref totalErrorCount);
                                }
                            });

                            if (affectedDestDirs.Any())
                            {
                                var keysToRemove = new List<string>();
                                foreach (var keyStr in folderScanCache.Keys)
                                {
                                    var parts = keyStr.Split('|');
                                    if (parts.Length < 3) continue;
                                    string cachedPath = parts[0];
                                    bool isSubdirs = parts[2] == "True";

                                    foreach (var affectedDir in affectedDestDirs)
                                    {
                                        // 完全に一致する場合
                                        if (string.Equals(cachedPath, affectedDir, StringComparison.OrdinalIgnoreCase))
                                        {
                                            keysToRemove.Add(keyStr);
                                            break;
                                        }

                                        // サブフォルダ検索が有効で、影響を受けたフォルダがその配下にある場合
                                        string cachedPathSep = cachedPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                                            ? cachedPath
                                            : cachedPath + Path.DirectorySeparatorChar;

                                        if (isSubdirs && affectedDir.StartsWith(cachedPathSep, StringComparison.OrdinalIgnoreCase))
                                        {
                                            keysToRemove.Add(keyStr);
                                            break;
                                        }
                                    }
                                }
                                // 次のルールで再スキャンされるよう、キャッシュを削除
                                foreach (var k in keysToRemove) folderScanCache.Remove(k);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Dispatcher.Invoke(() => Log(Loc.T("log.cancelled")));
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => Log(Loc.F("log.fatal", ex.Message)));
                    }
                });

                progressBar.Value = 100;
                txtStatus.Text = Loc.T("common.waiting");

                if (historyRecords.Any())
                {
                    try
                    {
                        HistoryManager.AddHistories(historyRecords.ToList(), CurrentSettings);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Failed to save the history.", ex);
                        Log(Loc.F("log.history_save_failed", ex.Message));
                    }
                }

                if (totalWarningCount > 0)
                {
                    Log(Loc.F("log.same_location_warning", totalWarningCount));
                }

                if (!cts.IsCancellationRequested)
                {
                    string endMessage = totalErrorCount > 0
                        ? Loc.F("log.finish_with_errors", totalMovedCount, totalCopiedCount, totalDeletedCount, totalErrorCount)
                        : Loc.F("log.finish_ok", totalMovedCount, totalCopiedCount, totalDeletedCount);
                    Log(endMessage);

                    if (!isAutoExecution && totalErrorCount > 0)
                    {
                        MessageBox.Show(this, Loc.F("msg.errors_occurred", totalErrorCount),
                                        Loc.T("title.partial_fail"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                // async void から呼ばれることがあるため、ここで受け止めてアプリが落ちないようにする
                AppLog.Error("An unexpected exception occurred during sorting.", ex);
                Log(Loc.F("log.fatal", ex.Message));
            }
            finally
            {
                if (_progressWin != null)
                {
                    _progressWin.CompleteAndClose();
                    _progressWin = null;
                }

                if (_notifyIcon != null)
                {
                    _blinkTimer.Stop();
                    _isIconBlank = false;
                    _notifyIcon.Icon = _normalIcon;
                    _notifyIcon.Text = Loc.T("tray.idle");
                }

                this.IsEnabled = true;
                RefreshRuleList();
                _isSorting = false;
                _currentCts = null;

                // 処理中に終了を要求されていた場合は、履歴の保存など後始末が済んだここで閉じる
                if (_exitRequested)
                {
                    // (戻り値は待たずに破棄する。ここで待つと、閉じる処理と自分自身が互いに待ち合ってしまう)
                    _ = Dispatcher.BeginInvoke(new Action(() => Close()));
                }
            }
        }

        private List<FileSystemInfo> SafeGetItemsToProcess(string rootPath, bool recurse, TargetType targetType, List<string> skippedDirs)
        {
            var items = new List<FileSystemInfo>();
            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            bool wantFiles = targetType == TargetType.FileOnly || targetType == TargetType.Both;
            bool wantDirs = targetType == TargetType.DirectoryOnly || targetType == TargetType.Both;

            // 属性・サイズ・更新日時は列挙と同時に取得されるので、後から1件ずつ問い合わせる必要がない。
            // 隠し/システム属性のファイルも対象にする。
            var options = new EnumerationOptions
            {
                AttributesToSkip = 0,
                IgnoreInaccessible = false, // 開けないフォルダは例外にして、スキップした記録を残す
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                try
                {
                    // 途中で例外になったフォルダの分は、中途半端に追加しない
                    var found = new List<FileSystemInfo>();
                    foreach (var info in new DirectoryInfo(current).EnumerateFileSystemInfos("*", options))
                    {
                        found.Add(info);
                    }

                    foreach (var info in found)
                    {
                        bool isDir = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                        if (isDir)
                        {
                            if (wantDirs) items.Add(info);
                            // サブフォルダがある場合はキューに追加して後で検索する
                            if (recurse) queue.Enqueue(info.FullName);
                        }
                        else if (wantFiles)
                        {
                            items.Add(info);
                        }
                    }
                }
                catch
                {
                    // アクセス拒否やパス長超過などのエラーが出たフォルダはスキップして処理を継続する(スキップした事実はログに出す)
                    skippedDirs?.Add(current);
                }
            }
            return items;
        }

        // ==========================================
        // メニューの各機能
        // ==========================================
        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            var window = new HistoryWindow { Owner = this };
            window.ShowDialog();
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItem is SortRuleViewModel selected)
            {
                _copyBuffer = selected.Rule.Clone();
                _isCutBuffer = false;

                UpdateMenuState();

                Log(Loc.F("log.copied_rule", _copyBuffer.RuleName));
            }
        }

        private void MenuCut_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItem is SortRuleViewModel selected)
            {
                _copyBuffer = selected.Rule.Clone();
                _isCutBuffer = true;
                _cutSource = selected;

                // 貼り付けるまではリストから消さない
                // (貼り付ける前にアプリを閉じても、ルールが失われないようにするため)
                UpdateMenuState();

                Log(Loc.F("log.cut_rule", _copyBuffer.RuleName));
            }
        }

        private void MenuPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_copyBuffer != null && lvRules.SelectedItem is SortRuleViewModel selected)
            {
                // --- 切り取りからの貼り付け: 元のルール自体を移動する (IDも維持される) ---
                if (_isCutBuffer && _cutSource != null && RulesList.Contains(_cutSource))
                {
                    var moving = _cutSource;

                    // 一度切り取ったものを貼り付けた後は、通常コピーと同じ扱いにする
                    _copyBuffer = moving.Rule.Clone();
                    _isCutBuffer = false;
                    _cutSource = null;

                    if (!ReferenceEquals(moving, selected))
                    {
                        RulesList.Remove(moving);
                        int movedIndex = RulesList.IndexOf(selected);
                        RulesList.Insert(movedIndex, moving);
                        SaveAllRules();
                    }

                    lvRules.SelectedItem = moving;
                    UpdateMenuState();
                    Log(Loc.T("log.moved_rule"));
                    return;
                }

                // --- コピーからの貼り付け (切り取り元が既に削除されていた場合も含む) ---
                int index = RulesList.IndexOf(selected);
                var newRule = _copyBuffer.Clone();

                if (!_isCutBuffer)
                {
                    newRule.RuleName += Loc.T("rule.copy_suffix");
                }
                else
                {
                    _isCutBuffer = false;
                    _cutSource = null;
                }

                var newRuleVm = new SortRuleViewModel(newRule, PresetList);

                RulesList.Insert(index, newRuleVm);

                // 貼り付けた行を自動で選択状態にする（連続ペーストをしやすくするため）
                lvRules.SelectedItem = newRuleVm;

                SaveAllRules();
                Log(Loc.T("log.pasted_rule"));
            }
        }

        private void MenuCheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var rule in RulesList) rule.IsEnabled = true;
            RefreshRuleList(); // 画面にチェック状態を反映
            SaveAllRules();
        }

        private void MenuUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var rule in RulesList) rule.IsEnabled = false;
            RefreshRuleList();
            SaveAllRules();
        }

        private void MenuOpenSource_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItem is SortRuleViewModel selected)
            {
                string path = selected.Rule.SourceMode == "Preset"
                    ? PresetList.FirstOrDefault(p => p.Id == selected.Rule.SourcePresetId)?.DirectoryPath
                    : selected.Rule.SourceCustomPath;

                OpenFolderWithTags(path, selected.RuleName);
            }
        }

        private void MenuOpenDest_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItem is SortRuleViewModel selected)
            {
                string path = selected.Rule.DestMode == "Preset"
                    ? PresetList.FirstOrDefault(p => p.Id == selected.Rule.DestPresetId)?.DirectoryPath
                    : selected.Rule.DestCustomPath;

                OpenFolderWithTags(path, selected.RuleName);
            }
        }

        private void OpenFolderWithTags(string path, string ruleName)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                // 1. 日付や条件タグなどの変動タグが出現した位置を探す
                string[] stopTags = { "/YYYY", "/YY", "/MM", "/DD", "/FYYYY", "/FYY", "/FMM", "/FDD", "/COND" };
                int firstTagIndex = -1;
                foreach (var tag in stopTags)
                {
                    int idx = path.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && (firstTagIndex == -1 || idx < firstTagIndex))
                    {
                        firstTagIndex = idx;
                    }
                }

                // 2. 変動タグが見つかったら、そこより手前までをシンプルに切り出す
                if (firstTagIndex >= 0)
                {
                    path = path.Substring(0, firstTagIndex);
                }

                // 3. /NAME タグを実際のルール名に展開する
                // (実際の振り分け先の作成時と同じ規則で、使えない文字を _ に置換する)
                path = path.Replace("/NAME", DynamicPathBuilder.SanitizeForPath(ruleName), StringComparison.OrdinalIgnoreCase);

                // パス末尾の不要な空白やスラッシュを掃除する
                path = path.TrimEnd(' ', '/');
                if (path.Length > 3 && path.EndsWith("\\"))
                {
                    path = path.TrimEnd('\\');
                }

                // 4. フォルダが存在しない場合、存在する親ディレクトリが見つかるまで遡る
                while (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                {
                    var parent = Directory.GetParent(path);
                    if (parent == null) break;
                    path = parent.FullName;
                }

                // 5. 確定したフォルダを開く
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", path);
                }
                else
                {
                    MessageBox.Show(this, Loc.F("msg.folder_not_found", path), Loc.T("title.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private SortRuleViewModel GetRowItemFromOriginalSource(DependencyObject depObj)
        {
            while (depObj != null && !(depObj is ListViewItem))
            {
                // スクロールバーやヘッダー行をクリックした場合は無視する
                if (depObj is System.Windows.Controls.Primitives.ScrollBar) return null;
                if (depObj is GridViewColumnHeader) return null;

                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem row)
            {
                return row.Content as SortRuleViewModel;
            }
            return null;
        }

        private void ApplyProcessPriority(ProcessPriorityClass priority)
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    process.PriorityClass = priority;
                }
            }
            catch (Exception ex)
            {
                Log(Loc.F("log.priority_failed", ex.Message));
            }
        }

        private async void SaveAllRules()
        {
            // 保存する瞬間の最新のリストを取得
            var rulesToSave = RulesList.Select(vm => vm.Rule).ToList();

            // 他の保存処理が実行中なら、それが終わるまでここで待機する（順番待ち）
            await _saveLock.WaitAsync();
            try
            {
                // 順番が来たら、画面を止めずに裏側のスレッドでファイルに書き込む
                await System.Threading.Tasks.Task.Run(() =>
                {
                    DataManager.SaveRules(rulesToSave);
                });
            }
            finally
            {
                // 書き込みが終わったらロックを解除し、待っている次の処理に譲る
                _saveLock.Release();
            }
        }

        private void RuleCheckBox_Click(object sender, RoutedEventArgs e)
        {
            // クリックされたチェックボックスとその行のデータ(ViewModel)を取得
            if (sender is CheckBox chk && chk.DataContext is SortRuleViewModel clickedItem)
            {
                // 複数行が選択されており、かつクリックした行がその選択範囲に含まれているか判定
                if (lvRules.SelectedItems.Count > 1 && lvRules.SelectedItems.Contains(clickedItem))
                {
                    bool newState = chk.IsChecked ?? false;

                    // 選択されているすべてのルールの状態を、クリックされた状態に合わせる
                    foreach (SortRuleViewModel item in lvRules.SelectedItems)
                    {
                        if (item != clickedItem)
                        {
                            item.IsEnabled = newState;
                        }
                    }

                    // 画面の表示を更新して、他のチェックボックスの見た目も即座に切り替える
                    RefreshRuleList();
                }
            }

            SaveAllRules();
        }

        private void InitializeTimer()
        {
            // すでにタイマーが動いていれば一度止めて破棄する
            if (_periodicTimer != null)
            {
                _periodicTimer.Stop();
                _periodicTimer.Tick -= PeriodicTimer_Tick;
                _periodicTimer = null;
            }

            if (CurrentSettings.EnablePeriodicExecution && CurrentSettings.PeriodicIntervalMinutes > 0)
            {
                _periodicTimer = new System.Windows.Threading.DispatcherTimer();
                _periodicTimer.Interval = TimeSpan.FromMinutes(CurrentSettings.PeriodicIntervalMinutes);
                _periodicTimer.Tick += PeriodicTimer_Tick;
                _periodicTimer.Start();

                Log(Loc.F("log.periodic_enabled", CurrentSettings.PeriodicIntervalMinutes));
            }
            else
            {
                Log(Loc.T("log.periodic_disabled"));
            }
        }

        // タイマーの時間になったら呼ばれる処理
        private async void PeriodicTimer_Tick(object sender, EventArgs e)
        {
            if (_isSorting) return; // 既に処理中なら今回はスキップ(安全装置)

            if (this.IsVisible && this.WindowState != WindowState.Minimized)
            {
                Log(Loc.T("log.periodic_skip"));
                return;
            }

            Log(Loc.T("log.periodic_start"));
            var targetRules = RulesList.ToList();

            // isAutoExecutionフラグを true にして呼び出す
            await ExecuteSortingAsync(targetRules, isAutoExecution: true);
        }

        // ==========================================
        // タスクトレイ常駐とウィンドウ制御
        // ==========================================
        private void InitializeNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = Loc.T("tray.idle");

            try
            {
                var iconUri = new Uri("pack://application:,,,/c8_icon.ico", UriKind.Absolute);
                using (var stream = Application.GetResourceStream(iconUri)?.Stream)
                {
                    if (stream != null)
                    {
                        _normalIcon = new System.Drawing.Icon(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(Loc.F("log.tray_icon_failed", ex.Message));
                _normalIcon = System.Drawing.SystemIcons.Information;
                _normalIconIsShared = true;
            }

            _notifyIcon.Icon = _normalIcon;
            _notifyIcon.Visible = true;

            using (var bmp = new System.Drawing.Bitmap(16, 16))
            {
                IntPtr hIcon = bmp.GetHicon();
                _blankIcon = System.Drawing.Icon.FromHandle(hIcon);
            }

            _blinkTimer = new System.Windows.Threading.DispatcherTimer();
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(500);
            _blinkTimer.Tick += (s, e) =>
            {
                _isIconBlank = !_isIconBlank;
                _notifyIcon.Icon = _isIconBlank ? _blankIcon : _normalIcon;
            };

            // ダブルクリックで画面を復帰
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            // 左シングルクリックでも画面を復帰
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    ShowMainWindow();
                }
            };

            // 右クリックメニューの作成
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var openItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.show"));
            _trayShowItem = openItem;
            openItem.Click += (s, e) => ShowMainWindow();
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.exit"));
            _trayExitItem = exitItem;
            exitItem.Click += (s, e) => MenuExit_Click(null, null);
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;

            if (_isSorting && _progressWin != null && !_progressWin.IsVisible)
            {
                _progressWin.Show();
            }

            this.Activate(); // メイン画面を最前面へ持ってくる
        }

        // 最小化ボタン「_」が押された時、タスクバーから消してトレイのみにする
        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                if (_progressWin != null && _progressWin.IsVisible)
                {
                    _progressWin.Hide();
                }
                this.Hide();
            }
            base.OnStateChanged(e);
        }

        // トップメニューの「ファイル > 終了」が押された時
        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // 閉じるボタン「×」が押された時、またはアプリが終了する直前の処理
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // --- 振り分け処理の実行中に終了しようとした場合 ---
            // そのまま閉じると、移動済みファイルの履歴が保存されないため、処理を中止して後始末が済んでから閉じる
            if (_isSorting && !_sessionEnding)
            {
                if (!_exitRequested)
                {
                    string question = Loc.T("msg.confirm_exit_sorting");
                    var answer = this.IsVisible
                        ? MessageBox.Show(this, question, Loc.T("title.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
                        : MessageBox.Show(question, Loc.T("title.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (answer != MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }

                    _exitRequested = true;
                    _currentCts?.Cancel();
                }

                // 処理の後始末が終わると、ExecuteSortingAsync の最後で自動的に閉じ直される
                e.Cancel = true;
                return;
            }

            // --- 完全に終了する ---
            Loc.LanguageChanged -= OnLanguageChanged;

            // 非同期の保存処理(SaveAllRules)が実行中なら、終わるのを待ってから保存する (同じファイルへの同時書き込みを避ける)
            bool lockTaken = false;
            try { lockTaken = _saveLock.Wait(3000); } catch { }
            try
            {
                var rulesToSave = RulesList.Select(vm => vm.Rule).ToList();
                DataManager.SaveRules(rulesToSave);
                DataManager.SavePresets(PresetList);

                DataManager.SaveSettings(CurrentSettings);
            }
            finally
            {
                if (lockTaken) _saveLock.Release();
            }

            // タスクトレイのアイコンを綺麗に消去する
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (_blankIcon != null)
            {
                DestroyIcon(_blankIcon.Handle);
                _blankIcon.Dispose();
            }
            if (_normalIcon != null && !_normalIconIsShared)
            {
                _normalIcon.Dispose();
            }
        }

        public void ImportLegacyCsv(string filePath)
        {
            try
            {
                // PresetList を渡して、必要に応じてプリセットを追加させる
                var importedRules = LegacyImporter.ImportFromClassiNyCsv(filePath, PresetList);

                if (importedRules.Count == 0)
                {
                    MessageBox.Show(this, Loc.T("msg.import_none"), Loc.T("title.info"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var rule in importedRules)
                {
                    RulesList.Add(new SortRuleViewModel(rule, PresetList));
                }

                // 新しく追加されたかもしれないプリセットと、ルールを保存
                DataManager.SavePresets(PresetList);
                SaveAllRules();
                RefreshRuleList(); // 画面を更新

                Log(Loc.F("log.import_done", importedRules.Count));
                string note = "";
                if (PresetList.Any(p => p.AliasName == LegacyImporter.CommonPresetAlias && string.IsNullOrWhiteSpace(p.DirectoryPath)))
                {
                    note = Loc.F("msg.import_preset_note", LegacyImporter.CommonPresetAlias);
                }
                MessageBox.Show(this, Loc.F("msg.import_success", importedRules.Count, note), Loc.T("title.import_done"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("msg.import_error", ex.Message), Loc.T("title.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenRuleEditWindow(string ruleId, string ruleName)
        {
            // まず一意のIdで探す
            var targetVm = RulesList.FirstOrDefault(r => r.Rule.Id == ruleId);

            // 見つからなければ(過去の履歴を開いた等)、名前でフォールバック検索する
            if (targetVm == null)
            {
                targetVm = RulesList.FirstOrDefault(r => r.RuleName == ruleName);
            }

            if (targetVm != null)
            {
                lvRules.SelectedItem = targetVm;
                lvRules.ScrollIntoView(targetVm);

                var window = new RuleEditWindow(targetVm.Rule, PresetList) { Owner = this };
                if (window.ShowDialog() == true)
                {
                    int index = RulesList.IndexOf(targetVm);
                    RulesList.RemoveAt(index);
                    RulesList.Insert(index, new SortRuleViewModel(window.ResultRule, PresetList));
                    SaveAllRules();
                    lvRules.SelectedIndex = index;
                    Log(Loc.F("log.rule_updated", window.ResultRule.RuleName));
                }
            }
            else
            {
                MessageBox.Show(Loc.F("msg.rule_not_found", ruleName), Loc.T("title.info"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ==========================================
        // 操作ヘルプ (Help.html を開く)
        // ==========================================
        private void MenuHelp_Click(object sender, RoutedEventArgs e)
        {
            // 実行ファイルと同じフォルダにあるヘルプを開く
            // 表示言語用のヘルプ (Help.en.html など) があればそれを、無ければ Help.html を開く
            string helpFileName = $"Help.{Loc.CurrentLanguage}.html";
            string helpFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, helpFileName);
            if (!File.Exists(helpFilePath))
            {
                helpFileName = "Help.html";
                helpFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, helpFileName);
            }

            if (File.Exists(helpFilePath))
            {
                try
                {
                    // UseShellExecute を true にすることで、OSで関連付けられている標準アプリで開く
                    Process.Start(new ProcessStartInfo(helpFilePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Loc.F("msg.help_launch_failed", ex.Message), Loc.T("title.error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(this, Loc.F("msg.help_not_found", helpFileName),
                                Loc.T("title.file_not_found"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ==========================================
        // ヘルプ / バージョン情報
        // ==========================================
        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow { Owner = this };
            window.ShowDialog();
        }

        // 表示言語が切り替わったとき。XAML の文字列は自動で切り替わるので、コードで設定している文字列を更新する
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnLanguageChanged(sender, e)));
                return;
            }

            if (_trayShowItem != null) _trayShowItem.Text = Loc.T("tray.show");
            if (_trayExitItem != null) _trayExitItem.Text = Loc.T("tray.exit");
            if (_notifyIcon != null) _notifyIcon.Text = Loc.T(_isSorting ? "tray.running" : "tray.idle");
            if (!_isSorting) txtStatus.Text = Loc.T("common.waiting");

            RefreshRuleList(); // 「移動/コピー」「不明なプリセット」などの表示を更新する
        }

        // ログ欄の行数上限 (タスクトレイに常駐し続けても、メモリや描画が重くならないようにする)
        private const int MaxLogLines = 2000;
        private const int LogTrimLines = 500;
        private int _logLineCount = 0;

        private void Log(string message)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            _logLineCount++;

            if (_logLineCount > MaxLogLines)
            {
                // 古い行をまとめて切り捨てる
                string text = txtLog.Text;
                int cut = 0;
                for (int i = 0; i < LogTrimLines; i++)
                {
                    int nl = text.IndexOf('\n', cut);
                    if (nl < 0) break;
                    cut = nl + 1;
                }
                txtLog.Text = text.Substring(cut);
                _logLineCount -= LogTrimLines;
            }

            // 非表示(タスクトレイ格納中)のときは、スクロール処理は不要
            if (this.IsVisible) txtLog.ScrollToEnd();
        }

        // ルール一覧の表示を更新する (プリセットの名称やパスが変わったとき等)
        private void RefreshRuleList()
        {
            foreach (var vm in RulesList) vm.NotifyAll();
        }
    }

    // --- GUI表示用のViewModel ---
    public class SortRuleViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public SortRule Rule { get; }
        private readonly IEnumerable<Preset> _presets;

        public SortRuleViewModel(SortRule rule, IEnumerable<Preset> presets)
        {
            Rule = rule;
            _presets = presets;
        }

        public bool IsEnabled
        {
            get => Rule.IsEnabled;
            set
            {
                if (Rule.IsEnabled == value) return;
                Rule.IsEnabled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }

        // すべてのプロパティの表示を更新させる (プリセットの変更など、間接的に表示が変わる場合に使う)
        public void NotifyAll()
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
        }

        public string RuleName => Rule.RuleName;
        public string SearchCondition => Rule.SearchCondition;

        public string SourcePathDisplay
        {
            get
            {
                if (Rule.SourceMode == "Preset")
                {
                    var preset = _presets?.FirstOrDefault(p => p.Id == Rule.SourcePresetId);
                    return preset != null ? $"[P] {preset.AliasName} ({preset.DirectoryPath})" : Loc.T("rule.preset_unknown");
                }
                return Rule.SourceCustomPath;
            }
        }

        public string DestPathDisplay
        {
            get
            {
                if (Rule.DestMode == "Preset")
                {
                    var preset = _presets?.FirstOrDefault(p => p.Id == Rule.DestPresetId);
                    return preset != null ? $"[P] {preset.AliasName} ({preset.DirectoryPath})" : Loc.T("rule.preset_unknown");
                }
                return Rule.DestCustomPath;
            }
        }
    }
}