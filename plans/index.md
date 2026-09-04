# Optimus — Plano de Implementação (índice)

> **Modo:** PLANNER. Este índice + os `Phase_N.md` são o contrato de planejamento.
> Nenhum código de produção é escrito até aprovação explícita (gate S5).
> **Steering:** `CLAUDE.md` (na raiz do `optimus/`). **Fonte autoritativa da API do Corel:**
> `docs/vgcore-tlb-dump.txt` (dump da typelib VGCore 25.2 — grep antes de escrever qualquer chamada).

---

## Objetivo global

Entregar o **Optimus**: uma extensão do CorelDRAW + um app de manutenção, que (1) **reduz o peso
de arquivos `.cdr`**, (2) **deixa o arquivo fluido para trabalhar** (não travar ao arrastar),
(3) **audita cores, fontes e cobertura de acentos**, e (4) **otimiza o Windows** com operações que
comprovadamente funcionam para o perfil de uma gráfica/estúdio de design.

### ⚠ Dois objetivos distintos, com alavancas distintas (correção pós-pesquisa)

O cliente enuncia a meta como "reduzir 50%", mas a dor real do operador é **semi-subjetiva: o PC
travar ao arrastar objeto dentro do Corel** (Davi 2026-07-24). São problemas diferentes e a medição
provou que **as alavancas quase não se sobrepõem**:

| Alavanca | Impacto em **TAMANHO** | Impacto em **FLUIDEZ** |
|---|---|---|
| Nº de nós / Béziers | **~2%** (medido: nó = 9 bytes; coordenadas ≈ 4,6% do documento) | **principal** |
| Nº de objetos | baixo | **alto** |
| Efeitos vivos (transparência, sombra, blend, contorno, envelope, PowerClip aninhado) | baixo | **alto** (recalculados a cada redesenho) |
| Perfil ICC embutido | **altíssimo** (até ~80% em arquivo vetorial pequeno) | nenhum |
| Preview/thumbnail, dados CMX, fontes embutidas | relevante | nenhum |
| Bitmaps | **dominante** (62% no `arquivo.cdr`) | médio (memória/redesenho; o Corel usa proxy de tela) |

**Consequência:** reduzir nó **não** é a alavanca de tamanho — é a alavanca de fluidez.
Perseguir 50% de tamanho e perseguir fluidez são trabalhos diferentes, medidos com réguas
diferentes, e o produto reporta os dois separadamente (P1).

**Meta declarada de tamanho:** ≥50%. **Veredito medido:** depende do arquivo — ver "Descobertas
empíricas". O produto **mede primeiro e informa o teto real**, em vez de prometer um número.
**Meta de fluidez:** reduzir o "peso de interação" (nós + objetos + efeitos) com desvio geométrico
dentro da tolerância, e **medir** a melhora, não presumi-la.

---

## Estado atual (v1.0 já em disco)

Existe e compila: plugin (`src/Optimus.AddIn`), DLL de ícone (`src/Optimus.Resources`), instalador
EXE único (`installer/`), build (`scripts/build-all.ps1`). **Não existe:** nenhum teste, nenhuma
separação de camadas, nenhuma lógica testável.

**A v1.0 entrega 0% de redução nos arquivos reais do cliente** (medido: `docs/Arquivos_teste/`,
`data1.dat` byte-idêntico antes/depois). As três causas foram identificadas e viram tarefas da Fase 1.

---

## Descobertas empíricas (medidas, não estimadas)

**Composição real dos arquivos do cliente** (inspeção direta do container ZIP — `.cdr` X4+ é um ZIP):

| Componente | `arquivo.cdr` (4,5 MB) | `arquivo2.cdr` (7,9 MB) |
|---|---|---|
| `content/data/Bitmaps.dat` | **2.791 KB — 62%** (84 MB crus) | ausente |
| Vetor (`data1`+`page1`+`root`) | 1.543 KB — 34% | **7.467 KB — 94%** |
| `previews/*.png` (thumbnail) | 81 KB — 1,8% | 126 KB — 1,6% |
| Fontes embutidas (`font/`) | ~0 | ~0 |

