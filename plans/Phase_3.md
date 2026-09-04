# Fase 3 — Otimização AJUSTÁVEL (o "remap" do arquivo)

- **STATUS:** [x] **v1.4.0 — implementada** (191 testes verdes). Falta a validação na VM (task 12/13).
- **OBJETIVO (1 frase):** Dar ao operador **controles em linguagem de gráfica** que trocam
  qualidade por tamanho de forma consciente, mostrando **antes de aplicar** quanto cada escolha
  economiza — e aplicar o que ele escolheu com backup e reserialização limpa.

## FILOSOFIA (Davi 2026-07-25) — o produto é um REMAP, não um botão mágico

> *"É como um overclock ou remap. O usuário não sabe o que é ICC. A interface é tão importante
> quanto a técnica."*

Consequências vinculantes para esta fase:

1. **Nenhum termo técnico na UI.** "ICC" não aparece; aparece **FIDELIDADE DE COR**. "DPI" vira
   **QUALIDADE DE IMAGEM** com destinos ("impressão fina", "grande formato", "tela").
2. **Todo controle é um cursor com dois extremos rotulados** — *maior economia / cores menos fiéis*
   ↔ *menor economia / cores mais fiéis*. O operador escolhe onde ficar; o produto não decide.
3. **Estimativa ANTES de aplicar.** Cada posição mostra o resultado previsto
   (`7,80 MB → ~6,2 MB, −20%`), e depois o resultado **real medido**. Nunca só depois.
4. **O que não existe no arquivo aparece desabilitado, dizendo que não se aplica.** No `kaneki.cdr`
   não há perfil ICC — o cursor de cor mostra "este arquivo não tem perfil embutido", em vez de
   prometer economia que não virá.
5. **Perda é sempre declarada no rótulo**, não em nota de rodapé.

## REQUIREMENTS (EARS)

- **R3.1 — WHEN** o operador otimiza, **THEN** o sistema SHALL criar uma cópia de segurança ANTES de
  qualquer mutação e SHALL abortar se a cópia falhar. _O2, P4_
- **R3.2 — WHEN** um perfil de configuração é escolhido, **THEN** o sistema SHALL exibir a economia
  **estimada por componente** antes de aplicar. _P1_
- **R3.3 — WHEN** a otimização termina, **THEN** o sistema SHALL exibir a economia **real medida no
  arquivo em disco**, comparada com a estimativa. _P1_
- **R3.4 — WHEN** o cursor de **FIDELIDADE DE COR** está em máxima economia, **THEN** o sistema SHALL
  salvar sem embutir perfil de cor, avisando que a cor impressa pode mudar. _P3_
- **R3.5 — WHEN** o arquivo NÃO tem perfil embutido, **THEN** o cursor de cor SHALL aparecer inativo
  com a explicação, e SHALL NOT prometer economia. _P1_
- **R3.6 — WHEN** o cursor de **QUALIDADE DE IMAGEM** define um destino, **THEN** o sistema SHALL
  tratá-lo como DPI efetivo alvo, nunca reamostrando imagem já abaixo dele. _O3_
- **R3.7 — WHEN** o operador escolhe um **ESPAÇO DE COR** diferente de "manter", **THEN** o sistema
  SHALL avisar que a conversão altera as cores e SHALL exigir confirmação explícita. _P3_
- **R3.8 — WHEN** a otimização roda, **THEN** o preview embutido SHALL ser removido no salvamento
  (ganho sem perda: 2,0% do `kaneki.cdr`, até 60% em arquivos do corpus). _P1_
- **R3.9 — WHEN** o documento é salvo, **THEN** o sistema SHALL chamar `ClearUndoList` e fechar/reabrir
  para reserializar limpo. _O2_
- **R3.10 — WHEN** existe payload órfão (dado de imagem sem objeto que o use), **THEN** a
  reserialização SHALL descartá-lo, e o ganho SHALL ser medido e reportado. _P1_
- **R3.11 — WHEN** todas as mutações ocorrem, **THEN** elas SHALL estar dentro de um par
  `BeginCommandGroup`/`EndCommandGroup` fechado mesmo em erro. _P4_
