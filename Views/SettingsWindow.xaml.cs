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
            // 同名ファイル
            SetRadioFromResolution(ResultSettings.SameSizeResolution, rbSameKeepNewer, rbSameKeepOlder, rbSameRename, rbSameSkip);
            SetRadioFromResolution(ResultSettings.DiffSizeResolution, rbDiffKeepNewer, rbDiffKeepOlder, rbDiffRename, rbDiffSkip);

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

            chkLimitHistory.IsChecked = ResultSettings.LimitHistoryCount;
            txtHistoryLimit.Text = ResultSettings.HistoryLimitHundreds.ToString();

            if (ResultSettings.ProcessPriority == ProcessPriorityClass.High) rbPriorityHigh.IsChecked = true;
            else if (ResultSettings.ProcessPriority == ProcessPriorityClass.BelowNormal) rbPriorityLow.IsChecked = true;
            else if (ResultSettings.ProcessPriority == ProcessPriorityClass.Idle) rbPriorityIdle.IsChecked = true;
            else rbPriorityNormal.IsChecked = true;
        }

        private void SaveDataFromUI()
        {
            ResultSettings.SameSizeResolution = GetResolutionFromRadio(rbSameKeepNewer, rbSameKeepOlder, rbSameRename, rbSameSkip);
            ResultSettings.DiffSizeResolution = GetResolutionFromRadio(rbDiffKeepNewer, rbDiffKeepOlder, rbDiffRename, rbDiffSkip);

            ResultSettings.GlobalNgKeywords = txtNgKeywords.Text;

            ResultSettings.EnablePeriodicExecution = rbPeriodicOn.IsChecked == true;
            if (int.TryParse(txtPeriodicMinutes.Text, out int minutes)) ResultSettings.PeriodicIntervalMinutes = minutes;

            ResultSettings.MoveToRecycleBin = chkMoveToRecycleBin.IsChecked ?? true;
            ResultSettings.AutoCreateDestFolder = chkAutoCreateDest.IsChecked ?? true;
            ResultSettings.StartMinimized = chkStartMinimized.IsChecked ?? false;
            ResultSettings.SilentReadOnly = chkSilentReadOnly.IsChecked ?? true;

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
                Title = "ClassyNyの振り分け条件CSVファイルを選択してください",
                Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*"
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