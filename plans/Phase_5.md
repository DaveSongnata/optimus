# Fase 5 — Bitmaps por DPI efetivo (opt-in)

- **STATUS:** [x] **v1.7.0 — implementada** (252 testes verdes). Falta validar na VM.
- **OBJETIVO (1 frase):** Reduzir imagens embutidas cuja resolução **efetiva** está muito acima do
  necessário para impressão, sempre com consentimento explícito e com padrão seguro para papel.
- **POR QUE EXISTE:** medição do `arquivo.cdr` — **62% do arquivo são imagens** (84 MB crus). Sem
  esta fase, a meta de 50% é matematicamente inatingível nesse tipo de arquivo (teto de ~34%).

## REQUIREMENTS (EARS)

- **R5.1 — WHEN** o arquivo tem bitmaps, **THEN** o sistema SHALL calcular a **resolução efetiva**
  de cada um (pixels ÷ tamanho físico colocado na página), não a nominal. _R2.4_
- **R5.2 — WHEN** um bitmap tem resolução efetiva **≤ 300 DPI**, **THEN** o sistema SHALL deixá-lo
  intacto. _O3, P3_
- **R5.3 — WHEN** um bitmap tem resolução efetiva **> 300 DPI** e o operador autoriza, **THEN** o
  sistema SHALL reamostrá-lo para 300 DPI (padrão). _O3_
- **R5.4 — WHEN** a reamostragem é oferecida, **THEN** o sistema SHALL informar **antes** quantas
  imagens seriam afetadas e quantos bytes seriam economizados. _P1, P3_
- **R5.5 — WHEN** nenhum consentimento é dado, **THEN** o sistema SHALL NOT modificar imagem alguma. _P3_
- **R5.6 — WHEN** o operador escolhe um alvo mais agressivo (200 ou 150 DPI), **THEN** o sistema
  SHALL avisar que é adequado apenas para grandes formatos/visualização. _P3_
- **R5.7 — WHEN** a reamostragem termina, **THEN** o sistema SHALL reportar a economia **real** em
  disco atribuída às imagens (comparativo da Fase 2). _P1_

## DESIGN (curto)

**Por que "DPI efetivo" e não "DPI nominal":** uma imagem de 4000 px colocada em 5 cm tem ~2032 DPI
efetivos — 85% desses pixels são desperdício invisível em qualquer impressão. É exatamente esse
desperdício que explica os 84 MB de bitmap cru dentro de um arquivo de 4,5 MB.

```
dpiEfetivo = pixels / (tamanhoFisicoMm / 25.4)
alvo       = 300 (padrão, seguro para impressão)
se dpiEfetivo > alvo * 1.10   →  candidato   (margem de 10% evita reamostrar por arredondamento)
novaLargura  = round(tamanhoFisicoPol * alvo)
```

Aplicação: `Bitmap.Resample(Width, Height, AntiAlias, ResolutionX, ResolutionY)` — **verificado**
(dump 3289). Passar **as quatro** dimensões calculadas explicitamente; não confiar em sentinela 0.

**Nunca chamar `Document.ResolveAllBitmapsLinks()`** — ela **embute** bitmaps ligados, ou seja,
**aumenta** o arquivo. Está anotada aqui justamente para não ser usada por engano.

