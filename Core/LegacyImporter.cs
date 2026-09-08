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

                var cols = line.Split(',');
                if (cols.Length < 28) continue;

                var rule = new SortRule();

                // 1) チェック
                rule.IsEnabled = cols[0] == "1";

                // 2) 設定名 (空ならそのまま空にする)
                rule.RuleName = cols[1];

                // 3, 4) 元フォルダ
                if (cols[2] == "0")
                {
                    // "ClassiNy共通振り分け元" プリセットを探す。なければ新規作成
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

                // 5) サブフォルダ
                rule.SearchSubDirectories = cols[4] == "1";

                // 6, 7, 8) 検索キーワードの独自構文への変換
                bool isIncludeOr = cols[5] == "1"; // 0:AND, 1:OR
                bool isExcludeOr = cols[6] == "1"; // 0:AND, 1:OR
                rule.SearchCondition = ConvertLegacyKeyword(cols[7], isIncludeOr, isExcludeOr);

                // 9) 振り分け先フォルダ
                rule.DestMode = "Custom";
                rule.DestCustomPath = cols[8];

                // 10) 振り分けorコピー
                rule.IsMoveAction = cols[9] == "0";

                // 12, 13, 14) 揺れ吸収 
                rule.IgnoreCase = cols[11] == "1";
                rule.IgnoreWidth = cols[12] == "1";
                rule.IgnoreKana = cols[13] == "1";

                // 16) 履歴保存
                rule.DoNotSaveHistory = cols[15] == "1";

                // 18) フォルダも対象 
                rule.TargetType = cols[17] == "1" ? TargetType.Both : TargetType.FileOnly;

                // 19~22) サイズ限定1
                ApplyLegacySizeCondition(rule, cols[18], cols[19], cols[20], cols[21]);

                // 23~26) サイズ限定2
                ApplyLegacySizeCondition(rule, cols[22], cols[23], cols[24], cols[25]);

                rules.Add(rule);
            }

            return rules;
        }

        // --- ヘルパーメソッド: キーワードの変換 ---
        private static string ConvertLegacyKeyword(string rawKeyword, bool isIncludeOr, bool isExcludeOr)
        {
            if (string.IsNullOrWhiteSpace(rawKeyword)) return "*";

            // 全角スペースを半角スペースに変換して分割
            var tokens = rawKeyword.Replace("　", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var includes = new List<string>();
            var excludes = new List<string>();

            // スラッシュ(/)で始まるものを除外キーワードとして仕分け
            foreach (var token in tokens)
            {
                if (token.StartsWith("/") && token.Length > 1) excludes.Add(token.Substring(1));
                else includes.Add(token);
            }

            // 括弧の最適化
            string incStr = "";
            if (includes.Count == 1)
            {
                incStr = includes[0];
            }
            else if (includes.Count > 1)
            {
                string op = isIncludeOr ? " /| " : " /& ";
                incStr = string.Join(op, includes);
                // Excludesが存在し、かつIncludesがORの場合は括弧が必要
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
                // Includesが存在し、かつExcludesがORの場合は括弧が必要
                if (includes.Count > 0 && isExcludeOr) excStr = "/( " + excStr + " /)";
            }

            if (includes.Count > 0 && excludes.Count > 0)
                return incStr + " /& " + excStr;     // 両方ある場合はANDで繋ぐ
            else if (includes.Count > 0)
                return incStr;                       // 包含のみ
            else if (excludes.Count > 0)
                return "* /& " + excStr;             // 除外のみ (すべて対象から除外)
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