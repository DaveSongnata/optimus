using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Optimus.Windows
{
    /// <summary>
    /// The modern Windows folder picker — the same dialog Explorer and every current application
    /// shows: clickable breadcrumb path, search box, favourites, resizable, and it accepts a pasted
    /// path.
    ///
    /// <para>
    /// It exists because <c>System.Windows.Forms.FolderBrowserDialog</c> on .NET Framework 4.8 still
    /// wraps <c>SHBrowseForFolder</c>, an API from the 90s: the little tree with "Network / Control
    /// Panel / Recycle Bin", no way to type or paste a path, and no search. .NET Core 3.0 swapped
    /// that class's internals for this same dialog, but the add-in targets net48 and inherited the
    /// old one. Pasting a path is not a nicety here — it is how anyone reaches a folder on a shop's
    /// network share.
    /// </para>
    /// <para>
    /// Called through COM interop rather than a package: no new dependency to ship, and it matches
    /// how the rest of this codebase already talks to Windows and to CorelDRAW.
    /// </para>
    /// </summary>
    public static class FolderPicker
    {
        /// <summary>
        /// Whether the modern dialog can actually be created and talked to on this machine.
        ///
        /// <para>
        /// Exercises the whole interop path except <c>Show</c>: create the class, QueryInterface for
        /// IFileDialog, then read and write options. That cast is the part worth proving — a vtable
        /// declared out of order does not fail cleanly, it calls the WRONG method, and inside
        /// CorelDRAW that surfaces as a freeze with no clue attached. Checking it here means the
        /// host is never the test.
        /// </para>
        /// </summary>
        public static bool IsAvailable()
        {
            IFileDialog? dialog = null;
            try
            {
                Type? type = Type.GetTypeFromCLSID(ClsidFileOpenDialog);
                if (type == null) return false;
                dialog = Activator.CreateInstance(type) as IFileDialog;
                if (dialog == null) return false;

                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem);
                dialog.GetOptions(out uint back);
                return (back & FosPickFolders) != 0;
            }
            catch (Exception) { return false; }
            finally
            {
                if (dialog != null) { try { Marshal.ReleaseComObject(dialog); } catch (Exception) { } }
            }
        }

        /// <summary>
        /// Shows the picker. Returns the chosen folder, or null when the operator cancels — or when
        /// the modern dialog is unavailable and the caller should fall back.
        /// </summary>
        public static string? Pick(string title, string? startAt = null)
        {
            IFileDialog? dialog = null;
            try
            {
                // The class is IFileOpenDialog; IFileDialog is the interface that carries everything
                // a folder pick needs, so only that vtable has to be declared.
                Type? type = Type.GetTypeFromCLSID(ClsidFileOpenDialog);
                if (type == null) return null;
                dialog = Activator.CreateInstance(type) as IFileDialog;
                if (dialog == null) return null;

                // FORCEFILESYSTEM keeps the result to a real path: without it the operator can pick a
                // virtual place (a library, "This PC") that has no folder behind it, and every later
                // Directory call on that string fails for a reason nobody can see.
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);

                if (!string.IsNullOrWhiteSpace(title)) dialog.SetTitle(title);

                if (!string.IsNullOrWhiteSpace(startAt) && Directory.Exists(startAt))
                {
                    // SetFolder, not SetDefaultFolder: the caller is saying "reopen where you were",
                    // and SetDefaultFolder only applies the first time the dialog is ever shown.
                    if (SHCreateItemFromParsingName(startAt!, IntPtr.Zero, IidShellItem,
                            out IShellItem? start) == 0 && start != null)
                    {
                        try { dialog.SetFolder(start); }
                        finally { Marshal.ReleaseComObject(start); }
                    }
                }

                // Parenting matters inside CorelDRAW: an unparented dialog can open behind the host
                // window, and the operator sees a frozen panel with no explanation.
                int hr = dialog.Show(ActiveWindow());
                if (hr != 0) return null;   // cancel arrives here as ERROR_CANCELLED

                dialog.GetResult(out IShellItem? item);
                if (item == null) return null;
                try
                {
                    item.GetDisplayName(SigdnFileSysPath, out IntPtr buffer);
                    if (buffer == IntPtr.Zero) return null;
                    try { return Marshal.PtrToStringAuto(buffer); }
                    finally { Marshal.FreeCoTaskMem(buffer); }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            catch (Exception)
            {
                // An unavailable dialog is not a crash: the caller falls back to the old one.
                return null;
            }
            finally
            {
                if (dialog != null) { try { Marshal.ReleaseComObject(dialog); } catch (Exception) { } }
            }
        }

        private static IntPtr ActiveWindow()
        {
            try { return GetActiveWindow(); }
            catch (Exception) { return IntPtr.Zero; }
        }

        // ── interop ─────────────────────────────────────────────────────────────────────────────
        private static readonly Guid ClsidFileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        private static readonly Guid IidShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        private const uint FosPickFolders = 0x00000020;
        private const uint FosForceFileSystem = 0x00000040;
        private const uint FosPathMustExist = 0x00000800;
        private const uint SigdnFileSysPath = 0x80058000;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr bindCtx,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

        /// <remarks>
        /// The declaration order IS the vtable. Every method has to be listed, in order, including
        /// the ones this code never calls — leave one out and the calls after it dispatch to the
        /// wrong slot, which does not fail cleanly.
        /// </remarks>
        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr parent);

            // IFileDialog
            void SetFileTypes(uint count, IntPtr filterSpec);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem? folder);
            void GetCurrentSelection(out IShellItem? item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem? item);
            void AddPlace(IShellItem place, int order);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close([MarshalAs(UnmanagedType.Error)] int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindCtx, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem? parent);
            void GetDisplayName(uint sigdnName, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
