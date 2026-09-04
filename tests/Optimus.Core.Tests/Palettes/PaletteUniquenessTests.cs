using Optimus.Core.Audit;
using Optimus.Core.Palettes;
using Xunit;

namespace Optimus.Core.Tests.Palettes
{
    /// <summary>
    /// Uniqueness lives in the registry, not in a screen. Three routes create palettes and colours —
    /// manual create, rename, SVG import — and a rule enforced in two of them is a trap waiting for
    /// the third (O25).
    /// </summary>
    public class PaletteUniquenessTests
    {
        private static PaletteColor Rgb(string name, string hex) =>
            new PaletteColor { Name = name, Model = ColorModel.Rgb, Hex = hex };

        [Fact]
        public void Two_palettes_cannot_share_a_name()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Time A");

            ColorPalette? second = r.AddPalette("Time A", out PaletteChange status);

            Assert.Null(second);
            Assert.Equal(PaletteChange.NameTaken, status);
            Assert.Single(r.Palettes);
        }

        [Fact]
        public void The_name_check_ignores_case_and_surrounding_space()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Time A");

            Assert.Null(r.AddPalette("  time a  ", out PaletteChange status));
            Assert.Equal(PaletteChange.NameTaken, status);
        }

        [Fact]
        public void A_blank_name_is_refused_rather_than_turned_into_Paleta()
        {
            // It used to become "Paleta" silently, so an operator who hit Enter early got a palette
            // they never named and could not tell apart from the next one.
            PaletteRegistry r = PaletteRegistry.InMemory();

            Assert.Null(r.AddPalette("   ", out PaletteChange status));
            Assert.Equal(PaletteChange.InvalidName, status);
            Assert.Empty(r.Palettes);
        }

        [Fact]
        public void Renaming_onto_an_existing_name_is_refused_and_changes_nothing()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Time A");
            r.AddPalette("Time B");

            Assert.False(r.RenamePalette("Time B", "Time A", out PaletteChange status));
            Assert.Equal(PaletteChange.NameTaken, status);
            Assert.Equal("Time B", r.Palettes[1].Name);
        }

        [Fact]
        public void Renaming_a_palette_to_its_own_name_is_allowed()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Time A");

            Assert.True(r.RenamePalette("Time A", "TIME A", out PaletteChange status));
            Assert.Equal(PaletteChange.Ok, status);
            Assert.Equal("TIME A", r.Palettes[0].Name);
        }

        [Fact]
        public void The_same_colour_twice_in_one_palette_is_refused_even_under_another_name()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Time A");
            r.AddColor("Time A", Rgb("Vermelho Principal", "FF0000"));

            PaletteColor? again = r.AddColor("Time A", Rgb("Vermelho Uniforme", "FF0000"), out PaletteChange status);

            Assert.Null(again);
            Assert.Equal(PaletteChange.ColorAlreadyInPalette, status);
            Assert.Single(r.Palettes[0].Colors);
            Assert.Equal("Vermelho Principal", r.Palettes[0].Colors[0].Name);
        }

        [Fact]
        public void The_same_colour_in_two_different_palettes_is_fine()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Time A");
            r.AddPalette("Time B");

            Assert.NotNull(r.AddColor("Time A", Rgb("vermelho", "FF0000")));
            Assert.NotNull(r.AddColor("Time B", Rgb("vermelho", "FF0000")));
        }

        [Fact]
        public void A_colour_may_have_no_name_at_all()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Time A");

            Assert.NotNull(r.AddColor("Time A", Rgb("", "FF0000"), out PaletteChange status));
            Assert.Equal(PaletteChange.Ok, status);
            Assert.Equal("", r.Palettes[0].Colors[0].Name);
        }

        [Fact]
        public void Import_drops_repeats_instead_of_refusing_the_whole_file()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();

            ColorPalette? p = r.ImportPalette("Do SVG",
                new[] { Rgb("", "FF0000"), Rgb("", "0000FF"), Rgb("", "FF0000") },
                out PaletteChange status);

            Assert.Equal(PaletteChange.Ok, status);
            Assert.Equal(2, p!.Colors.Count);
        }

        [Fact]
        public void Confirm_before_applying_defaults_to_on_and_survives_a_reload()
        {
            string? blob = null;
            PaletteRegistry r = PaletteRegistry.Load(() => blob, s => blob = s);
            Assert.True(r.ConfirmApply);

            r.SetConfirmApply(false);
            PaletteRegistry reloaded = PaletteRegistry.Load(() => blob, s => blob = s);

            Assert.False(reloaded.ConfirmApply);
        }

        [Fact]
        public void A_palette_file_written_before_the_setting_existed_still_asks()
        {
            // The safe default has to survive the upgrade: reading "not stated" as "do not ask" would
            // silently disarm the confirmation on every machine that already had palettes.
            string legacy = "[{\"Name\":\"Time A\",\"PreferredModel\":\"Rgb\",\"Colors\":[]}]";
            PaletteRegistry r = PaletteRegistry.Load(() => legacy, _ => { });

            Assert.True(r.ConfirmApply);
            Assert.Single(r.Palettes);
        }
    }
}
