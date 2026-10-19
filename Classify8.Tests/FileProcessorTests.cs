using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Classify8.Core;
using Xunit;

namespace Classify8.Tests
{
    public class FileProcessorTests : IDisposable
    {
        private readonly string _root;
        private readonly string _src;
        private readonly string _dest;
        private readonly ConcurrentQueue<string> _logs = new ConcurrentQueue<string>();

        public FileProcessorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "c8test_" + Guid.NewGuid().ToString("N"));
            _src = Path.Combine(_root, "src");
            _dest = Path.Combine(_root, "dest");
            Directory.CreateDirectory(_src);
            Directory.CreateDirectory(_dest);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        // --- ヘルパー --------------------------------------------------------------

        private static AppSettings Settings() => new AppSettings
        {
            MoveToRecycleBin = false, // テストではごみ箱を使わず完全削除する
            AutoCreateDestFolder = true,
            SameSizeResolution = ConflictResolution.Rename,
            DiffSizeResolution = ConflictResolution.Rename,
            CompareContentWhenTimestampDiffers = true
        };

        private FileProcessor NewProcessor(AppSettings settings = null)
            => new FileProcessor(settings ?? Settings(), msg => _logs.Enqueue(msg));

        private static SortRule MoveRule() => new SortRule { RuleName = "test", IsMoveAction = true };
        private static SortRule CopyRule() => new SortRule { RuleName = "test", IsMoveAction = false };

        private static string Write(string path, string content, DateTime? time = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            if (time.HasValue) File.SetLastWriteTime(path, time.Value);
            return path;
        }

        private static string WriteBytes(string path, byte[] content, DateTime? time = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            if (time.HasValue) File.SetLastWriteTime(path, time.Value);
            return path;
        }

        private Task<SortHistory> Process(FileProcessor processor, string source, SortRule rule, bool isDirectory = false, string dest = null)
            => processor.ProcessItemAsync(source, dest ?? _dest, rule, CancellationToken.None, isDirectory);

        private static readonly DateTime Old = new DateTime(2020, 1, 1, 12, 0, 0);
        private static readonly DateTime New = new DateTime(2024, 6, 1, 12, 0, 0);

        // =========================================================
        // 重複判定 (内容比較)
        // =========================================================
        [Fact]
        public async Task 内容が同じでタイムスタンプが違うなら_移動モードでは元ファイルが削除される()
        {
            Write(Path.Combine(_dest, "a.txt"), "hello world", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "hello world", New);

            var history = await Process(NewProcessor(), src, MoveRule());

            Assert.Equal(HistoryStatus.DeletedContentMatch, history.Status);
            Assert.False(File.Exists(src));
            Assert.Single(Directory.GetFiles(_dest));
        }

        [Fact]
        public async Task 内容が同じでタイムスタンプが違うなら_コピーモードではスキップされ元ファイルは残る()
        {
            Write(Path.Combine(_dest, "a.txt"), "hello world", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "hello world", New);

            var history = await Process(NewProcessor(), src, CopyRule());

            Assert.Equal(HistoryStatus.SkippedContentMatch, history.Status);
            Assert.True(File.Exists(src));
            Assert.Single(Directory.GetFiles(_dest));
        }

        [Fact]
        public async Task サイズが同じで内容が違うなら_重複とせずリネームして移動する()
        {
            Write(Path.Combine(_dest, "a.txt"), "AAAAAAAAAA", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "BBBBBBBBBB", New);

            var history = await Process(NewProcessor(), src, MoveRule());

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.Equal("a(1).txt", history.NewFileName);
            Assert.True(File.Exists(Path.Combine(_dest, "a(1).txt")));
            Assert.Equal("AAAAAAAAAA", File.ReadAllText(Path.Combine(_dest, "a.txt"))); // 既存は変わらない
        }

        [Fact]
        public async Task 内容比較をOFFにすると_内容が同じでもタイムスタンプが違えば重複とみなさない()
        {
            Write(Path.Combine(_dest, "a.txt"), "hello world", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "hello world", New);

            var settings = Settings();
            settings.CompareContentWhenTimestampDiffers = false;

            var history = await Process(NewProcessor(settings), src, MoveRule());

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.Equal("a(1).txt", history.NewFileName);
        }

        [Fact]
        public async Task サイズと更新日時が同じ_誤差1秒なら完全一致として扱う()
        {
            Write(Path.Combine(_dest, "a.txt"), "same", New);
            string src = Write(Path.Combine(_src, "a.txt"), "same", New.AddSeconds(1));

            var history = await Process(NewProcessor(), src, MoveRule());

            Assert.Equal(HistoryStatus.DeletedExactMatch, history.Status);
        }

