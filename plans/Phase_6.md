# Fase 6 — Módulo 1: app de manutenção do Windows

- **STATUS:** [x] Concluída (v1.8.0) · **DEPENDE DE:** Fase 1 (camadas/testes)
- **OBJETIVO (1 frase):** Um app de desktop **manual** que executa as operações de manutenção que
  comprovadamente ajudam uma máquina de CorelDRAW — e que se recusa a vender placebo.
- **DECISÕES:** O5 (app separado, **sem agendamento**), O6 (RAM e Prefetch **fora**).

## REQUIREMENTS (EARS)

- **R6.1 — WHEN** o app inicia, **THEN** ele SHALL verificar a saúde do disco e o espaço livre
  **antes** de oferecer qualquer operação destrutiva. _P4_
- **R6.2 — WHEN** o disco reporta estado não saudável, **THEN** o sistema SHALL bloquear as
  operações destrutivas e orientar backup. _P4_
- **R6.3 — WHEN** limpa arquivos de usuário rodando elevado, **THEN** o sistema SHALL enumerar os
  perfis reais via `ProfileList\<SID>\ProfileImagePath`, e não o perfil do administrador. _P1_
- **R6.4 — WHEN** remove temporários, **THEN** o sistema SHALL ignorar arquivos modificados nas
  últimas **72 horas**. _P4_
- **R6.5 — WHEN** encontra resíduo específico do CorelDRAW (`Backup_of_*.cdr`, recuperação,
  workspace, scratch), **THEN** o sistema SHALL listá-lo com caminho e tamanho e removê-lo somente
  com confirmação. _P3_
- **R6.6 — WHEN** exibe o resultado, **THEN** a manchete SHALL ser o ganho **real** de espaço livre
  do disco; os valores por categoria SHALL ser rotulados como estimativa e sua soma nunca SHALL ser
  apresentada como resultado. _P1_
- **R6.7 — WHEN** apresenta a auditoria de inicialização, **THEN** o sistema SHALL permitir
  **desabilitar** (reversível), nunca excluir, e SHALL nunca desabilitar automaticamente. _P4_
- **R6.8 — WHEN** usa `cleanmgr`, **THEN** o sistema SHALL escrever explicitamente `StateFlags = 0`
  para `DownloadsFolder` (e demais handlers proibidos) antes de executar. _P4_
- **R6.9 — WHEN** uma operação irreversível vai rodar, **THEN** o sistema SHALL tentar criar um
  ponto de restauração e **verificar** que foi realmente criado (número de sequência), informando o
  operador se não foi. _P1, P4_
- **R6.10 — WHEN** otimiza volume, **THEN** o sistema SHALL usar operação **ciente de mídia**
  (`defrag /O`), nunca `/D` em SSD. _P4_
- **R6.11 — WHEN** o operador procura "limpar RAM", **THEN** o sistema SHALL apresentar diagnóstico
  de memória e a auditoria de inicialização, explicando por que limpar RAM não ajuda. _O6, P2_

## DESIGN (curto)

App próprio (`Optimus.Maintenance`, net48 + WebView2, atalho na área de trabalho), instalado pelo
mesmo instalador. **Manual**: sem tarefa agendada, sem serviço, sem toast (O5) — o que elimina a
dependência do TaskScheduler, o problema de Sessão 0 e o atrito com antivírus.

**Operações, ranqueadas para o perfil CorelDRAW:**

