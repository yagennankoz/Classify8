using System;
using Xunit;
using Classify8.Core;

namespace Classify8.Tests
{
    public class ParserTests
    {
        // SortRule.IsMatch と同じ手順 (名前を正規化してから条件を評価) で判定する
        private static bool Matches(SortRule rule, string fileName)
        {
            if (rule.CompiledCondition == null) rule.CompileCondition();
            string name = TextNormalizer.Normalize(fileName, rule.IgnoreWidth, rule.IgnoreKana);
            return rule.CompiledCondition.Evaluate(name);
        }

        private static bool Matches(string condition, string fileName,
                                    bool ignoreCase = true, bool ignoreWidth = false, bool ignoreKana = false)
        {
            var rule = new SortRule
            {
                SearchCondition = condition,
                IgnoreCase = ignoreCase,
                IgnoreWidth = ignoreWidth,
                IgnoreKana = ignoreKana
            };
            return Matches(rule, fileName);
        }

        // =========================================================
        // ワイルドカード
        // =========================================================
        [Theory]
        [InlineData("image.jpg", "*.jpg", true)]
        [InlineData("IMAGE.JPG", "*.jpg", true)] // 大文字小文字無視
        [InlineData("doc.pdf", "*.jpg", false)]
        [InlineData("file_01.txt", "file_??.txt", true)] // ?ワイルドカード
        [InlineData("file_A.txt", "file_??.txt", false)]
        [InlineData("abc", "a?c", true)]
        [InlineData("ac", "a?c", false)]
        [InlineData("xaaay", "x*y", true)]
        [InlineData("xy", "x*y", true)] // * は0文字にも一致する
        public void ワイルドカードの基本判定テスト(string fileName, string condition, bool expected)
        {
            Assert.Equal(expected, Matches(condition, fileName));
        }

        // 「?」は、見た目の1文字 (書記素クラスタ) に一致する。
        // 内部では複数の UTF-16 単位でできている文字でも、「?」1つで一致し、「??」では一致しない。
        [Theory]
        [InlineData("あ")]                                                 // あ (比較用: 1単位の文字)
        [InlineData("\uD842\uDFB7")]                                           // 𠮷 (サロゲートペア)
        [InlineData("\uD83D\uDE00")]                                           // 絵文字 (笑顔 U+1F600)
        [InlineData("\uD83C\uDDEF\uD83C\uDDF5")]                               // 国旗 (日本。地域指示記号 2つ)
        [InlineData("\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67")]       // 家族の絵文字 (3つの絵文字を ZWJ でつないだもの)
        [InlineData("\uD83D\uDC4D\uD83C\uDFFD")]                               // 親指を立てた手の絵文字 + 肌の色の指定
        [InlineData("\u2764\uFE0F")]                                           // ハートの記号 + 異体字セレクタ (絵文字として表示させる指定)
        [InlineData("1\uFE0F\u20E3")]                                          // キーキャップの 1 (1 + 異体字セレクタ + 囲み記号)
        [InlineData("葛\uDB40\uDD00")]                                     // 葛 + IVS (漢字の字形の指定)
        [InlineData("q\u0301")]                                                // q + アクセント記号 (合成済みの文字がない結合文字)
        [InlineData("\u0915\u093F")]                                           // कि ヒンディー語 (子音 + 母音記号)
        [InlineData("\u0915\u094D\u0937")]                                     // क्ष ヒンディー語の結合子音
        [InlineData("\u0E01\u0E34")]                                           // กิ タイ語 (子音 + 母音記号)
        [InlineData("한")]                                                 // 한 韓国語
        public void ワイルドカードの疑問符は見た目の1文字に一致する(string oneChar)
        {
            string fileName = "x" + oneChar + "y.txt";

            Assert.True(Matches("x?y", fileName));
            Assert.False(Matches("x??y", fileName)); // 1文字を途中で分けて、2文字として一致させない
        }

