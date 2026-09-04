using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Optimus.Windows.Maintenance
{
    /// <summary>One real Windows user profile found on this machine.</summary>
    public sealed class UserProfile
    {
        public string Sid { get; set; } = "";
        public string ProfilePath { get; set; } = "";

        /// <summary>Leaf folder name, which is what the operator recognises ("Davi", "Producao").</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(ProfilePath) ? Sid : Path.GetFileName(ProfilePath.TrimEnd('\\'));

        public string LocalAppData => Path.Combine(ProfilePath, @"AppData\Local");
        public string RoamingAppData => Path.Combine(ProfilePath, @"AppData\Roaming");
        public string Temp => Path.Combine(ProfilePath, @"AppData\Local\Temp");

        public override string ToString() => DisplayName + " (" + ProfilePath + ")";
    }

    /// <summary>
    /// Enumerates the machine's real user profiles from the registry.
    ///
    /// <para>
    /// THE reason this class exists: a maintenance app runs <b>elevated</b>, and once elevated
    /// <c>Environment.GetFolderPath(LocalApplicationData)</c> and <c>%TEMP%</c> resolve to the
    /// <b>administrator's</b> profile — not the profile of the person whose machine is full. That single
    /// mistake is the number-one reason cleaners "free nothing", and it is precisely the v1.0 bug: it
    /// cleaned an admin profile that had never opened CorelDRAW.
    /// </para>
    /// <para>
    /// The authoritative list is
    /// <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\&lt;SID&gt;\ProfileImagePath</c>.
    /// Service accounts (SIDs S-1-5-18/19/20) are excluded — cleaning those is how you break Windows
    /// Update — and so are profiles whose folder no longer exists (stale registry entries are common).
    /// </para>
    /// </summary>
    public static class UserProfiles
    {
        private const string ProfileListKey =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

        /// <summary>
        /// Every real interactive profile on the machine. Never throws: a machine with a locked-down
        /// registry degrades to the current profile rather than failing the whole run.
        /// </summary>
        public static List<UserProfile> All()
        {
            var found = new List<UserProfile>();

            try
            {
                using (RegistryKey? root = RegistryKey
                        .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                        .OpenSubKey(ProfileListKey))
                {
                    if (root != null)
                    {
                        foreach (string sid in root.GetSubKeyNames())
                        {
                            if (!IsInteractiveSid(sid)) continue;

                            using (RegistryKey? sub = root.OpenSubKey(sid))
                            {
                                string? path = sub?.GetValue("ProfileImagePath") as string;
                                if (string.IsNullOrWhiteSpace(path)) continue;

                                path = Environment.ExpandEnvironmentVariables(path!);

                                // A registry entry whose folder is gone is a leftover, not a profile.
                                if (!Directory.Exists(path)) continue;

                                found.Add(new UserProfile { Sid = sid, ProfilePath = path });
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Registry unreadable — fall through to the current-profile fallback below.
            }

            if (found.Count == 0)
            {
                string mine = CurrentProfilePath();
                if (!string.IsNullOrEmpty(mine))
                    found.Add(new UserProfile { Sid = "current", ProfilePath = mine });
            }

            return found;
        }

        /// <summary>
        /// True for a normal user SID. <c>S-1-5-18</c> (LocalSystem), <c>-19</c> (LocalService) and
        /// <c>-20</c> (NetworkService) own Windows' own working data; deleting from them breaks servicing.
        /// </summary>
        internal static bool IsInteractiveSid(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            if (!sid.StartsWith("S-1-5-21", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>The profile the process itself runs under — which, elevated, is the ADMIN's.</summary>
        public static string CurrentProfilePath()
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// Resolves a rule's path for one profile. Per-user paths are joined onto the profile folder
        /// (never onto <c>%LOCALAPPDATA%</c>); machine paths get environment expansion only.
        /// </summary>
        public static string Resolve(string rulePath, bool perUserProfile, UserProfile profile)
        {
            if (string.IsNullOrWhiteSpace(rulePath)) return "";

            if (!perUserProfile)
                return Environment.ExpandEnvironmentVariables(rulePath);

            string relative = rulePath.TrimStart('\\');
            return Path.Combine(profile.ProfilePath, relative);
        }
    }
}
