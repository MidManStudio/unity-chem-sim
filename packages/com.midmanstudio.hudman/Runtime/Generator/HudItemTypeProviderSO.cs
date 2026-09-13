using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.HudMan.Generator
{
    /// <summary>
    /// Contributes a block of entries to the generated HudItemType enum.
    /// One of these per package/game that needs its own HUD item catalog
    /// -- hudman itself ships zero concrete HUD items (see the pinned
    /// "Unassigned" placeholder in Runtime/Generated/HudItemType.cs,
    /// reserved to block 0-9 at priority 0), so a real project always
    /// needs at least one of these to have anything worth registering
    /// with HudLayoutRuntime.
    /// </summary>
    [CreateAssetMenu(
        fileName = "HudItemTypeProvider",
        menuName = "MidManStudio/HudMan/Hud Item Type Provider",
        order = 191)]
    public class HudItemTypeProviderSO : ScriptableObject
    {
        [Header("Identity")]
        public string packageId = "com.mygame";
        public string displayName = "My Game";

        [Header("Block Priority")]
        public int priority = 100;

        [Header("Entries")]
        public List<HudItemEntryDefinition> entries = new();

        public int EntryCount => entries?.Count ?? 0;
    }
}
