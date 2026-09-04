using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Optimus.Core.Tests.Ui
{
    /// <summary>
    /// Locks the design system down to ONE source.
    ///
    /// <para>
    /// The three screens used to carry divergent copies, with different vocabularies for the same
    /// things: the maintenance app said <c>--orange/--red/--green</c>, the installer said
    /// <c>--brand-strong/--t</c>, the docker used the current names. That is not a naming preference
    /// — it is the proof they were three systems. It is how the orange that failed the contrast
    /// audit (O20) stayed alive in the maintenance app long after it was removed from the docker.
    /// </para>
    /// <para>
    /// These are source-level assertions because the drift they guard against is invisible until a
    /// screenshot of a screen nobody was looking at shows the old colour still there.
    /// </para>
    /// </summary>
    public sealed class DesignSystemTests
    {
        private static readonly string[] Screens =
        {
            "src/Optimus.AddIn/wwwroot/index.html",
            "src/Optimus.Maintenance/wwwroot/index.html",
            "installer/wwwroot/index.html",
        };

        private static string Root()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null && !Directory.Exists(Path.Combine(dir, "src"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir!;
        }

        private static string Read(string relative)
        {
            string path = Path.Combine(Root(), relative);
            Assert.True(File.Exists(path), "não achei " + path);
            return File.ReadAllText(path);
        }

        /// <summary>The hand-written style block — the generated one carries id="tw".</summary>
        private static string OwnCss(string html)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Match m in Regex.Matches(html, @"<style>([\s\S]*?)</style>")) sb.Append(m.Groups[1].Value);
            return sb.ToString();
        }

        [Fact]
        public void The_system_lives_in_exactly_one_file()
        {
            string css = Read("src/ui/design-system.css");
            Assert.Contains("@tailwind", css);
            Assert.Contains("@layer base", css);
            Assert.Contains("@layer components", css);
        }

        [Fact]
        public void No_screen_declares_its_own_tokens()
        {
            var offenders = new List<string>();
            foreach (string s in Screens)
                if (Regex.IsMatch(OwnCss(Read(s)), @":root\s*\{")) offenders.Add(s);

            Assert.True(offenders.Count == 0,
                "estas telas voltaram a declarar tokens próprios:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_screen_receives_the_generated_system()
        {
            foreach (string s in Screens)
                Assert.Contains("<style id=\"tw\">", Read(s));
        }

        [Fact]
        public void No_screen_uses_a_token_the_system_does_not_define()
        {
            string system = Read("src/ui/design-system.css");

            foreach (string screen in Screens)
            {
                string html = Read(screen);
                var used = Regex.Matches(html, @"var\((--[a-z0-9-]+)\)")
                                .Select(m => m.Groups[1].Value)
                                // --tw-* are Tailwind's own internals, emitted by the utilities.
                                .Where(t => !t.StartsWith("--tw-"))
                                .Distinct();

                foreach (string token in used)
                    Assert.True(system.Contains(token + ":"),
                        screen + " usa " + token + ", que o sistema não define");
            }
        }

        [Fact]
        public void The_brand_colour_is_the_one_that_passes_contrast()
        {
            // MEASURED: white on #EA580C is 3.56:1 and fails; on #C2410C it is 5.18:1 and passes.
            // The lighter orange may still fill a shape — it must never be the colour under text.
            string css = Read("src/ui/design-system.css");
            Assert.Matches(@"--brand:\s*#C2410C", css);
            Assert.Matches(@"--brand-bright:\s*#EA580C", css);
        }
    }
}