| # | Operação | Ganho | Segurança |
|---|---|---|---|
| 1 | Espaço livre + saúde do disco (SMART) — **roda primeiro, só leitura** | previne perda de arte | SEGURA |
| 2 | **Resíduo do CorelDRAW** — `Backup_of_*.cdr`, `%APPDATA%\Corel\` (recuperação/workspace), scratch no `%TEMP%` | GBs; **diferencial do produto** | CONFIRMAR |
| 3 | Auditoria de inicialização (5 superfícies: Run HKCU/HKLM, WOW6432Node, pastas Startup, Task Scheduler) | 800 MB–1,5 GB de RAM **permanentes** + boot | CONFIRMAR |
| 4 | Temporários por perfil, com age-gate 72 h | 0,5–15 GB | SEGURA |
| 5 | Cache do Windows Update (`SoftwareDistribution\Download`, parando `wuauserv`) | 0,3–8 GB | SEGURA |
| 6 | Dumps de travamento + WER + LiveKernelReports (**preservando os 5 mais recentes**) | 0,1–64 GB | SEGURA |
| 7 | Lixeira (mostra tamanho + 5 maiores antes) | 0–50 GB | CONFIRMAR |
| 8 | Spool de impressão travado | destrava a fila | CONFIRMAR |
| 9 | Cache de miniaturas (reinicia Explorer, com aviso) | 0,1–5 GB + corrige miniatura errada | SEGURA |
| 10 | Cache de navegadores (só as pastas de cache; **nunca** cookies/logins/histórico) | 0,3–3 GB | CONFIRMAR |
| 11 | DISM `/StartComponentCleanup` (`/ResetBase` só opt-in, com aviso de perda de rollback) | 1–8 GB | CONFIRMAR |
| 12 | `defrag /O` ciente de mídia | desempenho (HD) | SEGURA |
| 13 | **Diagnóstico** de memória + "este PC travou N vezes nos últimos 30 dias" (conta minidumps) | informação real | SEGURA |

**Explicitamente fora (O6/P2):** limpar RAM (`EmptyWorkingSet`/standby purge) e limpar Prefetch —
ambos medidos como valor negativo. A UI explica o porquê em vez de esconder.

**Medição honesta (R6.6):** amostrar `DriveInfo.AvailableFreeSpace` antes/depois de cada fase, medir
o "ruído" do disco antes de começar (duas amostras com 10 s), e nunca exibir precisão maior que o
ruído. Delta global negativo ⇒ exibir 0 com nota, nunca número inventado.

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/Maintenance/CleanupRule.cs` — **novo**: regra pura (caminho, filtro, idade, risco). _R6.4, R6.5_
- `src/Optimus.Core/Maintenance/CorelResidueRules.cs` — **novo**: as regras específicas do Corel. _R6.5_
- `src/Optimus.Core/Maintenance/FreedSpaceReport.cs` — **novo**: manchete real × estimativas. _R6.6_
- `src/Optimus.Windows/UserProfiles.cs` — **novo**: enumera perfis reais. _R6.3_
- `src/Optimus.Windows/DiskHealth.cs` — **novo**: `MSFT_PhysicalDisk.HealthStatus`/`MediaType`. _R6.1, R6.2, R6.10_
- `src/Optimus.Windows/StartupAudit.cs` — **novo**: 5 superfícies, desabilita reversível. _R6.7_
- `src/Optimus.Windows/CleanMgrRunner.cs` — **novo**: `VolumeCaches` com handlers proibidos zerados. _R6.8_
- `src/Optimus.Windows/RestorePoint.cs` — **novo**: cria **e verifica**. _R6.9_
- `src/Optimus.Maintenance/` — **novo**: app WinForms + WebView2 (mesmo padrão do docker).
- `tests/Optimus.Core.Tests/Maintenance/` — regras puras testadas.

## READY-MADE SOLUTIONS TO USE

Tudo in-box, **zero dependência nova**: `cleanmgr` (`VolumeCaches` + `/sagerun:N`), DISM,
`System.Management`/WMI (`MSFT_PhysicalDisk`, `MSFT_StorageReliabilityCounter`),
`SHEmptyRecycleBin` (shell32), `defrag /O`, `ServiceController`, Restart Manager (`rstrtmgr.dll`)
para descobrir quem trava um arquivo, WMI `SystemRestore.CreateRestorePoint`.

**Não embarcar** (licença): BleachBit (GPLv3), Sysinternals (EULA proíbe redistribuição),
Winapp2.ini (CC BY-SA contamina o arquivo de regras). Regras próprias — localização de arquivo é
fato, não obra protegida.