        [Fact]
        public void ワイルドカードの疑問符2つは絵文字2文字に一致する()
        {
            string fileName = "x\uD83D\uDE00\uD842\uDFB7y.txt"; // x + 絵文字 + 𠮷 + y

            Assert.True(Matches("x??y", fileName));
            Assert.False(Matches("x?y", fileName));
            Assert.False(Matches("x???y", fileName));
        }

        [Fact]
        public void 濁点が分かれた名前でもワイルドカードの疑問符1つで一致する()
        {
            // 「か＋結合用濁点」は「が」1文字にそろえてから判定される
            Assert.True(Matches("まん?.txt", "まんか\u3099.txt"));   // まん?.txt と まんが.txt
            Assert.False(Matches("まん??.txt", "まんか\u3099.txt"));
        }

        [Fact]
        public void 対になっていないサロゲートを含む名前でもワイルドカードで判定できる()
        {
            // Windows のファイル名には、対になっていないサロゲートが含まれる場合がある
            string fileName = "x" + '\uD800' + "y.txt";

            Assert.True(Matches("x?y", fileName));
        }

        [Theory]
        [InlineData("a.jpg.bak", "*.jpg", true)]   // キーワードはファイル名の一部に含まれていれば一致する(部分一致)
        [InlineData("report_2026.pdf", "report", true)]
        [InlineData("my_report.pdf", "report", true)]
        [InlineData("rep.pdf", "report", false)]
        public void キーワードは部分一致で判定される(string fileName, string condition, bool expected)
        {
            Assert.Equal(expected, Matches(condition, fileName));
        }

        [Fact]
        public void 大文字小文字を区別する設定のテスト()
        {
            Assert.True(Matches("report", "REPORT.TXT", ignoreCase: true));
            Assert.False(Matches("report", "REPORT.TXT", ignoreCase: false));
        }

        // =========================================================
        // AND / OR / NOT / グループ
        // =========================================================
        [Fact]
        public void 独自構文_ANDとORとNOTの複合テスト()
        {
            // 条件: 「請求書 または 見積書」を含み、かつ「破棄」を含まない
            var rule = new SortRule { SearchCondition = "/( 請求書 /| 見積書 /) /& /! 破棄", IgnoreCase = true };
            rule.CompileCondition();

            Assert.True(rule.CompiledCondition.Evaluate("2026年_請求書.pdf"));
            Assert.True(rule.CompiledCondition.Evaluate("見積書(最終).xlsx"));
            Assert.False(rule.CompiledCondition.Evaluate("古い請求書_破棄予定.pdf")); // 破棄を含むためNG
            Assert.False(rule.CompiledCondition.Evaluate("ただの報告書.pdf"));
        }

        [Theory]
        [InlineData("a /& /! b", "a.txt", true)]
        [InlineData("a /& /! b", "ab.txt", false)]
        [InlineData("a /! b", "a.txt", true)]          // 演算子なしの NOT も暗黙の AND
        [InlineData("a /! b", "ab.txt", false)]
        [InlineData("/! tmp", "report.doc", true)]
        [InlineData("/! tmp", "tmp_report.doc", false)]
        [InlineData("* /& /! tmp", "report.doc", true)]
        public void NOTのテスト(string condition, string fileName, bool expected)
        {
            Assert.Equal(expected, Matches(condition, fileName));
        }

        [Theory]
        [InlineData("a /& b /| c", "c", true)]    // AND は OR より優先: (a かつ b) または c
        [InlineData("a /& b /| c", "a", false)]
        [InlineData("a /& b /| c", "ab", true)]
        [InlineData("a /| b /& c", "a", true)]    // a または (b かつ c)
        [InlineData("a /| b /& c", "b", false)]
        [InlineData("a /| b /& c", "bc", true)]
        [InlineData("/( a /| b /) /& c", "b", false)] // 括弧で優先順位を変えられる
        [InlineData("/( a /| b /) /& c", "bc", true)]
        [InlineData("/( a /| /( b /& c /) /) /& /! d", "bc", true)] // ネスト
        [InlineData("/( a /| /( b /& c /) /) /& /! d", "bcd", false)]
        public void 優先順位とグループのテスト(string condition, string fileName, bool expected)
        {
            Assert.Equal(expected, Matches(condition, fileName));
        }

