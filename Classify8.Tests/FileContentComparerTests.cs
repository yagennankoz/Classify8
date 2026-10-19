using System;
using System.IO;
using System.Threading;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    public class FileContentComparerTests : IDisposable
    {
        private readonly string _dir;

        public FileContentComparerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "c8test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Make(string name, byte[] data)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, data);
            return path;
        }

        private static byte[] Bytes(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        [Fact]
        public void IdenticalSmallFiles_AreEqual()
        {
            var data = Bytes(1000, 1);
            Assert.True(FileContentComparer.AreContentsEqual(Make("a", data), Make("b", data)));
        }

        [Fact]
        public void IdenticalLargeFiles_AreEqual()
        {
            var data = Bytes(1024 * 1024 + 123, 2); // バッファ・末尾サンプルより大きい
            Assert.True(FileContentComparer.AreContentsEqual(Make("a", data), Make("b", data)));
        }

        [Fact]
        public void SameFile_ComparedWithItself_IsEqual()
        {
            string a = Make("a", Bytes(500_000, 9));
            Assert.True(FileContentComparer.AreContentsEqual(a, a));
        }

        [Fact]
        public void DifferentSize_AreNotEqual()
        {
            Assert.False(FileContentComparer.AreContentsEqual(Make("a", Bytes(100, 1)), Make("b", Bytes(101, 1))));
        }

        [Fact]
        public void SameSize_DifferenceAtStart_AreNotEqual()
        {
            var a = Bytes(500_000, 3);
            var b = (byte[])a.Clone();
            b[0] ^= 0xFF;
            Assert.False(FileContentComparer.AreContentsEqual(Make("a", a), Make("b", b)));
        }

        [Fact]
        public void SameSize_DifferenceInMiddle_AreNotEqual()
        {
            var a = Bytes(500_000, 4);
            var b = (byte[])a.Clone();
            b[250_000] ^= 0xFF; // 先頭にも末尾サンプルにも含まれない位置
            Assert.False(FileContentComparer.AreContentsEqual(Make("a", a), Make("b", b)));
        }

        [Fact]
        public void SameSize_DifferenceAtEnd_AreNotEqual()
        {
            var a = Bytes(500_000, 5);
            var b = (byte[])a.Clone();
            b[^1] ^= 0xFF;
            Assert.False(FileContentComparer.AreContentsEqual(Make("a", a), Make("b", b)));
        }

        [Fact]
        public void SameSize_DifferenceAtBufferBoundary_AreNotEqual()
        {
            // 内部バッファ(256KB)の境目の前後で差が出るケース
            var a = Bytes(1_000_000, 6);
            foreach (int pos in new[] { 256 * 1024 - 1, 256 * 1024, 256 * 1024 + 1 })
            {
                var b = (byte[])a.Clone();
                b[pos] ^= 0xFF;
                Assert.False(FileContentComparer.AreContentsEqual(Make("a" + pos, a), Make("b" + pos, b)), $"位置 {pos}");
            }
        }

        [Fact]
        public void EmptyFiles_AreEqual()
        {
            Assert.True(FileContentComparer.AreContentsEqual(Make("a", new byte[0]), Make("b", new byte[0])));
        }

        [Fact]
        public void MissingFile_ReturnsFalse()
        {
            string a = Make("a", Bytes(10, 1));
            Assert.False(FileContentComparer.AreContentsEqual(a, Path.Combine(_dir, "nothing")));
            Assert.False(FileContentComparer.AreContentsEqual(Path.Combine(_dir, "nothing"), a));
        }

        [Fact]
        public void FileOpenedExclusivelyByAnotherProcess_ReturnsFalse_NotThrow()
        {
            var data = Bytes(1000, 7);
            string a = Make("a", data);
            string b = Make("b", data);

            using (new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.False(FileContentComparer.AreContentsEqual(a, b));
            }
        }

        [Fact]
        public void FileBeingWrittenByAnotherProcess_CanStillBeRead()
        {
            var data = Bytes(1000, 8);
            string a = Make("a", data);
            string b = Make("b", data);

            // 書き込み用に開かれていても(共有で)、読み込んで比較できる
            using (new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                Assert.True(FileContentComparer.AreContentsEqual(a, b));
            }
        }

        [Fact]
        public void Cancellation_ThrowsOperationCanceledException()
        {
            var data = Bytes(1_000_000, 10);
            string a = Make("a", data);
            string b = Make("b", data);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => FileContentComparer.AreContentsEqual(a, b, cts.Token));
        }
    }
}