## TASKS

1. [x] **Teste primeiro:** `CleanupRule` respeita age-gate (arquivo de 1 h não entra; de 5 dias entra). _R6.4_
2. [x] **Teste primeiro:** `CorelResidueRules` reconhece `Backup_of_*.cdr` e os caminhos do Corel,
   e **não** casa com um `.cdr` de trabalho normal. _R6.5_
3. [x] Implementar `CleanupRule` + `CorelResidueRules` (puros). _R6.4, R6.5_
4. [x] **Teste primeiro:** `FreedSpaceReport` — manchete = delta global; soma das categorias maior
   que o global **não** vira manchete; delta negativo vira 0 + nota. _R6.6_
5. [x] Implementar `FreedSpaceReport`. _R6.6_
6. [x] Implementar `UserProfiles` (enumeração real de perfis) + testes do parsing. _R6.3_
7. [x] Implementar `DiskHealth` (HealthStatus + MediaType + espaço livre); bloquear destrutivas em
   disco não saudável. _R6.1, R6.2, R6.10_
8. [x] Implementar `RestorePoint` com **verificação** por número de sequência e mensagem honesta
   quando não foi possível criar. _R6.9_
9. [x] Implementar `CleanMgrRunner` zerando `DownloadsFolder` e demais handlers proibidos. _R6.8_
10. [x] Implementar `StartupAudit` (5 superfícies, desabilitar reversível, nunca automático). _R6.7_
    A 5ª superfície (Agendador de Tarefas) ficou em `ScheduledTaskAudit.cs`, COM tardio via
    `Schedule.Service` — sem interop para embarcar.
11. [x] Implementar as operações 4–13 da tabela, cada uma com seu nível de confirmação. _R6.4–R6.12_
    `SystemResidueRules` (temp por perfil, temp da máquina, cache do WU, evidência de travamento,
    delivery optimization, cache de navegador), `RecycleBin`, `ServiceGuard`, `ExplorerHost`,
    `VolumeOptimizer` (defrag /O + DISM + sfc /verifyonly), `MemoryDiagnostics`.
12. [x] App `Optimus.Maintenance` (WebView2, paleta laranja) com: diagnóstico primeiro, seleção de
    operações, execução, relatório honesto. Atalho na área de trabalho. _R6.1, R6.6_
13. [x] Tela de memória que **explica** por que não existe botão de "limpar RAM" e leva para a
    auditoria de inicialização. _R6.11, P2_
14. [x] Remover do instalador o `WindowsOptimizer.cs` atual (Prefetch, sem age-gate, perfil errado);
    nenhum código órfão. _O6, R6.3, R6.4_ O flag `winopt` do wizard passou a significar
    "criar o atalho do app de manutenção"; a etapa 2 foi reescrita.
15. [ ] **VM/máquina real de gráfica:** rodar tudo e registrar em LESSONS quanto cada categoria
    liberou — em especial quanto o resíduo do CorelDRAW representou.
16. [x] **Aba Desempenho (v1.10.0)** — feedback do Davi ao ver a 1.9.0: *"o app ficou com cara de
    LIMPEZA de arquivos, não de otimização"*. Ele tem razão: apagar arquivo é uma das coisas que o
    módulo faz, não o que ele é. Entrou `PerformanceTweak`/`PerformanceTweaks` (Core, puro) +
    `PerformanceTuner` (Windows), com seis ajustes **reversíveis**: plano de energia, animações,
    transparência, atraso de menu, exclusão das pastas de arte da varredura do antivírus e do índice
    de pesquisa. A aba é a **primeira** da janela. _O6_
17. [x] **Correções da rodada na máquina do Davi (v1.10.0):** ícone do EXE de manutenção (estava
    vazio), fila de comandos no bridge (o boot pedia diagnóstico + memória juntos e o segundo era
    recusado — a tela de memória nunca carregava) e mensagem honesta quando o ponto de restauração
    falha sem motivo.