- **R3.12 — WHEN** o operador prefere não pensar, **THEN** SHALL existir um preset padrão seguro
  ("Equilibrado") que só aplica ganhos sem perda. _UX_

## DESIGN — os quatro cursores

| Cursor (rótulo do operador) | Posições | O que faz por baixo |
|---|---|---|
| **FIDELIDADE DE COR** | Máxima · Alta *(padrão)* · Econômica · Sem perfil | `EmbedICCProfile`: mantém / mantém só o espaço usado / substitui por perfil padrão pequeno / não embute |
| **QUALIDADE DE IMAGEM** | Não mexer *(padrão)* · Impressão fina 300 · Grande formato 200 · Tela 150 | alvo de **DPI efetivo** (Fase 5 aplica) |
| **PESO DO DESENHO** | Preservar detalhe 0,01 mm · Equilibrado 0,03 · Máxima fluidez 0,08 | tolerância de redução de nós (Fase 4 aplica) |
| **ESPAÇO DE COR** | Manter *(padrão)* · Tudo CMYK · Tudo RGB | conversão de cor — **exige confirmação** (R3.7) |

Mais três interruptores sem perda, **ligados por padrão**: remover preview, limpar camadas vazias,
reserializar (fecha/reabre). E um opt-in: remover objetos fora da página.

**Presets prontos:** *Seguro* (só sem perda) · *Equilibrado* (padrão) · *Máxima economia* (declara
todas as perdas). O operador pode partir de um preset e mover qualquer cursor.

### Alavancas verificadas na typelib (`IVGStructSaveAsOptions`, dump 6720)
`set_ThumbnailSize(cdrNoThumbnail=0)` · `set_IncludeCMXData(false)` · `set_EmbedICCProfile(bool)` ·
`set_EmbedVBAProject(false)` · `set_Version(cdrCurrentVersion=0)`. Instância por
`Application.CreateStructSaveAsOptions()` (3112). Backup por `Document.SaveAsCopy` (3975), salvamento
por `SaveAs` (4013).

