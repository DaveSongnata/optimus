# Optimus — briefing de contexto (para colar em outra IA)

> Documento de contexto do projeto **Optimus**. Escrito para ser colado inteiro no início de uma
> conversa com outro modelo, de modo que ele pare de sugerir coisas que já foram medidas e
> descartadas. Última atualização: **2026-08-19**, versão do produto **v1.40.0**.
> Tudo que aparece como "medido" foi medido em arquivos reais de cliente, não estimado.

---

## 1. O que é o produto

**Optimus** = add-in do CorelDRAW (painel/docker dentro do Corel) + um app desktop companheiro.
Ele faz quatro coisas:

1. **Reduz o peso de arquivos `.cdr`** (o pedido original do cliente: "quero 50%").
2. **Deixa o arquivo fluido** para trabalhar (não travar ao arrastar objeto) — objetivo **separado**
   do anterior, com alavancas diferentes.
3. **Audita cores, fontes e cobertura de acentos** (PT/ES: á, ã, ç, ñ…).
4. **Otimiza o Windows** da máquina da gráfica (app separado, manual).

**Público:** confecções pequenas e médias, estampas de camisa / interclasse / futebol de várzea.
Operador de CorelDRAW, **não** é técnico. Arquivos frequentemente baixados da internet, cheios de
arte repetida, PowerClips e milhares de objetos.

**Filosofia do produto (regra dura):** o Optimus é um **remap**, não um botão mágico.
Tudo é ajustável, o operador escolhe o trade-off, cada perda é declarada **no rótulo** do controle
(nunca em nota de rodapé), e nenhuma alavanca que o arquivo não tem é oferecida — ela aparece
**desabilitada com o motivo**. Nada de jargão na UI: "ICC" vira **FIDELIDADE DE COR**, "DPI" vira
**QUALIDADE DE IMAGEM** com destinos (impressão fina / grande formato / tela).

**Princípios invioláveis:**
- **P1 — nunca mentir um número.** Percentual exibido é medido em disco; estimativa é rotulada como
  estimativa e nunca é a manchete.
- **P2 — nada de placebo.** Operação sem benefício medido não entra, mesmo se pedida.
- **P3 — nunca degradar a arte sem consentimento explícito.** Toda perda é opt-in, por arquivo.
- **P4 — nada pode derrubar o CorelDRAW nem corromper o documento.**
- **P5 — TDD**, arquivos < 500 linhas. **P6 —** constante do Corel só entra com evidência na typelib.

---

## 2. Stack e arquitetura

| Camada | Projeto | Target | Papel |
|---|---|---|---|
| Lógica pura | `src/Optimus.Core` | `netstandard2.0` | geometria, estimadores, regras, i18n, leitura do container `.cdr`. **Sem COM, sem I/O.** |
| COM do Corel | `src/Optimus.Interop` | `net48` | tudo que fala com o CorelDRAW, late-bound (`dynamic`) |
| Win32/WMI | `src/Optimus.Windows` | `net48` | manutenção do Windows, fontes, voz offline |
| Casca do add-in | `src/Optimus.AddIn` | `net48` | docker WebView2 + ponte JS↔C# |
| App de manutenção | `src/Optimus.Maintenance` | `net48` | app desktop separado |
| Testes | `tests/Optimus.Core.Tests` | `net8.0` + xUnit | **298 testes**, 29 arquivos |

