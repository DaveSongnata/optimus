using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using Optimus.Core.Maintenance;

namespace Optimus.Windows.Maintenance
{
    /// <summary>
    /// Reads, applies and reverts the performance adjustments.
    ///
    /// <para>
    /// Every one is <b>two-way</b>. That is not a nicety: an "optimizer" the customer cannot undo is a
    /// machine they can never restore, and it is why this class stores the previous value before writing
    /// and exposes <see cref="Revert"/> for each id.
    /// </para>
    /// <para>
    /// Nothing here disables a Windows service, touches the pagefile or changes process priorities. Those
    /// are the three staples of "PC booster" software and all three range from placebo to a support call
    /// weeks later — same evidence bar as decision O6.
    /// </para>
    /// </summary>
    public sealed class PerformanceTuner
    {
        private const string DesktopKey = @"Control Panel\Desktop";
        private const string VisualFxKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
        private const string PersonalizeKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string DwmKey = @"SOFTWARE\Microsoft\Windows\DWM";

        /// <summary>Windows' own "High performance" plan GUID — stable across versions.</summary>
        private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        /// <summary>Where Optimus remembers what it changed, so a revert restores the real previous value.</summary>
        private const string BackupKey = @"SOFTWARE\Aisten\Optimus\PerfBackup";

        private readonly Action<string> _log;

        public PerformanceTuner(Action<string>? log = null) => _log = log ?? (_ => { });

        // ── read ────────────────────────────────────────────────────────────────────

        /// <summary>The catalogue with each tweak's CURRENT state filled in from the machine.</summary>
        public List<PerformanceTweak> Read()
        {
            List<PerformanceTweak> tweaks = PerformanceTweaks.All();

            foreach (PerformanceTweak t in tweaks)
            {
                try
                {
                    switch (t.Id)
                    {
                        case PerformanceTweaks.PowerPlan: ReadPowerPlan(t); break;
                        case PerformanceTweaks.Animations: ReadAnimations(t); break;
                        case PerformanceTweaks.Transparency: ReadTransparency(t); break;
                        case PerformanceTweaks.MenuDelay: ReadMenuDelay(t); break;
                        case PerformanceTweaks.DefenderExclusions: ReadDefender(t); break;
                        case PerformanceTweaks.SearchIndex: ReadSearchIndex(t); break;
                    }
                }
                catch (Exception ex)
                {
                    t.State = TweakState.Unknown;
                    t.CurrentValue = "não foi possível ler";
                    _log("Leitura de " + t.Id + " falhou: " + ex.Message);
                }
            }

            return tweaks;
        }

        private void ReadPowerPlan(PerformanceTweak t)
        {
            string active = RunCapture("powercfg", "/getactivescheme");
            t.CurrentValue = ExtractPlanName(active);

            bool isHigh = active.IndexOf(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) >= 0
                       || t.CurrentValue.IndexOf("desempenho", StringComparison.OrdinalIgnoreCase) >= 0
                       || t.CurrentValue.IndexOf("performance", StringComparison.OrdinalIgnoreCase) >= 0;

            t.State = isHigh ? TweakState.Optimized : TweakState.Default;

            // A laptop on battery is a different machine: the trade stops being free.
            if (HasBattery())
                t.Tradeoff = "Este computador tem bateria: em alto desempenho ela dura menos. "
                           + t.Tradeoff;
        }

        /// <summary>
        /// Animations live in two places and Windows reads both: the <c>VisualFXSetting</c> switch and the
        /// bit field <c>UserPreferencesMask</c>. Reading only the first is why some tools "turn animations
        /// off" and nothing changes.
        /// </summary>
        private void ReadAnimations(PerformanceTweak t)
        {
            object? fx = ReadUser(VisualFxKey, "VisualFXSetting");
            string minAnimate = ReadUser(DesktopKey, "MinAnimate")?.ToString() ?? "1";

            bool off = minAnimate == "0" && fx != null && Convert.ToInt32(fx) == 2;

            t.CurrentValue = off ? "sem animações" : "animações ligadas";
            t.State = off ? TweakState.Optimized : TweakState.Default;
        }

        private void ReadTransparency(PerformanceTweak t)
        {
            object? v = ReadUser(PersonalizeKey, "EnableTransparency");
            bool on = v == null || Convert.ToInt32(v) != 0;

            t.CurrentValue = on ? "ligada" : "desligada";
            t.State = on ? TweakState.Default : TweakState.Optimized;
        }

        private void ReadMenuDelay(PerformanceTweak t)
        {
            string delay = ReadUser(DesktopKey, "MenuShowDelay")?.ToString() ?? "400";
            t.CurrentValue = delay + " ms";

            int value = int.TryParse(delay, out int ms) ? ms : 400;
            t.State = value <= 20 ? TweakState.Optimized : TweakState.Default;
        }