**Consequência de projeto:** a estratégia de otimização **depende do arquivo**. Um otimizador que
só mexe em curvas tem teto de ~34% no `arquivo.cdr`. Por isso a Fase 2 (diagnóstico) vem **antes**
das fases de otimização — o produto escolhe a alavanca certa e diz a verdade sobre o teto.

**Custo real de um nó (verificado no formato, via libcdr):** `pointSize = 2*4 + 1` ⇒ **9 bytes por
nó** (2 × int32 + 1 byte de tipo), coordenadas em 1/254000 pol. Um segmento Bézier = 3 nós = 27 B.
Conferido empiricamente: 1318 nós × 9 + 148 caminhos × 4 = **12.454 bytes, batendo exato** com o
medido. As coordenadas representam **~4,6% dos bytes** do documento ⇒ **cortar metade dos nós
economiza ~2% do arquivo**. Confirmado por dois métodos independentes (orçamento de bytes no ZIP e
parsing de chunks). É por isso que a redução de nós foi **reclassificada para o objetivo de fluidez**.

**Onde o tamanho realmente mora:** perfil **ICC** embutido (medido em ~80% de um arquivo vetorial
pequeno — é uma taxa fixa que independe da complexidade da arte, por isso arquivos vetoriais
pequenos são os piores ofensores proporcionalmente), fontes embutidas, preview e bitmaps.

**Eras de container (verificado):** ≤X3 = RIFF puro · X4/X5 = ZIP com `content/riffData.cdr` ·
**X6+ = ZIP com `content/root.dat` + `content/data/*.dat`** ← os arquivos do cliente são todos
deste último caso. Em arquivo moderno **não há chunk `cmpr`**: toda a compressão é a do ZIP externo.
A classificação vetor/raster **não pode** ser por nome de arquivo (heurística furou: em
`arquivo.cdr` o `data1.dat` é ~97% `loda`, ou seja, vetor) — tem de ser por contagem de chunks.

**Bugs confirmados na v1.0:**
1. `FileOptimizer.cs:66` usa `CdrMillimeter = 4` → documento vai para **centímetros** → as
   tolerâncias Leve/Médio/Forte são **10× mais agressivas** que o rótulo. (`cdrUnit` na typelib:
   `cdrMillimeter = 3`, `cdrCentimeter = 4`.)
2. A travessia ignora **conteúdo de PowerClip** (`Shape.PowerClip.Shapes`) — em arquivo de design,
   a maior parte da arte está lá dentro e nunca foi visitada.
3. Só processa shape que **já é curva** (`shp.Curve`); retângulo/elipse/texto/efeito são pulados.
4. `WindowsOptimizer.cs`: roda elevado e usa `Path.GetTempPath()` → limpa o `%TEMP%` **do admin**,
   não o do designer; **sem age-gate**; e limpa **Prefetch** (valor negativo comprovado).

---

## CONVENTIONS MAP

1. Lógica pura → `netstandard2.0`; COM/Win32 → `net48`; testes → `net8.0` + xUnit. **TDD: teste primeiro.**
2. COM late-bound (`dynamic`) atrás de interface → 1 binário para Corel 2024/25/26.
3. **Constante de VGCore só entra no código depois de grep no `docs/vgcore-tlb-dump.txt`.**
4. `Page.FindShapes(...)` (verificado) em vez de recursão manual; **PowerClip percorrido explicitamente**.
5. `Color.GetCopy()` antes de inspecionar cor — `Fill.UniformColor` é referência viva; ler não pode alterar arte.
6. `BeginCommandGroup`/`EndCommandGroup` pareados; estado do documento (unidade/view) restaurado sempre.
7. UI WebView2 + HTML **offline**; JS→C# via `WebMessageAsJson`; C#→JS via `ExecuteScriptAsync`.
8. `AssemblyResolve` da pasta do addon; nada pode derrubar o Corel; log em `%TEMP%\Optimus\docker.log`.
9. Mensagens pt-BR + i18n PT/ES/EN (padrão `LocalizationService` do SisCut, testável).
10. Só dependência **permissiva** (MIT/Apache/BSD/zlib); `THIRD-PARTY-NOTICES.txt` obrigatório.
11. Manutenção do Windows: **nada de placebo**; número exibido = ganho real de espaço em disco.
12. Arquivos < 500 linhas. Instalador = EXE único. Build por `scripts/build-all.ps1`.

