using System;
using System.Collections.Generic;
using System.IO;
using Classify8.Core;

namespace Classify8.Tests
{
    // データファイルを扱うテストの土台。
    // 保存先を一時フォルダに差し替えるので、本物の設定・ルール・履歴には一切触れない。
    public abstract class DataFileTestBase : IDisposable
    {
        protected string DataDir { get; }
        protected List<string> ReportedErrors { get; } = new List<string>();

        protected DataFileTestBase()
        {
            DataDir = Path.Combine(Path.GetTempPath(), "c8data_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDir);

            AppPaths.DataDirectoryOverride = DataDir;
            DataManager.ErrorReporter = msg => ReportedErrors.Add(msg);
        }

        public void Dispose()
        {
            AppPaths.DataDirectoryOverride = null;
            DataManager.ErrorReporter = null;
            try { Directory.Delete(DataDir, true); } catch { }
        }

        protected string PathOf(string fileName) => Path.Combine(DataDir, fileName);
    }
}