        private void ReadDefender(PerformanceTweak t)
        {
            string output = RunCapture("powershell",
                "-NoProfile -Command \"(Get-MpPreference).ExclusionPath -join ';'\"");

            if (string.IsNullOrWhiteSpace(output))
            {
                t.State = TweakState.NotApplicable;
                t.NotApplicableBecause =
                    "O Windows Defender não respondeu — provavelmente há outro antivírus instalado, "
                  + "e nesse caso a exclusão tem de ser feita no programa dele.";
                t.CurrentValue = "—";
                return;
            }

            string[] paths = output.Trim().Split(';');
            int count = 0;
            foreach (string p in paths) if (!string.IsNullOrWhiteSpace(p)) count++;

            t.CurrentValue = count == 0 ? "nenhuma pasta excluída" : count + " pasta(s) já excluída(s)";
            t.State = TweakState.Default;   // always offerable: the operator picks which folders
        }

        private void ReadSearchIndex(PerformanceTweak t)
        {
            // Reading the index scope reliably needs the Search COM API; what matters to the operator is
            // whether the service is even running, since a stopped indexer is already "optimized".
            bool running = ServiceGuard.Exists("WSearch");
            t.CurrentValue = running ? "indexador ativo" : "indexador não está instalado";
            t.State = running ? TweakState.Default : TweakState.NotApplicable;
            if (!running)
                t.NotApplicableBecause = "O serviço de indexação do Windows não está presente nesta máquina.";
        }

        // ── apply ───────────────────────────────────────────────────────────────────

        /// <summary>Applies one tweak, remembering the previous value so it can be undone.</summary>
        public bool Apply(string id, IEnumerable<string>? folders, out string message)
        {
            try
            {
                switch (id)
                {
                    case PerformanceTweaks.PowerPlan: return ApplyPowerPlan(out message);
                    case PerformanceTweaks.Animations: return ApplyAnimations(out message);
                    case PerformanceTweaks.Transparency: return ApplyTransparency(out message);
                    case PerformanceTweaks.MenuDelay: return ApplyMenuDelay(out message);
                    case PerformanceTweaks.DefenderExclusions: return ApplyDefender(folders, out message);
                    case PerformanceTweaks.SearchIndex: return ApplySearchIndex(folders, out message);
                    default: message = "Ajuste desconhecido."; return false;
                }
            }
            catch (Exception ex)
            {
                message = "Falhou: " + ex.Message;
                return false;
            }
        }

        private bool ApplyPowerPlan(out string message)
        {
            string previous = ExtractPlanGuid(RunCapture("powercfg", "/getactivescheme"));
            if (!string.IsNullOrEmpty(previous)) Remember("powerplan", previous);

            RunCapture("powercfg", "/setactive " + HighPerformanceGuid);

            string now = RunCapture("powercfg", "/getactivescheme");
            bool ok = now.IndexOf(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) >= 0;

            message = ok
                ? "Plano de energia agora é " + ExtractPlanName(now) + "."
                : "O Windows não aceitou trocar o plano de energia (política da empresa pode bloquear isso).";
            return ok;
        }

        private bool ApplyAnimations(out string message)
        {
            Remember("minanimate", ReadUser(DesktopKey, "MinAnimate")?.ToString() ?? "1");
            Remember("visualfx", ReadUser(VisualFxKey, "VisualFXSetting")?.ToString() ?? "0");

            WriteUser(DesktopKey, "MinAnimate", "0", RegistryValueKind.String);

            // 2 = "adjust for best performance". Windows then reads UserPreferencesMask for the details,
            // which is where ClearType and thumbnails are preserved below.
            WriteUser(VisualFxKey, "VisualFXSetting", 2, RegistryValueKind.DWord);

            ApplyPreferencesMask();

            message = "Animações desligadas. O Explorer reinicia por 1–2 segundos para valer agora.";
            return true;
        }

