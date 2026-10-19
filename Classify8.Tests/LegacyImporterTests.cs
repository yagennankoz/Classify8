using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Classify8.Core;

namespace Classify8.Tests
{
    public class LegacyImporterTests : IDisposable
    {
        private readonly string _tempCsvPath;

        public LegacyImporterTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _tempCsvPath = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(_tempCsvPath))
                File.Delete(_tempCsvPath);
        }

        // --- テスト用ヘルパー ---------------------------------------------------

        // ClassiNy の CSV 1行(28列)を作る。既定値は「有効・個別指定の振り分け元・条件なし」。
        // customize で列ごとに上書きする。
        private static string Row(Action<string[]> customize = null)
        {
            var cols = Enumerable.Repeat("0", 28).ToArray();
            cols[0] = "1";            // 有効
            cols[1] = "rule";         // 設定名
            cols[2] = "1";            // 振り分け元: 1=個別指定, 0=共通
            cols[3] = @"C:\Src";      // 振り分け元フォルダ
            cols[7] = "";             // キーワード
            cols[8] = @"D:\Dest";     // 振り分け先
            customize?.Invoke(cols);
            return string.Join(",", cols.Select(Csv));
        }

        private static string Csv(string value)
        {
            if (value.Contains(',') || value.Contains('"')) return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private List<SortRule> Import(IList<Preset> presets, params string[] rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ヘッダ1");
            sb.AppendLine("ヘッダ2");
            sb.AppendLine("ヘッダ3");
            foreach (var row in rows) sb.AppendLine(row);

            File.WriteAllText(_tempCsvPath, sb.ToString(), Encoding.GetEncoding("Shift_JIS"));
            return LegacyImporter.ImportFromClassiNyCsv(_tempCsvPath, presets);
        }

        // --- 既存のテスト --------------------------------------------------------

        [Fact]
        public void ImportFromClassiNyCsv_正常系_各種ロジックの変換テスト()
        {
            // ダミーのCSVを作成 (Shift_JIS)
            var sb = new StringBuilder();
            sb.AppendLine("ヘッダ1");
            sb.AppendLine("ヘッダ2");
            sb.AppendLine("ヘッダ3");

            // カラム構成: 
            // 0:チェック(1), 1:設定名, 2:元フォルダ選択(0:共通), 3:適用フォルダ, 4:サブフォルダ(1), 5:振り分けOR(1), 6:除外OR(0), 
            // 7:キーワード("aaaa bbbb /cccc"), 8:振り分け先(D:\Dest), 9:移動orコピー(1:コピー), 10:その他(0), 
            // 11:大文字小文字(1), 12:半角全角(1), 13:ひらがなカタカナ(1), 14:コンペア(0), 15:履歴(1), 16:拡張子(0), 17:フォルダ対象(1), 
            // 18:サイズ限定1(1), 19:サイズ値(10), 20:単位(1:KB), 21:以下の/以上の(1:以下のものを対象),
            // 22:サイズ限定2(0)...以降適当
            sb.AppendLine("1,テストルール,0,,1,1,0,aaaa bbbb /cccc,D:\\Dest,1,0,1,1,1,0,1,0,1,1,10,1,1,0,0,0,0,0,0");

            File.WriteAllText(_tempCsvPath, sb.ToString(), Encoding.GetEncoding("Shift_JIS"));

            var presets = new List<Preset>();
            var rules = LegacyImporter.ImportFromClassiNyCsv(_tempCsvPath, presets);

            // 【検証 1】ルール本体と共通プリセットの生成
            Assert.Single(rules);
            var rule = rules[0];
            Assert.Equal("テストルール", rule.RuleName);

            Assert.Equal("Preset", rule.SourceMode);
            Assert.Single(presets);
            Assert.Equal("ClassiNy共通振り分け元", presets[0].AliasName);
            Assert.Equal(presets[0].Id, rule.SourcePresetId);

            // 【検証 1b】共通プリセットのパスは空のまま作られ(C:\ などを仮置きしない)、
            //            パスが決まるまでこのプリセットを使うルールは無効になる (ドライブ全体を誤って検索しないため)
            Assert.Equal("", presets[0].DirectoryPath);
            Assert.False(rule.IsEnabled);

            // 【検証 2】キーワードの変換ロジック
            Assert.Equal("/( aaaa /| bbbb /) /& /! cccc", rule.SearchCondition);

            // 【検証 3】その他のフラグ類
            Assert.False(rule.IsMoveAction); // コピー
            Assert.True(rule.IgnoreCase);
            Assert.True(rule.DoNotSaveHistory);
            Assert.Equal(TargetType.Both, rule.TargetType);

            // 【検証 4】サイズ限定の逆転処理 
            Assert.True(rule.SizeMax_Enabled);
            Assert.Equal(10, rule.SizeMax_Value);
            Assert.Equal(SizeUnit.KB, rule.SizeMax_Unit);
            Assert.False(rule.SizeMin_Enabled);
        }

        [Fact]
        public void ConvertLegacyKeyword_複雑なパターンの最適化テスト()
        {
            var sb = new StringBuilder();
            sb.AppendLine("H1\nH2\nH3");

            // 振り分け:AND(0), 除外:OR(1), キーワード:"hoge /foo /bar"
            // 期待値: "hoge /& /( /! foo /| /! bar /)" 
            sb.AppendLine("1,Rule2,1,C:\\Src,0,0,1,hoge /foo /bar,D:\\Dest,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0");

            // ここでも Shift_JIS が使えるようになる
            File.WriteAllText(_tempCsvPath, sb.ToString(), Encoding.GetEncoding("Shift_JIS"));

            var rules = LegacyImporter.ImportFromClassiNyCsv(_tempCsvPath, new List<Preset>());
            Assert.Equal("hoge /& /( /! foo /| /! bar /)", rules[0].SearchCondition);
        }

        // --- 有効/無効と共通プリセット -----------------------------------------------

        [Fact]
        public void 個別指定の振り分け元は有効のまま取り込まれる()
        {
            var presets = new List<Preset>();
            var rules = Import(presets, Row());

            var rule = Assert.Single(rules);
            Assert.True(rule.IsEnabled);
            Assert.Equal("Custom", rule.SourceMode);
            Assert.Equal(@"C:\Src", rule.SourceCustomPath);
            Assert.Equal(@"D:\Dest", rule.DestCustomPath);
            Assert.Empty(presets); // 共通プリセットは作られない
        }

        [Fact]
        public void 元から無効のルールは無効のまま取り込まれる()
        {
            var rules = Import(new List<Preset>(), Row(c => c[0] = "0"));

            Assert.False(Assert.Single(rules).IsEnabled);
        }

        [Fact]
        public void 共通プリセットのパスが設定済みなら有効のまま取り込まれる()
        {
            var existing = new Preset { AliasName = "ClassiNy共通振り分け元", DirectoryPath = @"D:\Common" };
            var presets = new List<Preset> { existing };

            var rules = Import(presets, Row(c => c[2] = "0"));

            var rule = Assert.Single(rules);
            Assert.True(rule.IsEnabled);
            Assert.Equal(existing.Id, rule.SourcePresetId);
            Assert.Single(presets); // 既存のプリセットを再利用し、増やさない
            Assert.Equal(@"D:\Common", presets[0].DirectoryPath);
        }

        [Fact]
        public void 共通プリセットは複数ルールでも1つだけ作られる()
        {
            var presets = new List<Preset>();
            var rules = Import(presets,
                Row(c => { c[1] = "r1"; c[2] = "0"; }),
                Row(c => { c[1] = "r2"; c[2] = "0"; }));

            Assert.Equal(2, rules.Count);
            Assert.Single(presets);
            Assert.All(rules, r => Assert.Equal(presets[0].Id, r.SourcePresetId));
            Assert.All(rules, r => Assert.False(r.IsEnabled));
        }

        [Fact]
        public void 列数が足りない行は無視される()
        {
            var rules = Import(new List<Preset>(), "1,too,few,columns", Row());

            Assert.Single(rules);
        }

        // --- キーワードの変換と、新しい構文パーサとの整合 -----------------------------

        private static IConditionNode Parse(string condition) => new ConditionParser(ignoreCase: true).Parse(condition);

        [Fact]
        public void キーワード_引用符付きのフレーズは1語として変換される()
        {
            var rules = Import(new List<Preset>(), Row(c => c[7] = "\"hello world\" foo"));

            Assert.Equal("\"hello world\" /& foo", rules[0].SearchCondition);

            var node = Parse(rules[0].SearchCondition);
            Assert.True(node.Evaluate("hello world foo.txt"));
            Assert.False(node.Evaluate("hello foo.txt")); // フレーズ「hello world」を含まない
        }

        [Fact]
        public void キーワード_除外のみの指定はすべて対象から除外条件を引いた形になる()
        {
            var rules = Import(new List<Preset>(), Row(c => c[7] = "/tmp"));

            Assert.Equal("* /& /! tmp", rules[0].SearchCondition);

            var node = Parse(rules[0].SearchCondition);
            Assert.True(node.Evaluate("report.doc"));
            Assert.False(node.Evaluate("tmp_file.doc"));
        }

        [Fact]
        public void キーワード_空欄はすべて対象になる()
        {
            var rules = Import(new List<Preset>(), Row(c => c[7] = ""));

            Assert.Equal("*", rules[0].SearchCondition);
        }

        [Fact]
        public void キーワード_AND指定の複数語は新しい構文でも全語を含むものだけに一致する()
        {
            // 振り分け: AND(0)
            var rules = Import(new List<Preset>(), Row(c => { c[5] = "0"; c[7] = "aaa bbb"; }));

            Assert.Equal("aaa /& bbb", rules[0].SearchCondition);
            var node = Parse(rules[0].SearchCondition);
            Assert.True(node.Evaluate("aaa_bbb.txt"));
            Assert.False(node.Evaluate("aaa.txt"));
        }

        [Fact]
        public void キーワード_変換結果はどのパターンでも新しい構文として解釈できる()
        {
            var keywords = new[] { "a", "a b", "a b c", "a /x", "/x /y", "\"p q\" /\"r s\"", "a -b", "" };
            foreach (var includeOr in new[] { "0", "1" })
            {
                foreach (var excludeOr in new[] { "0", "1" })
                {
                    foreach (var kw in keywords)
                    {
                        var rules = Import(new List<Preset>(), Row(c => { c[5] = includeOr; c[6] = excludeOr; c[7] = kw; }));
                        var condition = rules[0].SearchCondition;
                        var ex = Record.Exception(() => Parse(condition));
                        Assert.True(ex == null, $"キーワード「{kw}」(含むOR={includeOr}, 除くOR={excludeOr}) → 「{condition}」が解釈できません: {ex?.Message}");
                    }
                }
            }
        }

        // --- サイズ限定 ----------------------------------------------------------

        [Fact]
        public void サイズ限定_以上の指定は下限として取り込まれる()
        {
            // 22:有効, 23:値, 24:単位(2=MB), 25:種類(2=以上のものを対象)
            var rules = Import(new List<Preset>(), Row(c => { c[22] = "1"; c[23] = "5"; c[24] = "2"; c[25] = "2"; }));

            var rule = rules[0];
            Assert.True(rule.SizeMin_Enabled);
            Assert.Equal(5, rule.SizeMin_Value);
            Assert.Equal(SizeUnit.MB, rule.SizeMin_Unit);
            Assert.False(rule.SizeMax_Enabled);
        }
    }
}
