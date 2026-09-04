using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Optimus.Installer.Core;

/// <summary>The add-in itself installed, but one or more payload files could not be copied —
/// distinct from a hard failure so the message can carry the REAL per-file reason (disk space,
/// access denied, file in use...) instead of a single guessed cause and a generic "instalação
/// falhou".</summary>
public sealed class InstallPartialFailureException : Exception
{
    public InstallPartialFailureException(string message) : base(message) { }
}

public sealed class InstallProgress
{
    public string File { get; }
    public int Percent { get; }
    public InstallProgress(string file, int percent) { File = file; Percent = percent; }
}

/// <summary>
/// Deploys the Optimus add-on to every installed CorelDRAW's
/// <c>Programs64\Addons\Optimus\</c>: Optimus.AddIn.dll + all loose deps + Optimus.Resources.dll
/// + WebView2Loader.dll + the addon manifest (Coreldrw.addon marker, config.xml, AppUI/UserUI.xslt),
/// so the addon auto-loads and places its toolbar button + docker with no manual steps.
/// The whole payload is embedded as "addon.*" resources in the installer EXE.
/// </summary>
public class InstallerEngine
{
    private readonly IProgress<InstallProgress> _progress;

    public InstallerEngine(IProgress<InstallProgress> progress)
    {
        _progress = progress;
    }

    /// <summary>
    /// Where the standalone maintenance app is installed. Not under the CorelDRAW addon folder: it is a
    /// desktop app that runs with CorelDRAW closed, and burying it inside a Corel version folder would
    /// make it disappear when the shop upgrades Corel.
    /// </summary>
    public static string MaintenanceDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Optimus", "Manutencao");

    private const string MaintenanceExe = "Optimus.Manutencao.exe";

    public void Run(bool createDesktopShortcut = true)
    {
        // CorelDRAW loads Optimus.AddIn.dll in-process; that lock turns File.Create into an ugly
        // IOException on a re-install, so stop early with a clear message instead.
        EnsureCorelClosed();

        var asm = Assembly.GetExecutingAssembly();
        var addonRes = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("addon.", StringComparison.Ordinal))
            .OrderBy(n => n).ToArray();

        if (addonRes.Length == 0)
            throw new InvalidOperationException("Pacote do instalador incompleto (payload do addon ausente).");

        var addonDirs = FindCorelAddonDirs();
        if (addonDirs.Count == 0)
            throw new InvalidOperationException(
                "CorelDRAW não foi encontrado em C:\\Program Files\\Corel\\. Instale o CorelDRAW antes do Optimus.");

        // Checked BEFORE touching a single byte: a silent truncated copy of the ~57 MB voice model
        // (antivirus was the first guess and was RULED OUT on Davi's machine — 2026-08-14) is far
        // more likely to be plain disk space than anything exotic. Failing loud and early, with the
        // actual number, beats reporting a guessed cause after the fact.
        CheckFreeSpace(addonDirs);

