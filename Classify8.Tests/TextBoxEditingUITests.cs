using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Classify8.Tests
{
    // テキストボックスの編集操作 (右クリックメニュー・キーボードでの切り取り/コピー/貼り付け) を、
    // 各画面のすべての入力欄について、実際の画面で確認するテスト。
    // 操作中は他のウィンドウをクリックしたりキーボードを触ったりしないでください。
    [Collection("UI")]
    public class TextBoxEditingUITests : UITestBase
    {
        [Fact]
        public void ルール編集画面のすべての入力欄で切り取りと貼り付けができる()
        {
            var edit = OpenWindowByButton("btnAddRule", "振り分け条件の編集");
            var failures = new List<string>();

            foreach (var id in new[] { "txtRuleName", "txtSourceCustom", "txtDestCustom", "txtSearchCondition" })
                CheckCutAndPaste(edit, id, failures);

            SelectTab(edit, "ファイルサイズ限定");
            foreach (var id in new[] { "txtSizeMinValue", "txtSizeMaxValue" })
                CheckCutAndPaste(edit, id, failures);

            SelectTab(edit, "タイムスタンプ限定");
            foreach (var id in new[] { "txtTimeBeforeValue", "txtTimeAfterValue" })
                CheckCutAndPaste(edit, id, failures);

            ClickButton(edit, "キャンセル");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void 設定画面のすべての入力欄で切り取りと貼り付けができる()
        {
            var settings = OpenSettingsWindow();
            var failures = new List<string>();

            SelectTab(settings, "振り分けないファイル");
            CheckCutAndPaste(settings, "txtNgKeywords", failures);

            // 定周期実行の間隔は、定周期実行を ON にしたときだけ入力できる
            SelectTab(settings, "実行条件");
            var rbPeriodicOn = settings.FindFirstDescendant(cf => cf.ByAutomationId("rbPeriodicOn"))?.AsRadioButton();
            Assert.NotNull(rbPeriodicOn);
            if (!rbPeriodicOn.IsChecked) rbPeriodicOn.Click();
            Thread.Sleep(300);

            foreach (var id in new[] { "txtPeriodicMinutes", "txtHistoryLimit" })
                CheckCutAndPaste(settings, id, failures);

            ClickButton(settings, "キャンセル");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void プリセット管理画面のすべての入力欄で切り取りと貼り付けができる()
        {
            var presets = OpenWindowByButton("btnPreset", "プリセット管理");
            ClickButton(presets, "＋ 新規追加");
            var failures = new List<string>();

            foreach (var id in new[] { "txtAliasName", "txtDirectoryPath" })
                CheckCutAndPaste(presets, id, failures);

            ClickButton(presets, "キャンセル");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void 文字のない部分を右クリックしても選択は解除されず切り取りできる()
        {
            var edit = OpenWindowByButton("btnAddRule", "振り分け条件の編集");
            var failures = new List<string>();

            foreach (var id in new[] { "txtRuleName", "txtSearchCondition" })
            {
                var box = FindTextBox(edit, id);
                box.Text = "abc";
                SelectAllByKeyboard(box);

                // 入力欄の右端付近 (文字のない部分) を右クリックして「切り取り」
                var rect = box.BoundingRectangle;
                Mouse.Click(new System.Drawing.Point(rect.Right - 10, rect.Top + rect.Height / 2), MouseButton.Right);
                Thread.Sleep(500);
                Keyboard.Type(AccessKeys["切り取り"]);
                Thread.Sleep(500);

                if (box.Text != "") failures.Add($"{id}: 文字のない部分を右クリックして「切り取り」した後も文字が残っている (「{box.Text}」)");
            }

            ClickButton(edit, "キャンセル");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void 右クリックメニューのコピーは別の入力欄に貼り付けられる()
        {
            var edit = OpenWindowByButton("btnAddRule", "振り分け条件の編集");
            var name = FindTextBox(edit, "txtRuleName");
            var condition = FindTextBox(edit, "txtSearchCondition");

            name.Text = "copy-source";
            condition.Text = "";

            ClickContextMenuItem(name, "すべて選択");
            ClickContextMenuItem(name, "コピー");
            Assert.Equal("copy-source", name.Text);

            ClickContextMenuItem(condition, "貼り付け");
            Assert.Equal("copy-source", condition.Text);

            ClickButton(edit, "キャンセル");
        }

        [Fact]
        public void 文字が収まらない入力欄をダブルクリックしても次のクリックが効く()
        {
            var log = new List<string>();

            // 設定画面: 幅の狭い入力欄
            var settings = OpenSettingsWindow();
            SelectTab(settings, "実行条件");
            var rbPeriodicOn = settings.FindFirstDescendant(cf => cf.ByAutomationId("rbPeriodicOn"))?.AsRadioButton();
            Assert.NotNull(rbPeriodicOn);
            if (!rbPeriodicOn.IsChecked) rbPeriodicOn.Click();
            Thread.Sleep(300);
            var recycle = settings.FindFirstDescendant(cf => cf.ByAutomationId("chkMoveToRecycleBin"))?.AsCheckBox();
            Assert.NotNull(recycle);
            foreach (var id in new[] { "txtPeriodicMinutes", "txtHistoryLimit" })
                CheckClickAfterDoubleClick(settings, id, "abc123", recycle, log);
            ClickButton(settings, "キャンセル");

            // ルール編集画面: 幅の広い入力欄に長い文字を入れた場合
            var edit = OpenWindowByButton("btnAddRule", "振り分け条件の編集");
            var chkEnabled = edit.FindFirstDescendant(cf => cf.ByAutomationId("chkIsEnabled"))?.AsCheckBox();
            Assert.NotNull(chkEnabled);
            CheckClickAfterDoubleClick(edit, "txtRuleName", new string('a', 30) + " " + new string('b', 30), chkEnabled, log);
            ClickButton(edit, "キャンセル");

            Assert.True(log.Count == 0, string.Join("\n", log));
        }

        // 入力欄をダブルクリックした直後に、チェックボックスを1回クリックして切り替わるかを確認する
        private static void CheckClickAfterDoubleClick(Window window, string automationId, string text, CheckBox checkBox, List<string> failures)
        {
            var box = FindTextBox(window, automationId);
            TypeText(box, text);
            Thread.Sleep(600); // ダブルクリックと見なされないよう、直前のクリックから間をあける
            Mouse.DoubleClick(TextPoint(box));
            Thread.Sleep(500);

            bool before = checkBox.IsChecked == true;
            checkBox.Click();
            Thread.Sleep(500);
            bool toggled = (checkBox.IsChecked == true) != before;
            if (!toggled) failures.Add($"{automationId}: ダブルクリックの後、次のクリックが効かなかった");

            // チェックボックスを元の状態に戻す
            for (int i = 0; i < 3 && (checkBox.IsChecked == true) != before; i++)
            {
                checkBox.Click();
                Thread.Sleep(300);
            }
        }
        // --- 確認の手順 -------------------------------------------------------------

        // 右クリックメニューとキーボードの両方で、全選択 → 切り取り (空になる) → 貼り付け (元に戻る) を確認する
        private static void CheckCutAndPaste(Window window, string automationId, List<string> failures)
        {
            var box = FindTextBox(window, automationId);
            const string sample = "abc123";

            // 右クリックメニュー
            TypeText(box, sample);
            if (box.Text != sample) { failures.Add($"{automationId}: 入力した文字が入っていない (「{box.Text}」)"); return; }
            SelectAllByKeyboard(box);
            ClickContextMenuItem(box, "切り取り");
            if (box.Text != "") failures.Add($"{automationId}: 右クリックの「切り取り」後も文字が残っている (「{box.Text}」)");
            ClickContextMenuItem(box, "貼り付け");
            if (box.Text != sample) failures.Add($"{automationId}: 右クリックの「貼り付け」後の文字が違う (「{box.Text}」)");

            // キーボード
            TypeText(box, sample);
            if (box.Text != sample) { failures.Add($"{automationId}: 入力した文字が入っていない (「{box.Text}」)"); return; }
            SelectAllByKeyboard(box);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_X);
            Thread.Sleep(300);
            if (box.Text != "") failures.Add($"{automationId}: Ctrl+X 後も文字が残っている (「{box.Text}」)");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
            Thread.Sleep(300);
            if (box.Text != sample) failures.Add($"{automationId}: Ctrl+V 後の文字が違う (「{box.Text}」)");
        }

        // --- 補助 -------------------------------------------------------------------

        private Window OpenWindowByButton(string buttonId, string windowTitle)
        {
            var button = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(buttonId))?.AsButton();
            Assert.True(button != null, $"ボタン {buttonId} が見つかりません");
            button.Click();
            Thread.Sleep(1000);

            var window = MainWindow.FindFirstDescendant(cf => cf.ByName(windowTitle))?.AsWindow();
            Assert.True(window != null, $"画面「{windowTitle}」が見つかりません");
            return window;
        }

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

            var window = MainWindow.FindFirstDescendant(cf => cf.ByName("全体設定 (オプション)"))?.AsWindow();
            Assert.NotNull(window);
            return window;
        }

        private static TextBox FindTextBox(AutomationElement window, string automationId)
        {
            var box = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsTextBox();
            Assert.True(box != null, $"入力欄 {automationId} が見つかりません");
            return box;
        }

        private static void SelectTab(Window window, string tabName)
        {
            var tab = window.FindFirstDescendant(cf => cf.ByName(tabName))?.AsTabItem();
            Assert.True(tab != null, $"タブ「{tabName}」が見つかりません");
            tab.Select();
            Thread.Sleep(500);
        }

        private static void ClickButton(Window window, string name)
        {
            var button = window.FindFirstDescendant(cf => cf.ByName(name))?.AsButton();
            Assert.True(button != null, $"ボタン「{name}」が見つかりません");
            button.Click();
            Thread.Sleep(500);
        }

        // 文字のある左上付近の座標。選択範囲の外を右クリックすると選択が解除されるため、文字の上を狙う。
        private static System.Drawing.Point TextPoint(TextBox box)
        {
            var rect = box.BoundingRectangle;
            return new System.Drawing.Point(rect.Left + 8, rect.Top + Math.Min(12, rect.Height / 2));
        }

        // 人が入力するのと同じ手順 (クリック → 全選択 → キーボードで入力) で、入力欄の文字を置き換える
        private static void TypeText(TextBox box, string text)
        {
            SelectAllByKeyboard(box);
            Keyboard.Type(text);
            Thread.Sleep(300);
        }

        // 入力欄にフォーカスがなければクリックしてから、Ctrl+A で全選択する。
        // フォーカスがあるときにクリックしないのは、直前のクリックと合わせて意図しないダブルクリックにしないため。
        private static void SelectAllByKeyboard(TextBox box)
        {
            if (!box.Properties.HasKeyboardFocus.ValueOrDefault)
            {
                Mouse.Click(TextPoint(box));
                Thread.Sleep(300);
            }
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Thread.Sleep(200);
        }

        // テキストボックスを右クリックし、開いたメニューの項目をアクセスキーで選ぶ (人が使うときと同じ操作)。
        // アクセスキーは今開いているメニューにしか効かないため、閉じた別のメニューの項目を誤って押すことがない。
        // 項目が無効な場合は何も起きないので、操作後の文字で結果を確認する。
        private static readonly Dictionary<string, VirtualKeyShort> AccessKeys = new Dictionary<string, VirtualKeyShort>
        {
            ["切り取り"] = VirtualKeyShort.KEY_T,
            ["コピー"] = VirtualKeyShort.KEY_C,
            ["貼り付け"] = VirtualKeyShort.KEY_P,
            ["すべて選択"] = VirtualKeyShort.KEY_A,
        };

        private static void ClickContextMenuItem(TextBox box, string itemName)
        {
            Mouse.Click(TextPoint(box), MouseButton.Right);
            Thread.Sleep(800);
            Keyboard.Type(AccessKeys[itemName]);
            Thread.Sleep(500);
        }
    }
}
