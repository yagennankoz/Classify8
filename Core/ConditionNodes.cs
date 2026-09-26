using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Classify8.Core
{
    // --- 構文木のノード定義 (前回提示のものを拡張) ---
    public interface IConditionNode
    {
        bool Evaluate(string targetText);
    }

    public class TermNode : IConditionNode
    {
        private readonly Regex _regex;

        public TermNode(string term, bool ignoreCase)
        {
            if (term == "*")
            {
                _regex = null;
                return;
            }

            string pattern = Regex.Escape(term).Replace("\\*", ".*").Replace("\\?", ".");
            var options = RegexOptions.Compiled;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;
            _regex = new Regex(pattern, options);
        }

        public bool Evaluate(string targetText)
        {
            if (_regex == null) return true;
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