# Fase 4 — Fluidez: redução de nós, objetos e efeitos

- **STATUS:** [x] **v1.5.0 — implementada** (226 testes verdes). Falta calibrar/validar na VM.
- **OBJETIVO (1 frase):** Fazer o arquivo **parar de travar ao arrastar** dentro do CorelDRAW,
  reduzindo o peso de interação (nós, objetos e efeitos vivos) com desvio geométrico controlado — e
  **medindo** a melhora no tempo de redesenho, em vez de presumi-la.
- **DOR ATENDIDA:** a métrica semi-subjetiva do cliente — "o PC não travar para arrastar dentro do
  Corel" (Davi 2026-07-24). É esta fase, não as de tamanho, que resolve isso.

## ⚠ Correção de premissa (evidência)

O plano original tratava a redução de nós como alavanca de **tamanho**. **Está errado.** Medido por
dois métodos independentes (orçamento de bytes no ZIP + parsing de chunks via libcdr):
`pointSize = 2*4 + 1` ⇒ **9 bytes por nó**; coordenadas ≈ **4,6% dos bytes** do documento ⇒
**cortar metade dos nós economiza ~2% do arquivo**. O tamanho mora em ICC/fontes/preview/bitmap
(Fases 3 e 5).

**Mas o nó é exatamente a alavanca de FLUIDEZ:** cada nó é geometria que o Corel reprocessa e
redesenha a cada frame do arraste. Por isso a fase permanece — com o objetivo corrigido e uma régua
própria.

## REQUIREMENTS (EARS)

- **R4.1 — WHEN** o operador escolhe um nível (Leve/Médio/Forte), **THEN** o sistema SHALL aplicar
  uma tolerância de desvio **em milímetros reais**. _O1, P1_
- **R4.2 — WHEN** a redução é aplicada, **THEN** nenhum ponto da curva resultante SHALL divergir do
  original além da tolerância escolhida. _P3_
- **R4.3 — WHEN** uma curva é fechada, **THEN** ela SHALL permanecer fechada. _P3_
- **R4.4 — WHEN** o documento é analisado, **THEN** o sistema SHALL calcular um **peso de interação**
  a partir de nós, objetos, efeitos vivos, profundidade de PowerClip e transparências. _Fluidez_
- **R4.5 — WHEN** a otimização de fluidez termina, **THEN** o sistema SHALL reportar o **tempo real
  de redesenho antes e depois**, medido, além do peso de interação. _P1, Fluidez_
- **R4.6 — WHEN** o documento contém efeitos vivos, **THEN** o sistema SHALL listá-los com seu custo
  e oferecer achatamento **apenas como opt-in**, avisando que a editabilidade do efeito se perde. _P3_
- **R4.7 — WHEN** duas estratégias de redução de nós estão disponíveis (nativa e própria), **THEN** a
  escolha SHALL ser por **medição comparativa** sob a mesma tolerância. _V2_
- **R4.8 — WHEN** a curva tem 2 nós ou menos, ou é degenerada, **THEN** ela SHALL ficar intacta. _P3_
- **R4.9 — WHEN** formas estão dentro de PowerClip ou grupos, **THEN** elas SHALL ser processadas
  igualmente. _R1.3_

## DESIGN (curto)

### 1. Como medir fluidez (o que torna esta fase honesta)

**Régua A — peso de interação (determinística, pura, testável):**
```
peso = nós
     + objetos      × Po
     + efeitosVivos × Pe          (blend/extrude/envelope/sombra/contorno/distorção/perspectiva/lente)
     + transparências × Pt
     + profundidadeDePowerClip × Pp
```
Os pesos saem calibrados pela Régua B (não são chutados) e ficam documentados.

**Régua B — tempo real de redesenho (empírica, na VM):**
`ActiveView.ToFitPage()` para fixar o zoom → cronometrar **N × `Window.Refresh()`** → mediana.
Ambos verificados na typelib (`Window.Refresh()` dump 7811; `ActiveView.ToFitPage()` 2975;
`get_Zoom` 2973). Medir **antes e depois**, no mesmo zoom e na mesma máquina, descartando a primeira
amostra (aquecimento). É esse número que aparece para o operador — nunca uma promessa.

