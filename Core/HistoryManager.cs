using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;

namespace Classify8.Core
{
    public static class HistoryManager
    {
        private static string HistoryFile => AppPaths.GetFile("history.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        /// <summary>
        /// 履歴の閲覧用。読めない場合は空のリストを返す。
        /// </summary>
        public static List<SortHistory> LoadHistories()
        {
            try
            {
                return LoadForAppend();
            }
            catch
            {
                return new List<SortHistory>();
            }
        }

        // 追記用の読み込み。
        // ・JSON が壊れている場合: 壊れたファイルを .corrupt に退避して、空から始める
        // ・ファイルが他のアプリに掴まれている等で読めない場合: 例外を投げる
        //   (空のリストを返すと、次の保存で既存の履歴を上書きして失ってしまうため)
        private static List<SortHistory> LoadForAppend()
        {
            if (!File.Exists(HistoryFile)) return new List<SortHistory>();

            string json = ReadAllTextWithRetry(HistoryFile);
            if (string.IsNullOrWhiteSpace(json)) return new List<SortHistory>();

            try
            {
                var histories = JsonSerializer.Deserialize<List<SortHistory>>(json, JsonOptions) ?? new List<SortHistory>();

                // 以前のバージョンが日本語で保存した「状態」を、言語に依存しないコードに変換する
                foreach (var h in histories) h.Status = HistoryStatus.Normalize(h.Status);
                return histories;
            }
            catch (JsonException ex)
            {
                try { File.Copy(HistoryFile, HistoryFile + ".corrupt", true); } catch { }
                AppLog.Error("The history file (history.json) was corrupted. It was moved to history.json.corrupt and a new history was started.", ex);
                return new List<SortHistory>();
            }
        }

        private static string ReadAllTextWithRetry(string path)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(200 * attempt);
                }
            }
        }

        public static void SaveHistories(List<SortHistory> histories, AppSettings settings)
        {
            // オプションに従って件数を制限する
            if (settings.LimitHistoryCount)
            {
                // 設定値が 0 や負数でも、履歴が全消去されないように 1 以上に補正する
                int maxCount = Math.Max(1, settings.HistoryLimitHundreds) * 100;
                if (histories.Count > maxCount)
                {
                    // 新しいもの（末尾に追加されている前提）を残し、古いものを切り捨てる
                    histories = histories.Skip(histories.Count - maxCount).ToList();
                }
            }

            string json = JsonSerializer.Serialize(histories, JsonOptions);
            AtomicFile.WriteAllText(HistoryFile, json);
        }

        // 実行エンジンから1件ずつ、あるいはリストで追加する用のメソッド
        // 失敗した場合は例外を投げるので、呼び出し側で処理すること
        public static void AddHistories(IEnumerable<SortHistory> newRecords, AppSettings settings)
        {
            var histories = LoadForAppend();
            // 並列処理で順不同に集まった記録を、時系列に並べてから追加する
            histories.AddRange(newRecords.OrderBy(h => h.Timestamp));
            SaveHistories(histories, settings);
        }
    }
}
