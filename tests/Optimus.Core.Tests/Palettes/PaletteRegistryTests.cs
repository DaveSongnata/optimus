using Optimus.Core.Audit;
using Optimus.Core.Palettes;
using Xunit;

namespace Optimus.Core.Tests.Palettes
{
    public class PaletteRegistryTests
    {
        [Fact]
        public void AddPalette_and_AddColor_round_trip_through_persistence()
        {
            string? blob = null;
            PaletteRegistry registry = PaletteRegistry.Load(() => blob, s => blob = s);

            registry.AddPalette("Paleta1");
            registry.AddColor("Paleta1", new PaletteColor { Name = "cor 1", Model = ColorModel.Rgb, Hex = "FF0000" });

            Assert.NotNull(blob);

            // A second registry backed by the SAME store must see what the first wrote.
            PaletteRegistry reloaded = PaletteRegistry.Load(() => blob, s => blob = s);
            Assert.Single(reloaded.Palettes);
            Assert.Equal("cor 1", reloaded.Palettes[0].Colors[0].Name);
        }

        [Fact]
        public void RemovePalette_returns_false_for_unknown_name()
        {
            PaletteRegistry registry = PaletteRegistry.InMemory();
            Assert.False(registry.RemovePalette("nope"));
        }

        [Fact]
        public void RenameColor_updates_the_name_but_not_the_key()
        {
            PaletteRegistry registry = PaletteRegistry.InMemory();
            registry.AddPalette("P1");
            var color = new PaletteColor { Name = "cor 1", Model = ColorModel.Rgb, Hex = "00FF00" };
            registry.AddColor("P1", color);

            Assert.True(registry.RenameColor("P1", color.Key, "verde"));
            Assert.Equal("verde", registry.Find("P1")!.Colors[0].Name);
        }

        [Fact]
        public void AllColorsByKey_merges_colors_across_palettes()
        {
            PaletteRegistry registry = PaletteRegistry.InMemory();
            registry.AddPalette("P1");
            registry.AddPalette("P2");
            registry.AddColor("P1", new PaletteColor { Name = "a", Model = ColorModel.Rgb, Hex = "111111" });
            registry.AddColor("P2", new PaletteColor { Name = "b", Model = ColorModel.Cmyk, Hex = "222222" });

            var map = registry.AllColorsByKey();
            Assert.Equal(2, map.Count);
        }

        [Fact]
        public void Corrupted_blob_leaves_registry_empty_instead_of_throwing()
        {
            PaletteRegistry registry = PaletteRegistry.Load(() => "{ not json", s => { });
            Assert.Empty(registry.Palettes);
        }
    }
}
