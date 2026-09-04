using System.Collections.Generic;

namespace Optimus.Core.Maintenance
{
    /// <summary>What a tweak currently is on this machine.</summary>
    public enum TweakState
    {
        /// <summary>Could not be read — never guess, and never offer to "fix" what was not measured.</summary>
        Unknown,

        /// <summary>Already the way Optimus would set it. Nothing to do, and the UI says so.</summary>
        Optimized,

        /// <summary>At the Windows default (or something else). This is the only state worth offering.</summary>
        Default,

        /// <summary>Does not apply to this machine (e.g. a laptop-only or GPU-only setting).</summary>
        NotApplicable,
    }

    /// <summary>
    /// One reversible performance adjustment, described the way the operator experiences it.
    ///
    /// <para>
    /// The bar for being in this list is deliberately high, and it is the same bar as decision O6: the
    /// change must be <b>measurable, reversible, and explainable in one sentence</b>. That rules out most
    /// of what "optimizer" tools ship — service disabling, pagefile surgery, priority boosting, RAM
    /// cleaning — which is folklore that ranges from placebo to actively harmful.
    /// </para>
    /// <para>
    /// What survives the bar is the set of things Windows itself trades AWAY for looks or for battery:
    /// animations, transparency, menu delay, CPU parking. Those are real costs the shop is paying for
    /// nothing, and giving them back is honest optimisation.
    /// </para>
    /// </summary>
    public sealed class PerformanceTweak
    {
        public string Id { get; set; } = "";

        /// <summary>What the operator reads.</summary>
        public string Label { get; set; } = "";

        /// <summary>What changes, in the operator's language.</summary>
        public string Description { get; set; } = "";

        /// <summary>What is given up. Empty when nothing is — and then it must genuinely be nothing.</summary>
        public string Tradeoff { get; set; } = "";

        public RiskLevel Risk { get; set; } = RiskLevel.Safe;

        public TweakState State { get; set; } = TweakState.Unknown;

        /// <summary>Reason this tweak does not apply, when <see cref="TweakState.NotApplicable"/>.</summary>
        public string NotApplicableBecause { get; set; } = "";

        /// <summary>The value found on the machine, shown so the operator can see what changed.</summary>
        public string CurrentValue { get; set; } = "";

        /// <summary>What it becomes once applied.</summary>
        public string OptimizedValue { get; set; } = "";

        /// <summary>True when Explorer must restart for the change to show (1–2 s of blank taskbar).</summary>
        public bool NeedsExplorerRestart { get; set; }

        /// <summary>True when the operator has to sign out or reboot for the full effect.</summary>
        public bool NeedsSignOut { get; set; }

        /// <summary>Offered by default. Only the genuinely free wins are.</summary>
        public bool DefaultOn { get; set; }

        /// <summary>True when the tweak is worth offering: readable, applicable and not already applied.</summary>
        public bool IsActionable => State == TweakState.Default;
    }

    /// <summary>
    /// The catalogue of performance adjustments, as pure data.
    ///
    /// <para>
    /// Ranked for a CorelDRAW workstation. The first three are what an operator actually feels while
    /// dragging objects; the rest are about not making the machine fight itself.
    /// </para>
    /// <para>
    /// <b>Explicitly rejected</b>, and staying rejected: disabling Windows services wholesale (breaks
    /// printing, updates and licensing in ways that surface weeks later), pagefile resizing (a classic way
    /// to turn a working machine into one that cannot open a big file), process-priority boosting
    /// (measurably does nothing for a foreground app that already has the CPU), and anything that turns
    /// off ClearType or thumbnails — a designer needs both.
    /// </para>
    /// </summary>
    public static class PerformanceTweaks
    {
        public const string PowerPlan = "perf.powerplan";
        public const string Animations = "perf.animations";
        public const string Transparency = "perf.transparency";
        public const string MenuDelay = "perf.menudelay";
        public const string DefenderExclusions = "perf.defender";
        public const string SearchIndex = "perf.searchindex";