## DO NOT WANT

- Nada de limpar RAM nem Prefetch (O6).
- Nada de tarefa agendada, serviço ou toast (O5).
- Não excluir entrada de inicialização (só desabilitar, reversível).
- Não apagar `Windows.old` com menos de 15 dias, nem por padrão.
- Não limpar log de `Security`, nem cookies/logins/histórico de navegador.
- Não embarcar BleachBit/Sysinternals/Winapp2.
- Não exibir número maior que o ganho real do disco.

## VALIDATION

- `dotnet test` verde nas regras puras (age-gate, Corel, relatório).
- Numa VM real: rodar tudo e conferir que a manchete bate com o que o Explorer mostra de espaço livre.
- Verificar que `%TEMP%` do **usuário logado** foi limpo (não o do admin) — teste do R6.3.
- Conferir que `DownloadsFolder` continua intacta após `cleanmgr` (teste do R6.8).
- Confirmar que arte aberta no Corel não é afetada pela limpeza de temporários (age-gate).

## VERIFICATION GATE

- Adversarial: máquina "limpa" ⇒ relatório honesto de ~0, sem número inflado.
- A pasta Downloads tem de sobreviver — se sumir, a fase falhou (é o pior defeito possível aqui).
- Ponto de restauração: se não foi criado, o app tem de **dizer** isso, não fingir.
- Entrada de inicialização desabilitada tem de ser reversível pela própria UI.

## TECHNICAL CONSTRAINTS

`Optimus.Core` sem Win32 (as regras são puras; a execução mora em `Optimus.Windows`). Elevação só
onde necessário. `app.config` com switches de caminho longo. Assinar o binário (antivírus vê
exclusão em massa como comportamento suspeito). Arquivos < 500 linhas.

## LESSONS

Medido na máquina de desenvolvimento (Windows 11 Pro, NVMe 1 TB, 15,69 GB de RAM), carregando
`Optimus.Windows.dll` direto no PowerShell — ou seja, **dados reais**, não hipótese:

- **A 5ª superfície não é detalhe: é ~27% do total.** As quatro superfícies clássicas (Run HKCU,
  Run HKLM, WOW6432Node, pastas Startup) somaram **11 itens**; o Agendador de Tarefas somou
  **4 itens invisíveis nelas** — MSI Afterburner, atualizador do Edge, Thermal Control Center e uma
  tarefa de driver de áudio. Um "gerenciador de inicialização" que só lê as chaves Run mostra a
  máquina como mais limpa do que ela é, porque atualizadores modernos migraram justamente para cá.
- **A lista de itens protegidos teve de ser ampliada por evidência, não por teoria.** A tarefa real
  chamava-se `iGoAudioTaskSession` e não casava com `realtek` nem `rtkaudio`. O critério virou o
  substring `audio`: proteger demais custa uma otimização perdida, proteger de menos custa uma
  gráfica sem som. Assimetria decidida a favor do cliente.
- **`DownloadsFolder` está registrado nesta máquina** e o `Preview` confirmou que ele — mais Lixeira,
  Windows.old e arquivos ESD — sai como `StateFlags = 0` explícito. Escrever o zero é o mecanismo de
  segurança; "não marcar" não seria suficiente, porque `/sagerun` obedece ao valor que qualquer outra
  ferramenta tenha deixado na chave.
- **Espaço livre crítico é um sintoma real de "travando":** o disco C: desta máquina está com
  **6,8% livre** (24,5 GB de 361 GB), abaixo do piso de 10% em que o próprio Windows começa a sofrer
  com paginação, servicing e defrag. O painel marca isso em vermelho antes de oferecer qualquer
  limpeza.
- **A varredura de `Backup_of_*.cdr` encontrou 11,9 MB em 2 arquivos** já na pasta de testes do
  projeto (`Backup_of_arquivo2.cdr` 7,5 MB + `Backup_of_arquivo.cdr` 4,3 MB) e **não** casou com
  `arquivo.cdr`/`arquivo2.cdr` ao lado. Numa máquina de gráfica com anos de trabalho isso escala para
  gigabytes — é o diferencial do módulo, e nenhum limpador genérico sabe que esses arquivos existem.