        /// <summary>
        /// Writes <c>UserPreferencesMask</c> with animations off but <b>ClearType and drag-window content
        /// ON</b>.
        ///
        /// <para>
        /// This is the whole reason Optimus writes the mask by hand instead of letting Windows apply its
        /// "best performance" preset: that preset also kills font smoothing and thumbnail previews, which
        /// for a designer is not an optimisation, it is damage. Bit layout per Microsoft's documented
        /// SPI_SETUSERPREFERENCE flags; the two bits kept set are font smoothing (byte 0, 0x02 combo) and
        /// drag-full-windows (byte 0, 0x20).
        /// </para>
        /// </summary>
        private void ApplyPreferencesMask()
        {
            try
            {
                object? current = ReadUser(DesktopKey, "UserPreferencesMask");
                if (current is byte[] mask && mask.Length >= 4)
                {
                    Remember("prefmask", Convert.ToBase64String(mask));

                    // byte 0: clear the animation/fade bits (0x80 combobox anim, 0x08 menu anim,
                    // 0x04 tooltip anim), keep 0x02 (font smoothing) and 0x20 (drag full windows).
                    mask[0] = (byte)((mask[0] & ~0x8C) | 0x22);

                    // byte 1: 0x80 = "UI effects" master. Leaving it ON keeps ClearType alive.
                    mask[1] = (byte)(mask[1] | 0x80);

                    // byte 2: 0x02 = menu fade, 0x04 = tooltip fade, 0x08 = selection fade — all off.
                    mask[2] = (byte)(mask[2] & ~0x0E);

                    WriteUser(DesktopKey, "UserPreferencesMask", mask, RegistryValueKind.Binary);
                }
            }
            catch (Exception ex)
            {
                _log("UserPreferencesMask não pôde ser ajustada: " + ex.Message);
            }
        }

        private bool ApplyTransparency(out string message)
        {
            Remember("transparency", ReadUser(PersonalizeKey, "EnableTransparency")?.ToString() ?? "1");

            WriteUser(PersonalizeKey, "EnableTransparency", 0, RegistryValueKind.DWord);
            message = "Efeitos de transparência desligados.";
            return true;
        }

        private bool ApplyMenuDelay(out string message)
        {
            Remember("menudelay", ReadUser(DesktopKey, "MenuShowDelay")?.ToString() ?? "400");

            WriteUser(DesktopKey, "MenuShowDelay", "0", RegistryValueKind.String);
            message = "Menus passam a abrir na hora (vale totalmente após sair e entrar na conta).";
            return true;
        }

        private bool ApplyDefender(IEnumerable<string>? folders, out string message)
        {
            var list = new List<string>();
            if (folders != null) foreach (string f in folders) if (!string.IsNullOrWhiteSpace(f)) list.Add(f);

            if (list.Count == 0)
            {
                message = "Escolha primeiro as pastas de arte — nada é excluído da varredura sem você dizer quais.";
                return false;
            }

            var script = new StringBuilder("-NoProfile -Command \"");
            foreach (string f in list)
                script.Append("Add-MpPreference -ExclusionPath '").Append(f.Replace("'", "''")).Append("'; ");

            // The CorelDRAW executable itself: every save is otherwise scanned in full.
            script.Append("Add-MpPreference -ExclusionProcess 'CorelDRW.exe'\"");

            string output = RunCapture("powershell", script.ToString());
            _log("Defender: " + (string.IsNullOrWhiteSpace(output) ? "sem saída (normal)" : output.Trim()));

            Remember("defender", string.Join("|", list.ToArray()));
            message = list.Count + " pasta(s) fora da varredura, mais o CorelDRAW.";
            return true;
        }

        private bool ApplySearchIndex(IEnumerable<string>? folders, out string message)
        {
            var list = new List<string>();
            if (folders != null) foreach (string f in folders) if (!string.IsNullOrWhiteSpace(f)) list.Add(f);

            if (list.Count == 0)
            {
                message = "Escolha primeiro as pastas de arte.";
                return false;
            }

            // Windows offers no supported command line for index scopes, and hand-editing the Search
            // database is exactly the kind of thing that leaves a machine with a broken search. So the
            // app opens the official dialog with the folders listed for the operator to untick.
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "shell32.dll,Control_RunDLL srchadmin.dll",
                    UseShellExecute = false,
                })?.Dispose();