**Linguagem: C#.** Não é VBA, não é C++, não é macro. Add-in COM registrado como addon do Corel
(`<Corel>\Programs64\Addons\Optimus\`), UI em **WebView2 + HTML 100% offline** (nunca CDN, nunca
webfont — a gráfica pode estar sem internet). Instalador é **um único EXE** (ILRepack).

**Regra de ouro da arquitetura:** algoritmo vive em `netstandard2.0` e é testado sem CorelDRAW;
as camadas COM/Win32 são deliberadamente burras (sem regra de negócio).

Idiomas: código e comentários em inglês; UI em **pt-BR + PT/ES/EN**; conversa com o dono em pt-BR.

---

## 3. Fatos medidos sobre o formato `.cdr` (não repetir suposições)

`.cdr` do X6 em diante é um **ZIP**. Eras: ≤X3 = RIFF puro; X4/X5 = ZIP com `content/riffData.cdr`;
**X6+ = ZIP com `content/root.dat` + `content/data/*.dat`**. Todos os arquivos do cliente são X6+.
(Referência pública: especificação Kaitai `coreldraw_cdr.ksy` e a libcdr do LibreOffice.)

**Composição real (medida hoje, entradas do ZIP, 4 arquivos reais):**

| Arquivo | Entrada dominante | Comprimido | Cru | % do arquivo |
|---|---|---|---|---|
| `arquivo.cdr` (4,53 MB) | `content/data/Bitmaps.dat` | 2.858.062 B | **88.566.362 B** | **63,1%** |
| | `content/data/data1.dat` (vetor) | 1.443.333 B | 2.309.126 B | 31,9% |
| | `previews/thumbnail.png` (**STORED**) | 77.208 B | 77.208 B | 1,7% |
| `arquivo2.cdr` (7,91 MB) | `content/data/data1.dat` (vetor) | 5.743.972 B | 22.008.614 B | **72,6%** |
| | `content/data/page1.dat` | 1.465.137 B | 5.938.439 B | 18,5% |
| | `embed/embedding0` (fonte) | 111.864 B | 209.675 B | 1,4% |
| `icc-heavy.cdr` (1,41 MB) | `color/profiles/cmyk/isocoated_v2_eci.icc` | 1.367.132 B | 1.829.077 B | **97,3%** |
| `kaneki.cdr` (8,18 MB) | `content/data/data1.dat` (vetor) | 7.276.436 B | 27.980.848 B | **89,0%** |
| | `content/root.dat` | 430.375 B | 3.463.320 B | 5,3% |
| | `previews/*.png` (**STORED**) | 163.318 B | — | 2,0% |
| | `content/data/Bitmaps.dat` | 87.285 B | 477.508 B | 1,1% |

**Leituras obrigatórias dessa tabela:**

- **Não existe "o que pesa num CDR". Depende do arquivo.** O mesmo produto vê 63% de raster em um
  arquivo, 89% de vetor em outro e 97,3% de perfil ICC num terceiro. Por isso o Optimus **mede
  antes** e informa o teto real, em vez de prometer 50%.
- **Peso é o tamanho COMPRIMIDO, nunca o lógico.** `Bitmaps.dat` do `kaneki.cdr` tem 477 KB lógicos
  mas **85 KB no ZIP = 1,07%** — quotar o lógico teria prometido ~6% e entregado 1%. Mesmo caso do
  JSON de estilos por objeto: **12 MB lógicos que deflatam para 125 KB (2,1%)**, porque são poucas
  strings distintas repetidas milhares de vezes.
- **Raster é guardado praticamente CRU dentro do container.** 88,5 MB → 2,86 MB só pelo deflate do
  ZIP (taxa 3,2%). Ou seja: o Corel não aplica compressão de imagem forte; quem comprime é o ZIP.
  Consequência: **reamostrar/reduzir profundidade de cor tem efeito direto e grande** naquele arquivo.
- **Previews são STORED** (taxa 100%, incompressíveis). Removê-los é ganho **grátis e imediato**
  (1,7% a 2,0% dos arquivos medidos).
- **Estilos e paleta do documento NÃO são alavanca:** `styles/document.cdss` = 2.002–2.422 bytes e
  `color/docPalette.xml` = 287–1.715 bytes, ou seja **~0,03% do arquivo**. Qualquer proposta de
  "expurgar estilos/paletas fantasmas para reduzir tamanho" está errada em ordem de grandeza.
- **Recompactar o ZIP não vale nada.** Medido hoje: reescrevendo todas as entradas com Deflate
  Optimal, `kaneki.cdr` 8.177.769 → 8.176.605 B e `arquivo.cdr` 4.529.421 → 4.527.544 B, ou seja
  **0,0%**. (E trocar por LZMA/BZIP2 produziria um ZIP que o CorelDRAW não abre.)

**Custo de um nó (verificado no formato):** 9 bytes (2 × int32 + 1 byte de tipo), coordenadas em
1/254000 pol. Um segmento Bézier = 3 nós = 27 B. As coordenadas são ~4,6% dos bytes do documento.

---

## 4. O que cada alavanca vale (medido) — e o veredito

| Alavanca | Tamanho | Fluidez | Veredito |
|---|---|---|---|
| **Instanciar arte repetida (símbolos)** | **até 58,2%, LOSSLESS** | alto | **a maior alavanca real** |
| Perfil ICC embutido | até 97,3% | nenhum | enorme, mas exige consentimento (cor) |
| Fontes embutidas (`embed/*`) | até 93% em alguns arquivos | nenhum | opt-in |
| Bitmaps / reamostragem por DPI efetivo | até ~60% em arquivo raster | médio | opt-in por arquivo |
| Preview/thumbnail | 1,7–2,0% | nenhum | grátis, ligado por padrão |
| Dados CMX de compatibilidade | pequeno | nenhum | grátis, ligado por padrão |
| **Redução de nós** | **~2% do arquivo** (medido: −31% de nós ⇒ −14,3% de bytes num caso extremo) | **principal** | é alavanca de **FLUIDEZ**, não de tamanho |
| Nº de objetos | baixo | alto | fluidez |
| Efeitos vivos (sombra, transparência, blend, contorno, envelope) | baixo | **alto** (recalculados a cada redesenho) | fluidez, opt-in (destrói editabilidade) |
| Recompactar o ZIP | **0,0%** | nenhum | **não fazer** |
| Expurgar estilos/paletas | ~0,03% | nenhum | **não fazer por tamanho** |

### A descoberta mais importante do projeto (regra O15)

No `kaneki_no_ponto.cdr`: **16.914 objetos armazenam apenas 4.789 blocos de coordenadas distintos** —
**71,4% dos shapes são cópias byte-idênticas** colocadas por transformações diferentes.
Guardar cada bloco uma vez economiza **4.628.466 bytes = 58,2% do arquivo, sem perda nenhuma**.

O Deflate **não** enxerga isso: a janela dele é 32 KB e o `data1.dat` tem 28 MB — as cópias ficam
megabytes umas das outras. (É o oposto do JSON de estilos, cuja repetição é local e sai de graça.)

**E a ordem não é negociável:** rodar o AutoReduce antes elevou os blocos distintos de 4.789 para
**5.400** e derrubou o recuperável de 58,2% para **23,7%** — simplificar cada shape isoladamente faz
cópias idênticas deixarem de ser idênticas.
**Sempre: detectar repetição → instanciar como símbolo → só então simplificar.** Nunca o inverso.

### Erros de estimativa já pagos

- **Remover N% dos nós não reduz N% do arquivo.** Medido: nós −31% ⇒ bytes −14,3%, contra uma
  estimativa de 24,8%. Coordenadas simplificadas comprimem um pouco pior por byte. O estimador
  carrega um fator `DeflateRecovery = 0,55` (deliberadamente abaixo do 0,63 medido) porque
  **prometer demais é mentira, prometer de menos é só decepção**.
- **`Window.Refresh()` não pode ser cronometrado** — ele só POSTA um repaint e retorna (medido
  0,1 ms antes e 0 ms depois de remover 130.000 nós). Fluidez se mede com render síncrono
  (`Document.ExportBitmap` + `ExportFilter.Finish()`), e se chama "tempo para desenhar".

---

## 5. Armadilhas de COM do CorelDRAW já pagas (não repetir)

Fonte autoritativa da API: `docs/vgcore-tlb-dump.txt` (dump da typelib VGCore 25.2, 8.454 linhas).
**Nunca chutar constante ou membro do VGCore** — se não está no dump, não existe.

- `cdrMillimeter = 3`. `4` é `cdrCentimeter`. Trocar isso torna as tolerâncias **10× mais agressivas**
  que o rótulo (bug histórico, reintroduzido na v1.0).
- **`Shape.PowerClip.Shapes` precisa ser percorrido explicitamente.** `Page.FindShapes(Recursive:=True)`
  não desce dentro de PowerClip. Arquivo de design é cheio deles — ignorar isso é exatamente o motivo
  de a v1.0 ter conseguido **0%** nos arquivos reais.
- **`FindShapes` com filtro de tipo não é fonte de verdade:** somar os 27 valores do enum deu 6.118
  shapes onde a chamada sem filtro deu 16.899 na mesma página. Enumerar UMA vez e ler `.Type`.
- **Custo de COM é por objeto TOCADO, não por chamada:** de uma varredura de 174 s, **110 s foram
  limpeza de RCW** (o `Release` em STA roda na thread dona e a bloqueia). Sempre
  `Marshal.ReleaseComObject` em shapes, `Curve`, `Nodes` e ranges.
- **Edição em massa TEM que rodar dentro de `Application.Optimization = true` + `EventsEnabled = false`.**
  Sem isso o Corel repinta e dispara evento a cada edição: simplificar 14.342 curvas levou **13 min 22 s**.
  Restaurar os dois num `finally` (sessão deixada com `Optimization = true` parece travada).
- **`Fill.UniformColor` é referência VIVA no documento** — `ConvertToRGB()` muta a arte. Sempre
  `Color.GetCopy()` antes de inspecionar. E **nunca pintar swatch a partir de `Color.HexValue`**
  (formato indocumentado; renderizou 128 swatches brancos).
- **Chamada late-bound que OMITE parâmetro opcional final pode falhar** com "Could not convert
  argument 0" — medido em `document.SaveAsCopy(path)`. Passar todos os parâmetros ou usar
  `InvokeMember`. (Hoje o backup é um `File.Copy` puro, que é byte-idêntico ao que o operador tem.)
- **Para LISTAR as cores, nunca varrer os shapes** (~25 propriedades late-bound por shape = 420.000
  chamadas COM e **90 s** num arquivo de 16.899 shapes). Usar `Document.AddColorsToDocPalette()` +
  `Document.Palette`: o Corel faz a varredura no código nativo dele e devolve ~128 cores. Trade-off
  declarado: a paleta sabe QUAIS cores existem, não QUANTO cada uma é usada, e superestima.
- Parear `BeginCommandGroup`/`EndCommandGroup`; restaurar unidade e view em erro.

### Armadilhas da UI (WebView2 dentro do Corel)

- JS→C#: ler `e.WebMessageAsJson`; `TryGetWebMessageAsString()` lança exceção.
- C#→JS: `ExecuteScriptAsync("window.optimusReceive(...)")`; o canal de evento `message` é
  não-confiável dentro do host WPF do Corel.
- **Um erro de JS no docker é INVISÍVEL e custou semanas:** `esc()` era chamada 34 vezes e definida
  zero vezes; toda função de render morria em silêncio no primeiro `esc()`. O operador relatou
  "clico em auditar e não acontece nada" — literalmente verdade: o C# rodava a auditoria de 90 s
  inteira e a função que ia desenhar o resultado quebrava na linha 1. **Os 414 testes unitários
  ficaram verdes o tempo todo, porque nenhum deles enxerga JavaScript.** Hoje existem três defesas:
  `window.onerror` reportando ao host, `scripts/check-ui-js.js` quebrando o build, e o build rodando
  esse check ANTES de compilar.
- **Um `WebView2` coletado pelo GC em vez de `Dispose()`d derruba o processo inteiro** do CorelDRAW
  (`E_NOINTERFACE` no finalizador). Existe um backstop em `ProcessExit`.
- Nada pode derrubar o Corel: todo handler é embrulhado, falha vira linha em `%TEMP%\Optimus\docker.log`.

---

## 6. Restrições de licenciamento (produto comercial de código fechado)

- Só dependência **permissiva** (MIT/Apache-2.0/BSD/zlib/domínio público); `THIRD-PARTY-NOTICES.txt`
  é obrigatório.
- **Nunca embarcar:** BleachBit (GPLv3), qualquer binário Sysinternals (a EULA proíbe
  redistribuição), dados Pantone (há histórico de DMCA — o certo é ecoar o nome que vem do próprio
  arquivo do cliente via `Color.SpotColorName`).
- Winapp2.ini é CC BY-SA 4.0: pode ser embarcado verbatim com atribuição, mas adaptar contamina o
  arquivo de regras. Decisão: escrever nossas próprias regras (localização de arquivo é fato, não
  obra).
- Preferir facilidades in-box do Windows (cleanmgr, DISM, WMI, Restart Manager, `defrag /O`).
- **Voz roda 100% offline** (whisper.cpp local, modelo ggml embarcado no instalador). Um desenho de
  API na nuvem foi construído e rejeitado na hora: "eu quero que funcione offline... não é para usar
  API". Nunca propor serviço de nuvem para nada voltado ao usuário no Optimus.

---

## 7. Módulo de manutenção do Windows — o que foi rejeitado por evidência

- **"Limpeza de RAM" (EmptyWorkingSet, purge da standby list) deixa a máquina MAIS LENTA** e não é
  entregue. Limpar Prefetch tem valor negativo e não é entregue. A mesma régua reprovou **desabilitar
  serviços do Windows**, **cirurgia em pagefile** e **boost de prioridade de processo**.
- O módulo é um **otimizador**, não um "cleaner": a primeira aba é **Desempenho** (plano de energia,
  animações, transparência, atraso de menu, exclusão das pastas de arte no antivírus e no índice de
  busca). Cada item lê o estado ATUAL, só é aplicado quando pedido, declara o que se perde e é
  **reversível na mesma tela**.
- **Nunca aplicar o preset "melhor desempenho" do Windows** — ele mata ClearType e miniaturas, o que
  para um designer é dano, não otimização.
- Rodando elevado, `%TEMP%` resolve para o perfil do **admin**. Enumerar perfis reais via
  `ProfileList\<SID>\ProfileImagePath`. Age-gate de 72 h antes de apagar temporário.
- Auditoria de inicialização tem **cinco superfícies**, e a quinta não é opcional: Run HKCU + Run HKLM
  + Wow6432Node + pastas Startup + **Agendador de Tarefas** (medido: 11 itens nas quatro clássicas,
  **mais 4 só no agendador**). **Desabilitar, nunca apagar**, pela codificação do próprio Windows.

---

## 8. Estado atual

v1.40.0. Fases 1–7 concluídas, fase 8 (i18n/empacotamento/validação final) em andamento.
Já existem em código: leitor do container ZIP do `.cdr`, classificador de entradas, DPI efetivo,
detector de repetição (`RepetitionFinder`) e conversor para símbolos (`SymbolConverter`), estimador
de economia calibrado, redução de nós nativa + fitting de Bézier próprio, auditoria de cor/fonte/
glifo, medidor de tempo de render, app de manutenção completo, i18n PT/ES/EN, comando de voz offline.

---

## 9. Onde ainda queremos ideias

1. **Bitmap recortado**: pixels que ficam fora da moldura de PowerClip / fora do `CropEnvelope`
   continuam armazenados. Vale um passe destrutivo consentido (`Bitmap.CropEnvelope` + `Bitmap.Crop()`
   existem na typelib) — quanto isso vale nos arquivos de estampa reais?
2. **Reduzir profundidade de cor de raster** (estampa costuma ser poucas cores chapadas): `ConvertToPaletted2`
   pode valer mais que reamostrar, sem perder nitidez de borda. Falta medir.
3. **Reduzir a CONTAGEM de objetos** (combinar curvas de mesmo preenchimento) — `content/root.dat` tem
   3,46 MB lógicos no `kaneki.cdr` e cresce com o número de objetos. Falta medir o ganho real.
4. Detectar **cópias quase-idênticas** (mesma arte escalada/espelhada) para ampliar a alavanca de
   símbolos além do byte-idêntico, sem trocar arte por arte diferente.
5. Qualquer alavanca nova precisa vir com: **em que arquivo real ela foi medida** e **quanto ela vale
   em bytes comprimidos**. Sugestão sem número medido não entra no produto (P1/P2).
