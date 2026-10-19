using System;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    public class TextNormalizerTests
    {
        [Theory]
        [InlineData("ＡＢＣ１２３", "ABC123")]       // 全角英数 → 半角
        [InlineData("ｶﾀｶﾅ", "カタカナ")]            // 半角カナ → 全角カナ
        [InlineData("ｶﾞｷﾞｸﾞ", "ガギグ")]            // 濁点つきの半角カナも1文字に結合される
        [InlineData("ｱｲｳ　ABC", "アイウ ABC")]       // 全角空白 → 半角空白
        [InlineData("abc", "abc")]
        public void 全角半角の正規化(string input, string expected)
        {
            Assert.Equal(expected, TextNormalizer.Normalize(input, ignoreWidth: true, ignoreKana: false));
        }

        [Theory]
        [InlineData("ひらがな", "ヒラガナ")]
        [InlineData("ぁぃぅぇぉ", "ァィゥェォ")]
        [InlineData("がぎぐ", "ガギグ")]
        [InlineData("ゝゞ", "ヽヾ")]
        [InlineData("カタカナ", "カタカナ")]
        [InlineData("ー・", "ー・")]                  // 長音や中黒はそのまま
        public void ひらがなからカタカナへの正規化(string input, string expected)
        {
            Assert.Equal(expected, TextNormalizer.Normalize(input, ignoreWidth: false, ignoreKana: true));
        }

        [Fact]
        public void 両方を有効にすると両方が適用される()
        {
            Assert.Equal("ABCセイキュウショ", TextNormalizer.Normalize("ＡＢＣせいきゅうしょ", true, true));
            Assert.Equal("カタカナ", TextNormalizer.Normalize("ｶﾀｶﾅ", true, true));
        }

        // =========================================================
        // 濁点・半濁点の表記ゆれ
        // 「が」(U+304C) と「か＋結合用濁点」(U+304B U+3099) は、オプションに関係なく同じにそろえる
        // =========================================================
        [Theory]
        [InlineData(false, false, "が")]  // が
        [InlineData(true, false, "が")]   // が
        [InlineData(false, true, "ガ")]   // ガ
        [InlineData(true, true, "ガ")]    // ガ
        public void 結合用の濁点は_オプションに関係なく1文字にそろえる(bool ignoreWidth, bool ignoreKana, string expected)
        {
            Assert.Equal(expected, TextNormalizer.Normalize("か\u3099", ignoreWidth, ignoreKana));
            Assert.Equal(expected, TextNormalizer.Normalize("が", ignoreWidth, ignoreKana));
        }

        [Theory]
        [InlineData(false, false, "ぱパ")]  // ぱパ
        [InlineData(true, false, "ぱパ")]
        [InlineData(false, true, "パパ")]   // パパ
        [InlineData(true, true, "パパ")]
        public void 結合用の半濁点も_オプションに関係なく1文字にそろえる(bool ignoreWidth, bool ignoreKana, string expected)
        {
            Assert.Equal(expected, TextNormalizer.Normalize("は\u309Aハ\u309A", ignoreWidth, ignoreKana)); // は゜ハ゜ (結合用)
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void 単独の濁点記号は_濁音とは別の文字のまま(bool ignoreWidth, bool ignoreKana)
        {
            // 「か」+「゛」(U+309B: 単独で表示される濁点記号) は、文字としての「が」とは別物
            Assert.NotEqual(
                TextNormalizer.Normalize("が", ignoreWidth, ignoreKana),
                TextNormalizer.Normalize("か゛", ignoreWidth, ignoreKana));
        }

        [Fact]
        public void 両方OFFでも全角半角とひらがなカタカナは変えない()
        {
            // 濁点の結合 (NFC) は行うが、全角英数やカナの種類は変えない
            Assert.Equal("ＡＢＣがｶﾞ", TextNormalizer.Normalize("ＡＢＣか\u3099ｶﾞ", false, false));
            Assert.Equal("ＡＢＣせいきゅうしょｶﾀｶﾅ", TextNormalizer.Normalize("ＡＢＣせいきゅうしょｶﾀｶﾅ", false, false));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void nullと空文字はそのまま返す(string input)
        {
            Assert.Equal(input, TextNormalizer.Normalize(input, true, true));
        }

        [Fact]
        public void 不正なサロゲートを含む名前でも例外にならない()
        {
            // Windows のファイル名には、対になっていないサロゲートが含まれる場合がある
            string invalid = "a\uD800b";

            var result = TextNormalizer.Normalize(invalid, ignoreWidth: true, ignoreKana: true);

            Assert.Contains("a", result);
            Assert.Contains("b", result);
        }
    }
}
