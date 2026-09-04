using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>Outcome of a colour-mode conversion pass, in the terms the operator asked about.</summary>
    public sealed class ColorConvertResult
    {
        public int ShapesInspected { get; set; }
        public int FillsChanged { get; set; }
        public int OutlinesChanged { get; set; }

        /// <summary>
        /// Fills the pass could not convert: fountain, pattern, texture and hatch fills hold their
        /// colours inside structures this pass does not walk, and spot colours are left alone on
        /// purpose. Reported, never hidden — a conversion that silently leaves half the drawing in
        /// the old mode is worse than one that refuses to run.
        /// </summary>
        public int NotConvertible { get; set; }

        /// <summary>Colours already in the requested mode.</summary>
        public int AlreadyInMode { get; set; }

        /// <summary>
        /// Colours whose model could not even be read. The first version returned false here with no
        /// counter at all, so a total COM failure and a document already in the right mode produced
        /// byte-identical results — zero everywhere — and the screen had to guess which one it was.
        /// </summary>
        public int ReadFailed { get; set; }

        /// <summary>Colours that were the wrong model and still refused to convert or apply.</summary>
        public int ConvertFailed { get; set; }

        /// <summary>First real exception message, so the log names a cause instead of a symptom.</summary>
        public string FirstError { get; set; } = "";

        /// <summary>
        /// True when FindShapes came back empty and the hand-rolled walk had to take over. Worth
        /// reporting: it means the fast route is failing on this document, which is a fact about the
        /// file (or the CorelDRAW build) that we want to see in the log rather than infer later.
        /// </summary>
        public bool UsedFallbackWalk { get; set; }

        /// <summary>
        /// Group shapes traversed but deliberately NOT written to. Reported because it explains the
        /// gap between "shapes inspected" and "colours changed" that would otherwise read as a bug —
        /// and because writing to a group was the bug that repainted a client's artwork black.
        /// </summary>
        public int GroupsSkipped { get; set; }

        /// <summary>
        /// How many colours of each <c>cdrColorType</c> the walk actually met. This census settles an
        /// argument the product could not settle before: the audit counts colours from the DOCUMENT
        /// PALETTE, which over-reports (a colour survives the deletion of the last object using it —
        /// O19), while this pass counts colours actually attached to shapes. When the two disagree,
        /// this is the honest number.
        /// </summary>
        public Dictionary<int, int> Census { get; } = new Dictionary<int, int>();

        public int TotalChanged => FillsChanged + OutlinesChanged;

        public int CountOf(int cdrColorType) => Census.TryGetValue(cdrColorType, out int n) ? n : 0;

        /// <summary>Census in "MODELO=n" form, for the log and for the screen.</summary>
        public string CensusText()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, int> kv in Census) parts.Add(Name(kv.Key) + "=" + kv.Value);
            return parts.Count == 0 ? "(nenhuma)" : string.Join(" ", parts);
        }

        private static string Name(int t) => t switch
        {
            CorelConstants.CdrColorCmyk => "CMYK",
            CorelConstants.CdrColorRgb => "RGB",
            CorelConstants.CdrColorGray => "Cinza",
            CorelConstants.CdrColorSpot => "Spot",
            CorelConstants.CdrColorPantone => "Pantone",
            CorelConstants.CdrColorLab => "Lab",
            CorelConstants.CdrColorRegistration => "Registro",
            CorelConstants.CdrColorMixed => "Misto",
            _ => "tipo" + t,
        };
    }

    /// <summary>
    /// Converts every uniform fill and outline colour in the document (or the selection) to one
    /// colour model.
    ///
    /// <para>
    /// The converted colour is written back through <c>Fill.UniformColor =</c> and
    /// <c>Outline.Color =</c>, both verified setters in the typelib dump: <c>IVGFill.set_UniformColor</c>
    /// at line 4658 and <c>IVGOutline.set_Color</c> at line 5287.
    /// The first version called <c>Shape.ApplyUniformFill</c>, which measured as
    /// <c>'System.__ComObject' does not contain a definition for 'ApplyUniformFill'</c> on a real
    /// file: that method lives on <b>IVGFill</b> and <b>IVGShapeRange</b> (dump lines 4668 and
    /// 6316), <b>not</b> on IVGShape. Verifying that a member EXISTS is not enough — it has to be
    /// verified on the interface of the object actually being called.
    /// </para>
    /// <para>
    /// Spot, Pantone and registration colours are deliberately left alone. A spot names an INK, not a
    /// colour value; flattening it to process turns a two-ink job into a four-ink one, which is a
    /// production decision belonging to the shop and its printer, not to a bulk action.
    /// </para>
    /// </summary>
    public sealed class ColorModeConverter
    {
        public ColorConvertResult Convert(dynamic app, dynamic doc, bool toCmyk, bool selectionOnly,
            Action<string>? log = null)
        {
            var result = new ColorConvertResult();

            // O12: without this, CorelDRAW repaints and fires events after EVERY edit.
            using (new BulkEditScope((object)app, log))
            {
                void Visit(object shape)
                {
                    result.ShapesInspected++;

                    // NUNCA escrever num GRUPO. Shape.Fill de um grupo é um agregado: atribuir nele
                    // aplica a cor a TODOS os filhos de uma vez. Como a passada visita o grupo e os
                    // filhos, um único grupo bastava para a arte inteira virar uma cor só — foi o que
                    // pintou o desenho de preto num arquivo real. O grupo continua sendo percorrido;
                    // ele só deixa de ser alvo de escrita.
                    if (IsGroup(shape)) { result.GroupsSkipped++; return; }

                    ConvertFill(shape, toCmyk, result);
                    ConvertOutline(shape, toCmyk, result);
                }

                // ROUTE 1 — the fast, verified one.
                // The range is pinned to `object?` first: FindScope takes dynamic parameters, so
                // feeding its result straight into RunOverRange would make the whole call
                // dynamically bound, and a local function cannot be passed to one of those.
                object? found = FindScope(app, doc, selectionOnly, result);
                int viaFind = RunOverRange(found, Visit, result);

                // ROUTE 2 — the reason this method has two.
                //
                // On a real client file (x2.cdr) FindShapes returned ZERO shapes while the very same
                // document happily handed over a 14-colour palette. Zero shapes and a failed call are
                // indistinguishable from here, and both end the pass silently, so the operator saw
                // "converteu 0" with nothing to act on.
                //
                // So when the fast route yields nothing, the walk falls back to Page.Shapes and
                // recurses by hand through groups and PowerClips — the traversal the optimizer has
                // always used. It costs more COM calls, but it only runs when the cheap route already
                // came back empty, and "slower" beats "silently did nothing".
                if (viaFind == 0)
                {
                    result.UsedFallbackWalk = true;
                    object? page = null;
                    try { page = doc.ActivePage; }
                    catch (Exception ex) { Note(result, ex); }

                    if (page != null)
                    {
                        object? shapes = null;
                        try { shapes = ((dynamic)page).Shapes; }
                        catch (Exception ex) { Note(result, ex); }
                        if (shapes != null) { WalkShapes(shapes, Visit, result, 0); Release(shapes); }
                        Release(page);
                    }
                }
            }

            log?.Invoke($"Conversão: {result.ShapesInspected} forma(s); " +
                $"{result.FillsChanged} preenchimento(s) e {result.OutlinesChanged} contorno(s) convertidos; " +
                $"cores encontradas: {result.CensusText()}");
            return result;
        }

        /// <summary>
        /// Runs <paramref name="visit"/> over a shape range and returns how many shapes it saw.
        /// Zero means "this route found nothing", which is what triggers the fallback.
        /// </summary>
        private static int RunOverRange(object? range, Action<object> visit, ColorConvertResult result)
        {
            if (range == null) return 0;
            int seen = 0;
            try
            {
                int count;
                try { count = (int)((dynamic)range).Count; }
                catch (Exception ex) { Note(result, ex); return 0; }

                dynamic shapes = range;
                for (int i = 1; i <= count; i++)
                {
                    object? shape = null;
                    try { shape = shapes[i]; } catch (Exception ex) { Note(result, ex); continue; }
                    if (shape == null) continue;
                    try { visit(shape); seen++; }
                    catch (Exception ex) { Note(result, ex); }
                    finally { Release(shape); }
                }
            }
            finally { Release(range); }
            return seen;
        }

        /// <summary>
        /// Hand-rolled recursive traversal: groups AND PowerClips. Design files are full of
        /// PowerClips, and art hidden inside one is exactly the art that made v1.0 report success
        /// while changing nothing.
        /// </summary>
        private static void WalkShapes(object shapes, Action<object> visit, ColorConvertResult result,
            int depth)
        {
            if (depth > 32) return; // a corrupt file must not become an infinite descent

            int count;
            try { count = (int)((dynamic)shapes).Count; }
            catch (Exception ex) { Note(result, ex); return; }

            for (int i = 1; i <= count; i++)
            {
                object? shape = null;
                try { shape = ((dynamic)shapes)[i]; } catch (Exception ex) { Note(result, ex); continue; }
                if (shape == null) continue;

                try
                {
                    visit(shape);

                    int type = -1;
                    try { type = (int)((dynamic)shape).Type; } catch { }

                    if (type == CorelConstants.CdrGroupShape)
                    {
                        object? inner = null;
                        try { inner = ((dynamic)shape).Shapes; } catch { }
                        if (inner != null) { WalkShapes(inner, visit, result, depth + 1); Release(inner); }
                    }

                    // Asking a shape without a PowerClip for .PowerClip throws, so the probe is
                    // wrapped rather than guarded by a type check — any shape type can carry one.
                    object? pc = null;
                    try { pc = ((dynamic)shape).PowerClip; } catch { }
                    if (pc != null)
                    {
                        object? pcShapes = null;
                        try { pcShapes = ((dynamic)pc).Shapes; } catch { }
                        if (pcShapes != null) { WalkShapes(pcShapes, visit, result, depth + 1); Release(pcShapes); }
                        Release(pc);
                    }
                }
                catch (Exception ex) { Note(result, ex); }
                finally { Release(shape); }
            }
        }

        /// <summary>
        /// True for a group container. Reading a group's Fill gives an aggregate, and writing it
        /// cascades to every child — which is why this pass only ever writes to leaf shapes.
        /// </summary>
        private static bool IsGroup(object shape)
        {
            try { return (int)((dynamic)shape).Type == CorelConstants.CdrGroupShape; }
            catch { return false; }
        }

        private static void ConvertFill(object shape, bool toCmyk, ColorConvertResult result)
        {
            object? fill = null;
            try
            {
                fill = ((dynamic)shape).Fill;
                if (fill == null) return;

                int fillType;
                try { fillType = (int)((dynamic)fill).Type; }
                catch (Exception ex) { Note(result, ex); return; }

                // Type 0 is "no fill" — not a failure, and not something worth reporting.
                if (fillType == 0) return;

                if (fillType != CorelConstants.CdrUniformFill) { result.NotConvertible++; return; }

                object? converted = Converted(((dynamic)fill).UniformColor, toCmyk, result);
                if (converted == null) return;

                try
                {
                    // ApplyUniformFill vive em IVGFill e em IVGShapeRange — NAO em IVGShape (dump,
                    // linhas 4668 e 6316). Chamar no shape rendia
                    // "'System.__ComObject' does not contain a definition for 'ApplyUniformFill'",
                    // engolido por um catch, e o resultado era zero preenchimento convertido num
                    // arquivo cheio deles. O setter de UniformColor esta verificado na linha 4658 e
                    // e o caminho mais direto: sem argumento opcional, sem sobrecarga.
                    ((dynamic)fill).UniformColor = converted;
                    result.FillsChanged++;
                }
                catch (Exception ex) { Note(result, ex); result.ConvertFailed++; }
                finally { Release(converted); }
            }
            catch (Exception ex) { Note(result, ex); }
            finally { Release(fill); }
        }

        private static void ConvertOutline(object shape, bool toCmyk, ColorConvertResult result)
        {
            object? outline = null;
            try
            {
                outline = ((dynamic)shape).Outline;
                if (outline == null) return;

                // Type 0 is "no outline" — asking for its Color throws.
                try { if ((int)((dynamic)outline).Type == 0) return; }
                catch { return; }

                object? converted = Converted(((dynamic)outline).Color, toCmyk, result);
                if (converted == null) return;

                try
                {
                    ((dynamic)outline).Color = converted;
                    result.OutlinesChanged++;
                }
                catch (Exception ex) { Note(result, ex); result.ConvertFailed++; }
                finally { Release(converted); }
            }
            catch (Exception ex) { Note(result, ex); }
            finally { Release(outline); }
        }

        /// <summary>
        /// Returns a COPY of <paramref name="color"/> converted to the requested model, or null when
        /// there is nothing to do. Working on a copy is what makes this safe to call on a live
        /// document colour: <c>ConvertToCMYK()</c> mutates in place, so converting the live object
        /// would edit the artwork before the caller had decided to apply anything.
        /// </summary>
        private static object? Converted(object? color, bool toCmyk, ColorConvertResult result)
        {
            if (color == null) return null;
            try
            {
                int type;
                try { type = (int)((dynamic)color).Type; }
                catch (Exception ex) { Note(result, ex); result.ReadFailed++; return null; }

                // Census first: it must record what is really in the drawing, including the colours
                // this pass then decides not to touch.
                result.Census[type] = result.CountOf(type) + 1;

                // "Misto" não é uma cor: é o que o CorelDRAW responde quando o objeto tem VÁRIAS.
                // Convertê-lo produz C100 M100 Y100 K100 — preto de registro — e gravar isso é
                // destruir a arte. Spot, Pantone e registro ficam de fora por decisão de produção:
                // um spot nomeia uma TINTA, e achatá-lo para processo é escolha da gráfica.
                if (type == CorelConstants.CdrColorMixed ||
                    type == CorelConstants.CdrColorSpot ||
                    type == CorelConstants.CdrColorPantone ||
                    type == CorelConstants.CdrColorRegistration)
                {
                    result.NotConvertible++;
                    return null;
                }

                int want = toCmyk ? CorelConstants.CdrColorCmyk : CorelConstants.CdrColorRgb;
                if (type == want) { result.AlreadyInMode++; return null; }

                object? copy;
                try { copy = ((dynamic)color).GetCopy(); }
                catch (Exception ex) { Note(result, ex); result.ConvertFailed++; return null; }
                if (copy == null) { result.ConvertFailed++; return null; }

                try
                {
                    if (toCmyk) ((dynamic)copy).ConvertToCMYK();
                    else ((dynamic)copy).ConvertToRGB();
                    return copy;
                }
                catch (Exception ex)
                {
                    Note(result, ex);
                    result.ConvertFailed++;
                    Release(copy);
                    return null;
                }
            }
            finally { Release(color); }
        }

        private static object? FindScope(dynamic app, dynamic doc, bool selectionOnly,
            ColorConvertResult result)
        {
            try
            {
                if (selectionOnly)
                {
                    object? sel = doc.SelectionRange;
                    if (sel != null && (int)((dynamic)sel).Count > 0) return sel;
                    Release(sel);
                }
            }
            catch (Exception ex) { Note(result, ex); }

            // FindShapes(Recursive:=True) was measured to descend into PowerClips, which is what makes
            // it the right scope here: art hidden in a PowerClip is exactly what a client file is full
            // of, and missing it is how v1.0 managed to optimise 0% of a real drawing.
            try { return doc.ActivePage.FindShapes(Type.Missing, Type.Missing, true); }
            catch (Exception ex) { Note(result, ex); return null; }
        }

        /// <summary>Keeps the FIRST real error — later ones are usually the same cause repeated.</summary>
        private static void Note(ColorConvertResult result, Exception ex)
        {
            if (result.FirstError.Length == 0)
                result.FirstError = ex.GetType().Name + ": " + ex.Message;
        }

        private static void Release(object? o)
        {
            // O9: COM cost is per object TOUCHED, and an STA Release runs on the owning thread.
            if (o == null) return;
            try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { }
        }
    }
}
