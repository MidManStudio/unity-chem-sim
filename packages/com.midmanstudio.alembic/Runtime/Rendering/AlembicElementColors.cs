using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.Alembic.Rendering
{
    /// <summary>
    /// Standard CPK/Jmol element coloring — the same convention used
    /// broadly across chemistry visualization tools, not something
    /// invented for this package. Covers exactly the 20 real elements
    /// chemistry_core's own element_data.rs table has (see that file's
    /// own doc for why those 20 specifically). Custom-registered
    /// elements (atomic number >= 1000 — see
    /// <see cref="MidManStudio.Alembic.Core.ChemistryLib.RegisterElement(int,float,float,float,float,float,float,float)"/>)
    /// have no defined color here — <see cref="DefaultColor"/> (a
    /// deliberately eye-catching magenta, matching the "missing
    /// texture"/"unassigned" convention used broadly elsewhere) is
    /// returned instead, so a fictional reagent draws as something
    /// visibly present rather than invisible or silently wrong-colored.
    /// A game is free to override this per custom element on its own
    /// side; nothing here assumes it has to use these defaults.
    /// </summary>
    public static class AlembicElementColors
    {
        /// <summary>
        /// Deliberately eye-catching — an element with no defined color
        /// should look obviously undefined, not quietly blend in as if
        /// someone had chosen gray on purpose.
        /// </summary>
        public static readonly Color DefaultColor = new Color(1f, 0f, 1f, 1f);

        private static readonly Dictionary<int, Color> Table = new Dictionary<int, Color>
        {
            { 1,  HtmlColor(0xFFFFFF) }, // Hydrogen — white
            { 2,  HtmlColor(0xD9FFFF) }, // Helium — pale cyan
            { 3,  HtmlColor(0xCC80FF) }, // Lithium — violet
            { 4,  HtmlColor(0xC2FF00) }, // Beryllium — yellow-green
            { 5,  HtmlColor(0xFFB5B5) }, // Boron — salmon
            { 6,  HtmlColor(0x909090) }, // Carbon — grey (classic CPK black softened for visibility)
            { 7,  HtmlColor(0x3050F8) }, // Nitrogen — blue
            { 8,  HtmlColor(0xFF0D0D) }, // Oxygen — red
            { 15, HtmlColor(0xFF8000) }, // Phosphorus — orange
            { 16, HtmlColor(0xFFFF30) }, // Sulfur — yellow
            { 26, HtmlColor(0xE06633) }, // Iron — orange-brown/rust
            { 29, HtmlColor(0xC88033) }, // Copper — copper/brown-orange
            { 30, HtmlColor(0x7D80B0) }, // Zinc — slate blue-grey
            { 33, HtmlColor(0xBD80E3) }, // Arsenic — purple
            { 47, HtmlColor(0xC0C0C0) }, // Silver — silver
            { 50, HtmlColor(0x668080) }, // Tin — greyish teal
            { 51, HtmlColor(0x9E63B5) }, // Antimony — purple-grey
            { 79, HtmlColor(0xFFD123) }, // Gold — gold
            { 80, HtmlColor(0xB8B8D0) }, // Mercury — pale lavender-grey
            { 82, HtmlColor(0x575961) }, // Lead — dark grey
        };

        /// <summary>Element color by atomic number, or <see cref="DefaultColor"/> for anything not in the table above.</summary>
        public static Color GetColor(int atomicNumber) =>
            Table.TryGetValue(atomicNumber, out var c) ? c : DefaultColor;

        private static Color HtmlColor(int rgb) => new Color32(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF),
            255);
    }
}
