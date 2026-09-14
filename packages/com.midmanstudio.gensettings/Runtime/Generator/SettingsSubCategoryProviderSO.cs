using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.GenSettings.Generator
{
    /// <summary>Same role as <see cref="SettingsCategoryProviderSO"/>, for the paired SettingsSubCategory enum.</summary>
    [CreateAssetMenu(
        fileName = "SettingsSubCategoryProvider",
        menuName = "MidManStudio/GenSettings/Settings SubCategory Provider",
        order = 194)]
    public class SettingsSubCategoryProviderSO : ScriptableObject
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