        public static List<PerformanceTweak> All() => new List<PerformanceTweak>
        {
            new PerformanceTweak
            {
                Id = PowerPlan,
                Label = "Plano de energia de alto desempenho",
                Description = "No plano \"Equilibrado\" o Windows reduz a frequência do processador e "
                            + "\"estaciona\" núcleos para economizar energia. Num computador de mesa que "
                            + "fica ligado o dia inteiro isso só atrasa o CorelDRAW ao abrir e ao "
                            + "redesenhar arquivos pesados.",
                Tradeoff = "Consome mais energia. Em notebook, reduz a duração da bateria.",
                Risk = RiskLevel.Safe,
                DefaultOn = true,
                OptimizedValue = "Alto desempenho",
            },

            new PerformanceTweak
            {
                Id = Animations,
                Label = "Desligar as animações da interface",
                Description = "O Windows anima cada janela que abre, minimiza ou aparece. Cada animação "
                            + "é tempo que você espera sem precisar. Desligar deixa a resposta imediata — "
                            + "é o ajuste que mais muda a sensação de máquina rápida.",
                Tradeoff = "A interface fica \"seca\", sem os efeitos de abrir e fechar. "
                         + "O texto suavizado (ClearType) e as miniaturas continuam ligados: "
                         + "designer precisa dos dois.",
                Risk = RiskLevel.Safe,
                DefaultOn = true,
                NeedsExplorerRestart = true,
                OptimizedValue = "sem animações",
            },

            new PerformanceTweak
            {
                Id = Transparency,
                Label = "Desligar os efeitos de transparência",
                Description = "O vidro fosco do menu Iniciar e da barra de tarefas é recalculado pela "
                            + "placa de vídeo o tempo todo, inclusive enquanto você arrasta objetos no "
                            + "CorelDRAW.",
                Tradeoff = "Menu Iniciar e barra de tarefas ficam opacos.",
                Risk = RiskLevel.Safe,
                DefaultOn = true,
                OptimizedValue = "desligada",
            },

            new PerformanceTweak
            {
                Id = MenuDelay,
                Label = "Menus abrem na hora",
                Description = "O Windows espera 400 milissegundos antes de abrir um submenu. "
                            + "Multiplicado por um dia de trabalho, é bastante espera à toa.",
                Tradeoff = "",
                Risk = RiskLevel.Safe,
                DefaultOn = true,
                NeedsSignOut = true,
                OptimizedValue = "0 ms",
            },

            new PerformanceTweak
            {
                Id = DefenderExclusions,
                Label = "Tirar as pastas de arte da varredura do antivírus",
                Description = "O Windows Defender varre cada arquivo aberto e salvo. Um .cdr de 500 MB é "
                            + "varrido a cada salvamento — e a gráfica salva o dia inteiro. Excluir as "
                            + "pastas de trabalho e o próprio CorelDRAW elimina essa espera.",
                Tradeoff = "É uma troca de SEGURANÇA: arquivos dentro dessas pastas deixam de ser "
                         + "verificados. Só faça isso em pastas de arte da própria gráfica, nunca na "
                         + "pasta de downloads.",
                Risk = RiskLevel.NeedsConfirmation,
                DefaultOn = false,
                OptimizedValue = "pastas excluídas da varredura",
            },

            new PerformanceTweak
            {
                Id = SearchIndex,
                Label = "Tirar as pastas de arte do índice de pesquisa",
                Description = "O indexador do Windows lê as pastas em segundo plano para acelerar a "
                            + "busca. Numa pasta com milhares de .cdr grandes, ele fica lendo disco sem "
                            + "que a busca por nome de arquivo melhore em nada.",
                Tradeoff = "A busca do Windows dentro dessas pastas fica mais lenta "
                         + "(mas continua funcionando).",
                Risk = RiskLevel.NeedsConfirmation,
                DefaultOn = false,
                OptimizedValue = "pastas fora do índice",
            },
        };
    }
}
