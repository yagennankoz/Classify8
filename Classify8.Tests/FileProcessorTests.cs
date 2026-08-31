using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Classify8.Core;

namespace Classify8.Tests
{
    public class FileProcessorTests : IDisposable
    {
        private readonly string _srcDir;
        private readonly string _destDir;

        public FileProcessorTests()
        {
            string baseTemp = Path.Combine(Path.GetTempPath(), "Classify8_EngineTests", Guid.NewGuid().ToString());
            _srcDir = Path.Combine(baseTemp, "Source");
            _destDir = Path.Combine(baseTemp, "Dest");
            Directory.CreateDirectory(_srcDir);
            Directory.CreateDirectory(_destDir);
        }

        public void Dispose()
        {
            string baseTemp = Directory.GetParent(_srcDir).FullName;
            if (Directory.Exists(baseTemp)) Directory.Delete(baseTemp, true);
        }

        [Fact]
        public async Task ファイル移動テスト_正常にファイルが移動されるか()
        {
            string srcFile = Path.Combine(_srcDir, "move_test.txt");
            File.WriteAllText(srcFile, "Hello World");

            var settings = new AppSettings { MoveToRecycleBin = false };
            var processor = new FileProcessor(settings, msg => { });
            var rule = new SortRule { IsMoveAction = true, SearchCondition = "*" };

            await processor.ProcessItemAsync(srcFile, _destDir, rule, CancellationToken.None);

            Assert.False(File.Exists(srcFile));
            Assert.True(File.Exists(Path.Combine(_destDir, "move_test.txt")));
        }

        [Fact]
        public async Task コンフリクト解決テスト_完全一致の場合はスキップして元ファイルが削除されるか()
        {
            string fileName = "duplicate.txt";
            string srcFile = Path.Combine(_srcDir, fileName);
            string destFile = Path.Combine(_destDir, fileName);

            File.WriteAllText(srcFile, "Exactly Same Content");
            File.WriteAllText(destFile, "Exactly Same Content");

            var settings = new AppSettings { MoveToRecycleBin = false };
            var processor = new FileProcessor(settings, msg => { });
            var rule = new SortRule { IsMoveAction = true, SearchCondition = "*" }; 

            var history = await processor.ProcessItemAsync(srcFile, _destDir, rule, CancellationToken.None);

            Assert.Equal("スキップ (完全一致)", history.Status);
            Assert.False(File.Exists(srcFile)); // 移動モードなのでお掃除される
            Assert.True(File.Exists(destFile));
        }

        // =========================================================
        // AppSettings による詳細なコンフリクト処理テスト
        // =========================================================

        [Fact]
        public async Task 全体設定_SameSize_KeepNewer_ソースが新しい場合は上書きされるか()
        {
            string fileName = "keepnewer_overwrite.txt";
            string srcFile = Path.Combine(_srcDir, fileName);
            string destFile = Path.Combine(_destDir, fileName);

            // サイズは同じだが中身(ハッシュ)が違うファイル
            File.WriteAllText(srcFile, "Data A");
            File.WriteAllText(destFile, "Data B");

            // 送信元(Source)のタイムスタンプを新しくする
            File.SetLastWriteTime(srcFile, DateTime.Now);
            File.SetLastWriteTime(destFile, DateTime.Now.AddDays(-1));

            var settings = new AppSettings 
            { 
                SameSizeResolution = ConflictResolution.KeepNewer, // 同じサイズの場合は新しい方を残す
                MoveToRecycleBin = false
            };
            var processor = new FileProcessor(settings, msg => { });
            var rule = new SortRule { IsMoveAction = true, SearchCondition = "*" };

            await processor.ProcessItemAsync(srcFile, _destDir, rule, CancellationToken.None);

            Assert.False(File.Exists(srcFile));
            Assert.Equal("Data A", File.ReadAllText(destFile)); // 上書きされたことを確認
        }

        [Fact]
        public async Task 全体設定_SameSize_KeepNewer_ソースが古い場合はスキップされて元ファイルがお掃除されるか()
        {
            string fileName = "keepnewer_skip.txt";
            string srcFile = Path.Combine(_srcDir, fileName);
            string destFile = Path.Combine(_destDir, fileName);

            File.WriteAllText(srcFile, "Data A");
            File.WriteAllText(destFile, "Data B");

            // 送信元(Source)のタイムスタンプを古くする
            File.SetLastWriteTime(srcFile, DateTime.Now.AddDays(-1));
            File.SetLastWriteTime(destFile, DateTime.Now);

            var settings = new AppSettings 
            { 
                SameSizeResolution = ConflictResolution.KeepNewer,
                MoveToRecycleBin = false
            };
            var processor = new FileProcessor(settings, msg => { });
            var rule = new SortRule { IsMoveAction = true, SearchCondition = "*" };

            var history = await processor.ProcessItemAsync(srcFile, _destDir, rule, CancellationToken.None);

            Assert.Equal("スキップ (より新しいファイル有)", history.Status);
            Assert.False(File.Exists(srcFile)); // 振り分けはしないが、移動モードの時は不要なので消す
            Assert.Equal("Data B", File.ReadAllText(destFile)); // 振り分け先は保護されている
        }

