using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    [Collection("DataFiles")]
    public class HistoryManagerTests : DataFileTestBase
    {
        private static readonly DateTime Base = new DateTime(2026, 1, 1, 0, 0, 0);

        private static SortHistory Record(int minute) => new SortHistory
        {
            Timestamp = Base.AddMinutes(minute),
            RuleId = "rule",
            RuleName = "ルール",
            FileName = $"f{minute}.txt",
            SourceDir = @"C:\Src",
            DestDir = @"D:\Dest",
            Status = "移動"
        };

        private static AppSettings Unlimited() => new AppSettings { LimitHistoryCount = false };
        private static AppSettings Limited(int hundreds) => new AppSettings { LimitHistoryCount = true, HistoryLimitHundreds = hundreds };

        [Fact]
        public void 履歴ファイルが無ければ空のリストになる()
        {
            Assert.Empty(HistoryManager.LoadHistories());
        }

        [Fact]
        public void 追加した履歴は保存され_読み込める()
        {
            HistoryManager.AddHistories(new[] { Record(1) }, Unlimited());

            var loaded = HistoryManager.LoadHistories();
            var h = Assert.Single(loaded);
            Assert.Equal("f1.txt", h.FileName);
            Assert.Equal("ルール", h.RuleName);
            Assert.Equal(Base.AddMinutes(1), h.Timestamp);
        }

        [Fact]
        public void 追加は既存の履歴を維持する()
        {
            HistoryManager.AddHistories(new[] { Record(1) }, Unlimited());
            HistoryManager.AddHistories(new[] { Record(2), Record(3) }, Unlimited());

            Assert.Equal(new[] { "f1.txt", "f2.txt", "f3.txt" }, HistoryManager.LoadHistories().Select(h => h.FileName));
        }

        [Fact]
        public void 並列処理で順不同に集まった記録も_時系列に並べて保存される()
        {
            HistoryManager.AddHistories(new[] { Record(30), Record(10), Record(20) }, Unlimited());

            Assert.Equal(new[] { "f10.txt", "f20.txt", "f30.txt" }, HistoryManager.LoadHistories().Select(h => h.FileName));
        }

        [Fact]
        public void 件数上限を超えると古いものから切り捨てられる()
        {
            var records = Enumerable.Range(0, 150).Select(Record).ToList();

            HistoryManager.AddHistories(records, Limited(1)); // 1(百件) = 100件まで

            var loaded = HistoryManager.LoadHistories();
            Assert.Equal(100, loaded.Count);
            Assert.Equal("f50.txt", loaded.First().FileName);  // 古い50件(0〜49)が消える
            Assert.Equal("f149.txt", loaded.Last().FileName);
        }

        [Fact]
        public void 件数上限は複数回の追加をまたいで保たれる()
        {
            HistoryManager.AddHistories(Enumerable.Range(0, 80).Select(Record), Limited(1));
            HistoryManager.AddHistories(Enumerable.Range(80, 80).Select(Record), Limited(1));

            var loaded = HistoryManager.LoadHistories();
            Assert.Equal(100, loaded.Count);
            Assert.Equal("f60.txt", loaded.First().FileName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void 上限の設定値が0や負数でも_履歴が全消去されない(int hundreds)
        {
            var records = Enumerable.Range(0, 150).Select(Record).ToList();

            HistoryManager.AddHistories(records, Limited(hundreds));

            Assert.Equal(100, HistoryManager.LoadHistories().Count); // 最小の1(百件)として扱う
        }

        [Fact]
        public void 上限をOFFにすると全件保持される()
        {
            HistoryManager.AddHistories(Enumerable.Range(0, 250).Select(Record), Unlimited());

            Assert.Equal(250, HistoryManager.LoadHistories().Count);
        }

        [Fact]
        public void 壊れた履歴ファイルは退避され_新規に履歴が始まる()
        {
            File.WriteAllText(PathOf("history.json"), "{ broken json");

            HistoryManager.AddHistories(new[] { Record(1) }, Unlimited());

            // 新しい履歴は正常に保存される
            Assert.Equal("f1.txt", Assert.Single(HistoryManager.LoadHistories()).FileName);
            // 壊れたファイルは別名で残る (中身を調べたり復旧したりできる)
            Assert.True(File.Exists(PathOf("history.json.corrupt")));
            Assert.Equal("{ broken json", File.ReadAllText(PathOf("history.json.corrupt")));
            // 原因はログにも残る
            Assert.True(File.Exists(PathOf("error.log")));
        }

        [Fact]
        public void 履歴ファイルが他のアプリに掴まれて読めないときは_例外になり既存の履歴は失われない()
        {
            HistoryManager.AddHistories(new[] { Record(1), Record(2) }, Unlimited());

            using (new FileStream(PathOf("history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // 追記は失敗する (読めないのに空として扱って上書きしない)
                Assert.ThrowsAny<IOException>(() => HistoryManager.AddHistories(new[] { Record(3) }, Unlimited()));

                // 閲覧用の読み込みは、例外にせず空のリストを返す
                Assert.Empty(HistoryManager.LoadHistories());
            }

            // ロックが解除されたら、元の履歴がそのまま残っている
            Assert.Equal(new[] { "f1.txt", "f2.txt" }, HistoryManager.LoadHistories().Select(h => h.FileName));
        }

        [Fact]
        public void 保存を繰り返しても一時ファイルは残らない()
        {
            for (int i = 0; i < 3; i++) HistoryManager.AddHistories(new[] { Record(i) }, Unlimited());

            Assert.Empty(Directory.GetFiles(DataDir, "*.tmp"));
        }

        [Fact]
        public void SaveHistoriesは渡された内容をそのまま保存する()
        {
            HistoryManager.SaveHistories(new List<SortHistory> { Record(5), Record(6) }, Unlimited());

            Assert.Equal(2, HistoryManager.LoadHistories().Count);
        }
    }
}
