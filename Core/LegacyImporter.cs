using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Classify8.Core
{
    public static class LegacyImporter
    {
        public static List<SortRule> ImportFromClassyNyCsv(string filePath, IList<Preset> presets)
        {
            var rules = new List<SortRule>();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var lines = File.ReadAllLines(filePath, Encoding.GetEncoding("Shift_JIS"));

            for (int i = 3; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line);

                if (cols.Length < 28) continue;

                var rule = new SortRule();

                rule.IsEnabled = cols[0] == "1";
                rule.RuleName = cols[1];

                if (cols[2] == "0")
                {
                    var targetPreset = presets.FirstOrDefault(p => p.AliasName == "ClassiNy共通振り分け元");
                    if (targetPreset == null)
                    {
                        targetPreset = new Preset { AliasName = "ClassiNy共通振り分け元", DirectoryPath = @"C:\" };
                        presets.Add(targetPreset);
                    }
                    rule.SourceMode = "Preset";
                    rule.SourcePresetId = targetPreset.Id;
                    rule.SourceCustomPath = "";
                }
                else
                {
                    rule.SourceMode = "Custom";
                    rule.SourceCustomPath = cols[3];
                }

                rule.SearchSubDirectories = cols[4] == "1";

                bool isIncludeOr = cols[5] == "1";
                bool isExcludeOr = cols[6] == "1";
                rule.SearchCondition = ConvertLegacyKeyword(cols[7], isIncludeOr, isExcludeOr);

                rule.DestMode = "Custom";
                rule.DestCustomPath = cols[8];
                rule.IsMoveAction = cols[9] == "0";

                rule.IgnoreCase = cols[11] == "1";
                rule.IgnoreWidth = cols[12] == "1";
                rule.IgnoreKana = cols[13] == "1";
                rule.DoNotSaveHistory = cols[15] == "1";
                rule.TargetType = cols[17] == "1" ? TargetType.Both : TargetType.FileOnly;

                ApplyLegacySizeCondition(rule, cols[18], cols[19], cols[20], cols[21]);
                ApplyLegacySizeCondition(rule, cols[22], cols[23], cols[24], cols[25]);

                rules.Add(rule);
            }

            return rules;
        }

        // ==========================================
        // CSVのダブルクォートエスケープを正確に解除する
        // ==========================================
        private static string[] ParseCsvLine(string line)
        {
            var row = new List<string>();
            bool inQuotes = false;
            var token = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    // 連続するダブルクォート "" は、1つの " として扱う
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        token.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes; // クォートの開始・終了を切り替え
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // クォートの外側にあるカンマは区切り文字
                    row.Add(token.ToString());
                    token.Clear();
                }
                else
                {
                    // 通常の文字、またはクォートの中のカンマ
                    token.Append(c);
                }
            }
            row.Add(token.ToString());
            return row.ToArray();
        }

        // --- ヘルパーメソッド: キーワードの変換 ---
        private static string ConvertLegacyKeyword(string rawKeyword, bool isIncludeOr, bool isExcludeOr)
        {
            if (string.IsNullOrWhiteSpace(rawKeyword)) return "*";

            var includes = new List<string>();
            var excludes = new List<string>();

            // ダブルクォートを考慮して分割 (マイナスやスラッシュが直前にある場合も1トークンとする)
            var regex = new System.Text.RegularExpressions.Regex(@"[-/]?\""[^\""]*\""|[-/]?[^\s\""]+");
            foreach (System.Text.RegularExpressions.Match match in regex.Matches(rawKeyword.Replace("　", " ")))
            {
                string token = match.Value;

                if (token.StartsWith("-") || token.StartsWith("/"))
                {
                    excludes.Add(token.Substring(1));
                }
                else
                {
                    includes.Add(token);
                }
            }

            string incStr = "";
            if (includes.Count == 1)
            {
                incStr = includes[0];
            }
            else if (includes.Count > 1)
            {
                string op = isIncludeOr ? " /| " : " /& ";
                incStr = string.Join(op, includes);
                if (excludes.Count > 0 && isIncludeOr) incStr = "/( " + incStr + " /)";
            }

            string excStr = "";
            if (excludes.Count == 1)
            {
                excStr = "/! " + excludes[0];
            }
            else if (excludes.Count > 1)
            {
                string op = isExcludeOr ? " /| " : " /& ";
                excStr = string.Join(op, excludes.Select(x => "/! " + x));
                if (includes.Count > 0 && isExcludeOr) excStr = "/( " + excStr + " /)";
            }

            if (includes.Count > 0 && excludes.Count > 0)
                return incStr + " /& " + excStr;
            else if (includes.Count > 0)
                return incStr;
            else if (excludes.Count > 0)
                return "* /& " + excStr;
            else
                return "*";
        }

        private static void ApplyLegacySizeCondition(SortRule rule, string enabledFlag, string sizeVal, string unitStr, string conditionType)
        {
            if (enabledFlag != "1") return;
            if (!double.TryParse(sizeVal, out double val)) return;

            if (!int.TryParse(unitStr, out int unitInt)) unitInt = 1;
            SizeUnit unit = (SizeUnit)unitInt;

            if (conditionType == "1")
            {
                rule.SizeMax_Enabled = true;
                rule.SizeMax_Value = val;
                rule.SizeMax_Unit = unit;
            }
            else if (conditionType == "2")
            {
                rule.SizeMin_Enabled = true;
                rule.SizeMin_Value = val;
                rule.SizeMin_Unit = unit;
            }
        }
    }
}