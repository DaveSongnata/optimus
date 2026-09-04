using System;
using Optimus.Core.Audit;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>
    /// Collects every colour used in a document — WITHOUT altering a single one.
    ///
    /// <para>
    /// ⚠ THE HAZARD THAT SHAPES THIS CLASS: <c>Fill.UniformColor</c> is a LIVE REFERENCE into the
    /// document, and <c>ConvertToRGB()</c>/<c>ConvertToCMYK()</c> mutate IN PLACE. Reading colours
    /// naively can therefore change the customer's artwork — an audit that damages what it inspects.
    /// Every read here starts with <c>Color.GetCopy()</c> (verified, dump 3473) and works on the copy.
    /// </para>
    /// <para>
    /// Verified members: <c>Shape.Fill.Type</c> (<c>cdrFillType</c>), <c>Fill.UniformColor</c>,
    /// <c>Shape.Outline.Color</c>, <c>Color.Type</c>, <c>Color.HexValue</c> (there is no
    /// <c>ToHex</c>), <c>Color.IsSpot</c> (3462), <c>Color.SpotColorName</c> (3485),
    /// <c>Color.GetCopy()</c> (3473). Texture and mesh fills expose NO colour API — they are counted
    /// as not-inspectable rather than guessed at.
    /// </para>
    /// </summary>
    public sealed class ColorCollector
    {
        /// <summary>
        /// Reports progress as (shapesDone, shapesTotal, auditSoFar). Called at most ~50 times for the
        /// whole walk, never once per shape: on a 16.899-shape file a per-shape callback would itself
        /// become the bottleneck it is meant to report on.
        ///
        /// <para>
        /// The third argument is the LIVE audit, so the caller can show the ranking forming instead of
        /// a blank wait. Measured (Zgraggen et al., N=24): results that stream in are statistically
        /// indistinguishable from instantaneous ones, and clearly beat a blocking wait. The caller
        /// must present it as PARTIAL — an early leader can still be overtaken, and a number that
        /// silently changes its mind is worse than no number.
        /// </para>
        /// </summary>
        public Action<int, int, ColorAudit>? Progress { get; set; }

        /// <summary>True when FindShapes came up empty and the hand-rolled walk had to take over.</summary>
        public bool UsedFallbackWalk { get; private set; }

        public ColorAudit Collect(dynamic page)
        {
            var audit = new ColorAudit();
            UsedFallbackWalk = false;

            // ROTA 1 — rápida e verificada.
            object? range = FindAll(page);
            int count = 0;
            if (range != null)
            {
                try { count = (int)((dynamic)range).Count; }
                catch { count = 0; }
            }

            // ROTA 2 — a mesma lição do conversor (O23), e aqui ela custava mais caro.
            //
            // Medido em x2.cdr: FindShapes devolveu ZERO formas num documento cujas 12 cores a rota
            // rápida já tinha lido sem dificuldade. Como esta passada SUBSTITUI o resultado anterior,
            // o zero não ficava parado num canto — ele apagava as 12 cores corretas da tela e deixava
            // "0 cores" no lugar, além de desabilitar o botão que traz as cores do arquivo para a
            // paleta. Uma varredura que falha calada é ruim; uma que troca dado bom por nada é pior.
            if (count == 0)
            {
                Release(range);
                UsedFallbackWalk = true;
                object? shapesObj = null;
                try { shapesObj = page.Shapes; } catch { }
                if (shapesObj != null)
                {
                    WalkShapes(shapesObj, audit, 0);
                    Release(shapesObj);
                    if (Progress != null)
                    {
                        try { Progress(audit.ShapesInspected, audit.ShapesInspected, audit); } catch { }
                    }
                }
                return audit;
            }

            // One report per ~2% of the walk — enough for a progress bar to move smoothly, cheap
            // enough to be invisible in the total cost.
            int every = count / 50;
            if (every < 1) every = 1;

            dynamic shapes = range!;
            for (int i = 1; i <= count; i++)
            {
                object? shape = null;
                try { shape = shapes[i]; } catch { continue; }
                if (shape == null) continue;

                try
                {
                    audit.ShapesInspected++;
                    CollectFill(shape, audit);
                    CollectOutline(shape, audit);
                }
                catch { /* one unreadable shape never aborts the audit */ }
                finally { Release(shape); }

                if (Progress != null && (i % every == 0 || i == count))
                {
                    try { Progress(i, count, audit); } catch { /* reporting must never break the walk */ }
                }
            }

            Release(range);
            return audit;
        }

        private void CollectFill(object shape, ColorAudit audit)
        {
            object? fill = null;
            try
            {
                fill = ((dynamic)shape).Fill;
                if (fill == null) return;

                int fillType;
                try { fillType = (int)((dynamic)fill).Type; }
                catch { return; }

                switch (fillType)
                {
                    case CorelConstants.CdrNoFill:
                        return;

                    case CorelConstants.CdrUniformFill:
                        AddFrom(((dynamic)fill).UniformColor, ColorUsage.Fill, audit);
                        return;

                    case CorelConstants.CdrFountainFill:
                        CollectFountain(fill, audit);
                        return;

                    case CorelConstants.CdrPatternFill:
                        CollectPattern(fill, audit);
                        return;

                    default:
                        // Texture, PostScript and hatch fills expose no colour API. Counting them as
                        // "not inspectable" is honest; inventing a colour would not be.
                        audit.NotInspectable++;
                        return;
                }
            }
            catch { }
            finally { Release(fill); }
        }

        private void CollectFountain(object fill, ColorAudit audit)
        {
            object? fountain = null, colors = null;
            try
            {
                fountain = ((dynamic)fill).Fountain;
                if (fountain == null) return;

                colors = ((dynamic)fountain).Colors;
                if (colors == null) return;

                int n = (int)((dynamic)colors).Count;
                for (int i = 1; i <= n; i++)
                {
                    object? stop = null;
                    try
                    {
                        stop = ((dynamic)colors)[i];      // FountainColors is base-1
                        AddFrom(((dynamic)stop).Color, ColorUsage.Gradient, audit);
                    }
                    catch { }
                    finally { Release(stop); }
                }
            }
            catch { }
            finally { Release(colors); Release(fountain); }
        }

        private void CollectPattern(object fill, ColorAudit audit)
        {
            object? pattern = null;
            try
            {
                pattern = ((dynamic)fill).Pattern;
                if (pattern == null) { audit.NotInspectable++; return; }

                // Front/back colours exist only on a TWO-COLOUR pattern; full-colour and bitmap
                // patterns carry no readable palette.
                int patternType;
                try { patternType = (int)((dynamic)pattern).Type; }
                catch { audit.NotInspectable++; return; }

                if (patternType != 0) { audit.NotInspectable++; return; }   // 0 = cdrTwoColorPattern

                AddFrom(((dynamic)pattern).FrontColor, ColorUsage.Pattern, audit);
                AddFrom(((dynamic)pattern).BackColor, ColorUsage.Pattern, audit);
            }
            catch { audit.NotInspectable++; }
            finally { Release(pattern); }
        }

        private void CollectOutline(object shape, ColorAudit audit)
        {
            object? outline = null;
            try
            {
                outline = ((dynamic)shape).Outline;
                if (outline == null) return;

                // Outline type 0 = cdrNoOutline: nothing is drawn, so there is no colour in use.
                try { if ((int)((dynamic)outline).Type == 0) return; }
                catch { }

                AddFrom(((dynamic)outline).Color, ColorUsage.Outline, audit);
            }
            catch { }
            finally { Release(outline); }
        }

        /// <summary>
        /// Reads one colour SAFELY: takes <c>GetCopy()</c> first, so nothing we do can touch the
        /// document's live colour object.
        /// </summary>
        private void AddFrom(object? liveColor, ColorUsage usage, ColorAudit audit)
        {
            if (liveColor == null) return;

            object? copy = null;
            try
            {
                try { copy = ((dynamic)liveColor).GetCopy(); }
                catch { copy = null; }

                // No copy available means we must NOT read further: reading a live colour risks
                // mutating it. Better to report one uninspectable shape than to alter the art.
                if (copy == null) { audit.NotInspectable++; return; }

                var record = new ColorRecord { Usage = usage };

                // Identity through the shared reader, in ITS order. This method used to call ReadRgb
                // between the hex and the CMYK components — and ConvertToRGB() mutates the copy, so
                // the components were read off a colour that had already stopped being CMYK. They came
                // back empty, the key lost its separation, and every CMYK replacement silently matched
                // nothing (measured on x2.cdr: 100 shapes, 0 fills). FastColorCollector already had
                // this right; this is the sibling path that was left behind.
                ColorIdentity.Fill(copy, record);

                // RGB LAST, and never before the line above: it converts the copy.
                ReadRgb(copy, record);

                audit.Add(record);
            }
            finally
            {
                Release(copy);
                Release(liveColor);
            }
        }

        /// <summary>
        /// Reads the on-screen RGB of a colour, for the swatch. Safe to call ONLY on a
        /// <c>GetCopy()</c> — <c>ConvertToRGB()</c> mutates in place, and doing this to the live
        /// colour would repaint the customer's artwork just by looking at it.
        /// </summary>
        /// <summary>
        /// First failure seen while reading RGB, kept for the log. Measured on a real file (x2.cdr):
        /// all 14 swatches came back unreadable and the screen drew 14 blank rectangles, with nothing
        /// anywhere saying why. A swallowed exception that produces a blank UI is the worst of both
        /// worlds — it looks like a rendering bug and it is not.
        /// </summary>
        internal static string LastRgbError { get; private set; } = "";

        internal static void ReadRgb(object colorCopy, ColorRecord record)
        {
            try
            {
                dynamic c = colorCopy;
                // Convert first: a CMYK or spot colour has no meaningful RGBRed until it is one.
                try { if ((int)c.Type != CorelConstants.CdrColorRgb) c.ConvertToRGB(); }
                catch (Exception ex) { if (LastRgbError.Length == 0) LastRgbError = "ConvertToRGB: " + ex.Message; }
                record.Rgb = new[] { (int)c.RGBRed, (int)c.RGBGreen, (int)c.RGBBlue };
                record.RgbKnown = true;
            }
            catch (Exception ex)
            {
                // Leaves RgbKnown false — the UI shows a neutral checkerboard, never an invented black.
                if (LastRgbError.Length == 0) LastRgbError = ex.GetType().Name + ": " + ex.Message;
            }
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
        /// Recursão à mão por grupos e PowerClips — a travessia que o otimizador sempre usou. Só roda
        /// quando FindShapes vem vazia, então o custo extra de COM é pago apenas por documentos em
        /// que a rota barata não funciona.
        /// </summary>
        private void WalkShapes(object shapes, ColorAudit audit, int depth)
        {
            if (depth > 32) return; // um arquivo corrompido não pode virar descida infinita

            int count;
            try { count = (int)((dynamic)shapes).Count; }
            catch { return; }

            for (int i = 1; i <= count; i++)
            {
                object? shape = null;
                try { shape = ((dynamic)shapes)[i]; } catch { continue; }
                if (shape == null) continue;

                try
                {
                    audit.ShapesInspected++;

                    // O agregado de um grupo aparece como cdrColorMixed e entraria na auditoria como
                    // se fosse uma cor do desenho — uma cor fantasma que nenhum objeto usa.
                    int gtype = -1;
                    try { gtype = (int)((dynamic)shape).Type; } catch { }
                    if (gtype != CorelConstants.CdrGroupShape)
                    {
                        CollectFill(shape, audit);
                        CollectOutline(shape, audit);
                    }

                    int type = gtype;
                    try { type = (int)((dynamic)shape).Type; } catch { }

                    if (type == CorelConstants.CdrGroupShape)
                    {
                        object? inner = null;
                        try { inner = ((dynamic)shape).Shapes; } catch { }
                        if (inner != null) { WalkShapes(inner, audit, depth + 1); Release(inner); }
                    }

                    // Perguntar .PowerClip a uma forma que não tem uma lança, então a sondagem é
                    // protegida em vez de decidida por tipo — qualquer tipo pode carregar um.
                    object? pc = null;
                    try { pc = ((dynamic)shape).PowerClip; } catch { }
                    if (pc != null)
                    {
                        object? pcShapes = null;
                        try { pcShapes = ((dynamic)pc).Shapes; } catch { }
                        if (pcShapes != null) { WalkShapes(pcShapes, audit, depth + 1); Release(pcShapes); }
                        Release(pc);
                    }
                }
                catch { }
                finally { Release(shape); }
            }
        }

        private static object? FindAll(dynamic page)
        {
            try { return page.FindShapes(Type.Missing, Type.Missing, true); }
            catch { return null; }
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