        [Fact]
        public async Task 更新日時が誤差を超えて違う場合は_内容比較で一致を確認する()
        {
            Write(Path.Combine(_dest, "a.txt"), "same", New);
            string src = Write(Path.Combine(_src, "a.txt"), "same", New.AddSeconds(10));

            var history = await Process(NewProcessor(), src, MoveRule());

            Assert.Equal(HistoryStatus.DeletedContentMatch, history.Status);
        }

        [Fact]
        public async Task 連番違いの同名ファイルとも内容比較される()
        {
            Write(Path.Combine(_dest, "report(1).txt"), "same content", Old);
            string src = Write(Path.Combine(_src, "report.txt"), "same content", New);

            var history = await Process(NewProcessor(), src, MoveRule());

            Assert.Equal(HistoryStatus.DeletedContentMatch, history.Status);
            Assert.False(File.Exists(src));
        }

        [Fact]
        public async Task 拡張子が違うファイルは_内容が同じでも重複としない()
        {
            Write(Path.Combine(_dest, "report.docx"), "same content", Old);
            string src = Write(Path.Combine(_src, "report.doc"), "same content", New);

            var history = await Process(NewProcessor(), src, MoveRule());

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.True(File.Exists(Path.Combine(_dest, "report.doc")));
        }

        [Fact]
        public async Task 内容比較の上限サイズを超えるファイルは_比較せず従来の判定になる()
        {
            var data = new byte[2 * 1024 * 1024];
            new Random(1).NextBytes(data);
            WriteBytes(Path.Combine(_dest, "big.bin"), data, Old);
            string src = WriteBytes(Path.Combine(_src, "big.bin"), data, New);

            var settings = Settings();
            settings.ContentCompareMaxSizeMB = 1; // 1MB を超えるファイルは比較しない

            var history = await Process(NewProcessor(settings), src, MoveRule());

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.Equal("big(1).bin", history.NewFileName);
        }

        [Fact]
        public async Task 内容比較の上限サイズ以内なら比較される()
        {
            var data = new byte[2 * 1024 * 1024];
            new Random(2).NextBytes(data);
            WriteBytes(Path.Combine(_dest, "big.bin"), data, Old);
            string src = WriteBytes(Path.Combine(_src, "big.bin"), data, New);

            var settings = Settings();
            settings.ContentCompareMaxSizeMB = 5;

            var history = await Process(NewProcessor(settings), src, MoveRule());

            Assert.Equal(HistoryStatus.DeletedContentMatch, history.Status);
        }

        // =========================================================
        // 同名ファイルの競合解決
        // =========================================================
        [Fact]
        public async Task 新しい方を残す_元ファイルが新しければ既存を置き換える()
        {
            Write(Path.Combine(_dest, "a.txt"), "old", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "newer content", New);

            var settings = Settings();
            settings.DiffSizeResolution = ConflictResolution.KeepNewer;

            var history = await Process(NewProcessor(settings), src, MoveRule());

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.Equal("newer content", File.ReadAllText(Path.Combine(_dest, "a.txt")));
            Assert.False(File.Exists(src));
        }

        [Fact]
        public async Task 新しい方を残す_元ファイルが古ければ_移動モードでは元ファイルを削除する()
        {
            Write(Path.Combine(_dest, "a.txt"), "newer content", New);
            string src = Write(Path.Combine(_src, "a.txt"), "old", Old);

            var settings = Settings();
            settings.DiffSizeResolution = ConflictResolution.KeepNewer;

            var history = await Process(NewProcessor(settings), src, MoveRule());

            Assert.Equal(HistoryStatus.DeletedNewerExists, history.Status);
            Assert.False(File.Exists(src));
            Assert.Equal("newer content", File.ReadAllText(Path.Combine(_dest, "a.txt")));
        }

        [Fact]
        public async Task 新しい方を残す_元ファイルが古ければ_コピーモードではスキップして元ファイルを残す()
        {
            Write(Path.Combine(_dest, "a.txt"), "newer content", New);
            string src = Write(Path.Combine(_src, "a.txt"), "old", Old);

            var settings = Settings();
            settings.DiffSizeResolution = ConflictResolution.KeepNewer;

            var history = await Process(NewProcessor(settings), src, CopyRule());

            Assert.Equal(HistoryStatus.SkippedNewerExists, history.Status);
            Assert.True(File.Exists(src));
        }

