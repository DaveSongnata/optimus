# Fase 8 — i18n, empacotamento e validação final

- **STATUS:** [~] i18n, empacotamento e desinstalação prontos (v1.9.0); falta a validação final na VM
  (tarefas 10 e 11) · **DEPENDE DE:** Fases 1–7
- **OBJETIVO (1 frase):** Traduzir a interface para PT/ES/EN, empacotar plugin + app de manutenção
  num instalador único, e **provar nos arquivos reais do cliente** o que o produto entrega.

## REQUIREMENTS (EARS)

- **R8.1 — WHEN** o operador escolhe um idioma (PT/ES/EN), **THEN** toda a interface SHALL ser
  exibida nesse idioma e a escolha SHALL ser lembrada entre sessões. _RF i18n_
- **R8.2 — WHEN** nenhum idioma foi escolhido, **THEN** o padrão SHALL ser **pt-BR**. _D25_
- **R8.3 — WHEN** uma string é exibida ao usuário, **THEN** ela SHALL vir da camada de tradução, e
  nenhuma string SHALL estar codificada diretamente na UI. _P5_
- **R8.4 — WHEN** o instalador roda, **THEN** ele SHALL instalar o plugin no CorelDRAW **e** o app
  de manutenção com atalho na área de trabalho, em um único EXE. _O5_
- **R8.5 — WHEN** o produto é desinstalado, **THEN** o addon, o app e o atalho SHALL ser removidos. _P4_
- **R8.6 — WHEN** a validação final roda, **THEN** ela SHALL medir e registrar a redução obtida em
  `docs/Arquivos_teste/arquivo.cdr` e `arquivo2.cdr`, com o veredito honesto sobre a meta de 50%. _O4, P1_
- **R8.7 — WHEN** o produto usa componente de terceiro, **THEN** um `THIRD-PARTY-NOTICES.txt` SHALL
  acompanhar a distribuição. _Licenciamento_

## DESIGN (curto)

**i18n** — portar o padrão do SisCut (`LocalizationService` + `LocalizedStrings`, `enum Language
{ Pt, Es, En }`, persistência **injetada**, portanto testável). O dicionário vive em
`Optimus.Core` (puro) e é servido à UI HTML como um objeto JSON injetado no boot da página; a UI só
consome chaves. Isso mantém a tradução testável e a UI burra.

