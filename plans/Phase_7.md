# Fase 7 — Módulo 3: cores, fontes e acentos

- **STATUS:** [x] **v1.6.0 — implementada** (252 testes verdes). Falta validar na VM + exportar relatório.
- **OBJETIVO (1 frase):** Auditar o documento aberto — quais cores são usadas, quais fontes são
  usadas, quais dessas fontes não estão instaladas, e quais **não cobrem os acentos do português**.
- **VALOR REAL:** o erro clássico da gráfica é imprimir com fonte que não tem "ç"/"ã" e só descobrir
  no papel. Esta fase é a que impede o prejuízo.

## REQUIREMENTS (EARS)

- **R7.1 — WHEN** o documento é auditado, **THEN** o sistema SHALL listar todas as cores usadas em
  preenchimento e contorno, com modelo (CMYK/RGB/Escala de cinza/Spot), componentes e hex. _P1_
- **R7.2 — WHEN** uma cor é lida, **THEN** o documento SHALL permanecer inalterado. _P4_
- **R7.3 — WHEN** uma cor é spot/Pantone, **THEN** o sistema SHALL exibir o nome vindo **do próprio
  arquivo do cliente**, sem embarcar tabela proprietária. _P1_
- **R7.4 — WHEN** o documento é auditado, **THEN** o sistema SHALL listar todas as fontes usadas,
  incluindo fontes diferentes dentro de um mesmo bloco de texto. _P1_
- **R7.5 — WHEN** uma fonte usada não está instalada na máquina, **THEN** o sistema SHALL sinalizá-la
  como ausente (substituição silenciosa é o risco). _P1_
- **R7.6 — WHEN** uma fonte é verificada para pt-BR, **THEN** o sistema SHALL testar a cobertura de
  glifos dos caracteres obrigatórios e reportar exatamente quais faltam. _P1_
- **R7.7 — WHEN** a fonte é simbólica (Wingdings e afins), **THEN** ela SHALL ser marcada como
  símbolo e **não** reprovada por falta de acento. _P1_
- **R7.8 — WHEN** um caractere existe no mapa mas aponta para `.notdef`, **THEN** ele SHALL contar
  como **ausente** (`.notdef` imprime um quadrado vazado, não o caractere). _P1_
- **R7.9 — WHEN** a auditoria termina, **THEN** o sistema SHALL permitir exportar o resultado
  (relatório legível para anexar ao job). _P1_

## DESIGN (curto)

**Cores** — membros **verificados** na typelib: `Shape.Fill.Type` (`cdrUniformFill=1`,
`cdrFountainFill=2`, `cdrTextureFill=8`, `cdrPatternFill=9`, `cdrHatchFill=10`),
`Fill.UniformColor`, `Fill.Fountain.Colors` (item 1-based), `Outline.Color`, `Color.Type`
(`cdrColorCMYK=2`, `cdrColorRGB=5`, `cdrColorSpot=25`…), componentes CMYK/RGB/Lab,
**`Color.HexValue`** (não existe `ToHex`), **`Color.IsSpot`**, **`Color.SpotColorName`**,
`Color.Tint`, `Palette.MatchColor(Color) → Int32`.

> ⚠ **Risco de mutação (crítico):** `Fill.UniformColor` é **referência viva** para o documento e
> `ConvertToRGB()`/`ConvertToCMYK()` mutam **no lugar**. Toda leitura de cor SHALL começar por
> **`Color.GetCopy()`** (verificado, dump 3473). Auditar não pode alterar a arte do cliente (R7.2).

Sem API para cor de fill de textura e malha (`cdrMeshFillShape=20`) — reportar como
"não inspecionável" em vez de mentir. Alternativa do próprio Corel para colheita ampla:
`Document.AddColorsToDocPalette(SelectedOnly, MaxColorsPerBitmap)` (verificado).