        // =========================================================
        // 空白(暗黙のAND)と引用符
        // =========================================================
        [Theory]
        [InlineData("PURPLE SKY", "purple sky.jpg", true)]  // 空白で区切ったキーワードは暗黙のAND
        [InlineData("PURPLE SKY", "sky purple.jpg", true)]  // 順序は問わない
        [InlineData("PURPLE SKY", "purple.jpg", false)]
        [InlineData("PURPLE　SKY", "sky purple.jpg", true)] // 全角空白も区切りになる
        [InlineData("*.jpg *.png", "a.jpg", false)]          // AND なので両方を満たすものだけ
        public void 空白区切りは暗黙のANDになる(string condition, string fileName, bool expected)
        {
            Assert.Equal(expected, Matches(condition, fileName));
        }

        [Theory]
        [InlineData("\"PURPLE SKY\"", "purple sky.jpg", true)]  // 引用符で囲むと1つの語
        [InlineData("\"PURPLE SKY\"", "sky purple.jpg", false)]
        [InlineData("\"a b\" c", "a b c.txt", true)]
        [InlineData("\"a b\" c", "a b.txt", false)]
        [InlineData("\"x /& y\"", "x /& y.txt", true)]          // 引用符の中の演算子は文字として扱う
        [InlineData("\"x /& y\"", "x y.txt", false)]
        public void 引用符のテスト(string condition, string fileName, bool expected)
        {
            Assert.Equal(expected, Matches(condition, fileName));
        }

        [Theory]
        [InlineData("a/&b", "ab", true)]    // 演算子は語の途中でも区切りになる
        [InlineData("a/&b", "a", false)]
        [InlineData("a/|b", "b", true)]
        [InlineData("a/b", "a/b", true)]    // 演算子ではない / は文字として扱う
        public void 演算子と語の境界のテスト(string condition, string fileName, bool expected)
        {
            Assert.Equal(expected, Matches(condition, fileName));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("*")]
        public void 空の条件はすべてに一致する(string condition)
        {
            Assert.True(Matches(condition, "anything.txt"));
        }

        // =========================================================
        // 構文エラー (黙って無視せず、エラーにする)
        // =========================================================
        [Theory]
        [InlineData("/( a")]        // 閉じ括弧がない
        [InlineData("a /)")]        // 開き括弧がない
        [InlineData("a /|")]        // 演算子の後ろが空
        [InlineData("/| a")]        // 演算子で始まる
        [InlineData("\"abc")]       // 引用符が閉じていない
        [InlineData("/! ")]         // NOT の対象がない
        [InlineData("a /& /& b")]   // 演算子の連続
        [InlineData("/( /)")]       // 空のグループ
        public void 構文エラーは例外になる(string condition)
        {
            Assert.Throws<FormatException>(() => new ConditionParser().Parse(condition));
        }

        // =========================================================
        // 表記ゆれの吸収 (全角/半角、ひらがな/カタカナ)
        // =========================================================
        [Fact]
        public void 全角半角を区別しない設定のテスト()
        {
            Assert.True(Matches("ABC123", "ＡＢＣ１２３.txt", ignoreWidth: true));
            Assert.False(Matches("ABC123", "ＡＢＣ１２３.txt", ignoreWidth: false));
            Assert.True(Matches("ｶﾀｶﾅ", "カタカナ.txt", ignoreWidth: true));   // 半角カナ → 全角カナ
            Assert.True(Matches("ＡＢＣ", "abc.txt", ignoreWidth: true));        // 条件側が全角でも一致する
        }

        [Fact]
        public void ひらがなカタカナを区別しない設定のテスト()
        {
            Assert.True(Matches("せいきゅうしょ", "セイキュウショ.pdf", ignoreKana: true));
            Assert.True(Matches("セイキュウショ", "せいきゅうしょ.pdf", ignoreKana: true));
            Assert.False(Matches("せいきゅうしょ", "セイキュウショ.pdf", ignoreKana: false));
        }