**Empacotamento** — evolução do que já existe: o instalador continua sendo **EXE único** (ILRepack
para os gerenciados; `WebView2Loader.dll` nativo embutido e extraído em runtime). Passa a carregar
dois payloads: `addon.*` (→ `<Corel>\Programs64\Addons\Optimus\`) e `app.*`
(→ `C:\Program Files\Optimus\` + atalho na área de trabalho e no menu Iniciar).

**Validação final** — é o aceite do projeto: rodar o produto completo nos dois arquivos reais e
publicar a tabela de redução por fase (sem perda → curvas → bitmap opt-in).

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/I18n/LocalizedStrings.cs` — **novo**: dicionário PT/ES/EN. _R8.1–R8.3_
- `src/Optimus.Core/I18n/LocalizationService.cs` — **novo**: idioma corrente + persistência injetada. _R8.1, R8.2_
- `src/Optimus.AddIn/wwwroot/index.html` — consome chaves, sem string fixa. _R8.3_
- `src/Optimus.Maintenance/wwwroot/index.html` — idem. _R8.3_
- `installer/Core/InstallerEngine.cs` — dois payloads + atalho. _R8.4, R8.5_
- `scripts/build-all.ps1` — monta os dois payloads. _R8.4_
- `THIRD-PARTY-NOTICES.txt` — **novo**. _R8.7_
- `tests/Optimus.Core.Tests/I18n/` — testes de completude de tradução.

## EXEMPLARS TO MIRROR

- `../siscut/plugin/src/SisCut.Localization/LocalizationService.cs` e `LocalizedStrings.cs` — copiar
  a **forma** (storage-agnostic, injetada, testável), não o conteúdo.
- `installer/` atual — já resolve addon; estender para o segundo payload.

## READY-MADE SOLUTIONS TO USE

- Padrão de i18n do SisCut (não reinventar).
- Instalador/ILRepack/ícone atuais (já funcionam).
- `WScript.Shell`/`IShellLink` via COM para o atalho, ou `.lnk` gerado pelo próprio instalador.

## TASKS

1. [x] **Teste primeiro:** toda chave existe nos três idiomas; nenhuma chave órfã; nenhuma string
   vazia. (O teste falha se alguém adicionar chave só em PT.) _R8.1, R8.3_
   Mais três guardas que valeram a pena: marcadores `{0}` iguais nos três idiomas, toda chave
   usada na UI existe no catálogo, e nenhum literal acentuado sobrou nos dois HTML.
2. [x] Implementar `LocalizedStrings` + `LocalizationService` (padrão SisCut). _R8.1, R8.2_
3. [x] **Teste primeiro:** idioma padrão é `Pt`; troca persiste e é relida. _R8.2_
4. [x] Extrair todas as strings das duas UIs para chaves; injetar o dicionário no boot da página. _R8.3_
5. [x] Seletor de idioma nas duas interfaces. _R8.1_
6. [x] Instalador: segundo payload (`app.payload.*`) + atalho na área de trabalho e menu Iniciar. _R8.4_
7. [x] Desinstalação limpa (addon + app + atalhos + entrada em "Aplicativos e recursos"). _R8.5_
8. [x] `THIRD-PARTY-NOTICES.txt` com todas as licenças efetivamente usadas. _R8.7_
9. [x] Atualizar `scripts/build-all.ps1` para montar e publicar os dois payloads. _R8.4_
10. [ ] **VALIDAÇÃO FINAL (aceite):** rodar o produto completo em `arquivo.cdr` e `arquivo2.cdr`;
    registrar a redução por fase e o veredito da meta de 50%; anexar ao `LESSONS` e ao `index.md`. _R8.6_
11. [ ] Teste de instalação limpa numa VM sem Optimus: instala, botão aparece no Corel, painel abre,
    app de manutenção abre pelo atalho. _R8.4_

## DO NOT WANT

- Não usar arquivo de recurso `.resx` para a UI HTML (o dicionário é dado, não recurso do .NET).
- Não traduzir por máquina sem revisão — ES e EN precisam de revisão humana antes de entregar.
- Não deixar string fixa em HTML/JS.
- Não quebrar o instalador que já funciona: evolução, não reescrita.
- Não publicar sem o `THIRD-PARTY-NOTICES.txt`.

## VALIDATION

- `dotnet test` verde, incluindo o teste de completude das traduções.
- Instalação limpa em VM: plugin + app + atalho.
- Trocar idioma nas duas UIs e confirmar que nada ficou em português.
- Tabela final de redução publicada, medida, nos dois arquivos reais.

## VERIFICATION GATE

- **O aceite do projeto:** o `arquivo2.cdr` (94% vetor) atinge ≥50%; o `arquivo.cdr` (62% bitmap)
  declara honestamente seu teto e o atinge quando a reamostragem é autorizada.
- Se a meta não for atingida em nenhum arquivo, o produto **ainda assim** tem de reportar a verdade
  — nunca inflar o número para "cumprir" (P1).
- Desinstalar e confirmar que não sobrou addon carregando no Corel.

## TECHNICAL CONSTRAINTS

Instalador continua EXE único. Arquivos < 500 linhas. Nenhuma dependência não-permissiva.
As UIs seguem 100% offline (sem CDN/webfont).

## LESSONS

**i18n (v1.9.0) — o que a implementação ensinou:**

- **Comparar TEXTO para detectar tradução faltando é o teste errado.** A primeira versão do teste de
  completude comparava o valor em ES/EN com o valor em PT e acusava 28 "chaves sem tradução" —
  todas falsas: português e espanhol coincidem legitimamente em "Copiar", "Moderado", "Objetos",
  "Resultado", "Segura", "Modificado", e o inglês coincide em "Preview". Pior: o teste incentivaria
  parafrasear uma tradução correta só para passar. O guarda certo é **presença explícita da chave**
  (`LocalizedStrings.HasExplicit`), que é estrutural e não tem falso positivo.
- **O dicionário tem de chegar ANTES do script da página.** As duas UIs não têm texto próprio (tudo
  é `data-i18n` / `T('chave')`), então um dicionário que chegasse por mensagem mostraria um flash de
  rótulos vazios — e numa máquina lenta, não tão flash. Solução:
  `AddScriptToExecuteOnDocumentCreatedAsync`, que roda antes de qualquer script da página.
- **Injeção acumula.** Ao trocar de idioma é obrigatório `RemoveScriptToExecuteOnDocumentCreated`
  antes de injetar o novo catálogo; sem isso dois catálogos correm e a tela fica bilíngue.
- **Separadores numéricos importam.** `14.342` em pt-BR é `14,342` em inglês; `toLocaleString` passou
  a receber o idioma escolhido, senão uma contagem de nós parece um decimal para o operador inglês.
- **Fronteira declarada, não esquecida:** o *log técnico* (painel de registro e `manutencao.log`)
  continua em português nos três idiomas, e a própria UI diz isso em `mnt.log.hint`. Tudo de que o
  operador **decide** — rótulos, botões, manchete, veredictos, tabelas, avisos de perda — é
  traduzido. Traduzir o log exigiria transformar em chave cada mensagem de progresso do
  `MaintenanceRunner`/`FileOptimizer`, e o valor disso é baixo perto do risco de deixar metade
  convertida.
- **Os avisos de perda viraram chaves sem quebrar nada:** `OptimizationSettings` ganhou
  `LossWarningKeys` ao lado de `LossWarnings` (pt-BR). Um teste percorre **todas** as combinações dos
  quatro cursores e exige que as duas listas tenham o mesmo tamanho — se divergirem, o operador veria
  uma consequência em português e outra em espanhol.
- **Rótulo de regra por id:** `mnt.op.<id>.label|desc|gain`, com o texto pt-BR da própria
  `CleanupRule` como fallback. Isso traduziu as 18 operações **sem** tocar nas regras puras: uma regra
  nova sem chaves ainda aparece correta em português em vez de em branco.

**Desinstalação:** entrada em "Aplicativos e recursos" com `UninstallString` apontando para uma
**cópia guardada** do próprio instalador em `%ProgramFiles%\Optimus\Manutencao\` — o cliente não pode
depender de reencontrar o download meses depois. O desinstalador **não** apaga
`%LOCALAPPDATA%\Optimus` (preferência de idioma e o registro do que já foi limpo são dele) e nunca
toca em `.cdr`.

_(Falta: a tabela final de redução por fase e por arquivo — tarefa 10, precisa da VM. É o material de
venda e a base honesta da promessa comercial.)_

## COVERAGE

R8.1 → 1,2,3,5 · R8.2 → 2,3 · R8.3 → 1,4 · R8.4 → 6,9,11 · R8.5 → 7 · R8.6 → 10 · R8.7 → 8
