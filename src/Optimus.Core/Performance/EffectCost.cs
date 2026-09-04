using System.Collections.Generic;

namespace Optimus.Core.Performance
{
    /// <summary>
    /// The live effects CorelDRAW can apply, with their verified <c>cdrEffectType</c> value.
    /// Each one is REGENERATED on every redraw, which is why a handful of them can cost more than
    /// hundreds of thousands of nodes.
    /// </summary>
    public enum LiveEffectKind
    {
        Blend = 0,
        Extrude = 1,
        Envelope = 2,
        TextOnPath = 3,
        ControlPath = 4,
        DropShadow = 5,
        Contour = 6,
        Distortion = 7,
        Perspective = 8,
        Lens = 9,
        CustomEffect = 10,
        InnerShadow = 11,
        Unknown = 99,
    }

    /// <summary>One live effect found in the document.</summary>
    public sealed class EffectRecord
    {
        public LiveEffectKind Kind { get; set; } = LiveEffectKind.Unknown;

        /// <summary>Shapes the effect generates or spans, when known. 0 when unknown.</summary>
        public int GeneratedShapes { get; set; }
    }

    /// <summary>
    /// Ranks live effects by how much they cost a redraw, and translates them into the operator's
    /// language.
    ///
    /// <para>
    /// The weights are ORDINAL, not milliseconds: they order the effects so the UI can say "attack
    /// these three contours before touching 400.000 nodes". The actual millisecond truth comes from
    /// <c>RedrawTimer</c>, measured before and after.
    /// </para>
    /// <para>
    /// Ordering rationale: effects that must RE-RASTERIZE (drop shadow, inner shadow, lens) or
    /// generate many intermediate shapes on the fly (blend, contour, extrude) dominate; effects that
    /// only warp an existing path once (envelope, perspective, distortion) cost less; text-on-path is
    /// cheap.
    /// </para>
    /// </summary>
    public static class EffectCost
    {
        private static readonly Dictionary<LiveEffectKind, int> Weights = new Dictionary<LiveEffectKind, int>
        {
            { LiveEffectKind.DropShadow, 100 },     // re-rasterized every frame
            { LiveEffectKind.InnerShadow, 100 },
            { LiveEffectKind.Lens, 90 },            // re-composites what is behind it
            { LiveEffectKind.Blend, 80 },           // generates N intermediate shapes
            { LiveEffectKind.Contour, 75 },
            { LiveEffectKind.Extrude, 70 },
            { LiveEffectKind.CustomEffect, 60 },
            { LiveEffectKind.Distortion, 40 },
            { LiveEffectKind.Envelope, 30 },
            { LiveEffectKind.Perspective, 25 },
            { LiveEffectKind.ControlPath, 10 },
            { LiveEffectKind.TextOnPath, 10 },
            { LiveEffectKind.Unknown, 50 },
        };

        public static int Weight(LiveEffectKind kind) =>
            Weights.TryGetValue(kind, out int w) ? w : Weights[LiveEffectKind.Unknown];

        /// <summary>Total ordinal cost of a set of effects, including the shapes they generate.</summary>
        public static int TotalCost(IEnumerable<EffectRecord> effects)
        {
            if (effects == null) return 0;
            int total = 0;
            foreach (EffectRecord e in effects)
            {
                if (e == null) continue;
                // Generated shapes are real work on every frame, so they add on top of the base cost.
                total += Weight(e.Kind) + System.Math.Max(0, e.GeneratedShapes);
            }
            return total;
        }

        /// <summary>Name in the operator's language — "efeito de contorno", not "cdrContour".</summary>
        public static string Label(LiveEffectKind kind)
        {
            switch (kind)
            {
                case LiveEffectKind.Blend: return "Mistura (blend)";
                case LiveEffectKind.Extrude: return "Extrusão";
                case LiveEffectKind.Envelope: return "Envelope";
                case LiveEffectKind.TextOnPath: return "Texto em curva";
                case LiveEffectKind.ControlPath: return "Curva de controle";
                case LiveEffectKind.DropShadow: return "Sombra";
                case LiveEffectKind.Contour: return "Contorno";
                case LiveEffectKind.Distortion: return "Distorção";
                case LiveEffectKind.Perspective: return "Perspectiva";
                case LiveEffectKind.Lens: return "Lente";
                case LiveEffectKind.InnerShadow: return "Sombra interna";
                case LiveEffectKind.CustomEffect: return "Efeito personalizado";
                default: return "Efeito";
            }
        }

        /// <summary>
        /// What flattening this effect costs the operator — always stated, because flattening is
        /// irreversible for editability even though it looks identical.
        /// </summary>
        public static string FlattenCost(LiveEffectKind kind)
        {
            switch (kind)
            {
                case LiveEffectKind.Blend:
                    return "Vira formas soltas: você não poderá mais ajustar a quantidade de passos.";
                case LiveEffectKind.Contour:
                    return "Vira formas soltas: você não poderá mais ajustar a distância do contorno.";
                case LiveEffectKind.DropShadow:
                case LiveEffectKind.InnerShadow:
                    return "A sombra vira imagem: não dá mais para mudar desfoque, cor ou direção.";
                case LiveEffectKind.Extrude:
                    return "Vira formas soltas: a profundidade deixa de ser editável.";
                case LiveEffectKind.Lens:
                    return "A lente vira arte fixa: o efeito deixa de acompanhar o que está atrás.";
                default:
                    return "O efeito deixa de ser editável, mas a aparência é mantida.";
            }
        }
    }
}
