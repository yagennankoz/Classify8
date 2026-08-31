using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Classify8.Core
{
    public static class DynamicPathBuilder
    {
        public static string BuildPath(string templatePath, string sourceFilePath, SortRule rule)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
                return templatePath;

            // タグが含まれていそうな場合のみ処理 (スラッシュが含まれているかチェックして高速化)
            if (!templatePath.Contains("/"))
                return templatePath;

            DateTime now = DateTime.Now;
            // 万が一ファイルが存在しない場合のフォールバックとして現在日時を設定
            DateTime fileDate = File.Exists(sourceFilePath) 
                ? new FileInfo(sourceFilePath).LastWriteTime 
                : now; 

            string result = templatePath;

            // 大文字・小文字を区別せず (?i) に正規表現で置換するためのヘルパー関数
            string ReplaceIgnoreCase(string input, string pattern, string replacement)
            {
                return Regex.Replace(input, "(?i)" + Regex.Escape(pattern), replacement);
            }

            // 長いタグ（YYYYなど）から先に置換する (/YYYY が /YY に誤爆するのを防ぐため)
            
            // ファイルのタイムスタンプ
            result = ReplaceIgnoreCase(result, "/FYYYY", fileDate.ToString("yyyy"));
            result = ReplaceIgnoreCase(result, "/FYY", fileDate.ToString("yy"));
            result = ReplaceIgnoreCase(result, "/FMM", fileDate.ToString("MM"));
            result = ReplaceIgnoreCase(result, "/FDD", fileDate.ToString("dd"));

            // 実行日時のタイムスタンプ
            result = ReplaceIgnoreCase(result, "/YYYY", now.ToString("yyyy"));
            result = ReplaceIgnoreCase(result, "/YY", now.ToString("yy"));
            result = ReplaceIgnoreCase(result, "/MM", now.ToString("MM"));
            result = ReplaceIgnoreCase(result, "/DD", now.ToString("dd"));

            // ルール固有の情報
            if (rule != null)
            {
                result = ReplaceIgnoreCase(result, "/NAME", rule.RuleName ?? "");
                
                // 検索条件からスラッシュ(/)を除外して挿入
                string cond = rule.SearchCondition?.Replace("/", "") ?? "";
                result = ReplaceIgnoreCase(result, "/COND", cond);
            }

            return result;
        }
    }
}