### Pipeline
```
1. diagnóstico ANTES (Fase 2: composição exata + teto)
2. estimativa por cursor  → mostra ao operador, ele ajusta
3. backup  → SaveAsCopy("<nome>_backup_<ts>.cdr")   [aborta se falhar]
4. BeginCommandGroup
5. limpeza estrutural (camadas vazias; fora-da-página se autorizado)
6. EndCommandGroup + ClearUndoList
7. SaveAs com as opções derivadas dos cursores
8. fecha + reabre (reserialização limpa; derruba payload órfão)
9. diagnóstico DEPOIS + comparativo real vs estimado
```

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/Optimization/OptimizationSettings.cs` — **novo**: os quatro cursores + presets. _R3.4–R3.7, R3.12_
- `src/Optimus.Core/Optimization/SavingsEstimator.cs` — **novo**: estimativa por componente. _R3.2_
- `src/Optimus.Core/Optimization/CompositionDelta.cs` — **novo**: antes × depois (R2.6 diferido). _R3.3, R3.10_
- `src/Optimus.Core/Optimization/CleanupRules.cs` — **novo**: regras puras de limpeza. _R3.8_
- `src/Optimus.Interop/DocumentSaver.cs` — **novo**: backup, SaveAs com opções, fecha/reabre. _R3.1, R3.9_
- `src/Optimus.Interop/CommandGroupScope.cs` — **novo**: garante o par. _R3.11_
- `src/Optimus.AddIn/wwwroot/index.html` — os cursores e a estimativa viva.
- `tests/Optimus.Core.Tests/Optimization/` — presets, estimativa, delta.

## READY-MADE SOLUTIONS TO USE

`StructSaveAsOptions` inteiro (nada a implementar — só configurar), `Document.ClearUndoList`,
`SaveAsCopy`, `Application.OpenDocument`, e o `CdrContainerReader`/`ReductionCeiling` da Fase 2 para
estimar e conferir.

## TASKS

1. [ ] **Teste primeiro:** `OptimizationSettings` — cada preset produz a combinação esperada; mover um
   cursor não altera os outros; "Sem perfil" marca perda declarada. _R3.4, R3.12_
2. [ ] Implementar `OptimizationSettings` + presets (puro). _R3.4–R3.7, R3.12_
3. [ ] **Teste primeiro:** `SavingsEstimator` — num arquivo sem ICC a economia de cor é **0** e o
   cursor sai inativo; num arquivo 97% ICC, "sem perfil" estima ~97%. _R3.2, R3.5_
4. [ ] Implementar `SavingsEstimator` usando a composição real da Fase 2. _R3.2_
5. [ ] **Teste primeiro:** `CompositionDelta` — antes/depois por componente; delta negativo (arquivo
   cresceu) é reportado como tal, nunca como ganho. _R3.3_
6. [ ] Implementar `CompositionDelta`. _R3.3, R3.10_
7. [ ] **Teste primeiro:** `CleanupRules` — camada vazia removível; camada com formas não; forma
   totalmente fora da página é candidata, forma que cruza a borda não. _R3.8_
8. [ ] Implementar `CleanupRules`. _R3.8_
9. [ ] Implementar `CommandGroupScope` (fecha em exceção). _R3.11_
10. [ ] Implementar `DocumentSaver`: backup verificado, `SaveAs` com opções dos cursores,
    `ClearUndoList`, fecha/reabre. _R3.1, R3.9_
11. [ ] UI dos cursores com estimativa viva + rótulos de perda + preset padrão. _R3.2, R3.4–R3.7, R3.12_
12. [ ] **MEDIR nos arquivos reais** (`kaneki.cdr`, `arquivo.cdr`, `icc-heavy.cdr`): ganho isolado de
    (a) sem preview, (b) sem CMX, (c) sem ICC, (d) reserialização. Registrar em LESSONS. _R3.3, P1_
13. [x] **Investigar o payload órfão do `kaneki.cdr`** — RESPONDIDO offline, sem VM (só análise do ZIP).
    Veredito: **não é alavanca**. `Bitmaps.dat` tem 477 KB lógicos mas **85,2 KB no disco = 1,07% do
    arquivo**; mesmo 100% órfão, removê-lo é arredondamento. E não é órfão: o `kaneki_no_ponto.cdr`
    (mesma arte, molduras removidas) **não tem `Bitmaps.dat` nenhum** — o payload pertencia às
    molduras. O conteúdo não tem assinatura de imagem (`RIFF`/`JFIF`/`PNG`/`BMP`/`TIFF`); começa com
    registros de ponteiro de 16 bytes, o formato já documentado em O10. A hipótese de "ganho de graça"
    está morta. _R3.10_
14. [ ] **Teste de segurança:** falha no meio do pipeline → documento íntegro, command group fechado,
    backup presente. _R3.1, R3.11_

## DO NOT WANT

- Não usar jargão na UI (ICC, DPI, CMX, reserializar) sem tradução para o operador.
- Não prometer economia de um componente que o arquivo não tem (R3.5).
- Não remover perfil de cor por padrão — é trade-off, não limpeza (medido: 29/29 arquivos usam o
  perfil que embutem).
- Não converter espaço de cor sem confirmação explícita (R3.7).
- Não tocar em geometria nesta fase (Fase 4) nem em bitmap (Fase 5) — aqui só os cursores existem;
  quem aplica são as fases seguintes.
- Não salvar em versão antiga do `.cdr` para ganhar tamanho.
- Não confiar no Ctrl+Z: backup em arquivo é obrigatório (O2).

## VALIDATION

- `dotnet test` verde nas regras puras (presets, estimativa, delta, limpeza).
- Na VM: cada alavanca medida isoladamente nos três arquivos (task 12), com tabela no LESSONS.
- Arquivo otimizado abre normalmente e a arte é visualmente idêntica no preset padrão.
- Backup existe, abre, e é idêntico ao original (hash).
- Economia exibida == economia real do arquivo em disco.

## VERIFICATION GATE

- Rodar duas vezes: a segunda reduz ~0% (idempotência) e **nunca aumenta** (era o sintoma da v1.0).
- Num arquivo sem ICC, o cursor de cor tem de sair **inativo** — se prometer economia, falhou.
- A estimativa não pode ser maior que o ganho real medido; se for, o estimador é otimista e volta.
- Um operador leigo tem de conseguir explicar o que cada cursor faz só lendo os rótulos.

## TECHNICAL CONSTRAINTS

Toda mutação dentro do command group; estado restaurado em erro. `Optimus.Core` sem COM. Arquivos
< 500 linhas. Nenhuma dependência nova.

## LESSONS

**Implementada em 2026-07-25 — v1.4.0, 191 testes verdes** (eram 136 no fim da Fase 2).

### ⚠ A correção que mudou o valor do produto: a alavanca é POR ARQUIVO

Eu afirmei, com base na pesquisa, que reduzir nós rende **~2% de tamanho**. Aquele 4,6% foi medido num
arquivo **dominado por ICC**, onde a geometria era migalha. Medi o payload do `kaneki.cdr` de verdade
(`ObjectPayloadAnalyzer`: segue os ponteiros de 16 bytes, separa argumentos do `loda` por tipo e
**comprime cada grupo isoladamente**):

| Componente do payload | Lógico | **Comprimido (custo real)** | % |
|---|---|---|---|
| **Geometria** (`0x1e`) | 10,1 MB | **5,99 MB** | **97,3%** |
| Estilo JSON duplicado (`0xc9`) | **12,0 MB** | **125 KB** | 2,1% |
| Outros | 0,3 MB | 36 KB | 0,6% |

*16.948 objetos, 0 malformados. O arquivo enxuto (`kaneki_no_ponto.cdr`) dá 97,4% / 2,0%.*

**Duas conclusões medidas:**
1. **O estilo JSON duplicado NÃO é alavanca.** 12 MB lógicos (mais que a geometria!) viram **125 KB** —
   são ~30 textos distintos repetidos 16.948 vezes e o deflate resolve. Vender "metade do arquivo é
   lixo repetido" seria mentira: metade do lógico, 2% do real.
2. **A geometria é ~73% do arquivo inteiro** → reduzir nós vale **dezenas de por cento** aqui, não 2%.

O `4,6%` estava **chumbado** em `ReductionCeiling` e `SavingsEstimator`, subestimando a alavanca ~15×.
Ambas passaram a receber a **geometria medida**; o fallback está documentado como não-confiável.
Decisão **O8 revisada**: *quanto cada alavanca vale é por arquivo, nunca constante.*

### Comparação dos dois arquivos do operador
Remover as molduras externas mudou o `data1.dat` em **17 bytes** (7.276.436 → 7.276.419): o peso é a
**arte**, não a moldura. O que saiu foi `Bitmaps.dat` (87 KB) e uma fonte embutida (99 KB).

### Entregue
`OptimizationSettings` (4 cursores + 3 presets), `SavingsEstimator`, `CompositionDelta`,
`CleanupRules`, `RiffPointerReader`, `ObjectPayloadAnalyzer`, `CommandGroupScope`, `DocumentSaver`
(backup verificado que **aborta** se falhar, `SaveAs` com opções, `ClearUndoList`, fecha/reabre),
`FileOptimizer.Apply` com o pipeline completo, e a UI com cursores + estimativa viva.

### Bug de desenho que um teste pegou
O preset **Seguro** ainda simplificava curvas (tolerância 0,01 mm) — promessa falsa. Foi criada a
posição **"Não mexer"** no cursor de desenho; Seguro agora é literalmente nenhuma alteração na arte.

### O "payload órfão" do kaneki.cdr — encerrado sem VM (task 13)
A pergunta era: `Bitmaps.dat` com 477 KB lógicos e **0 formas de bitmap** seria lixo que a
reserialização derruba de graça? Respondida com análise do ZIP, sem CorelDRAW:

- **1,07% do arquivo.** 477 KB são o tamanho LÓGICO; no disco o entry ocupa **85,2 KB** de 7,8 MB.
  Mesmo se fosse 100% órfão, remover é arredondamento — não é alavanca.
- **E não é órfão.** O `kaneki_no_ponto.cdr` (mesma arte, molduras removidas pelo Davi) **não tem
  `Bitmaps.dat` nenhum**. O payload pertencia às molduras, não estava solto. No mesmo par de arquivos,
  `embed/` cai de 121,9 KB (1,53%) para 12,5 KB (0,16%) — as molduras carregavam objetos embutidos.
- **Não contém imagem reconhecível:** nenhuma assinatura `RIFF`/`JFIF`/`PNG`/`BMP`/`GIF8`/`TIFF`.
  Começa com registros de ponteiro de 16 bytes — o mesmo formato de indireção documentado em O10.

Lição: **"lógico" e "no disco" são números diferentes, e só o segundo paga a promessa.** O `.cdr` é um
ZIP; um entry de 477 KB que deflaciona para 85 KB nunca vai render 6% de economia. A checagem custou
uma leitura de ZIP e evitou gastar sessão de VM para confirmar ~1%.

### Onde estão os 7,96 MB do kaneki_no_ponto.cdr — e por que 50% não sai daqui

Medido no arquivo, sem CorelDRAW (ZIP + `ObjectPayloadAnalyzer`, deflacionando cada grupo de argumentos
separadamente):

| Componente | No disco | % do arquivo |
|---|---:|---:|
| **Geometria** (args `0x1e`) | 5.970.608 | **75,0%** |
| Estrutura do `data1.dat` fora dos args | 1.195.635 | 15,0% |
| `root.dat` | 429.204 | 5,4% |
| Previews PNG (STORED) | 182.135 | 2,3% |
| JSON de estilo por objeto (`0xc9`) | 74.650 | 0,9% |
| Demais args de objeto | 35.526 | 0,4% |

**A aritmética que decide a meta de 50%:** 5.970.608 bytes de geometria ÷ 421.057 nós =
**14,18 bytes por nó no disco**. Logo:

| Alvo de redução | Nós a remover | % de todos os nós |
|---:|---:|---:|
| 14,3% (o que saiu a 0,03 mm) | 80.266 | 19,1% |
| 20% | 112.260 | 26,7% |
| 30% | 168.390 | 40,0% |
| 40% | 224.520 | 53,3% |
| **50%** | **280.650** | **66,7%** |

Ou seja: **50% de arquivo exige apagar dois terços de todos os nós do desenho.** Não é questão de
achar uma alavanca escondida — é o que os bytes dizem. O JSON de estilo (12,5 MB lógicos!) deflaciona
para 0,9%, confirmando O8 pela terceira vez; previews somam 2,3%; não há ICC nem imagem.

**A alavanca ainda não tentada, e a única que ataca os dois objetivos:** os 15% de estrutura são
**70,8 bytes por objeto** × 16.899 objetos. Reduzir a CONTAGEM de objetos (combinar/soldar curvas de
mesmo estilo, remover sobrepostos e duplicados) mexe nesses 15% **e** é o que o operador sente ao
arrastar — cortar nós encolhe o arquivo mas não mudou a sensação (Fase 4 LESSONS).

### A DESCOBERTA — a alavanca real é REPETIÇÃO, e a nossa otimização a destrói (2026-07-25)

Medido com censo próprio de repetição (`ObjectPayloadAnalyzer`, hash FNV-1a por bloco de coordenadas,
deflate real de cada conjunto — método verificado reproduzindo o número já conhecido do JSON de estilo):

| Arquivo | Objetos | Geometrias **distintas** | Repetidas | Economia possível |
|---|---:|---:|---:|---:|
| `kaneki_no_ponto.cdr` | 16.914 | **4.789** | 71,4% | **4.628.466 B = 58,2% do arquivo** |
| `kaneki.cdr` | 16.948 | 4.808 | 71,3% | 4.626.199 B = 56,6% |
| `kaneki_no_ponto.cdr` **depois da nossa otimização** | 16.911 | **5.400** | 67,8% | 1.612.816 B = **23,7%** |

**16.914 objetos guardam apenas 4.789 desenhos diferentes.** O resto são cópias byte a byte, colocadas
por matrizes de transformação distintas — é *step-and-repeat*, arte legítima repetida, não lixo.

Por que o deflate não resolve sozinho: a janela dele é **32 KB** e o `data1.dat` tem **28 MB**; as
cópias estão a megabytes de distância. É o oposto exato do JSON de estilo (O8), cuja repetição é local
e o zlib come de graça. Aqui a repetição é remota e sobrevive à compressão.

Sanidade: bloco distinto médio de **499 bytes** contra 620 da média geral — se as "repetições" fossem
blocos vazios, o distinto médio seria MAIOR, não menor.

**E o mais grave: a nossa própria simplificação queimou a alavanca.** Ao rodar `AutoReduce` shape a
shape, cópias idênticas recebem resultados ligeiramente diferentes e **deixam de ser idênticas**:
4.789 → 5.400 geometrias distintas, e a economia recuperável cai de **58,2% para 23,7%**. Trocamos
uma alavanca de 58% sem perda por uma de 14% com perda.

**A ordem passa a ser inegociável:** detectar repetição → instanciar (símbolo) → só então simplificar.
Nunca o contrário. Travado em O15 e em `RepeatedGeometryTests`.

### Estado da arte: ninguém detecta arte repetida — só duplicata empilhada

Levantamento das macros públicas da comunidade CorelDRAW (2026-07-25). Todas comparam **posição
absoluta** (`PositionX`/`PositionY`) mais tamanho:

| Macro | Chaves comparadas |
|---|---|
| `removeUnderlyingDups` (wOxxOm) | posição + tamanho + contagem de nós, `Jitter = 0.0001` |
| AshishKumar004 | posição + tamanho apenas (o autor documenta falso positivo: círculo e retângulo de mesmo tamanho e posição) |
| Plixo | `Curve.Area` com igualdade exata de float |
| TakoiNeTakoi | tipo + preenchimento + contorno, **zero geometria** |
| `Shape.CompareTo` (oficial) | bitmask de 8 membros, **nenhum geométrico** |

**Nenhuma macro publicada compara coordenada de nó. E, por serem ancoradas em posição ABSOLUTA,
nenhuma delas enxerga a mesma forma colocada em lugares diferentes.** O problema que a comunidade
resolve é "importei um CAD e ficaram linhas dobradas em cima uma da outra".

O nosso é outro: *step-and-repeat*, a mesma arte em N posições. Não há arte anterior para copiar — a
normalização por translação é nossa. Isso é diferencial de produto, não só detalhe técnico.

**A CQL também não tem token de geometria** (`@name @type @width @height @left @right @top @bottom
@centerX @centerY @fill @outline @colors @com` — e nada de `@nodes`/`@length`/`@area`). O escape é
`@com`, que reentra no COM: ganha expressividade, não velocidade.

### Duas descobertas técnicas guardadas para quando forem necessárias

- **Quantização em décimo de mícron.** A codificação canônica de geometria (Vakulenko, 2005) usa
  `Document.FromUnits(coord, cdrTenthMicron)` — o **inteiro nativo do documento**. Isso elimina o
  problema de comparar float: a quantização é exata, não é tolerância chutada. Verificado no dump:
  `cdrTenthMicron = 0` (1521), `FromUnits` (4105). Só serve se `GetCurveInfo` marshalar, o que não
  acontece no Corel 2024 desta VM.
- **`Shape.DisplayCurve`** (dump 5997) devolve a curva de QUALQUER tipo de forma — retângulo, elipse,
  polígono — **sem converter em curvas**, ou seja sem alterar o documento. É o caminho certo para
  impressão digital de não-curvas; as 2.523 elipses deste arquivo caem nessa categoria.

### PENDENTE — validação na VM (task 12)
Medir o ganho isolado de cada alavanca nos três arquivos reais (sem preview, sem CMX, sem ICC,
reserialização). Só o CorelDRAW pode aplicar.

## COVERAGE

R3.1 → 10,14 · R3.2 → 3,4,11 · R3.3 → 5,6,12 · R3.4 → 1,2,11 · R3.5 → 3,11 · R3.6 → 1,2 ·
R3.7 → 1,2,11 · R3.8 → 7,8 · R3.9 → 10 · R3.10 → 6,13 · R3.11 → 9,14 · R3.12 → 1,2,11
