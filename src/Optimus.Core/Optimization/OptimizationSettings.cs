using System.Collections.Generic;

namespace Optimus.Core.Optimization
{
    /// <summary>
    /// Colour-fidelity slider. Shown to the operator as <b>FIDELIDADE DE COR</b> — never as "ICC",
    /// which means nothing to a CorelDRAW operator.
    /// </summary>
    public enum ColorFidelity
    {
        /// <summary>Keep everything the document embeds. No saving, no risk.</summary>
        Maximum,

        /// <summary>Keep the profile for spaces the document actually uses (default).</summary>
        High,

        /// <summary>Replace a large press profile with a small standard one.</summary>
        Economical,

        /// <summary>Embed no profile at all. Biggest saving; printed colour may shift.</summary>
        NoProfile,
    }

    /// <summary>Image slider — shown as <b>QUALIDADE DE IMAGEM</b>, by destination, not by DPI number.</summary>
    public enum ImageQuality
    {
        /// <summary>Leave every image exactly as it is (default — no loss).</summary>
        Untouched,

        /// <summary>Fine print: 300 DPI effective, the print standard.</summary>
        FinePrint,

        /// <summary>Large format, viewed from a distance: 200 DPI.</summary>
        LargeFormat,

        /// <summary>Screen only: 150 DPI.</summary>
        Screen,
    }

    /// <summary>Geometry slider — shown as <b>PESO DO DESENHO</b>. Drives fluidity, not file size (O8).</summary>
    public enum DrawingWeight
    {
        /// <summary>
        /// Do not touch the curves at all. Exists because "Seguro" must mean literally no change to
        /// the artwork — a test caught that the safe preset was still simplifying geometry, however
        /// slightly, which would have made the promise false.
        /// </summary>
        Untouched,

        PreserveDetail,
        Balanced,
        MaxFluidity,
    }

    /// <summary>Colour-space conversion. The most dangerous control: it CHANGES the colours.</summary>
    public enum ColorSpaceTarget { Keep, Cmyk, Rgb }

    public enum OptimizationPreset { Safe, Balanced, MaxSavings }

    /// <summary>
    /// Everything the operator chose. The product is a REMAP: it applies the trade-off the operator
    /// selected and states the cost of each one, rather than deciding on their behalf
    /// (product philosophy, Davi 2026-07-25).
    ///
    /// <para>
    /// Pure settings object — no COM, no I/O — so every mapping (slider → tolerance in millimetres,
    /// slider → DPI target, which positions are lossy) is testable without CorelDRAW.
    /// </para>
    /// </summary>
    public sealed class OptimizationSettings
    {
        // ── the four sliders ────────────────────────────────────────────────────────
        public ColorFidelity Color { get; set; } = ColorFidelity.High;
        public ImageQuality Image { get; set; } = ImageQuality.Untouched;
        public DrawingWeight Weight { get; set; } = DrawingWeight.Balanced;
        public ColorSpaceTarget Space { get; set; } = ColorSpaceTarget.Keep;

        // ── lossless switches, ON by default: there is no reason to leave free gains ──
        /// <summary>Drop the embedded preview. Free, and it is STORED (incompressible) in real files.</summary>
        public bool RemovePreview { get; set; } = true;

        public bool RemoveEmptyLayers { get; set; } = true;

        /// <summary>Drop legacy CMX compatibility payload written for very old CorelDRAW versions.</summary>
        public bool RemoveCompatibilityData { get; set; } = true;

        /// <summary>Close and reopen after saving so the file is reserialized clean (drops retained
        /// undo state and orphaned payload).</summary>
        public bool Reserialize { get; set; } = true;

        /// <summary>OFF by default: "outside the page" may be the operator's parked notes.</summary>
        public bool RemoveOffPage { get; set; } = false;

        /// <summary>
        /// Flatten live effects (blend, contour, extrude, shadow…). OFF by default because it keeps the
        /// APPEARANCE but destroys the EDITABILITY — the operator can no longer change the contour
        /// distance or the shadow blur. It is often the largest fluidity win, since Corel regenerates
        /// these on every redraw.
        /// </summary>
        public bool FlattenEffects { get; set; } = false;

        /// <summary>Measure the real redraw time before and after, so fluidity is proven, not claimed.</summary>
        public bool MeasureRedraw { get; set; } = true;

        /// <summary>
        /// Replace repeated art with instances of one symbol — the lossless lever measured at 58,2% of a
        /// real file (O15).
        ///
        /// <para>
        /// OFF by default, and it stays off by default: symbols change how the drawing is EDITED. An
        /// instance cannot be reshaped on its own without breaking away from its definition, the same
        /// bargain as a block in AutoCAD. For repeated shop art that is usually exactly what is wanted,
        /// but it is a trade the operator accepts explicitly — the same rule as the colour conversion.
        /// </para>
        /// </summary>
        public bool InstanceRepeatedArt { get; set; } = false;

        // ── derived, technical values the interop layer consumes ─────────────────────

        /// <summary>Maps to <c>StructSaveAsOptions.EmbedICCProfile</c>.</summary>
        public bool EmbedColorProfile => Color != ColorFidelity.NoProfile;

        /// <summary>Target EFFECTIVE DPI, or null to leave images untouched.</summary>
        public int? TargetDpi
        {
            get
            {
                switch (Image)
                {
                    case ImageQuality.FinePrint: return 300;
                    case ImageQuality.LargeFormat: return 200;
                    case ImageQuality.Screen: return 150;
                    default: return null;
                }
            }
        }

