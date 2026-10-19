using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Classify8.Core;

namespace Classify8.Views
{
    public partial class SettingsWindow : Window
    {
        public AppSettings ResultSettings { get; private set; }

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            ResultSettings = currentSettings.Clone();
            LoadDataToUI();
        }

        private void LoadDataToUI()
        {
            // 表示言語: 「自動」+ 選べる言語の一覧
            var languages = new List<LanguageInfo> { new LanguageInfo(Loc.AutoSetting, Loc.T("settings.language_auto")) };
            languages.AddRange(Loc.AvailableLanguages);
            cmbLanguage.ItemsSource = languages;
            var selectedLanguage = languages.FirstOrDefault(l => l.Code.Equals(ResultSettings.Language, StringComparison.OrdinalIgnoreCase));
            cmbLanguage.SelectedValue = selectedLanguage?.Code ?? Loc.AutoSetting;

            // 同名ファイル
            SetRadioFromResolution(ResultSettings.SameSizeResolution, rbSameKeepNewer, rbSameKeepOlder, rbSameRename, rbSameSkip);
            SetRadioFromResolution(ResultSettings.DiffSizeResolution, rbDiffKeepNewer, rbDiffKeepOlder, rbDiffRename, rbDiffSkip);

            chkCompareContent.IsChecked = ResultSettings.CompareContentWhenTimestampDiffers;

            // NGキーワード
            txtNgKeywords.Text = ResultSettings.GlobalNgKeywords;

            // 実行条件
            if (ResultSettings.EnablePeriodicExecution) rbPeriodicOn.IsChecked = true;
            else rbPeriodicOff.IsChecked = true;
            txtPeriodicMinutes.Text = ResultSettings.PeriodicIntervalMinutes.ToString();

            chkMoveToRecycleBin.IsChecked = ResultSettings.MoveToRecycleBin;
            chkAutoCreateDest.IsChecked = ResultSettings.AutoCreateDestFolder;
            chkStartMinimized.IsChecked = ResultSettings.StartMinimized;
            chkSilentReadOnly.IsChecked = ResultSettings.SilentReadOnly;
            chkConfirmDoubleClick.IsChecked = ResultSettings.ConfirmBeforeDoubleClickRun;

            chkLimitHistory.IsChecked = ResultSettings.LimitHistoryCount;
            txtHistoryLimit.Text = ResultSettings.HistoryLimitHundreds.ToString();

            if (ResultSettings.ProcessPriority == ProcessPriorityClass.High) rbPriorityHigh.IsChecked = true;
            else if (ResultSettings.ProcessPriority == ProcessPriorityClass.BelowNormal) rbPriorityLow.IsChecked = true;
            else if (ResultSettings.ProcessPriority == ProcessPriorityClass.Idle) rbPriorityIdle.IsChecked = true;
            else rbPriorityNormal.IsChecked = true;
        }

        private void SaveDataFromUI()
        {
            ResultSettings.Language = cmbLanguage.SelectedValue as string ?? Loc.AutoSetting;

            ResultSettings.SameSizeResolution = GetResolutionFromRadio(rbSameKeepNewer, rbSameKeepOlder, rbSameRename, rbSameSkip);
            ResultSettings.DiffSizeResolution = GetResolutionFromRadio(rbDiffKeepNewer, rbDiffKeepOlder, rbDiffRename, rbDiffSkip);

            ResultSettings.CompareContentWhenTimestampDiffers = chkCompareContent.IsChecked ?? true;

            ResultSettings.GlobalNgKeywords = txtNgKeywords.Text;

            ResultSettings.EnablePeriodicExecution = rbPeriodicOn.IsChecked == true;
            if (int.TryParse(txtPeriodicMinutes.Text, out int minutes)) ResultSettings.PeriodicIntervalMinutes = minutes;

            ResultSettings.MoveToRecycleBin = chkMoveToRecycleBin.IsChecked ?? true;
            ResultSettings.AutoCreateDestFolder = chkAutoCreateDest.IsChecked ?? true;
            ResultSettings.StartMinimized = chkStartMinimized.IsChecked ?? false;
            ResultSettings.SilentReadOnly = chkSilentReadOnly.IsChecked ?? true;
            ResultSettings.ConfirmBeforeDoubleClickRun = chkConfirmDoubleClick.IsChecked ?? false;

            ResultSettings.LimitHistoryCount = chkLimitHistory.IsChecked ?? true;
            if (int.TryParse(txtHistoryLimit.Text, out int hundreds)) ResultSettings.HistoryLimitHundreds = hundreds;

            if (rbPriorityHigh.IsChecked == true) ResultSettings.ProcessPriority = ProcessPriorityClass.High;
            else if (rbPriorityLow.IsChecked == true) ResultSettings.ProcessPriority = ProcessPriorityClass.BelowNormal;
            else if (rbPriorityIdle.IsChecked == true) ResultSettings.ProcessPriority = ProcessPriorityClass.Idle;
            else ResultSettings.ProcessPriority = ProcessPriorityClass.Normal;
        }

        // --- ヘルパーメソッド群 ---
        private void SetRadioFromResolution(ConflictResolution res, RadioButton rNew, RadioButton rOld, RadioButton rRename, RadioButton rSkip)
        {
            if (res == ConflictResolution.KeepNewer) rNew.IsChecked = true;
            else if (res == ConflictResolution.KeepOlder) rOld.IsChecked = true;
            else if (res == ConflictResolution.Skip) rSkip.IsChecked = true;
            else rRename.IsChecked = true;
        }

        private ConflictResolution GetResolutionFromRadio(RadioButton rNew, RadioButton rOld, RadioButton rRename, RadioButton rSkip)
        {
            if (rNew.IsChecked == true) return ConflictResolution.KeepNewer;
            if (rOld.IsChecked == true) return ConflictResolution.KeepOlder;
            if (rSkip.IsChecked == true) return ConflictResolution.Skip;
            return ConflictResolution.Rename;
        }

        // --- UI連動イベント ---
        private void Periodic_Changed(object sender, RoutedEventArgs e)
        {
            if (txtPeriodicMinutes != null) txtPeriodicMinutes.IsEnabled = rbPeriodicOn.IsChecked == true;
        }

        private void HistoryLimit_Changed(object sender, RoutedEventArgs e)
        {
            if (txtHistoryLimit != null) txtHistoryLimit.IsEnabled = chkLimitHistory.IsChecked == true;
        }

        // --- ボタン ---
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // 数値の入力チェック (不正な値が黙って無視・全消去につながらないようにする)
            if (rbPeriodicOn.IsChecked == true && !(int.TryParse(txtPeriodicMinutes.Text, out int minutes) && minutes >= 1))
            {
                MessageBox.Show(this, Loc.T("msg.periodic_invalid"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPeriodicMinutes.Focus();
                return;
            }
            if (chkLimitHistory.IsChecked == true && !(int.TryParse(txtHistoryLimit.Text, out int hundreds) && hundreds >= 1))
            {
                MessageBox.Show(this, Loc.T("msg.history_limit_invalid"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                txtHistoryLimit.Focus();
                return;
            }

            SaveDataFromUI();
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnImportLegacy_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("dialog.import_csv_title"),
                Filter = Loc.T("dialog.import_csv_filter")
            };

            if (dialog.ShowDialog() == true)
            {
                // インポートの実処理（プリセット管理等）は呼び出し元のメイン画面に任せる
                if (this.Owner is MainWindow mainWindow)
                {
                    mainWindow.ImportLegacyCsv(dialog.FileName);
                }
            }
        }

    }
}