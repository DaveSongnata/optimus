using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Optimus.Windows.Maintenance
{
    /// <summary>What is in the Recycle Bin, before anything is emptied.</summary>
    public sealed class RecycleBinContents
    {
        public long TotalBytes { get; set; }
        public long ItemCount { get; set; }

        /// <summary>The biggest items, so the operator can spot the file they are about to lose forever.</summary>
        public List<CleanupCandidate> Largest { get; } = new List<CleanupCandidate>();

        public bool IsEmpty => ItemCount == 0 && TotalBytes == 0;
    }

    /// <summary>
    /// Reads and empties the Recycle Bin.
    ///
    /// <para>
    /// The Bin is the operator's LAST line of defence against their own mistake, so emptying it is never
    /// silent and never bundled with the safe operations: <see cref="Query"/> runs first and the UI shows
    /// the total plus the five largest items by name. A designer who deleted the wrong artboard last
    /// Tuesday still has it in there.
    /// </para>
    /// <para>
    /// Emptying goes through the shell (<c>SHEmptyRecycleBin</c>) rather than deleting
    /// <c>$Recycle.Bin</c> by hand — the shell keeps the per-user metadata consistent, and hand-deleting
    /// that folder is a known way to make Explorer show a permanently broken bin.
    /// </para>
    /// </summary>
    public static class RecycleBin
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        /// <summary>Size and item count, per drive or machine-wide when <paramref name="driveRoot"/> is null.</summary>
        public static RecycleBinContents Query(string? driveRoot = null)
        {
            var contents = new RecycleBinContents();

            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                if (SHQueryRecycleBin(driveRoot, ref info) == 0)
                {
                    contents.TotalBytes = info.i64Size;
                    contents.ItemCount = info.i64NumItems;
                }
            }
            catch (Exception) { /* an unreadable bin reports empty rather than blocking the app */ }

            CollectLargest(contents, driveRoot);
            return contents;
        }

        /// <summary>
        /// Lists the biggest items by walking <c>$Recycle.Bin</c> read-only. The names are mangled
        /// (<c>$R…</c>), so size is what makes an item recognisable — "you are about to permanently delete
        /// a 2,4 GB file" is information the operator can act on.
        /// </summary>
        private static void CollectLargest(RecycleBinContents contents, string? driveRoot)
        {
            var found = new List<CleanupCandidate>();

            List<string> roots = new List<string>();
            if (!string.IsNullOrEmpty(driveRoot)) roots.Add(driveRoot!);
            else foreach (VolumeInfo v in DiskHealth.Volumes()) roots.Add(v.Root);

            foreach (string root in roots)
            {
                string bin = Path.Combine(root, "$Recycle.Bin");
                if (!Directory.Exists(bin)) continue;

                try
                {
                    foreach (string userDir in Directory.GetDirectories(bin))
                    {
                        try
                        {
                            foreach (string file in Directory.GetFiles(userDir))
                            {
                                string name = Path.GetFileName(file);
                                // $I* files are the metadata records, not the content.
                                if (name.StartsWith("$I", StringComparison.OrdinalIgnoreCase)) continue;

                                var fi = new FileInfo(file);
                                found.Add(new CleanupCandidate
                                {
                                    FullPath = file,
                                    SizeBytes = fi.Length,
                                    LastWriteUtc = fi.LastWriteTimeUtc,
                                    RuleId = "windows.recyclebin",
                                });
                            }
                        }
                        catch (Exception) { /* another user's bin is ACL-protected: expected */ }
                    }
                }
                catch (Exception) { }
            }

            found.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            for (int i = 0; i < found.Count && i < 5; i++) contents.Largest.Add(found[i]);
        }

        /// <summary>
        /// Empties the Bin. Requires the operator's explicit confirmation upstream — this method does not
        /// ask, and there is no undo after it.
        /// </summary>
        public static bool Empty(out string message, string? driveRoot = null)
        {
            try
            {
                int hr = SHEmptyRecycleBin(IntPtr.Zero, driveRoot,
                                           SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                // 0 = done. E_UNEXPECTED (0x8000FFFF) is what an already-empty bin returns.
                if (hr == 0) { message = "Lixeira esvaziada."; return true; }
                if (hr == unchecked((int)0x8000FFFF)) { message = "A Lixeira já estava vazia."; return true; }

                message = "A Lixeira não pôde ser esvaziada (código 0x" + hr.ToString("X8") + ").";
                return false;
            }
            catch (Exception ex)
            {
                message = "Falha ao esvaziar a Lixeira: " + ex.Message;
                return false;
            }
        }
    }
}
