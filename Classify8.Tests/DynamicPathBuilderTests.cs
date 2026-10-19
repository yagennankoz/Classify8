using System;
using System.IO;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    public class DynamicPathBuilderTests : IDisposable
    {
        private readonly string _dir;

        public DynamicPathBuilderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "c8path_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // =========================================================
        // フォルダ名として使えない文字の置き換え
        // =========================================================
        [Theory]
        [InlineData("normal name", "normal name")]
        [InlineData("a:b", "a_b")]
        [InlineData("*.jpg", "_.jpg")]
        [InlineData("a\\b", "a_b")]
        [InlineData("what?", "what_")]
        [InlineData("say \"hi\"", "say _hi_")]
        [InlineData("a|b<c>d", "a_b_c_d")]
        [InlineData("trailing. ", "trailing")]   // 末尾の空白とピリオドは使えない
        [InlineData("日本語の名前", "日本語の名前")]
        [InlineData("", "_")]
        [InlineData(null, "_")]
        [InlineData("...", "_")]
        public void SanitizeForPath(string input, string expected)
        {
            Assert.Equal(expected, DynamicPathBuilder.SanitizeForPath(input));
        }

        // =========================================================
        // タグの展開
        // =========================================================
        [Fact]
        public void タグがなければそのまま返す()
        {
            var rule = new SortRule { RuleName = "r" };
            Assert.Equal(@"D:\Backup", DynamicPathBuilder.BuildPath(@"D:\Backup", "", rule));
        }

        [Fact]
        public void 空のパスはそのまま返す()
        {
            var rule = new SortRule { RuleName = "r" };
            Assert.Equal("", DynamicPathBuilder.BuildPath("", "", rule));
            Assert.Null(DynamicPathBuilder.BuildPath(null, "", rule));
        }

        [Fact]
        public void 実行日時のタグが展開される()
        {
            var rule = new SortRule { RuleName = "r" };
            string result = DynamicPathBuilder.BuildPath(@"D:\B\/YYYY\/YY\/MM\/DD", "", rule);
            var now = DateTime.Now;
            Assert.Equal($@"D:\B\{now:yyyy}\{now:yy}\{now:MM}\{now:dd}", result);
        }

        [Fact]
        public void タグは大文字小文字を区別しない()
        {
            var rule = new SortRule { RuleName = "rule1" };
            Assert.Equal(@"D:\B\rule1", DynamicPathBuilder.BuildPath(@"D:\B\/name", "", rule));
        }

        [Fact]
        public void NAMEとCONDはフォルダ名として使えない文字が置き換えられる()
        {
            var rule = new SortRule { RuleName = "a:b", SearchCondition = "*.jpg /| *.png" };
            string result = DynamicPathBuilder.BuildPath(@"D:\B\/NAME\/COND", "", rule);

            // CONDはスラッシュを除去したうえで、使えない文字(* |)を _ に置き換える
            Assert.Equal(@"D:\B\a_b\_.jpg _ _.png", result);
        }

        [Fact]
        public void ルール名にタグのような文字列があっても再展開されない()
        {
            var rule = new SortRule { RuleName = "/YYYY" };
            string result = DynamicPathBuilder.BuildPath(@"D:\B\/NAME", "", rule);

            Assert.Equal(@"D:\B\_YYYY", result); // "/" は "_" になるので、日付タグとして展開されない
        }

        [Fact]
        public void ルールがnullならNAMEとCONDは展開されない()
        {
            Assert.Equal(@"D:\B\/NAME", DynamicPathBuilder.BuildPath(@"D:\B\/NAME", "", null));
        }

        // =========================================================
        // ファイルの更新日時タグ (/FYYYY など)
        // =========================================================
        [Fact]
        public void ファイルの更新日時タグはファイルの日時で展開される()
        {
            string file = Path.Combine(_dir, "a.txt");
            File.WriteAllText(file, "x");
            File.SetLastWriteTime(file, new DateTime(2021, 3, 4, 10, 0, 0));

            var rule = new SortRule { RuleName = "r" };
            string result = DynamicPathBuilder.BuildPath(@"D:\B\/FYYYY\/FMM\/FDD", file, rule);

            Assert.Equal(@"D:\B\2021\03\04", result);
        }

        [Fact]
        public void FYYYYとFYYは混ざらずそれぞれ展開される()
        {
            string file = Path.Combine(_dir, "a.txt");
            File.WriteAllText(file, "x");
            File.SetLastWriteTime(file, new DateTime(2021, 3, 4));

            string result = DynamicPathBuilder.BuildPath(@"D:\B\/FYYYY-/FYY", file, new SortRule { RuleName = "r" });

            Assert.Equal(@"D:\B\2021-21", result);
        }

        [Fact]
        public void フォルダの更新日時でも展開される()
        {
            string dir = Path.Combine(_dir, "folder");
            Directory.CreateDirectory(dir);
            Directory.SetLastWriteTime(dir, new DateTime(2019, 12, 31));

            string result = DynamicPathBuilder.BuildPath(@"D:\B\/FYYYY\/FMM", dir, new SortRule { RuleName = "r" });

            Assert.Equal(@"D:\B\2019\12", result);
        }

        [Fact]
        public void 対象が存在しない場合のファイル日付タグは現在日時になる()
        {
            string result = DynamicPathBuilder.BuildPath(@"D:\B\/FYYYY", Path.Combine(_dir, "nothing.txt"), new SortRule { RuleName = "r" });

            Assert.Equal($@"D:\B\{DateTime.Now:yyyy}", result);
        }
    }
}
