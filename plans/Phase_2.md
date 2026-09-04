# Fase 2 — Diagnóstico do arquivo (medir antes de otimizar)

- **STATUS:** [x] **CONCLUÍDA — v1.3.1** (136 testes verdes; itens de COM diferidos, ver LESSONS)
- **OBJETIVO (1 frase):** Medir a composição real de um `.cdr` (quanto é bitmap, vetor, preview,
  fonte, metadado) e calcular o **teto honesto de redução** antes de qualquer otimização.
- **POR QUE EXISTE:** a medição dos arquivos do cliente provou que a estratégia certa depende do
  arquivo — `arquivo.cdr` é 62% bitmap (teto de ~34% sem tocar em imagem) e `arquivo2.cdr` é 94%
  vetor. Sem esta fase, o produto promete 50% e mente em metade dos casos (viola P1/O4).

## REQUIREMENTS (EARS)

- **R2.1 — WHEN** um `.cdr` salvo é analisado, **THEN** o sistema SHALL reportar o tamanho em bytes
  de cada componente (bitmaps, vetor, preview/thumbnail, fontes, estilos, paletas, metadados) e o
  respectivo percentual do arquivo. _O4, P1_
- **R2.2 — WHEN** a composição é conhecida, **THEN** o sistema SHALL calcular e exibir o **teto de
  redução sem perda** e o **teto com reamostragem de bitmap**, separadamente. _O4, P1, P3_
- **R2.3 — WHEN** o arquivo é dominado por bitmap (bitmaps > 40% do total), **THEN** o sistema SHALL
  declarar explicitamente que a meta de 50% exige reamostragem, e qual seria o resultado sem ela. _O4, P3_
- **R2.4 — WHEN** o documento está aberto no CorelDRAW, **THEN** o sistema SHALL inventariar via COM
  a contagem de nós por forma e a **resolução efetiva** de cada bitmap (pixels ÷ tamanho colocado). _R1.5_
- **R2.5 — WHEN** o diagnóstico roda, **THEN** o sistema SHALL não modificar o documento nem o arquivo. _P4_
- **R2.6 — WHEN** o mesmo arquivo é diagnosticado antes e depois de uma otimização, **THEN** o
  sistema SHALL produzir um comparativo por componente (o que de fato encolheu). _P1_
- **R2.7 — WHEN** o documento é diagnosticado, **THEN** o sistema SHALL reportar também o **peso de
  interação** (nós, objetos, efeitos vivos, profundidade de PowerClip) — o diagnóstico responde às
  DUAS perguntas do operador: "por que este arquivo é pesado?" e **"por que ele trava para
  arrastar?"**. As causas são diferentes e o relatório SHALL separá-las. _Fluidez, P1_
- **R2.8 — WHEN** a composição é classificada, **THEN** ela SHALL ser apurada por **contagem de
  chunks**, não por nome de arquivo — a heurística por nome foi medida como incorreta (em
  `arquivo.cdr` o `data1.dat` é ~97% `loda`, ou seja, vetor). _P1_

## DESIGN (curto)

Duas fontes de verdade, combinadas:

1. **Análise do container (sem CorelDRAW):** `.cdr` X4+ é um **ZIP** (magic `PK\x03\x04`, confirmado
   nos arquivos do cliente). `System.IO.Compression` lê as entradas e seus `CompressedLength` —
   isso dá a composição **exata em bytes no disco**, que é o número que importa. Mapeamento das
   entradas observadas:
   `content/data/Bitmaps.dat` → imagens · `content/data/data1.dat` + `page*.dat` + `root.dat` →
   vetor/objetos · `previews/*.png` → preview · `font/*` → fontes · `styles/*`, `color/*`,
   `META-INF/*` → metadados · `embed/*` → objetos incorporados.
   ⚠ `.cdr` anterior ao X4 é **RIFF**, não ZIP — detectar pelo magic e degradar para "composição
   indisponível" em vez de errar.
2. **Inventário COM (com CorelDRAW):** nós por forma e **DPI efetivo** de cada bitmap —
   `Bitmap.SizeWidth`(px) ÷ `Shape.SizeWidth`(mm→pol). É o DPI efetivo, não o nominal, que decide
   se há gordura (uma imagem de 4000 px colocada em 5 cm tem ~2000 DPI de desperdício).

