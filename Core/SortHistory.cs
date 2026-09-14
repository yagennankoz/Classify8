using System;

namespace Classify8.Core
{
    public class SortHistory
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string RuleId { get; set; } = ""; // 🚨 これを追加
        public string RuleName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string NewFileName { get; set; } = "";
        public string SourceDir { get; set; } = "";
        public string DestDir { get; set; } = "";
        public string Status { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }
}