using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Classify8.Core
{
    public static class HistoryManager
    {
        private static readonly string HistoryFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public static List<SortHistory> LoadHistories()
        {
            if (File.Exists(HistoryFile))
            {
                try
                {
                    string json = File.ReadAllText(HistoryFile);
                    return JsonSerializer.Deserialize<List<SortHistory>>(json, JsonOptions) ?? new List<SortHistory>();
                }
                catch { }
            }
            return new List<SortHistory>();
        }

        public static void SaveHistories(List<SortHistory> histories, AppSettings settings)
        {
            // オプションに従って件数を制限する
            if (settings.LimitHistoryCount)
            {
                int maxCount = settings.HistoryLimitHundreds * 100;
                if (histories.Count > maxCount)
                {
                    // 新しいもの（末尾に追加されている前提）を残し、古いものを切り捨てる
                    histories = histories.Skip(histories.Count - maxCount).ToList();
                }
            }

            string json = JsonSerializer.Serialize(histories, JsonOptions);
            File.WriteAllText(HistoryFile, json);
        }

        // 実行エンジンから1件ずつ、あるいはリストで追加する用のメソッド
        public static void AddHistories(IEnumerable<SortHistory> newRecords, AppSettings settings)
        {
            var histories = LoadHistories();
            histories.AddRange(newRecords);
            SaveHistories(histories, settings);
        }
    }
}