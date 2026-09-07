using System;
using Optimus.Core.Audit;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>Why <see cref="SelectionColorReader.Read"/> did or did not return a colour — so the UI
    /// can say something honest ("selecione um objeto") instead of just doing nothing.</summary>
    public enum SelectionColorStatus
    {
        Ok,
        Empty,      // Nothing selected.
        Multiple,   // More than one COLOUR among the selected shapes (or too many to check cheaply).
        Group,      // A group's Fill is an AGGREGATE (cdrColorMixed) — not a colour, per O24.
        NoColor,    // Fill is none/fountain/texture/pattern/hatch — no single swatch to point at.
    }

    public sealed class SelectionColorResult
    {
        public SelectionColorStatus Status { get; set; }
        public ColorRecord? Color { get; set; }
    }

    /// <summary>
    /// Reads the fill colour of the current CorelDRAW selection, so the UI can point to it in the
    /// palette the moment the operator selects something — instead of making them hunt for the
    /// matching swatch by eye.
    ///
    /// <para>
    /// READ-ONLY, leaf-only (O24): a group is reported as <see cref="SelectionColorStatus.Group"/>
    /// rather than read, because <c>Shape.Fill</c> on a group is an aggregate and <c>Color.Type</c>
    /// answers <c>cdrColorMixed</c> — "mixed" is the absence of an answer, not a colour.
    /// </para>
    /// <para>
    /// Selecting several shapes of the SAME colour ("marquei todos os logos vermelhos") answers with
    /// that one colour — a common workflow the product already builds on (colorReplace's
    /// "somente selecionado" scope). A mixed selection, or one large enough that reading every shape
    /// on a 2.5 s poll would turn a cheap check into a bulk walk, answers <c>Multiple</c> instead of
    /// paying that cost.
    /// </para>
    /// </summary>
    public static class SelectionColorReader
    {
        /// <summary>Above this many selected shapes, resolving "do they all share one colour" costs
        /// more than a poll tick should — report <c>Multiple</c> without reading any of them.</summary>
        private const int MaxShapesToCompare = 25;

        public static SelectionColorResult Read(dynamic app)
        {
            object? shapes = null;
            try
            {
                shapes = app.ActiveSelection.Shapes;
                int count;
                try { count = (int)((dynamic)shapes).Count; } catch { count = 0; }

                if (count == 0) return new SelectionColorResult { Status = SelectionColorStatus.Empty };
                if (count > MaxShapesToCompare) return new SelectionColorResult { Status = SelectionColorStatus.Multiple };

                ColorRecord? agreed = null;
                for (int i = 1; i <= count; i++)
                {
                    object? shape = null;
                    SelectionColorResult one;
                    try
                    {
                        shape = ((dynamic)shapes)[i];
                        one = shape == null
                            ? new SelectionColorResult { Status = SelectionColorStatus.NoColor }
                            : ReadOneShape(shape);
                    }
                    finally { Release(shape); }

                    if (one.Status != SelectionColorStatus.Ok || one.Color == null)
                        return count == 1 ? one : new SelectionColorResult { Status = SelectionColorStatus.Multiple };

                    if (agreed == null) agreed = one.Color;
                    else if (agreed.Key != one.Color.Key)
                        return new SelectionColorResult { Status = SelectionColorStatus.Multiple };
                }

                return new SelectionColorResult { Status = SelectionColorStatus.Ok, Color = agreed };
            }
            catch (Exception)
            {
                return new SelectionColorResult { Status = SelectionColorStatus.NoColor };
            }
            finally { Release(shapes); }
        }

        /// <summary>Reads ONE shape's uniform-fill colour. Never touches a group (O24) and never
        /// inspects a live colour (GetCopy() first, same rule as every other reader in this project).</summary>
        private static SelectionColorResult ReadOneShape(object shape)
        {
            object? fill = null, liveColor = null, copy = null;
            try
            {
                int shapeType = -1;
                try { shapeType = (int)((dynamic)shape).Type; } catch { }
                if (shapeType == CorelConstants.CdrGroupShape)
                    return new SelectionColorResult { Status = SelectionColorStatus.Group };

                fill = ((dynamic)shape).Fill;
                if (fill == null) return new SelectionColorResult { Status = SelectionColorStatus.NoColor };

                int fillType;
                try { fillType = (int)((dynamic)fill).Type; }
                catch { return new SelectionColorResult { Status = SelectionColorStatus.NoColor }; }

                if (fillType != CorelConstants.CdrUniformFill)
                    return new SelectionColorResult { Status = SelectionColorStatus.NoColor };

                liveColor = ((dynamic)fill).UniformColor;
                if (liveColor == null) return new SelectionColorResult { Status = SelectionColorStatus.NoColor };

                try { copy = ((dynamic)liveColor).GetCopy(); } catch { copy = null; }
                if (copy == null) return new SelectionColorResult { Status = SelectionColorStatus.NoColor };

                var record = new ColorRecord { Usage = ColorUsage.Fill };
                ColorIdentity.Fill(copy, record);

                // RGB last — ConvertToRGB() mutates the copy, so anything read after it would lose
                // CMYK separation (O27).
                ColorCollector.ReadRgb(copy, record);

                return new SelectionColorResult { Status = SelectionColorStatus.Ok, Color = record };
            }
            catch (Exception)
            {
                return new SelectionColorResult { Status = SelectionColorStatus.NoColor };
            }
            finally
            {
                Release(copy);
                Release(liveColor);
                Release(fill);
            }
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
