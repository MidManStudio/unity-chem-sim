using System.Collections.Generic;
using MidManStudio.GenSettings.Core;

namespace MidManStudio.GenSettings.Core
{
    /// <summary>
    /// One setting's persisted value. Carries all four typed fields with
    /// neutral defaults for the three a given setting doesn't use --
    /// same "one fixed shape per family" rule SettingDefinition itself
    /// follows, so there's no discriminator field to keep in sync
    /// separately: <see cref="ToValue"/>/<see cref="FromValue"/> both key
    /// off the setting's already-registered ControlType.
    /// </summary>
    public sealed class SettingValueRow
    {
        public string SettingId { get; set; } = string.Empty;
        public bool BoolValue { get; set; }
        public float FloatValue { get; set; }
        public int IntValue { get; set; }
        public string StringValue { get; set; } = string.Empty;

        public static SettingValueRow FromValue(string settingId, SettingControlType controlType, object? value)
        {
            var row = new SettingValueRow { SettingId = settingId };
            switch (controlType)
            {
                case SettingControlType.Toggle: row.BoolValue = value is bool b && b; break;
                case SettingControlType.Slider: row.FloatValue = value is float f ? f : 0f; break;
                case SettingControlType.Dropdown:
                case SettingControlType.MultiOption: row.IntValue = value is int i ? i : 0; break;
                case SettingControlType.InputField: row.StringValue = value as string ?? string.Empty; break;
                case SettingControlType.Button: break; // no value to carry
            }
            return row;
        }

        public object? ToValue(SettingControlType controlType) => controlType switch
        {
            SettingControlType.Toggle => BoolValue,
            SettingControlType.Slider => FloatValue,
            SettingControlType.Dropdown => IntValue,
            SettingControlType.MultiOption => IntValue,
            SettingControlType.InputField => StringValue,
            SettingControlType.Button => null,
            _ => null,
        };
    }

    /// <summary>
    /// Host-owned persistence payload. <see cref="SettingsRuntime.CaptureState"/>
    /// returns this, <see cref="SettingsRuntime.RestoreState"/> takes one
    /// back. A plain POCO, not a file -- same relationship
    /// QuestProgressSnapshot has with QuestRuntime.
    /// </summary>
    public sealed class SettingsSnapshot
    {
        public List<SettingValueRow> Values { get; set; } = new();
    }

    /// <summary>Nested so serialization naturally produces dotted <c>settings.values</c> paths.</summary>
    public sealed class SettingsGroup
    {
        public List<SettingValueRow> Values { get; set; } = new();
    }

    /// <summary>
    /// Package-owned persistence format. Only <see cref="SettingsRuntime.SaveToMdix"/>
    /// / <see cref="SettingsRuntime.LoadFromMdix"/> touch this type. See
    /// <c>Samples~/example_settings_save.mdix</c> for the resulting shape.
    /// </summary>
    public sealed class SettingsSaveFile
    {
        public string SavedAt { get; set; } = string.Empty;
        public SettingsGroup Settings { get; set; } = new();
    }
}
