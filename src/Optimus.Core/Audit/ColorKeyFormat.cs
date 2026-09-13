namespace Optimus.Core.Audit
{
    /// <summary>
    /// THE single formula for a colour's identity key — shared by <see cref="ColorRecord.Key"/> (what
    /// the audit found in the document) and <c>Optimus.Core.Palettes.PaletteColor.Key</c> (what a
    /// palette has registered). Before this existed the two types carried the SAME expression typed
    /// twice, which is exactly the trap O27 already named once: two independent formulas drift.
    ///
    /// <para>
    /// <b>Hex is normalized here — strip a leading '#', upper-case — because it MUST be.</b> Measured
    /// on a real file (2026-09): an operator converted a CMYK orange to RGB, got
    /// <c>#F58634</c> back from <c>Color.HexValue</c> (which, per O19, "já vem com '#' embutido" and
    /// whose exact format is otherwise undocumented), while the palette's own entry for the same
    /// orange — added earlier through a manual hex field — held the bare digits <c>F58634</c> with no
    /// '#'. Same ink, same six digits, two different key strings: <c>Rgb|#F58634</c> never equals
    /// <c>Rgb|F58634</c>, so the colour audited as "in the palette, visibly" printed as FORA DA
    /// PALETA. Comparing hex textually instead of numerically was the bug; normalizing it once, here,
    /// closes every path that can produce it — including a palette file written by an older build,
    /// with no migration needed, because this runs at READ time.
    /// </para>
    /// </summary>
    public static class ColorKeyFormat
    {
        public static string Build(ColorModel model, string spotName, string hex, string components)
        {
            string identity = (spotName?.Length ?? 0) > 0
                ? NormalizeSpot(spotName!)
                : NormalizeHex(hex) + (components ?? "");
            return $"{model}|{identity}";
        }

        /// <summary>Strips the optional leading '#' and forces upper-case, so two spellings of the
        /// exact same colour value always produce the exact same key.</summary>
        public static string NormalizeHex(string? hex) =>
            (hex ?? "").Trim().TrimStart('#').ToUpperInvariant();

        /// <summary>
        /// Same reasoning as <see cref="NormalizeHex"/>, for the OTHER half of the key: every route
        /// that copies a spot name into a palette (PaletteColorAdd, PaletteCapture) does so verbatim
        /// from the exact same document reading, so this has not been observed to bite yet — but
        /// PaletteRegistry's own lookups (<c>Dictionary&lt;string, PaletteColor&gt;</c> with no
        /// comparer, <c>c.Key == color.Key</c>) are case-SENSITIVE, unlike <c>ColorAudit</c>'s
        /// (<c>StringComparer.OrdinalIgnoreCase</c>). Normalizing case here, once, means neither side
        /// has to agree on a comparer for the key to compare equal.
        /// </summary>
        public static string NormalizeSpot(string spotName) => spotName.Trim().ToUpperInvariant();
    }
}
