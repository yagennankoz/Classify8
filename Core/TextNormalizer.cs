using System;
using System.Text;

namespace Classify8.Core
{
    // ==========================================
    // 検索時の表記ゆれ吸収 (全角/半角、ひらがな/カタカナ)
    // ==========================================
    public static class TextNormalizer
    {
        /// <summary>
        /// 検索の判定用に文字列を正規化する。
        /// オプションに関係なく、「が」と「か＋結合用濁点(U+3099)」のような同じ文字の別表現は常にそろえる (NFC)。
        /// Mac で作られたファイル名などは、濁点・半濁点が分かれた形で保存されていることがあるため。
        /// </summary>
        public static string Normalize(string text, bool ignoreWidth, bool ignoreKana)
        {
            if (string.IsNullOrEmpty(text)) return text;

            try
            {
                // ignoreWidth: 全角英数→半角、半角カナ→全角カナなども行う (NFKC)。濁点の結合は NFC/NFKC のどちらでも行われる
                text = text.Normalize(ignoreWidth ? NormalizationForm.FormKC : NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                // 不正なサロゲート等を含むファイル名は、そのまま扱う
            }

            if (ignoreKana)
            {
                var sb = new StringBuilder(text.Length);
                foreach (char c in text)
                {
                    // ぁ(U+3041)～ゖ(U+3096)、ゝゞ(U+309D,U+309E) → 対応するカタカナ
                    if ((c >= '\u3041' && c <= '\u3096') || c == '\u309D' || c == '\u309E')
                        sb.Append((char)(c + 0x60));
                    else
                        sb.Append(c);
                }
                text = sb.ToString();
            }

            return text;
        }
    }
}
