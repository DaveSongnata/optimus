# Optimus — sistema de design

> Documento de referência do desenho da interface do Optimus (docker do CorelDRAW, app de
> manutenção e instalador). Última atualização: **2026-08-19**.
>
> **Regra de leitura:** tudo que aparece como *medido* foi calculado sobre as cores e o HTML que
> estavam efetivamente no ar — não estimado, não copiado de artigo. Onde a literatura recomenda uma
> coisa e a medição mostrou que ela não se sustenta nesta superfície, o documento diz isso e explica
> por quê. É justamente esse tipo de conflito que costuma ser desfeito por engano depois.

---

## 1. Por que a interface parecia "gerada por máquina"

O diagnóstico não é estético, é estrutural: **cor era a única coisa tokenizada.** Todo o resto —
tamanho de texto, raio de canto, espaçamento, sombra — estava escrito à mão, valor por valor, no
ponto de uso. O arquivo continha:

| Eixo | Antes | Depois |
|------|-------|--------|
| Tamanhos de fonte | **16** (11 deles entre 8 e 13px, em passos de 0,5px) | 5 |
| Raios de canto | **14** | 3 |
| Espaçamentos | **19** | 6 |
| Sombras | **21** | 2 |

Onze tamanhos de fonte separados por meio pixel não são uma decisão de desenho — são onze decisões
independentes que por acaso ficaram parecidas. É exatamente a assinatura de conteúdo gerado: cada
trecho é localmente plausível e o conjunto não tem sistema. O olho não consegue nomear o erro, mas
percebe que nada rima com nada.

**Regra:** nenhum valor visual escrito à mão. Tamanho, raio, espaço e sombra saem dos tokens.

### A escala

```
--s1:4px  --s2:8px  --s3:12px  --s4:16px  --s5:24px  --s6:32px
--r-sm:6px  --r-md:10px  --r-pill:999px
--f-cap:11px  --f-sm:12px  --f-md:13px  --f-lg:15px  --f-hero:30px
```

Dois motivos concretos por trás dos números:

- **Espaços são múltiplos de 4** porque a 125% e 150% de DPI — as escalas mais comuns em máquina de
  gráfica — eles caem em pixel inteiro. Um espaçamento de 6px vira 7,5px a 125% e o navegador
  arredonda de forma inconsistente entre elementos vizinhos, produzindo aquele desalinhamento de
  1px que ninguém consegue apontar mas todo mundo sente.
- **13px é o piso para texto corrido.** A curva de velocidade de leitura é plana acima do "tamanho
  crítico" e despenca abaixo dele (Legge & Bigelow, 2011) — ou seja, encolher texto não custa nada
  até certo ponto e depois custa muito, de uma vez. 11px fica reservado para caixa-alta curta, que
  tolera tamanho menor por ter altura-x proporcionalmente maior.

---

## 2. Contraste — o que estava reprovado no ar

Medido sobre as cores que o produto estava efetivamente usando:

| Combinação | Onde aparecia | Razão | Mínimo | |
|---|---|---:|---:|---|
| branco sobre `#EA580C` | **rótulo do botão principal** | **3,56:1** | 4,5:1 | reprova |
| `#EA580C` sobre papel | texto de marca | **3,41:1** | 4,5:1 | reprova |
| branco sobre `#C2410C` | — | 5,18:1 | 4,5:1 | passa |
| cor branca do documento, sem contorno | **amostras da tabela de cores** | **1,04:1** | 3:1 | reprova |

As correções:

- **Botão principal virou `#C2410C` sólido.** O gradiente saiu porque ia até o tom claro, então
  metade do rótulo caía na faixa reprovada. Gradiente atrás de texto significa que o **pior ponto**
  do gradiente é o contraste real, não a média.
- **`#EA580C` continua existindo, mas nunca mais carrega texto sobre o papel** — só preenchimento e
  borda. Está tokenizado à parte (`--brand-bright`) exatamente para que isso não se perca de vista.
- **Toda amostra de cor ganhou contorno `#767676`** (4,35:1 no papel, 4,54:1 sobre branco). Sem ele
  uma cor branca do documento é literalmente invisível: o operador vê uma lacuna e conclui que a
  auditoria falhou.
- **`--err` deixou de ser a própria cor da marca.** Os dois eram `#C2410C` idênticos — erro e ação
  primária pintados iguais.

> Cinzas "sutis" do mercado reprovam contraste de componente com folga: `#E5E7EB`, muito comum, dá
> **1,19:1**. Por isso há dois tokens distintos — `--border` para contorno que **precisa** ser
> visto, `--line` só para separador decorativo, que não carrega informação.

---

## 3. Status: a cor NÃO pode carregar significado sozinha

Esta é a descoberta que mais muda o desenho, e vem de duas medições que se cruzam.

**Primeira.** Simulando deuteranopia (Viénot/Brettel/Mollon 1999) sobre o par vermelho/âmbar:

```
par ERRO vs AVISO
  visão normal     dE = 48,8
  deuteranopia     dE =  6,3
```

