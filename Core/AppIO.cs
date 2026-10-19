using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Classify8.Core
{
    // ==========================================
    // データ保存先の決定
    // 実行ファイルと同じフォルダに書き込めればそこを使い、
    // 書き込めない場合(Program Files 配下など)は %AppData%\Classify8 を使う
    // ==========================================
    public static class AppPaths
    {
        private static readonly Lazy<string> _dataDir = new Lazy<string>(ResolveDataDirectory);

        /// <summary>
        /// テスト用: データ保存先を一時的に差し替える。null に戻すと通常の保存先になる。
        /// </summary>
        public static string DataDirectoryOverride { get; set; }

        public static string DataDirectory => DataDirectoryOverride ?? _dataDir.Value;

        public static string GetFile(string fileName) => Path.Combine(DataDirectory, fileName);

        private static string ResolveDataDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (IsWritable(baseDir)) return baseDir;

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Classify8");
            Directory.CreateDirectory(appData);
            return appData;
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".c8_write_test_" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // ==========================================
    // 原子的なファイル書き込み
    // 一時ファイルに書いてから差し替えるので、書き込み中に落ちても元のファイルは壊れない
    // ==========================================
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string content, Encoding encoding = null)
        {
            encoding ??= new UTF8Encoding(false);
            string tmp = path + ".tmp";

            File.WriteAllText(tmp, content, encoding);

            try
            {
                // ウイルス対策ソフト等が一瞬ファイルを掴んでいる場合に備えて数回リトライする
                const int maxAttempts = 3;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(tmp, path, null);
                        else File.Move(tmp, path);
                        return;
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(100 * attempt);
                    }
                }
            }
            catch
            {
                // 差し替えに失敗した場合は、一時ファイルを残さない (元のファイルはそのまま)
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }
    }

    // ==========================================
    // 簡易エラーログ (error.log)
    // 常駐アプリが予期しない例外で落ちたときに、原因を後から調べられるようにする
    // ==========================================
    public static class AppLog
    {
        private static readonly object _sync = new object();
        private const long MaxBytes = 1024 * 1024; // 1MB を超えたら世代交代

        public static void Error(string message, Exception ex = null)
        {
            try
            {
                lock (_sync)
                {
                    string path = AppPaths.GetFile("error.log");

                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }

                    var sb = new StringBuilder();
                    sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ").AppendLine(message);
                    if (ex != null) sb.AppendLine(ex.ToString());
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // ログ出力の失敗でアプリを止めない
            }
        }
    }
}
