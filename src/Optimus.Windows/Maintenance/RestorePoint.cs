using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace Optimus.Windows.Maintenance
{
    /// <summary>Outcome of asking Windows for a restore point — with no room for a polite lie.</summary>
    public sealed class RestorePointResult
    {
        /// <summary>True only when a NEW sequence number appeared. Nothing else counts.</summary>
        public bool Created { get; set; }

        /// <summary>The new restore point's sequence number, when one was created.</summary>
        public long? SequenceNumber { get; set; }

        /// <summary>True when System Restore is turned off for the system drive.</summary>
        public bool SystemRestoreDisabled { get; set; }

        /// <summary>
        /// True when Windows returned success but created nothing because a restore point already exists
        /// from the last 24 hours (the <c>SystemRestorePointCreationFrequency</c> throttle).
        /// </summary>
        public bool ThrottledByExistingPoint { get; set; }

        /// <summary>Sequence number of the most recent point found, created now or pre-existing.</summary>
        public long? LatestExistingSequence { get; set; }

        public DateTime? LatestExistingUtc { get; set; }

        public string Message { get; set; } = "";

        /// <summary>
        /// True when it is safe to proceed with a reversible-by-restore operation: either we created a
        /// point, or a recent one already covers the machine.
        /// </summary>
        public bool HasUsableProtection =>
            Created || (ThrottledByExistingPoint && LatestExistingSequence.HasValue);
    }

    /// <summary>
    /// Creates and — crucially — VERIFIES a System Restore point.
    ///
    /// <para>
    /// Two traps, both of which make naive code claim protection it does not have:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <c>SystemRestore.CreateRestorePoint</c> <b>returns success (0) while doing nothing</b> if a restore
    /// point was created in the previous 24 hours. The only way to know a point exists is to compare the
    /// <b>sequence numbers</b> before and after.
    /// </description></item>
    /// <item><description>
    /// System Restore is <b>disabled by default on many OEM machines</b> and on most Windows Server
    /// installs. Calling into it then throws, and "protection enabled" would be a flat lie.
    /// </description></item>
    /// </list>
    /// <para>
    /// So: enumerate before, call, enumerate after, and report exactly which of the three worlds we are in.
    /// Nothing destructive may run unless <see cref="RestorePointResult.HasUsableProtection"/> is true or
    /// the operator explicitly waives it.
    /// </para>
    /// </summary>
    public static class RestorePoint
    {
        private const string DefaultNamespace = @"root\default";

        /// <summary>MODIFY_SETTINGS — the correct type for "I am about to change this machine".</summary>
        private const uint TypeModifySettings = 12;

        /// <summary>BEGIN_SYSTEM_CHANGE.</summary>
        private const uint EventBeginSystemChange = 100;

        public static RestorePointResult Create(string description)
        {
            var result = new RestorePointResult();

            if (!IsEnabled(out string why))
            {
                result.SystemRestoreDisabled = true;
                result.Message = why;
                return result;
            }

            List<RestorePointEntry> before = Existing();
            long maxBefore = MaxSequence(before);

            RestorePointEntry? newest = Newest(before);
            result.LatestExistingSequence = newest?.SequenceNumber;
            result.LatestExistingUtc = newest?.CreationUtc;

            uint returnValue;
            try
            {
                returnValue = Invoke(description);
            }
            catch (Exception ex)
            {
                // COM failures here often arrive with an EMPTY Message (measured on Davi's VM:
                // "Não foi possível criar o ponto de restauração: " with nothing after the colon).
                // An empty reason tells the operator nothing, so name the most likely cause instead.
                string reason = string.IsNullOrWhiteSpace(ex.Message)
                    ? "o Windows recusou a chamada sem dar um motivo — normalmente é a Proteção do "
                    + "Sistema desligada no disco do Windows, ou uma edição do Windows que não a inclui"
                    : ex.Message;

                result.Message = "Não foi possível criar o ponto de restauração: " + reason
                               + ". A limpeza pode seguir, mas sem esse ponto de retorno — e apagar "
                               + "arquivo não é revertido por ponto de restauração de qualquer forma.";
                return result;
            }

            // The verification that the API itself will not do for us.
            List<RestorePointEntry> after = Existing();
            long maxAfter = MaxSequence(after);

            if (maxAfter > maxBefore)
            {
                result.Created = true;
                result.SequenceNumber = maxAfter;
                result.LatestExistingSequence = maxAfter;
                result.LatestExistingUtc = Newest(after)?.CreationUtc;
                result.Message = "Ponto de restauração criado (nº " + maxAfter + ").";
                return result;
            }

            if (newest != null)
            {
                result.ThrottledByExistingPoint = true;
                result.Message =
                    "O Windows não criou um ponto novo porque já existe um recente (nº "
                    + newest.SequenceNumber + ", de " + newest.CreationUtc.ToLocalTime().ToString("g")
                    + "). Esse ponto já protege a máquina.";
                return result;
            }

            result.Message = returnValue == 0
                ? "O Windows informou sucesso, mas nenhum ponto de restauração apareceu — "
                + "a proteção do sistema pode estar desativada para o disco do Windows."
                : "O Windows recusou criar o ponto de restauração (código " + returnValue + ").";
            return result;
        }

        /// <summary>One restore point as the machine reports it.</summary>
        public sealed class RestorePointEntry
        {
            public long SequenceNumber { get; set; }
            public string Description { get; set; } = "";
            public DateTime CreationUtc { get; set; }
        }

        public static List<RestorePointEntry> Existing()
        {
            var list = new List<RestorePointEntry>();
            try
            {
                var scope = new ManagementScope(DefaultNamespace);
                using (var searcher = new ManagementObjectSearcher(
                           scope, new ObjectQuery("SELECT * FROM SystemRestore")))
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        list.Add(new RestorePointEntry
                        {
                            SequenceNumber = Convert.ToInt64(mo["SequenceNumber"]),
                            Description = mo["Description"]?.ToString() ?? "",
                            CreationUtc = ParseWmiDate(mo["CreationTime"]?.ToString()),
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Disabled or unavailable: an empty list, which the caller reads as "no protection".
            }
            return list;
        }

        /// <summary>
        /// True when System Restore can actually be used. Checked before calling so the operator gets an
        /// actionable message ("ative a proteção do sistema") instead of a COM error code.
        /// </summary>
        public static bool IsEnabled(out string reason)
        {
            reason = "";
            try
            {
                string systemDrive = Path.GetPathRoot(
                    Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";

                var scope = new ManagementScope(DefaultNamespace);
                scope.Connect();

                using (var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
                {
                    // Enumerating instances is the cheapest probe that does not mutate anything.
                    // A working-but-empty System Restore is legitimate on a fresh install, so absence of
                    // points is NOT treated as disabled here.
                    cls.GetInstances().GetEnumerator();
                }

                if (!IsProtectionOn(systemDrive))
                {
                    reason = "A Proteção do Sistema está desativada no disco " + systemDrive
                           + ". Sem ela o Windows não cria pontos de restauração.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = "A Proteção do Sistema não está disponível nesta máquina (" + ex.Message + ").";
                return false;
            }
        }

        /// <summary>
        /// Reads the per-volume protection flag. <c>SystemRestore.Disable/Enable</c> is deliberately NOT
        /// called: turning protection on without being asked changes the customer's disk budget.
        /// </summary>
        private static bool IsProtectionOn(string driveRoot)
        {
            try
            {
                var scope = new ManagementScope(DefaultNamespace);
                using (var searcher = new ManagementObjectSearcher(
                           scope, new ObjectQuery("SELECT * FROM SystemRestoreConfig")))
                foreach (ManagementObject mo in searcher.Get())
                    using (mo)
                        return true;   // the class answering at all means the service is present
            }
            catch (Exception) { }

            // Fall back to the shadow-storage view: no shadow storage on the system drive means no
            // restore points can be written there.
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                           "SELECT * FROM Win32_ShadowStorage"))
                    foreach (ManagementObject mo in searcher.Get())
                        using (mo)
                            return true;
            }
            catch (Exception) { }

            return false;
        }

        private static uint Invoke(string description)
        {
            var scope = new ManagementScope(DefaultNamespace);
            scope.Connect();

            using (var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
            {
                ManagementBaseObject args = cls.GetMethodParameters("CreateRestorePoint");
                args["Description"] = description;
                args["RestorePointType"] = TypeModifySettings;
                args["EventType"] = EventBeginSystemChange;

                using (ManagementBaseObject outParams = cls.InvokeMethod("CreateRestorePoint", args, null))
                    return Convert.ToUInt32(outParams["ReturnValue"] ?? 0u);
            }
        }

        private static long MaxSequence(List<RestorePointEntry> points)
        {
            long max = 0;
            foreach (RestorePointEntry p in points) if (p.SequenceNumber > max) max = p.SequenceNumber;
            return max;
        }

        private static RestorePointEntry? Newest(List<RestorePointEntry> points)
        {
            RestorePointEntry? best = null;
            foreach (RestorePointEntry p in points)
                if (best == null || p.SequenceNumber > best.SequenceNumber) best = p;
            return best;
        }

        /// <summary>Parses a WMI DMTF datetime (<c>yyyyMMddHHmmss.ffffff+zzz</c>).</summary>
        internal static DateTime ParseWmiDate(string? dmtf)
        {
            if (string.IsNullOrEmpty(dmtf) || dmtf!.Length < 14) return DateTime.MinValue;
            try
            {
                int year = int.Parse(dmtf.Substring(0, 4));
                int month = int.Parse(dmtf.Substring(4, 2));
                int day = int.Parse(dmtf.Substring(6, 2));
                int hour = int.Parse(dmtf.Substring(8, 2));
                int minute = int.Parse(dmtf.Substring(10, 2));
                int second = int.Parse(dmtf.Substring(12, 2));
                return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            }
            catch (Exception) { return DateTime.MinValue; }
        }
    }
}
