using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Classify8.Core
{
    /// <summary>
    /// 起動中に .NET ランタイムのファイルが消えていないかを確かめる。
    /// Windows Update などで .NET が更新されると、古いバージョンのファイルは削除される。
    /// アプリは起動時のバージョンを使い続け、DLL を必要になったときに読み込むため、
    /// 更新後はまだ読み込んでいない DLL が見つからず、振り分けやメニューの表示が失敗するようになる。
    /// </summary>
    public sealed class RuntimeFileCheck
    {
        private readonly string[] _files;

        private RuntimeFileCheck(string[] files)
        {
            _files = files;
        }

        /// <summary>確認したファイルの数</summary>
        public int FileCount => _files.Length;

        /// <summary>指定したフォルダにある DLL の一覧を記録する。存在しないフォルダは無視する</summary>
        public static RuntimeFileCheck Capture(IEnumerable<string> directories)
        {
            var files = new List<string>();
            foreach (var dir in directories
                         .Where(d => !string.IsNullOrEmpty(d))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.dll"));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 一覧が取れないフォルダは確認の対象にしない
                }
            }
            return new RuntimeFileCheck(files.ToArray());
        }

        /// <summary>
        /// このアプリが使っている .NET ランタイム (基本部分と WPF/WinForms の部分) のフォルダを記録する。
        /// ランタイムを同梱した単一ファイルの発行などで場所が分からない場合は、何も確認しない。
        /// </summary>
        public static RuntimeFileCheck CaptureCurrentRuntime(params Assembly[] frameworkAssemblies)
        {
            var dirs = new List<string> { GetDirectory(typeof(object).Assembly) };
            dirs.AddRange(frameworkAssemblies.Select(GetDirectory));
            return Capture(dirs);
        }

        /// <summary>記録したファイルのうち、なくなっているものを1つ返す。すべて揃っていれば null</summary>
        public string FindMissingFile()
        {
            foreach (var file in _files)
            {
                if (!File.Exists(file)) return file;
            }
            return null;
        }

        /// <summary>記録したファイルがすべて揃っているか</summary>
        public bool IsIntact => FindMissingFile() == null;

        /// <summary>DLL の読み込みに失敗したことによる例外か (内側の例外もたどる)</summary>
        public static bool IsAssemblyLoadError(Exception ex)
        {
            for (int depth = 0; ex != null && depth < 10; depth++)
            {
                if (ex is FileNotFoundException || ex is FileLoadException || ex is TypeLoadException) return true;

                if (ex is AggregateException agg)
                {
                    if (agg.InnerExceptions.Any(IsAssemblyLoadError)) return true;
                    return false;
                }
                ex = ex.InnerException;
            }
            return false;
        }

        private static string GetDirectory(Assembly assembly)
        {
            try
            {
                string location = assembly?.Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}
