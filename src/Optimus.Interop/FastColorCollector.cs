using System;
using Optimus.Core.Audit;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>
    /// Lists the colours of a document in SECONDS instead of minutes, by asking CorelDRAW to do the
    /// scan in its own native code instead of walking every shape over COM.
    ///
    /// <para>
    /// WHY. <see cref="ColorCollector"/> touches every shape and reads ~25 late-bound properties from
    /// each: on a 16.899-shape file that is ~420.000 COM calls and <b>90 seconds</b> (measured). Davi's
    /// question was exactly right — "para ver as cores temos que passar pelos 16 mil elementos?"
    /// No. <c>Document.AddColorsToDocPalette()</c> makes CorelDRAW fill its own document palette
    /// internally, and <c>Document.Palette</c> then hands back the finished list — a few hundred COM
    /// reads total, because the loop is over COLOURS (~128) rather than over SHAPES (~16.899).
    /// </para>
    /// <para>
    /// WHAT IT COSTS. The document palette knows WHICH colours exist, not HOW MUCH each is used, and
    /// it is known to over-report (a colour stays after the object using it is deleted, until the
    /// palette is refreshed). So this is the fast answer to "what is in here", and
    /// <see cref="ColorCollector"/> remains the slow, exact answer to "how much of the drawing is this
    /// colour" — the operator asks for that second pass only when they want it.
    /// </para>
    /// <para>
    /// Verified members: <c>Document.AddColorsToDocPalette(Boolean SelectedOnly?, Int32
    /// MaxColorsPerBitmap?)</c> and <c>Document.Palette</c> (typelib dump, IVGDocument);
    /// <c>Palette.ColorCount</c> (5486), <c>Palette.Color(Index)</c> (5479).
    /// </para>
    /// </summary>
    public sealed class FastColorCollector
    {
        public sealed class Result
        {
            public ColorAudit Audit { get; } = new ColorAudit();

            /// <summary>False when this route was not available and the caller must fall back to the
            /// full shape walk.</summary>
            public bool Available { get; set; }

            public string Note { get; set; } = "";
        }

        /// <summary>Reads the document palette. Never throws; never mutates artwork.</summary>
        public Result Collect(dynamic doc, Action<string>? log = null)
        {
            var result = new Result();
            object? palette = null;

            try
            {
                // Ask CorelDRAW to (re)fill its own document palette from the artwork. Bitmaps are
                // capped: a photo can otherwise contribute thousands of colours that are not design
                // decisions and would bury the ones that are.
                // ESVAZIAR ANTES DE RELER — sem isto a paleta do documento ACUMULA.
                //
                // Medido em x2.cdr: o arquivo tinha 14 cores; depois de converter tudo para CMYK a
                // auditoria passou a listar 19. Não trocou nada de lugar: somou as CMYK novas e
                // manteve as RGB velhas, porque AddColorsToDocPalette só ACRESCENTA. O operador
                // então relia a tela, via RGB e CMYK ainda misturados, e concluía — com toda a razão
                // aparente — que a conversão não tinha sido aplicada. Ela tinha: a passagem seguinte
                // encontrou 91 cores, todas CMYK, zero RGB.
                //
                // Isto também corrige a super-notificação descrita em O19: uma cor sobrevivia na
                // paleta depois de o último objeto que a usava ser apagado. O custo é uma chamada
                // COM por cor removida (dezenas), contra as 16.899 formas que a rota lenta percorre.
                EmptyPalette(doc, log);

                try { doc.AddColorsToDocPalette(false, 16); }
                catch (Exception ex) { log?.Invoke("AddColorsToDocPalette indisponível: " + ex.Message); }

                palette = doc.Palette;
                if (palette == null)
                {
                    result.Note = "Este documento não expõe paleta própria.";
                    return result;
                }

                int count;
                try { count = (int)((dynamic)palette).ColorCount; }
                catch (Exception ex)
                {
                    result.Note = "Paleta do documento ilegível: " + ex.Message;
                    return result;
                }

                for (int i = 1; i <= count; i++)
                {
                    object? color = null, copy = null;
                    try
                    {
                        color = ((dynamic)palette).Color[i];
                        if (color == null) continue;

                        // Same rule as everywhere else: never inspect a live colour, and never let a
                        // read mutate the document.
                        try { copy = ((dynamic)color).GetCopy(); } catch { copy = null; }
                        if (copy == null) continue;

                        var record = new ColorRecord { Usage = ColorUsage.Fill };
                        ColorIdentity.Fill(copy, record);

                        // Read RGB LAST: it converts the copy, which would corrupt the CMYK reading above.
                        ColorCollector.ReadRgb(copy, record);

                        result.Audit.Add(record);
                    }
                    catch { /* one unreadable entry never aborts the list */ }
                    finally { Release(copy); Release(color); }
                }

                // Quantas cores saíram legíveis, e por que as outras não. Sem esta linha, uma tela
                // com 14 amostras em branco não tinha NADA em lugar nenhum explicando o motivo.
                int legiveis = 0;
                foreach (var r in result.Audit.Unique) if (r.RgbKnown) legiveis++;
                log?.Invoke($"Cores legíveis: {legiveis}/{result.Audit.Unique.Count}" +
                    (legiveis < result.Audit.Unique.Count && ColorCollector.LastRgbError.Length > 0
                        ? " — " + ColorCollector.LastRgbError : ""));

                result.Available = result.Audit.Unique.Count > 0;
                if (!result.Available) result.Note = "A paleta do documento veio vazia.";
                return result;
            }
            catch (Exception ex)
            {
                result.Note = ex.Message;
                return result;
            }
            finally { Release(palette); }
        }

        private static ColorModel ModelOf(object color)
        {
            int type;
            try { type = (int)((dynamic)color).Type; }
            catch { return ColorModel.Unknown; }

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

        /// <summary>
        /// Empties the document palette so the next scan describes the artwork as it is NOW.
        /// Removes from the end: removing index 1 repeatedly renumbers every remaining entry, and a
        /// forward loop would skip every other colour.
        /// </summary>
        private static void EmptyPalette(dynamic doc, Action<string>? log)
        {
            object? palette = null;
            try
            {
                palette = doc.Palette;
                if (palette == null) return;

                int count;
                try { count = (int)((dynamic)palette).ColorCount; }
                catch { return; }

                int removed = 0;
                for (int i = count; i >= 1; i--)
                {
                    try { ((dynamic)palette).RemoveColor(i); removed++; }
                    catch { /* a locked entry costs one colour, never the scan */ }
                }

                if (removed > 0 && removed < count)
                    log?.Invoke($"Paleta do documento: {removed}/{count} entradas antigas removidas.");
            }
            catch (Exception ex) { log?.Invoke("Não foi possível limpar a paleta: " + ex.Message); }
            finally { Release(palette); }
        }

        private static void Release(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch { }
        }
    }
}
