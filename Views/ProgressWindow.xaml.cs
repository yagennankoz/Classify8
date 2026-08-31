using System;
using System.Threading;
using System.Windows;

namespace Classify8.Views
{
    public partial class ProgressWindow : Window
    {
        private readonly CancellationTokenSource _cts;
        private bool _isCompleted = false;

        public ProgressWindow(CancellationTokenSource cts)
        {
            InitializeComponent();
            _cts = cts;
        }

        // 別スレッドから進行状況を受け取って画面を更新する
        public void UpdateState(string ruleName, string fileName, int processedCount, int errorCount)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => UpdateState(ruleName, fileName, processedCount, errorCount));
                return;
            }

            txtRuleName.Text = ruleName;
            txtFileName.Text = fileName;
            txtCounts.Text = $"処理完了: {processedCount} 件  /  エラー: {errorCount} 件";
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            btnCancel.IsEnabled = false;
            btnCancel.Content = "キャンセルしています...";
            _cts.Cancel(); // 処理エンジンに中止信号を送る
        }

        public void CompleteAndClose()
        {
            _isCompleted = true;
            Dispatcher.InvokeAsync(() => Close());
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 完了前にウィンドウの「×」ボタンで閉じられようとした場合もキャンセル扱いにする
            if (!_isCompleted && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
    }
}