using Optimus.Core.Fonts;
using Xunit;

namespace Optimus.Core.Tests.Fonts
{
    public class FontRegistryTests
    {
        [Fact]
        public void Add_and_reload_round_trips_through_persistence()
        {
            string? blob = null;
            FontRegistry registry = FontRegistry.Load(() => blob, s => blob = s);
            registry.Add("Arial", "padrão");

            FontRegistry reloaded = FontRegistry.Load(() => blob, s => blob = s);
            Assert.True(reloaded.IsRegistered("Arial"));
            Assert.Equal("padrão", reloaded.Fonts[0].Note);
        }

        [Fact]
        public void Add_is_idempotent_by_name_case_insensitive()
        {
            FontRegistry registry = FontRegistry.InMemory();
            registry.Add("Arial");
            registry.Add("ARIAL");
            Assert.Single(registry.Fonts);
        }

        [Fact]
        public void ImportNames_splits_lines_dedupes_and_skips_already_registered()
        {
            FontRegistry registry = FontRegistry.InMemory();
            registry.Add("Arial");

            int added = registry.ImportNames("Arial\nMontserrat\nMontserrat\n\nOpen Sans");

            Assert.Equal(2, added); // Montserrat, Open Sans — Arial already registered, dupe collapsed
            Assert.Equal(3, registry.Fonts.Count);
        }

        [Fact]
        public void Remove_returns_false_for_unknown_font()
        {
            FontRegistry registry = FontRegistry.InMemory();
            Assert.False(registry.Remove("nope"));
        }
    }
}