Praticamente a mesma cor para cerca de **1 em 12 homens**. E é o par mais caro do produto:
*"isto vai estragar a tiragem"* contra *"confira isto"* é a confusão que custa dinheiro de verdade
numa gráfica. Vermelho-vs-âmbar é pior que vermelho-vs-verde, ao contrário da intuição.

**Segunda.** A recomendação usual para resolver isso é escalonar a **claridade** (L\*) entre os
estados, com separação de pelo menos 8. **Isso não é alcançável nesta superfície.** Exigir 4,5:1
contra um papel claro é exigir uma luminância específica; quatro cores com o mesmo contraste têm,
por construção, quase a mesma luminância. Medido:

```
cor      razão   L*
ERRO  #B31212  6,68  37,9
AVISO #7A5200  6,63  38,1
OK    #186A4B  6,29  39,6
INFO  #2F5FB3  5,91  41,3

separação L*:  ERRO->AVISO 0,2   AVISO->OK 1,4   OK->INFO 1,7
```

Todos entre L\* 37 e 41. Forçar a separação de 8 significaria clarear alguém até reprovar contraste.
**A regra da literatura e a regra de contraste são incompatíveis aqui, e contraste ganha.**

**Conclusão aplicada:** quem informa é a **forma**; a cor virou reforço.

| Estado | Silhueta | Símbolo |
|---|---|---|
| Erro | círculo | ✕ |
| Aviso | **triângulo** | ! |
| OK | círculo | ✓ |
| Info | quadrado | i |

As quatro se distinguem **em escala de cinza**, sem ler o símbolo interno. O triângulo é a silhueta
mais reconhecível do conjunto e por isso ficou com o aviso. O que estava no ar antes era um ponto
colorido de 8px, que não informa nada para essa pessoa.

> Detalhes de execução: o "!" do triângulo desce 1px porque o centro óptico de um triângulo não é o
> centro geométrico. E os símbolos são Unicode básico de propósito — a página roda sem internet e
> sem webfont, então icon-font está fora por regra do produto.

---

## 4. Alvos, foco e movimento

- **Alvo de clique é a linha inteira, com 32px de altura.** Numa linha que ocupa a largura toda, o
  que limita o movimento é a altura — então crescer a altura é o ganho barato. Antes só o
  quadradinho do checkbox era clicável.
- **`:focus-visible` visível.** Estava `outline:0` com troca de cor de borda, o que some para quem
  navega por teclado.
- **Movimento é desligável** (`prefers-reduced-motion`). Importa mais aqui que na média: máquina de
  gráfica costuma rodar com animação desligada para o sistema parecer mais rápido.
- **Duas durações, não uma.** 110ms é *resposta* ("registrei seu clique"); 200ms é *compreensão*
  ("isto veio dali"). São trabalhos diferentes, e a prática comum mistura os dois num valor só.

---

## 5. `tabular-nums` é obrigatório

O "1" da Segoe UI Variable — fonte de interface do Windows 11 — é cerca de **30% mais estreito** que
os outros dígitos. Numa coluna de números que atualiza durante a análise, isso faz o texto **tremer**
a cada quadro. Custa zero na Segoe clássica e resolve o sintoma inteiro:

```css
*{font-variant-numeric:tabular-nums;font-feature-settings:"tnum" 1}
```

---

## 6. Armadilha de DPI

O docker é estreito e o Windows escala. A 150%, um painel de 340px físicos vira **227 CSS px** —
abaixo do piso de 320px em que as regras de refluxo assumem que o conteúdo ainda cabe. Qualquer
largura mínima escrita em px precisa ser conferida nessa condição, não só a 100%.

---

## 7. Regras permanentes

1. **Status = forma + símbolo + texto.** Cor é reforço. Nunca sinalizar severidade só por cor.
2. **Nenhum valor visual escrito à mão** — sai dos tokens.
3. **Toda amostra de cor tem contorno.** Sem exceção, inclusive a de cor clara.
4. **Texto sobre cor de marca usa `--brand`, nunca `--brand-bright`.**
5. **Gradiente atrás de texto vale pelo pior ponto do gradiente**, não pela média — na dúvida, sólido.
6. **A perda vive no rótulo do controle**, nunca em nota de rodapé nem em `title=` (que não aparece
   para quem usa toque e não é anunciado de forma confiável).
7. **A página nunca morre calada** — `window.onerror` reporta ao host e `scripts/check-ui-js.js`
   reprova a compilação antes de qualquer coisa ser compilada (ver O18).

---

## 8. Referências

- Legge & Bigelow (2011), *Does print size matter for reading?* — velocidade de leitura e tamanho
  crítico.
- Viénot, Brettel & Mollon (1999) — simulação de dicromacia, usada para medir o par erro/aviso.
- WCAG 2.2 — 1.4.3 (contraste de texto), 1.4.11 (contraste de componente), 1.4.10 (refluxo, 320px),
  2.5.8 (tamanho de alvo) e sobretudo **1.4.1 (não usar cor como único meio)**.

Decisões travadas correspondentes no steering document: **O18**, **O20**, **O21**.