**Pantone — decisão legal:** **não embarcar nenhuma tabela Pantone.** Há aplicação de DMCA
documentada contra repositórios que publicaram as bibliotecas, com a tese de substituição de mercado.
O caminho seguro é ecoar o nome que **já está no arquivo do cliente** (`SpotColorName`) e consultar
a paleta licenciada do próprio Corel (`Palette.MatchColor`). Se um dia for preciso um sistema de
cores nomeado próprio, o **freieFarbe/HLC** (dados sob licença zlib) é a única opção realmente livre.

**Fontes** — `Shape.Text.Story` → `TextRange`; **`TextRange.Font` é String**;
`TextRange.EnumRanges(cdrTextPropertyFont = 4)` dá os trechos por fonte (detecta fonte mista);
`IVGText.get_FontProperties(cdrTextFrames?)` **existe** (verificado no dump, linha 7076 — havia
divergência entre fontes de pesquisa, resolvida pela typelib). Fonte instalada: comparar contra
`Application.FontList` (case-insensitive) — o Corel não expõe API de "fonte ausente".

**Acentos — a decisão técnica desta fase:** usar **WPF `GlyphTypeface`**, que já vem no
`PresentationCore` **já referenciado** pelo add-in. **Zero dependência nova, zero custo de licença.**

```csharp
new Typeface(nome).TryGetGlyphTypeface(out GlyphTypeface g);   // false ⇒ não instalada
g.CharacterToGlyphMap.TryGetValue(0x00E7, out ushort gid);      // é a tabela cmap da fonte
```

Quatro armadilhas verificadas empiricamente, todas com mitigação obrigatória:
1. **Fonte simbólica** (Wingdings) responde `true` para "ç" — guardar com `g.Symbol` (R7.7).
2. **Substituição silenciosa**: `Typeface("Arial Narrow")` resolve para Arial e responde `true` —
   validar o nome pedido contra `Win32FamilyNames`/`FamilyNames`/`família + estilo` (R7.5).
3. **Desempenho**: enumerar `CharacterToGlyphMap` materializa todo o Unicode (~138 ms/fonte); usar
   **só `TryGetValue`** (~11 ms).
4. **TTC**: o índice da face vem em `FontUri.Fragment` (`cambria.ttc#1`) — o registro não tem isso.

**Checklist pt-BR** (baseado no CLDR `pt`), em camadas:
- **T1 — reprova (24):** `á à â ã ç é ê í ó ô õ ú Á À Â Ã Ç É Ê Í Ó Ô Õ Ú`
- **T2 — alerta (12):** `ª º ° § – — “ ” ‘ ’ …` e `$` (para R$)
- **T3 — informativo (12):** `ü Ü ñ Ñ ò Ò € © ® ™ « »`
- **T4 — só informa, nunca reprova:** marcas combinantes (`U+0301 0300 0302 0303 0308 0327`) —
  em NFC o português não precisa delas, e fontes ótimas não as têm.

## TARGET FILES (weight > 0.7)

- `src/Optimus.Core/Audit/PtBrCharset.cs` — **novo**: as camadas T1–T4. _R7.6_
- `src/Optimus.Core/Audit/GlyphCoverage.cs` — **novo**: modelo do resultado por fonte. _R7.6–R7.8_
- `src/Optimus.Core/Audit/ColorRecord.cs` — **novo**: modelo puro de cor. _R7.1_
- `src/Optimus.Core/Audit/AuditReport.cs` — **novo**: relatório exportável. _R7.9_
- `src/Optimus.Windows/FontProbe.cs` — **novo**: `GlyphTypeface` + guardas 1–4. _R7.5–R7.8_
- `src/Optimus.Interop/ColorCollector.cs` — **novo**: colhe cores via COM com `GetCopy()`. _R7.1–R7.3_
- `src/Optimus.Interop/FontCollector.cs` — **novo**: colhe fontes via `EnumRanges`. _R7.4_
- `tests/Optimus.Core.Tests/Audit/` — testes das camadas e do modelo.