        // =========================================================
        // 全体設定 (NGキーワード)
        // =========================================================
        [Fact]
        public void 全体設定_NGキーワードの生成ロジックテスト()
        {
            // 全体設定画面(txtNgKeywords)でユーザーが改行区切りで入力したと想定
            string ngKeywordsInput = "破棄\nテスト用\r\n*old*";

            // MainWindow が実際に使っている変換処理をそのまま呼ぶ
            var node = NgKeywordBuilder.BuildNode(ngKeywordsInput);

            // これらいずれかに該当すれば true(除外対象) となることを確認
            Assert.True(node.Evaluate("破棄ファイル.txt"));
            Assert.True(node.Evaluate("これはテスト用データ.pdf"));
            Assert.True(node.Evaluate("my_old_data.csv"));

            // 該当しないファイルは false
            Assert.False(node.Evaluate("大切なデータ.xlsx"));
            Assert.False(node.Evaluate("通常の報告書.docx"));
        }

        [Fact]
        public void 全体設定_NGキーワードは1行が1キーワードで空白もそのまま扱う()
        {
            var node = NgKeywordBuilder.BuildNode("Thumbs db");

            Assert.True(node.Evaluate("Thumbs db.txt"));
            Assert.False(node.Evaluate("Thumbs.db"));      // 「Thumbs」かつ「db」ではなく、「Thumbs db」という1語
            Assert.False(node.Evaluate("db Thumbs.txt"));
        }

        [Fact]
        public void 全体設定_NGキーワードの演算子や引用符は文字として扱う()
        {
            var node = NgKeywordBuilder.BuildNode("a /& b");
            Assert.True(node.Evaluate("a /& b.txt"));
            Assert.False(node.Evaluate("a b.txt"));

            // 引用符は取り除かれる (条件式を壊さない)
            Assert.Equal("\"abc\"", NgKeywordBuilder.BuildConditionString("\"abc\""));
            Assert.True(NgKeywordBuilder.BuildNode("\"abc\"").Evaluate("xabcx"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        [InlineData("\"\"")]
        public void 全体設定_NGキーワードが空ならnullになる(string input)
        {
            Assert.Null(NgKeywordBuilder.BuildNode(input));
            Assert.Equal("", NgKeywordBuilder.BuildConditionString(input));
        }

        [Fact]
        public void 全体設定_NGキーワードは大文字小文字を区別しない()
        {
            Assert.True(NgKeywordBuilder.BuildNode("TEMP").Evaluate("temp_file.txt"));
        }

        [Fact]
        public void 全体設定_NGキーワードは濁点が分かれた名前にも一致する()
        {
            // 「が」(U+304C) と「か＋結合用濁点」(U+304B U+3099) は同じ文字として扱う
            var composedKeyword = NgKeywordBuilder.BuildNode("まんが");       // まんが
            var decomposedKeyword = NgKeywordBuilder.BuildNode("まんか\u3099"); // まんが (濁点が分かれた形)

            Assert.True(NgKeywordBuilder.IsExcluded(composedKeyword, "まんか\u3099.png"));
            Assert.True(NgKeywordBuilder.IsExcluded(decomposedKeyword, "まんが.png"));
            Assert.True(NgKeywordBuilder.IsExcluded(composedKeyword, "まんが.png"));

            // 「か」は「が」とは別の文字
            Assert.False(NgKeywordBuilder.IsExcluded(composedKeyword, "まんか.png"));
        }

        [Fact]
        public void 全体設定_NGキーワードのワイルドカードの疑問符も見た目の1文字に一致する()
        {
            var node = NgKeywordBuilder.BuildNode("tmp?.log");

            Assert.True(NgKeywordBuilder.IsExcluded(node, "tmp\uD83D\uDE00.log"));   // tmp + 絵文字 + .log
            Assert.False(NgKeywordBuilder.IsExcluded(node, "tmp.log"));
        }

        [Fact]
        public void 全体設定_NGキーワードがなければ除外しない()
        {
            Assert.False(NgKeywordBuilder.IsExcluded(null, "anything.txt"));
        }
    }
}