Bitmaps também aparecem em `cdrOLEObjectShape = 12` e `cdrEPSShape = 27`, que **não** são pegos por
`FindShapes(cdrBitmapShape)` — devem ser detectados e **reportados**, não reamostrados.

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/Diagnostics/EffectiveDpi.cs` — reusado da Fase 2. _R5.1_
- `src/Optimus.Core/Optimization/BitmapPlan.cs` — **novo**: decide candidatos e estima economia. _R5.2–R5.4_
- `src/Optimus.Interop/BitmapResampler.cs` — **novo**: aplica `Resample` via COM. _R5.3_
- `tests/Optimus.Core.Tests/Optimization/BitmapPlanTests.cs` — **novo**.

## READY-MADE SOLUTIONS TO USE

- `Bitmap.Resample(...)` — nativo, verificado. Nenhuma biblioteca de imagem é necessária.
- `Bitmap.SizeWidth/SizeHeight` (px) + `Shape.SizeWidth/SizeHeight` (mm) para o DPI efetivo.

## TASKS

1. [ ] **Teste primeiro:** `BitmapPlan` — imagem a 300 DPI efetivos não é candidata; a 2000 DPI é;
   a 330 DPI (dentro da margem de 10%) **não** é. _R5.1, R5.2_
2. [ ] **Teste primeiro:** cálculo da nova dimensão em pixels para o alvo, e estimativa de economia
   proporcional à razão de área. _R5.3, R5.4_
3. [ ] Implementar `BitmapPlan` (puro). _R5.2–R5.4_
4. [ ] Implementar `BitmapResampler` (COM), passando as quatro dimensões explicitamente. _R5.3_
5. [ ] UI: painel de consentimento mostrando "N imagens acima de 300 DPI · economia estimada X MB",
   com os alvos 300 (padrão) / 200 / 150 e o aviso do R5.6. Desligado por padrão. _R5.4, R5.5, R5.6_
6. [ ] Detectar e **apenas reportar** rasters em OLE/EPS (não reamostrar). _R5.1_
7. [ ] **Medir nos arquivos reais:** aplicar 300 DPI no `arquivo.cdr` e registrar a redução real
   obtida; confirmar se a meta de 50% é atingida com as Fases 3+4+5 combinadas. _R5.7, O4_
8. [ ] Verificação visual: imprimir/ampliar uma peça reamostrada e confirmar ausência de perda
   perceptível a 300 DPI. _P3_

## DO NOT WANT

- Não reamostrar nada por padrão (é opt-in — O3/P3).
- Não reamostrar abaixo de 300 DPI sem escolha explícita do operador.
- Não usar DPI nominal como critério.
- **Não chamar `ResolveAllBitmapsLinks()`** (aumenta o arquivo).
- Não recomprimir com perda adicional (JPEG) sem que isso seja uma decisão separada e futura.
- Não tocar em bitmaps de OLE/EPS.

## VALIDATION

- `dotnet test` verde no plano puro.
- Na VM com `arquivo.cdr`: relatório mostra as imagens candidatas e a economia estimada **antes**;
  após aplicar, a economia real é comparável à estimada.
- O arquivo continua abrindo e a arte continua correta.
- Backup da Fase 3 permite voltar ao original.

## VERIFICATION GATE

- Adversarial: arquivo cujas imagens já estão a 300 DPI ⇒ **zero** candidatas e zero mutação.
- Rodar duas vezes: a segunda não deve encontrar candidata (idempotência) — se encontrar, o cálculo
  de DPI efetivo pós-reamostragem está errado.
- A economia reportada tem de ser a real medida no arquivo, nunca a estimativa.

## TECHNICAL CONSTRAINTS

Reamostragem é irreversível no documento: só roda **depois** do backup da Fase 3 existir e ser
verificado. Arquivos < 500 linhas.

## LESSONS

**Implementada em 2026-07-25 — v1.7.0.**

`BitmapInspector` (COM): varre via `FindShapes(cdrBitmapShape, recursivo)`, mede **DPI efetivo** por
eixo (`Bitmap.SizeWidth` em px ÷ `Shape.SizeWidth` em mm) e reamostra com
`Bitmap.Resample(W, H, AntiAlias, ResX, ResY)` passando **as quatro dimensões explicitamente** — a
documentação nada diz sobre um sentinela 0 significar "manter", e chutar isso teria consequência
irreversível na imagem do cliente.

Regras que viraram código:
- Imagem **em ou abaixo** do alvo **nunca** é tocada (O3); com margem de 10% para não reamostrar por
  arredondamento de posicionamento.
- Se o novo tamanho em pixels não for **menor**, a imagem é pulada (evita "reamostrar para igual").
- **OLE e EPS** carregam raster mas não expõem objeto `Bitmap`: **contados e reportados**, nunca
  modificados.
- **`Document.ResolveAllBitmapsLinks()` jamais é chamado** — ele EMBUTE bitmaps ligados e **aumenta**
  o arquivo. Está nomeado no código justamente para ninguém alcançá-lo por engano.
- Uma imagem que se recusa a reamostrar não aborta as outras.

Ligado ao cursor **QUALIDADE DE IMAGEM** (300/200/150 DPI ou "não mexer"), e o log registra
`found / aboveTarget / resampled / unresampleable`.

### PENDENTE na VM
Medir a redução real do `arquivo.cdr` (63% raster, 88,5 MB crus) a 300/200/150 DPI e fechar o veredito
sobre a meta de 50% combinando Fases 3+4+5. Conferir visualmente que 300 DPI é imperceptível no papel.

## COVERAGE

R5.1 → 1,6 · R5.2 → 1,3 · R5.3 → 2,3,4 · R5.4 → 2,3,5 · R5.5 → 5 · R5.6 → 5 · R5.7 → 7
