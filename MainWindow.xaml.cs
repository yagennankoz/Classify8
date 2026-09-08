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
        public MainWindow()
        {
            InitializeComponent();
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


        private void LoadRules()
        {
            RulesList.Add(new SortRuleViewModel(new SortRule
            {
                RuleName = "画像データの退避",
                SearchCondition = "*.jpg /| *.png",
                IsMoveAction = true,
                IsEnabled = true
            }, PresetList));
            RulesList.Add(new SortRuleViewModel(new SortRule
            {
                RuleName = "古い請求書のコピー",
                SearchCondition = "/( 請求書 /| 見積書 /) /& /! 破棄",
                IsMoveAction = false,
                IsEnabled = false // テストとして無効に設定
            }, PresetList));
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
                Log($"新しいルール「{window.ResultRule.RuleName}」を追加しました。");
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
                    Log($"ルール「{window.ResultRule.RuleName}」を更新しました。");
                }
            }
            else
            {
                MessageBox.Show("編集するルールを選択してください。", "お知らせ");
            }
        }

        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItems.Count > 0)
            {
                string msg = lvRules.SelectedItems.Count == 1
                    ? "選択したルールを本当に削除しますか？"
                    : $"{lvRules.SelectedItems.Count} 件のルールを本当に削除しますか？";

                if (MessageBox.Show(msg, "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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

                // プリセットの名称やパスが変更されたら一覧表示を即座に更新する
                lvRules.Items.Refresh();

                Log("プリセットを更新しました。");
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

                Log("全体設定を保存しました。");
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
            var rowItem = GetRowItemFromPoint(e.GetPosition(lvRules));

            // =========================================================
            // WFPのイベント吸収を無視する、ダブルクリック検知
            // =========================================================
            if (e.ClickCount == 2 && rowItem != null)
            {
                var targetRules = new List<SortRuleViewModel> { rowItem };
                _ = ExecuteSortingAsync(targetRules);

                e.Handled = true; // 他のイベントが暴発するのを防ぐ
                return; // ドラッグ処理などには進まず、ここで終了
            }
            // =========================================================

            if (rowItem != null)
            {
                // クリックした行が「既に選択されている行」かチェック
                if (lvRules.SelectedItems.Contains(rowItem))
                {
                    // CTRLキーやSHIFTキーが押されている時は「選択の解除/追加」を優先するため移動はしない
                    if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
                    {
                        _dragStartPoint = null;
                    }
                    else
                    {
                        // 選択済み行を純粋に左クリックした場合のみ、移動(DnD)の準備をする
                        _dragStartPoint = e.GetPosition(null);
                    }
                }
                else
                {
                    // 非選択行をクリックした時は、WPF標準の選択動作(ドラッグ複数選択など)に任せるため、移動準備はしない
                    _dragStartPoint = null;
                }
            }
            else
            {
                _dragStartPoint = null;
            }
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

                // ドロップされた位置の行を取得（何もない空白なら null になる）
                var dropTarget = GetRowItemFromPoint(e.GetPosition(lvRules));
                bool appendToEnd = (dropTarget == null); // 空白なら最後尾に追加フラグを立てる

                var itemsToMove = _draggingItems.OrderBy(x => RulesList.IndexOf(x)).ToList();

                // ターゲット自身へのドロップは無効
                if (!appendToEnd && itemsToMove.Contains(dropTarget)) return;

                // 一旦リストから削除
                foreach (var item in itemsToMove)
                {
                    RulesList.Remove(item);
                }

                // 挿入位置を決定（最後尾に追加するか、ターゲット行の位置に挿入するか）
                int insertIndex = appendToEnd ? RulesList.Count : RulesList.IndexOf(dropTarget);
                if (insertIndex < 0) insertIndex = RulesList.Count;

                // ターゲット位置へ順番に挿入
                foreach (var item in itemsToMove)
                {
                    RulesList.Insert(insertIndex, item);
                    insertIndex++; // 複数行ある場合はインデックスをずらしながら挿入
                }

                // 移動後の行を選択状態にする
                lvRules.SelectedItems.Clear();
                foreach (var item in itemsToMove)
                {
                    lvRules.SelectedItems.Add(item);
                }

                SaveAllRules();
            }
        }

        // ListViewItem を取得するように変更
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
                return row.Content as SortRuleViewModel; // Item ではなく Content
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
                if (!isAutoExecution) Log("[警告] 現在別の振り分け処理が実行中です。完了するまでお待ちください。");
                return;
            }

            _isSorting = true;
            var cts = new System.Threading.CancellationTokenSource();

            _progressWin = new ProgressWindow(cts) { Owner = this };
            this.IsEnabled = false;

            if (this.IsVisible && this.WindowState != WindowState.Minimized)
            {
                _progressWin.Show();
            }

            try
            {
                txtStatus.Text = "処理中...";
                progressBar.Value = 30;

                Log("=========================================");
                Log($"[開始] 振り分け処理を開始しました。対象ルール: {targetRules.Count}件");

                int totalProcessedCount = 0;
                int totalErrorCount = 0;
                int totalRules = targetRules.Count;
                int currentRuleIndex = 0;

                var historyRecords = new System.Collections.Concurrent.ConcurrentBag<SortHistory>();
                var processor = new FileProcessor(CurrentSettings, msg => Dispatcher.InvokeAsync(() => Log(msg)));

                var folderScanCache = new Dictionary<string, List<string>>();

                var ngKeywords = CurrentSettings.GlobalNgKeywords
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList();
                string ngConditionString = string.Join(" /| ", ngKeywords);
                var ngParser = new ConditionParser(ignoreCase: true);
                var ngConditionNode = string.IsNullOrEmpty(ngConditionString) ? null : ngParser.Parse(ngConditionString);

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

                            string displayRuleName = $"[{currentRuleIndex} / {totalRules} 件目]  {rule.RuleName}";

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
                                Dispatcher.Invoke(() => Log($"[エラー] ルール「{rule.RuleName}」: フォルダ設定が不明なためスキップ"));
                                Interlocked.Increment(ref totalErrorCount);
                                continue;
                            }

                            if (!Directory.Exists(srcPath))
                            {
                                Dispatcher.Invoke(() => Log($"[エラー] 振り分け元が存在しません: {srcPath}"));
                                Interlocked.Increment(ref totalErrorCount);
                                continue;
                            }

                            // キャッシュのキーを作成
                            string cacheKey = $"{srcPath}|{rule.TargetType}|{rule.SearchSubDirectories}";
                            List<string> itemsToProcess;

                            if (folderScanCache.ContainsKey(cacheKey))
                            {
                                itemsToProcess = folderScanCache[cacheKey];
                            }
                            else
                            {
                                if (_progressWin != null)
                                {
                                    _progressWin.UpdateState(displayRuleName, "フォルダ内をスキャン中...", totalProcessedCount, totalErrorCount);
                                }

                                var enumOptions = new EnumerationOptions
                                {
                                    RecurseSubdirectories = rule.SearchSubDirectories,
                                    IgnoreInaccessible = true
                                };

                                itemsToProcess = new List<string>();

                                if (rule.TargetType == TargetType.FileOnly || rule.TargetType == TargetType.Both)
                                    itemsToProcess.AddRange(Directory.EnumerateFiles(srcPath, "*", enumOptions));

                                if (rule.TargetType == TargetType.DirectoryOnly || rule.TargetType == TargetType.Both)
                                    itemsToProcess.AddRange(Directory.EnumerateDirectories(srcPath, "*", enumOptions));

                                folderScanCache[cacheKey] = itemsToProcess;
                            }

                            var parallelOptions = new ParallelOptions
                            {
                                MaxDegreeOfParallelism = Environment.ProcessorCount,
                                CancellationToken = cts.Token
                            };

                            var affectedDestDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            await Parallel.ForEachAsync(itemsToProcess, parallelOptions, async (itemPath, ct) =>
                            {
                                if (!File.Exists(itemPath) && !Directory.Exists(itemPath)) return;

                                string itemName = Path.GetFileName(itemPath);

                                if (ngConditionNode != null && ngConditionNode.Evaluate(itemName)) return;

                                bool isDir = (File.GetAttributes(itemPath) & FileAttributes.Directory) == FileAttributes.Directory;
                                if (!rule.IsMatch(itemPath, isDir)) return;

                                var history = await processor.ProcessItemAsync(itemPath, destPath, rule, ct);
                                if (history == null) return;

                                if (history.Status == "移動" || history.Status == "コピー")
                                {
                                    lock (affectedDestDirs)
                                    {
                                        affectedDestDirs.Add(history.DestDir);
                                    }
                                }

                                if (history.Status == "エラー") Interlocked.Increment(ref totalErrorCount);
                                if (!rule.DoNotSaveHistory && !history.Status.StartsWith("スキップ"))
                                {
                                    historyRecords.Add(history);
                                }

                                int currentCount = Interlocked.Increment(ref totalProcessedCount);

                                if (_progressWin != null && currentCount % 3 == 0)
                                {
                                    _progressWin.UpdateState(displayRuleName, itemName, currentCount, totalErrorCount);
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
                        Dispatcher.Invoke(() => Log("[中止] ユーザーによって処理がキャンセルされました。"));
                    }
                });

                progressBar.Value = 100;
                txtStatus.Text = "待機中...";

                if (historyRecords.Any()) HistoryManager.AddHistories(historyRecords.ToList(), CurrentSettings);

                if (!cts.IsCancellationRequested)
                {
                    string endMessage = totalErrorCount > 0 ? $"[終了] 処理完了（{totalErrorCount}件のエラーあり）" : "[終了] すべての処理が正常に完了しました。";
                    Log(endMessage);

                    if (!isAutoExecution && totalErrorCount > 0)
                    {
                        MessageBox.Show($"{totalErrorCount}件のエラーが発生しました。\n詳細は画面下のログを確認してください。",
                                        "一部処理に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            finally
            {
                if (_progressWin != null)
                {
                    _progressWin.CompleteAndClose();
                    _progressWin = null;
                }

                this.IsEnabled = true;
                lvRules.Items.Refresh();
                _isSorting = false;
            }
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

                Log($"ルール「{_copyBuffer.RuleName}」をコピーしました。");
            }
        }

        private void MenuCut_Click(object sender, RoutedEventArgs e)
        {
            if (lvRules.SelectedItem is SortRuleViewModel selected)
            {
                _copyBuffer = selected.Rule.Clone();
                _isCutBuffer = true;

                // リストから即座に削除する
                RulesList.Remove(selected);
                SaveAllRules();

                UpdateMenuState();

                Log($"ルール「{_copyBuffer.RuleName}」を切り取りました。");
            }
        }

        private void MenuPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_copyBuffer != null && lvRules.SelectedItem is SortRuleViewModel selected)
            {
                int index = RulesList.IndexOf(selected);
                var newRule = _copyBuffer.Clone();

                // 切り取りからのペーストか、コピーからのペーストかで名前を変える
                if (!_isCutBuffer)
                {
                    newRule.RuleName += " (コピー)";
                }
                else
                {
                    // 一度切り取ったものを貼り付けた後は、通常コピーと同じ扱いにする
                    _isCutBuffer = false;
                }

                var newRuleVm = new SortRuleViewModel(newRule, PresetList);

                RulesList.Insert(index, newRuleVm);

                // 貼り付けた行を自動で選択状態にする（連続ペーストをしやすくするため）
                lvRules.SelectedItem = newRuleVm;

                SaveAllRules();
                Log("ルールを貼り付けました。");
            }
        }

        private void MenuCheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var rule in RulesList) rule.IsEnabled = true;
            lvRules.Items.Refresh(); // 画面にチェック状態を反映
            SaveAllRules();
        }

        private void MenuUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var rule in RulesList) rule.IsEnabled = false;
            lvRules.Items.Refresh();
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
                string safeRuleName = ruleName ?? "";
                // (万が一ルール名にWindowsパスとして使えない文字が含まれていたらアンダーバーに置換)
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    safeRuleName = safeRuleName.Replace(c.ToString(), "_");
                }

                path = System.Text.RegularExpressions.Regex.Replace(path, "(?i)/NAME", safeRuleName);

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
                    MessageBox.Show($"フォルダが存在しません:\n{path}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }


        // ダブルクリックで選択行の振り分けを実行
        private void LvRules_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var rowItem = GetRowItemFromPoint(e.GetPosition(lvRules));
            // データ行以外（ヘッダー部分や空白）のダブルクリックを無視する
            if (rowItem != null)
            {
                // 選択状態に依存せず、確実に「今ダブルクリックした対象」だけをリストに入れて実行する
                var targetRules = new List<SortRuleViewModel> { rowItem };
                _ = ExecuteSortingAsync(targetRules);

                // 余計なイベント（文字のテキスト選択など）が暴発するのを防ぐ
                e.Handled = true;
            }
        }

        // 右クリック時に、クリックされた行だけを「単一行選択」にする
        private void LvRules_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rowItem = GetRowItemFromPoint(e.GetPosition(lvRules));
            if (rowItem != null)
            {
                lvRules.SelectedItems.Clear();
                lvRules.SelectedItems.Add(rowItem);
            }
            else
            {
                lvRules.SelectedItems.Clear();
            }
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
                Log($"[警告] プロセス優先度の変更に失敗しました: {ex.Message}");
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
                    lvRules.Items.Refresh();
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

                Log($"[システム] 定周期実行が有効になりました（{CurrentSettings.PeriodicIntervalMinutes}分毎）。");
            }
            else
            {
                Log("[システム] 定周期実行は無効です。");
            }
        }

        // タイマーの時間になったら呼ばれる処理
        private async void PeriodicTimer_Tick(object sender, EventArgs e)
        {
            if (_isSorting) return; // 既に処理中なら今回はスキップ(安全装置)

            if (this.IsVisible && this.WindowState != WindowState.Minimized)
            {
                Log("[自動実行] 画面が表示中(操作中)のため、今回の定周期実行をスキップします。");
                return;
            }

            Log("[自動実行] 定周期実行の時間が来ました。処理を開始します。");
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
            _notifyIcon.Text = "Classify8 - 自動振り分け実行中";

            try
            {
                var iconUri = new Uri("pack://application:,,,/c8_icon.ico", UriKind.Absolute);
                using (var stream = Application.GetResourceStream(iconUri)?.Stream)
                {
                    if (stream != null)
                    {
                        _notifyIcon.Icon = new System.Drawing.Icon(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[警告] タスクトレイアイコンの読み込みに失敗しました: {ex.Message}");
                // 万が一読み込めなかった場合は、Windows標準の「i」マークを代わりに出す
                _notifyIcon.Icon = System.Drawing.SystemIcons.Information;
            }
            _notifyIcon.Visible = true;

            // ダブルクリックで画面を復帰
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            // 右クリックメニューの作成
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var openItem = new System.Windows.Forms.ToolStripMenuItem("画面を表示(_O)");
            openItem.Click += (s, e) => ShowMainWindow();
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("終了(_X)");
            exitItem.Click += (s, e) => MenuExit_Click(null, null); // 終了処理を呼ぶ
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
            // --- 完全に終了する ---
            var rulesToSave = RulesList.Select(vm => vm.Rule).ToList();
            DataManager.SaveRules(rulesToSave);
            DataManager.SavePresets(PresetList);

            DataManager.SaveSettings(CurrentSettings);

            // タスクトレイのアイコンを綺麗に消去する
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }


        public void AddImportedRules(List<SortRule> importedRules)
        {
            foreach (var rule in importedRules)
            {
                RulesList.Add(new SortRuleViewModel(rule, PresetList));
            }

            // ファイルへ保存してUIに反映
            SaveAllRules();
            lvRules.Items.Refresh();

            Log($"[インポート] ClassyNyから {importedRules.Count} 件のルールをインポートしました。");
        }

        public void ImportLegacyCsv(string filePath)
        {
            try
            {
                // PresetList を渡して、必要に応じてプリセットを追加させる
                var importedRules = LegacyImporter.ImportFromClassyNyCsv(filePath, PresetList);

                if (importedRules.Count == 0)
                {
                    MessageBox.Show("インポートできるルールが見つかりませんでした。\nファイル形式が正しいか確認してください。", "お知らせ", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var rule in importedRules)
                {
                    RulesList.Add(new SortRuleViewModel(rule, PresetList));
                }

                // 新しく追加されたかもしれないプリセットと、ルールを保存
                DataManager.SavePresets(PresetList);
                SaveAllRules();
                lvRules.Items.Refresh(); // 画面を更新

                Log($"[インポート] ClassyNyから {importedRules.Count} 件のルールをインポートしました。");
                MessageBox.Show($"{importedRules.Count} 件のルールを正常にインポートしました！\n（※設定画面を閉じるとメイン画面に反映されます）", "インポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"インポート中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenRuleEditWindow(string ruleName)
        {
            var targetVm = RulesList.FirstOrDefault(r => r.RuleName == ruleName);
            if (targetVm != null)
            {
                // リスト上で選択状態にし、見える位置までスクロールする
                lvRules.SelectedItem = targetVm;
                lvRules.ScrollIntoView(targetVm);

                // 編集画面を開く
                var window = new RuleEditWindow(targetVm.Rule, PresetList) { Owner = this };
                if (window.ShowDialog() == true)
                {
                    int index = RulesList.IndexOf(targetVm);
                    RulesList.RemoveAt(index);
                    RulesList.Insert(index, new SortRuleViewModel(window.ResultRule, PresetList));
                    SaveAllRules();
                    lvRules.SelectedIndex = index;
                    Log($"ルール「{window.ResultRule.RuleName}」を更新しました。");
                }
            }
            else
            {
                MessageBox.Show($"振り分け条件「{ruleName}」が見つかりません。\nすでに削除されたか、名称が変更された可能性があります。", "お知らせ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Log(string message)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            txtLog.ScrollToEnd();
        }
    }

    // --- GUI表示用のViewModel ---
    public class SortRuleViewModel
    {
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
            set => Rule.IsEnabled = value;
        }

        public string RuleName => Rule.RuleName;
        public string SearchCondition => Rule.SearchCondition;
        public string ActionText => Rule.IsMoveAction ? "移動" : "コピー";

        public string SourcePathDisplay
        {
            get
            {
                if (Rule.SourceMode == "Preset")
                {
                    var preset = _presets?.FirstOrDefault(p => p.Id == Rule.SourcePresetId);
                    return preset != null ? $"[P] {preset.AliasName} ({preset.DirectoryPath})" : "[P] 不明なプリセット";
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
                    return preset != null ? $"[P] {preset.AliasName} ({preset.DirectoryPath})" : "[P] 不明なプリセット";
                }
                return Rule.DestCustomPath;
            }
        }
    }
}