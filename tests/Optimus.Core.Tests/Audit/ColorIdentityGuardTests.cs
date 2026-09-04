using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Optimus.Core.Audit;
using Xunit;

namespace Optimus.Core.Tests.Audit
{
    /// <summary>
    /// Guards the colour IDENTITY — the string that has to mean the same thing to the audit and to
    /// the replacement, or a colour found in the drawing can never be replaced.
    ///
    /// <para>
    /// Measured on <c>x2.cdr</c>: <c>ColorReplace chave=Cmyk|#F58634 formas=100 preench=0</c>. A
    /// hundred shapes walked and nothing changed, with no error anywhere, because the audit spelled
    /// that colour without its CMYK separation and the replacer spelled it with. Converting the file
    /// to RGB made the same action work instantly — RGB carries no components, so both sides agreed
    /// by accident.
    /// </para>
    /// <para>
    /// These are SOURCE checks, not behaviour checks: the reader itself lives in
    /// <c>Optimus.Interop</c>, which is net48 + COM and cannot be referenced from a net8 test. What
    /// can be pinned from here is the thing that actually broke — that the identity is read in ONE
    /// place, in the one order that survives <c>ConvertToRGB</c>.
    /// </para>
    /// </summary>
    public class ColorIdentityGuardTests
    {
        private static string InteropDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Optimus.Interop")))
                dir = dir.Parent;

            Assert.True(dir != null, "não encontrei a raiz do repositório a partir de " + AppContext.BaseDirectory);
            return Path.Combine(dir!.FullName, "src", "Optimus.Interop");
        }

        /// <summary>Source with comments and string literals stripped — a mention of a member inside
        /// a comment is documentation, not a second implementation.</summary>
        private static string Code(string file)
        {
            string text = File.ReadAllText(file);
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//[^\n]*", "");
            return text;
        }

        [Theory]
        [InlineData("CMYKCyan")]
        [InlineData("HexValue")]
        [InlineData("SpotColorName")]
        public void Only_ColorIdentity_reads_the_fields_an_identity_is_made_of(string member)
        {
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(InteropDir(), "*.cs"))
            {
                if (Path.GetFileName(file) == "ColorIdentity.cs") continue;
                if (Code(file).Contains(member)) offenders.Add(Path.GetFileName(file));
            }

            Assert.True(offenders.Count == 0,
                $"\"{member}\" é lido fora de ColorIdentity.cs, em: {string.Join(", ", offenders)}. " +
                "Duas leituras da mesma identidade viram duas grafias da mesma cor, e aí a auditoria " +
                "acha uma cor que a substituição nunca encontra.");
        }

        [Fact]
        public void Every_collector_reads_the_identity_BEFORE_converting_to_RGB()
        {
            // ConvertToRGB() muta a cópia. Ler os componentes CMYK depois dela devolve vazio, sem
            // erro nenhum — que é exatamente como a chave perdeu a separação.
            foreach (string name in new[] { "ColorCollector.cs", "FastColorCollector.cs" })
            {
                string code = Code(Path.Combine(InteropDir(), name));

                // LAST occurrence, not the first: um segundo Fill depois do ReadRgb sobrescreve os
                // componentes com vazio, e conferir só a primeira chamada deixaria isso passar —
                // foi o que aconteceu ao verificar este próprio teste.
                int identity = code.LastIndexOf("ColorIdentity.Fill", StringComparison.Ordinal);
                int readRgb = code.IndexOf("ReadRgb(copy", StringComparison.Ordinal);

                Assert.True(identity >= 0, name + " não usa o leitor único ColorIdentity.Fill");
                Assert.True(readRgb >= 0, name + " não chama ReadRgb — a amostra ficaria sem cor");
                Assert.True(readRgb > identity,
                    name + ": ReadRgb roda ANTES de ColorIdentity.Fill — os componentes CMYK vão " +
                    "sair vazios, porque ConvertToRGB() já mutou a cópia.");
            }
        }

        [Fact]
        public void A_cmyk_colour_without_its_separation_is_not_the_same_key()
        {
            // O que estava em jogo, escrito como dado: as duas grafias que apareceram no arquivo real.
            var comAudit = new ColorRecord { Model = ColorModel.Cmyk, Hex = "#F58634" };
            var naForma = new ColorRecord { Model = ColorModel.Cmyk, Hex = "#F58634", Components = "C0 M50 Y90 K0" };

            Assert.NotEqual(comAudit.Key, naForma.Key);
        }

        [Fact]
        public void Two_cmyk_builds_that_look_alike_stay_different_colours()
        {
            // Por isso a correção NÃO foi tirar os componentes da chave: preto de uma chapa e preto
            // rico são a mesma aparência e tintas diferentes, e juntá-los estragaria a tiragem.
            var chapa = new ColorRecord { Model = ColorModel.Cmyk, Hex = "#000000", Components = "C0 M0 Y0 K100" };
            var rico = new ColorRecord { Model = ColorModel.Cmyk, Hex = "#000000", Components = "C60 M40 Y40 K100" };

            Assert.NotEqual(chapa.Key, rico.Key);
        }
    }
}
