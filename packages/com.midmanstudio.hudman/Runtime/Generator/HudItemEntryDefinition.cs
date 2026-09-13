using UnityEngine;

namespace MidManStudio.HudMan.Generator
{
    /// <summary>
    /// One entry in a <see cref="HudItemTypeProviderSO"/>. Same pinning
    /// and auto-assign rules as com.midmanstudio.utilities' FXEntryDefinition
    /// -- deliberately not depending on that package just to reuse its
    /// IArrayElementTitle inspector polish, so this list draws with the
    /// default Unity inspector until/unless that's worth adding here too.
    /// </summary>
    [System.Serializable]
    public class HudItemEntryDefinition
    {
        [Tooltip("Becomes the enum member name. PascalCase, no spaces.\n" +
                 "e.g. HealthBar, AmmoCounter, MiniMap")]
        public string entryName = string.Empty;

        [Tooltip("Optional comment written next to the enum member.")]
        public string comment = string.Empty;

        [Tooltip("-1 = auto-assigned by generator.\n" +
                 ">=0 = pinned to this offset within the provider's block.\n" +
                 "Pin entries referenced from serialised inspector data.")]
        public int explicitOffset = -1;
    }
}
