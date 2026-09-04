using System;
using Optimus.Core.Audit;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>
    /// The ONE place that reads a CorelDRAW colour into the four fields its identity is made of.
    ///
    /// <para>
    /// It exists because there were three of them. <c>ColorCollector</c>, <c>FastColorCollector</c>
    /// and <c>ColorReplacer</c> each read model/hex/CMYK/spot in their own order, and a colour audited
    /// by one had to match a colour read by another. Measured on <c>x2.cdr</c>: a CMYK fill audited as
    /// <c>Cmyk|#F58634</c> and read back by the replacer as <c>Cmyk|#F58634C0 M50 Y90 K0</c> — 100
    /// shapes walked, ZERO replaced, and no error anywhere, because a key that does not match is
    /// indistinguishable from a colour that is not in the file.
    /// </para>
    /// <para>
    /// <b>The read ORDER is the whole point.</b> <c>ConvertToRGB()</c> mutates the copy in place, so
    /// the CMYK components have to come off it BEFORE anything asks for an RGB value. Getting that
    /// backwards does not throw — it silently yields a colour with no separation, which is exactly
    /// how the key lost its components.
    /// </para>
    /// </summary>
    public static class ColorIdentity
    {
        /// <summary>
        /// Fills the identity fields of <paramref name="record"/> from a colour COPY.
        ///
        /// <para>
        /// Must be handed a <c>GetCopy()</c>, never the document's live colour. Callers that also want
        /// the on-screen RGB must call that AFTER this returns.
        /// </para>
        /// </summary>
        public static void Fill(object colorCopy, ColorRecord record)
        {
            if (colorCopy == null || record == null) return;

            record.Model = ModelOf(colorCopy);

            // Components BEFORE hex, and both before any RGB conversion: this is the order that was
            // wrong in ColorCollector and right in FastColorCollector.
            if (record.Model == ColorModel.Cmyk)
            {
                try
                {
                    dynamic c = colorCopy;
                    record.Components = $"C{(int)c.CMYKCyan} M{(int)c.CMYKMagenta} " +
                                        $"Y{(int)c.CMYKYellow} K{(int)c.CMYKBlack}";
                }
                catch (Exception ex) { Note("CMYK: " + ex.Message); }
            }

            try { record.Hex = ((dynamic)colorCopy).HexValue?.ToString() ?? ""; }
            catch (Exception ex) { Note("HexValue: " + ex.Message); }

            // The spot name comes from the CUSTOMER'S file, never from a table we ship (Pantone).
            try
            {
                if ((bool)((dynamic)colorCopy).IsSpot)
                    record.SpotName = ((dynamic)colorCopy).SpotColorName?.ToString() ?? "";
            }
            catch (Exception ex) { Note("IsSpot: " + ex.Message); }
        }

        /// <summary>
        /// The identity of a LIVE colour, as <see cref="ColorRecord.Key"/> would spell it. Takes its
        /// own copy, so reading never alters the artwork.
        /// </summary>
        public static string KeyOf(object? liveColor)
        {
            if (liveColor == null) return "";

            object? copy = null;
            try
            {
                try { copy = ((dynamic)liveColor).GetCopy(); }
                catch (Exception ex) { Note("GetCopy: " + ex.Message); return ""; }
                if (copy == null) return "";

                var record = new ColorRecord();
                Fill(copy, record);

                // The SAME expression the audit uses — computed by ColorRecord itself, so the two can
                // never drift into two different formulas again.
                return record.Key;
            }
            finally { Release(copy); }
        }

        public static ColorModel ModelOf(object color)
        {
            int type;
            try { type = (int)((dynamic)color).Type; }
            catch (Exception ex) { Note("Type: " + ex.Message); return ColorModel.Unknown; }

            switch (type)
            {
                case CorelConstants.CdrColorPantone: return ColorModel.Pantone;
                case CorelConstants.CdrColorCmyk: return ColorModel.Cmyk;
                case CorelConstants.CdrColorRgb: return ColorModel.Rgb;
                case CorelConstants.CdrColorGray: return ColorModel.Gray;
                case CorelConstants.CdrColorLab: return ColorModel.Lab;
                case CorelConstants.CdrColorRegistration: return ColorModel.Registration;
                case CorelConstants.CdrColorSpot: return ColorModel.Spot;
                case CorelConstants.CdrColorMixed: return ColorModel.Mixed;
                default: return ColorModel.Unknown;
            }
        }

        /// <summary>First failure while reading a colour's identity — a silent one is what let the
        /// missing CMYK components look like "nothing to do" for as long as they did (O22).</summary>
        public static string LastError { get; private set; } = "";

        public static void ClearError() => LastError = "";

        private static void Note(string message)
        {
            if (LastError.Length == 0) LastError = message;
        }

        private static void Release(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch (Exception) { }
        }
    }
}