---

## Reúso vs. construção (S1.5)

| Componente | Decisão |
|---|---|
| Plugin/docker WebView2, instalador EXE único, ícone, XSLT do addon | **REUSA** (v1.0 já funciona) |
| Padrão de i18n (`LocalizationService`/`LocalizedStrings`) | **PORTA do SisCut** (não reinventar) |
| Redução de nós nativa | **USA** `NodeRange.AutoReduce(mm)` + `Curve.AutoReduceNodes(0..100)` (ambos verificados) |
| Enxugar arquivo no salvamento | **USA** `StructSaveAsOptions`: `ThumbnailSize=cdrNoThumbnail`, `IncludeCMXData=False`, `EmbedICCProfile` (verificados) |
| Backup antes de otimizar | **USA** `Document.SaveAsCopy(...)` (verificado) |
| Simplificação de curva além do nativo | **CONSTRÓI** em `netstandard2.0` (fitting de Bézier, algoritmo de Schneider) — RDP puro é para polilinha, não Bézier |
| Reamostragem de bitmap | **USA** `Bitmap.Resample(W,H,AntiAlias,ResX,ResY)` (verificado) |
| Cobertura de acento/glifo | **USA WPF `GlyphTypeface.CharacterToGlyphMap`** — zero dependência nova, `PresentationCore` já é referenciado |
| Cores/spot | **USA** `Color.IsSpot`/`SpotColorName`/`HexValue` + `Palette.MatchColor`; **nunca embarcar tabela Pantone** |
| Fontes usadas | **USA** `TextRange.EnumRanges(cdrTextPropertyFont=4)` + `Application.FontList` |
| Manutenção do Windows | **USA in-box**: `cleanmgr` (VolumeCaches), DISM, WMI/`MSFT_PhysicalDisk`, `SHEmptyRecycleBin`, `defrag /O`, Restart Manager |
| BleachBit / Sysinternals / Winapp2 / Pantone | **NÃO EMBARCA** (GPLv3 / EULA proíbe / CC BY-SA contamina o arquivo de regras / DMCA) |
| Agendador, toast, TaskScheduler NuGet | **FORA DE ESCOPO** (O5: app manual) |

---

## Verdicts de arquitetura (S3)

**V1 — Como atingir a meta de 50%?**
- A) Só redução de nós. B) Só reamostragem de bitmap. C) Estratégia por arquivo, medida antes.
- **Verdict: C.** Medição prova que A tem teto de ~34% em arquivo com bitmap e B é irrelevante em
  arquivo 94% vetor. O produto **diagnostica a composição, aplica as alavancas sem perda primeiro,
  depois as com perda mediante consentimento, e reporta o teto real**. Honestidade é requisito (O4).

**V2 — Onde mora o algoritmo de simplificação de curva?**
- A) Chamar só a API nativa do Corel. B) Algoritmo próprio em C# puro. C) Os dois, medindo qual ganha.
- **Verdict: C.** A API nativa (`AutoReduce`) é o baseline barato e sem risco; o fitting próprio vive
  em `netstandard2.0` (testável sem Corel) e só é aplicado se **medir melhor** que o nativo no mesmo
  arquivo, com o mesmo desvio máximo. Sem teste comparativo, não entra.

