using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Classify8.Core;

namespace Classify8.Views
{
    public partial class RuleEditWindow : Window
    {
        public SortRule ResultRule { get; private set; }

        public RuleEditWindow(SortRule rule, ObservableCollection<Preset> presets)
        {
            InitializeComponent();

            cmbSourcePreset.ItemsSource = presets;
            cmbDestPreset.ItemsSource = presets;

            // Clone() は新しいIDを発行するが、編集では元のルールと同一とみなす
            // (IDが変わると、履歴の RuleId とのひも付けが切れてしまうため)
            ResultRule = rule.Clone();
            ResultRule.Id = rule.Id;
            LoadDataToUI();
        }

        private void LoadDataToUI()
        {
            // --- メイン項目 ---
            chkIsEnabled.IsChecked = ResultRule.IsEnabled;
            txtRuleName.Text = ResultRule.RuleName;
            txtSearchCondition.Text = ResultRule.SearchCondition;

            // 振り分け元
            if (ResultRule.SourceMode == "Preset") rbSourcePreset.IsChecked = true;
            else rbSourceCustom.IsChecked = true;
            cmbSourcePreset.SelectedValue = ResultRule.SourcePresetId;
            txtSourceCustom.Text = ResultRule.SourceCustomPath;
            chkSearchSubDirs.IsChecked = ResultRule.SearchSubDirectories;

            // 振り分け先
            if (ResultRule.DestMode == "Preset") rbDestPreset.IsChecked = true;
            else rbDestCustom.IsChecked = true;
            cmbDestPreset.SelectedValue = ResultRule.DestPresetId;
            txtDestCustom.Text = ResultRule.DestCustomPath;
            chkIsCopy.IsChecked = !ResultRule.IsMoveAction; // 移動アクションではない＝コピー

            // --- 一般タブ ---
            chkIgnoreCase.IsChecked = ResultRule.IgnoreCase;
            chkIgnoreWidth.IsChecked = ResultRule.IgnoreWidth;
            chkIgnoreKana.IsChecked = ResultRule.IgnoreKana;
            chkDoNotSaveHistory.IsChecked = ResultRule.DoNotSaveHistory;
            cmbTargetType.SelectedIndex = (int)ResultRule.TargetType;

            // --- ファイルサイズ限定タブ ---
            chkSizeMinEnabled.IsChecked = ResultRule.SizeMin_Enabled;
            txtSizeMinValue.Text = ResultRule.SizeMin_Value.ToString();
            cmbSizeMinUnit.SelectedIndex = (int)ResultRule.SizeMin_Unit;

            chkSizeMaxEnabled.IsChecked = ResultRule.SizeMax_Enabled;
            txtSizeMaxValue.Text = ResultRule.SizeMax_Value.ToString();
            cmbSizeMaxUnit.SelectedIndex = (int)ResultRule.SizeMax_Unit;

            // --- タイムスタンプ限定タブ ---
            chkDateBeforeEnabled.IsChecked = ResultRule.DateBefore_Enabled;
            dpDateBefore.SelectedDate = ResultRule.DateBefore_Date;

            chkDateAfterEnabled.IsChecked = ResultRule.DateAfter_Enabled;
            dpDateAfter.SelectedDate = ResultRule.DateAfter_Date;

            chkTimeBeforeEnabled.IsChecked = ResultRule.TimeBefore_Enabled;
            txtTimeBeforeValue.Text = ResultRule.TimeBefore_Value.ToString();
            cmbTimeBeforeUnit.SelectedIndex = (int)ResultRule.TimeBefore_Unit;

            chkTimeAfterEnabled.IsChecked = ResultRule.TimeAfter_Enabled;
            txtTimeAfterValue.Text = ResultRule.TimeAfter_Value.ToString();
            cmbTimeAfterUnit.SelectedIndex = (int)ResultRule.TimeAfter_Unit;
        }

