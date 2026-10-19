using System;
using System.IO;

namespace Classify8.Core
{
    // ==========================================
    // 振り分け元として危険なフォルダを判定する
    // (ドライブ全体を検索して移動してしまう事故や、システムフォルダの破壊を防ぐ)
    // ==========================================
    public static class PathSafety
    {
        public static bool IsDangerousSource(string path, bool searchSubDirectories, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(path)) return false;

            string full;
            string root;
            try
            {
                string fullWithSep = Path.GetFullPath(path.Trim());
                root = (Path.GetPathRoot(fullWithSep) ?? "").TrimEnd('\\', '/');
                full = fullWithSep.TrimEnd('\\', '/');
            }
            catch
            {
                // MTP(スマホ等)や仮想ドライブなど、判定できないものは許可する
                return false;
            }

            // ドライブ直下 (サブフォルダ検索ありの場合のみ危険とみなす)
            if (searchSubDirectories && string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
            {
                reason = Loc.T("safety.drive_root");
                return true;
            }

            // システムフォルダ (配下も含む)
            foreach (var folder in new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            })
            {
                string sys = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(sys)) continue;
                sys = sys.TrimEnd('\\', '/');

                if (string.Equals(full, sys, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(sys + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    reason = Loc.T("safety.system_folder");
                    return true;
                }
            }

            return false;
        }
    }
}
