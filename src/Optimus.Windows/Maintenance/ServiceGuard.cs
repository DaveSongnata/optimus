using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace Optimus.Windows.Maintenance
{
    /// <summary>
    /// Stops a Windows service for the duration of a cleanup and <b>always puts it back</b>.
    ///
    /// <para>
    /// Two folders can only be cleaned with their owner stopped: Windows Update's download cache
    /// (<c>wuauserv</c>) and the print spool (<c>Spooler</c>). Leaving either stopped is a far worse
    /// outcome than not cleaning — a print shop with a dead spooler cannot work at all — so the restore
    /// runs from a <c>finally</c>, and the object remembers whether the service was running to begin with
    /// (restarting a service the customer had deliberately disabled is its own kind of damage).
    /// </para>
    /// </summary>
    public sealed class ServiceGuard : IDisposable
    {
        private readonly string _serviceName;
        private readonly Action<string>? _log;
        private bool _wasRunning;
        private bool _stoppedByUs;

        public ServiceGuard(string serviceName, Action<string>? log = null)
        {
            _serviceName = serviceName;
            _log = log;
        }

        /// <summary>True when the service is now stopped and the folder can be cleaned.</summary>
        public bool Stopped { get; private set; }

        public string Message { get; private set; } = "";

        public bool TryStop(int timeoutSeconds = 30)
        {
            try
            {
                using (var sc = new ServiceController(_serviceName))
                {
                    _wasRunning = sc.Status == ServiceControllerStatus.Running
                               || sc.Status == ServiceControllerStatus.StartPending;

                    if (!_wasRunning)
                    {
                        Stopped = true;              // already stopped: nothing to undo
                        Message = "Serviço " + _serviceName + " já estava parado.";
                        return true;
                    }

                    if (!sc.CanStop)
                    {
                        Message = "O serviço " + _serviceName + " não pode ser parado nesta máquina.";
                        return false;
                    }

                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped,
                                     TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
                    _stoppedByUs = true;
                    Stopped = true;
                    Message = "Serviço " + _serviceName + " parado temporariamente.";
                    _log?.Invoke(Message);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Message = "Não foi possível parar o serviço " + _serviceName + ": " + ex.Message;
                _log?.Invoke(Message);
                return false;
            }
        }

        /// <summary>Restores the previous state. Called from <see cref="Dispose"/>, so it always runs.</summary>
        public void Restore()
        {
            if (!_stoppedByUs) return;
            _stoppedByUs = false;

            try
            {
                using (var sc = new ServiceController(_serviceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                    }
                    _log?.Invoke("Serviço " + _serviceName + " religado.");
                }
            }
            catch (Exception ex)
            {
                // Last resort: the shop MUST get its spooler back, so try the command line too.
                _log?.Invoke("FALHA ao religar " + _serviceName + " via API: " + ex.Message
                           + " — tentando net start.");
                try
                {
                    using (Process? p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = "start " + _serviceName,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }))
                        p?.WaitForExit(60_000);
                }
                catch (Exception ex2)
                {
                    _log?.Invoke("FALHA CRÍTICA ao religar " + _serviceName + ": " + ex2.Message);
                }
            }
        }

        public void Dispose() => Restore();

        /// <summary>Whether a service exists at all — checked before offering the operation.</summary>
        public static bool Exists(string serviceName)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    ServiceControllerStatus _ = sc.Status;   // throws when absent
                    return true;
                }
            }
            catch (Exception) { return false; }
        }
    }
}
