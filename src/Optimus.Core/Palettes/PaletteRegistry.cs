using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Optimus.Core.Palettes
{
    /// <summary>
    /// Holds the shop's registered colour palettes and persists them through injected delegates —
    /// same storage-agnostic shape as <c>LocalizationService</c> (decision O7: no SQLite, no settings
    /// service). The Windows layer supplies a file-backed <c>Func&lt;string?&gt;</c>/<c>Action&lt;string&gt;</c>
    /// pair; tests supply an in-memory one.
    /// </summary>
    public sealed class PaletteRegistry
    {
        private readonly Func<string?> _load;
        private readonly Action<string> _save;
        private readonly List<ColorPalette> _palettes = new List<ColorPalette>();

        // Which palette the shop is currently working against. Off-palette is measured against THIS
        // one alone: a colour belonging to another client's palette is precisely the mistake the
        // operator is trying to catch, so merging every palette made the alert meaningless.
        private string _active = "";

        // Whether applying a palette colour asks before touching the document. Default ON: an
        // operator who has never seen this control must not discover it by having a client's artwork
        // repainted from a stray click.
        private bool _confirmApply = true;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>
        /// On-disk shape. Files written before the active palette existed are a bare JSON array, so
        /// <see cref="Reload"/> accepts both — refusing the old shape would wipe a shop's registered
        /// palettes on upgrade, which is the worst possible way to ship an improvement.
        /// </summary>
        private sealed class Envelope
        {
            public string Active { get; set; } = "";
            public List<ColorPalette> Palettes { get; set; } = new List<ColorPalette>();

            // Lives HERE, in the palette file, rather than in a new settings store: it is a
            // preference ABOUT palette application, and Optimus deliberately has no settings service
            // (O7). Nullable so a file written before this existed reads as "not stated" and takes
            // the safe default (ask).
            public bool? ConfirmApply { get; set; }
        }

        private PaletteRegistry(Func<string?> load, Action<string> save)
        {
            _load = load;
            _save = save;
        }

        public static PaletteRegistry Load(Func<string?> load, Action<string> save)
        {
            var registry = new PaletteRegistry(load, save);
            registry.Reload();
            return registry;
        }

        /// <summary>An in-memory registry for tests — nothing is persisted.</summary>
        public static PaletteRegistry InMemory()
        {
            string? blob = null;
            return Load(() => blob, s => blob = s);
        }

        public void Reload()
        {
            _palettes.Clear();
            string? json = null;
            try { json = _load(); } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                // The first non-space character tells the two shapes apart without a failed parse:
                // '[' is the legacy array, '{' is the envelope.
                string trimmed = json!.TrimStart();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    List<ColorPalette>? loaded =
                        JsonSerializer.Deserialize<List<ColorPalette>>(trimmed, JsonOptions);
                    if (loaded != null) _palettes.AddRange(loaded);
                }
                else
                {
                    Envelope? env = JsonSerializer.Deserialize<Envelope>(trimmed, JsonOptions);
                    if (env != null)
                    {
                        _palettes.AddRange(env.Palettes);
                        _active = env.Active ?? "";
                        _confirmApply = env.ConfirmApply ?? true;
                    }
                }
            }
            catch (Exception) { /* a corrupted file costs the registry, not the app */ }

            NormalizeActive();
        }

        public IReadOnlyList<ColorPalette> Palettes => _palettes;

        /// <summary>The palette off-palette is measured against, or null when none is registered.</summary>
        public ColorPalette? Active => _active.Length == 0 ? null : Find(_active);

        /// <summary>Name of <see cref="Active"/>, or an empty string. Never null — it goes straight to the UI.</summary>
        public string ActiveName => Active?.Name ?? "";

        /// <summary>Chooses the working palette. False (and no change) when the name is not registered.</summary>
        public bool SetActive(string name)
        {
            ColorPalette? p = Find(name ?? "");
            if (p == null) return false;
            _active = p.Name;
            Persist();
            return true;
        }

        /// <summary>
        /// The colours of the ACTIVE palette, keyed for matching against an audited ColorRecord.
        /// Empty when no palette is registered — an empty registry must never read as "everything is
        /// approved", which is what merging all palettes effectively did.
        /// </summary>
        public Dictionary<string, PaletteColor> ActiveColorsByKey()
        {
            var map = new Dictionary<string, PaletteColor>();
            ColorPalette? p = Active;
            if (p == null) return map;
            foreach (PaletteColor color in p.Colors)
                if (!map.ContainsKey(color.Key)) map[color.Key] = color;
            return map;
        }

        // Keeps _active pointing at something that exists. A registry pointing at a deleted palette
        // would mark every colour in the document as off-palette — a screen full of alarm with no
        // visible cause.
        private void NormalizeActive()
        {
            if (_active.Length > 0 && Find(_active) != null) return;
            _active = _palettes.Count > 0 ? _palettes[0].Name : "";
        }

        /// <summary>
        /// Whether applying a palette colour asks for confirmation first. Persisted with the palettes.
        /// </summary>
        public bool ConfirmApply => _confirmApply;

        public void SetConfirmApply(bool on)
        {
            if (_confirmApply == on) return;
            _confirmApply = on;
            Persist();
        }

        /// <summary>
        /// True when some palette already answers to <paramref name="name"/>. <paramref name="except"/>
        /// is the palette being renamed — renaming "Time A" to "Time A" is not a collision with itself.
        /// </summary>
        public bool NameTaken(string name, ColorPalette? except = null)
        {
            string wanted = (name ?? "").Trim();
            if (wanted.Length == 0) return false;
            foreach (ColorPalette p in _palettes)
            {
                if (ReferenceEquals(p, except)) continue;
                if (string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public ColorPalette? AddPalette(string name) => AddPalette(name, out _);

        /// <summary>
        /// Creates a palette. Returns null — with <paramref name="status"/> saying why — when the name
        /// is blank or already taken. Uniqueness lives here, in the ONE place every route goes
        /// through (manual create, rename, SVG import), rather than in three screens that would
        /// eventually disagree.
        /// </summary>
        public ColorPalette? AddPalette(string name, out PaletteChange status)
        {
            string wanted = (name ?? "").Trim();
            if (wanted.Length == 0) { status = PaletteChange.InvalidName; return null; }
            if (NameTaken(wanted)) { status = PaletteChange.NameTaken; return null; }

            var palette = new ColorPalette { Name = wanted };
            _palettes.Add(palette);
            // The first palette a shop creates becomes the working one by itself. Otherwise it would
            // sit there doing nothing until someone found a control they had no reason to look for.
            if (_active.Length == 0) _active = palette.Name;
            Persist();
            status = PaletteChange.Ok;
            return palette;
        }

        /// <summary>
        /// Creates a palette from a set of colours in ONE write — the SVG import route. Duplicates by
        /// key are dropped rather than rejected: the same colour appearing twenty times in a drawing
        /// is normal, and it is the palette that must hold it once.
        /// </summary>
        public ColorPalette? ImportPalette(string name, IEnumerable<PaletteColor> colors, out PaletteChange status)
        {
            ColorPalette? palette = AddPalette(name, out status);
            if (palette == null) return null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PaletteColor color in colors ?? new List<PaletteColor>())
            {
                if (color == null || !seen.Add(color.Key)) continue;
                palette.Colors.Add(color);
            }
            Persist();
            return palette;
        }

        public bool RemovePalette(string name)
        {
            int removed = _palettes.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) { NormalizeActive(); Persist(); }
            return removed > 0;
        }

        public bool RenamePalette(string oldName, string newName) =>
            RenamePalette(oldName, newName, out _);

        public bool RenamePalette(string oldName, string newName, out PaletteChange status)
        {
            ColorPalette? palette = Find(oldName);
            if (palette == null) { status = PaletteChange.NotFound; return false; }
            if (string.IsNullOrWhiteSpace(newName)) { status = PaletteChange.InvalidName; return false; }
            if (NameTaken(newName, palette)) { status = PaletteChange.NameTaken; return false; }

            bool wasActive = string.Equals(palette.Name, _active, StringComparison.OrdinalIgnoreCase);
            palette.Name = newName.Trim();
            if (wasActive) _active = palette.Name;
            Persist();
            status = PaletteChange.Ok;
            return true;
        }

        public PaletteColor? AddColor(string paletteName, PaletteColor color) =>
            AddColor(paletteName, color, out _);

        /// <summary>
        /// Adds a colour to a palette. Refuses a value that palette already holds — identity is
        /// <see cref="PaletteColor.Key"/>, so two entries with different NAMES but the same value are
        /// the same colour, and the operator is told rather than left with a silent second entry.
        /// The same value in a DIFFERENT palette is fine (§6.2) and is not checked here.
        /// </summary>
        public PaletteColor? AddColor(string paletteName, PaletteColor color, out PaletteChange status)
        {
            ColorPalette? palette = Find(paletteName);
            if (palette == null || color == null) { status = PaletteChange.NotFound; return null; }
            if (palette.Colors.Exists(c => c.Key == color.Key))
            {
                status = PaletteChange.ColorAlreadyInPalette;
                return null;
            }

            palette.Colors.Add(color);
            Persist();
            status = PaletteChange.Ok;
            return color;
        }

        public bool RemoveColor(string paletteName, string colorKey)
        {
            ColorPalette? palette = Find(paletteName);
            if (palette == null) return false;
            int removed = palette.Colors.RemoveAll(c => c.Key == colorKey);
            if (removed > 0) Persist();
            return removed > 0;
        }

        public bool RenameColor(string paletteName, string colorKey, string newName)
        {
            ColorPalette? palette = Find(paletteName);
            PaletteColor? color = palette?.Colors.Find(c => c.Key == colorKey);
            if (color == null || string.IsNullOrWhiteSpace(newName)) return false;
            color.Name = newName.Trim();
            Persist();
            return true;
        }

        public ColorPalette? Find(string name) =>
            _palettes.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Every registered colour across every palette, keyed for fast lookup.</summary>
        public Dictionary<string, PaletteColor> AllColorsByKey()
        {
            var map = new Dictionary<string, PaletteColor>();
            foreach (ColorPalette palette in _palettes)
                foreach (PaletteColor color in palette.Colors)
                    if (!map.ContainsKey(color.Key)) map[color.Key] = color;
            return map;
        }

        /// <summary>
        /// Persists the current state. Public because bulk edits (capturing a whole document's colours
        /// as one palette) build the palette directly and then save ONCE, instead of paying a full
        /// serialize per colour added — 128 colours would otherwise mean 128 file writes.
        /// </summary>
        public void Save() => Persist();

        private void Persist()
        {
            try
            {
                _save(JsonSerializer.Serialize(
                    new Envelope { Active = _active, Palettes = _palettes, ConfirmApply = _confirmApply },
                    JsonOptions));
            }
            catch (Exception) { /* an unwritable profile costs persistence, not the in-memory state */ }
        }
    }
}