## READY-MADE SOLUTIONS TO USE

- **WPF `GlyphTypeface`** — já disponível; substitui SixLabors.Fonts (a linha 2.x é net6+, não carrega
  em net48), HarfBuzzSharp (é shaping, ferramenta errada) e SharpFont (FreeType traz FTL/GPL).
- API nativa do Corel para cores/fontes — nada a implementar além da coleta.
- `Application.FontList` (verificado) para "está instalada?".

## TASKS

1. [ ] **Teste primeiro:** `PtBrCharset` — T1 tem exatamente os 24 obrigatórios; camadas não se
   sobrepõem; combinantes ficam em T4. _R7.6_
2. [ ] Implementar `PtBrCharset` + `GlyphCoverage` (puros). _R7.6_
3. [ ] **Teste primeiro:** `FontProbe` — fonte comum cobre T1; fonte simbólica é marcada como
   símbolo e não reprovada; nome inexistente ⇒ ausente; nome que sofre substituição ("Arial Narrow"
   quando não instalada) ⇒ **ausente**, não presente. _R7.5, R7.7_
4. [ ] Implementar `FontProbe` com as quatro guardas (símbolo, substituição, `TryGetValue`, TTC). _R7.5–R7.8_
5. [ ] **Teste primeiro:** `.notdef` conta como ausente; glifo em branco (espaço) é distinguido de
   `.notdef`. _R7.8_
6. [ ] Implementar `ColorCollector` — **`GetCopy()` antes de qualquer leitura**; percorre fill
   uniforme, fountain, contorno; recursa grupos e PowerClips; texturas/malhas reportadas como
   não inspecionáveis. _R7.1–R7.3_
7. [ ] Implementar `FontCollector` via `EnumRanges(cdrTextPropertyFont)`, cobrindo fonte mista. _R7.4_
8. [ ] Cruzar fontes usadas × `Application.FontList` para marcar ausentes. _R7.5_
9. [ ] **Teste de não-mutação:** auditar um documento e conferir hash do arquivo inalterado. _R7.2_
10. [ ] UI: aba **"Auditoria"** com cores (amostra + hex + modelo + spot), fontes (usada/instalada/
    cobertura pt-BR) e destaque vermelho para fonte que reprova em T1. _R7.1, R7.4, R7.6_
11. [ ] Exportar relatório (HTML imprimível, offline). _R7.9_

## DO NOT WANT

- **Não embarcar tabela Pantone** (nem RAL/HKS/DIC/TOYO — licenciados).
- Não converter cor para comparar (mutaria o documento) — usar `GetCopy()`.
- Não reprovar fonte por falta de marca combinante (T4).
- Não afirmar cobertura de textura/malha, que não têm API.
- Não trazer dependência de parsing de fonte quando o `GlyphTypeface` resolve.
- Não alterar nenhuma cor ou fonte do documento — esta fase é **só auditoria**.

## VALIDATION

- `dotnet test` verde: camadas pt-BR, cobertura, substituição, `.notdef`.
- Numa VM com Corel: auditar `docs/Arquivos_teste/*.cdr` e conferir que cores/fontes listadas batem
  com o que o Corel mostra em Janela → Cores/Texto.
- Hash do arquivo inalterado após auditoria (R7.2).
- Testar com uma fonte real que não tem "€"/aspas tipográficas e confirmar o alerta correto.

## VERIFICATION GATE

- Adversarial: documento com fonte propositalmente sem "ç" ⇒ tem de reprovar em T1 e nomear o
  caractere faltante. Se passar, a verificação é inútil.
- Wingdings não pode aparecer como "sem suporte a português".
- Fonte não instalada não pode ser reportada como instalada por causa de substituição.
- Nenhuma cor do documento pode mudar de modelo após a auditoria.

## TECHNICAL CONSTRAINTS

`Optimus.Core` puro. `FontProbe` fica em `Optimus.Windows` (usa WPF/`PresentationCore`) e precisa
funcionar em thread MTA. Arquivos < 500 linhas. Nenhuma dependência nova.

