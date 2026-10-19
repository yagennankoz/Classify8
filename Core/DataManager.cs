using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Classify8.Core
{
    public static class DataManager
    {
        // 保存先は、実行ファイルのフォルダに書き込めればそこ、書き込めなければ %AppData%\Classify8
        private static string SettingsFile => AppPaths.GetFile("settings.json");
        private static string PresetsFile => AppPaths.GetFile("presets.json");

        private static string RulesFile => AppPaths.GetFile("rules.csv");

        /// <summary>
        /// 読み込み/保存エラーの通知先。Core 層が UI(MessageBox)に依存しないよう、呼び出し側(画面)が設定する。
        /// バックグラウンドスレッドから呼ばれることがある。
        /// </summary>
        public static Action<string> ErrorReporter { get; set; }

        private static void ReportError(string message, Exception ex)
        {
            AppLog.Error(message, ex);
            try { ErrorReporter?.Invoke(message + "\n" + ex.Message); } catch { }
        }

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
                catch (Exception ex) { ReportError(Loc.T("err.settings_load"), ex); }
            }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try { AtomicFile.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions)); }
            catch (Exception ex) { ReportError(Loc.T("err.settings_save"), ex); }
        }

        public static List<Preset> LoadPresets()
        {
            if (File.Exists(PresetsFile))
            {
                try { return JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(PresetsFile), JsonOptions) ?? new List<Preset>(); }
                catch (Exception ex) { ReportError(Loc.T("err.presets_load"), ex); }
            }
            return new List<Preset>();
        }

        public static void SavePresets(IEnumerable<Preset> presets)
        {
            try { AtomicFile.WriteAllText(PresetsFile, JsonSerializer.Serialize(presets, JsonOptions)); }
            catch (Exception ex) { ReportError(Loc.T("err.presets_save"), ex); }
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
            "TimeAfter_Enabled", "TimeAfter_Value", "TimeAfter_Unit",
            "Id"
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
                    if (cols.Length < 32) continue;

                    var rule = new SortRule();
                    rule.RuleName = cols[0];
                    rule.IsEnabled = bool.TryParse(cols[1], out bool b1) ? b1 : true;
                    rule.SearchCondition = cols[2];
                    rule.SourceMode = cols[3];
                    rule.SourcePresetId = cols[4];
                    rule.SourceCustomPath = cols[5];
                    rule.SearchSubDirectories = bool.TryParse(cols[6], out bool b2) ? b2 : false;
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
                    rule.SizeMin_Value = TryParseDouble(cols[17], out double d1) ? d1 : 0;
                    rule.SizeMin_Unit = Enum.TryParse(cols[18], out SizeUnit su1) ? su1 : SizeUnit.KB;

                    rule.SizeMax_Enabled = bool.TryParse(cols[19], out bool b9) ? b9 : false;
                    rule.SizeMax_Value = TryParseDouble(cols[20], out double d2) ? d2 : 0;
                    rule.SizeMax_Unit = Enum.TryParse(cols[21], out SizeUnit su2) ? su2 : SizeUnit.KB;

                    rule.DateBefore_Enabled = bool.TryParse(cols[22], out bool b10) ? b10 : false;
                    rule.DateBefore_Date = TryParseDate(cols[23], out DateTime dt1) ? dt1 : (DateTime?)null;

                    rule.DateAfter_Enabled = bool.TryParse(cols[24], out bool b11) ? b11 : false;
                    rule.DateAfter_Date = TryParseDate(cols[25], out DateTime dt2) ? dt2 : (DateTime?)null;

                    rule.TimeBefore_Enabled = bool.TryParse(cols[26], out bool b12) ? b12 : false;
                    rule.TimeBefore_Value = int.TryParse(cols[27], out int i1) ? i1 : 0;
                    rule.TimeBefore_Unit = Enum.TryParse(cols[28], out TimeUnit tu1) ? tu1 : TimeUnit.Day;

                    rule.TimeAfter_Enabled = bool.TryParse(cols[29], out bool b13) ? b13 : false;
                    rule.TimeAfter_Value = int.TryParse(cols[30], out int i2) ? i2 : 0;
                    rule.TimeAfter_Unit = Enum.TryParse(cols[31], out TimeUnit tu2) ? tu2 : TimeUnit.Day;

                    if (cols.Length > 32 && !string.IsNullOrWhiteSpace(cols[32]))
                    {
                        rule.Id = cols[32];
                    }
                    else
                    {
                        rule.Id = Guid.NewGuid().ToString();
                    }

                    rules.Add(rule);
                }
            }
            catch (Exception ex) { ReportError(Loc.T("err.rules_load"), ex); }

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
                        rule.SizeMin_Value.ToString(CultureInfo.InvariantCulture),
                        rule.SizeMin_Unit.ToString(),

                        rule.SizeMax_Enabled.ToString(),
                        rule.SizeMax_Value.ToString(CultureInfo.InvariantCulture),
                        rule.SizeMax_Unit.ToString(),

                        rule.DateBefore_Enabled.ToString(),
                        rule.DateBefore_Date?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "",
                        rule.DateAfter_Enabled.ToString(),
                        rule.DateAfter_Date?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "",

                        rule.TimeBefore_Enabled.ToString(),
                        rule.TimeBefore_Value.ToString(),
                        rule.TimeBefore_Unit.ToString(),

                        rule.TimeAfter_Enabled.ToString(),
                        rule.TimeAfter_Value.ToString(),
                        rule.TimeAfter_Unit.ToString(),
                        rule.Id
                    };
                    sb.AppendLine(string.Join(",", cols));
                }

                // UTF-8(BOM付き)で保存し、Excelでの文字化けを防ぐ (一時ファイル経由で原子的に書き込む)
                AtomicFile.WriteAllText(RulesFile, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex) { ReportError(Loc.T("err.rules_save"), ex); }
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

        // 実行環境のカルチャ(小数点の記号など)に依存せず読み書きするためのヘルパー
        private static bool TryParseDouble(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(s, out value); // 旧バージョンで現在のカルチャのまま保存されたファイルの救済
        }

        private static bool TryParseDate(string s, out DateTime value)
        {
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
                || DateTime.TryParse(s, out value);
        }

        // --- カンマや改行を含む安全なCSVパース処理 ---
        // 全体を1文字ずつ読む状態機械。引用符内の改行も正しく扱い、引用符が閉じていなくても必ず終了する。
        private static List<string[]> ReadCsvLines(string filePath)
        {
            string text = File.ReadAllText(filePath, Encoding.UTF8); // BOM は自動で処理される

            var result = new List<string[]>();
            var row = new List<string>();
            var token = new StringBuilder();
            bool inQuotes = false;
            bool rowStarted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '\"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\"')
                        {
                            token.Append('\"'); // エスケープされたダブルクォート
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        token.Append(c); // セル内の改行もそのまま保持する
                    }
                    continue;
                }

                if (c == '\"')
                {
                    inQuotes = true;
                    rowStarted = true;
                }
                else if (c == ',')
                {
                    row.Add(token.ToString());
                    token.Clear();
                    rowStarted = true;
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;

                    if (rowStarted || token.Length > 0)
                    {
                        row.Add(token.ToString());
                        result.Add(row.ToArray());
                    }
                    row = new List<string>();
                    token.Clear();
                    rowStarted = false;
                }
                else
                {
                    token.Append(c);
                }
            }

            if (rowStarted || token.Length > 0)
            {
                row.Add(token.ToString());
                result.Add(row.ToArray());
            }

            return result;
        }
    }
}
