using System.Collections.Generic;

namespace Optimus.Core.I18n
{
    /// <summary>
    /// pt-BR catalog for the maintenance app's chrome, verdicts and honest-reporting sentences
    /// (<c>mnt.*</c>). Authoritative.
    ///
    /// <para>
    /// The headline sentences live here rather than being composed in C# so that the ONE number that
    /// matters — the drive's real free-space delta — reads naturally in every language, and so the caveat
    /// about per-category estimates can never be dropped in translation.
    /// </para>
    /// </summary>
    public static partial class LocalizedStrings
    {
        private static Dictionary<string, string> MaintenancePt() => new Dictionary<string, string>
        {
            // ── chrome ──────────────────────────────────────────────────────────────
            ["mnt.title"] = "Optimus Manutenção",
            ["mnt.subtitle"] = "Manutenção do Windows para máquinas de CorelDRAW",
            ["mnt.pill.checking"] = "verificando…",
            ["mnt.pill.admin"] = "administrador",
            ["mnt.pill.noadmin"] = "SEM administrador",
            ["mnt.tab.cleanup"] = "Limpeza",
            ["mnt.tab.startup"] = "Inicialização",
            ["mnt.tab.memory"] = "Memória e travamentos",
            ["mnt.lang"] = "Idioma",

            // ── machine state ───────────────────────────────────────────────────────
            ["mnt.state.title"] = "Estado da máquina",
            ["mnt.state.hint"] = "Lido antes de qualquer coisa e sem alterar nada. Se um disco estiver com problema, a limpeza não roda — primeiro se salva a arte.",
            ["mnt.state.nodisks"] = "O Windows não informou os discos físicos desta máquina.",
            ["mnt.disk.hours"] = "{0} h ligado",
            ["mnt.disk.life"] = "vida restante estimada: {0}%",
            ["mnt.vol.free"] = "{0} livres",
            ["mnt.vol.of"] = "de {0} — {1}% livre",
            ["mnt.vol.low"] = "pouco espaço",
            ["mnt.blocked.title"] = "A limpeza foi bloqueada por segurança",
            ["mnt.blocked.reason"] = "O disco \"{0}\" está reportando problema ({1}). Nenhuma limpeza será executada: primeiro copie a arte para outro disco ou para a nuvem. Apagar arquivos num disco que está falhando pode inviabilizar a recuperação do que ainda dá para salvar.",

            // ── disk verdicts ───────────────────────────────────────────────────────
            ["mnt.health.healthy"] = "Saudável",
            ["mnt.health.warning"] = "ATENÇÃO — faça backup agora",
            ["mnt.health.unhealthy"] = "FALHANDO — faça backup imediatamente",
            ["mnt.health.unknown"] = "O disco não informa o estado de saúde",
            ["mnt.media.hdd"] = "HD mecânico",
            ["mnt.media.ssd"] = "SSD",
            ["mnt.media.scm"] = "memória persistente",
            ["mnt.media.unknown"] = "tipo não informado",
            ["mnt.optimize.hdd"] = "Este é um HD mecânico: o Windows vai desfragmentar, juntando os pedaços dos arquivos. É onde a desfragmentação realmente acelera a máquina.",
            ["mnt.optimize.ssd"] = "Este é um SSD: o Windows NÃO desfragmenta (isso só gastaria a vida do disco). Ele executa o TRIM, que avisa o disco quais blocos estão livres.",
            ["mnt.optimize.scm"] = "Este disco é memória persistente e não precisa de otimização.",
            ["mnt.optimize.unknown"] = "O tipo de mídia não foi informado pelo disco; a otimização do Windows decide sozinha o que fazer com segurança.",

            // ── operation list ──────────────────────────────────────────────────────
            ["mnt.ops.title"] = "O que fazer",
            ["mnt.ops.hint"] = "Nada é executado antes de você ver a lista de arquivos. As operações marcadas como “precisa da sua confirmação” ficam desmarcadas de propósito.",
            ["mnt.risk.safe"] = "Segura",
            ["mnt.risk.confirm"] = "Precisa da sua confirmação",
            ["mnt.risk.irreversible"] = "Irreversível",
            ["mnt.badge.readonly"] = "só leitura",
            ["mnt.badge.safe"] = "segura",
            ["mnt.badge.noundo"] = "sem desfazer",
            ["mnt.folders.summary"] = "Pastas onde procurar cópias do CorelDRAW (Backup_of_*.cdr)",
            ["mnt.folders.hint"] = "Por padrão o Optimus procura na Área de Trabalho, em Documentos e nos outros discos. Adicione a pasta onde a gráfica guarda a arte.",
            ["mnt.folders.add"] = "Adicionar pasta…",
            ["mnt.picker.title"] = "Escolha a pasta onde a arte fica guardada",
            ["mnt.btn.scan"] = "Ver o que dá para limpar",
            ["mnt.btn.rescan"] = "Reler o estado da máquina",

            // ── scan review ─────────────────────────────────────────────────────────
            ["mnt.scan.title"] = "Encontrado",
            ["mnt.scan.hint"] = "Confira antes de apagar. Isto é o que será removido — não há como desfazer a exclusão de arquivo, mesmo com ponto de restauração.",
            ["mnt.scan.nothing"] = "Nada a remover nas opções escolhidas. Isso é um bom sinal, não um erro.",
            ["mnt.scan.total"] = "{0} arquivo(s) · {1} (estimativa)",
            ["mnt.scan.item"] = "{0} — {1} arquivo(s), {2}",
            ["mnt.scan.kept72"] = "{0} recentes preservados (regra de 72 h)",
            ["mnt.scan.keptevidence"] = "{0} mantidos como evidência de travamento",
            ["mnt.scan.noaccess"] = "{0} pasta(s) sem acesso",
            ["mnt.scan.col.file"] = "Arquivo",
            ["mnt.scan.col.size"] = "Tamanho",
            ["mnt.scan.col.modified"] = "Modificado",
            ["mnt.scan.showing"] = "Mostrando os {0} maiores de {1}.",
            ["mnt.scan.empty"] = "Nada encontrado aqui.",
            ["mnt.chk.update"] = "Incluir limpeza de atualizações antigas (libera mais, impede desinstalá-las depois)",
            ["mnt.btn.run"] = "Executar agora",

            // ── result: the honest report ───────────────────────────────────────────
            ["mnt.result.title"] = "Resultado",
            ["mnt.result.col.category"] = "Categoria (estimativa)",
            ["mnt.result.col.size"] = "Tamanho",
            ["mnt.result.col.removed"] = "Removidos",
            ["mnt.result.col.kept"] = "Preservados",
            ["mnt.head.concurrent"] = "Espaço liberado: 0 — outro programa gravou dados durante a limpeza, então o disco não mostrou ganho.",
            ["mnt.head.nothing"] = "Espaço liberado: 0 — não havia nada a remover nas opções escolhidas.",
            ["mnt.head.about"] = "Espaço liberado: cerca de {0} (valor pequeno demais para medir com precisão).",
            ["mnt.head.exact"] = "Espaço liberado: {0}.",
            ["mnt.caveat.over"] = "Os valores por categoria são estimativas e somam mais que o ganho real do disco (arquivos compartilhados e arredondamento de blocos) — o número que vale é o de cima.",
            ["mnt.caveat.plain"] = "Os valores por categoria são estimativas; o número que vale é o ganho real do disco, acima.",

            // ── restore point ───────────────────────────────────────────────────────
            ["mnt.restore.created"] = "Ponto de restauração criado.",
            ["mnt.restore.unprotected"] = "ATENÇÃO: a máquina seguirá SEM ponto de restauração novo. A exclusão de arquivos não é revertida por ponto de restauração de qualquer forma.",

            // ── startup audit ───────────────────────────────────────────────────────
            ["mnt.startup.title"] = "O que abre junto com o Windows",
            ["mnt.startup.hint"] = "Desativar aqui é reversível — o Optimus grava a mesma marcação que o Gerenciador de Tarefas do Windows, e nada é desativado automaticamente. Itens essenciais (antivírus, áudio, mesa digitalizadora, CorelDRAW) aparecem protegidos.",
            ["mnt.btn.startup"] = "Ler a inicialização",
            ["mnt.startup.col.program"] = "Programa",
            ["mnt.startup.col.origin"] = "Origem",
            ["mnt.startup.col.state"] = "Estado",
            ["mnt.startup.col.action"] = "Ação",
            ["mnt.startup.protected"] = "protegido",
            ["mnt.startup.active"] = "ativo",
            ["mnt.startup.disabled"] = "desativado",
            ["mnt.startup.disable"] = "Desativar",
            ["mnt.startup.enable"] = "Reativar",
            ["mnt.startup.missing"] = "arquivo não existe",
            ["mnt.startup.none"] = "Nada foi encontrado na inicialização.",
            ["mnt.surface.userrun"] = "Registro (este usuário)",
            ["mnt.surface.machinerun"] = "Registro (todos os usuários)",
            ["mnt.surface.machinerun32"] = "Registro (programas 32 bits)",
            ["mnt.surface.folder"] = "Pasta Inicializar",
            ["mnt.surface.task"] = "Agendador de Tarefas",

            // ── memory screen (where a "clean RAM" button would be) ─────────────────
            ["mnt.mem.title"] = "Memória",
            ["mnt.mem.installed"] = "Memória instalada",
            ["mnt.mem.available"] = "Disponível agora",
            ["mnt.mem.inuse"] = "{0}% em uso",
            ["mnt.mem.committed"] = "Memória comprometida",
            ["mnt.mem.pressure"] = "pressão real de memória",
            ["mnt.mem.nopressure"] = "sem pressão",
            ["mnt.mem.crashes"] = "Travamentos (30 dias)",
            ["mnt.mem.crashes.hint"] = "contados pelos relatórios do Windows",
            ["mnt.mem.gotostartup"] = "Ir para a auditoria de inicialização",
            ["mnt.mem.col.program"] = "Programa",
            ["mnt.mem.col.memory"] = "Memória em uso",
            ["mnt.mem.whyram"] = "Não existe botão de \"limpar RAM\" aqui de propósito.\n\nMemória livre não é memória economizada: o Windows usa a RAM sobrando para guardar o que você acabou de abrir. \"Liberar\" essa memória obriga o próximo acesso a voltar ao disco — o número na tela melhora e o computador fica MAIS LENTO.\n\nO que realmente devolve memória é deixar de carregar programas junto com o Windows. É isso que a auditoria de inicialização faz, e o efeito é permanente.",
            ["mnt.mem.whyprefetch"] = "Limpar a pasta Prefetch também não entra aqui. Ela é justamente o registro que faz os programas abrirem rápido; apagá-la deixa tudo mais lento até o Windows reconstruí-la, e ela ocupa poucos megabytes.",
            ["mnt.crashes.note"] = "Esta máquina registrou {0} travamento(s) nos últimos 30 dias.",

            // ── log and status bar ──────────────────────────────────────────────────
            ["mnt.log.title"] = "Registro do que foi feito",
            ["mnt.log.hint"] = "Tudo que é removido fica registrado em arquivo, para você poder conferir depois. É um registro técnico e sai sempre em português, mesmo com a interface em outro idioma.",
            ["mnt.btn.copy"] = "Copiar",
            ["mnt.btn.openlog"] = "Abrir o arquivo de registro",
            ["mnt.btn.clearscreen"] = "Limpar a tela",
            ["mnt.btn.refresh"] = "Atualizar",
            ["mnt.log.copied"] = "(registro copiado)",
            ["mnt.status.ready"] = "Pronto.",
            ["mnt.status.working"] = "Trabalhando…",
            ["mnt.busy.diagnose"] = "Lendo o estado da máquina…",
            ["mnt.busy.scan"] = "Procurando arquivos…",
            ["mnt.busy.exec"] = "Executando…",
            ["mnt.busy.startup"] = "Lendo a inicialização…",
            ["mnt.busy.perf"] = "Lendo os ajustes de desempenho…",
            ["mnt.err.pickone"] = "Marque ao menos uma operação.",
            ["mnt.err.scanfirst"] = "Nada varrido ainda — execute a varredura primeiro.",
            ["mnt.err.uibusy"] = "Já existe uma operação em andamento.",
            ["mnt.err.screen"] = "Erro na tela: {0}",
            ["mnt.err.generic"] = "Erro: {0}",
            ["mnt.webview.missing"] = "O componente WebView2 do Windows não pôde ser iniciado.\n\nInstale o \"Microsoft Edge WebView2 Runtime\" e abra o Optimus novamente.",
            ["mnt.already.open"] = "O Optimus Manutenção já está aberto.",
            ["mnt.crash.notice"] = "Ocorreu um erro inesperado. Nada foi alterado nesta etapa.\n\nDetalhes técnicos foram gravados em:\n{0}",
            ["mnt.start.failed"] = "O Optimus Manutenção não conseguiu iniciar.\n\n{0}",
        };
    }
}
