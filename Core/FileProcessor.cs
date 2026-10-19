using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Classify8.Core
{
    public class FileProcessor
    {
        private readonly AppSettings _settings;
        private readonly Action<string> _logCallback;

        // 振り分け先に同一ファイルがあったときの、一致の種類
        private enum DuplicateKind { None, ExactMatch, ContentMatch }

        // 内容比較は I/O 負荷が高いため、並列処理中でも同時に走る数を絞る (HDD/NAS での逆効果を防ぐ)
        private static readonly SemaphoreSlim ContentCompareGate = new SemaphoreSlim(2, 2);

        // 「同じ振り分け先 × 同じ名前」への同時書き込みを直列化するためのロック (ストライプ方式)
        // サブフォルダ検索で同名ファイルが同じ振り分け先へ並列に流れ込んでも、競合しないようにする
        private const int GateCount = 64;
        private readonly SemaphoreSlim[] _gates =
            Enumerable.Range(0, GateCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

        // 振り分け先フォルダごとの既存ファイル索引 (実行中に1回だけフォルダを列挙する)
        private readonly ConcurrentDictionary<string, Lazy<DestIndex>> _destIndexes =
            new ConcurrentDictionary<string, Lazy<DestIndex>>(StringComparer.OrdinalIgnoreCase);

        // 存在を確認済みの振り分け先フォルダ
        private readonly ConcurrentDictionary<string, byte> _knownDirs =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex SerialRegex = new Regex(@"^(.*?)(?:\(\d+\))?$", RegexOptions.CultureInvariant);

        public FileProcessor(AppSettings settings, Action<string> logCallback)
        {
            _settings = settings;
            _logCallback = logCallback;
        }

        /// <param name="isDirectoryHint">
        /// 呼び出し側がすでに種類(ファイル/フォルダ)を把握している場合に渡すと、ファイルシステムへの問い合わせを減らせる。
        /// </param>
        /// <returns>処理結果の履歴。対象が存在しない場合や、処理中にキャンセルされた場合は null。</returns>
        public async Task<SortHistory> ProcessItemAsync(string sourcePath, string baseDestDir, SortRule rule, CancellationToken ct, bool? isDirectoryHint = null)
        {
            bool isDirectory;
            if (isDirectoryHint.HasValue)
            {
                isDirectory = isDirectoryHint.Value;
                if (!(isDirectory ? Directory.Exists(sourcePath) : File.Exists(sourcePath))) return null;
            }
            else
            {
                if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) return null;
                isDirectory = (File.GetAttributes(sourcePath) & FileAttributes.Directory) == FileAttributes.Directory;
            }

            string itemName = Path.GetFileName(sourcePath);

            string destDir = DynamicPathBuilder.BuildPath(baseDestDir, sourcePath, rule);
            string destPath = Path.Combine(destDir, itemName);
            string newFileName = "";
            string status = rule.IsMoveAction ? HistoryStatus.Moved : HistoryStatus.Copied;
            string errorMsg = "";

            try
            {
                // 絶対パスに変換し、末尾の不要なスラッシュを除去して大文字小文字を無視して比較
                string fullSource = Path.GetFullPath(sourcePath).TrimEnd('\\', '/');
                string fullDest = Path.GetFullPath(destPath).TrimEnd('\\', '/');
                if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
                {
                    return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, HistoryStatus.WarningSamePath);
                }
            }
            catch
            {
                // MTP(スマホ等)や仮想ドライブで GetFullPath が使えずエラーになる場合のフォールバック比較
                string sSource = sourcePath.Replace("/", "\\").TrimEnd('\\');
                string sDest = destPath.Replace("/", "\\").TrimEnd('\\');
                if (string.Equals(sSource, sDest, StringComparison.OrdinalIgnoreCase))
                {
                    return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, HistoryStatus.WarningSamePath);
                }
            }

            // 同じ振り分け先・同じ名前の処理は1件ずつ行う (判定→リネーム→移動の間に他の処理が割り込まないようにする)
            SemaphoreSlim gate = GetGate(destDir, itemName);
            try
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            try
            {
                EnsureDestDir(destDir);

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
                            Directory.Move(sourcePath, destPath);
                        }
                        else
                        {
                            // 別ドライブなら、中身をコピーしてから元フォルダをごみ箱/完全削除へ送る
                            CopyDirectory(sourcePath, destPath);
                            DeleteOrThrow(sourcePath, true);
                        }
                    }
                    else
                    {
                        CopyDirectory(sourcePath, destPath);
                    }
                }
                else
                {
                    DuplicateKind matchKind = CheckIfAlreadyExists(sourcePath, destDir, itemName, ct);
                    if (matchKind != DuplicateKind.None)
                    {
                        if (rule.IsMoveAction)
                        {
                            // 削除に失敗した場合は例外になり、「エラー」として記録される
                            DeleteOrThrow(sourcePath, false);
                            return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, matchKind == DuplicateKind.ExactMatch ? HistoryStatus.DeletedExactMatch : HistoryStatus.DeletedContentMatch);
                        }
                        else
                        {
                            return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, matchKind == DuplicateKind.ExactMatch ? HistoryStatus.SkippedExactMatch : HistoryStatus.SkippedContentMatch);
                        }
                    }

                    if (File.Exists(destPath))
                    {
                        var srcInfo = new FileInfo(sourcePath);
                        var destInfo = new FileInfo(destPath);
                        bool isSameSize = srcInfo.Length == destInfo.Length;

                        var resolution = isSameSize ? _settings.SameSizeResolution : _settings.DiffSizeResolution;

                        if (resolution == ConflictResolution.Skip)
                        {
                            return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, HistoryStatus.SkippedSameName);
                        }
                        else if (resolution == ConflictResolution.KeepNewer)
                        {
                            if (srcInfo.LastWriteTime > destInfo.LastWriteTime)
                            {
                                DeleteOrThrow(destPath, false);
                                RemoveFromIndex(destDir, Path.GetFileName(destPath));
                            }
                            else
                            {
                                if (rule.IsMoveAction)
                                {
                                    DeleteOrThrow(sourcePath, false);
                                    return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, HistoryStatus.DeletedNewerExists);
                                }
                                return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, HistoryStatus.SkippedNewerExists);
                            }
                        }
                        else if (resolution == ConflictResolution.KeepOlder)
                        {
                            if (srcInfo.LastWriteTime < destInfo.LastWriteTime)
                            {
                                DeleteOrThrow(destPath, false);
                                RemoveFromIndex(destDir, Path.GetFileName(destPath));
                            }
                            else
                            {
                                if (rule.IsMoveAction)
                                {
                                    DeleteOrThrow(sourcePath, false);
                                    return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, HistoryStatus.DeletedOlderExists);
                                }
                                return CreateHistory(rule.Id, rule.RuleName, itemName, "", sourcePath, destDir, HistoryStatus.SkippedOlderExists);
                            }
                        }
                        else if (resolution == ConflictResolution.Rename)
                        {
                            destPath = GetUniquePath(destDir, itemName, false);
                            newFileName = Path.GetFileName(destPath);
                        }
                    }

                    if (rule.IsMoveAction) File.Move(sourcePath, destPath);
                    else File.Copy(sourcePath, destPath);

                    AddToIndex(destDir, Path.GetFileName(destPath));
                }
            }
            catch (OperationCanceledException)
            {
                // 内容比較の途中でキャンセルされた場合は、何も処理せず中断扱いにする
                return null;
            }
            catch (Exception ex)
            {
                status = HistoryStatus.Error;
                errorMsg = ex.Message;
                _logCallback(Loc.F("log.file_error", itemName, ex.Message));
            }
            finally
            {
                gate.Release();
            }

            return CreateHistory(rule.Id, rule.RuleName, itemName, newFileName, sourcePath, destDir, status, errorMsg);
        }

        // ==========================================
        // 同一ファイルの判定
        // ==========================================

        /// <summary>
        /// 振り分け先に同一ファイルが既にあるか調べる。
        /// 戻り値: ExactMatch(サイズ・更新日時が一致) / ContentMatch(サイズ同一・更新日時違い・中身が同一) / None(重複なし)
        /// </summary>
        private DuplicateKind CheckIfAlreadyExists(string sourcePath, string destDir, string itemName, CancellationToken ct)
        {
            if (!DirExists(destDir)) return DuplicateKind.None;

            var srcInfo = new FileInfo(sourcePath);
            long srcSize = srcInfo.Length;
            DateTime srcTime = srcInfo.LastWriteTime;

            // 「名前」「名前(1)」「名前(2)」のような連番違いを同じグループとして扱う (拡張子も一致している必要がある)
            string baseName = GetBaseNameWithoutSerial(itemName, out string ext);
            var index = GetDestIndex(destDir);
            string[] names = index.GetCandidates(baseName, ext);
            if (names.Length == 0) return DuplicateKind.None;

            // サイズは同じだが更新日時が異なる候補 (内容比較の対象)
            List<string> sameSizeCandidates = null;

            foreach (var destName in names)
            {
                string filePath = Path.Combine(destDir, destName);

                // 万一、処理対象のファイル自身が候補に入っていても、自分自身とは比較しない
                if (string.Equals(filePath, sourcePath, StringComparison.OrdinalIgnoreCase)) continue;

                var destInfo = new FileInfo(filePath);
                if (!destInfo.Exists)
                {
                    // 他のルールの処理などで既に無くなっている
                    index.Remove(destName);
                    continue;
                }

                if (destInfo.Length != srcSize) continue;

                // 簡易判定: サイズ一致 + 更新日時が2秒以内 → I/O なしで即決
                if (Math.Abs((srcTime - destInfo.LastWriteTime).TotalSeconds) <= 2)
                {
                    return DuplicateKind.ExactMatch;
                }

                if (_settings.CompareContentWhenTimestampDiffers)
                {
                    sameSizeCandidates ??= new List<string>();

                    // 同名ファイルは最も一致しやすいので先頭に置く
                    if (string.Equals(destName, itemName, StringComparison.OrdinalIgnoreCase))
                        sameSizeCandidates.Insert(0, filePath);
                    else
                        sameSizeCandidates.Add(filePath);
                }
            }

            // 更新日時の一致候補が無かった場合のみ、内容比較に進む (ここで初めてファイルの中身を読む)
            if (sameSizeCandidates != null && sameSizeCandidates.Count > 0 && IsWithinCompareLimit(srcSize))
            {
                foreach (var candidate in sameSizeCandidates)
                {
                    ContentCompareGate.Wait(ct);
                    try
                    {
                        if (FileContentComparer.AreContentsEqual(sourcePath, candidate, ct))
                        {
                            return DuplicateKind.ContentMatch;
                        }
                    }
                    finally
                    {
                        ContentCompareGate.Release();
                    }
                }
            }

            return DuplicateKind.None;
        }

        private bool IsWithinCompareLimit(long size)
        {
            if (_settings.ContentCompareMaxSizeMB <= 0) return true;
            return size <= (long)_settings.ContentCompareMaxSizeMB * 1024 * 1024;
        }

        private static string GetBaseNameWithoutSerial(string fileName, out string ext)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            ext = Path.GetExtension(fileName);
            var match = SerialRegex.Match(name);
            return match.Success ? match.Groups[1].Value : name;
        }

        // ==========================================
        // 振り分け先フォルダの索引・排他制御
        // ==========================================

        private sealed class DestIndex
        {
            private readonly object _sync = new object();
            // キー: 連番を除いた名前 + 拡張子 → 実際のファイル名の一覧
            private readonly Dictionary<string, List<string>> _map =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            private static string Key(string baseName, string ext) => baseName + "\0" + ext;

            public static DestIndex Build(string dir)
            {
                var index = new DestIndex();
                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(dir))
                    {
                        index.Add(Path.GetFileName(filePath));
                    }
                }
                catch
                {
                    // 列挙できなかった場合は、取得できた分だけで続行する
                }
                return index;
            }

            public void Add(string fileName)
            {
                string baseName = GetBaseNameWithoutSerial(fileName, out string ext);
                string key = Key(baseName, ext);
                lock (_sync)
                {
                    if (!_map.TryGetValue(key, out var list))
                    {
                        list = new List<string>();
                        _map[key] = list;
                    }
                    if (!list.Contains(fileName, StringComparer.OrdinalIgnoreCase)) list.Add(fileName);
                }
            }

            public void Remove(string fileName)
            {
                string baseName = GetBaseNameWithoutSerial(fileName, out string ext);
                string key = Key(baseName, ext);
                lock (_sync)
                {
                    if (_map.TryGetValue(key, out var list))
                    {
                        list.RemoveAll(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            public string[] GetCandidates(string baseName, string ext)
            {
                lock (_sync)
                {
                    return _map.TryGetValue(Key(baseName, ext), out var list) ? list.ToArray() : Array.Empty<string>();
                }
            }
        }

        private static string NormalizeDir(string dir)
        {
            try { return Path.GetFullPath(dir).TrimEnd('\\', '/'); }
            catch { return dir.TrimEnd('\\', '/'); }
        }

        private DestIndex GetDestIndex(string destDir)
        {
            return _destIndexes.GetOrAdd(NormalizeDir(destDir), d => new Lazy<DestIndex>(() => DestIndex.Build(destDir))).Value;
        }

        private void AddToIndex(string destDir, string fileName) => GetDestIndex(destDir).Add(fileName);

        private void RemoveFromIndex(string destDir, string fileName) => GetDestIndex(destDir).Remove(fileName);

        private SemaphoreSlim GetGate(string destDir, string itemName)
        {
            string baseName = GetBaseNameWithoutSerial(itemName, out string ext);
            string key = (NormalizeDir(destDir) + "|" + baseName + ext).ToUpperInvariant();
            int hash = key.GetHashCode() & 0x7FFFFFFF;
            return _gates[hash % GateCount];
        }

        private bool DirExists(string destDir)
        {
            string key = NormalizeDir(destDir);
            if (_knownDirs.ContainsKey(key)) return true;
            if (Directory.Exists(destDir))
            {
                _knownDirs.TryAdd(key, 0);
                return true;
            }
            return false;
        }

        private void EnsureDestDir(string destDir)
        {
            if (DirExists(destDir)) return;
            if (_settings.AutoCreateDestFolder)
            {
                Directory.CreateDirectory(destDir);
                _knownDirs.TryAdd(NormalizeDir(destDir), 0);
            }
        }

        // ==========================================
        // 削除
        // ==========================================
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

        // 削除を試み、実際に消えたことを確認する。消えていなければ例外にする
        // (失敗を成功として履歴に残してしまい、毎回同じファイルを処理し続けるのを防ぐ)
        // 注意: ネットワークドライブ等ごみ箱の無い場所では、OS の仕様により完全削除になる
        private void DeleteOrThrow(string path, bool isDirectory)
        {
            SafeDelete(path, isDirectory);

            bool stillExists = isDirectory ? Directory.Exists(path) : File.Exists(path);
            if (stillExists)
            {
                throw new IOException(Loc.F("err.delete_failed", path));
            }
        }

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

        private SortHistory CreateHistory(string ruleId, string ruleName, string name, string newName, string src, string dest, string status, string error = "")
        {
            return new SortHistory
            {
                Timestamp = DateTime.Now,
                RuleId = ruleId,
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
