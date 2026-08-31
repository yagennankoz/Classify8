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
            _tempDir = Path.Combine(Path.GetTempPath(), "Classify8_RuleTests");
            Directory.CreateDirectory(_tempDir);
            _testFilePath = Path.Combine(_tempDir, "test.txt");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

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
    }
}