        [Fact]
        public async Task 古い方を残す_元ファイルが古ければ既存を置き換え_新しければ元ファイルを削除する()
        {
            var settings = Settings();
            settings.DiffSizeResolution = ConflictResolution.KeepOlder;

            // 元ファイルの方が古い → 既存を置き換える
            Write(Path.Combine(_dest, "a.txt"), "newer content", New);
            string older = Write(Path.Combine(_src, "a.txt"), "old", Old);
            var h1 = await Process(NewProcessor(settings), older, MoveRule());
            Assert.Equal(HistoryStatus.Moved, h1.Status);
            Assert.Equal("old", File.ReadAllText(Path.Combine(_dest, "a.txt")));

            // 元ファイルの方が新しい → 元ファイルを削除する
            string newer = Write(Path.Combine(_src, "a.txt"), "newer content", New);
            var h2 = await Process(NewProcessor(settings), newer, MoveRule());
            Assert.Equal(HistoryStatus.DeletedOlderExists, h2.Status);
            Assert.False(File.Exists(newer));
        }

        [Fact]
        public async Task 振り分けない設定_同名があればスキップして何も変えない()
        {
            Write(Path.Combine(_dest, "a.txt"), "old", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "newer content", New);

            var settings = Settings();
            settings.DiffSizeResolution = ConflictResolution.Skip;

            var history = await Process(NewProcessor(settings), src, MoveRule());

            Assert.Equal(HistoryStatus.SkippedSameName, history.Status);
            Assert.True(File.Exists(src));
            Assert.Equal("old", File.ReadAllText(Path.Combine(_dest, "a.txt")));
        }

        [Fact]
        public async Task リネーム設定_連番は空いている番号が使われる()
        {
            Write(Path.Combine(_dest, "a.txt"), "AAAAAAAAAA", Old);
            Write(Path.Combine(_dest, "a(1).txt"), "CCCCCCCCCC", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "BBBBBBBBBB", New);

            var history = await Process(NewProcessor(), src, MoveRule());

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.Equal("a(2).txt", history.NewFileName);
        }

        // =========================================================
        // エラー・中断・警告
        // =========================================================
        [Fact]
        public async Task 削除に失敗した場合は_成功扱いにせずエラーとして記録される()
        {
            Write(Path.Combine(_dest, "a.txt"), "same content", Old);
            string src = Write(Path.Combine(_src, "a.txt"), "same content", New);

            // 他のプロセスが削除を許可しない形で開いている状態を作る (読み込みは許可される)
            using (new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var history = await Process(NewProcessor(), src, MoveRule());

                Assert.Equal(HistoryStatus.Error, history.Status);
                Assert.Contains("削除できませんでした", history.ErrorMessage);
                Assert.True(File.Exists(src));
            }
            Assert.Contains(_logs, m => m.Contains("削除できませんでした"));
        }

        [Fact]
        public async Task 振り分け先がなく自動作成もOFFならエラーになる()
        {
            string src = Write(Path.Combine(_src, "a.txt"), "x", New);
            var settings = Settings();
            settings.AutoCreateDestFolder = false;

            var history = await Process(NewProcessor(settings), src, MoveRule(), dest: Path.Combine(_root, "missing"));

            Assert.Equal(HistoryStatus.Error, history.Status);
            Assert.True(File.Exists(src));
            Assert.False(Directory.Exists(Path.Combine(_root, "missing")));
        }

        [Fact]
        public async Task 振り分け先と振り分け元が同じ場所なら警告になる()
        {
            string src = Write(Path.Combine(_src, "a.txt"), "x");

            var history = await Process(NewProcessor(), src, MoveRule(), dest: _src);

            Assert.True(HistoryStatus.IsWarning(history.Status));
            Assert.True(File.Exists(src));
        }

        [Fact]
        public async Task 存在しない対象はnullを返す()
        {
            var history = await Process(NewProcessor(), Path.Combine(_src, "nothing.txt"), MoveRule());

            Assert.Null(history);
        }

        [Fact]
        public async Task キャンセル済みなら何も処理せずnullを返す()
        {
            string src = Write(Path.Combine(_src, "a.txt"), "x");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var history = await NewProcessor().ProcessItemAsync(src, _dest, MoveRule(), cts.Token, false);

            Assert.Null(history);
            Assert.True(File.Exists(src));
            Assert.Empty(Directory.GetFiles(_dest));
        }

        // =========================================================
        // 振り分け先パスのタグ
        // =========================================================
        [Fact]
        public async Task 振り分け先のNAMEタグはフォルダ名に使えない文字が置き換えられて展開される()
        {
            string src = Write(Path.Combine(_src, "a.txt"), "x");
            var rule = new SortRule { RuleName = "a:b", IsMoveAction = true };

            var history = await Process(NewProcessor(), src, rule, dest: _dest + @"\/NAME");

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.True(File.Exists(Path.Combine(_dest, "a_b", "a.txt")));
        }

        [Fact]
        public async Task 振り分け先のファイル日付タグはファイルの更新日時で展開される()
        {
            string src = Write(Path.Combine(_src, "a.txt"), "x", new DateTime(2021, 3, 4, 10, 0, 0));

            var history = await Process(NewProcessor(), src, MoveRule(), dest: _dest + @"\/FYYYY\/FMM\/FDD");

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.True(File.Exists(Path.Combine(_dest, "2021", "03", "04", "a.txt")));
        }

