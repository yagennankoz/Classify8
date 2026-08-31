using System;

namespace Classify8.Core
{
    public class SortHistory
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string RuleName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string NewFileName { get; set; } = ""; // 変更していなければ空欄
        public string SourceDir { get; set; } = "";
        public string DestDir { get; set; } = "";

        // ステータス: "移動", "コピー", "削除", "エラー" など
        public string Status { get; set; } = "";
        public string ErrorMessage { get; set; } = ""; // エラー時の詳細
    }
}