- **`MSFT_PhysicalDisk` respondeu com MediaType e HealthStatus corretos** (SSD / Saudável) sem
  precisar do legado `MSStorageDriver_FailurePredictStatus`, que retorna "not supported" em NVMe.
- **Memória: 86% "em uso" e apenas 68% comprometida.** É exatamente por isso que o painel destaca o
  *commit* e não o "usado": o número que assusta o usuário não é o que prevê travamento.
- O relatório em GiB (base 1024) foi mantido de propósito: 6,84 × 10⁹ bytes aparecem como **6,37 GB**
  porque é o que o Explorer vai mostrar. Manchete e Explorer têm de bater.

### Rodada na máquina do Davi (v1.9.0 → v1.10.0)

O app rodou de verdade e liberou **55,7 MB** (110 temporários + cache do WU + 2 relatórios de
travamento), com a manchete batendo com o delta do disco: 14,04 GB → 14,09 GB livres. O que o teste
real ensinou:

- **"Uma operação por vez" era o desenho errado.** O boot pede diagnóstico e memória juntos, e o
  segundo comando era recusado com *"já existe uma operação em andamento"* — a tela de memória
  simplesmente nunca carregava. Virou **fila**, com descarte de duplicata: segurar um botão não pode
  agendar vinte varreduras.
- **O age-gate está fazendo o trabalho dele:** 337 arquivos temporários foram preservados por terem
  menos de 72 h, contra 110 removidos. Sem essa regra, o número "liberado" seria maior e um documento
  aberto poderia ter sido corrompido.
- **O ícone importa.** O EXE saiu sem `ApplicationIcon` e o atalho na área de trabalho — que é TODO o
  mecanismo de descoberta do módulo — ficou com o ícone genérico. Um atalho sem cara de produto lê
  como programa quebrado ou não confiável.
- **Erro de COM sem `Message` é pior que erro nenhum.** O ponto de restauração falhou e a linha saiu
  como `"Não foi possível criar o ponto de restauração: "` — dois-pontos e nada. Agora, quando a
  exceção vem vazia, o app nomeia a causa provável (Proteção do Sistema desligada) em vez de deixar o
  operador sem informação.
- **E o feedback que mudou o produto:** *"o app ficou com cara de LIMPEZA de arquivos, não de
  otimização"*. Daí a aba Desempenho, e ela vem primeiro.

### Por que estes seis ajustes, e não os outros

A régua é a mesma do O6: **mensurável, reversível e explicável numa frase**. Isso elimina quase tudo
que os "aceleradores de PC" vendem. O que sobra é o que o **próprio Windows** troca por enfeite ou por
bateria — animações, transparência, atraso de menu, estacionamento de núcleo — mais duas trocas que
só fazem sentido numa gráfica: tirar as pastas de arte da varredura do antivírus (um `.cdr` de 500 MB
é varrido a cada salvamento, e a gráfica salva o dia inteiro) e do indexador.

Ficaram **fora, de propósito**: desativar serviços do Windows, mexer no arquivo de paginação e forçar
prioridade de processo. E há uma armadilha específica: aplicar o preset "melhor desempenho" do próprio
Windows **também desliga ClearType e miniaturas** — para um designer isso é dano, não otimização. Por
isso a `UserPreferencesMask` é escrita bit a bit, limpando as animações e mantendo suavização de fonte
e conteúdo de janela ao arrastar.

## COVERAGE

R6.1 → 7,12 · R6.2 → 7 · R6.3 → 6,14 · R6.4 → 1,3,11,14 · R6.5 → 2,3,11 · R6.6 → 4,5,12 ·
R6.7 → 10 · R6.8 → 9 · R6.9 → 8 · R6.10 → 7,11 · R6.11 → 13
