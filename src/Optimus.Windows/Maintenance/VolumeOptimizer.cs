using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Optimus.Windows.Maintenance
{
    public sealed class ToolRunResult
    {
        public bool Ran { get; set; }
        public bool TimedOut { get; set; }
        public int ExitCode { get; set; }
        public string Message { get; set; } = "";
        public string Output { get; set; } = "";
        public bool Succeeded => Ran && ExitCode == 0;
    }

    /// <summary>
    /// Media-aware volume optimisation and component-store cleanup — both delegated to the in-box tools.
    ///
    /// <para>
    /// <b>Never <c>defrag /D</c>.</b> On an SSD a forced defragment writes gigabytes to move blocks whose
    /// physical location is invented by the controller anyway: it consumes write endurance and gains
    /// nothing. <c>/O</c> is the one correct verb — Windows reads the media type itself and defragments an
    /// HDD or retrims an SSD. The media check here exists so the app can also <b>tell</b> the operator
    /// which one is about to happen, in their language.
    /// </para>
    /// </summary>
    public static class VolumeOptimizer
    {
        /// <summary>
        /// Runs <c>defrag &lt;drive&gt; /O</c>. Blocks destructive framing: this is a long, safe,
        /// interruptible operation, so the timeout is generous and a timeout is reported as "still
        /// running", not as a failure.
        /// </summary>
        public static ToolRunResult Optimize(string driveRoot, int timeoutMinutes = 120)
        {
            string drive = (driveRoot ?? "C:\\").Substring(0, 1) + ":";
            return Run("defrag", drive + " /O", timeoutMinutes,
                       "Otimização do volume " + drive + " concluída.");
        }

        /// <summary>
        /// Describes, in the operator's words, what optimising this drive will actually do — so nobody
        /// believes their SSD was "defragmented".
        /// </summary>
        public static string DescribeOptimization(MediaKind media)
        {
            switch (media)
            {
                case MediaKind.Hdd:
                    return "Este é um HD mecânico: o Windows vai desfragmentar, juntando os pedaços dos "
                         + "arquivos. É onde a desfragmentação realmente acelera a máquina.";
                case MediaKind.Ssd:
                    return "Este é um SSD: o Windows NÃO desfragmenta (isso só gastaria a vida do disco). "
                         + "Ele executa o TRIM, que avisa o disco quais blocos estão livres.";
                case MediaKind.Scm:
                    return "Este disco é memória persistente e não precisa de otimização.";
                default:
                    return "O tipo de mídia não foi informado pelo disco; a otimização do Windows decide "
                         + "sozinha o que fazer com segurança.";
            }
        }

        /// <summary>
        /// <c>DISM /Online /Cleanup-Image /StartComponentCleanup</c> — removes superseded servicing
        /// components. Typically 1–8 GB on a machine that has been updating for years.
        ///
        /// <para>
        /// <paramref name="resetBase"/> adds <c>/ResetBase</c>, which is bigger AND irreversible: after it,
        /// no installed update can be uninstalled. It must never default to true.
        /// </para>
        /// </summary>
        public static ToolRunResult ComponentCleanup(bool resetBase, int timeoutMinutes = 120)
        {
            string args = "/Online /Cleanup-Image /StartComponentCleanup";
            if (resetBase) args += " /ResetBase";

            return Run("dism.exe", args, timeoutMinutes,
                       resetBase
                           ? "Limpeza profunda do repositório de componentes concluída "
                           + "(atualizações antigas não podem mais ser desinstaladas)."
                           : "Limpeza do repositório de componentes concluída.");
        }

        /// <summary>
        /// <c>DISM /Online /Cleanup-Image /AnalyzeComponentStore</c> — read-only, and the honest way to
        /// know whether the cleanup is worth its two hours before starting it.
        /// </summary>
        public static ToolRunResult AnalyzeComponentStore(int timeoutMinutes = 30) =>
            Run("dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore", timeoutMinutes,
                "Análise do repositório de componentes concluída.");

        /// <summary>
        /// <c>sfc /verifyonly</c> — checks Windows' own files without changing anything. Offered because
        /// "o Corel fecha do nada" is often a corrupted system file, and a read-only check is the correct
        /// first step rather than a repair nobody asked for.
        /// </summary>
        public static ToolRunResult VerifySystemFiles(int timeoutMinutes = 60) =>
            Run("sfc.exe", "/verifyonly", timeoutMinutes, "Verificação dos arquivos do Windows concluída.");

        private static ToolRunResult Run(string exe, string args, int timeoutMinutes, string okMessage)
        {
            var result = new ToolRunResult();

            string path = exe;
            try
            {
                string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string candidate = Path.Combine(system32, exe.EndsWith(".exe") ? exe : exe + ".exe");
                if (File.Exists(candidate)) path = candidate;
            }
            catch (Exception) { }

            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            var output = new StringBuilder();

            try
            {
                using (Process? p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        result.Message = "Não foi possível iniciar " + exe + ".";
                        return result;
                    }

                    // Async reads: these tools emit progress continuously and a full pipe buffer would
                    // deadlock the wait below.
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(Math.Max(1, timeoutMinutes) * 60_000))
                    {
                        result.TimedOut = true;
                        result.Output = output.ToString();
                        result.Message = exe + " passou de " + timeoutMinutes
                                       + " minutos e continua trabalhando em segundo plano.";
                        return result;
                    }

                    result.Ran = true;
                    result.ExitCode = p.ExitCode;
                    result.Output = output.ToString();
                    result.Message = p.ExitCode == 0
                        ? okMessage
                        : exe + " terminou com o código " + p.ExitCode + ".";
                }
            }
            catch (Exception ex)
            {
                result.Message = "Falha ao executar " + exe + ": " + ex.Message;
            }

            return result;
        }
    }
}
