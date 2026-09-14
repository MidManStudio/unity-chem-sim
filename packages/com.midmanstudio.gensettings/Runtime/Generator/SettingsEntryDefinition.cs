using UnityEngine;

namespace MidManStudio.GenSettings.Generator
{
    /// <summary>
    /// One entry in a <see cref="SettingsCategoryProviderSO"/> or
    /// <see cref="SettingsSubCategoryProviderSO"/>. Same pinning and
    /// auto-assign rules as com.midmanstudio.hudman's HudItemEntryDefinition
    /// -- duplicated rather than shared across packages on purpose, so a
    /// game can use gensettings without pulling in hudman or vice versa.
    /// </summary>
    [System.Serializable]
    public class SettingsEntryDefinition
    {
        [Tooltip("Becomes the enum member name. PascalCase, no spaces.\ne.g. Graphics, AudioMusic, Gameplay")]
        public string entryName = string.Empty;

        [Tooltip("Optional comment written next to the enum member.")]
        public string comment = string.Empty;

        [Tooltip("-1 = auto-assigned by generator.\n>=0 = pinned to this offset within the provider's block.")]
        public int explicitOffset = -1;
    }
}
