#!/usr/bin/env node
/**
 * Builds the product's stylesheet and injects it into every screen.
 *
 * ARCHITECTURE
 * ------------
 * One source of truth: `src/ui/design-system.css`. It is a real Tailwind source file, so the design
 * system is expressed the way Tailwind expresses one:
 *
 *   @layer base        tokens and element defaults
 *   @layer components  the product's components, built with @apply from the theme scale
 *   @tailwind utilities  one-off adjustments, last so markup can always win
 *
 * Tailwind compiles that against `tailwind.config.js` (whose theme IS the token scale) and emits a
 * single stylesheet. This script injects it into the `<style id="tw">` block of all three screens —
 * the CorelDRAW docker, the maintenance app and the installer.
 *
 * WHY INJECT INSTEAD OF LINK
 * Each screen ships as ONE embedded resource and has to work with no network. A second file would
 * be a second way for a screen to load half-styled inside CorelDRAW.
 *
 * WHY BUILD TIME INSTEAD OF THE BROWSER
 * Tailwind's in-browser JIT means shipping a CSS compiler into a panel whose first rule is that it
 * can never take CorelDRAW down. The CLI does the same job here and ships bytes, not a compiler.
 *
 *   node scripts/build-css.js          rebuild and inject
 *   node scripts/build-css.js --check  fail if any screen is out of date
 */
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const os = require('os');

const ROOT = path.resolve(__dirname, '..');
const CHECK = process.argv.includes('--check');

const SOURCE = path.join(ROOT, 'src/ui/design-system.css');

const PAGES = [
  'src/Optimus.AddIn/wwwroot/index.html',
  'src/Optimus.Maintenance/wwwroot/index.html',
  'installer/wwwroot/index.html',
];

const OPEN = '<style id="tw">';
const CLOSE = '</style>';

// The CLI's own JS entry, run with node. NOT node_modules/.bin/tailwindcss.cmd: on Windows, Node
// refuses to spawn a .cmd without a shell (EINVAL), and going through a shell would drag command
// quoting into a path that already contains spaces.
function tailwindEntry() {
  const entry = path.join(ROOT, 'node_modules', 'tailwindcss', 'lib', 'cli.js');
  if (!fs.existsSync(entry)) {
    console.error('tailwindcss não está instalado. Rode: npm install');
    process.exit(1);
  }
  return entry;
}

function generate() {
  if (!fs.existsSync(SOURCE)) {
    console.error('não achei ' + path.relative(ROOT, SOURCE));
    process.exit(1);
  }

  const out = path.join(os.tmpdir(), 'optimus-ui.css');
  try {
    execFileSync(process.execPath, [tailwindEntry(), '-i', SOURCE, '-o', out, '--minify'], {
      cwd: ROOT,
      stdio: ['ignore', 'ignore', 'pipe'],
    });
  } catch (e) {
    console.error('Tailwind falhou:\n' + (e.stderr ? e.stderr.toString() : e.message));
    process.exit(1);
  }

  const css = fs.readFileSync(out, 'utf8').trim();
  try { fs.unlinkSync(out); } catch (e) { /* a temp file, not the build */ }
  return css;
}

function inject(css) {
  const stale = [];

  for (const rel of PAGES) {
    const file = path.join(ROOT, rel);
    if (!fs.existsSync(file)) continue;

    const html = fs.readFileSync(file, 'utf8');
    const nl = html.includes('\r\n') ? '\r\n' : '\n';
    const a = html.indexOf(OPEN);
    if (a < 0) { console.log('  --   ' + rel + ' (sem <style id="tw">, ignorada)'); continue; }

    const b = html.indexOf(CLOSE, a);
    if (b < 0) { console.error('  !!   ' + rel + ': <style id="tw"> sem fechamento'); process.exit(1); }

    if (html.slice(a + OPEN.length, b).trim() === css) {
      console.log('  ok   ' + rel + ' (já em dia)');
      continue;
    }
    if (CHECK) { stale.push(rel); continue; }

    fs.writeFileSync(file, html.slice(0, a + OPEN.length) + nl + css + nl + html.slice(b));
    console.log('  ok   ' + rel);
  }

  if (CHECK && stale.length) {
    console.error('\nCSS desatualizado em:\n  ' + stale.join('\n  ') +
      '\nRode: node scripts/build-css.js');
    process.exit(1);
  }
}

console.log('==> Compilando o sistema de design (Tailwind)');
const css = generate();
console.log('    ' + (css.length / 1024).toFixed(1) + ' KB para ' + PAGES.length + ' telas');
inject(css);
console.log('CSS ok.');
