using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Optimus.Windows.Maintenance
{
    /// <summary>
    /// Stops and restarts Explorer around the thumbnail-cache cleanup.
    ///
    /// <para>
    /// The cache databases are held open by <c>explorer.exe</c> for as long as it runs, so the files simply
    /// cannot be deleted while the desktop is up. The trade is one or two seconds of a blank taskbar — which
    /// the UI warns about beforehand — and the payoff is real for this customer: designers browse folders
    /// full of huge images, and a corrupted thumbnail cache is exactly why previews show the wrong artwork.
    /// </para>
    /// <para>
    /// Explorer is <b>always</b> restarted from a <c>finally</c>. Leaving a shop machine with no taskbar is
    /// not an acceptable failure mode, so if the restart fails it is retried explicitly.
    /// </para>
    /// </summary>
    public static class ExplorerHost
    {
        /// <summary>
        /// Runs <paramref name="action"/> with Explorer stopped, then brings it back.
        /// Returns whatever the action produced; the restart happens either way.
        /// </summary>
        public static void WithExplorerStopped(Action action, Action<string>? log = null)
        {
            bool stopped = Stop(log);
            try
            {
                action();
            }
            finally
            {
                if (stopped) Start(log);
            }
        }

        private static bool Stop(Action<string>? log)
        {
            bool any = false;
            try
            {
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(10_000);
                        any = true;
                    }
                    catch (Exception ex) { log?.Invoke("Explorer não pôde ser encerrado: " + ex.Message); }
                    finally { try { p.Dispose(); } catch { } }
                }

                if (any)
                {
                    // Explorer releases the cache handles asynchronously; deleting immediately still hits
                    // a lock on a slow machine.
                    Thread.Sleep(1200);
                    log?.Invoke("Explorer encerrado temporariamente.");
                }
            }
            catch (Exception ex) { log?.Invoke("Falha ao encerrar o Explorer: " + ex.Message); }

            return any;
        }

        private static void Start(Action<string>? log)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    // Windows normally auto-restarts the shell; starting it explicitly covers the machines
                    // where AutoRestartShell is disabled.
                    if (Process.GetProcessesByName("explorer").Length > 0)
                    {
                        log?.Invoke("Explorer já voltou sozinho.");
                        return;
                    }

                    string exe = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

                    Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true })?.Dispose();
                    Thread.Sleep(800);

                    if (Process.GetProcessesByName("explorer").Length > 0)
                    {
                        log?.Invoke("Explorer religado.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke("Tentativa " + attempt + " de religar o Explorer falhou: " + ex.Message);
                }
            }

            log?.Invoke("ATENÇÃO: o Explorer não voltou. Pressione Ctrl+Shift+Esc, Arquivo → "
                      + "Executar nova tarefa → explorer.exe");
        }
    }
}
