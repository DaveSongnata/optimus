using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace Optimus.Windows.Maintenance
{
    /// <summary>Physical media kind. Decides whether defragmenting is help or harm.</summary>
    public enum MediaKind
    {
        Unknown = 0,
        Hdd = 3,
        Ssd = 4,
        /// <summary>Storage-class memory (Optane and friends). Never defragment.</summary>
        Scm = 5,
    }

    /// <summary>What the drive says about itself.</summary>
    public enum DriveHealth
    {
        Unknown = -1,
        Healthy = 0,
        Warning = 1,
        Unhealthy = 2,
    }

    /// <summary>One physical disk, as the operator should read it.</summary>
    public sealed class PhysicalDiskInfo
    {
        public string FriendlyName { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public MediaKind Media { get; set; } = MediaKind.Unknown;
        public DriveHealth Health { get; set; } = DriveHealth.Unknown;
        public long SizeBytes { get; set; }

        /// <summary>Hours powered on, when the firmware reports it (SSD wear context).</summary>
        public int? PowerOnHours { get; set; }

        /// <summary>Remaining write endurance, 0–100, when the firmware reports it.</summary>
        public int? WearRemainingPercent { get; set; }

        /// <summary>
        /// <c>defrag /O</c> — "optimize", which is the ONLY correct verb: on an HDD it defragments, on an
        /// SSD it issues TRIM/retrim. Never <c>/D</c>, which forces a defragment and burns SSD writes for
        /// no gain.
        /// </summary>
        public bool ShouldOptimize => Media == MediaKind.Hdd || Media == MediaKind.Ssd;

        public string MediaLabel()
        {
            switch (Media)
            {
                case MediaKind.Hdd: return "HD mecânico";
                case MediaKind.Ssd: return "SSD";
                case MediaKind.Scm: return "memória persistente";
                default: return "tipo não informado";
            }
        }

        /// <summary>What the operator reads. A drive in Warning is a backup-now message, not a metric.</summary>
        public string HealthLabel()
        {
            switch (Health)
            {
                case DriveHealth.Healthy: return "Saudável";
                case DriveHealth.Warning: return "ATENÇÃO — faça backup agora";
                case DriveHealth.Unhealthy: return "FALHANDO — faça backup imediatamente";
                default: return "O disco não informa o estado de saúde";
            }
        }
    }

    /// <summary>One logical volume with its free space.</summary>
    public sealed class VolumeInfo
    {
        public string Root { get; set; } = "";
        public string Label { get; set; } = "";
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }

        public double FreePercent => TotalBytes > 0 ? 100.0 * FreeBytes / TotalBytes : 0;

        /// <summary>
        /// Below ~10% free, Windows itself starts to suffer (paging, servicing, defrag all need room),
        /// and that is a genuine cause of the "travando" the operator complains about.
        /// </summary>
        public bool IsCritical => TotalBytes > 0 && FreePercent < 10;
    }

    /// <summary>
    /// Reads media type and health from WMI.
    ///
    /// <para>
    /// SMART is layered on <c>MSFT_PhysicalDisk.HealthStatus</c> (namespace
    /// <c>root\Microsoft\Windows\Storage</c>), NOT on the classic
    /// <c>MSStorageDriver_FailurePredictStatus</c> — that legacy class returns "not supported" on NVMe,
    /// which is most of the drives a shop bought in the last five years. When neither answers, the honest
    /// output is <see cref="DriveHealth.Unknown"/> with an explanation, never a green tick.
    /// </para>
    /// </summary>
    public static class DiskHealth
    {
        private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

        public static List<PhysicalDiskInfo> PhysicalDisks()
        {
            var disks = new List<PhysicalDiskInfo>();

            try
            {
                var scope = new ManagementScope(StorageNamespace);
                var query = new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk");
                using (var searcher = new ManagementObjectSearcher(scope, query))
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        disks.Add(new PhysicalDiskInfo
                        {
                            FriendlyName = Str(mo, "FriendlyName"),
                            SerialNumber = Str(mo, "SerialNumber"),
                            Media = ToMediaKind(Num(mo, "MediaType")),
                            Health = ToHealth(Num(mo, "HealthStatus")),
                            SizeBytes = (long)(Num(mo, "Size") ?? 0),
                        });
                    }
                }

                AttachReliabilityCounters(disks);
            }
            catch (Exception)
            {
                // Storage namespace missing (very old Windows) or WMI broken: report nothing rather
                // than guessing. A wrong "SSD" answer would send defrag /D at a mechanical disk.
            }

            return disks;
        }

        /// <summary>
        /// Wear and power-on hours come from <c>MSFT_StorageReliabilityCounter</c>, which many consumer
        /// drives simply do not populate — hence nullable, never a fabricated 100%.
        /// </summary>
        private static void AttachReliabilityCounters(List<PhysicalDiskInfo> disks)
        {
            if (disks.Count == 0) return;

            try
            {
                var scope = new ManagementScope(StorageNamespace);
                var query = new ObjectQuery("SELECT * FROM MSFT_StorageReliabilityCounter");
                using (var searcher = new ManagementObjectSearcher(scope, query))
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        // DeviceId on the counter matches the disk's SerialNumber-bearing instance path;
                        // when we cannot correlate we attach nothing rather than mislabel a drive.
                        string id = Str(mo, "DeviceId");
                        PhysicalDiskInfo? match = disks.Find(d =>
                            !string.IsNullOrEmpty(id) &&
                            (id.IndexOf(d.SerialNumber, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             d.SerialNumber.Length == 0 && disks.Count == 1));
                        if (match == null) continue;

                        double? hours = Num(mo, "PowerOnHours");
                        double? wear = Num(mo, "Wear");
                        if (hours.HasValue) match.PowerOnHours = (int)hours.Value;
                        if (wear.HasValue) match.WearRemainingPercent = (int)Math.Max(0, 100 - wear.Value);
                    }
                }
            }
            catch (Exception) { /* counters are a bonus, never a requirement */ }
        }

        /// <summary>Every fixed volume with real free space, read from the filesystem (not WMI).</summary>
        public static List<VolumeInfo> Volumes()
        {
            var list = new List<VolumeInfo>();
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch (Exception) { return list; }

            foreach (DriveInfo d in drives)
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    list.Add(new VolumeInfo
                    {
                        Root = d.RootDirectory.FullName,
                        Label = d.VolumeLabel ?? "",
                        TotalBytes = d.TotalSize,
                        FreeBytes = d.TotalFreeSpace,
                    });
                }
                catch (Exception) { /* a drive that disappears mid-enumeration is not an error */ }
            }
            return list;
        }

        /// <summary>
        /// Free bytes on the volume that holds <paramref name="path"/>. This is the measurement the
        /// headline saving is computed from, so it must come from the drive, never from summed file sizes.
        /// </summary>
        public static long FreeBytesOn(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(path) ?? "C:\\").TotalFreeSpace; }
            catch (Exception) { return 0; }
        }

        internal static MediaKind ToMediaKind(double? raw)
        {
            if (!raw.HasValue) return MediaKind.Unknown;
            switch ((int)raw.Value)
            {
                case 3: return MediaKind.Hdd;
                case 4: return MediaKind.Ssd;
                case 5: return MediaKind.Scm;
                default: return MediaKind.Unknown;
            }
        }

        internal static DriveHealth ToHealth(double? raw)
        {
            if (!raw.HasValue) return DriveHealth.Unknown;
            switch ((int)raw.Value)
            {
                case 0: return DriveHealth.Healthy;
                case 1: return DriveHealth.Warning;
                case 2: return DriveHealth.Unhealthy;
                default: return DriveHealth.Unknown;
            }
        }

        private static string Str(ManagementBaseObject mo, string name)
        {
            try { return mo[name]?.ToString()?.Trim() ?? ""; } catch (Exception) { return ""; }
        }

        private static double? Num(ManagementBaseObject mo, string name)
        {
            try
            {
                object? v = mo[name];
                return v == null ? (double?)null : Convert.ToDouble(v);
            }
            catch (Exception) { return null; }
        }
    }
}
