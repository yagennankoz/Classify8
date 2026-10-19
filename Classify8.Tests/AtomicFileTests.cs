using System;
using System.IO;
using System.Linq;
using System.Text;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    public class AtomicFileTests : IDisposable
    {
        private readonly string _dir;

        public AtomicFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "c8atomic_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void 新規ファイルを作成できる()
        {
            string path = Path.Combine(_dir, "new.txt");

            AtomicFile.WriteAllText(path, "hello");

            Assert.Equal("hello", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void 既存ファイルを何度でも上書きできる()
        {
            string path = Path.Combine(_dir, "a.txt");

            AtomicFile.WriteAllText(path, "one");
            AtomicFile.WriteAllText(path, "two");
            AtomicFile.WriteAllText(path, "three");

            Assert.Equal("three", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(_dir)); // 一時ファイルが残らない
        }

        [Fact]
        public void 既定はBOMなしのUTF8_指定すればBOM付きにもできる()
        {
            string noBom = Path.Combine(_dir, "nobom.txt");
            string withBom = Path.Combine(_dir, "bom.txt");

            AtomicFile.WriteAllText(noBom, "あ");
            AtomicFile.WriteAllText(withBom, "あ", new UTF8Encoding(true));

            Assert.NotEqual(0xEF, File.ReadAllBytes(noBom)[0]);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(withBom).Take(3).ToArray());
        }

        [Fact]
        public void 日本語や改行を含む内容も正しく保存される()
        {
            string path = Path.Combine(_dir, "jp.txt");
            const string text = "日本語\r\n2行目 \"引用符\",カンマ";

            AtomicFile.WriteAllText(path, text);

            Assert.Equal(text, File.ReadAllText(path));
        }

        [Fact]
        public void 上書き先が他のアプリに掴まれていると例外になり_元の内容は壊れない()
        {
            string path = Path.Combine(_dir, "locked.txt");
            AtomicFile.WriteAllText(path, "original");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => AtomicFile.WriteAllText(path, "new content"));
            }

            Assert.Equal("original", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp")); // 失敗しても一時ファイルは残らない
        }
    }
}
