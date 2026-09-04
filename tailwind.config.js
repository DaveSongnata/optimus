/**
 * Tailwind at BUILD TIME only. Nothing is downloaded and no compiler ships inside the docker: the
 * CLI scans the pages, emits just the utilities they actually use, and `scripts/build-css.js`
 * injects the result into each page's <style id="tw"> block. The panel stays a single offline file.
 *
 * The scale below is not Tailwind's default — it is the token system this UI already measured its
 * way to (O21), expressed in a form Tailwind can generate utilities from. Two scales would be worse
 * than none: `p-4` and `var(--s4)` have to mean the same 16px, or the interface drifts apart again.
 */
module.exports = {
  content: {
    files: [
      './src/Optimus.AddIn/wwwroot/index.html',
      './src/Optimus.Maintenance/wwwroot/index.html',
      './installer/wwwroot/index.html',
    ],

    // Strip <style> blocks before extracting class names.
    //
    // Without this the generated CSS feeds itself: Tailwind's extractor is a regex over the raw
    // file, so it would find `.p-4` inside the block it generated last time and keep emitting it
    // forever. The output could then only ever grow — a utility would survive long after the last
    // markup using it was deleted, which is exactly the dead-CSS problem this is meant to end.
    transform: {
      html: (content) => content.replace(/<style[\s\S]*?<\/style>/g, ''),
    },
  },

  // The docker is a ~340px column. Nothing here is ever wider than a phone, so the desktop
  // breakpoints would only be dead weight in the scan.
  theme: {
    screens: {
      sm: '380px',
      md: '520px',
    },

    // Colours come from the CSS custom properties, which stay the single source of truth. They are
    // not duplicated as hex here: the accessibility work behind each value (O20) lives next to the
    // token with its measurement, and a second copy would eventually disagree with the first.
    colors: {
      transparent: 'transparent',
      current: 'currentColor',
      white: '#fff',
      paper: 'var(--paper)',
      card: 'var(--card)',
      bench: 'var(--bench)',
      field: 'var(--field)',
      ink: {
        DEFAULT: 'var(--ink)',
        70: 'var(--ink-70)',
        45: 'var(--ink-45)',
        30: 'var(--ink-30)',
      },
      line: {
        DEFAULT: 'var(--line)',
        2: 'var(--line-2)',
      },
      border: 'var(--border)',
      brand: {
        DEFAULT: 'var(--brand)',
        bright: 'var(--brand-bright)',
        soft: 'var(--brand-soft)',
        soft2: 'var(--brand-soft2)',
      },
      err: { DEFAULT: 'var(--err)', soft: 'var(--err-soft)' },
      warn: { DEFAULT: 'var(--warn)', soft: 'var(--warn-soft)' },
      ok: { DEFAULT: 'var(--ok)', soft: 'var(--ok-soft)' },
      info: { DEFAULT: 'var(--info)', soft: 'var(--info-soft)' },
    },

    // Multiples of 4: at 125% and 150% Windows scaling they land on whole pixels (O21).
    spacing: {
      0: '0',
      1: 'var(--s1)',   // 4
      2: 'var(--s2)',   // 8
      3: 'var(--s3)',   // 12
      4: 'var(--s4)',   // 16
      5: 'var(--s5)',   // 24
      6: 'var(--s6)',   // 32
      px: '1px',
      full: '100%',
    },

    borderRadius: {
      none: '0',
      sm: 'var(--r-sm)',
      md: 'var(--r-md)',
      full: 'var(--r-pill)',
    },

    fontSize: {
      cap: ['var(--f-cap)', { lineHeight: 'var(--lh-tight)' }],
      sm: ['var(--f-sm)', { lineHeight: 'var(--lh-body)' }],
      // 13px is the floor: below the critical size, reading speed falls off a cliff
      // (Legge & Bigelow 2011).
      md: ['var(--f-md)', { lineHeight: 'var(--lh-body)' }],
      lg: ['var(--f-lg)', { lineHeight: 'var(--lh-tight)' }],
      hero: ['var(--f-hero)', { lineHeight: '1.05', letterSpacing: '-0.02em' }],
    },

    fontFamily: {
      sans: 'var(--font-sans)',
      mono: 'var(--font-mono)',
    },

    boxShadow: {
      none: 'none',
      card: 'var(--sh-card)',
      raised: 'var(--sh-raised)',
    },

    extend: {
      transitionTimingFunction: { out: 'var(--ease)' },
      transitionDuration: { fast: 'var(--t-fast)', med: 'var(--t-med)' },
      minHeight: { tap: '48px' },   // the touch target the bottom bar has to honour
    },
  },

  // Preflight is OFF. These pages already ship a reset, and more importantly the vendored colour
  // picker brings its own styles — Tailwind's reset would flatten them and the picker would come
  // apart. Turning it off keeps this change additive: existing CSS keeps working exactly as it did.
  corePlugins: { preflight: false },
};
