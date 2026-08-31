using System;
using System.Collections.Generic;
using System.IO;
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
            // 🚨 修正: ここ(コンストラクタ)で Shift_JIS を登録し、このクラスの全テストで使えるようにする
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            _tempCsvPath = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(_tempCsvPath))
                File.Delete(_tempCsvPath);
        }

        [Fact]
        public void ImportFromClassyNyCsv_正常系_各種ロジックの変換テスト()
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
            var rules = LegacyImporter.ImportFromClassyNyCsv(_tempCsvPath, presets);

            // 【検証 1】ルール本体と共通プリセットの生成
            Assert.Single(rules);
            var rule = rules[0];
            Assert.Equal("テストルール", rule.RuleName);
            
            Assert.Equal("Preset", rule.SourceMode);
            Assert.Single(presets);
            Assert.Equal("ClassiNy共通振り分け元", presets[0].AliasName);
            Assert.Equal(presets[0].Id, rule.SourcePresetId);

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

            var rules = LegacyImporter.ImportFromClassyNyCsv(_tempCsvPath, new List<Preset>());
            Assert.Equal("hoge /& /( /! foo /| /! bar /)", rules[0].SearchCondition);
        }
    }
}