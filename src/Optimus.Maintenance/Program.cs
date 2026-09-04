using System;
using System.Threading;
using System.Windows;
using Optimus.Core.I18n;
using Optimus.Windows;

namespace Optimus.Maintenance
{
    /// <summary>
    /// Entry point of the standalone maintenance app.
    ///
    /// <para>
    /// A single instance is enforced with a named mutex: two copies scanning and deleting the same folders
    /// would race, and the free-space measurement of each would be corrupted by the other — producing
    /// exactly the invented numbers this product refuses to show.
    /// </para>
    /// </summary>
    internal static class Program
    {
        internal const string VersionTag = "1.65.0";

        private static Mutex? _single;

        [STAThread]
        private static int Main()
        {
            MaintenanceLog.RotateIfLarge();
            MaintenanceLog.Write("=== Optimus Manutenção v" + VersionTag + " ===");

            bool owned;
            try
            {
                _single = new Mutex(true, @"Global\OptimusManutencao", out owned);
            }
            catch (Exception ex)
            {
                MaintenanceLog.Write("Mutex falhou (seguindo assim mesmo): " + ex.Message);
                owned = true;
            }

            LocalizationService i18n = LanguageStore.Service();

            if (!owned)
            {
                MaintenanceLog.Write("Outra instância já está aberta — encerrando esta.");
                MessageBox.Show(i18n["mnt.already.open"],
                    "Optimus", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

                // A crash in a maintenance tool is alarming out of proportion to its cause, so every
                // managed failure degrades to a log line plus a plain message instead of a stack trace.
                app.DispatcherUnhandledException += (_, e) =>
                {
                    MaintenanceLog.Write("DispatcherUnhandledException: " + e.Exception);
                    e.Handled = true;
                    MessageBox.Show(
                        i18n["mnt.crash.notice"].Replace("{0}", MaintenanceLog.Path),
                        "Optimus", MessageBoxButton.OK, MessageBoxImage.Warning);
                };

                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                    MaintenanceLog.Write("FATAL UnhandledException: " + e.ExceptionObject);

                var window = new MaintenanceWindow();
                app.Run(window);
                MaintenanceLog.Write("Encerrado normalmente.");
                return 0;
            }
            catch (Exception ex)
            {
                MaintenanceLog.Write("FATAL na inicialização: " + ex);
                MessageBox.Show(
                    LanguageStore.Service()["mnt.start.failed"].Replace("{0}", ex.Message),
                    "Optimus", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            finally
            {
                try { _single?.ReleaseMutex(); } catch (Exception) { }
                try { _single?.Dispose(); } catch (Exception) { }
            }
        }
    }
}