        [Fact]
        public async Task 全体設定_DiffSize_KeepOlder_ソースが古い場合は上書きされるか()
        {
            string fileName = "keepolder.txt";
            string srcFile = Path.Combine(_srcDir, fileName);
            string destFile = Path.Combine(_destDir, fileName);

            // サイズが違うファイル
            File.WriteAllText(srcFile, "Short");
            File.WriteAllText(destFile, "Longer Data");

            // 送信元(Source)のタイムスタンプを古くする
            File.SetLastWriteTime(srcFile, DateTime.Now.AddDays(-1));
            File.SetLastWriteTime(destFile, DateTime.Now);

            var settings = new AppSettings 
            { 
                DiffSizeResolution = ConflictResolution.KeepOlder, // 違うサイズの場合は古い方を残す
                MoveToRecycleBin = false
            };
            var processor = new FileProcessor(settings, msg => { });
            var rule = new SortRule { IsMoveAction = true, SearchCondition = "*" };

            await processor.ProcessItemAsync(srcFile, _destDir, rule, CancellationToken.None);

            Assert.False(File.Exists(srcFile));
            Assert.Equal("Short", File.ReadAllText(destFile)); // 古いファイルで上書きされたことを確認
        }

        [Fact]
        public async Task 全体設定_DiffSize_Skip_スキップ設定の場合は無条件で保護されるか()
        {
            string fileName = "skip_always.txt";
            string srcFile = Path.Combine(_srcDir, fileName);
            string destFile = Path.Combine(_destDir, fileName);

            File.WriteAllText(srcFile, "New File");
            File.WriteAllText(destFile, "Existing File");

            var settings = new AppSettings 
            { 
                DiffSizeResolution = ConflictResolution.Skip, // リネームせずとにかくスキップ
                MoveToRecycleBin = false
            };
            var processor = new FileProcessor(settings, msg => { });
            var rule = new SortRule { IsMoveAction = true, SearchCondition = "*" };

            var history = await processor.ProcessItemAsync(srcFile, _destDir, rule, CancellationToken.None);

            Assert.Equal("スキップ (同名あり)", history.Status);
            Assert.True(File.Exists(srcFile)); // スキップ設定の時は元ファイルもそのまま残す
            Assert.Equal("Existing File", File.ReadAllText(destFile)); // 振り分け先も保護されている
        }

        [Fact]
        public async Task 結合テスト_動的パスタグが展開され_自動でフォルダが作成されてファイルが移動されるか()
        {
            // 事前準備
            string srcFile = Path.Combine(_srcDir, "dynamic_test.txt");
            File.WriteAllText(srcFile, "Hello Dynamic Path");

            // 🚨修正: Path.Combine に "/" で始まる文字列を渡すと絶対パス扱いになるため、文字列連結を使用する
            string baseDestDir = _destDir + @"\/YYYY\/NAME";

            var rule = new SortRule 
            { 
                RuleName = "動的生成テスト",
                IsMoveAction = true, 
                SearchCondition = "*" 
            };

            var settings = new AppSettings 
            { 
                AutoCreateDestFolder = true, // フォルダの自動生成を有効化
                MoveToRecycleBin = false 
            };
            
            var processor = new FileProcessor(settings, msg => { });

            // 実行
            var history = await processor.ProcessItemAsync(srcFile, baseDestDir, rule, CancellationToken.None);

            // 検証 (結果のフォルダパスを組み立てる)
            string expectedYear = DateTime.Now.ToString("yyyy");
            string expectedDir = Path.Combine(_destDir, expectedYear, "動的生成テスト");
            string expectedFilePath = Path.Combine(expectedDir, "dynamic_test.txt");

            // 元ファイルが消え、新しく作られた階層にファイルが存在するかチェック
            Assert.False(File.Exists(srcFile));
            Assert.True(Directory.Exists(expectedDir)); // フォルダが自動生成されていること
            Assert.True(File.Exists(expectedFilePath)); // ファイルが移動していること

            // 履歴のDestDirが正しく展開後のパスになっていること
            Assert.Equal(expectedDir, history.DestDir);
        }


    }
}