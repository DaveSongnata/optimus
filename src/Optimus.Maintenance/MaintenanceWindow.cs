using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Optimus.Core.I18n;
using Optimus.Windows;

namespace Optimus.Maintenance
{
    /// <summary>
    /// The app window: a WPF <see cref="Window"/> built in code hosting a WebView2, mirroring the add-in
    /// docker so the product has one UI technique instead of two.
    ///
    /// <para>
    /// The UI is a single embedded HTML file served through a WebView2 virtual host — fully offline, no CDN
    /// and no webfont, because the client's machine may have no internet at all (a shop PC is often on an
    /// isolated network).
    /// </para>
    /// </summary>
    internal sealed class MaintenanceWindow : Window
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string? lpPathName);

        private readonly WebView2 _web = new WebView2();
        private readonly LocalizationService _localization = LanguageStore.Service();
        private MaintenanceBridge? _bridge;
        private string _i18nScriptId = "";
        private bool _started;

        static MaintenanceWindow()
        {
            // The app ships its managed deps loose next to the EXE; resolving them from the EXE folder
            // keeps working even when the shortcut launches with a different working directory.
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(typeof(MaintenanceWindow).Assembly.Location) ?? "";
                    string file = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
                    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                }
                catch (Exception) { return null; }
            };
        }

        public MaintenanceWindow()
        {
            Title = _localization["mnt.title"] + " " + Program.VersionTag;
            Width = 1080;
            Height = 760;
            MinWidth = 860;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Matches the brand paper tone, so the frame does not flash white before the page paints.
            Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xFA, 0xF8));

            Content = _web;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started) return;
            _started = true;

            // async void: an escaping exception would kill the process, so nothing escapes.
            try
            {
                try
                {
                    SetDllDirectory(InstallDir());
                    try
                    {
                        CoreWebView2Environment env =
                            await CoreWebView2Environment.CreateAsync(null, UserDataDir());
                        await _web.EnsureCoreWebView2Async(env);
                    }
                    finally { SetDllDirectory(null); }

                    MaintenanceLog.Write("WebView2 pronto.");
                }
                catch (Exception ex)
                {
                    MaintenanceLog.Write("WebView2 FALHOU: " + ex);
                    MessageBox.Show(_localization["mnt.webview.missing"],
                        "Optimus", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;

                // The catalog must exist BEFORE the page's own script runs: the HTML carries no text of its
                // own, so a dictionary that arrived later would show a flash of empty labels.
                _i18nScriptId = await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    I18nScript.Build(_localization));

                _bridge = new MaintenanceBridge(_web.CoreWebView2, a => Dispatcher.Invoke(a),
                                                _localization, ReloadForLanguage);

                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "optimus.local", ExtractUi(), CoreWebView2HostResourceAccessKind.Allow);
                _web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _web.CoreWebView2.Navigate("https://optimus.local/index.html");
                MaintenanceLog.Write("UI navegada.");
            }
            catch (Exception ex)
            {
                MaintenanceLog.Write("OnLoaded FALHOU: " + ex);
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // The diagnosis runs as soon as the page is ready: read-only, and it is what decides whether
            // the destructive operations may be offered at all.
            try { _bridge?.StartDiagnosis(); }
            catch (Exception ex) { MaintenanceLog.Write("Diagnóstico inicial falhou: " + ex.Message); }
        }

        /// <summary>
        /// Applies a new language by re-injecting the catalog and reloading. A reload rather than a live
        /// re-render because the page's static markup and its dynamically built tables both read the
        /// dictionary at build time — reloading is one code path instead of two, and the operator loses
        /// nothing but a scan they can rerun.
        /// </summary>
        private async void ReloadForLanguage()
        {
            try
            {
                if (_web.CoreWebView2 == null) return;

                MaintenanceLog.Write("Idioma alterado para " + _localization.Current + " — recarregando a UI.");

                // The old injection must be REMOVED, not merely followed by a new one: these scripts
                // accumulate, and two catalogs racing would leave the page in a mixed language.
                if (!string.IsNullOrEmpty(_i18nScriptId))
                    _web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_i18nScriptId);

                _i18nScriptId = await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    I18nScript.Build(_localization));

                _web.CoreWebView2.Reload();
            }
            catch (Exception ex)
            {
                MaintenanceLog.Write("Falha ao trocar o idioma: " + ex.Message);
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            try { if (_web.CoreWebView2 != null) _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted; }
            catch (Exception) { }
            try { _bridge?.Detach(); } catch (Exception) { }
            try { _web.Dispose(); } catch (Exception) { }
        }

        private static string InstallDir()
        {
            try
            {
                string loc = typeof(MaintenanceWindow).Assembly.Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc)!;
            }
            catch (Exception) { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Optimus");
        }

        /// <summary>Extracts the embedded single-file UI next to the log, where it is writable.</summary>
        private static string ExtractUi()
        {
            string dir = Path.Combine(Path.GetTempPath(), "Optimus_manutencao_ui");
            Directory.CreateDirectory(dir);

            Assembly asm = typeof(MaintenanceWindow).Assembly;
            using (Stream s = asm.GetManifestResourceStream("maintenance.index.html")
                ?? throw new InvalidOperationException("Recurso de UI ausente: maintenance.index.html"))
            using (FileStream fs = File.Create(Path.Combine(dir, "index.html")))
                s.CopyTo(fs);

            return dir;
        }

        /// <summary>
        /// Writable per-user WebView2 data folder. CRITICAL: the app is installed under Program Files, and
        /// a default user-data folder there is read-only — WebView2 then fails silently and the window
        /// stays blank (the exact v1.0 symptom in the add-in).
        /// </summary>
        private static string UserDataDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Optimus", "WebView2Manutencao");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
