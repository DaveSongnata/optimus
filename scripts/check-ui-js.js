// check-ui-js.js — static sanity check of the WebView2 pages' JavaScript.
//
// WHY THIS EXISTS. `esc()` was called 34 times in the CorelDRAW add-in's page and
// defined ZERO times. Every render function threw ReferenceError on its first call
// and died silently — the panel simply did nothing, for weeks, and 414 unit tests
// stayed green the whole time because none of them can see JavaScript.
// This closes exactly that gap: it parses each page's script and fails the build on
// a syntax error, on a call to a function nobody defines, or on an onclick= handler
// in the markup that has no matching function.
//
//   node scripts/check-ui-js.js
//
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const PAGES = [
  'src/Optimus.AddIn/wwwroot/index.html',
  'src/Optimus.Maintenance/wwwroot/index.html',
  'installer/wwwroot/index.html',
];

// Provided by the host before the page script runs (I18nScript.Build) or by the browser.
const HOST_GLOBALS = new Set([
  'T', 'applyI18n', 'OPTIMUS_I18N', 'OPTIMUS_LANG', 'OPTIMUS_LANGS', 'OPTIMUS_LANG_KEY',
]);
const BROWSER_GLOBALS = new Set([
  'window', 'document', 'console', 'navigator', 'location', 'chrome', 'JSON', 'Math', 'Date',
  'Number', 'String', 'Boolean', 'Array', 'Object', 'RegExp', 'Error', 'Promise', 'Set', 'Map',
  'parseInt', 'parseFloat', 'isNaN', 'isFinite', 'encodeURIComponent', 'decodeURIComponent',
  'setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'requestAnimationFrame',
  'Blob', 'FileReader', 'MediaRecorder', 'SpeechSynthesisUtterance', 'Intl', 'alert',
]);

let failures = 0;
const root = path.resolve(__dirname, '..');

for (const rel of PAGES) {
  const file = path.join(root, rel);
  if (!fs.existsSync(file)) continue;

  const html = fs.readFileSync(file, 'utf8');

  // Duplicate class attributes: the browser keeps the FIRST and silently drops the rest, so the
  // classes look present in the source and do nothing on screen. Introduced here by a bulk edit
  // that turned style= into class= on elements that already had one.
  const dupClass = [...html.matchAll(/<\w+[^>]*?\sclass="[^"]*"[^>]*?\sclass="[^"]*"[^>]*>/g)];
  if (dupClass.length) {
    console.error(`FALHOU  ${rel}: ${dupClass.length} elemento(s) com class= duplicado`);
    dupClass.slice(0, 3).forEach(m => console.error(`          ${m[0].slice(0, 90)}`));
    failures++;
    continue;
  }

  // Unbalanced <div> inside a tab pane. A pane that never closes SWALLOWS the next one, so the
  // second tab is only visible while the first one is — which means never. The marker sits AFTER
  // the pane's own closing tag, so a healthy pane balances to zero across the slice.
  let paneBroken = false;
  for (const pane of [...html.matchAll(/<div class="pane[^"]*" id="(pane-[\w-]+)"/g)]) {
    const marker = html.indexOf('<!-- /' + pane[1] + ' -->', pane.index);
    if (marker < 0) continue;
    const slice = html.slice(pane.index, marker);
    const open = (slice.match(/<div\b/g) || []).length;
    const close = (slice.match(/<\/div>/g) || []).length;
    if (open !== close) {
      console.error(`FALHOU  ${rel}: ${pane[1]} com <div> desbalanceado (abre ${open}, fecha ${close})`);
      paneBroken = true;
    }
  }
  if (paneBroken) { failures++; continue; }

  // The page's own inline script (there is exactly one per page, by design).
  const blocks = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  if (blocks.length === 0) { console.log(`- ${rel}: sem <script>, ignorado`); continue; }
  const js = blocks.join('\n;\n');

  // 1. Syntax. A stray brace used to be discoverable only by loading CorelDRAW.
  try {
    new vm.Script(js, { filename: rel });
  } catch (e) {
    console.error(`FALHOU  ${rel}: erro de sintaxe — ${e.message}`);
    failures++;
    continue;
  }

  // 2. Every called name must be declared somewhere, or be a known global.
  const declared = new Set();
  for (const m of js.matchAll(/function\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
  for (const m of js.matchAll(/(?:var|let|const)\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
  // window.foo = ... also defines foo for later bare calls.
  for (const m of js.matchAll(/window\.([A-Za-z_$][\w$]*)\s*=/g)) declared.add(m[1]);
  // Parameters are declarations too — `function bootStep(label, fn){ fn(); }` is fine.
  for (const m of js.matchAll(/function\s*[A-Za-z_$\w]*\s*\(([^)]*)\)/g)) {
    for (const p of m[1].split(',')) {
      const name = p.trim().split(/[=\s]/)[0];
      if (/^[A-Za-z_$][\w$]*$/.test(name)) declared.add(name);
    }
  }
  for (const m of js.matchAll(/catch\s*\(\s*([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);

  // Comments and string literals must NOT be scanned for calls: prose like
  // "a paleta (ex.: Cliente X)" would otherwise read as a call to `ex()`.
  const code = js
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/(^|[^:\\])\/\/[^\n]*/g, '$1')
    .replace(/'(?:\\.|[^'\\])*'/g, "''")
    .replace(/"(?:\\.|[^"\\])*"/g, '""');

  const called = new Map(); // name -> first line
  const lines = code.split('\n');
  lines.forEach((line, i) => {
    // Bare `name(` not preceded by a dot (so obj.method() is skipped) and not a keyword.
    for (const m of line.matchAll(/(^|[^.\w$])([A-Za-z_$][\w$]*)\s*\(/g)) {
      const name = m[2];
      if (/^(if|for|while|switch|catch|return|typeof|function|new|else|do|delete|void|in|of|case|throw)$/.test(name)) continue;
      if (!called.has(name)) called.set(name, i + 1);
    }
  });

  const missing = [];
  for (const [name, line] of called) {
    if (declared.has(name) || HOST_GLOBALS.has(name) || BROWSER_GLOBALS.has(name)) continue;
    missing.push(`${name}() — primeira chamada na linha ${line} do script`);
  }

  // 3. Every onclick/oninput handler in the markup must resolve to a declared function.
  const handlers = new Set();
  for (const m of html.matchAll(/\son(?:click|input|change)\s*=\s*"([A-Za-z_$][\w$]*)\s*\(/g)) {
    handlers.add(m[1]);
  }
  for (const name of handlers) {
    if (declared.has(name) || HOST_GLOBALS.has(name) || BROWSER_GLOBALS.has(name)) continue;
    missing.push(`${name}() — usado num on*= do HTML, mas nao existe no script`);
  }

  if (missing.length) {
    console.error(`FALHOU  ${rel}: ${missing.length} funcao(oes) nao definida(s):`);
    for (const m of missing) console.error(`          ${m}`);
    failures++;
  } else {
    console.log(`ok      ${rel}  (${declared.size} declaracoes, ${called.size} chamadas)`);
  }
}

if (failures) {
  console.error(`\n${failures} pagina(s) com problema. O build nao deve seguir.`);
  process.exit(1);
}
console.log('\nUI JavaScript ok.');
