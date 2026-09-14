using System;
using System.Text.Json.Serialization;

namespace Classify8.Core
{
    public enum TargetType { FileOnly, DirectoryOnly, Both }
    public enum SizeUnit { Byte, KB, MB }
    public enum TimeUnit { Day, Week, Month }

    public class SortRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); // 🚨追加: 一意のID
        public string RuleName { get; set; } = "";
        public bool IsEnabled { get; set; } = true;

        public string SearchCondition { get; set; }
        [JsonIgnore]
        public IConditionNode CompiledCondition { get; set; }

        public string SourceMode { get; set; } = "Custom";
        public string SourcePresetId { get; set; }
        public string SourceCustomPath { get; set; }
        public bool SearchSubDirectories { get; set; } = false;

        public string DestMode { get; set; } = "Custom";
        public string DestPresetId { get; set; }
        public string DestCustomPath { get; set; }
        public bool IsMoveAction { get; set; } = true;

        public bool IgnoreCase { get; set; } = true;
        public bool IgnoreWidth { get; set; } = true;
        public bool IgnoreKana { get; set; } = true;
        public bool DoNotSaveHistory { get; set; } = false;
        public TargetType TargetType { get; set; } = TargetType.FileOnly;

        public bool SizeMin_Enabled { get; set; }
        public double SizeMin_Value { get; set; }
        public SizeUnit SizeMin_Unit { get; set; } = SizeUnit.KB;

        public bool SizeMax_Enabled { get; set; }
        public double SizeMax_Value { get; set; }
        public SizeUnit SizeMax_Unit { get; set; } = SizeUnit.KB;

        public bool DateBefore_Enabled { get; set; }
        public DateTime? DateBefore_Date { get; set; }

        public bool DateAfter_Enabled { get; set; }
        public DateTime? DateAfter_Date { get; set; }

        public bool TimeBefore_Enabled { get; set; }
        public int TimeBefore_Value { get; set; }
        public TimeUnit TimeBefore_Unit { get; set; } = TimeUnit.Day;

        public bool TimeAfter_Enabled { get; set; }
        public int TimeAfter_Value { get; set; }
        public TimeUnit TimeAfter_Unit { get; set; } = TimeUnit.Day;

        public SortRule Clone()
        {
            var clone = (SortRule)this.MemberwiseClone();
            clone.Id = Guid.NewGuid().ToString(); // 🚨追加: コピー時はIDを新しく発行する
            return clone;
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

            if (!CompiledCondition.Evaluate(name)) return false;

            if (!isDirectory)
            {
                long size = new System.IO.FileInfo(itemPath).Length;

                if (SizeMin_Enabled && size <= GetBytes(SizeMin_Value, SizeMin_Unit)) return false;
                if (SizeMax_Enabled && size >= GetBytes(SizeMax_Value, SizeMax_Unit)) return false;
            }

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