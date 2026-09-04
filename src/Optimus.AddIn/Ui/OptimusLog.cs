using System;
using System.IO;

namespace Optimus.AddIn.Ui
{
    /// <summary>
    /// Dirt-simple file logger for diagnosing the addon docker on the VM (no console there).
    /// Writes to %TEMP%\Optimus\docker.log. Best-effort: never throws.
    /// </summary>
    internal static class OptimusLog
    {
        private static readonly object Gate = new object();
        private static readonly string Path = BuildPath();

        private static string BuildPath()
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Optimus");
                Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "docker.log");
            }
            catch { return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "optimus_docker.log"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch { /* never break the UI over logging */ }
        }
    }
}
