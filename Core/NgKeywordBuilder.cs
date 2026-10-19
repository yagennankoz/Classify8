using System;
using System.Linq;

namespace Classify8.Core
{
    /// <summary>
    /// 全体設定の「振り分けないファイル(NGキーワード)」を、判定用の条件に変換する。
    /// 1行 = 1キーワード。空白や演算子を含んでいても、そのまま文字として扱う。
    /// </summary>
    public static class NgKeywordBuilder
    {
        public static string BuildConditionString(string rawKeywords)
        {
            if (string.IsNullOrWhiteSpace(rawKeywords)) return "";

            var keywords = rawKeywords
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Replace("\"", "").Trim())
                .Where(k => k.Length > 0)
                .ToList();

            // 各キーワードは引用符で囲み、空白や /& などを含んでいても1つの語として扱わせる
            return string.Join(" /| ", keywords.Select(k => "\"" + k + "\""));
        }

        /// <returns>NGキーワードが1つもない場合は null</returns>
        public static IConditionNode BuildNode(string rawKeywords)
        {
            string condition = BuildConditionString(rawKeywords);
            return condition.Length == 0 ? null : new ConditionParser(ignoreCase: true).Parse(condition);
        }

        /// <summary>
        /// 名前が NGキーワードに該当するか。キーワード側と同じ正規化 (「が」と「か＋濁点」をそろえる等) をしてから判定する。
        /// </summary>
        /// <param name="ngNode">BuildNode の結果。null (キーワードなし) なら常に false</param>
        public static bool IsExcluded(IConditionNode ngNode, string name)
        {
            if (ngNode == null) return false;
            return ngNode.Evaluate(TextNormalizer.Normalize(name, ignoreWidth: false, ignoreKana: false));
        }
    }
}
