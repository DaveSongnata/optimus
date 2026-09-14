using System;
using System.Collections.Generic;
using Optimus.Core.Audit;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>What colour to hunt for, what to replace it with, and where to look.</summary>
    public sealed class ColorReplaceRequest
    {
        /// <summary>Identity of the colour to replace — same format as <c>ColorRecord.Key</c>.</summary>
        public string SourceKey { get; set; } = "";

        public ColorModel TargetModel { get; set; } = ColorModel.Rgb;

        /// <summary>0-255 each, used when <see cref="TargetModel"/> is RGB.</summary>
        public int[] TargetRgb { get; set; } = new int[3];

        /// <summary>0-100 each, used when <see cref="TargetModel"/> is CMYK.</summary>
        public int[] TargetCmyk { get; set; } = new int[4];

        /// <summary>When true, only shapes in <c>Application.ActiveSelectionRange</c> are touched —
        /// the "somente selecionado" checkbox on the whiteboard. When false, the WHOLE active page is
        /// in scope — "todas as cores".</summary>
        public bool SelectionOnly { get; set; }

        /// <summary>
        /// When true only the FILL is written; outlines keep the colour they have. This is what the
        /// "apply a palette colour" action asks for: a contour is a deliberate decision about how a
        /// shape reads at a distance, and repainting it as a side effect of choosing a fill colour is
        /// damage the operator never asked for.
        /// </summary>
        public bool FillOnly { get; set; }
    }

    public sealed class ColorReplaceResult
    {
        public int ShapesInspected;
        public int FillsChanged;
        public int OutlinesChanged;

        /// <summary>Shapes that matched the source colour on a fill kind this pass does not mutate
        /// (fountain stops, pattern front/back) — counted honestly instead of silently skipped.</summary>
        public int NotReplaceable;

        /// <summary>True when FindShapes came back empty and the hand-rolled walk took over (O23).
        /// Kept for the "selecionado" path; the whole-page path always walks now (see O28) so this
        /// stays true there for backward-compatible log parsing.</summary>
        public bool UsedFallbackWalk;

        /// <summary>
        /// How many shapes <c>Page.FindShapes(Recursive:=True)</c> reported, measured purely for the
        /// log — never used to gather what gets visited. A non-zero, even LARGE count here does not
        /// mean it saw everything: O28 measured 3014 shapes back from FindShapes on a real file while
        /// the colour it was hunting for lived on a shape only reachable through a PowerClip, which
        /// FindShapes silently never expands. Comparing this to ShapesInspected is what makes the next
        /// docker.log self-diagnosing without another round of DevTools.
        /// </summary>
        public int ShapesViaFindShapes;

        /// <summary>Of ShapesInspected, how many were reached ONLY because a PowerClip was opened —
        /// i.e. shapes FindShapes' own recursion does not reach (O28). Zero here despite a colour
        /// being provably present (confirmed by the audit) points away from the PowerClip theory and
        /// at something else — a mesh/bitmap fill, or a key-format mismatch (O27).</summary>
        public int ShapesViaPowerClip;

        /// <summary>Group containers traversed but never written to — writing a group's fill cascades (O24).</summary>
        public int GroupsSkipped;

        /// <summary>First real exception, so a failed pass names a cause instead of looking like "nothing to do".</summary>
        public string FirstError = "";

        /// <summary>
        /// Selection-only was asked for and CorelDRAW had nothing selected. Without this flag the
        /// pass returns the same zeros as "that colour is not in this file", and the screen has to
        /// guess which of the two it is — the exact mistake O22 cost a week over.
        /// </summary>
        public bool SelectionEmpty;

        /// <summary>
        /// Distinct colour identities actually found in the drawing, capped. A pass that inspects a
        /// hundred shapes and matches nothing has to be able to say WHAT it saw instead — without
        /// this, a key mismatch and an absent colour produce the same empty result, which is what
        /// made the CMYK bug survive as long as it did.
        /// </summary>
        public readonly List<string> KeysSeen = new List<string>();

        internal void NoteKey(string key)
        {
            if (key.Length == 0 || KeysSeen.Count >= 16 || KeysSeen.Contains(key)) return;
            KeysSeen.Add(key);
        }

        public string KeysSeenText() => string.Join(" | ", KeysSeen);

        public int TotalChanged => FillsChanged + OutlinesChanged;
    }

    /// <summary>
    /// Replaces one colour with another across a document or a selection — the "troca em tempo real"
    /// from Davi's whiteboard: mark a colour found in the art as equivalent to a palette colour, and
    /// every shape using it switches over.
    ///
    /// <para>
    /// Scope is exactly two options, matching the two checkboxes on the whiteboard: selection-only or
    /// the whole active page. Matching reuses <see cref="Audit.ColorRecord.Key"/> so the source colour
    /// identified by <see cref="ColorCollector"/>'s audit is exactly what gets matched here — a table
    /// row and a replace target are the same identity, never two independent colour comparisons that
    /// could silently disagree.
    /// </para>
    /// <para>
    /// Only SOLID fills and outlines are mutated in this pass — <c>Shape.ApplyUniformFill</c> (dump
    /// 6316) and <c>Outline.set_Color</c> (dump 5287), both verified. Fountain stops and two-colour
    /// pattern front/back are counted as <see cref="ColorReplaceResult.NotReplaceable"/> rather than
    /// silently left untouched-and-unreported — the same honesty rule <c>ColorCollector</c> already
    /// applies to texture/mesh fills.
    /// </para>
    /// </summary>
    public sealed class ColorReplacer
    {
        public ColorReplaceResult Replace(dynamic app, dynamic doc, ColorReplaceRequest request, Action<string>? log = null)
        {
            var result = new ColorReplaceResult();
            if (request == null || string.IsNullOrWhiteSpace(request.SourceKey)) return result;

            object? targetColor = CreateTargetColor(app, request);
            if (targetColor == null)
            {
                log?.Invoke("Não foi possível criar a cor de destino.");
                return result;
            }

            using (new BulkEditScope(app, log))
            {
                void Visit(object shape)
                {
                    result.ShapesInspected++;
                    ReplaceOnShape(shape, request.SourceKey, targetColor, request.FillOnly, result);
                }

                if (request.SelectionOnly)
                {
                    object? found = FindScope(app, doc, true, result);
                    int viaFind = RunOverRange(found, Visit, result);

                    // Selection-only with nothing selected produces the SAME zeros as "that colour is
                    // not in this file". Recording which one it was is what lets the screen say the
                    // useful sentence instead of the generic one.
                    if (viaFind == 0 && result.FirstError.Length == 0)
                        result.SelectionEmpty = true;
                }
                else
                {
                    // O28 — "todas as cores" NUNCA percorre via FindShapes. Medido num arquivo real:
                    // FindShapes(Recursive:=True) devolveu 3014 formas (não zero — a antiga rota de
                    // fallback abaixo só disparava com ZERO, então nunca disparou aqui) e mesmo assim
                    // nunca visitou a forma com a cor que a auditoria (Document.Palette, leitura nativa
                    // do Corel) via sem problema nenhum. A cor estava dentro de um PowerClip, que
                    // FindShapes não é garantido descer (regra já documentada no CLAUDE.md). O25 diz
                    // que uma correção de travessia vale para TODOS os caminhos — então "todas as
                    // cores" usa sempre o mesmo passeio manual que já é comprovadamente completo
                    // (grupos + PowerClip), e o FindShapes vira só uma sonda para o log poder PROVAR a
                    // divergência entre "quantas formas o Corel diz que há" e "quantas foram
                    // realmente visitadas" — sem isso, um segundo relatório insuficiente exigiria mais
                    // uma rodada de DevTools em vez de já sair diagnosticável do próximo docker.log.
                    result.UsedFallbackWalk = true;
                    try
                    {
                        object? probe = doc.ActivePage.FindShapes(Type.Missing, Type.Missing, true);
                        if (probe != null)
                        {
                            try { result.ShapesViaFindShapes = (int)((dynamic)probe).Count; } catch { }
                            Release(probe);
                        }
                    }
                    catch (Exception ex) { Note(result, ex); }

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

            Release(targetColor);
            log?.Invoke($"Substituição: {result.ShapesInspected} forma(s) inspecionada(s), " +
                $"{result.FillsChanged} preenchimento(s) e {result.OutlinesChanged} contorno(s) trocados" +
                (result.NotReplaceable > 0 ? $", {result.NotReplaceable} não substituível(is) neste modo." : "."));
            return result;
        }

        /// <summary>Runs <paramref name="visit"/> over a range and returns how many shapes it saw.</summary>
        private static int RunOverRange(object? range, Action<object> visit, ColorReplaceResult result)
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

        /// <summary>Hand-rolled traversal: groups AND PowerClips, the art a client file is full of.</summary>
        private static void WalkShapes(object shapes, Action<object> visit, ColorReplaceResult result, int depth)
        {
            if (depth > 32) return;

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

                    object? pc = null;
                    try { pc = ((dynamic)shape).PowerClip; } catch { }
                    if (pc != null)
                    {
                        object? pcShapes = null;
                        try { pcShapes = ((dynamic)pc).Shapes; } catch { }
                        if (pcShapes != null)
                        {
                            // Envolve o visit para contar tudo que só existe porque abrimos este
                            // PowerClip — inclusive filhos de grupos dentro dele, já que a recursão
                            // de WalkShapes propaga este MESMO delegate para dentro.
                            void VisitInPowerClip(object s) { result.ShapesViaPowerClip++; visit(s); }
                            WalkShapes(pcShapes, VisitInPowerClip, result, depth + 1);
                            Release(pcShapes);
                        }
                        Release(pc);
                    }
                }
                catch (Exception ex) { Note(result, ex); }
                finally { Release(shape); }
            }
        }

        /// <summary>Keeps the FIRST real error — later ones are usually the same cause repeated.</summary>
        private static void Note(ColorReplaceResult result, Exception ex)
        {
            if (result.FirstError.Length == 0)
                result.FirstError = ex.GetType().Name + ": " + ex.Message;
        }

        private static void ReplaceOnShape(object shape, string sourceKey, object targetColor,
            bool fillOnly, ColorReplaceResult result)
        {
            // Mesmo motivo do conversor: escrever o preenchimento de um GRUPO aplica a cor a todos os
            // filhos. Aqui o estrago é menos provável (a chave de origem raramente casa com o
            // agregado de um grupo), mas "menos provável" não é uma garantia quando o custo é
            // repintar o desenho do cliente.
            try { if ((int)((dynamic)shape).Type == CorelConstants.CdrGroupShape) { result.GroupsSkipped++; return; } }
            catch { }

            ReplaceFill(shape, sourceKey, targetColor, result);
            if (!fillOnly) ReplaceOutline(shape, sourceKey, targetColor, result);
        }

        private static void ReplaceFill(object shape, string sourceKey, object targetColor, ColorReplaceResult result)
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
                    case CorelConstants.CdrUniformFill:
                        string key = KeyOf(((dynamic)fill).UniformColor);
                        result.NoteKey(key);
                        if (key == sourceKey)
                        {
                            // Mesmo defeito do conversor: ApplyUniformFill esta em IVGFill e
                            // IVGShapeRange, nao em IVGShape. Aqui a chamada errada nunca deu erro
                            // visivel porque o catch a engolia - a substituicao de cor simplesmente
                            // nao trocava preenchimento nenhum, calada, desde que foi escrita.
                            ((dynamic)fill).UniformColor = targetColor;
                            result.FillsChanged++;
                        }
                        return;

                    case CorelConstants.CdrFountainFill:
                    case CorelConstants.CdrPatternFill:
                        if (FillMentions(fill, fillType, sourceKey)) result.NotReplaceable++;
                        return;

                    default:
                        return;
                }
            }
            catch { }
            finally { Release(fill); }
        }

        private static void ReplaceOutline(object shape, string sourceKey, object targetColor, ColorReplaceResult result)
        {
            object? outline = null;
            try
            {
                outline = ((dynamic)shape).Outline;
                if (outline == null) return;

                try { if ((int)((dynamic)outline).Type == 0) return; } catch { }

                string key = KeyOf(((dynamic)outline).Color);
                result.NoteKey(key);
                if (key == sourceKey)
                {
                    ((dynamic)outline).Color = targetColor;
                    result.OutlinesChanged++;
                }
            }
            catch { }
            finally { Release(outline); }
        }

        /// <summary>True when a fountain/pattern fill has a stop matching the source colour — read-only
        /// check, this pass does not mutate gradients or patterns.</summary>
        private static bool FillMentions(object fill, int fillType, string sourceKey)
        {
            if (fillType == CorelConstants.CdrFountainFill)
            {
                object? fountain = null, colors = null;
                try
                {
                    fountain = ((dynamic)fill).Fountain;
                    colors = fountain == null ? null : ((dynamic)fountain).Colors;
                    if (colors == null) return false;
                    int n = (int)((dynamic)colors).Count;
                    for (int i = 1; i <= n; i++)
                    {
                        object? stop = null;
                        try { stop = ((dynamic)colors)[i]; if (KeyOf(((dynamic)stop).Color) == sourceKey) return true; }
                        catch { }
                        finally { Release(stop); }
                    }
                    return false;
                }
                catch { return false; }
                finally { Release(colors); Release(fountain); }
            }

            if (fillType == CorelConstants.CdrPatternFill)
            {
                object? pattern = null;
                try
                {
                    pattern = ((dynamic)fill).Pattern;
                    if (pattern == null) return false;
                    if ((int)((dynamic)pattern).Type != 0) return false; // full-colour/bitmap: no readable colour
                    return KeyOf(((dynamic)pattern).FrontColor) == sourceKey
                        || KeyOf(((dynamic)pattern).BackColor) == sourceKey;
                }
                catch { return false; }
                finally { Release(pattern); }
            }

            return false;
        }

        /// <summary>Same identity ColorCollector uses, computed on a COPY so nothing here is mutated
        /// by the act of reading it.</summary>
        private static string KeyOf(object? liveColor) => ColorIdentity.KeyOf(liveColor);

        private static object? CreateTargetColor(dynamic app, ColorReplaceRequest request)
        {
            try
            {
                if (request.TargetModel == ColorModel.Cmyk && request.TargetCmyk?.Length == 4)
                {
                    int[] c = request.TargetCmyk;
                    return app.CreateCMYKColor(c[0], c[1], c[2], c[3]);
                }

                int[] rgb = request.TargetRgb?.Length == 3 ? request.TargetRgb : new[] { 0, 0, 0 };
                return app.CreateRGBColor(rgb[0], rgb[1], rgb[2]);
            }
            catch { return null; }
        }

        private static object? FindScope(dynamic app, dynamic doc, bool selectionOnly,
            ColorReplaceResult result)
        {
            try
            {
                if (selectionOnly) return app.ActiveSelectionRange;
                return doc.ActivePage.FindShapes(Type.Missing, Type.Missing, true);
            }
            catch (Exception ex) { Note(result, ex); return null; }
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
