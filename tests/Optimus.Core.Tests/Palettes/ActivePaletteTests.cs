using System.Collections.Generic;
using Optimus.Core.Audit;
using Optimus.Core.Palettes;
using Xunit;

namespace Optimus.Core.Tests.Palettes
{
    /// <summary>
    /// The registry used to answer "is this colour registered?" by looking in EVERY palette at once.
    /// That made the answer useless in the one situation the feature exists for: a shop that has
    /// palettes for several clients. A colour belonging to client B is not "in the palette" while
    /// you are working on client A's file — it is exactly the mistake the operator is trying to catch.
    ///
    /// So one palette is the ACTIVE one, and off-palette is measured against that one alone.
    /// </summary>
    public sealed class ActivePaletteTests
    {
        private static PaletteColor Rgb(string name, string hex) =>
            new PaletteColor { Name = name, Model = ColorModel.Rgb, Hex = hex };

        [Fact]
        public void NoPalettes_HasNoActiveOne()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            Assert.Null(r.Active);
            Assert.Equal("", r.ActiveName);
        }

        [Fact]
        public void FirstPaletteCreated_BecomesActiveOnItsOwn()
        {
            // Otherwise the very first palette a shop creates would sit there doing nothing until
            // someone found a control they had no reason to look for.
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");

            Assert.Equal("Cliente X", r.ActiveName);
        }

        [Fact]
        public void SecondPaletteCreated_DoesNotStealTheActiveSlot()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");
            r.AddPalette("Cliente Y");

            Assert.Equal("Cliente X", r.ActiveName);
        }

        [Fact]
        public void SetActive_ChoosesIt_AndIsCaseInsensitiveLikeEverythingElseHere()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");
            r.AddPalette("Cliente Y");

            Assert.True(r.SetActive("cliente y"));
            Assert.Equal("Cliente Y", r.ActiveName);
        }

        [Fact]
        public void SetActive_UnknownName_IsRefusedAndChangesNothing()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");

            Assert.False(r.SetActive("nao existe"));
            Assert.Equal("Cliente X", r.ActiveName);
        }

        [Fact]
        public void RemovingTheActivePalette_PromotesAnotherOne()
        {
            // Leaving the registry pointing at a palette that no longer exists would make every colour
            // read as off-palette — a screen full of red for a reason nobody could see.
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");
            r.AddPalette("Cliente Y");

            r.RemovePalette("Cliente X");

            Assert.Equal("Cliente Y", r.ActiveName);
        }

        [Fact]
        public void RemovingTheLastPalette_LeavesNoActiveOne()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");
            r.RemovePalette("Cliente X");

            Assert.Null(r.Active);
            Assert.Equal("", r.ActiveName);
        }

        [Fact]
        public void RenamingTheActivePalette_KeepsItActive()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");
            r.RenamePalette("Cliente X", "Cliente X - 2026");

            Assert.Equal("Cliente X - 2026", r.ActiveName);
        }

        [Fact]
        public void ActiveColorsByKey_SeesOnlyTheActivePalette()
        {
            PaletteRegistry r = PaletteRegistry.InMemory();
            r.AddPalette("Cliente X");
            r.AddColor("Cliente X", Rgb("laranja", "EA580C"));
            r.AddPalette("Cliente Y");
            r.AddColor("Cliente Y", Rgb("azul", "2F5FB3"));

            Dictionary<string, PaletteColor> keys = r.ActiveColorsByKey();

            Assert.Single(keys);
            Assert.Contains("Rgb|EA580C", keys.Keys);
            Assert.DoesNotContain("Rgb|2F5FB3", keys.Keys);
        }

        [Fact]
        public void ActiveColorsByKey_WithNoPalettes_IsEmptyRatherThanEverything()
        {
            // An empty registry must not report "everything is registered". The colour table has to be
            // able to say "no palette yet" instead of quietly approving every colour in the file.
            PaletteRegistry r = PaletteRegistry.InMemory();
            Assert.Empty(r.ActiveColorsByKey());
        }

        [Fact]
        public void ActiveChoice_SurvivesAReload()
        {
            string? blob = null;
            PaletteRegistry r = PaletteRegistry.Load(() => blob, s => blob = s);
            r.AddPalette("Cliente X");
            r.AddPalette("Cliente Y");
            r.SetActive("Cliente Y");

            PaletteRegistry again = PaletteRegistry.Load(() => blob, s => blob = s);

            Assert.Equal("Cliente Y", again.ActiveName);
            Assert.Equal(2, again.Palettes.Count);
        }

        [Fact]
        public void OldBareArrayFile_StillLoads()
        {
            // Files written before the active-palette envelope existed are a plain JSON array. Refusing
            // to read them would silently wipe a shop's registered palettes on upgrade.
            const string legacy =
                "[{\"Name\":\"Cliente X\",\"PreferredModel\":\"Rgb\"," +
                "\"Colors\":[{\"Name\":\"laranja\",\"Model\":\"Rgb\",\"Hex\":\"EA580C\"," +
                "\"Components\":\"\",\"SpotName\":\"\"}]}]";

            PaletteRegistry r = PaletteRegistry.Load(() => legacy, _ => { });

            Assert.Single(r.Palettes);
            Assert.Equal("Cliente X", r.Palettes[0].Name);
            Assert.Single(r.Palettes[0].Colors);
            // With nothing recorded, the first palette is the sensible active one.
            Assert.Equal("Cliente X", r.ActiveName);
        }
    }
}