                message = "Abri as Opções de Indexação do Windows. Em \"Modificar\", desmarque: "
                        + string.Join(", ", list.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                message = "Não foi possível abrir as Opções de Indexação: " + ex.Message;
                return false;
            }
        }

        // ── revert ──────────────────────────────────────────────────────────────────

        /// <summary>Puts a tweak back the way it was, using the value stored when it was applied.</summary>
        public bool Revert(string id, out string message)
        {
            try
            {
                switch (id)
                {
                    case PerformanceTweaks.PowerPlan:
                    {
                        string? previous = Recall("powerplan");
                        if (string.IsNullOrEmpty(previous))
                        {
                            message = "Não há registro do plano anterior; escolha-o em Opções de Energia.";
                            return false;
                        }
                        RunCapture("powercfg", "/setactive " + previous);
                        message = "Plano de energia anterior restaurado.";
                        return true;
                    }

                    case PerformanceTweaks.Animations:
                    {
                        WriteUser(DesktopKey, "MinAnimate", Recall("minanimate") ?? "1", RegistryValueKind.String);

                        string fx = Recall("visualfx") ?? "0";
                        WriteUser(VisualFxKey, "VisualFXSetting",
                                  int.TryParse(fx, out int v) ? v : 0, RegistryValueKind.DWord);

                        string? maskB64 = Recall("prefmask");
                        if (!string.IsNullOrEmpty(maskB64))
                            WriteUser(DesktopKey, "UserPreferencesMask",
                                      Convert.FromBase64String(maskB64!), RegistryValueKind.Binary);

                        message = "Animações restauradas.";
                        return true;
                    }

                    case PerformanceTweaks.Transparency:
                    {
                        string t = Recall("transparency") ?? "1";
                        WriteUser(PersonalizeKey, "EnableTransparency",
                                  int.TryParse(t, out int v) ? v : 1, RegistryValueKind.DWord);
                        message = "Transparência restaurada.";
                        return true;
                    }

                    case PerformanceTweaks.MenuDelay:
                    {
                        WriteUser(DesktopKey, "MenuShowDelay", Recall("menudelay") ?? "400",
                                  RegistryValueKind.String);
                        message = "Atraso de menu restaurado.";
                        return true;
                    }

                    case PerformanceTweaks.DefenderExclusions:
                    {
                        string? saved = Recall("defender");
                        if (string.IsNullOrEmpty(saved)) { message = "Nada a restaurar."; return true; }

                        var script = new StringBuilder("-NoProfile -Command \"");
                        foreach (string f in saved!.Split('|'))
                            if (!string.IsNullOrWhiteSpace(f))
                                script.Append("Remove-MpPreference -ExclusionPath '")
                                      .Append(f.Replace("'", "''")).Append("'; ");
                        script.Append("Remove-MpPreference -ExclusionProcess 'CorelDRW.exe'\"");

                        RunCapture("powershell", script.ToString());
                        message = "Pastas devolvidas à varredura do antivírus.";
                        return true;
                    }

                    default:
                        message = "Este ajuste é revertido pela própria janela do Windows.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                message = "Falhou ao reverter: " + ex.Message;
                return false;
            }
        }

        // ── helpers ─────────────────────────────────────────────────────────────────

        private static object? ReadUser(string subKey, string name)
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey))
                return key?.GetValue(name);
        }

        private static void WriteUser(string subKey, string name, object value, RegistryValueKind kind)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey))
                key?.SetValue(name, value, kind);
        }

        private void Remember(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(BackupKey))
                    key?.SetValue(name, value, RegistryValueKind.String);
            }
            catch (Exception ex) { _log("Não foi possível guardar o valor anterior de " + name + ": " + ex.Message); }
        }

        private static string? Recall(string name)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(BackupKey))
                    return key?.GetValue(name)?.ToString();
            }
            catch (Exception) { return null; }
        }

        /// <summary>True when the machine has a battery — a laptop, where the power trade is not free.</summary>
        internal static bool HasBattery()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                           "SELECT * FROM Win32_Battery"))
                    foreach (System.Management.ManagementObject mo in searcher.Get())
                        using (mo) return true;
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Parses the plan NAME out of <c>powercfg /getactivescheme</c>.</summary>
        internal static string ExtractPlanName(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return "desconhecido";

            int open = output.IndexOf('(');
            int close = output.LastIndexOf(')');
            return close > open && open >= 0
                ? output.Substring(open + 1, close - open - 1).Trim()
                : output.Trim();
        }

        /// <summary>Parses the plan GUID out of <c>powercfg /getactivescheme</c>.</summary>
        internal static string ExtractPlanGuid(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return "";

            foreach (string token in output.Split(' ', '\r', '\n', '\t'))
                if (Guid.TryParse(token.Trim(), out Guid g)) return g.ToString();

            return "";
        }

        private string RunCapture(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using (Process? p = Process.Start(psi))
                {
                    if (p == null) return "";
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(60_000);
                    return output;
                }
            }
            catch (Exception ex)
            {
                _log(exe + " falhou: " + ex.Message);
                return "";
            }
        }

        /// <summary>Formats a tweak's state for the report, in the operator's terms.</summary>
        public static string StateLabel(TweakState state)
        {
            switch (state)
            {
                case TweakState.Optimized: return "já está otimizado";
                case TweakState.Default: return "pode melhorar";
                case TweakState.NotApplicable: return "não se aplica";
                default: return "não foi possível ler";
            }
        }

        /// <summary>Culture-independent number formatting for the values shown on screen.</summary>
        internal static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
