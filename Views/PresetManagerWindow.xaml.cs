using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Classify8.Core;

namespace Classify8.Views
{
    public partial class PresetManagerWindow : Window
    {
        // 画面上で編集するためのリスト
        public ObservableCollection<Preset> Presets { get; set; }
        private bool _isEditing = false;

        private ObservableCollection<SortRuleViewModel> _rulesList;

        // 「個別指定に変換して削除」の予約 (プリセットID → パス)
        // 「保存」を押すまでは実際のルールを書き換えず、「キャンセル」なら破棄する
        private readonly Dictionary<string, string> _pendingConversions = new Dictionary<string, string>();

        public PresetManagerWindow(ObservableCollection<Preset> existingPresets, ObservableCollection<SortRuleViewModel> rulesList)
        {
            InitializeComponent();
            _rulesList = rulesList;

            // キャンセルされた時に元に戻せるよう、渡されたリストをディープコピーして編集する
            Presets = new ObservableCollection<Preset>(
                existingPresets.Select(p => new Preset { Id = p.Id, AliasName = p.AliasName, DirectoryPath = p.DirectoryPath })
            );
            
            lvPresets.ItemsSource = Presets;

            // 初期状態では右側の入力欄を無効化
            SetEditorEnabled(false);
        }

        private void LvPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _isEditing = false; // TextChangedイベントが暴発しないようにフラグを下ろす
            if (lvPresets.SelectedItem is Preset selected)
            {
                txtAliasName.Text = selected.AliasName;
                txtDirectoryPath.Text = selected.DirectoryPath;
                SetEditorEnabled(true);
            }
            else
            {
                txtAliasName.Text = "";
                txtDirectoryPath.Text = "";
                SetEditorEnabled(false);
            }
            _isEditing = true;
        }

        private void SetEditorEnabled(bool isEnabled)
        {
            txtAliasName.IsEnabled = isEnabled;
            txtDirectoryPath.IsEnabled = isEnabled;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var newPreset = new Preset { AliasName = Loc.T("preset.new_name"), DirectoryPath = "" }; // パスは利用者が入力する (C:\\ 等を仮置きしない)
            Presets.Add(newPreset);
            lvPresets.SelectedItem = newPreset; // 追加したものを自動で選択状態にする
            txtAliasName.Focus();
            txtAliasName.SelectAll();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lvPresets.SelectedItem is Preset selected)
            {
                // 1. このプリセットが使用されているかチェック
                var usedRules = _rulesList.Where(r => 
                    (r.Rule.SourceMode == "Preset" && r.Rule.SourcePresetId == selected.Id) ||
                    (r.Rule.DestMode == "Preset" && r.Rule.DestPresetId == selected.Id)).ToList();

                if (usedRules.Any())
                {
                    // 2. 使われている場合は確認ダイアログを表示
                    var confirmDialog = new PresetDeleteConfirmWindow { Owner = this };
                    confirmDialog.ShowDialog();

                    if (confirmDialog.ResultAction == DeleteAction.Cancel)
                    {
                        return; // 削除を中止
                    }
                    else if (confirmDialog.ResultAction == DeleteAction.Convert)
                    {
                        // 個別指定(Custom)への変換は、「保存」を押したときに行う (キャンセルされたら何も変えない)
                        _pendingConversions[selected.Id] = selected.DirectoryPath;
                    }
                    // Force(強制削除)の場合は何もしない（そのまま削除処理へ）
                }

                // 3. 削除実行
                Presets.Remove(selected);
            }
        }

        private void Txt_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 入力欄の文字が変わったら、選択中のデータにリアルタイム反映する
            if (_isEditing && lvPresets.SelectedItem is Preset selected)
            {
                selected.AliasName = txtAliasName.Text;
                selected.DirectoryPath = txtDirectoryPath.Text;
                lvPresets.Items.Refresh(); // 左側のリスト表示を更新
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            // .NET 8 の WPF 標準機能である OpenFolderDialog
            var dialog = new OpenFolderDialog
            {
                Title = Loc.T("dialog.select_folder")
            };
            
            if (dialog.ShowDialog() == true)
            {
                txtDirectoryPath.Text = dialog.FolderName;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // 入力チェック
            foreach (var p in Presets)
            {
                if (string.IsNullOrWhiteSpace(p.AliasName))
                {
                    MessageBox.Show(this, Loc.T("msg.preset_name_empty"), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    lvPresets.SelectedItem = p;
                    return;
                }
                if (string.IsNullOrWhiteSpace(p.DirectoryPath))
                {
                    MessageBox.Show(this, Loc.F("msg.preset_path_empty", p.AliasName), Loc.T("title.input_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    lvPresets.SelectedItem = p;
                    return;
                }
            }

            // 予約しておいた「個別指定への変換」を、ここで初めてルールに反映する
            if (_pendingConversions.Count > 0)
            {
                foreach (var ruleVm in _rulesList)
                {
                    var rule = ruleVm.Rule;
                    if (rule.SourceMode == "Preset" && _pendingConversions.TryGetValue(rule.SourcePresetId ?? "", out string srcPath))
                    {
                        rule.SourceMode = "Custom";
                        rule.SourceCustomPath = srcPath;
                        rule.SourcePresetId = "";
                    }
                    if (rule.DestMode == "Preset" && _pendingConversions.TryGetValue(rule.DestPresetId ?? "", out string destPath))
                    {
                        rule.DestMode = "Custom";
                        rule.DestCustomPath = destPath;
                        rule.DestPresetId = "";
                    }
                }
            }

            DialogResult = true; // 成功として閉じる
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // キャンセルとして閉じる
            Close();
        }
    }
}