### 2. Alavancas de fluidez, por custo/benefício

| Alavanca | Efeito no arraste | Risco | Padrão |
|---|---|---|---|
| Redução de nós (tolerância em mm) | **alto** | baixo, limitado pela tolerância | **ligado** |
| Achatar efeitos vivos (`FlattenEffects`/`ClearEffect`) | **alto** | perde editabilidade do efeito | **desligado** (opt-in) |
| Reduzir resolução de bitmap (Fase 5) | médio | perda de imagem | opt-in (Fase 5) |
| Reduzir nº de objetos (combinar duplicados idênticos) | médio | pode alterar estrutura | **desligado** (opt-in) |
| Converter arte pesadíssima em bitmap (`ConvertToBitmapEx`) | altíssimo | perde o vetor | **fora do padrão**, só sugerido |

Efeitos verificados (`cdrEffectType`): `cdrBlend=0`, `cdrExtrude=1`, `cdrEnvelope=2`,
`cdrTextOnPath=3`, `cdrControlPath=4`, `cdrDropShadow=5`, `cdrContour=6`, `cdrDistortion=7`,
`cdrPerspective=8`, `cdrLens=9`, `cdrCustomEffect=10`, `cdrInnerShadow=11`.
Métodos verificados: `Shape.Effects` (4341/6168), `Shape.FlattenEffects()` (6094),
`Shape.ClearEffect(cdrEffectType)` (6215), `Shape.Separate()` (6145), `Shape.Transparency` (6205),
`Shape.ConvertToBitmapEx(...)` (6208).

### 3. Redução de nós — duas estratégias, decididas por medição

**(A) Nativa (baseline, sem risco):** `NodeRange.AutoReduce(Double PrecisionMargin)` (dump 5196) e
`Curve.AutoReduceNodes(Double 0..100?, Boolean?)` (dump 3787). Depende da Fase 1 (mm correto).

**(B) Própria, em `Optimus.Core`:** **RDP não se aplica direto a Bézier** — opera sobre polilinha.
O caminho correto (igual Inkscape/potrace) é **achatar → simplificar → reajustar Béziers**, com o
algoritmo de **Schneider (Graphics Gems, 1990)**.

