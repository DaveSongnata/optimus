using System.Collections.Generic;

namespace Optimus.Core.I18n
{
    /// <summary>
    /// pt-BR labels for the maintenance operations, keyed by operation id
    /// (<c>mnt.op.&lt;id&gt;.label|desc|gain</c>). Authoritative.
    ///
    /// <para>
    /// Keying by id is what makes the rules translatable <b>without</b> touching
    /// <see cref="Optimus.Core.Maintenance.CleanupRule"/>: the rule stays a pure data object carrying its
    /// pt-BR text as a fallback, and the UI looks the key up. A rule whose key is missing therefore still
    /// shows correct Portuguese instead of a blank row.
    /// </para>
    /// <para>
    /// Every <c>gain</c> is a RANGE, never a single number: the real figure is unknowable before the scan,
    /// and a precise-looking promise is the exact dishonesty this product refuses.
    /// </para>
    /// </summary>
    public static partial class LocalizedStrings
    {
        private static Dictionary<string, string> OpsPt() => new Dictionary<string, string>
        {
            ["mnt.op.diag.disk.label"] = "Espaço livre e saúde dos discos",
            ["mnt.op.diag.disk.desc"] = "Lê o estado de saúde (SMART) e o espaço livre de cada disco. Roda primeiro e não altera nada.",
            ["mnt.op.diag.disk.gain"] = "previne perda de arte",

            ["mnt.op.corel.savebackups.label"] = "Cópias de segurança do CorelDRAW",
            ["mnt.op.corel.savebackups.desc"] = "Arquivos \"Backup_of_*.cdr\" que o CorelDRAW cria a cada salvamento, ao lado da sua arte. Costumam somar gigabytes numa máquina antiga. Você verá a lista com caminho e tamanho antes de apagar.",
            ["mnt.op.corel.savebackups.gain"] = "gigabytes — o diferencial deste programa",

            ["mnt.op.corel.autorecovery.label"] = "Arquivos de recuperação do CorelDRAW",
            ["mnt.op.corel.autorecovery.desc"] = "Arquivos de recuperação automática antigos. O CorelDRAW os usa para restaurar o trabalho depois de um travamento; os antigos não servem mais.",
            ["mnt.op.corel.autorecovery.gain"] = "100 MB – 2 GB",

            ["mnt.op.diag.startup.label"] = "Auditoria de inicialização",
            ["mnt.op.diag.startup.desc"] = "Lista tudo que abre junto com o Windows, nas 5 origens possíveis. Você decide o que desativar — nada é desativado sozinho, e é reversível.",
            ["mnt.op.diag.startup.gain"] = "800 MB – 1,5 GB de RAM, permanente",

            ["mnt.op.windows.usertemp.label"] = "Arquivos temporários (por usuário)",
            ["mnt.op.windows.usertemp.desc"] = "A pasta de temporários de cada usuário real da máquina. Só remove o que está sem uso há mais de 3 dias, para não tocar em nada que o CorelDRAW ainda esteja usando.",
            ["mnt.op.windows.usertemp.gain"] = "500 MB – 15 GB",

            ["mnt.op.windows.machinetemp.label"] = "Arquivos temporários do Windows",
            ["mnt.op.windows.machinetemp.desc"] = "A pasta de temporários do próprio Windows (C:\\Windows\\Temp). Mesmo critério: nada modificado nos últimos 3 dias é tocado.",
            ["mnt.op.windows.machinetemp.gain"] = "100 MB – 5 GB",

            ["mnt.op.windows.updatecache.label"] = "Cache do Windows Update",
            ["mnt.op.windows.updatecache.desc"] = "Instaladores de atualizações já aplicadas. O Windows baixa de novo o que precisar. Costuma ocupar de 300 MB a 8 GB numa máquina antiga.",
            ["mnt.op.windows.updatecache.gain"] = "300 MB – 8 GB",

            ["mnt.op.windows.crashevidence.label"] = "Relatórios de travamento",
            ["mnt.op.windows.crashevidence.desc"] = "Despejos de memória e relatórios de erro antigos — um único arquivo pode ter o tamanho da memória RAM. Os 5 mais recentes são mantidos, porque são a única pista se a máquina voltar a travar.",
            ["mnt.op.windows.crashevidence.gain"] = "100 MB – 64 GB",

            ["mnt.op.windows.deliveryopt.label"] = "Cache de distribuição de atualizações",
            ["mnt.op.windows.deliveryopt.desc"] = "Arquivos que o Windows guarda para compartilhar atualizações com outros PCs da rede. São recriados sob demanda.",
            ["mnt.op.windows.deliveryopt.gain"] = "100 MB – 4 GB",

            ["mnt.op.windows.recyclebin.label"] = "Lixeira",
            ["mnt.op.windows.recyclebin.desc"] = "Mostra o tamanho e os 5 maiores itens ANTES de esvaziar. Depois de esvaziar não há como recuperar.",
            ["mnt.op.windows.recyclebin.gain"] = "0 – 50 GB",

            ["mnt.op.windows.spool.label"] = "Fila de impressão travada",
            ["mnt.op.windows.spool.desc"] = "Trabalhos de impressão presos que travam a fila inteira. Requer que o serviço de impressão seja parado por um instante.",
            ["mnt.op.windows.spool.gain"] = "destrava a fila de impressão",

            ["mnt.op.windows.thumbcache.label"] = "Cache de miniaturas",
            ["mnt.op.windows.thumbcache.desc"] = "Miniaturas do Explorer. Chega a vários GB em quem navega por pastas de imagens, e limpar resolve miniatura errada ou em branco. O Explorer reinicia por 1–2 segundos.",
            ["mnt.op.windows.thumbcache.gain"] = "100 MB – 5 GB; corrige miniatura errada",

            ["mnt.op.browser.cache.label"] = "Cache dos navegadores",
            ["mnt.op.browser.cache.desc"] = "Somente as pastas de cache de Chrome, Edge e Firefox. Senhas salvas, cookies, histórico e favoritos NÃO são tocados.",
            ["mnt.op.browser.cache.gain"] = "300 MB – 3 GB",

            ["mnt.op.design.scratch.label"] = "Arquivos temporários de programas de design",
            ["mnt.op.design.scratch.desc"] = "Rascunhos que CorelDRAW, Illustrator e InDesign deixam na pasta de temporários. Só remove o que está sem uso há mais de 3 dias.",
            ["mnt.op.design.scratch.gain"] = "50 MB – 2 GB",

            ["mnt.op.windows.cleanmgr.label"] = "Limpeza de Disco do Windows",
            ["mnt.op.windows.cleanmgr.desc"] = "Usa a própria ferramenta da Microsoft, com uma configuração segura: a pasta Downloads e a Lixeira ficam EXPLICITAMENTE de fora.",
            ["mnt.op.windows.cleanmgr.gain"] = "500 MB – 10 GB",

            ["mnt.op.windows.componentstore.label"] = "Limpeza do repositório de componentes (DISM)",
            ["mnt.op.windows.componentstore.desc"] = "Remove versões antigas de componentes do Windows. Demora, e depois atualizações antigas não podem mais ser desinstaladas.",
            ["mnt.op.windows.componentstore.gain"] = "1 – 8 GB",

            ["mnt.op.windows.optimize.label"] = "Otimizar o disco (ciente da mídia)",
            ["mnt.op.windows.optimize.desc"] = "Em HD mecânico desfragmenta; em SSD executa o TRIM. Nunca desfragmenta um SSD.",
            ["mnt.op.windows.optimize.gain"] = "desempenho (só em HD)",

            // ── aba Desempenho ──────────────────────────────────────────────────────
            ["mnt.tab.performance"] = "Desempenho",
            ["mnt.perf.title"] = "Ajustes de desempenho do Windows",
            ["mnt.perf.hint"] = "Coisas que o Windows troca por enfeite ou por economia de energia, e que "
                              + "numa estação de CorelDRAW só atrapalham. Cada ajuste mostra como está "
                              + "agora, o que você troca, e pode ser desfeito aqui mesmo.",
            ["mnt.btn.performance"] = "Ler os ajustes",
            ["mnt.perf.applyall"] = "Aplicar os marcados",
            ["mnt.perf.col.tweak"] = "Ajuste",
            ["mnt.perf.col.state"] = "Como está",
            ["mnt.perf.col.action"] = "Ação",
            ["mnt.perf.state.optimized"] = "já otimizado",
            ["mnt.perf.state.default"] = "pode melhorar",
            ["mnt.perf.state.na"] = "não se aplica",
            ["mnt.perf.state.unknown"] = "não foi possível ler",
            ["mnt.perf.current"] = "agora: {0}",
            ["mnt.perf.after"] = "fica: {0}",
            ["mnt.perf.tradeoff"] = "O que você troca: {0}",
            ["mnt.perf.explorer"] = "o Explorer reinicia por 1–2 segundos",
            ["mnt.perf.signout"] = "vale totalmente depois de sair e entrar na conta",
            ["mnt.perf.apply"] = "Aplicar",
            ["mnt.perf.revert"] = "Desfazer",
            ["mnt.perf.nofolders"] = "Escolha antes as pastas de arte, na aba Limpeza.",
            ["mnt.perf.rejected"] = "O que este programa NÃO faz, de propósito: desativar serviços do "
                                  + "Windows, mexer no arquivo de paginação e forçar prioridade de "
                                  + "processo. São os três clássicos de \"acelerador de PC\" e vão de "
                                  + "placebo a chamado de suporte semanas depois.",

            ["mnt.tweak.perf.powerplan.label"] = "Plano de energia de alto desempenho",
            ["mnt.tweak.perf.powerplan.desc"] = "No plano \"Equilibrado\" o Windows reduz a frequência do processador e \"estaciona\" núcleos para economizar energia. Num computador de mesa que fica ligado o dia inteiro isso só atrasa o CorelDRAW ao abrir e ao redesenhar arquivos pesados.",
            ["mnt.tweak.perf.powerplan.tradeoff"] = "Consome mais energia. Em notebook, reduz a duração da bateria.",

            ["mnt.tweak.perf.animations.label"] = "Desligar as animações da interface",
            ["mnt.tweak.perf.animations.desc"] = "O Windows anima cada janela que abre, minimiza ou aparece. Cada animação é tempo que você espera sem precisar. Desligar deixa a resposta imediata — é o ajuste que mais muda a sensação de máquina rápida.",
            ["mnt.tweak.perf.animations.tradeoff"] = "A interface fica \"seca\", sem os efeitos de abrir e fechar. O texto suavizado (ClearType) e as miniaturas continuam ligados: designer precisa dos dois.",

            ["mnt.tweak.perf.transparency.label"] = "Desligar os efeitos de transparência",
            ["mnt.tweak.perf.transparency.desc"] = "O vidro fosco do menu Iniciar e da barra de tarefas é recalculado pela placa de vídeo o tempo todo, inclusive enquanto você arrasta objetos no CorelDRAW.",
            ["mnt.tweak.perf.transparency.tradeoff"] = "Menu Iniciar e barra de tarefas ficam opacos.",

            ["mnt.tweak.perf.menudelay.label"] = "Menus abrem na hora",
            ["mnt.tweak.perf.menudelay.desc"] = "O Windows espera 400 milissegundos antes de abrir um submenu. Multiplicado por um dia de trabalho, é bastante espera à toa.",
            ["mnt.tweak.perf.menudelay.tradeoff"] = "Nada.",

            ["mnt.tweak.perf.defender.label"] = "Tirar as pastas de arte da varredura do antivírus",
            ["mnt.tweak.perf.defender.desc"] = "O Windows Defender varre cada arquivo aberto e salvo. Um .cdr de 500 MB é varrido a cada salvamento — e a gráfica salva o dia inteiro. Excluir as pastas de trabalho e o próprio CorelDRAW elimina essa espera.",
            ["mnt.tweak.perf.defender.tradeoff"] = "É uma troca de SEGURANÇA: arquivos dentro dessas pastas deixam de ser verificados. Só faça isso em pastas de arte da própria gráfica, nunca na pasta de downloads.",

            ["mnt.tweak.perf.searchindex.label"] = "Tirar as pastas de arte do índice de pesquisa",
            ["mnt.tweak.perf.searchindex.desc"] = "O indexador do Windows lê as pastas em segundo plano para acelerar a busca. Numa pasta com milhares de .cdr grandes, ele fica lendo disco sem que a busca por nome de arquivo melhore em nada.",
            ["mnt.tweak.perf.searchindex.tradeoff"] = "A busca do Windows dentro dessas pastas fica mais lenta (mas continua funcionando).",

            ["mnt.op.diag.memory.label"] = "Diagnóstico de memória e travamentos",
            ["mnt.op.diag.memory.desc"] = "Quanta memória está realmente comprometida, quais programas a consomem e quantas vezes esta máquina travou nos últimos 30 dias.",
            ["mnt.op.diag.memory.gain"] = "informação real",
        };
    }
}
