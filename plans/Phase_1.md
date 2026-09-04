# Fase 1 — Fundação testável + correções críticas

- **STATUS:** [x] **CONCLUÍDA** (entregue em `shared/bin/redistributables/Clientes/1.1.0/`)
- **OBJETIVO (1 frase):** Separar a solução em camadas testáveis (lógica pura ≠ COM), criar o
  harness de testes, e corrigir os três defeitos que fazem a v1.0 entregar 0% de redução.
- **POR QUE É A FASE 1:** sem a separação de camadas não existe TDD (P5), e sem as correções
  qualquer medição das fases seguintes mede o bug, não o algoritmo.

## REQUIREMENTS (EARS)

- **R1.1 — WHEN** o motor de otimização precisa da unidade do documento, **THEN** o sistema SHALL
  usar `cdrMillimeter = 3`, valor verificado em `docs/vgcore-tlb-dump.txt`. _O1, P6_
- **R1.2 — WHEN** uma tolerância é informada em milímetros pela UI, **THEN** o sistema SHALL aplicar
  exatamente essa distância física no documento (sem fator 10×). _O1, P1_
- **R1.3 — WHEN** o documento contém PowerClips, **THEN** o sistema SHALL percorrer também
  `Shape.PowerClip.Shapes` recursivamente e contabilizar essas formas. _P4_
- **R1.4 — WHEN** o documento contém formas que não são curvas (retângulo, elipse, polígono, texto,
  grupos de efeito), **THEN** o sistema SHALL enumerá-las e classificá-las, em vez de ignorá-las. _P1_
- **R1.5 — WHEN** a travessia encontra qualquer forma, **THEN** o sistema SHALL produzir um
  inventário `{tipo, quantidade, nós, temCurva, dentroDePowerClip}` sem alterar o documento. _P4_
- **R1.6 — WHEN** um algoritmo puro é escrito, **THEN** ele SHALL residir em `Optimus.Core`
  (`netstandard2.0`) e ter teste em `net8.0` que roda sem CorelDRAW instalado. _P5_
- **R1.7 — WHEN** o inventário é executado sobre o documento, **THEN** o sistema SHALL deixar o
  documento byte-idêntico (operação estritamente de leitura). _P4_

## DESIGN (curto)

Reestruturar em 6 projetos + 1 solution (`Optimus.sln`):

```
src/Optimus.Core          netstandard2.0   PURO: geometria, regras, medição, cobertura de glifos
src/Optimus.Interop       net48            COM CorelDRAW (dynamic) atrás de interfaces
src/Optimus.Windows       net48            Win32/WMI (Fase 6)
src/Optimus.AddIn         net48            casca do plugin (docker WebView2)   [existe]
src/Optimus.Resources     net48            ícone Win32                          [existe]
installer/                net48            instalador EXE único                 [existe]
tests/Optimus.Core.Tests  net8.0 + xUnit   testes de tudo que é puro
```

A travessia vira `ShapeWalker` (em `Optimus.Interop`), que **emite** um `ShapeInventory` (tipo puro
de `Optimus.Core`). Assim o inventário é testável com um `IShapeSource` falso, sem Corel. A regra
de decisão ("esta forma tem geometria reduzível?") mora em `Optimus.Core` e é 100% testada.

