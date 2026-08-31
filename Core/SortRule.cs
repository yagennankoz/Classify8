using System;
using System.Text.Json.Serialization;

namespace Classify8.Core
{
    public enum TargetType { FileOnly, DirectoryOnly, Both }
    public enum SizeUnit { Byte, KB, MB }
    public enum TimeUnit { Day, Week, Month }

    public class SortRule
    {
        public string RuleName { get; set; } = ""; 
        public bool IsEnabled { get; set; } = true;         

        // --- 1. 基本検索条件 ---
        public string SearchCondition { get; set; }
        [JsonIgnore]
        public IConditionNode CompiledCondition { get; set; }

        // --- 2. 振り分け元（Source） ---
        public string SourceMode { get; set; } = "Custom"; 
        public string SourcePresetId { get; set; }
        public string SourceCustomPath { get; set; }
        public bool SearchSubDirectories { get; set; } = true; // NEW: サブフォルダも対象とする

        // --- 3. 振り分け先（Destination） ---
        public string DestMode { get; set; } = "Custom"; 
        public string DestPresetId { get; set; }
        public string DestCustomPath { get; set; }
        public bool IsMoveAction { get; set; } = true; // true: 移動, false: コピー

        // ==========================================
        // NEW: オプション (一般タブ)
        // ==========================================
        public bool IgnoreCase { get; set; } = true;
        public bool IgnoreWidth { get; set; } = true;
        public bool IgnoreKana { get; set; } = true;
        public bool DoNotSaveHistory { get; set; } = false;
        public TargetType TargetType { get; set; } = TargetType.FileOnly;

        // ==========================================
        // NEW: オプション (ファイルサイズ限定タブ)
        // ==========================================
        // 「以下のファイルを対象としない」(= 指定サイズより大きいものを対象とする)
        public bool SizeMin_Enabled { get; set; }
        public double SizeMin_Value { get; set; }
        public SizeUnit SizeMin_Unit { get; set; } = SizeUnit.KB;

        // 「以上のファイルを対象としない」(= 指定サイズ未満のものを対象とする)
        public bool SizeMax_Enabled { get; set; }
        public double SizeMax_Value { get; set; }
        public SizeUnit SizeMax_Unit { get; set; } = SizeUnit.KB;

        // ==========================================
        // NEW: オプション (タイムスタンプ限定タブ)
        // ==========================================
        public bool DateBefore_Enabled { get; set; }
        public DateTime? DateBefore_Date { get; set; } // 指定日以前を対象としない

        public bool DateAfter_Enabled { get; set; }
        public DateTime? DateAfter_Date { get; set; }  // 指定日以降を対象としない

        public bool TimeBefore_Enabled { get; set; }
        public int TimeBefore_Value { get; set; }
        public TimeUnit TimeBefore_Unit { get; set; } = TimeUnit.Day; // X日(週/月)以前を対象としない

        public bool TimeAfter_Enabled { get; set; }
        public int TimeAfter_Value { get; set; }
        public TimeUnit TimeAfter_Unit { get; set; } = TimeUnit.Day;  // X日(週/月)以降を対象としない

        public SortRule Clone()
        {
            return (SortRule)this.MemberwiseClone();
        }

        public void CompileCondition()
        {
            var parser = new ConditionParser(ignoreCase: IgnoreCase);
            CompiledCondition = parser.Parse(SearchCondition);
        }

        public bool IsMatch(string itemPath, bool isDirectory)
        {
            if (CompiledCondition == null) CompileCondition();

            string name = System.IO.Path.GetFileName(itemPath);

            // 1. 名前判定 (独自パーサーによるAND/OR/NOT等)
            if (!CompiledCondition.Evaluate(name)) return false;

            // 2. フォルダの場合はサイズの概念がないため、サイズ判定はスキップ
            if (!isDirectory)
            {
                long size = new System.IO.FileInfo(itemPath).Length;
                
                if (SizeMin_Enabled && size <= GetBytes(SizeMin_Value, SizeMin_Unit)) return false; // 指定以下を除外
                if (SizeMax_Enabled && size >= GetBytes(SizeMax_Value, SizeMax_Unit)) return false; // 指定以上を除外
            }

            // 3. タイムスタンプ判定 (更新日時)
            DateTime lastWrite = isDirectory ? new System.IO.DirectoryInfo(itemPath).LastWriteTime : new System.IO.FileInfo(itemPath).LastWriteTime;

            if (DateBefore_Enabled && DateBefore_Date.HasValue && lastWrite <= DateBefore_Date.Value) return false;
            if (DateAfter_Enabled && DateAfter_Date.HasValue && lastWrite >= DateAfter_Date.Value) return false;

            DateTime now = DateTime.Now;
            if (TimeBefore_Enabled && lastWrite <= GetRelativeDate(now, TimeBefore_Value, TimeBefore_Unit)) return false;
            if (TimeAfter_Enabled && lastWrite >= GetRelativeDate(now, TimeAfter_Value, TimeAfter_Unit)) return false;

            return true;
        }

        private long GetBytes(double value, SizeUnit unit)
        {
            if (unit == SizeUnit.KB) return (long)(value * 1024);
            if (unit == SizeUnit.MB) return (long)(value * 1024 * 1024);
            return (long)value;
        }

        private DateTime GetRelativeDate(DateTime baseDate, int value, TimeUnit unit)
        {
            if (unit == TimeUnit.Day) return baseDate.AddDays(-value);
            if (unit == TimeUnit.Week) return baseDate.AddDays(-value * 7);
            if (unit == TimeUnit.Month) return baseDate.AddMonths(-value);
            return baseDate;
        }

    }
}