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
        // --- 0. 表示言語 ("auto" = OSの言語に合わせる。または言語コード: ja, en など) ---
        public string Language { get; set; } = "auto";

        // --- 1. 同名ファイルの処理 ---
        public ConflictResolution SameSizeResolution { get; set; } = ConflictResolution.Rename;
        public ConflictResolution DiffSizeResolution { get; set; } = ConflictResolution.Rename;

        // サイズが同じでタイムスタンプだけ異なる場合に、内容(バイト列)を比較して同一ファイルか判定する
        public bool CompareContentWhenTimestampDiffers { get; set; } = true;

        // 内容比較を行う上限サイズ(MB)。0 = 無制限。上限を超えるファイルは内容比較せず、サイズと更新日時だけで判定する
        public int ContentCompareMaxSizeMB { get; set; } = 0;

        // --- 2. 振り分けないファイル ---
        public string GlobalNgKeywords { get; set; } = "";

        // --- 3. 実行条件 ---
        public bool EnablePeriodicExecution { get; set; } = false;
        public int PeriodicIntervalMinutes { get; set; } = 60; // デフォルト60分

        public bool MoveToRecycleBin { get; set; } = true;
        public bool AutoCreateDestFolder { get; set; } = true;
        public bool StartMinimized { get; set; } = false;
        public bool SilentReadOnly { get; set; } = true;

        // ルールのダブルクリックで即実行する前に、確認ダイアログを表示する
        public bool ConfirmBeforeDoubleClickRun { get; set; } = false;
        
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