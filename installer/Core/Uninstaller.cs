using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Optimus.Installer.Core;

public sealed class UninstallResult
{
    public List<string> Removed { get; } = new();
    public List<string> Failed { get; } = new();
    public bool CorelWasOpen { get; set; }

    public bool Success => !CorelWasOpen && Failed.Count == 0;
}

/// <summary>
/// Removes everything the installer put on the machine: the add-on folder inside every CorelDRAW, the
/// maintenance app, both shortcuts and the "Apps &amp; features" entry.
///
/// <para>
/// Two things it deliberately does NOT touch, because they are the customer's, not ours:
/// the language preference and the log under <c>%LOCALAPPDATA%\Optimus</c> (a record of what was deleted
/// from their machine, which they may still need), and — above all — <b>any <c>.cdr</c> file anywhere</b>.
/// An uninstaller that removes artwork or the evidence of past cleanups would be a far worse defect than
/// leaving two small files behind.
/// </para>
/// </summary>
public sealed class Uninstaller
{
    /// <summary>Where the Uninstall entry lives, so Windows can offer the removal itself.</summary>
    public const string UninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Optimus";

    public UninstallResult Run()
    {
        var result = new UninstallResult();

        // The add-in DLL is loaded in-process by CorelDRAW; deleting it while Corel runs fails with a
        // sharing violation and leaves a half-removed install.
        try { result.CorelWasOpen = Process.GetProcessesByName("CorelDRW").Length > 0; }
        catch { result.CorelWasOpen = false; }
        if (result.CorelWasOpen) return result;

        KillMaintenanceApp();

        foreach (string dir in FindAddonDirs())
            Delete(dir, result);

        Delete(InstallerEngine.MaintenanceDir, result);

        // The parent %ProgramFiles%\Optimus is removed only if empty — a customer may have put something
        // of their own there.
        try
        {
            string parent = Path.GetDirectoryName(InstallerEngine.MaintenanceDir) ?? "";
            if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
                Directory.Delete(parent);
        }
        catch { }

        DeleteShortcuts(result);
        RemoveUninstallEntry(result);

        return result;
    }

    private static void KillMaintenanceApp()
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName("Optimus.Manutencao"))
            {
                try { p.Kill(); p.WaitForExit(10_000); } catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }
        catch { }
    }

    private static void Delete(string dir, UninstallResult result)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            Directory.Delete(dir, recursive: true);
            result.Removed.Add(dir);
        }
        catch (Exception ex)
        {
            result.Failed.Add(dir + " — " + ex.Message);
        }
    }

    private static void DeleteShortcuts(UninstallResult result)
    {
        var paths = new List<string>();

        try
        {
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                "Optimus Manutenção.lnk"));
        }
        catch { }

        try
        {
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                "Optimus", "Optimus Manutenção.lnk"));
        }
        catch { }

        foreach (string path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                File.Delete(path);
                result.Removed.Add(path);
            }
            catch (Exception ex) { result.Failed.Add(path + " — " + ex.Message); }
        }

        // The Start-menu folder goes only when empty.
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Optimus");
            if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
                Directory.Delete(folder);
        }
        catch { }
    }

    private static void RemoveUninstallEntry(UninstallResult result)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? parent = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);

            if (parent?.OpenSubKey("Optimus") == null) return;

            parent.DeleteSubKeyTree("Optimus", throwOnMissingSubKey: false);
            result.Removed.Add("HKLM\\" + UninstallKey);
        }
        catch (Exception ex) { result.Failed.Add("registro de desinstalação — " + ex.Message); }
    }

    /// <summary>Every <c>…\Addons\Optimus</c> folder currently on the machine.</summary>
    private static List<string> FindAddonDirs()
    {
        var result = new List<string>();
        try
        {
            string suiteRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Corel", "CorelDRAW Graphics Suite");
            if (!Directory.Exists(suiteRoot)) return result;

            foreach (string versionDir in Directory.GetDirectories(suiteRoot))
            {
                string dir = Path.Combine(versionDir, "Programs64", "Addons", "Optimus");
                if (Directory.Exists(dir)) result.Add(dir);
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Registers the product in "Apps &amp; features" so it can be uninstalled the way Windows expects.
    /// A copy of the installer is kept beside the app to serve as the uninstaller — the customer must not
    /// have to hunt for the original download months later.
    /// </summary>
    public static void RegisterUninstallEntry(string version)
    {
        try
        {
            Directory.CreateDirectory(InstallerEngine.MaintenanceDir);

            string self = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string kept = Path.Combine(InstallerEngine.MaintenanceDir, "Optimus_Setup.exe");

            if (!string.IsNullOrEmpty(self) &&
                !self.Equals(kept, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, kept, overwrite: true);

            using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey key = baseKey.CreateSubKey(UninstallKey);
            if (key == null) return;

            key.SetValue("DisplayName", "Optimus — Otimizador CorelDRAW");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Aisten Lab Technology");
            key.SetValue("InstallLocation", InstallerEngine.MaintenanceDir);
            key.SetValue("UninstallString", "\"" + kept + "\" /uninstall");
            key.SetValue("QuietUninstallString", "\"" + kept + "\" /uninstall /quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 12_000, RegistryValueKind.DWord);   // KB, approximate
        }
        catch
        {
            // No uninstall entry is a cosmetic loss; failing the install over it would not be.
        }
    }
}
