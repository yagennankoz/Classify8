using System;
using System.Collections.Generic;
using System.Linq;

namespace Classify8.Core
{
    /// <summary>
    /// 振り分け履歴の「状態」。
    /// 履歴ファイルには、言語に依存しないコード(下の定数)を保存し、画面に出すときに表示言語の文字列へ変換する。
    /// 以前のバージョンが保存した日本語の状態("移動" など)は、読み込み時にコードへ変換する。
    /// </summary>
    public static class HistoryStatus
    {
        public const string Moved = "Moved";
        public const string Copied = "Copied";
        public const string Error = "Error";

        public const string DeletedExactMatch = "DeletedExactMatch";
        public const string DeletedContentMatch = "DeletedContentMatch";
        public const string DeletedNewerExists = "DeletedNewerExists";
        public const string DeletedOlderExists = "DeletedOlderExists";

        public const string SkippedExactMatch = "SkippedExactMatch";
        public const string SkippedContentMatch = "SkippedContentMatch";
        public const string SkippedSameName = "SkippedSameName";
        public const string SkippedNewerExists = "SkippedNewerExists";
        public const string SkippedOlderExists = "SkippedOlderExists";

        public const string WarningSamePath = "WarningSamePath";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Moved, Copied, Error,
            DeletedExactMatch, DeletedContentMatch, DeletedNewerExists, DeletedOlderExists,
            SkippedExactMatch, SkippedContentMatch, SkippedSameName, SkippedNewerExists, SkippedOlderExists,
            WarningSamePath
        };

        // 以前のバージョンが履歴ファイルに保存していた日本語の状態 → コード
        private static readonly Dictionary<string, string> Legacy = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["移動"] = Moved,
            ["コピー"] = Copied,
            ["エラー"] = Error,
            ["削除 (完全一致)"] = DeletedExactMatch,
            ["削除 (内容一致)"] = DeletedContentMatch,
            ["削除 (新しいファイル有)"] = DeletedNewerExists,
            ["削除 (古いファイル有)"] = DeletedOlderExists,
            ["スキップ (完全一致)"] = SkippedExactMatch,
            ["スキップ (内容一致)"] = SkippedContentMatch,
            ["スキップ (同名あり)"] = SkippedSameName,
            ["スキップ (新しいファイル有)"] = SkippedNewerExists,
            ["スキップ (古いファイル有)"] = SkippedOlderExists,
            ["警告 (同一パス指定)"] = WarningSamePath,
        };

        /// <summary>日本語で保存された古い状態をコードに変換する。既にコードのものや、未知のものはそのまま返す。</summary>
        public static string Normalize(string status)
        {
            if (string.IsNullOrEmpty(status)) return status;
            return Legacy.TryGetValue(status.Trim(), out var code) ? code : status;
        }

        public static bool IsMoved(string status) => Normalize(status) == Moved;
        public static bool IsCopied(string status) => Normalize(status) == Copied;
        public static bool IsError(string status) => Normalize(status) == Error;
        public static bool IsDeleted(string status) => Normalize(status)?.StartsWith("Deleted", StringComparison.Ordinal) == true;
        public static bool IsSkipped(string status) => Normalize(status)?.StartsWith("Skipped", StringComparison.Ordinal) == true;
        public static bool IsWarning(string status) => Normalize(status)?.StartsWith("Warning", StringComparison.Ordinal) == true;

        /// <summary>画面に表示する文字列 (現在の表示言語)。未知の状態は、そのまま返す。</summary>
        public static string ToDisplay(string status)
        {
            string code = Normalize(status);
            if (code != null && All.Contains(code)) return Loc.T("status." + code);
            return status ?? "";
        }
    }
}
