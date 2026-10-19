using System;
using System.IO;
using System.Text;

namespace Classify8.Core
{
    public static class DynamicPathBuilder
    {
        public static string BuildPath(string templatePath, string sourceFilePath, SortRule rule)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
                return templatePath;

            // タグが含まれていそうな場合のみ処理 (スラッシュが含まれているかチェックして高速化)
            if (templatePath.IndexOf('/') < 0)
                return templatePath;

            DateTime now = DateTime.Now;
            string result = templatePath;

            // ファイルのタイムスタンプ系タグ (/F...) が使われているときだけ、ファイルの更新日時を取得する
            // (使われていなければ、ファイルへのアクセスは一切発生しない)
            if (result.IndexOf("/F", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DateTime fileDate = GetItemDate(sourceFilePath, now);

                // 長いタグ（YYYYなど）から先に置換する (/FYYYY が /FYY に誤爆するのを防ぐため)
                result = Replace(result, "/FYYYY", fileDate.ToString("yyyy"));
                result = Replace(result, "/FYY", fileDate.ToString("yy"));
                result = Replace(result, "/FMM", fileDate.ToString("MM"));
                result = Replace(result, "/FDD", fileDate.ToString("dd"));
            }

            // 実行日時のタイムスタンプ
            result = Replace(result, "/YYYY", now.ToString("yyyy"));
            result = Replace(result, "/YY", now.ToString("yy"));
            result = Replace(result, "/MM", now.ToString("MM"));
            result = Replace(result, "/DD", now.ToString("dd"));

            // ルール固有の情報 (フォルダ名として使えない文字は _ に置き換える)
            if (rule != null)
            {
                result = Replace(result, "/NAME", SanitizeForPath(rule.RuleName));

                // 検索条件からスラッシュ(/)を除外して挿入
                string cond = rule.SearchCondition?.Replace("/", "") ?? "";
                result = Replace(result, "/COND", SanitizeForPath(cond));
            }

            return result;
        }

        /// <summary>
        /// フォルダ名として使えない文字(\ / : * ? " &lt; &gt; | など)を _ に置き換える。
        /// 空文字になる場合は "_" を返す。
        /// </summary>
        public static string SanitizeForPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return "_";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            // Windows ではフォルダ名の末尾の空白とピリオドは使えない
            string s = sb.ToString().TrimEnd(' ', '.');
            return s.Length == 0 ? "_" : s;
        }

        private static string Replace(string input, string tag, string value)
        {
            // タグは大文字・小文字を区別せず置換する
            return input.Replace(tag, value, StringComparison.OrdinalIgnoreCase);
        }

        // 万が一ファイルが存在しない場合のフォールバックとして現在日時を返す
        private static DateTime GetItemDate(string path, DateTime fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            try
            {
                if (File.Exists(path)) return new FileInfo(path).LastWriteTime;
                if (Directory.Exists(path)) return new DirectoryInfo(path).LastWriteTime;
            }
            catch { }
            return fallback;
        }
    }
}
