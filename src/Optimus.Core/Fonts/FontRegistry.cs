using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Optimus.Core.Fonts
{
    /// <summary>One font the shop has registered — a name it wants to track and, eventually, verify
    /// against every job before printing.</summary>
    public sealed class RegisteredFont
    {
        public string Name { get; set; } = "";

        /// <summary>Free-text note — "usada nas etiquetas do cliente X", etc.</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// The shop's font catalog, persisted the same storage-agnostic way as
    /// <see cref="Optimus.Core.Palettes.PaletteRegistry"/>.
    ///
    /// <para>
    /// "Semi-automatic" (Davi's whiteboard): the operator pastes a LIST of names — one per line, or
    /// copied straight out of a font-list export — and every distinct name becomes a registered font in
    /// one shot, instead of typing each one individually. <see cref="ImportNames"/> is that shortcut;
    /// nothing here fetches a URL or reaches outside the machine — "link" on the whiteboard means "the
    /// list the operator pastes in", not a network call, which would break the offline requirement.
    /// </para>
    /// </summary>
    public sealed class FontRegistry
    {
        private readonly Func<string?> _load;
        private readonly Action<string> _save;
        private readonly List<RegisteredFont> _fonts = new List<RegisteredFont>();

        private FontRegistry(Func<string?> load, Action<string> save)
        {
            _load = load;
            _save = save;
        }

        public static FontRegistry Load(Func<string?> load, Action<string> save)
        {
            var registry = new FontRegistry(load, save);
            registry.Reload();
            return registry;
        }

        public static FontRegistry InMemory()
        {
            string? blob = null;
            return Load(() => blob, s => blob = s);
        }

        public void Reload()
        {
            _fonts.Clear();
            string? json = null;
            try { json = _load(); } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                List<RegisteredFont>? loaded = JsonSerializer.Deserialize<List<RegisteredFont>>(json!);
                if (loaded != null) _fonts.AddRange(loaded);
            }
            catch (Exception) { }
        }

        public IReadOnlyList<RegisteredFont> Fonts => _fonts;

        public bool IsRegistered(string name) =>
            !string.IsNullOrWhiteSpace(name) && _fonts.Exists(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        public RegisteredFont Add(string name, string note = "")
        {
            string trimmed = (name ?? "").Trim();
            RegisteredFont? existing = _fonts.Find(f => string.Equals(f.Name, trimmed, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var font = new RegisteredFont { Name = trimmed, Note = note ?? "" };
            _fonts.Add(font);
            Persist();
            return font;
        }

        public bool Remove(string name)
        {
            int removed = _fonts.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) Persist();
            return removed > 0;
        }

        /// <summary>
        /// Splits a pasted block of text into distinct font names (one per line, blank lines and
        /// duplicates ignored) and registers every one of them. Returns how many were NEW.
        /// </summary>
        public int ImportNames(string pastedList)
        {
            if (string.IsNullOrWhiteSpace(pastedList)) return 0;

            int added = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in pastedList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = line.Trim();
                if (name.Length == 0 || !seen.Add(name)) continue;
                if (IsRegistered(name)) continue;
                _fonts.Add(new RegisteredFont { Name = name });
                added++;
            }
            if (added > 0) Persist();
            return added;
        }

        private void Persist()
        {
            try { _save(JsonSerializer.Serialize(_fonts)); }
            catch (Exception) { }
        }
    }
}
