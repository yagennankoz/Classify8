using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    [Collection("DataFiles")]
    public class DataManagerTests : DataFileTestBase
    {
        // 全項目に「既定値とは違う値」を入れたルール
        private static SortRule FullRule(string name = "ルール,1 \"引用符\"")
        {
            return new SortRule
            {
                Id = Guid.NewGuid().ToString(),
                RuleName = name,
                IsEnabled = false,
                SearchCondition = "/( 請求書 /| 見積書 /) /& /! 破棄",
                SourceMode = "Preset",
                SourcePresetId = "preset-src",
                SourceCustomPath = @"C:\Src, with comma",
                SearchSubDirectories = true,
                DestMode = "Preset",
                DestPresetId = "preset-dest",
                DestCustomPath = @"D:\Dest\/YYYY\/NAME",
                IsMoveAction = false,
                IgnoreCase = false,
                IgnoreWidth = false,
                IgnoreKana = false,
                DoNotSaveHistory = true,
                TargetType = TargetType.Both,
                SizeMin_Enabled = true, SizeMin_Value = 1.5, SizeMin_Unit = SizeUnit.MB,
                SizeMax_Enabled = true, SizeMax_Value = 250.25, SizeMax_Unit = SizeUnit.KB,
                DateBefore_Enabled = true, DateBefore_Date = new DateTime(2020, 1, 2),
                DateAfter_Enabled = true, DateAfter_Date = new DateTime(2030, 12, 31),
                TimeBefore_Enabled = true, TimeBefore_Value = 7, TimeBefore_Unit = TimeUnit.Week,
                TimeAfter_Enabled = true, TimeAfter_Value = 3, TimeAfter_Unit = TimeUnit.Month,
            };
        }

        private static void AssertRulesEqual(SortRule expected, SortRule actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.RuleName, actual.RuleName);
            Assert.Equal(expected.IsEnabled, actual.IsEnabled);
            Assert.Equal(expected.SearchCondition, actual.SearchCondition);
            Assert.Equal(expected.SourceMode, actual.SourceMode);
            Assert.Equal(expected.SourcePresetId, actual.SourcePresetId);
            Assert.Equal(expected.SourceCustomPath, actual.SourceCustomPath);
            Assert.Equal(expected.SearchSubDirectories, actual.SearchSubDirectories);
            Assert.Equal(expected.DestMode, actual.DestMode);
            Assert.Equal(expected.DestPresetId, actual.DestPresetId);
            Assert.Equal(expected.DestCustomPath, actual.DestCustomPath);
            Assert.Equal(expected.IsMoveAction, actual.IsMoveAction);
            Assert.Equal(expected.IgnoreCase, actual.IgnoreCase);
            Assert.Equal(expected.IgnoreWidth, actual.IgnoreWidth);
            Assert.Equal(expected.IgnoreKana, actual.IgnoreKana);
            Assert.Equal(expected.DoNotSaveHistory, actual.DoNotSaveHistory);
            Assert.Equal(expected.TargetType, actual.TargetType);
            Assert.Equal(expected.SizeMin_Enabled, actual.SizeMin_Enabled);
            Assert.Equal(expected.SizeMin_Value, actual.SizeMin_Value);
            Assert.Equal(expected.SizeMin_Unit, actual.SizeMin_Unit);
            Assert.Equal(expected.SizeMax_Enabled, actual.SizeMax_Enabled);
            Assert.Equal(expected.SizeMax_Value, actual.SizeMax_Value);
            Assert.Equal(expected.SizeMax_Unit, actual.SizeMax_Unit);
            Assert.Equal(expected.DateBefore_Enabled, actual.DateBefore_Enabled);
            Assert.Equal(expected.DateBefore_Date, actual.DateBefore_Date);
            Assert.Equal(expected.DateAfter_Enabled, actual.DateAfter_Enabled);
            Assert.Equal(expected.DateAfter_Date, actual.DateAfter_Date);
            Assert.Equal(expected.TimeBefore_Enabled, actual.TimeBefore_Enabled);
            Assert.Equal(expected.TimeBefore_Value, actual.TimeBefore_Value);
            Assert.Equal(expected.TimeBefore_Unit, actual.TimeBefore_Unit);
            Assert.Equal(expected.TimeAfter_Enabled, actual.TimeAfter_Enabled);
            Assert.Equal(expected.TimeAfter_Value, actual.TimeAfter_Value);
            Assert.Equal(expected.TimeAfter_Unit, actual.TimeAfter_Unit);
        }

        // =========================================================
        // ルール (CSV)
        // =========================================================
        [Fact]
        public void ルールは保存して読み込むと全項目が元に戻る()
        {
            var rules = new[] { FullRule("ルール1"), FullRule("ルール2") };

            DataManager.SaveRules(rules);
            var loaded = DataManager.LoadRules();

            Assert.Equal(2, loaded.Count);
            AssertRulesEqual(rules[0], loaded[0]);
            AssertRulesEqual(rules[1], loaded[1]);
            Assert.Empty(ReportedErrors);
        }

        [Fact]
        public void ルールが0件でも保存と読み込みができる()
        {
            DataManager.SaveRules(new List<SortRule>());

            Assert.Empty(DataManager.LoadRules());
        }

        [Fact]
        public void ルールが無ければ空のリストになる()
        {
            Assert.Empty(DataManager.LoadRules());
        }

        [Theory]
        [InlineData("カンマ,を含む")]
        [InlineData("引用符\"を含む")]
        [InlineData("\"\"")]
        [InlineData("日本語・記号 !@#$%^&*()")]
        [InlineData("改行を\r\n含む")]
        [InlineData("改行を\n含む")]
        [InlineData(",,,")]
        public void カンマや引用符や改行を含む文字列も壊れずに保存される(string text)
        {
            var rule = FullRule("name");
            rule.SearchCondition = text;
            rule.SourceCustomPath = text;
            var second = FullRule("後続の行");

            DataManager.SaveRules(new[] { rule, second });
            var loaded = DataManager.LoadRules();

            Assert.Equal(2, loaded.Count);                 // 改行を含んでも、行がずれない
            Assert.Equal(text, loaded[0].SearchCondition);
            Assert.Equal(text, loaded[0].SourceCustomPath);
            Assert.Equal("後続の行", loaded[1].RuleName);
        }

        [Fact]
        public void 保存したCSVはUTF8のBOM付きで保存される()
        {
            DataManager.SaveRules(new[] { FullRule("r") });

            var bytes = File.ReadAllBytes(PathOf("rules.csv"));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }

        [Fact]
        public void 保存を繰り返しても一時ファイルは残らない()
        {
            for (int i = 0; i < 3; i++) DataManager.SaveRules(new[] { FullRule("r" + i) });

            Assert.Empty(Directory.GetFiles(DataDir, "*.tmp"));
            Assert.Equal("r2", DataManager.LoadRules().Single().RuleName);
        }

        [Fact]
        public async Task 引用符が閉じていない壊れたCSVでも固まらずに読み込みが終わる()
        {
            File.WriteAllText(PathOf("rules.csv"), "RuleName,IsEnabled\n\"abc,def\nxyz\n");

            var task = Task.Run(() => DataManager.LoadRules());
            var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(task, finished); // 10秒以内に終わらなければ、無限ループの疑い
            Assert.Empty(await task);    // 列数が足りない行は読み飛ばされる
        }

        [Fact]
        public void 列数が足りない行は読み飛ばされ_正しい行は読み込まれる()
        {
            var good = FullRule("正しい行");
            DataManager.SaveRules(new[] { good });

            // 壊れた行を追記する
            File.AppendAllText(PathOf("rules.csv"), "壊れた行,True,x\r\n\r\n");

            var loaded = DataManager.LoadRules();
            Assert.Single(loaded);
            Assert.Equal("正しい行", loaded[0].RuleName);
        }

        [Fact]
        public void 旧形式のID列がないCSVでもIDが自動で割り当てられる()
        {
            DataManager.SaveRules(new[] { FullRule("r") });

            // 最終列(Id)を取り除いたCSVにする
            var lines = File.ReadAllLines(PathOf("rules.csv"));
            File.WriteAllLines(PathOf("rules.csv"),
                lines.Select(l => l.Substring(0, l.LastIndexOf(','))).ToArray());

            var loaded = DataManager.LoadRules();
            Assert.Single(loaded);
            Assert.False(string.IsNullOrWhiteSpace(loaded[0].Id));
        }

        [Fact]
        public void 数値と日付は実行環境のカルチャに依存せず読み書きされる()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                // 小数点にカンマを使う文化圏で保存・読み込みしても、値が変わらないこと
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var rule = FullRule("r");

                DataManager.SaveRules(new[] { rule });
                var loaded = DataManager.LoadRules().Single();

                Assert.Equal(1.5, loaded.SizeMin_Value);
                Assert.Equal(250.25, loaded.SizeMax_Value);
                Assert.Equal(new DateTime(2020, 1, 2), loaded.DateBefore_Date);
                Assert.Contains("1.5", File.ReadAllText(PathOf("rules.csv")));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        // =========================================================
        // 全体設定 (JSON)
        // =========================================================
        [Fact]
        public void 全体設定は保存して読み込むと全項目が元に戻る()
        {
            var settings = new AppSettings
            {
                SameSizeResolution = ConflictResolution.KeepOlder,
                DiffSizeResolution = ConflictResolution.Skip,
                CompareContentWhenTimestampDiffers = false,
                ContentCompareMaxSizeMB = 512,
                GlobalNgKeywords = "破棄\r\nテスト用",
                EnablePeriodicExecution = true,
                PeriodicIntervalMinutes = 15,
                MoveToRecycleBin = false,
                AutoCreateDestFolder = false,
                StartMinimized = true,
                SilentReadOnly = false,
                ConfirmBeforeDoubleClickRun = true,
                LimitHistoryCount = false,
                HistoryLimitHundreds = 25,
                ProcessPriority = ProcessPriorityClass.BelowNormal
            };

            DataManager.SaveSettings(settings);
            var loaded = DataManager.LoadSettings();

            Assert.Equal(ConflictResolution.KeepOlder, loaded.SameSizeResolution);
            Assert.Equal(ConflictResolution.Skip, loaded.DiffSizeResolution);
            Assert.False(loaded.CompareContentWhenTimestampDiffers);
            Assert.Equal(512, loaded.ContentCompareMaxSizeMB);
            Assert.Equal("破棄\r\nテスト用", loaded.GlobalNgKeywords);
            Assert.True(loaded.EnablePeriodicExecution);
            Assert.Equal(15, loaded.PeriodicIntervalMinutes);
            Assert.False(loaded.MoveToRecycleBin);
            Assert.False(loaded.AutoCreateDestFolder);
            Assert.True(loaded.StartMinimized);
            Assert.False(loaded.SilentReadOnly);
            Assert.True(loaded.ConfirmBeforeDoubleClickRun);
            Assert.False(loaded.LimitHistoryCount);
            Assert.Equal(25, loaded.HistoryLimitHundreds);
            Assert.Equal(ProcessPriorityClass.BelowNormal, loaded.ProcessPriority);
        }

        [Fact]
        public void 設定ファイルが無ければ既定値になる()
        {
            var loaded = DataManager.LoadSettings();

            Assert.True(loaded.CompareContentWhenTimestampDiffers);   // 内容比較は既定でON
            Assert.Equal(0, loaded.ContentCompareMaxSizeMB);           // 既定は無制限
            Assert.False(loaded.ConfirmBeforeDoubleClickRun);         // 確認ダイアログは既定でOFF
            Assert.Equal(ConflictResolution.Rename, loaded.SameSizeResolution);
            Assert.Empty(ReportedErrors);
        }

        [Fact]
        public void 新しい項目を含まない古い設定ファイルでも_不足分は既定値で読み込める()
        {
            File.WriteAllText(PathOf("settings.json"),
                "{ \"SameSizeResolution\": \"Skip\", \"GlobalNgKeywords\": \"abc\" }");

            var loaded = DataManager.LoadSettings();

            Assert.Equal(ConflictResolution.Skip, loaded.SameSizeResolution);
            Assert.Equal("abc", loaded.GlobalNgKeywords);
            Assert.True(loaded.CompareContentWhenTimestampDiffers);
            Assert.False(loaded.ConfirmBeforeDoubleClickRun);
            Assert.Empty(ReportedErrors);
        }

        [Fact]
        public void 壊れた設定ファイルは既定値で起動し_エラーが通知される()
        {
            File.WriteAllText(PathOf("settings.json"), "{ これはJSONではない");

            var loaded = DataManager.LoadSettings();

            Assert.NotNull(loaded);
            Assert.True(loaded.CompareContentWhenTimestampDiffers);
            var message = Assert.Single(ReportedErrors);
            Assert.Contains("設定の読み込みに失敗しました", message);
            Assert.True(File.Exists(PathOf("error.log"))); // 原因を後から調べられるようログにも残る
        }

        [Fact]
        public void 設定のCloneは元と独立している()
        {
            var original = new AppSettings { GlobalNgKeywords = "a", PeriodicIntervalMinutes = 10 };

            var clone = original.Clone();
            clone.GlobalNgKeywords = "b";
            clone.PeriodicIntervalMinutes = 99;

            Assert.Equal("a", original.GlobalNgKeywords);
            Assert.Equal(10, original.PeriodicIntervalMinutes);
        }

        // =========================================================
        // プリセット (JSON)
        // =========================================================
        [Fact]
        public void プリセットは保存して読み込むと元に戻る()
        {
            var presets = new[]
            {
                new Preset { AliasName = "メイン画像フォルダ", DirectoryPath = @"D:\Backup\Images" },
                new Preset { AliasName = "ダウンロード", DirectoryPath = @"C:\Users\x\Downloads" },
            };

            DataManager.SavePresets(presets);
            var loaded = DataManager.LoadPresets();

            Assert.Equal(2, loaded.Count);
            Assert.Equal(presets[0].Id, loaded[0].Id);
            Assert.Equal("メイン画像フォルダ", loaded[0].AliasName);
            Assert.Equal(@"D:\Backup\Images", loaded[0].DirectoryPath);
            Assert.Equal(presets[1].Id, loaded[1].Id);
        }

        [Fact]
        public void プリセットファイルが無ければ空のリストになる()
        {
            Assert.Empty(DataManager.LoadPresets());
        }

        [Fact]
        public void 壊れたプリセットファイルは空のリストになり_エラーが通知される()
        {
            File.WriteAllText(PathOf("presets.json"), "[ broken");

            Assert.Empty(DataManager.LoadPresets());
            Assert.Single(ReportedErrors);
        }

        // =========================================================
        // 保存先
        // =========================================================
        [Fact]
        public void データは差し替えた保存先に作られる()
        {
            DataManager.SaveSettings(new AppSettings());
            DataManager.SavePresets(new List<Preset>());
            DataManager.SaveRules(new List<SortRule>());

            Assert.True(File.Exists(PathOf("settings.json")));
            Assert.True(File.Exists(PathOf("presets.json")));
            Assert.True(File.Exists(PathOf("rules.csv")));
        }
    }
}