        /// <summary>
        /// Node-reduction tolerance in MILLIMETRES. The unit is part of the contract: v1.0 applied
        /// this value to a document set to centimetres, making every level 10x more aggressive than
        /// its label.
        /// </summary>
        public double? ToleranceMm
        {
            get
            {
                switch (Weight)
                {
                    case DrawingWeight.Untouched: return null;
                    case DrawingWeight.PreserveDetail: return 0.01;
                    case DrawingWeight.MaxFluidity: return 0.08;
                    default: return 0.03;
                }
            }
        }

        /// <summary>True when the curves will be modified at all.</summary>
        public bool SimplifyDrawing => ToleranceMm.HasValue;

        /// <summary>True when a choice alters the artwork irreversibly.</summary>
        public bool HasDeclaredLoss => LossWarnings.Count > 0;

        /// <summary>True when a choice is dangerous enough to demand an explicit yes.</summary>
        public bool RequiresConfirmation => Space != ColorSpaceTarget.Keep;

        /// <summary>
        /// Plain-language consequences of the current selection, in pt-BR. These belong in the LABEL,
        /// not in a footnote — the operator has to be able to see what they are trading away.
        /// </summary>
        public List<string> LossWarnings
        {
            get
            {
                var w = new List<string>();

                if (Color == ColorFidelity.NoProfile)
                    w.Add("Sem perfil de cor embutido: a cor impressa pode sair diferente da que você vê.");
                else if (Color == ColorFidelity.Economical)
                    w.Add("Perfil de cor substituído por um padrão menor: pequenas diferenças de cor são possíveis.");

                switch (Image)
                {
                    case ImageQuality.FinePrint:
                        w.Add("Imagens acima de 300 DPI serão reduzidas para 300 (invisível no papel, irreversível).");
                        break;
                    case ImageQuality.LargeFormat:
                        w.Add("Imagens serão reduzidas para 200 DPI: bom para grande formato, não para impressão de perto.");
                        break;
                    case ImageQuality.Screen:
                        w.Add("Imagens serão reduzidas para 150 DPI: adequado só para tela, NÃO para impressão.");
                        break;
                }

                if (Space == ColorSpaceTarget.Cmyk)
                    w.Add("Conversão para CMYK: as cores mudam de verdade. Confirme antes de aplicar.");
                else if (Space == ColorSpaceTarget.Rgb)
                    w.Add("Conversão para RGB: as cores mudam de verdade. Confirme antes de aplicar.");

                if (RemoveOffPage)
                    w.Add("Objetos fora da página serão apagados — inclusive anotações que você deixou de lado.");

                if (FlattenEffects)
                    w.Add("Efeitos (contorno, sombra, mistura) viram formas fixas: a aparência é mantida, "
                        + "mas você não poderá mais editar o efeito.");

                if (InstanceRepeatedArt)
                    w.Add("Cópias repetidas viram instâncias vinculadas: a arte fica idêntica, mas "
                        + "editar uma cópia isolada exige desvinculá-la antes.");

                return w;
            }
        }

        /// <summary>
        /// The same consequences as <see cref="LossWarnings"/>, expressed as translation KEYS.
        ///
        /// <para>
        /// The UI sends these to the page and resolves them there, so the warnings are shown in the
        /// operator's language. <see cref="LossWarnings"/> is kept — it is the pt-BR fallback and what the
        /// tests assert on, and it guarantees the two lists can never drift apart silently: a test walks
        /// every combination and checks they have the same length.
        /// </para>
        /// </summary>
        public List<string> LossWarningKeys
        {
            get
            {
                var w = new List<string>();

                if (Color == ColorFidelity.NoProfile) w.Add("opt.warn.noprofile");
                else if (Color == ColorFidelity.Economical) w.Add("opt.warn.smallprofile");

                switch (Image)
                {
                    case ImageQuality.FinePrint: w.Add("opt.warn.dpi300"); break;
                    case ImageQuality.LargeFormat: w.Add("opt.warn.dpi200"); break;
                    case ImageQuality.Screen: w.Add("opt.warn.dpi150"); break;
                }

                if (Space == ColorSpaceTarget.Cmyk) w.Add("opt.warn.cmyk");
                else if (Space == ColorSpaceTarget.Rgb) w.Add("opt.warn.rgb");

                if (RemoveOffPage) w.Add("opt.warn.offpage");
                if (FlattenEffects) w.Add("opt.warn.flatten");
                if (InstanceRepeatedArt) w.Add("opt.warn.instance");

                return w;
            }
        }

        public static OptimizationSettings Preset(OptimizationPreset preset)
        {
            switch (preset)
            {
                case OptimizationPreset.Safe:
                    // Only what costs the artwork nothing.
                    return new OptimizationSettings
                    {
                        Color = ColorFidelity.Maximum,
                        Image = ImageQuality.Untouched,
                        Weight = DrawingWeight.Untouched,
                        Space = ColorSpaceTarget.Keep,
                    };

                case OptimizationPreset.MaxSavings:
                    // Allowed to hurt — but every cost appears in LossWarnings, and colour space is
                    // STILL never converted without an explicit choice.
                    return new OptimizationSettings
                    {
                        Color = ColorFidelity.NoProfile,
                        Image = ImageQuality.FinePrint,
                        Weight = DrawingWeight.MaxFluidity,
                        Space = ColorSpaceTarget.Keep,
                        RemoveOffPage = false,
                    };

                default:
                    return new OptimizationSettings();
            }
        }
    }
}
