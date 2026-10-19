using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Classify8.Core
{
    // --- 構文木のノード定義 ---
    public interface IConditionNode
    {
        bool Evaluate(string targetText);
    }

    public class TermNode : IConditionNode
    {
        /// <summary>
        /// ワイルドカード「?」に対応する正規表現。見た目の1文字 (書記素クラスタ) に一致する。
        /// 正規表現の「.」は UTF-16 の1単位にしか一致しないため、そのままでは絵文字や「𠮷」(サロゲートペア)、
        /// 結合文字 (アクセント、異体字セレクタ、インド系文字の結合子音など) が「?」1つで一致しない。
        /// Unicode の書記素クラスタ (UAX #29) のうち、ファイル名に現れるものを近似している。
        /// アトミックグループにして、1文字を途中で分けて一致させない (「??」が1文字に一致しないようにする)。
        /// </summary>
        internal const string OneCharPattern =
            @"(?>" +
                // --- 基底の文字 ---
                @"(?:\uD83C[\uDDE6-\uDDFF]\uD83C[\uDDE6-\uDDFF]" +   // 国旗 (地域指示記号 2つ)
                @"|[\uD800-\uDBFF][\uDC00-\uDFFF]" +                  // サロゲートペア (絵文字、𠮷 など)
                @"|[\s\S])" +                                           // その他の1文字
                // --- 基底の文字に続いて、同じ1文字になるもの ---
                @"(?:[\u094D\u09CD\u0ACD\u0B4D\u0C4D\u0D4D]\u200D?\p{Lo}" + // インド系文字の結合子音 (क्ष など)
                @"|\p{M}" +                                             // 結合文字 (アクセント、母音記号、異体字セレクタ FE00-FE0F など)
                @"|\uDB40[\uDD00-\uDDEF]" +                             // 異体字セレクタ (IVS: 漢字の字形の指定)
                @"|\uDB40[\uDC20-\uDC7F]" +                             // タグ文字 (地域の旗)
                @"|\uD83C[\uDFFB-\uDFFF]" +                             // 肌の色の指定
                @"|\u200D(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])" + // ZWJ でつながった絵文字
                @")*" +
            @")";

        private readonly bool _matchAll;
        private readonly string _literal;            // ワイルドカードなしの語 (部分一致)
        private readonly StringComparison _comparison;
        private readonly Regex _regex;               // ワイルドカードありの語

        public TermNode(string term, bool ignoreCase)
        {
            if (term == "*")
            {
                _matchAll = true;
                return;
            }

            if (term.IndexOfAny(new[] { '*', '?' }) < 0)
            {
                // ワイルドカードを含まない語は、正規表現を使わず高速な部分一致で判定する
                _literal = term;
                _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return;
            }

            string pattern = Regex.Escape(term).Replace("\\*", ".*").Replace("\\?", OneCharPattern);
            // ファイル名のような短い文字列では、Compiled はコンパイル時間の方が高くつくため使わない
            var options = RegexOptions.CultureInvariant | RegexOptions.Singleline;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;
            _regex = new Regex(pattern, options);
        }

        public bool Evaluate(string targetText)
        {
            if (_matchAll) return true;
            if (_literal != null) return targetText.IndexOf(_literal, _comparison) >= 0;
            return _regex.IsMatch(targetText);
        }
    }

    public class AndNode : IConditionNode
    {
        public IConditionNode Left { get; set; }
        public IConditionNode Right { get; set; }
        public bool Evaluate(string targetText) => Left.Evaluate(targetText) && Right.Evaluate(targetText);
    }

    public class OrNode : IConditionNode
    {
        public IConditionNode Left { get; set; }
        public IConditionNode Right { get; set; }
        public bool Evaluate(string targetText) => Left.Evaluate(targetText) || Right.Evaluate(targetText);
    }

    public class NotNode : IConditionNode
    {
        public IConditionNode Node { get; set; }
        public bool Evaluate(string targetText) => !Node.Evaluate(targetText);
    }
}
