using System;
using System.Buffers;
using System.IO;
using System.Threading;

namespace Classify8.Core
{
    /// <summary>
    /// 2つのファイルの内容(バイト列)が完全に同一かを高速に判定する。
    /// ・サイズが違えば即 false
    /// ・末尾を先に比較して、違うファイルを全体を読む前に弾く
    /// ・先頭から大きなバッファで比較し、差が見つかった時点で打ち切る
    /// </summary>
    public static class FileContentComparer
    {
        private const int BufferSize = 256 * 1024;  // 全体比較用バッファ
        private const int TailSample = 64 * 1024;   // 先に比べる末尾サイズ

        /// <summary>
        /// 内容が同一なら true。読み込めない場合(使用中・権限なし等)は false を返す
        /// (誤って元ファイルを削除しないよう、不明な場合は「別ファイル」扱い)。
        /// キャンセル時は OperationCanceledException を投げる。
        /// </summary>
        public static bool AreContentsEqual(string pathA, string pathB, CancellationToken ct = default)
        {
            try
            {
                var a = new FileInfo(pathA);
                var b = new FileInfo(pathB);
                if (!a.Exists || !b.Exists) return false;
                if (a.Length != b.Length) return false;
                if (a.Length == 0) return true;

                const FileShare share = FileShare.ReadWrite | FileShare.Delete;
                // bufferSize=1 で FileStream 内部バッファを無効化 (大きなチャンクで自前読み込みするため)
                using var fa = new FileStream(pathA, FileMode.Open, FileAccess.Read, share, 1, FileOptions.SequentialScan);
                using var fb = new FileStream(pathB, FileMode.Open, FileAccess.Read, share, 1, FileOptions.SequentialScan);

                byte[] bufA = ArrayPool<byte>.Shared.Rent(BufferSize);
                byte[] bufB = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    // 1) 末尾を先に比較 (違うファイルを早く弾く)
                    if (a.Length > TailSample * 2)
                    {
                        fa.Seek(-TailSample, SeekOrigin.End);
                        fb.Seek(-TailSample, SeekOrigin.End);
                        int ta = fa.ReadAtLeast(bufA.AsSpan(0, TailSample), TailSample, false);
                        int tb = fb.ReadAtLeast(bufB.AsSpan(0, TailSample), TailSample, false);
                        if (ta != tb || !bufA.AsSpan(0, ta).SequenceEqual(bufB.AsSpan(0, tb)))
                            return false;

                        fa.Seek(0, SeekOrigin.Begin);
                        fb.Seek(0, SeekOrigin.Begin);
                    }

                    // 2) 先頭から全体を比較 (差があれば即終了)
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();

                        int ra = fa.ReadAtLeast(bufA.AsSpan(0, BufferSize), BufferSize, false);
                        int rb = fb.ReadAtLeast(bufB.AsSpan(0, BufferSize), BufferSize, false);

                        if (ra != rb) return false;
                        if (ra == 0) return true;
                        if (!bufA.AsSpan(0, ra).SequenceEqual(bufB.AsSpan(0, rb))) return false;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bufA);
                    ArrayPool<byte>.Shared.Return(bufB);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
        }
    }
}
