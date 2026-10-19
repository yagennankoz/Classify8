using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Classify8.Tests
{
    // 実際にアプリを起動し、マウス・キーボードを操作して確認するテスト。
    // 操作中は他のウィンドウをクリックしたりキーボードを触ったりしないでください。
    [Collection("UI")]
    public class FullUITests : UITestBase
    {
        // ==========================================
        // 既存のテストシナリオ
        // ==========================================
        [Fact]
        public void シナリオ_ルールの追加からリスト選択_そして削除まで()
        {
            AddDummyRule("UI自動テスト用ルール");

            var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
            Assert.NotNull(lvRules);
            
            var addedItem = lvRules.FindFirstDescendant(cf => cf.ByName("UI自動テスト用ルール"));
            Assert.NotNull(addedItem);
            
            addedItem.Click();
            Thread.Sleep(500);

            // 画面上の「削除」ボタンをクリックする
            var btnDelete = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btnDeleteRule"))?.AsButton();
            Assert.NotNull(btnDelete);
            btnDelete.Click();
            Thread.Sleep(1000); 

            // 確認ダイアログを探す
            var desktop = Automation.GetDesktop();
            var confirmDialog = desktop.FindFirstChild(cf => cf.ByName("確認"))?.AsWindow() 
                             ?? MainWindow.FindFirstChild(cf => cf.ByName("確認"))?.AsWindow();
            Assert.NotNull(confirmDialog);

            var btnYes = confirmDialog.FindFirstDescendant(cf => cf.ByName("はい"))?.AsButton() 
                      ?? confirmDialog.FindFirstDescendant(cf => cf.ByAutomationId("6"))?.AsButton();
            Assert.NotNull(btnYes);
            btnYes.Click();
            Thread.Sleep(1000);

            // 削除されたことを確認する
            var deletedItem = lvRules.FindFirstDescendant(cf => cf.ByName("UI自動テスト用ルール"));
            Assert.Null(deletedItem); 
        }

        [Fact]
        public void シナリオ_設定画面を開き_タブを切り替えて保存する()
        {
            var menuSettings = MainWindow.FindFirstDescendant(cf => cf.ByName("設定(_O)"))?.AsMenuItem();
            Assert.NotNull(menuSettings);
            menuSettings.Click();
            Thread.Sleep(500);

            var menuOption = MainWindow.FindFirstDescendant(cf => cf.ByName("オプション"))?.AsMenuItem();
            Assert.NotNull(menuOption);
            menuOption.Click();
            Thread.Sleep(1000);

            var settingsWindow = MainWindow.FindFirstDescendant(cf => cf.ByName("全体設定 (オプション)"))?.AsWindow();
            Assert.NotNull(settingsWindow);

            var tabCondition = settingsWindow.FindFirstDescendant(cf => cf.ByName("実行条件"))?.AsTabItem();
            Assert.NotNull(tabCondition);
            tabCondition.Select();
            Thread.Sleep(1000);

            var chkMinimized = settingsWindow.FindFirstDescendant(cf => cf.ByAutomationId("chkStartMinimized"))?.AsCheckBox();
            Assert.NotNull(chkMinimized);

            // 「最小化で起動」をONのままにすると、次のテストでアプリが画面に出てこなくなるため、
            // 元の状態を覚えておいて、確認が終わったら必ず元に戻す
            bool original = chkMinimized.IsChecked == true;
            try
            {
                if (chkMinimized.IsChecked == false) chkMinimized.Click();

                var btnSave = settingsWindow.FindFirstDescendant(cf => cf.ByName("保存して閉じる"))?.AsButton();
                Assert.NotNull(btnSave);
                btnSave.Click();
                Thread.Sleep(1000);

                var closedWindow = MainWindow.FindFirstDescendant(cf => cf.ByName("全体設定 (オプション)"));
                Assert.Null(closedWindow);
            }
            finally
            {
                SetSettingCheckBox("chkStartMinimized", original);
            }
        }

        
        [Fact]
        public void シナリオ_ショートカットキーでのコピーと貼り付けテスト()
        {
            AddDummyRule("コピペテスト用ルール");

            var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
            var item = lvRules.FindFirstDescendant(cf => cf.ByName("コピペテスト用ルール"));
            Assert.NotNull(item);
            
            // 行をクリックして選択し、フォーカスを合わせる
            item.Click();
            Thread.Sleep(500);

            // Ctrl + C を送信
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            {
                Keyboard.Press(VirtualKeyShort.KEY_C);
            }
            Thread.Sleep(500);

            // Ctrl + V を送信
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            {
                Keyboard.Press(VirtualKeyShort.KEY_V);
            }
            Thread.Sleep(1000);

            // "(コピー)" という名前が追加されたアイテムがリストに存在するか確認
            var copiedItem = lvRules.FindFirstDescendant(cf => cf.ByName("コピペテスト用ルール (コピー)"));
            Assert.NotNull(copiedItem);

            // 終了後にテスト用のゴミデータを削除しておく
            DeleteRuleByName("コピペテスト用ルール (コピー)");
            DeleteRuleByName("コピペテスト用ルール");
        }

        [Fact]
        public void シナリオ_ドラッグアンドドロップによる並び替えテスト()
        {
            // 選択されていない行を、クリックし直さずにそのまま掴んで並び替えられる
            RunDragReorderScenario("DnDテスト1", "DnDテスト2", selectDraggedRowFirst: false);
        }

        [Fact]
        public void シナリオ_選択済みの行をドラッグして並び替えできる()
        {
            // すでに選択されている行を掴んで並び替える
            RunDragReorderScenario("DnD選択済み1", "DnD選択済み2", selectDraggedRowFirst: true);
        }

        [Fact]
        public void シナリオ_チェックボックスを押しても選択中の行は変わらない()
        {
            const string name1 = "チェック選択1";
            const string name2 = "チェック選択2";
            AddDummyRule(name1);
            AddDummyRule(name2);

            try
            {
                var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
                Assert.NotNull(lvRules);

                string[] ordered = new[] { name1, name2 }.OrderBy(n => CenterY(lvRules, n)).ToArray();
                string upperName = ordered[0];
                string lowerName = ordered[1];

                // 上の行を選択しておく
                lvRules.FindFirstDescendant(cf => cf.ByName(upperName)).Click();
                Thread.Sleep(500);
                Assert.True(FindRow(lvRules, upperName).IsSelected, "上の行を選択できていません。");

                // 選択されていない下の行の「有効/無効」チェックボックスを押す
                var checkBox = FindRow(lvRules, lowerName).FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox))?.AsCheckBox();
                Assert.NotNull(checkBox);
                checkBox.Click();
                Thread.Sleep(500);

                // チェックボックスの操作では、選択は変わらない (押した行が選択されたり、選択が解除されたりしない)
                Assert.True(FindRow(lvRules, upperName).IsSelected, "チェックボックスを押したことで、選択中の行が変わってしまいました。");
                Assert.False(FindRow(lvRules, lowerName).IsSelected, "チェックボックスを押した行が選択されてしまいました。");
            }
            finally
            {
                DeleteRuleByName(name1);
                DeleteRuleByName(name2);
            }
        }

        [Fact]
        public void シナリオ_入力エラーのバリデーションダイアログの表示テスト()
        {
            var btnAddRule = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btnAddRule"))?.AsButton();
            Assert.NotNull(btnAddRule);
            btnAddRule.Click();
            Thread.Sleep(1000);

            var editWindow = MainWindow.FindFirstDescendant(cf => cf.ByName("振り分け条件の編集"))?.AsWindow();
            Assert.NotNull(editWindow);

            // ルール名だけを入力し、必須の【振り分け元・先パス】を意図的に空にする
            var txtRuleName = editWindow.FindFirstDescendant(cf => cf.ByAutomationId("txtRuleName"))?.AsTextBox();
            txtRuleName.Text = "空パスエラーテスト";

            var txtSourceCustom = editWindow.FindFirstDescendant(cf => cf.ByAutomationId("txtSourceCustom"))?.AsTextBox();
            txtSourceCustom.Text = ""; 
            
            // 保存ボタンをクリック（ここで警告が出るはず）
            var btnSave = editWindow.FindFirstDescendant(cf => cf.ByName("保存して閉じる"))?.AsButton();
            btnSave.Click();
            Thread.Sleep(3000); 

            // 「入力エラー」の警告ダイアログが出ているか確認
            var errorDialog = FlaUI.Core.Tools.Retry.WhileNull(() =>
            {
                var desktop = Automation.GetDesktop();
                return desktop.FindFirstChild(cf => cf.ByName("入力エラー"))?.AsWindow() 
                    ?? editWindow.FindFirstDescendant(cf => cf.ByName("入力エラー"))?.AsWindow();
            }, System.TimeSpan.FromSeconds(3)).Result;

            Assert.NotNull(errorDialog);


            // ダイアログのOKボタン (AutomationId="2") を押して閉じる
            var btnOk = errorDialog.FindFirstDescendant(cf => cf.ByName("OK"))?.AsButton() 
                     ?? errorDialog.FindFirstDescendant(cf => cf.ByAutomationId("2"))?.AsButton();
            Assert.NotNull(btnOk);
            btnOk.Click();
            Thread.Sleep(500);

            // 編集画面の「キャンセル」ボタンで正しく画面を閉じる
            var btnCancel = editWindow.FindFirstDescendant(cf => cf.ByName("キャンセル"))?.AsButton();
            Assert.NotNull(btnCancel);
            btnCancel.Click();
            Thread.Sleep(500);
        }


        [Fact]
        public void シナリオ_設定名が空のままでは保存できない()
        {
            var edit = OpenNewRuleWindow();
            SetText(edit, "txtRuleName", "");
            SetText(edit, "txtSourceCustom", @"C:\Test\SourceFolder");
            SetText(edit, "txtDestCustom", @"C:\Test\DestFolder");

            ClickSaveExpectingError(edit, "入力エラー", "設定名");
            CancelEditWindow(edit);
        }

        [Fact]
        public void シナリオ_検索条件の構文エラーは保存時に知らされる()
        {
            var edit = OpenNewRuleWindow();
            SetText(edit, "txtRuleName", "構文エラーテスト");
            SetText(edit, "txtSourceCustom", @"C:\Test\SourceFolder");
            SetText(edit, "txtDestCustom", @"C:\Test\DestFolder");
            SetText(edit, "txtSearchCondition", "/( 請求書"); // 閉じ括弧がない

            ClickSaveExpectingError(edit, "検索条件の構文エラー", "閉じ括弧");
            CancelEditWindow(edit);
        }

        [Fact]
        public void シナリオ_システムフォルダは振り分け元に指定できない()
        {
            var edit = OpenNewRuleWindow();
            SetText(edit, "txtRuleName", "システムフォルダテスト");
            SetText(edit, "txtSourceCustom", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            SetText(edit, "txtDestCustom", @"C:\Test\DestFolder");

            ClickSaveExpectingError(edit, "入力エラー", "システムフォルダ");
            CancelEditWindow(edit);
        }


        [Fact]
        public void シナリオ_ルールを実行すると条件に合うファイルだけが振り分けられる()
        {
            const string ruleName = "UI実行テスト用ルール";
            string root = CreateTempRoot();
            string src = Path.Combine(root, "src");
            string dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(src, "a.txt"), "A");
            File.WriteAllText(Path.Combine(src, "b.txt"), "B");
            File.WriteAllText(Path.Combine(src, "c.log"), "C");

            try
            {
                AddRule(ruleName, src, dest, "*.txt");
                DoubleClickRule(ruleName);

                Assert.True(WaitUntil(() => File.Exists(Path.Combine(dest, "a.txt")) && File.Exists(Path.Combine(dest, "b.txt"))),
                    "条件に合うファイルが振り分け先に移動されませんでした。");

                // 条件に合うファイルだけが移動し、合わないファイルは元の場所に残る
                Assert.False(File.Exists(Path.Combine(src, "a.txt")));
                Assert.False(File.Exists(Path.Combine(src, "b.txt")));
                Assert.True(File.Exists(Path.Combine(src, "c.log")));
                Assert.False(File.Exists(Path.Combine(dest, "c.log")));
            }
            finally
            {
                Thread.Sleep(1500);
                DeleteRuleByName(ruleName);
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void シナリオ_内容が同じファイルは重複として元ファイルが片付けられる()
        {
            const string ruleName = "UI重複テスト用ルール";
            string root = CreateTempRoot();
            string src = Path.Combine(root, "src");
            string dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(dest);

            // 同名・同サイズ・同じ内容で、タイムスタンプだけが違う2つのファイル
            string destFile = Path.Combine(dest, "dup.txt");
            string srcFile = Path.Combine(src, "dup.txt");
            File.WriteAllText(destFile, "same content");
            File.WriteAllText(srcFile, "same content");
            File.SetLastWriteTime(destFile, new DateTime(2020, 1, 1, 12, 0, 0));
            File.SetLastWriteTime(srcFile, new DateTime(2024, 6, 1, 12, 0, 0));

            try
            {
                AddRule(ruleName, src, dest, "*.txt");
                DoubleClickRule(ruleName);

                Assert.True(WaitUntil(() => !File.Exists(srcFile)), "重複ファイルの元ファイルが片付けられませんでした。");

                // 振り分け先には1つだけが残り、連番付きのコピー(dup(1).txt)は作られない
                Assert.Single(Directory.GetFiles(dest));
                Assert.True(File.Exists(destFile));
            }
            finally
            {
                Thread.Sleep(1500);
                DeleteRuleByName(ruleName);
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void シナリオ_ダブルクリック実行の前に確認ダイアログを表示できる()
        {
            const string ruleName = "UI確認ダイアログテスト用ルール";
            string root = CreateTempRoot();
            string src = Path.Combine(root, "src");
            string dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(dest);
            string srcFile = Path.Combine(src, "a.txt");
            string destFile = Path.Combine(dest, "a.txt");
            File.WriteAllText(srcFile, "A");

            try
            {
                SetSettingCheckBox("chkConfirmDoubleClick", true);
                AddRule(ruleName, src, dest, "*.txt");

                // 1回目: 確認ダイアログで「いいえ」 → 何も振り分けられない
                DoubleClickRule(ruleName);
                var confirm1 = FindDialog("確認");
                Assert.NotNull(confirm1);
                ClickDialogButton(confirm1, "いいえ", "7");
                Thread.Sleep(2500);
                Assert.True(File.Exists(srcFile));
                Assert.False(File.Exists(destFile));

                // 2回目: 確認ダイアログで「はい」 → 振り分けが実行される
                DoubleClickRule(ruleName);
                var confirm2 = FindDialog("確認");
                Assert.NotNull(confirm2);
                ClickDialogButton(confirm2, "はい", "6");
                Assert.True(WaitUntil(() => File.Exists(destFile) && !File.Exists(srcFile)),
                    "「はい」を選んだのに振り分けが実行されませんでした。");
            }
            finally
            {
                Thread.Sleep(1500);
                SetSettingCheckBox("chkConfirmDoubleClick", false); // 元に戻す
                DeleteRuleByName(ruleName);
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void シナリオ_設定した内容は再度設定画面を開いても保持されている()
        {
            try
            {
                SetSettingCheckBox("chkConfirmDoubleClick", true);
                Assert.True(ReadSettingCheckBox("chkConfirmDoubleClick"), "保存した設定が保持されていません。");

                SetSettingCheckBox("chkConfirmDoubleClick", false);
                Assert.False(ReadSettingCheckBox("chkConfirmDoubleClick"), "設定を元に戻せていません。");
            }
            finally
            {
                SetSettingCheckBox("chkConfirmDoubleClick", false);
            }
        }


        [Fact]
        public void シナリオ_切り取りは貼り付けるまで一覧に残り_貼り付けで移動する()
        {
            AddDummyRule("切り取り1");
            AddDummyRule("切り取り2");
            AddDummyRule("切り取り3");

            try
            {
                var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
                Assert.NotNull(lvRules);

                // 新規ルールは「選択中の行の上」に挿入されるため、追加した順序に頼らず、実際の上下関係(上から順)を調べる
                string[] ordered = new[] { "切り取り1", "切り取り2", "切り取り3" }.OrderBy(n => CenterY(lvRules, n)).ToArray();
                string top = ordered[0];
                string middle = ordered[1];
                string bottom = ordered[2];

                int rowCountBefore = lvRules.Items.Length;

                // いちばん上の行を切り取る (Ctrl + X)
                lvRules.FindFirstDescendant(cf => cf.ByName(top)).Click();
                Thread.Sleep(500);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                {
                    Keyboard.Press(VirtualKeyShort.KEY_X);
                }
                Thread.Sleep(800);

                // 貼り付けるまでは、一覧から消えない
                Assert.NotNull(lvRules.FindFirstDescendant(cf => cf.ByName(top)));

                // いちばん下の行を選んで貼り付ける (Ctrl + V) → 選択行の上へ移動する
                lvRules.FindFirstDescendant(cf => cf.ByName(bottom)).Click();
                Thread.Sleep(500);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                {
                    Keyboard.Press(VirtualKeyShort.KEY_V);
                }
                Thread.Sleep(1000);

                double yMiddle = CenterY(lvRules, middle);
                double yCut = CenterY(lvRules, top);
                double yBottom = CenterY(lvRules, bottom);

                // 並び順 (上から): 真ん中だった行 → 切り取った行 → 貼り付け先の行
                Assert.True(yMiddle < yCut && yCut < yBottom, "切り取り・貼り付けで期待した順序になっていません。");

                // 移動なので、行数は増えていない (複製ではない)
                Assert.Equal(rowCountBefore, lvRules.Items.Length);
            }
            finally
            {
                DeleteRuleByName("切り取り1");
                DeleteRuleByName("切り取り2");
                DeleteRuleByName("切り取り3");
            }
        }

        // ==========================================
        // UIテスト用の補助(ヘルパー)メソッド群
        // ==========================================
        private void AddDummyRule(string ruleName)
        {
            AddRule(ruleName, @"C:\Test\SourceFolder", @"C:\Test\DestFolder");
        }

        // ルールを1件追加する (振り分け元・先は個別指定)
        private void AddRule(string ruleName, string sourcePath, string destPath, string searchCondition = null)
        {
            var editWindow = OpenNewRuleWindow();

            SetText(editWindow, "txtRuleName", ruleName);
            SetText(editWindow, "txtSourceCustom", sourcePath);
            SetText(editWindow, "txtDestCustom", destPath);
            if (searchCondition != null) SetText(editWindow, "txtSearchCondition", searchCondition);

            var btnSave = editWindow.FindFirstDescendant(cf => cf.ByName("保存して閉じる"))?.AsButton();
            Assert.NotNull(btnSave);
            btnSave.Click();
            Thread.Sleep(1000);
        }

        private Window OpenNewRuleWindow()
        {
            var btnAddRule = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btnAddRule"))?.AsButton();
            Assert.NotNull(btnAddRule);
            btnAddRule.Click();
            Thread.Sleep(1000);

            var editWindow = MainWindow.FindFirstDescendant(cf => cf.ByName("振り分け条件の編集"))?.AsWindow();
            Assert.NotNull(editWindow);
            return editWindow;
        }

        private static void SetText(Window window, string automationId, string text)
        {
            var box = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsTextBox();
            Assert.NotNull(box);
            box.Text = text;
        }

        // 保存ボタンを押し、指定したタイトルのエラーダイアログが出て、メッセージに指定の文字が含まれることを確認して閉じる
        private void ClickSaveExpectingError(Window editWindow, string dialogTitle, string expectedMessagePart)
        {
            var btnSave = editWindow.FindFirstDescendant(cf => cf.ByName("保存して閉じる"))?.AsButton();
            Assert.NotNull(btnSave);
            btnSave.Click();

            var dialog = FindDialog(dialogTitle, editWindow);
            Assert.NotNull(dialog);

            string text = GetDialogText(dialog);
            Assert.True(text.Contains(expectedMessagePart), $"ダイアログのメッセージに「{expectedMessagePart}」が含まれていません: {text}");

            ClickDialogButton(dialog, "OK", "2");
            Thread.Sleep(500);
        }

        private void CancelEditWindow(Window editWindow)
        {
            var btnCancel = editWindow.FindFirstDescendant(cf => cf.ByName("キャンセル"))?.AsButton();
            Assert.NotNull(btnCancel);
            btnCancel.Click();
            Thread.Sleep(500);
        }

        // 指定した名前のダイアログ(メッセージボックス)が出るのを待って返す。見つからなければ null
        private Window FindDialog(string title, AutomationElement owner = null, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var desktop = Automation.GetDesktop();
                var dialog = desktop.FindFirstChild(cf => cf.ByName(title))?.AsWindow()
                          ?? owner?.FindFirstDescendant(cf => cf.ByName(title))?.AsWindow()
                          ?? MainWindow.FindFirstDescendant(cf => cf.ByName(title))?.AsWindow();
                if (dialog != null) return dialog;
                Thread.Sleep(200);
            }
            return null;
        }

        private static string GetDialogText(Window dialog)
        {
            var texts = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
            return string.Join("\n", texts.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)));
        }

        // ボタンの表示名、またはメッセージボックスの標準ID(OK=2, はい=6, いいえ=7)で押す
        private static void ClickDialogButton(Window dialog, string name, string standardId)
        {
            var button = dialog.FindFirstDescendant(cf => cf.ByName(name))?.AsButton()
                      ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId(standardId))?.AsButton();
            Assert.NotNull(button);
            button.Click();
            Thread.Sleep(500);
        }

        private void DoubleClickRule(string ruleName)
        {
            var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
            var item = lvRules?.FindFirstDescendant(cf => cf.ByName(ruleName));
            Assert.NotNull(item);
            item.DoubleClick();
        }

        // 2つのルールを追加し、下の行を上の行の上へドラッグして、上下が入れ替わることを確認する
        private void RunDragReorderScenario(string name1, string name2, bool selectDraggedRowFirst)
        {
            AddDummyRule(name1);
            AddDummyRule(name2);

            try
            {
                var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
                Assert.NotNull(lvRules);

                // 新規ルールは「選択中の行の上」に挿入されるため、追加した順序に頼らず、実際の上下関係を調べる
                string[] ordered = new[] { name1, name2 }.OrderBy(n => CenterY(lvRules, n)).ToArray();
                string upperName = ordered[0];
                string lowerName = ordered[1];
                Assert.True(CenterY(lvRules, upperName) < CenterY(lvRules, lowerName), "初期状態の並び順を判定できません。");

                // 掴む行(下の行)の選択状態を、シナリオに合わせて整える
                // ・選択済みシナリオ: 下の行をクリックして選択しておく
                // ・直接ドラッグのシナリオ: 上の行を選択し、下の行は「選択されていない」状態にしておく
                string rowToSelect = selectDraggedRowFirst ? lowerName : upperName;
                lvRules.FindFirstDescendant(cf => cf.ByName(rowToSelect)).Click();
                Thread.Sleep(500);
                Assert.Equal(selectDraggedRowFirst, FindRow(lvRules, lowerName).IsSelected);

                DragRule(lvRules, lowerName, upperName);

                // 下にあった行が、上にあった行より上へ移動しているはず (上下が入れ替わる)
                double newUpperY = CenterY(lvRules, upperName);
                double newLowerY = CenterY(lvRules, lowerName);
                Assert.True(newLowerY < newUpperY, "ドラッグ＆ドロップによる並び替えが正しく反映されていません。");
            }
            finally
            {
                DeleteRuleByName(name1);
                DeleteRuleByName(name2);
            }
        }

        // fromName の行の中心を掴み、toName の行の中心までドラッグして離す (行の上にドロップすると、その行の上に挿入される)
        private static void DragRule(ListBox lvRules, string fromName, string toName)
        {
            var from = lvRules.FindFirstDescendant(cf => cf.ByName(fromName));
            var to = lvRules.FindFirstDescendant(cf => cf.ByName(toName));
            Assert.NotNull(from);
            Assert.NotNull(to);

            var rectFrom = from.BoundingRectangle;
            var rectTo = to.BoundingRectangle;
            int startX = (int)(rectFrom.X + (rectFrom.Width / 2.0));
            int startY = (int)(rectFrom.Y + (rectFrom.Height / 2.0));
            int endX = (int)(rectTo.X + (rectTo.Width / 2.0));
            int endY = (int)(rectTo.Y + (rectTo.Height / 2.0));

            // 1. 移動元へカーソルを合わせてマウスの左ボタンを押し込む
            Mouse.MoveTo(startX, startY);
            Thread.Sleep(300);
            Mouse.Down(MouseButton.Left);
            Thread.Sleep(300);

            // 2. WPFに「ドラッグが開始された」と認識させるため、少しずつ動かしながら目的の位置まで移動する
            const int steps = 12;
            for (int i = 1; i <= steps; i++)
            {
                Mouse.MoveTo(startX + (endX - startX) * i / steps, startY + (endY - startY) * i / steps);
                Thread.Sleep(60);
            }
            Thread.Sleep(300);

            // 3. マウスのボタンを離してドロップする
            Mouse.Up(MouseButton.Left);
            Thread.Sleep(1000); // UIの描画更新待ち
        }

        // 指定した名前のルールが表示されている行(ListViewItem)を返す
        private static ListBoxItem FindRow(ListBox lvRules, string ruleName)
        {
            var row = lvRules.Items.FirstOrDefault(r => r.FindFirstDescendant(cf => cf.ByName(ruleName)) != null);
            Assert.NotNull(row);
            return row;
        }

        private static double CenterY(ListBox list, string ruleName)
        {
            var item = list.FindFirstDescendant(cf => cf.ByName(ruleName));
            Assert.NotNull(item);
            var rect = item.BoundingRectangle;
            return rect.Y + (rect.Height / 2.0);
        }

        // --- 全体設定の操作 ---------------------------------------------------------

        private Window OpenSettingsWindow()
        {
            var menuSettings = MainWindow.FindFirstDescendant(cf => cf.ByName("設定(_O)"))?.AsMenuItem();
            Assert.NotNull(menuSettings);
            menuSettings.Click();
            Thread.Sleep(500);

            var menuOption = MainWindow.FindFirstDescendant(cf => cf.ByName("オプション"))?.AsMenuItem();
            Assert.NotNull(menuOption);
            menuOption.Click();
            Thread.Sleep(1000);

            var settingsWindow = MainWindow.FindFirstDescendant(cf => cf.ByName("全体設定 (オプション)"))?.AsWindow();
            Assert.NotNull(settingsWindow);

            var tabCondition = settingsWindow.FindFirstDescendant(cf => cf.ByName("実行条件"))?.AsTabItem();
            Assert.NotNull(tabCondition);
            tabCondition.Select();
            Thread.Sleep(500);
            return settingsWindow;
        }

        private void SaveAndCloseSettings(Window settingsWindow)
        {
            var btnSave = settingsWindow.FindFirstDescendant(cf => cf.ByName("保存して閉じる"))?.AsButton();
            Assert.NotNull(btnSave);
            btnSave.Click();
            Thread.Sleep(1000);
        }

        // 「実行条件」タブのチェックボックスを、指定した状態にして保存する
        private void SetSettingCheckBox(string automationId, bool value)
        {
            var settingsWindow = OpenSettingsWindow();
            var checkBox = settingsWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsCheckBox();
            Assert.NotNull(checkBox);
            if ((checkBox.IsChecked == true) != value) checkBox.Click();
            SaveAndCloseSettings(settingsWindow);
        }

        // 現在の状態を読む (何も変えずに保存して閉じる)
        private bool ReadSettingCheckBox(string automationId)
        {
            var settingsWindow = OpenSettingsWindow();
            var checkBox = settingsWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsCheckBox();
            Assert.NotNull(checkBox);
            bool state = checkBox.IsChecked == true;
            SaveAndCloseSettings(settingsWindow);
            return state;
        }

        // --- ファイル・待機 ---------------------------------------------------------

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "c8ui_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try { Directory.Delete(path, true); } catch { }
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

        private void DeleteRuleByName(string ruleName)
        {
            var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
            var item = lvRules?.FindFirstDescendant(cf => cf.ByName(ruleName));
            if (item != null)
            {
                item.Click();
                Thread.Sleep(500);

                var btnDelete = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btnDeleteRule"))?.AsButton();
                btnDelete.Click();
                Thread.Sleep(1000);

                var desktop = Automation.GetDesktop();
                var confirmDialog = desktop.FindFirstChild(cf => cf.ByName("確認"))?.AsWindow() 
                                 ?? MainWindow.FindFirstChild(cf => cf.ByName("確認"))?.AsWindow();
                
                var btnYes = confirmDialog?.FindFirstDescendant(cf => cf.ByName("はい"))?.AsButton() 
                          ?? confirmDialog?.FindFirstDescendant(cf => cf.ByAutomationId("6"))?.AsButton();
                btnYes?.Click();
                Thread.Sleep(1000);
            }
        }
    }
}
