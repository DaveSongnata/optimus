using System;

namespace Optimus.Interop
{
    /// <summary>CorelDRAW's view types, from the typelib enum <c>cdrViewType</c> (dump 1550).</summary>
    public enum CorelViewType
    {
        SimpleWireframe = 0,
        Wireframe = 1,
        Draft = 2,
        Normal = 3,
        Enhanced = 4,
        EnhancedWithOverprints = 5,
        Pixel = 6,
    }

    /// <summary>
    /// Reads and sets the drawing's view mode.
    ///
    /// <para>
    /// This is the fluidity lever that costs the customer <b>nothing</b> and works <b>instantly</b>, and it
    /// is the one every experienced CorelDRAW operator already uses on a heavy file. Corel's own
    /// documentation is explicit: Normal refreshes and opens faster than Enhanced (which renders
    /// PostScript fills, high-resolution bitmaps and anti-aliased vectors), and Simple Wireframe is
    /// faster still.
    /// </para>
    /// <para>
    /// It changes <b>nothing in the file</b> — no nodes, no objects, no saving. That is exactly why it
    /// belongs next to the destructive levers: on a drawing where simplifying 130.000 nodes did not
    /// produce a felt improvement, switching the view does, and it is reversible with one click.
    /// </para>
    /// </summary>
    public sealed class ViewModeSwitch
    {
        private readonly dynamic _app;

        public ViewModeSwitch(object application) => _app = application;

        /// <summary>The current view type, or null when there is no active view to ask.</summary>
        public CorelViewType? Current()
        {
            try { return (CorelViewType)(int)_app.ActiveDocument.ActiveView.Type; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Switches the active view. Returns false — and changes nothing — when the document has no
        /// active view, rather than pretending it worked.
        /// </summary>
        public bool Set(CorelViewType type)
        {
            try
            {
                _app.ActiveDocument.ActiveView.Type = (int)type;
                return Current() == type;
            }
            catch (Exception) { return false; }
        }

        /// <summary>What the operator gets, in their own terms. No jargon, consequence first.</summary>
        public static string Describe(CorelViewType type)
        {
            switch (type)
            {
                case CorelViewType.SimpleWireframe:
                    return "Só os contornos, sem preenchimento nenhum. É o modo mais rápido que existe — "
                         + "serve para posicionar e selecionar num arquivo pesado.";
                case CorelViewType.Wireframe:
                    return "Contornos, com as imagens em preto e branco. Bem rápido.";
                case CorelViewType.Draft:
                    return "Preenchimentos simplificados e imagens em baixa resolução. Rápido e já dá "
                         + "para reconhecer o desenho.";
                case CorelViewType.Normal:
                    return "Tudo aparece, menos os preenchimentos PostScript e a alta resolução das "
                         + "imagens. Redesenha mais rápido que o Aprimorado e é o melhor equilíbrio "
                         + "para trabalhar.";
                case CorelViewType.Enhanced:
                    return "Qualidade máxima: suavização, alta resolução e preenchimentos PostScript. "
                         + "É o mais bonito e o mais pesado — costuma ser o padrão.";
                case CorelViewType.EnhancedWithOverprints:
                    return "Como o Aprimorado, simulando as sobreimpressões. O mais pesado de todos.";
                default:
                    return "Simula os pixels da exportação.";
            }
        }

        /// <summary>Short label for the UI.</summary>
        public static string Label(CorelViewType type)
        {
            switch (type)
            {
                case CorelViewType.SimpleWireframe: return "Só contorno";
                case CorelViewType.Wireframe: return "Contorno";
                case CorelViewType.Draft: return "Rascunho";
                case CorelViewType.Normal: return "Normal";
                case CorelViewType.Enhanced: return "Aprimorado";
                case CorelViewType.EnhancedWithOverprints: return "Aprimorado + sobreimpressão";
                default: return "Pixels";
            }
        }
    }
}
