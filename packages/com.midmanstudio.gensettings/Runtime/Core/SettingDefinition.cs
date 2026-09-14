using System.Collections.Generic;
using MidManStudio.GenSettings.Generated;

namespace MidManStudio.GenSettings.Core
{
    /// <summary>
    /// Mirrors the original SettingItemData field-for-field, minus the two
    /// genuinely Unity-typed fields: <c>Sprite icon</c> moves to
    /// <see cref="MidManStudio.GenSettings.UnityBinding.SettingItemView"/>
    /// (display concern, not data), and <c>InputField.ContentType</c>
    /// becomes <see cref="SettingsInputContentType"/> so Core never needs
    /// a UnityEngine.UI reference.
    ///
    /// Every control-type's default/config fields live on every instance
    /// with neutral defaults for the ones a given ControlType doesn't
    /// use -- same "one fixed shape per family" rule as Questly's
    /// objective/prereq builders.
    /// </summary>
    public sealed class SettingDefinition
    {
        public string SettingId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;

        public SettingsCategory Category { get; set; }
        public SettingsSubCategory SubCategory { get; set; }
        public SettingsContext AvailableIn { get; set; } = SettingsContext.All;
        public SettingControlType ControlType { get; set; }
        public SettingFlags Flags { get; set; }

        public bool DefaultBoolValue { get; set; }
        public float DefaultFloatValue { get; set; }
        public int DefaultIntValue { get; set; }
        public string DefaultStringValue { get; set; } = string.Empty;
        public List<string> DropdownOptions { get; set; } = new();
        public List<string> MultiOptionLabels { get; set; } = new();

        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public bool WholeNumbers { get; set; }

        public int CharacterLimit { get; set; } = 50;
        public SettingsInputContentType ContentType { get; set; } = SettingsInputContentType.Standard;

        public bool HasFlag(SettingFlags flag) => (Flags & flag) != 0;
        public bool IsAvailableInContext(SettingsContext context) => (AvailableIn & context) != 0;

        /// <summary>
        /// The one field among DefaultBoolValue/DefaultFloatValue/DefaultIntValue/
        /// DefaultStringValue that's actually meaningful, boxed, based on
        /// ControlType -- Button has no value at all (returns null),
        /// matching the original ButtonSettingItem's "Buttons don't have
        /// values" comment exactly.
        /// </summary>
        public object? GetDefaultValue() => ControlType switch
        {
            SettingControlType.Toggle => DefaultBoolValue,
            SettingControlType.Slider => DefaultFloatValue,
            SettingControlType.Dropdown => DefaultIntValue,
            SettingControlType.MultiOption => DefaultIntValue,
            SettingControlType.InputField => DefaultStringValue,
            SettingControlType.Button => null,
            _ => null,
        };
    }
}
