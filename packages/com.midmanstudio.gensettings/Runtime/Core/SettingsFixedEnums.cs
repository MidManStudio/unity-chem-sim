using System;

namespace MidManStudio.GenSettings.Core
{
    /// <summary>
    /// Contexts a setting can be surfaced in. [Flags] so one setting can
    /// legitimately appear in more than one.
    /// </summary>
    [Flags]
    public enum SettingsContext
    {
        None = 0,
        MainMenu = 1 << 0,
        OfflineLobby = 1 << 1,
        InGame = 1 << 2,
        All = MainMenu | OfflineLobby | InGame,
    }

    /// <summary>
    /// The six control widgets com.midmanstudio.gensettings ships a
    /// UnityBinding view for. Fixed, not code-generated -- unlike
    /// SettingsCategory a game can't usefully invent a 7th control kind
    /// without new binding code to go with it, so there's nothing for a
    /// provider asset to extend here.
    /// </summary>
    public enum SettingControlType
    {
        Toggle,
        Slider,
        InputField,
        Dropdown,
        Button,
        MultiOption,
    }

    /// <summary>Special-handling flags a host can react to (e.g. show a "restart required" banner). [Flags] so more than one can apply.</summary>
    [Flags]
    public enum SettingFlags
    {
        None = 0,
        RequiresRestart = 1 << 0,
        RequiresReload = 1 << 1,
        CloudSynced = 1 << 2,
        DeviceSpecific = 1 << 3,
        Advanced = 1 << 4,
    }

    /// <summary>
    /// Mirrors UnityEngine.UI.InputField.ContentType / TMP_InputField.ContentType
    /// member-for-member so Core never needs a UnityEngine.UI reference.
    /// The InputField UnityBinding view converts to/from the real type.
    /// </summary>
    public enum SettingsInputContentType
    {
        Standard,
        Autocorrected,
        IntegerNumber,
        DecimalNumber,
        Alphanumeric,
        Name,
        EmailAddress,
        Password,
        Pin,
        Custom,
    }
}
