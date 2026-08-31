using System.Threading;
using System.Linq;
using Xunit;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.AutomationElements;

namespace Classify8.Tests
{
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
            if (chkMinimized.IsChecked == false) chkMinimized.Click();

            var btnSave = settingsWindow.FindFirstDescendant(cf => cf.ByName("保存して閉じる"))?.AsButton();
            btnSave.Click();
            Thread.Sleep(1000);

            var closedWindow = MainWindow.FindFirstDescendant(cf => cf.ByName("全体設定 (オプション)"));
            Assert.Null(closedWindow);
        }

        // ==========================================
        // 新規追加のテストシナリオ
        // ==========================================
        
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
            AddDummyRule("DnDテスト1");
            AddDummyRule("DnDテスト2");

            var lvRules = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("lvRules"))?.AsListBox();
            var item1 = lvRules.FindFirstDescendant(cf => cf.ByName("DnDテスト1"));
            var item2 = lvRules.FindFirstDescendant(cf => cf.ByName("DnDテスト2"));
            Assert.NotNull(item1);
            Assert.NotNull(item2);

            var rect1 = item1.BoundingRectangle;
            var rect2 = item2.BoundingRectangle;

            // 中心座標を自力で計算する (Top + Height/2)
            double y1 = rect1.Y + (rect1.Height / 2.0);
            double y2 = rect2.Y + rect2.Height;
            Assert.True(y1 < y2, "初期状態の並び順が期待と異なります。");

            // マウス操作をシミュレート
            int startX = (int)(rect2.X + (rect2.Width / 2.0));
            int startY = (int)y2;
            int endX = (int)(rect1.X + (rect1.Width / 2.0));
            int endY = (int)y1;

            // 1. 移動元へカーソルを合わせてマウスの左ボタンを押し込む
            Mouse.MoveTo(startX, startY);
            Thread.Sleep(300);
            Mouse.Down(MouseButton.Left);
            Thread.Sleep(2000);
            Mouse.Down(MouseButton.Left);
            Thread.Sleep(300);
            
            // 2. WPFに「ドラッグが開始された」と認識させるため、上へ20pxだけ動かす
            Mouse.MoveTo(startX, startY - 20);
            Thread.Sleep(300);

            // 3. 目的の位置まで移動させて、マウスのボタンを離す
            Mouse.MoveTo(endX, endY);
            Thread.Sleep(300);
            Mouse.Up(MouseButton.Left);
            Thread.Sleep(1000); // UIの描画更新待ち

            // リストの描画が更新された後の要素を再取得
            item1 = lvRules.FindFirstDescendant(cf => cf.ByName("DnDテスト1"));
            item2 = lvRules.FindFirstDescendant(cf => cf.ByName("DnDテスト2"));

            double newY1 = item1.BoundingRectangle.Y + (item1.BoundingRectangle.Height / 2.0);
            double newY2 = item2.BoundingRectangle.Y + (item2.BoundingRectangle.Height / 2.0);

            // item2 が上に移動しているはずなので、Y座標が逆転していることを確認
            Assert.True(newY2 < newY1, "ドラッグ＆ドロップによる並び替えが正しく反映されていません。");

            DeleteRuleByName("DnDテスト1");
            DeleteRuleByName("DnDテスト2");
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

        // ==========================================
        // UIテスト用の補助(ヘルパー)メソッド群
        // ==========================================
        private void AddDummyRule(string ruleName)
        {
            var btnAddRule = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btnAddRule"))?.AsButton();
            btnAddRule.Click();
            Thread.Sleep(1000);

            var editWindow = MainWindow.FindFirstDescendant(cf => cf.ByName("振り分け条件の編集"))?.AsWindow();

            var txtRuleName = editWindow.FindFirstDescendant(cf => cf.ByAutomationId("txtRuleName"))?.AsTextBox();
            txtRuleName.Text = ruleName;

            var txtSourceCustom = editWindow.FindFirstDescendant(cf => cf.ByAutomationId("txtSourceCustom"))?.AsTextBox();
            txtSourceCustom.Text = @"C:\Test\SourceFolder";

            var txtDestCustom = editWindow.FindFirstDescendant(cf => cf.ByAutomationId("txtDestCustom"))?.AsTextBox();
            txtDestCustom.Text = @"C:\Test\DestFolder";
            
            var btnSave = editWindow.FindFirstDescendant(cf => cf.ByName("保存して閉じる"))?.AsButton();
            btnSave.Click();
            Thread.Sleep(1000);
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