using System;
using System.Text.Json.Serialization;

namespace Classify8.Core
{
    public class SortHistory
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string RuleId { get; set; } = "";
        public string RuleName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string NewFileName { get; set; } = "";
        public string SourceDir { get; set; } = "";
        public string DestDir { get; set; } = "";
        // 言語に依存しない状態コード (HistoryStatus の定数)。表示には StatusDisplay を使う
        public string Status { get; set; } = "";

        // 画面に表示する状態 (現在の表示言語)。ファイルには保存しない
        [JsonIgnore]
        public string StatusDisplay => HistoryStatus.ToDisplay(Status);
        public string ErrorMessage { get; set; } = "";
    }
}