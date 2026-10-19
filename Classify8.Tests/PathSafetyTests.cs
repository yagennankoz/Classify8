using System;
using System.IO;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    public class PathSafetyTests
    {
        [Fact]
        public void ドライブ直下をサブフォルダごと検索する設定は危険()
        {
            Assert.True(PathSafety.IsDangerousSource(@"C:\", true, out string reason));
            Assert.False(string.IsNullOrEmpty(reason));

            Assert.True(PathSafety.IsDangerousSource(@"d:\", true, out _));   // 小文字でも同じ
        }

        [Fact]
        public void ドライブ直下でもサブフォルダを検索しないなら許可される()
        {
            Assert.False(PathSafety.IsDangerousSource(@"D:\", false, out _));
        }

        [Fact]
        public void ネットワークの共有フォルダ直下も同様に扱う()
        {
            Assert.True(PathSafety.IsDangerousSource(@"\\server\share", true, out _));
            Assert.False(PathSafety.IsDangerousSource(@"\\server\share\Downloads", true, out _));
        }

        [Fact]
        public void Windowsフォルダとその配下は常に危険()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            Assert.True(PathSafety.IsDangerousSource(windows, false, out string reason));
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.True(PathSafety.IsDangerousSource(Path.Combine(windows, "Temp"), true, out _));
            Assert.True(PathSafety.IsDangerousSource(windows + @"\", false, out _));      // 末尾の \ 付き
            Assert.True(PathSafety.IsDangerousSource(windows.ToLowerInvariant(), false, out _)); // 小文字
        }

        [Fact]
        public void ProgramFilesとその配下は危険()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(programFiles)) return;

            Assert.True(PathSafety.IsDangerousSource(programFiles, false, out _));
            Assert.True(PathSafety.IsDangerousSource(Path.Combine(programFiles, "Any App"), true, out _));
        }

        [Fact]
        public void システムフォルダと名前が似ているだけのフォルダは危険ではない()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            Assert.False(PathSafety.IsDangerousSource(windows + "_backup", true, out _));
        }

        [Theory]
        [InlineData(@"D:\Downloads")]
        [InlineData(@"C:\Users\someone\Downloads")]
        [InlineData(@"D:\Work\2026")]
        public void 通常のフォルダは許可される(string path)
        {
            Assert.False(PathSafety.IsDangerousSource(path, true, out _));
            Assert.False(PathSafety.IsDangerousSource(path, false, out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void 空のパスは判定対象外(string path)
        {
            Assert.False(PathSafety.IsDangerousSource(path, true, out _));
        }
    }
}
