using System;
using System.IO;
using Xunit;
using Classify8.Core;

namespace Classify8.Tests
{
    public class SortRuleTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _testFilePath;

        public SortRuleTests()
        {
            // 他のテストと衝突しないよう、テストごとに専用のフォルダを使う
            _tempDir = Path.Combine(Path.GetTempPath(), "Classify8_RuleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _testFilePath = Path.Combine(_tempDir, "test.txt");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private string CreateFile(string name, int size, DateTime? time = null)
        {
            string path = Path.Combine(_tempDir, name);
            File.WriteAllBytes(path, new byte[size]);
            if (time.HasValue) File.SetLastWriteTime(path, time.Value);
            return path;
        }

        // =========================================================
        // ファイルサイズ限定
        // =========================================================
        [Fact]
        public void ファイルサイズ限定テスト_1MB以上のファイルを除外する()
        {
            // 2MBのダミーファイルを作成
            File.WriteAllBytes(_testFilePath, new byte[2 * 1024 * 1024]);

            var rule = new SortRule
            {
                SearchCondition = "*.txt",
                SizeMax_Enabled = true,
                SizeMax_Value = 1,
                SizeMax_Unit = SizeUnit.MB // 1MB 以上のファイルを除外する設定
            };

            // 2MBのファイルなので、条件に合わず false になるはず
            Assert.False(rule.IsMatch(_testFilePath, isDirectory: false));
        }

        [Fact]
        public void ファイルサイズ限定テスト_上限未満のファイルは対象になる()
        {
            string path = CreateFile("small.txt", 500 * 1024);
            var rule = new SortRule
            {
                SearchCondition = "*.txt",
                SizeMax_Enabled = true,
                SizeMax_Value = 1,
                SizeMax_Unit = SizeUnit.MB
            };

            Assert.True(rule.IsMatch(path, isDirectory: false));
        }

        [Fact]
        public void ファイルサイズ限定テスト_下限以下のファイルを除外する()
        {
            string tiny = CreateFile("tiny.txt", 10);
            string big = CreateFile("big.txt", 2048);
            var rule = new SortRule
            {
                SearchCondition = "*.txt",
                SizeMin_Enabled = true,
                SizeMin_Value = 1,
                SizeMin_Unit = SizeUnit.KB // 1KB 以下のファイルを除外する設定
            };

            Assert.False(rule.IsMatch(tiny, isDirectory: false));
            Assert.True(rule.IsMatch(big, isDirectory: false));
        }

        // =========================================================
        // タイムスタンプ限定
        // =========================================================
        [Fact]
        public void タイムスタンプ限定テスト_7日以上前のファイルを除外する()
        {
            File.WriteAllText(_testFilePath, "dummy");

            // ファイルの更新日時を「10日前」に改ざん
            File.SetLastWriteTime(_testFilePath, DateTime.Now.AddDays(-10));

            var rule = new SortRule
            {
                SearchCondition = "*.txt",
                TimeBefore_Enabled = true,
                TimeBefore_Value = 7,
                TimeBefore_Unit = TimeUnit.Day // 7日以前を除外する設定
            };

            // 10日前のファイルなので false になるはず
            Assert.False(rule.IsMatch(_testFilePath, isDirectory: false));
        }

        [Fact]
        public void タイムスタンプ限定テスト_相対指定の以降を除外する()
        {
            string fresh = CreateFile("fresh.txt", 1, DateTime.Now);
            string old = CreateFile("old.txt", 1, DateTime.Now.AddDays(-10));
            var rule = new SortRule
            {
                SearchCondition = "*.txt",
                TimeAfter_Enabled = true,
                TimeAfter_Value = 7,
                TimeAfter_Unit = TimeUnit.Day // 7日以内(以降)のファイルを除外する設定
            };

            Assert.False(rule.IsMatch(fresh, isDirectory: false));
            Assert.True(rule.IsMatch(old, isDirectory: false));
        }

        [Fact]
        public void タイムスタンプ限定テスト_絶対日付での除外()
        {
            string older = CreateFile("older.txt", 1, DateTime.Today.AddDays(-10));
            string newer = CreateFile("newer.txt", 1, DateTime.Today.AddDays(-1));

            var before = new SortRule
            {
                SearchCondition = "*.txt",
                DateBefore_Enabled = true,
                DateBefore_Date = DateTime.Today.AddDays(-5) // 5日前以前を除外
            };
            Assert.False(before.IsMatch(older, isDirectory: false));
            Assert.True(before.IsMatch(newer, isDirectory: false));

            var after = new SortRule
            {
                SearchCondition = "*.txt",
                DateAfter_Enabled = true,
                DateAfter_Date = DateTime.Today.AddDays(-5) // 5日前以降を除外
            };
            Assert.True(after.IsMatch(older, isDirectory: false));
            Assert.False(after.IsMatch(newer, isDirectory: false));
        }

        // =========================================================
        // 名前の条件・種類
        // =========================================================
        [Fact]
        public void 名前が条件に合わなければサイズが範囲内でも対象外()
        {
            string path = CreateFile("photo.jpg", 100);
            var rule = new SortRule { SearchCondition = "*.txt" };

            Assert.False(rule.IsMatch(path, isDirectory: false));
        }

        [Fact]
        public void フォルダはサイズ条件の対象外で名前だけで判定される()
        {
            string dir = Path.Combine(_tempDir, "請求書_2026");
            Directory.CreateDirectory(dir);

            var rule = new SortRule
            {
                SearchCondition = "請求書",
                SizeMax_Enabled = true,
                SizeMax_Value = 0,
                SizeMax_Unit = SizeUnit.Byte // ファイルならすべて除外される設定
            };

            Assert.True(rule.IsMatch(dir, isDirectory: true));
            Assert.False(new SortRule { SearchCondition = "見積書" }.IsMatch(dir, isDirectory: true));
        }

        [Fact]
        public void FileSystemInfoでの判定はパスでの判定と一致する()
        {
            string small = CreateFile("a.txt", 10, DateTime.Now.AddDays(-10));
            string big = CreateFile("b.txt", 5000, DateTime.Now);
            string dir = Path.Combine(_tempDir, "folder.txt");
            Directory.CreateDirectory(dir);

            var rules = new[]
            {
                new SortRule { SearchCondition = "*.txt" },
                new SortRule { SearchCondition = "a", SizeMin_Enabled = true, SizeMin_Value = 1, SizeMin_Unit = SizeUnit.KB },
                new SortRule { SearchCondition = "*.txt", TimeBefore_Enabled = true, TimeBefore_Value = 7, TimeBefore_Unit = TimeUnit.Day },
                new SortRule { SearchCondition = "folder" },
            };

            foreach (var rule in rules)
            {
                Assert.Equal(rule.IsMatch(small, false), rule.IsMatch(new FileInfo(small)));
                Assert.Equal(rule.IsMatch(big, false), rule.IsMatch(new FileInfo(big)));
                Assert.Equal(rule.IsMatch(dir, true), rule.IsMatch(new DirectoryInfo(dir)));
            }
        }

        // =========================================================
        // 表記ゆれの吸収が、実際のファイル判定に反映されること
        // =========================================================
        [Fact]
        public void 全角半角を区別しない設定がファイル判定に反映される()
        {
            string path = CreateFile("ＡＢＣ１２３.txt", 1);

            Assert.True(new SortRule { SearchCondition = "ABC123", IgnoreWidth = true }.IsMatch(path, false));
            Assert.False(new SortRule { SearchCondition = "ABC123", IgnoreWidth = false }.IsMatch(path, false));
        }

        [Fact]
        public void ひらがなカタカナを区別しない設定がファイル判定に反映される()
        {
            string path = CreateFile("せいきゅうしょ.pdf", 1);

            Assert.True(new SortRule { SearchCondition = "セイキュウショ", IgnoreKana = true }.IsMatch(path, false));
            Assert.False(new SortRule { SearchCondition = "セイキュウショ", IgnoreKana = false }.IsMatch(path, false));
        }

        [Fact]
        public void 大文字小文字を区別する設定がファイル判定に反映される()
        {
            string path = CreateFile("IMAGE.JPG", 1);

            Assert.True(new SortRule { SearchCondition = "*.jpg", IgnoreCase = true }.IsMatch(path, false));
            Assert.False(new SortRule { SearchCondition = "*.jpg", IgnoreCase = false }.IsMatch(path, false));
        }

        // =========================================================
        // 条件のコンパイルとコピー
        // =========================================================
        [Fact]
        public void 構文エラーの条件では判定時に例外になる()
        {
            string path = CreateFile("a.txt", 1);
            var rule = new SortRule { SearchCondition = "/( a" };

            Assert.Throws<FormatException>(() => rule.IsMatch(path, false));
        }

        [Fact]
        public void 条件を変更して再コンパイルすると新しい条件で判定される()
        {
            string path = CreateFile("a.txt", 1);
            var rule = new SortRule { SearchCondition = "a" };
            Assert.True(rule.IsMatch(path, false));

            rule.SearchCondition = "zzz";
            rule.CompileCondition();
            Assert.False(rule.IsMatch(path, false));
        }

        [Fact]
        public void Cloneは新しいIDを持ち_コンパイル済み条件は引き継がない()
        {
            var rule = new SortRule { RuleName = "r", SearchCondition = "a", IsMoveAction = false, SizeMin_Value = 1.5 };
            rule.CompileCondition();

            var clone = rule.Clone();

            Assert.NotEqual(rule.Id, clone.Id);
            Assert.Equal("r", clone.RuleName);
            Assert.False(clone.IsMoveAction);
            Assert.Equal(1.5, clone.SizeMin_Value);
            Assert.Null(clone.CompiledCondition); // 設定を変更される可能性があるため、使用時に再コンパイルされる
            Assert.NotNull(rule.CompiledCondition);
        }

        // =========================================================
        // 濁点・半濁点の表記ゆれ (「が」と「か＋結合用濁点」)
        // Mac で作られたファイル名などは、濁点が分かれた形 (U+304B U+3099) で保存されていることがある
        // =========================================================
        private const string GaComposed = "が";            // が (1文字)
        private const string GaDecomposed = "か\u3099";    // か + 結合用濁点

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void 濁点が分かれたファイル名も_1文字の濁点の検索条件に一致する(bool ignoreWidth, bool ignoreKana)
        {
            string path = CreateFile("まん" + GaDecomposed + ".txt", 1); // まんが (濁点が分かれた形)
            var rule = new SortRule { SearchCondition = "まん" + GaComposed, IgnoreWidth = ignoreWidth, IgnoreKana = ignoreKana };

            Assert.True(rule.IsMatch(path, false));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void 濁点が分かれた検索条件も_1文字の濁点のファイル名に一致する(bool ignoreWidth, bool ignoreKana)
        {
            string path = CreateFile("まん" + GaComposed + ".txt", 1);
            var rule = new SortRule { SearchCondition = "まん" + GaDecomposed, IgnoreWidth = ignoreWidth, IgnoreKana = ignoreKana };

            Assert.True(rule.IsMatch(path, false));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void 半濁点が分かれたファイル名も一致する(bool ignoreWidth)
        {
            // プロジェクト (プ = フ + U+309A、ジ = シ + U+3099)
            string path = CreateFile("フ\u309Aロシ\u3099ェクト.txt", 1);
            var rule = new SortRule { SearchCondition = "プロジェクト", IgnoreWidth = ignoreWidth, IgnoreKana = false };

            Assert.True(rule.IsMatch(path, false));
        }

        [Fact]
        public void ひらがなカタカナを区別しない場合_濁点が分かれたひらがなもカタカナに一致する()
        {
            string path = CreateFile("まん" + GaDecomposed + ".txt", 1); // まんが (濁点が分かれた形)
            var rule = new SortRule { SearchCondition = "マンガ", IgnoreWidth = false, IgnoreKana = true }; // マンガ

            Assert.True(rule.IsMatch(path, false));
        }

        [Fact]
        public void 濁点の別表現をそろえても_別の文字とは一致しない()
        {
            string path = CreateFile("まん" + GaDecomposed + ".txt", 1); // まんが
            var rule = new SortRule { SearchCondition = "まんか.", IgnoreWidth = false, IgnoreKana = false }; // 「まんか.」

            Assert.False(rule.IsMatch(path, false));
        }
    }
}
