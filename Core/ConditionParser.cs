using System;
using System.Collections.Generic;
using System.Text;

namespace Classify8.Core
{
    /// <summary>
    /// 検索条件の構文:
    ///   *  ?        ワイルドカード
    ///   空白         キーワード間は暗黙の AND
    ///   /&amp;  /|  /!   AND / OR / NOT
    ///   /(  /)      グループ化
    ///   " "         空白や演算子を含むキーワードを囲む (例: "PURPLE SKY")
    /// </summary>
    public class ConditionParser
    {
        private enum TokenType { And, Or, Not, LParen, RParen, Term, EOF }
        private class Token
        {
            public TokenType Type { get; set; }
            public string Value { get; set; }
        }

        private readonly bool _ignoreCase;
        private readonly bool _ignoreWidth;
        private readonly bool _ignoreKana;

        public ConditionParser(bool ignoreCase = true, bool ignoreWidth = false, bool ignoreKana = false)
        {
            _ignoreCase = ignoreCase;
            _ignoreWidth = ignoreWidth;
            _ignoreKana = ignoreKana;
        }

        /// <exception cref="FormatException">構文エラー</exception>
        public IConditionNode Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new TermNode("*", _ignoreCase); // 空ならすべて一致

            var tokens = Tokenize(input);
            int pos = 0;
            var node = ParseExpression(tokens, ref pos);

            // 式の途中で解析が止まっている場合は、黙って無視せずエラーにする
            if (tokens[pos].Type != TokenType.EOF)
            {
                if (tokens[pos].Type == TokenType.RParen)
                    throw new FormatException(Loc.T("parser.err.missing_open_paren"));
                throw new FormatException(Loc.T("parser.err.trailing"));
            }
            return node;
        }

        private Token MakeTerm(string raw)
        {
            return new Token
            {
                Type = TokenType.Term,
                Value = TextNormalizer.Normalize(raw, _ignoreWidth, _ignoreKana)
            };
        }

        // 文字列を1文字ずつ読んでトークンに分解し、暗黙のANDを補完する
        private List<Token> Tokenize(string input)
        {
            var tokens = new List<Token>();
            var current = new StringBuilder();

            void Flush()
            {
                if (current.Length > 0)
                {
                    tokens.Add(MakeTerm(current.ToString()));
                    current.Clear();
                }
            }

            int i = 0;
            while (i < input.Length)
            {
                char c = input[i];

                // 演算子 (/& /| /! /( /))。語の途中でも区切りとして扱う
                if (c == '/' && i + 1 < input.Length)
                {
                    TokenType? op = null;
                    switch (input[i + 1])
                    {
                        case '&': op = TokenType.And; break;
                        case '|': op = TokenType.Or; break;
                        case '!': op = TokenType.Not; break;
                        case '(': op = TokenType.LParen; break;
                        case ')': op = TokenType.RParen; break;
                    }
                    if (op.HasValue)
                    {
                        Flush();
                        tokens.Add(new Token { Type = op.Value });
                        i += 2;
                        continue;
                    }
                }

                // 空白(全角含む)は語の区切り
                if (char.IsWhiteSpace(c))
                {
                    Flush();
                    i++;
                    continue;
                }

                // 引用符: 語の先頭にあるときだけ、次の " までを1つの語として読む
                if (c == '"' && current.Length == 0)
                {
                    int end = input.IndexOf('"', i + 1);
                    if (end < 0) throw new FormatException(Loc.T("parser.err.unclosed_quote"));

                    string quoted = input.Substring(i + 1, end - i - 1);
                    if (quoted.Length > 0) tokens.Add(MakeTerm(quoted));
                    i = end + 1;
                    continue;
                }

                current.Append(c);
                i++;
            }
            Flush();

            // 暗黙のANDの補完 (例: [Term] [Term] の間に [And] を挟む)
            var finalTokens = new List<Token>();
            for (int k = 0; k < tokens.Count; k++)
            {
                if (k > 0)
                {
                    var prev = tokens[k - 1].Type;
                    var curr = tokens[k].Type;

                    bool needsAnd =
                        (prev == TokenType.Term && curr == TokenType.Term) ||
                        (prev == TokenType.Term && curr == TokenType.Not) ||
                        (prev == TokenType.Term && curr == TokenType.LParen) ||
                        (prev == TokenType.RParen && curr == TokenType.Term) ||
                        (prev == TokenType.RParen && curr == TokenType.Not) ||
                        (prev == TokenType.RParen && curr == TokenType.LParen);

                    if (needsAnd)
                    {
                        finalTokens.Add(new Token { Type = TokenType.And });
                    }
                }
                finalTokens.Add(tokens[k]);
            }
            finalTokens.Add(new Token { Type = TokenType.EOF });
            return finalTokens;
        }

        // 再帰的下向き構文解析 (OR の評価)
        private IConditionNode ParseExpression(List<Token> tokens, ref int pos)
        {
            var node = ParseTerm(tokens, ref pos);
            while (tokens[pos].Type == TokenType.Or)
            {
                pos++;
                node = new OrNode { Left = node, Right = ParseTerm(tokens, ref pos) };
            }
            return node;
        }

        // (AND の評価)
        private IConditionNode ParseTerm(List<Token> tokens, ref int pos)
        {
            var node = ParseFactor(tokens, ref pos);
            while (tokens[pos].Type == TokenType.And)
            {
                pos++;
                node = new AndNode { Left = node, Right = ParseFactor(tokens, ref pos) };
            }
            return node;
        }

        // (NOT と 括弧 の評価)
        private IConditionNode ParseFactor(List<Token> tokens, ref int pos)
        {
            var token = tokens[pos];
            if (token.Type == TokenType.Not)
            {
                pos++;
                return new NotNode { Node = ParseFactor(tokens, ref pos) };
            }
            if (token.Type == TokenType.LParen)
            {
                pos++;
                var node = ParseExpression(tokens, ref pos);
                if (tokens[pos].Type != TokenType.RParen)
                    throw new FormatException(Loc.T("parser.err.missing_close_paren"));
                pos++;
                return node;
            }
            if (token.Type == TokenType.Term)
            {
                pos++;
                return new TermNode(token.Value, _ignoreCase);
            }

            if (token.Type == TokenType.EOF)
                throw new FormatException(Loc.T("parser.err.unexpected_end"));
            throw new FormatException(Loc.T("parser.err.misplaced"));
        }
    }
}
