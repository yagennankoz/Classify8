using System;
using System.Diagnostics;

namespace Classify8.Core
{
    // 同名ファイルの処理方法
    public enum ConflictResolution
    {
        KeepNewer, // 新しい日付のファイルを残す
        KeepOlder, // 古い日付のファイルを残す
        Rename,    // ファイル名を変えて振り分ける
        Skip       // 振り分けない
    }

    public class AppSettings
    {
        // --- 1. 同名ファイルの処理 ---
        public ConflictResolution SameSizeResolution { get; set; } = ConflictResolution.Rename;
        public ConflictResolution DiffSizeResolution { get; set; } = ConflictResolution.Rename;

        // --- 2. 振り分けないファイル ---
        public string GlobalNgKeywords { get; set; } = "";

        // --- 3. 実行条件 ---
        public bool EnablePeriodicExecution { get; set; } = false;
        public int PeriodicIntervalMinutes { get; set; } = 60; // デフォルト60分

        public bool MoveToRecycleBin { get; set; } = true;
        public bool AutoCreateDestFolder { get; set; } = true;
        public bool StartMinimized { get; set; } = false;
        public bool SilentReadOnly { get; set; } = true;
        
        public bool LimitHistoryCount { get; set; } = true;
        public int HistoryLimitHundreds { get; set; } = 10; // デフォルト10(百件) = 1000件

        public ProcessPriorityClass ProcessPriority { get; set; } = ProcessPriorityClass.Normal;


        // 複製用メソッド
        public AppSettings Clone()
        {
            return (AppSettings)this.MemberwiseClone();
        }
    }
}