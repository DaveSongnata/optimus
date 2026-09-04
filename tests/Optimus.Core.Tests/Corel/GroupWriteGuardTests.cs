using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Optimus.Core.Tests.Corel
{
    /// <summary>
    /// Guards the rule that cost a client's artwork: a bulk colour pass must never WRITE to a group
    /// shape, and must never convert a "mixed" colour.
    ///
    /// <para>
    /// Measured on a real file: converting to CMYK turned the whole drawing black. Two causes,
    /// both about groups. First, <c>Shape.Fill</c> on a group is an aggregate and assigning to it
    /// cascades to every child — so visiting the group AND its children meant one group was enough
    /// to repaint everything with a single colour. Second, a group holding several colours reports
    /// <c>Color.Type = cdrColorMixed (99)</c>; "mixed" is not a colour, it is the absence of an
    /// answer, and converting it yields C100 M100 Y100 K100 — registration black.
    /// </para>
    /// <para>
    /// These are source-level assertions on purpose. The COM paths cannot be exercised without
    /// CorelDRAW, and this rule is exactly the kind that a later refactor deletes without noticing
    /// because nothing appears to break until a real file is destroyed.
    /// </para>
    /// </summary>
    public sealed class GroupWriteGuardTests
    {
        private static string Read(string relative)
        {
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null && !Directory.Exists(Path.Combine(dir, "src"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            string path = Path.Combine(dir!, relative);
            Assert.True(File.Exists(path), "não achei " + path);
            return File.ReadAllText(path);
        }

        [Fact]
        public void Converter_refuses_to_write_to_a_group()
        {
            string src = Read("src/Optimus.Interop/ColorModeConverter.cs");
            Assert.Contains("IsGroup(shape)", src);
            Assert.Contains("CorelConstants.CdrGroupShape", src);
        }

        [Fact]
        public void Converter_treats_mixed_as_not_a_colour()
        {
            string src = Read("src/Optimus.Interop/ColorModeConverter.cs");

            // "Misto" tem de estar no MESMO bloco que recusa spot/pantone/registro.
            Match m = Regex.Match(src, @"if \(type == CorelConstants\.CdrColorMixed[\s\S]{0,320}?NotConvertible\+\+;");
            Assert.True(m.Success, "cdrColorMixed precisa cair no bloco de não-convertível");
        }

        [Fact]
        public void Replacer_refuses_to_write_to_a_group()
        {
            string src = Read("src/Optimus.Interop/ColorReplacer.cs");
            Assert.Contains("CdrGroupShape", src);
        }

        [Fact]
        public void Collector_does_not_read_a_groups_aggregate_as_a_colour()
        {
            // Um agregado de grupo entraria na auditoria como uma cor que nenhum objeto usa.
            string src = Read("src/Optimus.Interop/ColorCollector.cs");
            Assert.Contains("CdrGroupShape", src);
        }

        [Fact]
        public void Colour_writes_go_through_the_verified_setters_not_ApplyUniformFill_on_a_shape()
        {
            // O22: ApplyUniformFill vive em IVGFill e IVGShapeRange, nunca em IVGShape. Chamá-lo num
            // shape morre com "does not contain a definition for 'ApplyUniformFill'" — e o catch em
            // volta fazia isso parecer "nada a fazer".
            foreach (string f in new[] { "src/Optimus.Interop/ColorModeConverter.cs",
                                         "src/Optimus.Interop/ColorReplacer.cs" })
            {
                string src = Read(f);
                Assert.DoesNotContain("shape).ApplyUniformFill", src);
            }
        }
    }
}
