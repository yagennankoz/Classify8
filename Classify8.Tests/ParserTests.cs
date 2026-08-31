using System;
using Xunit;
using Classify8.Core;

namespace Classify8.Tests
{
    public class ParserTests
    {
        [Theory]
        [InlineData("image.jpg", "*.jpg", true)]
        [InlineData("IMAGE.JPG", "*.jpg", true)] // 大文字小文字無視
        [InlineData("doc.pdf", "*.jpg", false)]
        [InlineData("file_01.txt", "file_??.txt", true)] // ?ワイルドカード
        [InlineData("file_A.txt", "file_??.txt", false)]
        public void ワイルドカードの基本判定テスト(string fileName, string condition, bool expected)
        {
            var rule = new SortRule { SearchCondition = condition, IgnoreCase = true };
            rule.CompileCondition();
            Assert.Equal(expected, rule.CompiledCondition.Evaluate(fileName));
        }

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

        // =========================================================
        // AppSettings (全体設定) のNGキーワード処理テスト
        // =========================================================
        [Fact]
        public void 全体設定_NGキーワードの生成ロジックテスト()
        {
            // 全体設定画面(txtNgKeywords)でユーザーが改行区切りで入力したと想定
            string ngKeywordsInput = "破棄\nテスト用\r\n*old*";
            
            // MainWindowで行っているのと同じ、改行での分割＆OR条件(/|)での連結処理
            var keywords = ngKeywordsInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string globalCondition = string.Join(" /| ", keywords);
            
            // 生成された条件式: "破棄 /| テスト用 /| *old*"
            var parser = new ConditionParser(ignoreCase: true);
            var node = parser.Parse(globalCondition);

            // これらいずれかに該当すれば true(除外対象) となることを確認
            Assert.True(node.Evaluate("破棄ファイル.txt"));
            Assert.True(node.Evaluate("これはテスト用データ.pdf"));
            Assert.True(node.Evaluate("my_old_data.csv"));
            
            // 該当しないファイルは false
            Assert.False(node.Evaluate("大切なデータ.xlsx"));
            Assert.False(node.Evaluate("通常の報告書.docx"));
        }
    }
}