        // =========================================================
        // フォルダの振り分け
        // =========================================================
        [Fact]
        public async Task フォルダを移動できる()
        {
            string dir = Path.Combine(_src, "folderA");
            Write(Path.Combine(dir, "f.txt"), "x");

            var history = await Process(NewProcessor(), dir, MoveRule(), isDirectory: true);

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.False(Directory.Exists(dir));
            Assert.True(File.Exists(Path.Combine(_dest, "folderA", "f.txt")));
        }

        [Fact]
        public async Task 同名フォルダがあればリネームして移動する()
        {
            Directory.CreateDirectory(Path.Combine(_dest, "folderA"));
            string dir = Path.Combine(_src, "folderA");
            Write(Path.Combine(dir, "f.txt"), "x");

            var history = await Process(NewProcessor(), dir, MoveRule(), isDirectory: true);

            Assert.Equal(HistoryStatus.Moved, history.Status);
            Assert.Equal("folderA(1)", history.NewFileName);
            Assert.True(File.Exists(Path.Combine(_dest, "folderA(1)", "f.txt")));
        }

        [Fact]
        public async Task フォルダをコピーできる_中身も含めて元は残る()
        {
            string dir = Path.Combine(_src, "folderA");
            Write(Path.Combine(dir, "f.txt"), "x");
            Write(Path.Combine(dir, "sub", "g.txt"), "y");

            var history = await Process(NewProcessor(), dir, CopyRule(), isDirectory: true);

            Assert.Equal(HistoryStatus.Copied, history.Status);
            Assert.True(File.Exists(Path.Combine(dir, "f.txt")));
            Assert.True(File.Exists(Path.Combine(_dest, "folderA", "f.txt")));
            Assert.True(File.Exists(Path.Combine(_dest, "folderA", "sub", "g.txt")));
        }

        // =========================================================
        // 並列処理 (MainWindow は複数ファイルを同時に処理する)
        // =========================================================
        [Fact]
        public async Task 同名で内容の違うファイルが同時に流れ込んでも_すべて成功し上書きされない()
        {
            // サブフォルダ検索で、同名ファイル(中身は別)が同じ振り分け先へ同時に流れ込むケース
            const int count = 30;
            var sources = Enumerable.Range(0, count)
                .Select(i => Write(Path.Combine(_src, $"sub{i}", "image.jpg"), $"content-{i}-{new string('x', i)}"))
                .ToList();

            var processor = NewProcessor();
            var rule = MoveRule();

            var results = await Task.WhenAll(sources.Select(s => Task.Run(() => Process(processor, s, rule))));

            Assert.All(results, h => Assert.Equal(HistoryStatus.Moved, h.Status));
            Assert.Equal(count, Directory.GetFiles(_dest).Length);
            Assert.All(sources, s => Assert.False(File.Exists(s)));
            Assert.True(_logs.IsEmpty);

            // 中身がすべて異なる = どれも上書きで失われていない
            var contents = Directory.GetFiles(_dest).Select(File.ReadAllText).Distinct().Count();
            Assert.Equal(count, contents);
        }

        [Fact]
        public async Task 内容が同じファイルが同時に流れ込むと_1つだけ移動され残りは重複として削除される()
        {
            const int count = 10;
            var sources = Enumerable.Range(0, count)
                .Select(i => Write(Path.Combine(_src, $"s{i}", "dup.txt"), "same-content", Old.AddHours(i * 5)))
                .ToList();

            var processor = NewProcessor();
            var rule = MoveRule();

            var results = await Task.WhenAll(sources.Select(s => Task.Run(() => Process(processor, s, rule))));

            Assert.Equal(1, results.Count(h => h.Status == HistoryStatus.Moved));
            Assert.Equal(count - 1, results.Count(h => h.Status == HistoryStatus.DeletedContentMatch));
            Assert.Single(Directory.GetFiles(_dest));
            Assert.All(sources, s => Assert.False(File.Exists(s)));
        }

        [Fact]
        public async Task 名前の違う大量のファイルが同時に流れ込んでも_すべて成功する()
        {
            const int count = 100;
            var sources = Enumerable.Range(0, count)
                .Select(i => Write(Path.Combine(_src, $"f{i}.txt"), $"c{i}"))
                .ToList();

            var processor = NewProcessor();
            var rule = MoveRule();

            var results = await Task.WhenAll(sources.Select(s => Task.Run(() => Process(processor, s, rule))));

            Assert.All(results, h => Assert.Equal(HistoryStatus.Moved, h.Status));
            Assert.Equal(count, Directory.GetFiles(_dest).Length);
            Assert.True(_logs.IsEmpty);
        }
    }
}