**Regra (R4.7):** a nativa é o padrão; a própria só a substitui se **vencer em medição** — mais nós
removidos com o mesmo desvio máximo real. Sem evidência, não troca.

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/Performance/InteractionWeight.cs` — **novo**: régua A (pura). _R4.4_
- `src/Optimus.Core/Geometry/Polyline.cs` — achatamento com erro controlado. _R4.2_
- `src/Optimus.Core/Geometry/DouglasPeucker.cs` — decimação com tolerância. _R4.2_
- `src/Optimus.Core/Geometry/BezierFitter.cs` — ajuste de Bézier (Schneider). _R4.2, R4.3_
- `src/Optimus.Core/Geometry/MaxDeviation.cs` — mede o desvio real (é o juiz de R4.2). _R4.2, R4.7_
- `src/Optimus.Core/Optimization/CurvePlan.cs` — níveis → tolerância em mm. _R4.1_
- `src/Optimus.Interop/RedrawTimer.cs` — **novo**: régua B (`ToFitPage` + `Window.Refresh`). _R4.5_
- `src/Optimus.Interop/EffectInventory.cs` — **novo**: lista efeitos vivos e custo. _R4.6_
- `src/Optimus.Interop/CurveReducer.cs` — aplica a estratégia via COM. _R4.7, R4.9_
- `tests/Optimus.Core.Tests/Geometry/` e `/Performance/` — coração do TDD da fase.

## EXEMPLARS TO MIRROR

- `../siscut/plugin/src/SisCut.Interop/CorelShapeReader.cs` — leitura de `SubPaths`/`Segments` e
  pontos de controle de Bézier via COM (padrão já provado em produção).
- `../siscut/plugin/src/SisCut.Geometry/` — organização e teste de geometria pura.

## READY-MADE SOLUTIONS TO USE

- **API nativa de redução** — não reimplementar o que o Corel já faz.
- Para (B), **avaliar antes de escrever**: implementação permissiva (MIT/Apache/BSD/domínio público)
  do ajuste de Schneider em C#/portável — **tarefa 1**, com licença registrada.
- `Window.Refresh()` / `ActiveView.ToFitPage()` para a régua B — nativo, verificado.

## TASKS

1. [ ] **Decisão registrada:** levantar implementação permissiva do ajuste de Bézier (Schneider);
   anotar nome + licença + veredito de uso comercial fechado. Sem opção adequada, justificar
   implementação própria. _S1.5, V2_
2. [ ] **Teste primeiro:** `InteractionWeight` — documento com 10× mais nós pesa mais; efeito vivo
   pesa mais que forma simples; PowerClip aninhado pesa mais que raso. _R4.4_
3. [ ] Implementar `InteractionWeight` (puro). _R4.4_
4. [ ] Implementar `RedrawTimer` (régua B): zoom fixo, N amostras, descarta a primeira, usa mediana. _R4.5_
5. [ ] **Calibração (experimento):** correlacionar régua A × régua B em pelo menos 3 arquivos reais;
   fixar os pesos `Po/Pe/Pt/Pp` com base nessa medição e documentá-los. _R4.4, R4.5, P1_
6. [ ] **Teste primeiro:** achatamento — Bézier conhecida vira polilinha com erro abaixo do alvo;
   círculo por 4 Béziers achata com erro < 0,01 mm. _R4.2_
7. [ ] Implementar `Polyline`. _R4.2_
8. [ ] **Teste primeiro:** Douglas-Peucker — 100 pontos colineares reduzem a 2; nenhum ponto
   removido excede a tolerância. _R4.2_
9. [ ] Implementar `DouglasPeucker`. _R4.2_
10. [ ] **Teste primeiro:** `MaxDeviation` — caso trivial = 0; deslocamento conhecido = valor exato. _R4.2_
11. [ ] Implementar `MaxDeviation`. _R4.2_
12. [ ] **Teste primeiro:** `BezierFitter` — reajusta com menos segmentos e desvio ≤ tolerância;
    curva fechada continua fechada; curva de 2 nós fica intacta. _R4.2, R4.3, R4.8_
13. [ ] Implementar `BezierFitter`. _R4.2, R4.3_
14. [ ] **Teste primeiro:** `CurvePlan` — níveis mapeiam para tolerâncias em mm crescentes e
    documentadas; valor fora de faixa é rejeitado. _R4.1_
15. [ ] Implementar `CurveReducer` (COM), estratégia nativa como padrão, percorrendo grupos e
    PowerClips. _R4.7, R4.9_
16. [ ] Implementar `EffectInventory` + achatamento **opt-in** com aviso de perda de editabilidade. _R4.6_
17. [ ] **Experimento comparativo (obrigatório):** nos arquivos reais, medir A × B sob a mesma
    tolerância — nós removidos, desvio máximo real, peso de interação e **tempo de redesenho**.
    Registrar em `LESSONS` e definir o padrão. _R4.5, R4.7, P1_
18. [ ] Painel: mostrar **antes × depois** de nós, objetos, efeitos e **tempo de redesenho medido**. _R4.5_

## DO NOT WANT

- Não vender fluidez sem medir o redesenho (viraria a mesma promessa vazia do "limpar RAM").
- Não aplicar RDP diretamente sobre pontos de controle de Bézier (distorce).
- Não usar `Smoothen` — reposiciona alças e **não remove nó** (é por isso que o "Suavizar" do Corel
  nunca emagreceu nem acelerou os arquivos do cliente).
- Não achatar efeito por padrão (destrói editabilidade — opt-in, sempre).
- Não converter vetor em bitmap por padrão (`ConvertToBitmapEx` só como sugestão explícita).
- Não prometer que reduzir nós encolhe o arquivo — são ~2% (é fluidez, não tamanho).
- Não adotar o algoritmo próprio sem ele vencer o nativo em medição.

## VALIDATION

- `dotnet test` verde, com a geometria rodando **sem CorelDRAW**.
- Propriedade testada: para toda curva de teste e toda tolerância, `MaxDeviation ≤ tolerância`.
- Na VM, nos arquivos reais: **tempo de redesenho antes × depois**, mesma máquina e mesmo zoom.
- Inspeção visual: peça com detalhe fino, antes/depois sobrepostos, sem deformação perceptível.
- **Teste de sentido humano:** arrastar um objeto no arquivo antes e depois — a melhora tem de ser
  perceptível, não só numérica. Se o número melhora e a sensação não, a régua está errada.

## VERIFICATION GATE

- **O teste que mata a fase:** desvio máximo medido acima da tolerância em qualquer caso ⇒
  implementação errada. Não relaxar o teste.
- Curva fechada que abre = falha.
- Se o tempo de redesenho **não** melhorar de forma mensurável, a fase não cumpriu seu objetivo —
  reportar isso honestamente em vez de exibir só o peso de interação (P1).
- Rodar duas vezes: a segunda passada remove pouquíssimo (convergência) e nunca deforma mais.
- Se o algoritmo próprio não superar o nativo, fica só o nativo e o código não usado é **removido**.

## TECHNICAL CONSTRAINTS

`Optimus.Core` sem COM. Arquivos < 500 linhas. Tolerâncias documentadas em mm no código e na UI.
Medição de redesenho é sensível à máquina — sempre comparar antes/depois **na mesma sessão**,
nunca entre máquinas.

## LESSONS

### A primeira execução real (kaneki.cdr, máquina do Davi, 2026-07-25) — v1.11.0

Números medidos: **421.057 → 290.587 nós (−31%)**, **7.959.265 → 6.818.507 bytes (−14,3%)**, estimativa
de **24,8%**, tempo total **17 min 27 s** (só a simplificação: **13 min 22 s**). Veredito do Davi:
*"a fluidez não senti essa coca-cola toda não"*. Ele está certo, e cada parte disso virou correção:

- **O cronômetro de fluidez não media nada.** `Window.Refresh()` apenas ENFILEIRA um repaint e retorna;
  o desenho acontece depois, no laço de mensagens do Corel. Daí "0,1 ms → 0 ms" depois de remover
  130 mil nós. Entrou `RenderTimer`: renderiza o documento com `Document.ExportBitmap` +
  `ExportFilter.Finish()` — síncrono por construção, passa por todos os objetos — e a UI passa a dizer
  **"tempo para desenhar"**, não "redesenho". O `measured=False` do relatório antigo estava certo em
  não afirmar nada; o erro foi ter um instrumento que nunca ia medir.
- **13 minutos eram repaint, não geometria.** Faltava `Application.Optimization = true` +
  `EventsEnabled = false` durante o lote: o Corel repintava a tela a cada nó editado. Entrou
  `BulkEditScope`, que restaura os dois num `finally` e força um `Refresh()` final — deixar
  `Optimization` ligado faz o Corel parecer travado.
- **−31% de nós ≠ −31% de arquivo.** O `.cdr` é ZIP: as coordenadas deflacionam, e coordenada
  simplificada comprime um pouco pior por byte. A razão medida foi **0,63**; o estimador passou a
  aplicar 0,55 (abaixo de propósito). A estimativa nesse arquivo cai de 24,8% para dentro de 2 pontos
  do real, travada por `EstimatorCalibrationTests`.
- **Otimizamos o eixo errado para a sensação.** Cortar nós encolhe o ARQUIVO. O que o operador SENTE ao
  arrastar tem outro dominante: **a contagem de objetos não mudou** (16.899 formas, 16.893 aninhadas) e
  2.523 elipses continuam 2.523 elipses. O modelo de peso apontava `dominant=Nodes` porque conta nós;
  falta contar o custo por objeto e o aninhamento.
- **A alavanca de fluidez que funciona na hora é o MODO DE VISUALIZAÇÃO.** A documentação da Corel é
  explícita: Normal redesenha mais rápido que Aprimorado (que renderiza preenchimento PostScript, alta
  resolução e antisserrilhamento), e Contorno Simples é mais rápido ainda. Não altera o arquivo, é
  instantâneo e reversível. Entrou `ViewModeSwitch` (`ActiveView.Type`, enum `cdrViewType` verificado no
  dump).

**Implementada em 2026-07-25 — v1.5.0, 226 testes verdes** (eram 191 no fim da Fase 3).

### Entregue

**Régua B (a que importa):** `RedrawTimer` — fixa o zoom com `ActiveView.ToFitPage()`, cronometra
N × `Window.Refresh()`, **descarta a primeira amostra** (aquecimento) e usa a **mediana** (média seria
puxada por um travamento). Medido antes e depois no `Apply`. Se não medir, a UI **não afirma nada**
sobre arraste; se ficar **mais lento**, exibe em vermelho com "+X%".

**Efeitos vivos:** `EffectCost` (ordinal, com rótulo e custo de achatamento em pt-BR por tipo) +
`EffectInventory` (COM: varre, conta transparências, achata via `FlattenEffects`). Achatar é **opt-in**
— mantém a aparência e destrói a editabilidade, e o aviso diz isso.

**Geometria pura (netstandard2.0, sem dependência):** `Point2`/`CubicBezier` (de Casteljau),
`Polyline` (achatamento adaptativo com orçamento de erro = 10% da tolerância), `DouglasPeucker`
(**iterativo**, não recursivo — 20 mil pontos não podem estourar a pilha dentro do processo do Corel),
`BezierFitter` (Schneider/Graphics Gems, com Newton-Raphson e fallback de Wu/Barsky).

**Resultado medido do pipeline completo** (flatten → simplify → refit) num círculo de raio 40 mm com
tolerância 0,03 mm: 513 pontos → 129 → **14 Béziers**, com **erro radial máximo de 0,0134 mm**
(0,03% do raio). Numa elipse sobre-amostrada: **601 nós → 12 Béziers**, desvio 0,0256 mm.

### DECISÃO DE REÚSO registrada (task 1)

Nenhum pacote .NET com licença permissiva **verificada** foi confirmado para o ajuste de Schneider em
`netstandard2.0` sem dependências. O algoritmo é publicado (Graphics Gems, 1990), tem ~200 linhas e
precisaria de testes próprios de qualquer forma. **Implementado aqui** em vez de arriscar uma
dependência de licença não verificada — embarcar só código permissivo é regra da casa.

**E o `BezierFitter` NÃO é o padrão:** o `AutoReduce` nativo do Corel continua no comando até o
próprio medir melhor no mesmo arquivo com a mesma tolerância (verdict V2). O fitter existe como
desafiante, com o `MaxDeviation` pronto para julgar a disputa.

### Bug que um teste pegou
A asserção "um círculo refita em ≤12 segmentos" era um **chute meu** e falhou com 14. Troquei por
medir o **erro radial real** (0,0134 mm) — que é o que importa — em vez de contar segmentos.

### PENDENTE na VM
Calibrar os pesos da régua A (`InteractionWeight`) contra a régua B em ≥3 arquivos; rodar o
experimento comparativo nativo × próprio; e o teste de sentido humano (arrastar antes/depois).

## COVERAGE

R4.1 → 14,15 · R4.2 → 6–13,17 · R4.3 → 12,13 · R4.4 → 2,3,5 · R4.5 → 4,5,17,18 · R4.6 → 16 ·
R4.7 → 1,15,17 · R4.8 → 12 · R4.9 → 15
