using System.Collections.Generic;

namespace Optimus.Core.Palettes
{
    /// <summary>
    /// Why a palette mutation did or did not happen. It exists so a refused change can SAY what was
    /// wrong: a create that quietly does nothing because the name is taken is indistinguishable, from
    /// the operator's chair, from a create that failed to save.
    /// </summary>
    public enum PaletteChange
    {
        Ok,

        /// <summary>Another palette already answers to that name (§5.3).</summary>
        NameTaken,

        /// <summary>That exact colour value is already in this palette (§6.3).</summary>
        ColorAlreadyInPalette,

        /// <summary>No palette by that name.</summary>
        NotFound,

        /// <summary>Blank name — a palette without a name cannot be chosen later.</summary>
        InvalidName,

        /// <summary>The file could not be read as SVG at all (§17).</summary>
        UnreadableFile,

        /// <summary>The SVG parsed, but carries no colour to build a palette from (§17).</summary>
        NoColors,
    }

    /// <summary>
    /// One colour inside a registered palette. Carries the same identity key as
    /// <see cref="Optimus.Core.Audit.ColorRecord"/> so a palette entry and an audited colour can be
    /// matched without ever touching COM.
    /// </summary>
    public sealed class PaletteColor
    {
        /// <summary>Operator-given name — "cor 1", "Laranja Aisten", whatever the shop calls it.</summary>
        public string Name { get; set; } = "";

        public Audit.ColorModel Model { get; set; } = Audit.ColorModel.Rgb;

        public string Hex { get; set; } = "";

        /// <summary>CMYK components in the same "C.. M.. Y.. K.." text form <c>ColorCollector</c> writes.</summary>
        public string Components { get; set; } = "";

        /// <summary>Spot/Pantone name, mirroring <c>ColorRecord.SpotName</c>.</summary>
        public string SpotName { get; set; } = "";

        /// <summary>
        /// Identity for matching against an audited <see cref="Audit.ColorRecord"/> — built by the
        /// SAME shared formula (<see cref="Audit.ColorKeyFormat"/>), not a hand-copied twin of it.
        /// Two independent expressions of "the same" key is exactly how a palette entry stored as
        /// bare hex digits (typed by hand) stopped matching an audited colour whose
        /// <c>Color.HexValue</c> carries a leading '#' — literally the same six digits, two different
        /// key strings.
        /// </summary>
        public string Key => Audit.ColorKeyFormat.Build(Model, SpotName, Hex, Components);
    }

    /// <summary>A named group of colours the shop has standardised on.</summary>
    public sealed class ColorPalette
    {
        public string Name { get; set; } = "";
        public Audit.ColorModel PreferredModel { get; set; } = Audit.ColorModel.Rgb;

        // A public setter, not a get-only collection: System.Text.Json does not populate a get-only
        // list property on deserialize, so a get-only Colors here would silently come back empty on
        // every reload — measured while writing PaletteRegistryTests.
        public List<PaletteColor> Colors { get; set; } = new List<PaletteColor>();
    }
}
