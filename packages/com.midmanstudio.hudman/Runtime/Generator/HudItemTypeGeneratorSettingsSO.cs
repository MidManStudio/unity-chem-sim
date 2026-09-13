using UnityEngine;

namespace MidManStudio.HudMan.Generator
{
    /// <summary>
    /// Project-wide settings for the Hud Item Type Generator.
    /// Create via: MidManStudio > HudMan > Hud Item Type Generator Settings
    /// </summary>
    [CreateAssetMenu(
        fileName = "HudItemTypeGeneratorSettings",
        menuName = "MidManStudio/HudMan/Hud Item Type Generator Settings",
        order = 192)]
    public class HudItemTypeGeneratorSettingsSO : ScriptableObject
    {
        [Header("Output Path")]
        public string enumOutputPath =
            "packages/com.midmanstudio.hudman/Runtime/Generated/HudItemType.cs";

        [Tooltip("Commit this to source control. Keeps enum values stable across regenerations.")]
        public string lockFilePath =
            "Assets/MidManStudio/Generated/HudMan/HudItemTypeLock.json";

        [Header("Block Sizing")]
        [Min(10)]
        public int minimumBlockSize = 50;

        [Header("Namespace")]
        public string generatedNamespace = "MidManStudio.HudMan.Generated";

        [Header("Auto-Generate")]
        public bool autoGenerateOnAssetChange = false;
    }
}
