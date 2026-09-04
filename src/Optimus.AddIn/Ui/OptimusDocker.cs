using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Optimus.Core.I18n;
using Optimus.Windows;

namespace Optimus.AddIn.Ui
{
    /// <summary>
    /// THE production UI surface: a WPF UserControl the CorelDRAW addon framework hosts in an
    /// anchored docker (AppUI.xslt, <c>type="wpfhost"</c>). Built in code (no XAML). The
    /// framework instantiates it via the constructor with the live in-process Application.
    /// WebView2 inits on Loaded with a writable %LOCALAPPDATA% user-data folder; the native DLL
    /// search path is restored right after. Every step logs to %TEMP%\Optimus\docker.log.
    /// </summary>
    public sealed class OptimusDocker : UserControl
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string? lpPathName);

        private readonly object? _injectedApp;
        private readonly WebView2 _web = new WebView2();
        private readonly LocalizationService _localization = LanguageStore.Service();
        private OptimusBridge? _bridge;
        private string _i18nScriptId = "";
        private bool _started;
        private bool _disposed;

        // CRITICAL: the CorelDRAW addon host loads Optimus.AddIn.dll but does NOT add the addon
        // folder to the .NET assembly probe path, and there is no app.config. So loose deps
        // (System.Text.Json + its System.Runtime.CompilerServices.Unsafe, WebView2 managed) fail
        // to load. Resolve every managed dep from the folder this DLL sits in, ignoring the
        // requested version (load the sibling we shipped). Registered in the static ctor so it's
        // live before any dep is touched.
        static OptimusDocker()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromAddonDir;

            // A bug must NEVER silently take CorelDRAW down: guarantee a trace lands in the log,
            // and neutralise the two managed crash vectors we can (unobserved task faults).
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    try { OptimusLog.Write("FATAL UnhandledException (terminating=" + e.IsTerminating + "): " + e.ExceptionObject); } catch { }
                };
            }
            catch { }
            try
            {
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    try { OptimusLog.Write("UnobservedTaskException (neutralized): " + e.Exception); } catch { }
                    e.SetObserved();
                };
            }
            catch { }
        }

        private static Assembly? ResolveFromAddonDir(object? sender, ResolveEventArgs args)
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(OptimusDocker).Assembly.Location) ?? "";
                string name = new AssemblyName(args.Name).Name + ".dll";
                string path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    OptimusLog.Write("AssemblyResolve: " + name + " <- addon dir");
                    return Assembly.LoadFrom(path);
                }
                return null;
            }
            catch (Exception ex) { OptimusLog.Write("AssemblyResolve error: " + ex.Message); return null; }
        }

        // The addon framework calls this constructor; it MAY pass the CorelDRAW Application.
        public OptimusDocker(object app)
        {
            try
            {
                _injectedApp = app;
                OptimusLog.Write("=== Optimus docker v" + Build.Tag + " ===  ctor: injected app is "
                    + (app == null ? "NULL" : app.GetType().FullName));

                try
                {
                    Dispatcher.UnhandledException += (_, ex) =>
                    {
                        try { OptimusLog.Write("Dispatcher.UnhandledException (handled, Corel safe): " + ex.Exception); } catch { }
                        ex.Handled = true;
                    };
                }
                catch { }

                MinWidth = 320;
                Content = _web;
                Loaded += OnLoaded;
                Unloaded += OnUnloaded;

                // BACKSTOP for a real crash pattern: CorelDRAW's native docking host does not always
                // walk WPF's own disconnect logic when the addon panel is torn down (closing the
                // document, closing CorelDRAW itself) — WPF's Unloaded event above then never fires,
                // _web is never explicitly Dispose()'d, and it is later collected as garbage. A
                // WebView2 control finalized instead of disposed crashes the WHOLE PROCESS with
                // InvalidCastException / E_NOINTERFACE from the GC finalizer thread trying to reach a
                // COM interface that is no longer valid there — measured repeatedly on Davi's machine
                // (2026-08-14), ~10-25 min after load, unrelated to any Optimus feature: the stack
                // trace is entirely inside WebView2's own Dispose/Finalize, no Optimus frame in it.
                // ProcessExit is the last reliable point to force a clean, explicit Dispose() BEFORE
                // that finalizer ever gets a chance to run.
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeWebView();
            }
            catch (Exception ex)
            {
                try { OptimusLog.Write("ctor FAILED (docker disabled, Corel safe): " + ex); } catch { }
            }
        }

        // Some hosts call a parameterless ctor; cover it so instantiation never fails.
        public OptimusDocker() : this(null!) { }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started) return;
            _started = true;
            OptimusLog.Write("OnLoaded: start (v" + Build.Tag + ")");

            // This is an `async void` handler: an escaping exception goes to the host dispatcher
            // and CLOSES CorelDRAW. Everything degrades to a log line — the docker just doesn't
            // appear, but Corel stays alive.
            try
            {
                try
                {
                    SetDllDirectory(OptimusBridge.InstallDir());
                    try
                    {
                        CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, OptimusBridge.UserDataDir());
                        await _web.EnsureCoreWebView2Async(env);
                    }
                    finally { SetDllDirectory(null); }
                    OptimusLog.Write("OnLoaded: WebView2 ready");
                }
                catch (Exception ex)
                {
                    OptimusLog.Write("OnLoaded: WebView2 FAILED: " + ex);
                    return;
                }

                object app = ResolveApp();

                // The catalog is injected BEFORE the page script runs; the docker's HTML carries no text of
                // its own, so arriving late would show empty labels inside CorelDRAW.
                _i18nScriptId = await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    I18nScript.Build(_localization));

                // Genuinely marshals onto the UI thread — NOT "a => a()". That naive form was safe
                // only as long as every bridge call happened to already be running on the UI thread,
                // which stopped being true once voice command started isolating whisper.cpp's heavy
                // native work on a threadpool thread (Task.Run in RunVoiceStop): the continuation
                // after that await does not reliably resume on the UI thread, and OptimusBridge.Post()
                // calling CoreWebView2.ExecuteScriptAsync from a threadpool thread crashes with
                // "CoreWebView2 members can only be accessed from the UI thread" — measured on Davi's
                // machine immediately after a voiceStop, 2026-08-15. Dispatcher.Invoke is safe to call
                // from the UI thread itself too (WPF runs it inline there, no deadlock), so this one
                // change covers every existing synchronous call path as well as the new async one.
                _bridge = new OptimusBridge(_web.CoreWebView2, app, a => Dispatcher.Invoke(a),
                                            _localization, ReloadForLanguage);
                _web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "optimus.app", OptimusBridge.ExtractUi(), CoreWebView2HostResourceAccessKind.Allow);
                _web.CoreWebView2.Navigate("https://optimus.app/index.html");
                OptimusLog.Write("OnLoaded: navigated (idioma=" + _localization.Current + ")");
            }
            catch (Exception ex)
            {
                OptimusLog.Write("OnLoaded: FAILED (docker disabled, Corel stays alive): " + ex);
            }
        }

        /// <summary>
        /// Re-injects the catalog in the new language and reloads the docker. The old injection is REMOVED
        /// first — these scripts accumulate, and two catalogs would leave the panel in mixed languages.
        /// Wrapped end to end: a failure here must never take CorelDRAW down.
        /// </summary>
        private async void ReloadForLanguage()
        {
            try
            {
                if (_web.CoreWebView2 == null) return;

                if (!string.IsNullOrEmpty(_i18nScriptId))
                    _web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_i18nScriptId);

                _i18nScriptId = await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    I18nScript.Build(_localization));

                _web.CoreWebView2.Reload();
                OptimusLog.Write("Docker recarregado no idioma " + _localization.Current);
            }
            catch (Exception ex)
            {
                OptimusLog.Write("Troca de idioma falhou (Corel segue vivo): " + ex.Message);
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try { _bridge?.PostStatus(); } catch (Exception ex) { OptimusLog.Write("NavCompleted post failed: " + ex.Message); }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => DisposeWebView();

        /// <summary>Idempotent: reachable from both the normal WPF teardown path (OnUnloaded) and the
        /// ProcessExit backstop above, and safe to call from either more than once.</summary>
        private void DisposeWebView()
        {
            if (_disposed) return;
            _disposed = true;
            OptimusLog.Write("DisposeWebView: disposing");
            try { if (_web.CoreWebView2 != null) _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted; } catch { }
            try { _bridge?.Detach(); } catch { }
            try { _web.Dispose(); } catch (Exception ex) { OptimusLog.Write("DisposeWebView: _web.Dispose() failed: " + ex.Message); }
        }

        /// <summary>Returns the CorelDRAW Application the bridge drives. The framework injects a
        /// live in-process COM object through the constructor; only if it injects null do we fall
        /// back to the Running Object Table.</summary>
        private object ResolveApp()
        {
            if (_injectedApp != null)
            {
                OptimusLog.Write("ResolveApp: using injected app (" + _injectedApp.GetType().FullName + ")");
                return _injectedApp;
            }

            OptimusLog.Write("ResolveApp: injected app is NULL, trying ROT");
            foreach (string progId in new[] { "CorelDRAW.Application", "CorelDRAW.Application.25", "CorelDRAW.Application.24" })
            {
                try { return Marshal.GetActiveObject(progId); }
                catch (Exception ex) { OptimusLog.Write("ResolveApp: ROT " + progId + " failed: " + ex.Message); }
            }

            OptimusLog.Write("ResolveApp: NO app available");
            return _injectedApp!;
        }
    }
}