        private void SaveDataFromUI()
        {
            // --- メイン項目 ---
            ResultRule.IsEnabled = chkIsEnabled.IsChecked ?? true;
            ResultRule.RuleName = txtRuleName.Text;
            ResultRule.SearchCondition = txtSearchCondition.Text;

            ResultRule.SourceMode = rbSourcePreset.IsChecked == true ? "Preset" : "Custom";
            ResultRule.SourcePresetId = cmbSourcePreset.SelectedValue?.ToString() ?? "";
            ResultRule.SourceCustomPath = txtSourceCustom.Text;
            ResultRule.SearchSubDirectories = chkSearchSubDirs.IsChecked ?? true;

            ResultRule.DestMode = rbDestPreset.IsChecked == true ? "Preset" : "Custom";
            ResultRule.DestPresetId = cmbDestPreset.SelectedValue?.ToString() ?? "";
            ResultRule.DestCustomPath = txtDestCustom.Text;
            ResultRule.IsMoveAction = !(chkIsCopy.IsChecked ?? false); // コピーチェックONならMoveはfalse

            // --- 一般タブ ---
            ResultRule.IgnoreCase = chkIgnoreCase.IsChecked ?? true;
            ResultRule.IgnoreWidth = chkIgnoreWidth.IsChecked ?? true;
            ResultRule.IgnoreKana = chkIgnoreKana.IsChecked ?? true;
            ResultRule.DoNotSaveHistory = chkDoNotSaveHistory.IsChecked ?? false;
            ResultRule.TargetType = (TargetType)cmbTargetType.SelectedIndex;

            // --- ファイルサイズ限定タブ ---
            ResultRule.SizeMin_Enabled = chkSizeMinEnabled.IsChecked ?? false;
            double.TryParse(txtSizeMinValue.Text, out double sizeMin);
            ResultRule.SizeMin_Value = sizeMin;
            ResultRule.SizeMin_Unit = (SizeUnit)cmbSizeMinUnit.SelectedIndex;

            ResultRule.SizeMax_Enabled = chkSizeMaxEnabled.IsChecked ?? false;
            double.TryParse(txtSizeMaxValue.Text, out double sizeMax);
            ResultRule.SizeMax_Value = sizeMax;
            ResultRule.SizeMax_Unit = (SizeUnit)cmbSizeMaxUnit.SelectedIndex;

            // --- タイムスタンプ限定タブ ---
            ResultRule.DateBefore_Enabled = chkDateBeforeEnabled.IsChecked ?? false;
            ResultRule.DateBefore_Date = dpDateBefore.SelectedDate;

            ResultRule.DateAfter_Enabled = chkDateAfterEnabled.IsChecked ?? false;
            ResultRule.DateAfter_Date = dpDateAfter.SelectedDate;

            ResultRule.TimeBefore_Enabled = chkTimeBeforeEnabled.IsChecked ?? false;
            int.TryParse(txtTimeBeforeValue.Text, out int timeBefore);
            ResultRule.TimeBefore_Value = timeBefore;
            ResultRule.TimeBefore_Unit = (TimeUnit)cmbTimeBeforeUnit.SelectedIndex;

            ResultRule.TimeAfter_Enabled = chkTimeAfterEnabled.IsChecked ?? false;
            int.TryParse(txtTimeAfterValue.Text, out int timeAfter);
            ResultRule.TimeAfter_Value = timeAfter;
            ResultRule.TimeAfter_Unit = (TimeUnit)cmbTimeAfterUnit.SelectedIndex;
        }

        // ラジオボタンによるUIの有効/無効の切り替え
        private void SourceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (cmbSourcePreset == null) return;
            cmbSourcePreset.IsEnabled = rbSourcePreset.IsChecked == true;
            txtSourceCustom.IsEnabled = rbSourceCustom.IsChecked == true;
            btnSourceBrowse.IsEnabled = rbSourceCustom.IsChecked == true;
        }