O cálculo do teto é **puro** (`Optimus.Core`) e portanto totalmente testável:
`tetoSemPerda ≈ preview + CMX + ICC + metadados + (vetor × redução esperada de nós)`;
`tetoComBitmap ≈ tetoSemPerda + (bitmaps × economia estimada pelo DPI efetivo alvo)`.

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/Diagnostics/CdrComposition.cs` — **novo**: modelo da composição + percentuais. _R2.1_
- `src/Optimus.Core/Diagnostics/CdrContainerReader.cs` — **novo**: lê o ZIP, classifica entradas. _R2.1_
- `src/Optimus.Core/Diagnostics/ReductionCeiling.cs` — **novo**: cálculo puro dos tetos. _R2.2, R2.3_
- `src/Optimus.Core/Diagnostics/EffectiveDpi.cs` — **novo**: px ÷ tamanho físico → DPI efetivo. _R2.4_
- `src/Optimus.Interop/BitmapInspector.cs` — **novo**: lê dimensões/resolução via COM. _R2.4_
- `tests/Optimus.Core.Tests/Diagnostics/` — testes com os arquivos reais e com ZIPs sintéticos.

## EXEMPLARS TO MIRROR

- A análise já executada no planejamento (inspeção ZIP dos dois arquivos) — reproduzir em código.
- `../siscut/plugin/src/SisCut.Report/` — como um modelo de relatório puro é montado e testado.

## READY-MADE SOLUTIONS TO USE

- **`System.IO.Compression.ZipFile`** (BCL, sem dependência) para o container.
- `Bitmap.SizeWidth`/`SizeHeight` (px) e `Bitmap.ResolutionX/Y`; `Shape.SizeWidth/SizeHeight` (mm)
  — todos no dump. `Bitmap.Resample(...)` fica para a Fase 5.
- `Document.AddColorsToDocPalette(SelectedOnly, MaxColorsPerBitmap)` — **verificado**; alternativa
  do próprio Corel para colheita de cores (usada na Fase 7, citada aqui para não duplicar esforço).

## TASKS

1. [ ] **Teste primeiro:** `CdrContainerReader` classifica corretamente as entradas de um ZIP
   sintético que imita a estrutura observada (bitmaps/vetor/preview/fonte/metadado). _R2.1_
2. [ ] **Teste primeiro:** arquivo com magic RIFF (pré-X4) → resultado "composição indisponível",
   sem exceção. _R2.1_
3. [ ] Implementar `CdrContainerReader` + `CdrComposition` (percentual por componente). _R2.1_
4. [ ] **Teste primeiro (dados reais):** rodar sobre `docs/Arquivos_teste/arquivo.cdr` e
   `arquivo2.cdr` e asseverar os fatos já medidos — bitmaps ≈ 62% no primeiro, vetor ≈ 94% no
   segundo. Esse teste é a **regressão que prova que a medição continua honesta**. _R2.1, P1_
5. [ ] **Teste primeiro:** `ReductionCeiling` — arquivo 62% bitmap ⇒ teto sem perda < 40%; arquivo
   94% vetor ⇒ teto sem perda ≥ 50% quando a redução de nós esperada é atingida. _R2.2, R2.3_
6. [ ] Implementar `ReductionCeiling` (puro). _R2.2, R2.3_
7. [ ] **Teste primeiro:** `EffectiveDpi` — 4000 px em 50 mm ⇒ ~2032 DPI; 300 px em 25,4 mm ⇒ 300 DPI. _R2.4_
8. [ ] Implementar `EffectiveDpi` + `BitmapInspector` (COM, leitura pura). _R2.4, R2.5_
9. [ ] Ligar o diagnóstico ao painel: botão **"Analisar arquivo"** que mostra a composição, o teto
   sem perda e o teto com reamostragem, em linguagem clara. Sem otimizar nada. _R2.2, R2.3_
10. [ ] Implementar `CompareCompositions` (antes × depois) para a Fase 3+ usar no relatório. _R2.6_
11. [ ] Verificar hash do arquivo inalterado após diagnóstico. _R2.5_

## DO NOT WANT

- Não otimizar nada nesta fase — diagnóstico é estritamente leitura.
- Não estimar composição por heurística quando dá para medir o container.
- Não exibir um único número de "potencial" que esconda a diferença entre com e sem perda.
- Não prometer 50% na UI antes de ter medido o arquivo.

## VALIDATION

- `dotnet test` verde, incluindo os testes contra os **arquivos reais do cliente**.
- Diagnóstico do `arquivo.cdr` deve dizer, em português claro: *"62% deste arquivo são imagens.
  Sem reamostrar, o máximo é ~34%."*
- Diagnóstico do `arquivo2.cdr` deve indicar caminho viável para ≥50% sem perda.
- Hash idêntico antes/depois.

## VERIFICATION GATE

- Adversarial: um `.cdr` só com vetor não pode reportar bitmaps; um só com bitmap não pode reportar
  teto sem perda alto. Ambos casos cobertos por teste.
- A soma dos componentes tem de bater com o tamanho do arquivo (± overhead do ZIP) — se não bater,
  a classificação está perdendo entradas.

## TECHNICAL CONSTRAINTS

`Optimus.Core` continua sem COM: o leitor de container é BCL puro e roda no CI sem CorelDRAW. Os
arquivos de teste do cliente ficam em `docs/Arquivos_teste/` e **não** entram no instalador.

## LESSONS

**Executado em 2026-07-24 — v1.3.0. 118 testes verdes** (de 52 ao fim da Fase 1).

### O que a medição do formato revelou (e mudou no desenho)

A Fase 2 começou abrindo os arquivos, não teorizando. Isso derrubou duas premissas do próprio plano:

1. **Não é preciso contar chunks RIFF para atribuir bytes (R2.8 revisto).** Só o `content/root.dat`
   é RIFF; os `content/data/*.dat` são payload cru apontado por ponteiros de 16 bytes (a indireção
   do X6 — comprovada: 16.923 chunks `loda` × ~16 B = 264 KB, minúsculo). E raster e vetor moram em
   **entradas SEPARADAS** do ZIP. Logo a atribuição é feita no nível de entrada e é **exata**, sem
   estimativa e sem parser de chunk. Simplificou e ficou mais honesto.
2. **O arquivo declara sua própria estrutura:** `content/dataFileList.dat` lista os payloads
   (`Bitmaps.dat / data1.dat / masterPage.dat / page1.dat`). A classificação é autoritativa, não
   heurística de nome. A heurística "se existe Bitmaps.dat então dataN.dat também é raster" foi
   **testada e refutada**: num arquivo COM Bitmaps.dat, o `data1.dat` ainda guarda 32% do arquivo
   como objeto.

### Fatos medidos nos arquivos do cliente (viraram teste de regressão)

| | `arquivo.cdr` (4,5 MB) | `arquivo2.cdr` (7,9 MB) |
|---|---|---|
| Raster (`Bitmaps.dat`) | **2,86 MB = 63%** — 88,5 MB **crus** dentro | ausente (0) |
| Vetor | 1,57 MB = 35% | **7,65 MB = 94%** |
| Preview | 84 KB = 1,9% | 129 KB = 1,6% |
| Fontes | ausente | 298 B |
| **ICC** | **0** | **0** |

- **Bitmap é gravado SEM compressão** (razão comprimido/lógico = 0,032). É por isso que reamostrar
  rende tanto — e virou teste (`Raster_payload_is_stored_uncompressed`).
- **Nenhum dos dois arquivos tem perfil ICC.** Varredura por assinatura (`acsp` no offset 36 do
  cabeçalho ICC). O "ICC = 80% do arquivo" da pesquisa foi medido em OUTRO arquivo — **essa alavanca
  não existe nos arquivos do cliente**, e o produto não pode vendê-la. Teste:
  `Client_files_embed_no_icc_profile`.
- **`font/fontTable.dat` são NOMES, não outlines** (696 B): *Man City Dragon 2324, MS Gothic, Arial,
  Impact*. Fonte não é alavanca de tamanho aqui — mas é a lista de fontes usadas **sem COM**, que a
  Fase 7 vai consumir. Já implementado (`FontTableReader`) e testado.
- `color/color.xml` entrega `HasRgbObjects`/`HasCmykObjects`/`HasGrayscaleObjects` de graça (Fase 7).

### Armadilha de teste que quase passou

Os testes de integração fazem `return` quando o arquivo de amostra não existe — ou seja, **um verde
vazio** se a resolução de caminho quebrasse. Foi adicionado
`Sample_files_are_reachable_so_the_other_tests_are_not_vacuous`, que falha alto nesse caso. Sem essa
guarda, a suíte poderia "passar" sem verificar nada.

### Entregue

`CdrComponent`/`CdrComposition`, `CdrEntryClassifier`, `CdrContainerReader`, `IccProfileScanner`,
`FontTableReader`, `ReductionCeiling`, `EffectiveDpi`, `InteractionWeight` (+`InteractionInputs`,
bandas e fator dominante). UI: os **dois objetivos separados** — barras de composição com o teto
honesto, e banda de fluidez com a causa dominante e o aviso de que é estimativa.

### ⚠ CORREÇÃO NA v1.3.1 — o erro mais grave da fase (e o mais instrutivo)

A v1.3.0 concluiu **"nenhum arquivo do cliente tem perfil ICC"**. Estava certo para os dois arquivos
de teste do Optimus e **errado como generalização** — e por dois motivos, ambos meus:

1. **Procurei no lugar errado.** Varri as streams do documento pela assinatura ICC (`acsp`). Em
   `.cdr` X6+ o perfil **não é chunk RIFF nenhum**: é **entrada do ZIP** em
   `color/profiles/{cmyk,rgb,grayscale}/*.icc`. Varredura de assinatura dá **falso negativo**.
2. **Bug de ordenação no classificador.** A regra genérica `color/` vinha antes, então
   `color/profiles/cmyk/isocoated_v2_eci.icc` era classificado como **Metadados**.

**Magnitude do erro, medida:** em `ai-sten/docs/erro-1/arquivo.cdr` (copiado para
`docs/Arquivos_teste/icc-heavy.cdr`), o perfil ICC é **1.367.132 de 1.405.181 bytes = 97,3% do
arquivo**, enquanto **todo o desenho é 0,9%**. O Optimus teria reportado "97% metadados" — número
inútil exatamente no arquivo onde existe o maior ganho possível.

**Ressalva de honestidade que virou requisito (P3):** `color/color.xml` diz se o perfil está
**embutido** (`<ColorProfile id="Cmyk">`) e se aquele espaço tem **objetos** (`<HasCmykObjects>`). Em
29 de 29 arquivos com perfil do corpus real, o espaço embutido **estava em uso**. Ou seja: **remover
ICC NÃO é limpeza gratuita** — é decisão de gestão de cor e pode mudar a cor impressa. A UI diz isso
explicitamente. A alavanca honesta é *trocar por um perfil menor / não embutir*, com consentimento.

**Fontes embutidas** entraram como componente próprio: `embed/embedding*` chega a **93% de um
arquivo**. Discriminadas por assinatura verificada (`u32=1`, `u32=0x00000803`, byte de tipo, string
UTF-16LE de versão). **Não são subsetáveis por nós** (não há `sfnt` dentro; é payload opaco) — a
única alavanca é salvar sem embutir.

**Preview é STORED** (sem compressão): peso morto incompressível, até 60% de um arquivo do corpus.

Componentes novos: `IccProfile`, `EmbeddedFonts`. Novos: `ColorContextReader`, `EmbeddedFontProbe`.
`ReductionCeiling` passou a separar **sem perda** de **opt-in com custo declarado** e expõe
`DominantLever` (`icc`/`fonts`/`raster`/`vector`) para a UI recomendar a alavanca certa — num arquivo
97% ICC, sugerir redução de nós seria absurdo. Fixtures `icc-heavy.cdr`/`icc-heavy2.cdr` travam tudo.

**Lição de método:** meu teste `Client_files_embed_no_icc_profile` passava e estava *certo* — mas eu
transformei um resultado de **dois arquivos** numa conclusão sobre **o formato**. Amostra pequena não
vira lei. O corpus real tinha 29 contra-exemplos a um diretório de distância.

### DIFERIDO com justificativa (regra de não-órfão)

- **`BitmapInspector` (COM, R2.4)** → **Fase 5**. A matemática (`EffectiveDpi`) está pronta e
  testada; o leitor COM por bitmap só tem consumidor quando a reamostragem existir. Construí-lo
  agora seria código sem uso.
- **`CompareCompositions` (R2.6)** → **Fase 3**, onde o antes/depois é efetivamente exibido.
- **Transparências no peso de interação** → **Fase 4** (o score atual é um LIMITE INFERIOR, e a UI
  diz isso).

## COVERAGE

R2.1 → 1,2,3,4 · R2.2 → 5,6,9 · R2.3 → 5,6,9 · R2.4 → 7,8 · R2.5 → 8,11 · R2.6 → 10
