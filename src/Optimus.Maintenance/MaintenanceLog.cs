using System;
using System.IO;
using System.Text;

namespace Optimus.Maintenance
{
    /// <summary>
    /// The app's log at <c>%TEMP%\Optimus\manutencao.log</c>.
    ///
    /// <para>
    /// A maintenance tool deletes files, so the log is part of the product, not debug scaffolding: if the
    /// operator ever asks "what did it remove?", the answer must exist on disk. Every removed path is
    /// written here. It is also the only diagnosis channel available when the app runs on a client machine
    /// nobody can attach a debugger to.
    /// </para>
    /// </summary>
    internal static class MaintenanceLog
    {
        private static readonly object Gate = new object();

        public static string Path
        {
            get
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Optimus");
                try { Directory.CreateDirectory(dir); } catch (Exception) { }
                return System.IO.Path.Combine(dir, "manutencao.log");
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(
                        Path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // Logging must never be the thing that breaks the app.
            }
        }

        /// <summary>Rotates the log when it passes 4 MB, keeping one previous file.</summary>
        public static void RotateIfLarge()
        {
            try
            {
                var info = new FileInfo(Path);
                if (!info.Exists || info.Length < 4L * 1024 * 1024) return;

                string old = Path + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(Path, old);
            }
            catch (Exception) { }
        }
    }
}
