using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Classify8.Core
{
    public class ConditionParser
    {
        private enum TokenType { And, Or, Not, LParen, RParen, Term, EOF }
        private class Token
        {
            public TokenType Type { get; set; }
            public string Value { get; set; }
        }

        private readonly bool _ignoreCase;

        public ConditionParser(bool ignoreCase = true)
        {
            _ignoreCase = ignoreCase;
        }

        public IConditionNode Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new TermNode("*", _ignoreCase); // 空ならすべて一致

            var tokens = Tokenize(input);
            int pos = 0;
            return ParseExpression(tokens, ref pos);
        }

        // 文字列をトークンに分解し、暗黙のANDを補完する
        private List<Token> Tokenize(string input)
        {
            var tokens = new List<Token>();
            // 演算子で分割（デリミタも残す）
            var parts = Regex.Split(input, @"(/\&|/\||/\!|/\(|/\))");

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                switch (part)
                {
                    case "/&": tokens.Add(new Token { Type = TokenType.And }); break;
                    case "/|": tokens.Add(new Token { Type = TokenType.Or }); break;
                    case "/!": tokens.Add(new Token { Type = TokenType.Not }); break;
                    case "/(": tokens.Add(new Token { Type = TokenType.LParen }); break;
                    case "/)": tokens.Add(new Token { Type = TokenType.RParen }); break;
                    default: 
                        tokens.Add(new Token { Type = TokenType.Term, Value = part.Trim() }); 
                        break;
                }
            }

            // 暗黙のANDの補完 (例: [Term] [Term] の間に [And] を挟む)
            var finalTokens = new List<Token>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0)
                {
                    var prev = tokens[i - 1].Type;
                    var curr = tokens[i].Type;
                    
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
                finalTokens.Add(tokens[i]);
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
                if (tokens[pos].Type == TokenType.RParen) pos++; // 閉じ括弧をスキップ
                return node;
            }
            if (token.Type == TokenType.Term)
            {
                pos++;
                return new TermNode(token.Value, _ignoreCase);
            }
            throw new Exception("構文エラー: 予期しないトークンです。");
        }
    }
}