using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Classify8.Core
{
    public class FileProcessor
    {
        private readonly AppSettings _settings;
        private readonly Action<string> _logCallback;

        public FileProcessor(AppSettings settings, Action<string> logCallback)
        {
            _settings = settings;
            _logCallback = logCallback;
        }

        public async Task<SortHistory> ProcessItemAsync(string sourcePath, string baseDestDir, SortRule rule, CancellationToken ct)
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) return null;

            bool isDirectory = (File.GetAttributes(sourcePath) & FileAttributes.Directory) == FileAttributes.Directory;
            string itemName = Path.GetFileName(sourcePath);

            string destDir = DynamicPathBuilder.BuildPath(baseDestDir, sourcePath, rule);
            if (_settings.AutoCreateDestFolder && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            string destPath = Path.Combine(destDir, itemName);
            string newFileName = "";
            string status = rule.IsMoveAction ? "移動" : "コピー";
            string errorMsg = "";

            try
            {
                if (isDirectory)
                {
                    if (Directory.Exists(destPath))
                    {
                        destPath = GetUniquePath(destDir, itemName, true);
                        newFileName = Path.GetFileName(destPath);
                    }

                    if (rule.IsMoveAction)
                    {
                        string srcRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
                        string destRoot = Path.GetPathRoot(Path.GetFullPath(destPath));

                        if (string.Equals(srcRoot, destRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            // 同一ドライブならそのまま移動
                            await Task.Run(() => Directory.Move(sourcePath, destPath), ct);
                        }
                        else
                        {
                            // 別ドライブなら、中身をコピーしてから元フォルダをごみ箱/完全削除へ送る
                            await Task.Run(() =>
                            {
                                CopyDirectory(sourcePath, destPath);
                                SafeDelete(sourcePath, true);
                            }, ct);
                        }
                    }
                    else
                    {
                        await Task.Run(() => CopyDirectory(sourcePath, destPath), ct);
                    }
                }
                else
                {
                    if (CheckIfAlreadyExists(sourcePath, destDir, itemName))
                    {
                        if (rule.IsMoveAction) SafeDelete(sourcePath);
                        return CreateHistory(rule.RuleName, itemName, "", sourcePath, destDir, "スキップ (完全一致)");
                    }

                    if (File.Exists(destPath))
                    {
                        var srcInfo = new FileInfo(sourcePath);
                        var destInfo = new FileInfo(destPath);
                        bool isSameSize = srcInfo.Length == destInfo.Length;

                        var resolution = isSameSize ? _settings.SameSizeResolution : _settings.DiffSizeResolution;

                        if (resolution == ConflictResolution.Skip)
                        {
                            return CreateHistory(rule.RuleName, itemName, "", sourcePath, destDir, "スキップ (同名あり)");
                        }
                        else if (resolution == ConflictResolution.KeepNewer)
                        {
                            if (srcInfo.LastWriteTime > destInfo.LastWriteTime) SafeDelete(destPath);
                            else
                            {
                                if (rule.IsMoveAction) SafeDelete(sourcePath);
                                return CreateHistory(rule.RuleName, itemName, "", sourcePath, destDir, "スキップ (より新しいファイル有)");
                            }
                        }
                        else if (resolution == ConflictResolution.KeepOlder)
                        {
                            if (srcInfo.LastWriteTime < destInfo.LastWriteTime) SafeDelete(destPath);
                            else
                            {
                                if (rule.IsMoveAction) SafeDelete(sourcePath);
                                return CreateHistory(rule.RuleName, itemName, "", sourcePath, destDir, "スキップ (より古いファイル有)");
                            }
                        }
                        else if (resolution == ConflictResolution.Rename)
                        {
                            destPath = GetUniquePath(destDir, itemName, false);
                            newFileName = Path.GetFileName(destPath);
                        }
                    }

                    if (rule.IsMoveAction) await Task.Run(() => File.Move(sourcePath, destPath), ct);
                    else await Task.Run(() => File.Copy(sourcePath, destPath), ct);
                }
            }
            catch (Exception ex)
            {
                status = "エラー";
                errorMsg = ex.Message;
                _logCallback($"[エラー] {itemName}: {ex.Message}");
            }

            return CreateHistory(rule.RuleName, itemName, newFileName, sourcePath, destDir, status, errorMsg);
        }

        private bool CheckIfAlreadyExists(string sourcePath, string destDir, string itemName)
        {
            if (!Directory.Exists(destDir)) return false;

            var srcInfo = new FileInfo(sourcePath);
            long srcSize = srcInfo.Length;
            DateTime srcTime = srcInfo.LastWriteTime;

            string baseName = GetBaseNameWithoutSerial(itemName, out string ext);
            string searchPattern = $"{baseName}*{ext}";

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(destDir, searchPattern))
                {
                    string destName = Path.GetFileName(filePath);

                    if (GetBaseNameWithoutSerial(destName, out _) == baseName)
                    {
                        var destInfo = new FileInfo(filePath);
                        if (destInfo.Length == srcSize && Math.Abs((srcTime - destInfo.LastWriteTime).TotalSeconds) <= 2)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private string GetBaseNameWithoutSerial(string fileName, out string ext)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            ext = Path.GetExtension(fileName);
            var match = Regex.Match(name, @"^(.*?)(?:\(\d+\))?$");
            return match.Success ? match.Groups[1].Value : name;
        }

        private const int FO_DELETE = 0x0003;
        private const int FOF_ALLOWUNDO = 0x0040;
        private const int FOF_NOCONFIRMATION = 0x0010;
        private const int FOF_SILENT = 0x0004;
        private const int FOF_NOERRORUI = 0x0400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public int wFunc;
            public string pFrom;
            public string pTo;
            public short fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        private void SafeDelete(string path, bool isDirectory = false)
        {
            try
            {
                if (_settings.MoveToRecycleBin)
                {
                    var shf = new SHFILEOPSTRUCT
                    {
                        wFunc = FO_DELETE,
                        fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
                        pFrom = path + '\0' + '\0'
                    };
                    SHFileOperation(ref shf);
                }
                else
                {
                    if (isDirectory && Directory.Exists(path)) Directory.Delete(path, true);
                    else if (File.Exists(path)) File.Delete(path);
                }
            }
            catch { }
        }

        private string GetUniquePath(string dir, string originalName, bool isDir)
        {
            string name = isDir ? originalName : Path.GetFileNameWithoutExtension(originalName);
            string ext = isDir ? "" : Path.GetExtension(originalName);
            int count = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name}({count}){ext}");
                count++;
            } while (isDir ? Directory.Exists(newPath) : File.Exists(newPath));

            return newPath;
        }

        private void CopyDirectory(string srcDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(srcDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), false);
            }
            foreach (var dir in Directory.GetDirectories(srcDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }

        private SortHistory CreateHistory(string ruleName, string name, string newName, string src, string dest, string status, string error = "")
        {
            return new SortHistory
            {
                Timestamp = DateTime.Now,
                RuleName = ruleName,
                FileName = name,
                NewFileName = newName,
                SourceDir = Path.GetDirectoryName(src),
                DestDir = dest,
                Status = status,
                ErrorMessage = error
            };
        }
    }
}