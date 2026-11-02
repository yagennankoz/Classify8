using System;
using System.IO;
using System.Reflection;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    public class RuntimeFileCheckTests : IDisposable
    {
        private readonly string _root;

        public RuntimeFileCheckTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "c8rt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string CreateDir(string name, params string[] files)
        {
            string dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "x");
            return dir;
        }

        [Fact]
        public void ファイルが揃っていれば欠けなしと判定する()
        {
            string a = CreateDir("a", "A.dll", "B.dll");
            string b = CreateDir("b", "C.dll");

            var check = RuntimeFileCheck.Capture(new[] { a, b });

            Assert.Equal(3, check.FileCount);
            Assert.True(check.IsIntact);
            Assert.Null(check.FindMissingFile());
        }

        [Fact]
        public void 記録したDLLが消えると欠けを検出する()
        {
            string a = CreateDir("a", "A.dll", "B.dll");
            var check = RuntimeFileCheck.Capture(new[] { a });

            File.Delete(Path.Combine(a, "B.dll"));

            Assert.False(check.IsIntact);
            Assert.Equal(Path.Combine(a, "B.dll"), check.FindMissingFile());
        }

        [Fact]
        public void フォルダごと消えても欠けを検出する()
        {
            string a = CreateDir("a", "A.dll");
            var check = RuntimeFileCheck.Capture(new[] { a });

            Directory.Delete(a, true);

            Assert.False(check.IsIntact);
        }

        [Fact]
        public void DLL以外のファイルと後から増えたファイルは判定に使わない()
        {
            string a = CreateDir("a", "A.dll", "readme.txt");
            var check = RuntimeFileCheck.Capture(new[] { a });

            File.Delete(Path.Combine(a, "readme.txt"));
            File.WriteAllText(Path.Combine(a, "New.dll"), "x");

            Assert.Equal(1, check.FileCount);
            Assert.True(check.IsIntact);
        }

        [Fact]
        public void 存在しないフォルダや空の指定と重複は無視する()
        {
            string a = CreateDir("a", "A.dll");

            var check = RuntimeFileCheck.Capture(new[] { a, a.ToUpperInvariant(), null, "", Path.Combine(_root, "none") });

            Assert.Equal(1, check.FileCount);
            Assert.True(check.IsIntact);
        }

        [Fact]
        public void 実行中のランタイムを記録でき_欠けはない()
        {
            var check = RuntimeFileCheck.CaptureCurrentRuntime(typeof(System.Windows.Window).Assembly);

            Assert.True(check.FileCount > 0);
            Assert.True(check.IsIntact);
        }

        [Fact]
        public void DLLの読み込みエラーを判定できる()
        {
            Assert.True(RuntimeFileCheck.IsAssemblyLoadError(new FileNotFoundException("x", "System.Threading.Tasks.Parallel")));
            Assert.True(RuntimeFileCheck.IsAssemblyLoadError(new FileLoadException("x")));
            Assert.True(RuntimeFileCheck.IsAssemblyLoadError(new TypeLoadException("x")));

            // 並列処理やリフレクションで包まれている場合も、内側をたどって判定する
            Assert.True(RuntimeFileCheck.IsAssemblyLoadError(
                new AggregateException(new InvalidOperationException(), new FileNotFoundException("x"))));
            Assert.True(RuntimeFileCheck.IsAssemblyLoadError(
                new TargetInvocationException(new FileNotFoundException("x"))));
            Assert.True(RuntimeFileCheck.IsAssemblyLoadError(
                new TypeInitializationException("T", new FileLoadException("x"))));
        }

        [Fact]
        public void DLLの読み込み以外のエラーは対象外と判定する()
        {
            Assert.False(RuntimeFileCheck.IsAssemblyLoadError(null));
            Assert.False(RuntimeFileCheck.IsAssemblyLoadError(new InvalidOperationException()));
            Assert.False(RuntimeFileCheck.IsAssemblyLoadError(new IOException()));
            Assert.False(RuntimeFileCheck.IsAssemblyLoadError(new AggregateException(new IOException())));
        }
    }
}