## LESSONS

**Implementada em 2026-07-25 — v1.6.0, 252 testes verdes** (eram 226 no fim da Fase 4).

### Entregue

**Acentos (o item que salva um job):** `PtBrCharset` em 4 camadas — 24 obrigatórios (á à â ã ç é ê í
ó ô õ ú + maiúsculas), 12 de alerta (ª º ° § – — “ ” ‘ ’ … $), 12 informativos (ü ñ € © ® ™ « »), e
marcas combinantes **que nunca reprovam**. `GlyphVerdict` (puro, testado) + `FontProbe`
(`Optimus.Windows`, novo projeto).

**Escolha técnica: WPF `GlyphTypeface.CharacterToGlyphMap` — que É a tabela cmap da fonte.** Zero
dependência nova, zero questão de licença. Descartados: SixLabors.Fonts 2.x (net6+ e licença
dividida), SharpFont (arrasta FreeType FTL/GPL), HarfBuzz (é shaping, ferramenta errada), GDI (só BMP
e substitui em silêncio).

**As quatro guardas, cada uma para um jeito de essa API mentir:**
1. **Fonte simbólica:** Wingdings responde `true` para "ç" (cmaps de símbolo mapeiam U+F0xx→U+00xx).
   Guardado por `GlyphTypeface.Symbol`.
2. **Substituição silenciosa:** `new Typeface("Arial Narrow")` **funciona** resolvendo para Arial — o
   documento imprimiria com outra fonte e a auditoria diria que estava tudo bem. Guardado exigindo
   que o nome pedido exista no conjunto real de instaladas.
3. **Custo:** enumerar o mapa materializa todo o Unicode (~138 ms/fonte); só `TryGetValue` (~11 ms).
4. **TTC:** o índice da face vem em `FontUri.Fragment` (`cambria.ttc#1`).
5. **(extra) `.notdef`:** entrada de cmap apontando para o glifo 0 **desenha um quadrado vazado** —
   conta como AUSENTE, não como coberto.

**Fontes usadas:** `FontCollector` usa `TextRange.EnumRanges(cdrTextPropertyFont=4)` por trecho —
necessário porque `Story.Font` volta **vazio** em texto com fontes mistas, o que descartaria fontes em
silêncio. `Application.FontList` dá o cruzamento "está instalada?".

**Cores:** `ColorAudit`/`ColorRecord` (puro) + `ColorCollector` (COM). O ponto crítico:
**`Fill.UniformColor` é referência VIVA** e `ConvertToRGB/CMYK` mutam no lugar — uma auditoria poderia
**alterar a arte do cliente**. Toda leitura começa por `Color.GetCopy()`; sem cópia disponível, a forma
é contada como não-inspecionável em vez de arriscar. Textura/malha/PostScript não têm API de cor:
**declarados como não inspecionados**, nunca chutados.

**Pantone:** nenhuma tabela embarcada. O nome vem de `Color.SpotColorName`, ou seja, **do arquivo do
próprio cliente** — ato diferente de redistribuir a biblioteca (há aplicação de DMCA documentada).

Achados priorizados para gráfica: mistura RGB+CMYK (causa clássica de "a cor saiu diferente"), cores
spot listadas por nome, e **cor de REGISTRO** usada como arte (sai em todas as chapas).

### PENDENTE
Validar na VM contra os arquivos reais (a fonte "Man City Dragon 2324" do `arquivo2.cdr` é justamente
o tipo de fonte customizada que costuma faltar acento) e exportar o relatório em HTML (R7.9).

## COVERAGE

R7.1 → 6,10 · R7.2 → 6,9 · R7.3 → 6 · R7.4 → 7,10 · R7.5 → 3,4,8 · R7.6 → 1,2,3,10 ·
R7.7 → 3,4 · R7.8 → 4,5 · R7.9 → 11
