using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Windows;

namespace Classify8.Core
{
    public static class DataManager
    {
        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly string PresetsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets.json");
        
        private static readonly string RulesFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules.csv");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Converters = { new JsonStringEnumConverter() }
        };

        // ==========================================
        // 全体設定とプリセットの入出力 (JSONのまま)
        // ==========================================
        public static AppSettings LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new AppSettings(); }
                catch (Exception ex) { MessageBox.Show($"設定の読み込みに失敗しました。\n{ex.Message}", "エラー"); }
            }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try { File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions)); }
            catch (Exception ex) { MessageBox.Show($"設定の保存に失敗しました。\n{ex.Message}", "エラー"); }
        }

        public static List<Preset> LoadPresets()
        {
            if (File.Exists(PresetsFile))
            {
                try { return JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(PresetsFile), JsonOptions) ?? new List<Preset>(); }
                catch (Exception ex) { MessageBox.Show($"プリセットの読み込みに失敗しました。\n{ex.Message}", "エラー"); }
            }
            return new List<Preset>();
        }

        public static void SavePresets(IEnumerable<Preset> presets)
        {
            try { File.WriteAllText(PresetsFile, JsonSerializer.Serialize(presets, JsonOptions)); }
            catch (Exception ex) { MessageBox.Show($"プリセットの保存に失敗しました。\n{ex.Message}", "エラー"); }
        }

        // ==========================================
        // ルール (SortRules) の入出力 (CSV)
        // ==========================================
        
        // CSVのヘッダー定義
        private static readonly string[] CsvHeaders = new[]
        {
            "RuleName", "IsEnabled", "SearchCondition", 
            "SourceMode", "SourcePresetId", "SourceCustomPath", "SearchSubDirectories",
            "DestMode", "DestPresetId", "DestCustomPath", "IsMoveAction",
            "IgnoreCase", "IgnoreWidth", "IgnoreKana", "DoNotSaveHistory", "TargetType",
            "SizeMin_Enabled", "SizeMin_Value", "SizeMin_Unit",
            "SizeMax_Enabled", "SizeMax_Value", "SizeMax_Unit",
            "DateBefore_Enabled", "DateBefore_Date", "DateAfter_Enabled", "DateAfter_Date",
            "TimeBefore_Enabled", "TimeBefore_Value", "TimeBefore_Unit",
            "TimeAfter_Enabled", "TimeAfter_Value", "TimeAfter_Unit"
        };

        public static List<SortRule> LoadRules()
        {
            var rules = new List<SortRule>();
            if (!File.Exists(RulesFile)) return rules;

            try
            {
                var lines = ReadCsvLines(RulesFile);
                if (lines.Count <= 1) return rules; // ヘッダーのみ、または空

                for (int i = 1; i < lines.Count; i++) // 1行目(ヘッダー)をスキップ
                {
                    var cols = lines[i];
                    if (cols.Length < CsvHeaders.Length) continue;

                    var rule = new SortRule();
                    rule.RuleName = cols[0];
                    rule.IsEnabled = bool.TryParse(cols[1], out bool b1) ? b1 : true;
                    rule.SearchCondition = cols[2];
                    rule.SourceMode = cols[3];
                    rule.SourcePresetId = cols[4];
                    rule.SourceCustomPath = cols[5];
                    rule.SearchSubDirectories = bool.TryParse(cols[6], out bool b2) ? b2 : true;
                    rule.DestMode = cols[7];
                    rule.DestPresetId = cols[8];
                    rule.DestCustomPath = cols[9];
                    rule.IsMoveAction = bool.TryParse(cols[10], out bool b3) ? b3 : true;

                    rule.IgnoreCase = bool.TryParse(cols[11], out bool b4) ? b4 : true;
                    rule.IgnoreWidth = bool.TryParse(cols[12], out bool b5) ? b5 : true;
                    rule.IgnoreKana = bool.TryParse(cols[13], out bool b6) ? b6 : true;
                    rule.DoNotSaveHistory = bool.TryParse(cols[14], out bool b7) ? b7 : false;
                    rule.TargetType = Enum.TryParse(cols[15], out TargetType t) ? t : TargetType.FileOnly;

                    rule.SizeMin_Enabled = bool.TryParse(cols[16], out bool b8) ? b8 : false;
                    rule.SizeMin_Value = double.TryParse(cols[17], out double d1) ? d1 : 0;
                    rule.SizeMin_Unit = Enum.TryParse(cols[18], out SizeUnit su1) ? su1 : SizeUnit.KB;

                    rule.SizeMax_Enabled = bool.TryParse(cols[19], out bool b9) ? b9 : false;
                    rule.SizeMax_Value = double.TryParse(cols[20], out double d2) ? d2 : 0;
                    rule.SizeMax_Unit = Enum.TryParse(cols[21], out SizeUnit su2) ? su2 : SizeUnit.KB;

                    rule.DateBefore_Enabled = bool.TryParse(cols[22], out bool b10) ? b10 : false;
                    rule.DateBefore_Date = DateTime.TryParse(cols[23], out DateTime dt1) ? dt1 : (DateTime?)null;

                    rule.DateAfter_Enabled = bool.TryParse(cols[24], out bool b11) ? b11 : false;
                    rule.DateAfter_Date = DateTime.TryParse(cols[25], out DateTime dt2) ? dt2 : (DateTime?)null;

                    rule.TimeBefore_Enabled = bool.TryParse(cols[26], out bool b12) ? b12 : false;
                    rule.TimeBefore_Value = int.TryParse(cols[27], out int i1) ? i1 : 0;
                    rule.TimeBefore_Unit = Enum.TryParse(cols[28], out TimeUnit tu1) ? tu1 : TimeUnit.Day;

                    rule.TimeAfter_Enabled = bool.TryParse(cols[29], out bool b13) ? b13 : false;
                    rule.TimeAfter_Value = int.TryParse(cols[30], out int i2) ? i2 : 0;
                    rule.TimeAfter_Unit = Enum.TryParse(cols[31], out TimeUnit tu2) ? tu2 : TimeUnit.Day;

                    rules.Add(rule);
                }
            }
            catch (Exception ex) { MessageBox.Show($"ルールの読み込みに失敗しました。\n{ex.Message}", "エラー"); }

            return rules;
        }

        public static void SaveRules(IEnumerable<SortRule> rules)
        {
            try
            {
                var sb = new StringBuilder();
                
                // ヘッダー書き込み
                sb.AppendLine(string.Join(",", CsvHeaders));

                // データ書き込み
                foreach (var rule in rules)
                {
                    var cols = new[]
                    {
                        EscapeCsv(rule.RuleName),
                        rule.IsEnabled.ToString(),
                        EscapeCsv(rule.SearchCondition),
                        EscapeCsv(rule.SourceMode),
                        EscapeCsv(rule.SourcePresetId),
                        EscapeCsv(rule.SourceCustomPath),
                        rule.SearchSubDirectories.ToString(),
                        EscapeCsv(rule.DestMode),
                        EscapeCsv(rule.DestPresetId),
                        EscapeCsv(rule.DestCustomPath),
                        rule.IsMoveAction.ToString(),
                        
                        rule.IgnoreCase.ToString(),
                        rule.IgnoreWidth.ToString(),
                        rule.IgnoreKana.ToString(),
                        rule.DoNotSaveHistory.ToString(),
                        rule.TargetType.ToString(),

                        rule.SizeMin_Enabled.ToString(),
                        rule.SizeMin_Value.ToString(),
                        rule.SizeMin_Unit.ToString(),

                        rule.SizeMax_Enabled.ToString(),
                        rule.SizeMax_Value.ToString(),
                        rule.SizeMax_Unit.ToString(),

                        rule.DateBefore_Enabled.ToString(),
                        rule.DateBefore_Date?.ToString("yyyy/MM/dd") ?? "",
                        rule.DateAfter_Enabled.ToString(),
                        rule.DateAfter_Date?.ToString("yyyy/MM/dd") ?? "",

                        rule.TimeBefore_Enabled.ToString(),
                        rule.TimeBefore_Value.ToString(),
                        rule.TimeBefore_Unit.ToString(),

                        rule.TimeAfter_Enabled.ToString(),
                        rule.TimeAfter_Value.ToString(),
                        rule.TimeAfter_Unit.ToString()
                    };
                    sb.AppendLine(string.Join(",", cols));
                }

                // UTF-8(BOM付き)で保存し、Excelでの文字化けを防ぐ
                File.WriteAllText(RulesFile, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex) { MessageBox.Show($"ルールの保存に失敗しました。\n{ex.Message}", "エラー"); }
        }

        // --- CSV用の安全な文字エスケープ処理 ---
        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\n") || text.Contains("\r"))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }

        // --- カンマや改行を含む安全なCSVパース処理 ---
        private static List<string[]> ReadCsvLines(string filePath)
        {
            var result = new List<string[]>();
            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var row = new List<string>();
                    bool inQuotes = false;
                    var token = new StringBuilder();

                    for (int i = 0; i < line.Length; i++)
                    {
                        char c = line[i];
                        if (c == '\"')
                        {
                            if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                            {
                                token.Append('\"'); // エスケープされたダブルクォート
                                i++;
                            }
                            else
                            {
                                inQuotes = !inQuotes;
                            }
                        }
                        else if (c == ',' && !inQuotes)
                        {
                            row.Add(token.ToString());
                            token.Clear();
                        }
                        else
                        {
                            token.Append(c);
                        }

                        // セル内に改行が含まれている場合の対応
                        if (i == line.Length - 1 && inQuotes)
                        {
                            token.Append("\n");
                            line += "\n" + reader.ReadLine();
                        }
                    }
                    row.Add(token.ToString());
                    result.Add(row.ToArray());
                }
            }
            return result;
        }
    }
}