        var appRes = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("app.payload.", StringComparison.Ordinal))
            .OrderBy(n => n).ToArray();

        int total = addonRes.Length * addonDirs.Count + appRes.Length;
        int done = 0;

        // Delete-then-copy: wipe the old folder first so a stale DLL/XSLT can never linger and get
        // loaded instead of the new one. Safe because EnsureCorelClosed ran.
        //
        // Each file is extracted independently (try/catch INSIDE the loop, not around it): one
        // resource failing for whatever reason must not silently abort every file that would have
        // been copied after it in the alphabetical resource order. The install still completes; what
        // failed is collected and reported with its ACTUAL per-file exception, never swallowed and
        // never guessed at.
        var failures = new List<string>();
        foreach (string dir in addonDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            Directory.CreateDirectory(dir);
            foreach (string res in addonRes)
            {
                try { ExtractOne(asm, res, "addon.", dir, total, ref done); }
                catch (Exception ex)
                {
                    done++;
                    failures.Add($"{res.Substring("addon.".Length)} ({dir}): {ex.Message}");
                }
            }
        }

        InstallMaintenanceApp(asm, appRes, createDesktopShortcut, total, ref done);
        WriteThirdPartyNotices(asm);
        Uninstaller.RegisterUninstallEntry(Version);

        if (failures.Count > 0)
            // The REAL reason is whatever each per-file exception says (disk cheio, acesso negado,
            // arquivo em uso...) — never guessed here. Antivirus was the first hypothesis and Davi
            // confirmed it was OFF on his machine, so this message no longer names a cause; it shows
            // the actual Windows error for each file instead.
            throw new InstallPartialFailureException(
                $"O Optimus foi instalado, mas {failures.Count} arquivo(s) não puderam ser copiados: " +
                string.Join(" | ", failures));
    }

    /// <summary>
    /// Fails fast, before copying a single byte, when the target drive doesn't have room for the
    /// payload — a plain "sem espaço em disco" is a far more likely explanation for one large file
    /// silently failing than anything exotic, and it is entirely preventable.
    /// </summary>
    private static void CheckFreeSpace(List<string> addonDirs)
    {
        const long requiredBytes = 200L * 1024 * 1024; // payload (~66 MB) x up to a few Corel
                                                         // versions + the maintenance app + margin.
        try
        {
            string drive = Path.GetPathRoot(addonDirs[0]) ?? "C:\\";
            var info = new DriveInfo(drive);
            if (info.AvailableFreeSpace < requiredBytes)
                throw new InvalidOperationException(
                    $"Pouco espaço livre em {drive} ({info.AvailableFreeSpace / (1024 * 1024)} MB). " +
                    $"O Optimus precisa de pelo menos {requiredBytes / (1024 * 1024)} MB livres para instalar " +
                    "(o comando de voz sozinho usa uns 60 MB). Libere espaço e instale de novo.");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception) { /* couldn't even check free space — let the real copy attempt speak instead */ }
    }

    /// <summary>
    /// Version stamped into the "Apps &amp; features" entry. Read from this assembly, which
    /// <c>scripts/build-all.ps1</c> stamps from <c>Build.cs</c> — so the number the customer sees in
    /// Windows can never drift from the build they were actually given.
    /// </summary>
    public static string Version
    {
        get
        {
            try
            {
                Version? v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "1.0.0" : v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "1.0.0"; }
        }
    }

    /// <summary>
    /// Ships <c>THIRD-PARTY-NOTICES.txt</c> next to the installed app. Licence attribution has to travel
    /// with the binaries, not live only in the repository — that is the whole point of the obligation.
    /// </summary>
    private static void WriteThirdPartyNotices(Assembly asm)
    {
        try
        {
            using Stream? s = asm.GetManifestResourceStream("notices.THIRD-PARTY-NOTICES.txt");
            if (s == null) return;

            Directory.CreateDirectory(MaintenanceDir);
            using FileStream fs = File.Create(Path.Combine(MaintenanceDir, "THIRD-PARTY-NOTICES.txt"));
            s.CopyTo(fs);
        }
        catch { /* attribution file missing is not a reason to fail an install already completed */ }
    }

    /// <summary>
    /// Deploys Module 1 — the standalone maintenance app — and its shortcuts.
    ///
    /// <para>
    /// It is a SEPARATE app on purpose (decision O5): manual, no scheduled task, no service, no toast.
    /// The shortcut is the entire discovery mechanism, so if the payload is present the shortcut is
    /// created; and a failure here must never abort the add-in installation, which is what the customer
    /// actually bought.
    /// </para>
    /// </summary>
    private void InstallMaintenanceApp(Assembly asm, string[] appRes, bool createDesktopShortcut,
                                       int total, ref int done)
    {
        if (appRes.Length == 0) return;

        try
        {
            EnsureMaintenanceClosed();

            Directory.CreateDirectory(MaintenanceDir);
            foreach (string res in appRes)
                ExtractOne(asm, res, "app.payload.", MaintenanceDir, total, ref done);

            string exe = Path.Combine(MaintenanceDir, MaintenanceExe);
            if (!File.Exists(exe)) return;

            if (createDesktopShortcut)
                CreateShortcut(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    "Optimus Manutenção.lnk"), exe);

            // Start-menu entry regardless: a shortcut the operator deleted by accident must still be
            // findable by typing "Optimus" in the Start menu.
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Optimus");
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, "Optimus Manutenção.lnk"), exe);
        }
        catch (Exception)
        {
            // The add-in is the product; the maintenance app is a bonus module. Never fail the whole
            // install because a shortcut could not be written.
        }
    }

    /// <summary>
    /// Creates a .lnk through <c>WScript.Shell</c>, bound late by ProgID.
    ///
    /// <para>
    /// Late binding on purpose: a COM interop reference to IWshRuntimeLibrary would add an interop
    /// assembly that ILRepack then has to merge, for the sake of writing one shortcut file. Reflection
    /// keeps the installer a single EXE with no extra dependency.
    /// </para>
    /// </summary>
    private static void CreateShortcut(string linkPath, string targetExe)
    {
        object? shell = null;
        object? link = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            link = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
            if (link == null) return;

            Type linkType = link.GetType();
            void Set(string property, object value) => linkType.InvokeMember(property,
                System.Reflection.BindingFlags.SetProperty, null, link, new[] { value });

            Set("TargetPath", targetExe);
            Set("WorkingDirectory", Path.GetDirectoryName(targetExe) ?? "");
            Set("Description", "Manutenção do Windows para máquinas de CorelDRAW");
            Set("IconLocation", targetExe + ",0");

            linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception)
        {
            // A machine with WSH disabled by policy simply gets no shortcut.
        }
        finally
        {
            try { if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link); } catch { }
            try { if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell); } catch { }
        }
    }

    private static void EnsureMaintenanceClosed()
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

    private void ExtractOne(Assembly asm, string resourceName, string prefix, string destDir, int total, ref int done)
    {
        // Most payload files are flat, but Whisper.net's own native-library loader (not our code)
        // requires its DLLs under a "runtimes/win-x64/native/" subfolder — encoded here with '/' in
        // the LogicalName and reconstructed as real subdirectories on extraction.
        string fileName = resourceName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
        string destPath = Path.Combine(destDir, fileName);
        string? destSubDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destSubDir)) Directory.CreateDirectory(destSubDir);

        long expectedLength;
        using (var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso ausente: {resourceName}"))
        using (var fs = File.Create(destPath))
        {
            expectedLength = stream.Length;
            stream.CopyTo(fs);
        }

        // A silently truncated copy is worse than a thrown exception: the file LOOKS installed, and
        // whatever depends on it (the offline voice model, a native DLL) fails much later with no
        // obvious cause. Verified on disk, not just trusted because CopyTo returned.
        long actualLength = new FileInfo(destPath).Length;
        if (actualLength != expectedLength)
            throw new IOException($"cópia incompleta: {actualLength} de {expectedLength} bytes");

        // Strip any "Mark of the Web" so the .NET loader inside CorelDRAW never refuses a DLL
        // with the loadFromRemoteSources / 0x80131515 error.
        ClearMarkOfTheWeb(destPath);

        done++;
        _progress.Report(new InstallProgress(fileName, total == 0 ? 100 : done * 100 / total));
    }

    /// <summary>Finds every <c>…\Corel\CorelDRAW Graphics Suite\&lt;version&gt;\Programs64\Addons</c>
    /// and returns the Optimus subfolder under each (the real addon location).</summary>
    private static List<string> FindCorelAddonDirs()
    {
        var result = new List<string>();
        string suiteRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Corel", "CorelDRAW Graphics Suite");
        if (!Directory.Exists(suiteRoot)) return result;

        foreach (string versionDir in Directory.GetDirectories(suiteRoot))
        {
            string addons = Path.Combine(versionDir, "Programs64", "Addons");
            if (Directory.Exists(addons))
                result.Add(Path.Combine(addons, "Optimus"));
        }
        return result;
    }

    // CorelDRAW's process is CorelDRW.exe → process name "CorelDRW".
    private static void EnsureCorelClosed()
    {
        bool running;
        try { running = Process.GetProcessesByName("CorelDRW").Length > 0; }
        catch { running = false; }

        if (running)
            throw new InvalidOperationException(
                "O CorelDRAW está aberto. Feche-o completamente e clique em Instalar novamente.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string lpFileName);

    private static void ClearMarkOfTheWeb(string path)
    {
        try { DeleteFile(path + ":Zone.Identifier"); }
        catch { /* no ADS / not supported → nothing to clear */ }
    }
}