**V3 — Camadas (o que torna TDD possível).**
- **Verdict:** `Optimus.Core` (netstandard2.0, puro: geometria, regras de limpeza, cobertura de
  glifos, cores, regras de manutenção, medição) · `Optimus.Interop` (net48, COM) ·
  `Optimus.Windows` (net48, Win32/WMI) · `Optimus.AddIn` (net48, casca) · `Optimus.Maintenance`
  (net48, app desktop) · `tests/*` (net8.0). Sem isso não há teste — e a v1.0 provou o custo disso.

**V4 — CSS da UI: Tailwind compilado (padrão SisCut) ou CSS embutido (v1.0)?**
- **Verdict: manter CSS embutido à mão.** São 3 telas pequenas e o requisito real é *offline* — que
  ambos atendem. Trazer o pipeline do Tailwind adiciona etapa de build sem ganho aqui. (Reabrir se
  a UI crescer.)

**V5 — Segurança da otimização.**
- **Verdict (O2):** otimiza **no lugar**, com **`SaveAsCopy` de backup antes** de qualquer mutação,
  tudo dentro de um `BeginCommandGroup` pareado, `ClearUndoList` antes de salvar e fecha/reabre para
  reserializar limpo.

---

## Princípios invioláveis

- **P1 — Nunca mentir um número.** O percentual exibido é medido no arquivo/disco real; estimativas
  são rotuladas como estimativa e nunca viram a manchete.
- **P2 — Nada de placebo.** Operação que não tem benefício medido não entra, mesmo se pedida.
- **P3 — Nunca degradar a arte sem consentimento explícito.** Toda perda é opt-in, por arquivo, com
  o custo declarado.
- **P4 — Nada pode derrubar o CorelDRAW nem corromper o documento.**
- **P5 — TDD:** todo algoritmo nasce com teste; arquivos < 500 linhas.
- **P6 — Constante de Corel só com evidência** no dump da typelib.

---

## Fases

| # | Fase | STATUS |
|---|---|---|
| 1 | [Fundação testável + correções críticas](Phase_1.md) | [x] **CONCLUÍDA — v1.2.2** (verificada em arquivo real) |
| 2 | [Diagnóstico do arquivo (medir antes de otimizar)](Phase_2.md) | [x] **CONCLUÍDA — v1.3.0** |
| 3 | [Otimização AJUSTÁVEL (o "remap")](Phase_3.md) | [x] **v1.4.0** (falta validar na VM) |
| 4 | [Fluidez — redução de nós, objetos e efeitos](Phase_4.md) | [x] **v1.5.0** (falta calibrar na VM) |
| 5 | [Bitmaps por DPI efetivo (opt-in)](Phase_5.md) | [x] **v1.7.0** (falta validar na VM) |
| 6 | [Módulo 1 — app de manutenção do Windows](Phase_6.md) | [x] **v1.10.0** — rodou na máquina do Davi (55,7 MB liberados); ganhou a aba **Desempenho** (falta rodar numa máquina de gráfica) |
| 7 | [Módulo 3 — cores, fontes e acentos](Phase_7.md) | [x] **v1.6.0** (falta validar na VM) |
| 8 | [i18n, empacotamento e validação final](Phase_8.md) | [~] **v1.9.0** — i18n PT/ES/EN, dois payloads e desinstalação prontos; falta a validação final na VM |

**Ordem obrigatória:** 1 → 2 antes de 3/4/5 (sem medição não se sabe qual alavanca puxar nem se a
meta foi atingida). 6 e 7 são independentes e podem ser paralelizadas depois da Fase 1. 8 fecha.

---

## Critério de aceite do projeto

1. Nos arquivos reais em `docs/Arquivos_teste/`: o Optimus **mede** a composição, **aplica** as
   alavancas cabíveis e **reporta** redução real — atingindo ≥50% no `arquivo2.cdr` (94% vetor) e
   declarando com clareza o teto do `arquivo.cdr` (62% bitmap), atingindo-o se o operador autorizar
   a reamostragem.
2. Nenhum arquivo de teste é corrompido; o backup existe e abre.
3. `dotnet test` verde; nenhum arquivo > 500 linhas.
4. Instalador EXE único instala plugin + app de manutenção, botão aparece no Corel, painel abre.
