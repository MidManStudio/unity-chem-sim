using UnityEngine;

namespace MidManStudio.GenSettings.Generator
{
    /// <summary>
    /// Project-wide settings for the Settings Enum Generator.
    /// Create via: MidManStudio > GenSettings > Settings Enum Generator Settings
    /// </summary>
    [CreateAssetMenu(
        fileName = "SettingsEnumGeneratorSettings",
        menuName = "MidManStudio/GenSettings/Settings Enum Generator Settings",
        order = 195)]
    public class SettingsEnumGeneratorSettingsSO : ScriptableObject
    {
        [Header("Output Paths")]
        public string categoryEnumOutputPath =
            "packages/com.midmanstudio.gensettings/Runtime/Generated/SettingsCategory.cs";
        public string subCategoryEnumOutputPath =
            "packages/com.midmanstudio.gensettings/Runtime/Generated/SettingsSubCategory.cs";

        [Tooltip("Commit this to source control. Keeps enum values stable across regenerations.")]
        public string lockFilePath =
            "Assets/MidManStudio/Generated/GenSettings/SettingsEnumLock.json";

        [Header("Block Sizing")]
        [Min(10)]
        public int minimumBlockSize = 50;

        [Header("Namespace")]
        public string generatedNamespace = "MidManStudio.GenSettings.Generated";

        [Header("Auto-Generate")]
        public bool autoGenerateOnAssetChange = false;
    }
}