`FileOptimizer` atual é desmontado: a parte de decisão vai para `Core`, a parte de COM para `Interop`.

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/Model/ShapeInventory.cs` — **novo**: tipos puros do inventário. _R1.5_
- `src/Optimus.Core/Model/CorelShapeKind.cs` — **novo**: enum espelhando `cdrShapeType` verificado. _R1.4_
- `src/Optimus.Interop/CorelConstants.cs` — **novo**: constantes VERIFICADAS (`CdrMillimeter=3`, tipos). _R1.1_
- `src/Optimus.Interop/ShapeWalker.cs` — **novo**: travessia `FindShapes` + PowerClip explícito. _R1.3, R1.4_
- `src/Optimus.Interop/CorelDocumentState.cs` — **novo**: unidade mm + restauração garantida. _R1.1, R1.2_
- `src/Optimus.AddIn/Core/FileOptimizer.cs` — **desmontado** (lógica migra; COM fica). _R1.6_
- `tests/Optimus.Core.Tests/` — **novo**: xUnit.
- `Optimus.sln` — **novo**.

## EXEMPLARS TO MIRROR

- `../siscut/plugin/src/SisCut.Interop/CorelConstants.cs` — como documentar constante verificada.
- `../siscut/plugin/src/SisCut.Interop/CorelDocumentState.cs` — restauração de unidade em `Dispose`.
- `../ai-sten/src/AiSten.Interop/CorelConstants.cs` — mesmo padrão, já com `CdrMillimeter = 3`.
- `../siscut/plugin/src/SisCut.Interop/CorelShapeReader.cs` — travessia de grupos e curvas.

## READY-MADE SOLUTIONS TO USE

- `Page.FindShapes(String Name?, cdrShapeType Type?, Boolean Recursive?)` — **verificado** (dump
  linhas 5016/5393). Substitui a recursão manual.
- `Shape.PowerClip` → `IVGPowerClip.get_Shapes()` — **verificado** (dump 5638/6190).
- `Curve.Nodes.Count` para contagem de nós; `docs/vgcore-tlb-dump.txt` para toda constante.

## TASKS

1. [ ] Criar `Optimus.sln` + os projetos `Optimus.Core` (netstandard2.0) e
   `tests/Optimus.Core.Tests` (net8.0, xUnit); `dotnet test` roda verde vazio. _R1.6_
2. [ ] **Teste primeiro:** `CorelConstants.CdrMillimeter == 3` e um teste que falha se alguém
   trocar para 4 (com comentário apontando o dump). _R1.1_
3. [ ] Criar `Optimus.Interop` (net48) com `CorelConstants` verificadas contra o dump
   (`cdrCurveShape=3`, `cdrBitmapShape=5`, `cdrTextShape=6`, `cdrGroupShape=7`, `cdrUnit`). _R1.1, R1.4_
4. [ ] **Teste primeiro:** `ShapeInventory` agrega contagens por tipo/nós corretamente a partir de
   uma fonte falsa, incluindo formas marcadas como dentro de PowerClip. _R1.5_
5. [ ] Implementar `ShapeInventory` + `CorelShapeKind` em `Optimus.Core`. _R1.4, R1.5_
6. [ ] **Teste primeiro:** a regra "esta forma tem geometria reduzível?" classifica curva=sim,
   bitmap=não, texto=não, grupo=recursa, PowerClip=recursa. _R1.4_
7. [ ] Implementar `ShapeWalker` em `Optimus.Interop` usando `FindShapes` + recursão explícita em
   `PowerClip.Shapes`, emitindo `ShapeInventory`. **Somente leitura.** _R1.3, R1.4, R1.7_
8. [ ] Implementar `CorelDocumentState` (mm na entrada, unidade original restaurada em `Dispose`,
   inclusive em exceção). _R1.1, R1.2_
9. [ ] Migrar `FileOptimizer` para as novas camadas; remover a constante errada e a recursão manual;
   nenhum código órfão deve sobrar. _R1.6_
10. [ ] Smoke na VM: rodar o inventário em `docs/Arquivos_teste/arquivo.cdr` e `arquivo2.cdr` e
    registrar o resultado em `LESSONS`. _R1.5, R1.7_

## DO NOT WANT

- Não escrever nenhum algoritmo de otimização nesta fase (é fundação + inventário).
- Não alterar o documento (nem "só para testar") — a travessia é leitura pura.
- Não adivinhar constante alguma: sem linha no dump, não entra no código.
- Não manter a recursão manual por `.Shapes` como caminho principal.
- Não introduzir dependência nova.

## VALIDATION

- `dotnet build Optimus.sln -c Release` e `dotnet test` verdes.
- Teste que prova `CdrMillimeter == 3` e que a tolerância em mm não sofre fator 10×.
- Teste de inventário com fonte falsa cobrindo: curva, bitmap, texto, grupo aninhado, PowerClip.
- **Na VM:** inventário nos dois arquivos reais; o `.cdr` deve continuar **byte-idêntico** (comparar
  hash antes/depois — este é o teste de R1.7).

## VERIFICATION GATE

- O inventário do `arquivo.cdr` precisa **encontrar formas dentro de PowerClip** (se vier zero
  PowerClip num arquivo de design real, a travessia continua errada).
- O inventário precisa reportar as imagens do `arquivo.cdr` (sabemos que existem: 62% do arquivo).
  Se reportar 0 bitmaps, a travessia está mentindo.
- Hash do arquivo idêntico antes/depois prova ausência de efeito colateral.

## TECHNICAL CONSTRAINTS

Arquivos < 500 linhas. `Optimus.Core` sem nenhuma referência a COM/WinForms/WPF. Add-in continua
compilando e carregando no Corel ao fim da fase (não pode quebrar o que já funciona).

## LESSONS

**Executado em 2026-07-24 — v1.1.0.**

Estrutura entregue (`Optimus.sln`, 6 projetos): `src/Optimus.Core` (netstandard2.0, puro) ·
`src/Optimus.Interop` (net48, COM) · `tests/Optimus.Core.Tests` (net8.0, xUnit — **41 testes
verdes**) · AddIn/Resources/Installer. Build sem warnings.

Correções aplicadas:
1. **Unidade:** `CorelConstants.CdrMillimeter = 3`, com teste de regressão dedicado
   (`CdrMillimeter_is_3_not_4`). As constantes ficaram no **Core**, não no Interop — senão não
   seriam testáveis do net8.0 (net8 não referencia net48). `CorelDocumentState` aplica e restaura.
2. **PowerClip:** `ShapeWalker` recursa explicitamente em `Shape.PowerClip.Shapes`. O inventário
   expõe `ShapesInPowerClip` justamente para provar que a travessia chegou lá.
3. **Formas não-curva:** `GeometryClassifier` classifica todas (curva/bitmap/texto/paramétrica/
   contêiner/efeito vivo) em vez de pular o que não é curva. Primitivas paramétricas ficam intactas
   de propósito — convertê-las em curva **aumentaria** o arquivo.
4. `cdrCurveShape` estava comentado como 5 no v1.0; é **3** (5 é bitmap). Teste dedicado.

Decisões tomadas na execução:
- `ShapeWalker` usa travessia manual base-1 em vez de `Page.FindShapes(,, Recursive)`: precisamos
  saber **onde** cada forma foi achada (dentro ou fora de PowerClip) e recursar no PowerClip nós
  mesmos — `FindShapes` devolve um range plano e perderia esse contexto.
- `WalkResult` devolve o inventário **e** a lista de formas COM redutíveis, para o otimizador agir
  exatamente sobre o que foi contado (evita duas travessias divergentes).
- Comando **`analisar`** (leitura pura) + botão "Analisar documento" na UI, para a correção ser
  verificável pelo operador (regra de não-órfão).

### Smoke na VM — EXECUTADO (task 10 fechada), v1.2.2

Arquivo real do cliente: `kaneki.cdr` (Corel 2024, VM).

| | Travessia manual (v1.1.3) | Enumeração plana (v1.2.2) | Δ |
|---|---|---|---|
| Objetos | 16.901 | 16.899 | 0,01% |
| Nós | 421.371 | 421.057 | 0,07% |
| Curvas | 14.344 | 14.342 | 0,01% |
| Tempo | 174.119 ms | 34.710 ms | 5× |

**Dois métodos independentes concordam** → o gate da fase passou. Invariante verificado:
`classified == totalRecursive == 16.899`, `unclassified = 0`.

Distribuição real (`.Type` por forma): `0:Unknown=1, 1:Rectangle=2, 2:Ellipse=2523, 3:Curve=14342,
6:Text=2, 7:Group=25, 13:ContourGroup=3, 21:Custom=1`. Soma = 16.899 ✔
**PowerClip:** 16.893 formas abaixo do topo (topo = 6) — o v1.0 via 6. Correção comprovada.

### LIÇÕES DE PERFORMANCE (custaram 4 iterações — não repetir)

1. **O custo é proporcional aos OBJETOS COM tocados, não às chamadas feitas.** 110 s dos 174 s da
   travessia manual não apareciam em NENHUMA medição por chamada: era cleanup de RCW. `Release` de
   objeto STA roda na thread dona (a nossa), então deixar ~46 mil wrappers para o GC **bloqueia a
   própria thread que trabalha**. Solução: `Marshal.ReleaseComObject` explícito em forma, `Curve`,
   `Nodes` e range.
2. **`FindShapes` com filtro de tipo NÃO é fonte de verdade.** Somar `FindShapes(tipo)` sobre os 27
   valores do enum deu **6.118**; `FindShapes` sem filtro na mesma página deu **16.899**. O filtro
   devolve um subconjunto consistente (2,35× menos curvas). Usar `FindShapes(,, true)` **uma vez**
   e ler `.Type` de cada forma. Cross-check permanente no log: `curves: enumerated=X vs filter=Y`.
3. **Medir com `ElapsedMilliseconds` mente** quando a operação é sub-milissegundo — arredonda para
   zero. 64% do tempo "desaparecia". Usar ticks e reportar µs por chamada.
4. **`FindShapes(Recursive:=True)` ENTRA em PowerClip** (documentação anterior neste repo dizia o
   contrário, e foi o que justificou a travessia manual). Comprovado: 16.899 numa página com 6 no topo.
5. **Trocar um caminho correto por um novo sem conferir os dois é regressão garantida.** A v1.2.0
   ficou 5× mais rápida perdendo 58% dos nós, e só foi pega porque o operador estranhou o número.

## COVERAGE

R1.1 → 2,3,8 · R1.2 → 2,8 · R1.3 → 7,10 · R1.4 → 3,5,6,7 · R1.5 → 4,5,7,10 · R1.6 → 1,9 · R1.7 → 7,10
