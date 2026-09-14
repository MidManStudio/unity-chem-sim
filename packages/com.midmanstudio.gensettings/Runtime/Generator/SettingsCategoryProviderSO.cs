using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.GenSettings.Generator
{
    /// <summary>
    /// Contributes a block of entries to the generated SettingsCategory
    /// enum. One of these per package/game -- gensettings itself ships
    /// zero concrete categories beyond the pinned "General" fallback
    /// (block 0-9 at priority 0), so a real project always needs at
    /// least one of these to have a category list worth showing.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SettingsCategoryProvider",
        menuName = "MidManStudio/GenSettings/Settings Category Provider",
        order = 193)]
    public class SettingsCategoryProviderSO : ScriptableObject
    {
        [Header("Identity")]
        public string packageId = "com.mygame";
        public string displayName = "My Game";

        [Header("Block Priority")]
        public int priority = 100;

        [Header("Entries")]
        public List<SettingsEntryDefinition> entries = new();

        public int EntryCount => entries?.Count ?? 0;
    }
}