        private void DestMode_Changed(object sender, RoutedEventArgs e)
        {
            if (cmbDestPreset == null) return;
            cmbDestPreset.IsEnabled = rbDestPreset.IsChecked == true;
            txtDestCustom.IsEnabled = rbDestCustom.IsChecked == true;
            btnDestBrowse.IsEnabled = rbDestCustom.IsChecked == true;
        }

        private void BtnSourceBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = Loc.T("dialog.select_source") };
            if (dialog.ShowDialog() == true) txtSourceCustom.Text = dialog.FolderName;
        }

        private void BtnDestBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = Loc.T("dialog.select_dest") };
            if (dialog.ShowDialog() == true) txtDestCustom.Text = dialog.FolderName;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (rbSourcePreset.IsChecked == true && cmbSourcePreset.SelectedValue == null)
            {
                MessageBox.Show(this, Loc.T("msg.source_preset_missing"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (rbSourceCustom.IsChecked == true && string.IsNullOrWhiteSpace(txtSourceCustom.Text))
            {
                MessageBox.Show(this, Loc.T("msg.source_path_missing"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (rbDestPreset.IsChecked == true && cmbDestPreset.SelectedValue == null)
            {
                MessageBox.Show(this, Loc.T("msg.dest_preset_missing"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (rbDestCustom.IsChecked == true && string.IsNullOrWhiteSpace(txtDestCustom.Text))
            {
                MessageBox.Show(this, Loc.T("msg.dest_path_missing"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(txtRuleName.Text))
            {
                MessageBox.Show(this, Loc.T("msg.rule_name_empty"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                txtRuleName.Focus();
                return;
            }

            // 検索条件の構文チェック (実行して初めてエラーになるのを防ぐ)
            try
            {
                new ConditionParser(ignoreCase: true).Parse(txtSearchCondition.Text);
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, Loc.T("title.syntax_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                txtSearchCondition.Focus();
                return;
            }

            // 数値の入力チェック (有効にしている項目のみ)
            if (!ValidateNumber(chkSizeMinEnabled, txtSizeMinValue, Loc.T("validate.size_min"), true)) return;
            if (!ValidateNumber(chkSizeMaxEnabled, txtSizeMaxValue, Loc.T("validate.size_max"), true)) return;
            if (!ValidateNumber(chkTimeBeforeEnabled, txtTimeBeforeValue, Loc.T("validate.time_before"), false)) return;
            if (!ValidateNumber(chkTimeAfterEnabled, txtTimeAfterValue, Loc.T("validate.time_after"), false)) return;

            SaveDataFromUI();

            // 危険な振り分け元 (ドライブ全体・システムフォルダ) は登録できないようにする
            string sourcePath = ResultRule.SourceMode == "Preset"
                ? (cmbSourcePreset.SelectedItem as Preset)?.DirectoryPath
                : ResultRule.SourceCustomPath;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                string expanded = DynamicPathBuilder.BuildPath(sourcePath, "", ResultRule);
                if (PathSafety.IsDangerousSource(expanded, ResultRule.SearchSubDirectories, out string reason))
                {
                    MessageBox.Show(this, Loc.F("msg.dangerous_source", reason, expanded), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            DialogResult = true;
        }

        // 有効にされている数値項目が、0以上の数値になっているか確認する
        private bool ValidateNumber(CheckBox enabled, TextBox box, string label, bool allowDecimal)
        {
            if (enabled.IsChecked != true) return true;

            bool ok;
            if (allowDecimal)
            {
                ok = double.TryParse(box.Text, out double d) && d >= 0;
            }
            else
            {
                ok = int.TryParse(box.Text, out int n) && n >= 0;
            }

            if (!ok)
            {
                MessageBox.Show(this, Loc.F(allowDecimal ? "msg.number_invalid_decimal" : "msg.number_invalid_integer", label), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                box.Focus();
            }
            return ok;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}