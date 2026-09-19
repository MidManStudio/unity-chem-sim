// com.midmanstudio.gensettings/Samples~/editHUD_hudman_integration_example.cs
//
// The one place gensettings intentionally references com.midmanstudio.hudman
// directly. Everywhere else the two packages know nothing about each other
// -- gensettings ships zero built-in ISettingApplier implementations, the
// same relationship Questly has with IQuestObjectiveEvaluator. This file
// lives in Samples~ specifically because that folder is never part of
// either package's own compiled assembly: it's host integration code,
// meant to be copied into a game's own bootstrap, not shipped inside
// gensettings.Runtime or hudman.Runtime. A game that only wants gensettings
// (no HUD editing) is completely unaffected by this file's existence.
//
// Demonstrates the "editHUD" Button setting ButtonSettingItemView's own doc
// comment already gestures at ("the applier IS the action, e.g. 'open the
// HUD editor'"), wired to HudLayoutRuntime.ToggleEditMode() -- the edit-mode
// concept added to hudman specifically to give this button something real
// to do (previously HudItemDraggableAdapter allowed dragging unconditionally,
// with no on/off switch at all).
//
// SettingsCategory.General / SettingsSubCategory.General are used below
// because that's *all* that exists until a game registers its own
// SettingsCategoryProviderSO and regenerates -- swap in a real HUD-related
// category once one exists.

using MidManStudio.GenSettings.Core;
using MidManStudio.GenSettings.Generated;
using MidManStudio.HudMan.Core;

namespace MidManStudio.GenSettings.Samples
{
    public static class EditHudIntegrationExample
    {
        /// <summary>
        /// Call once at startup: after both runtimes exist, and after every
        /// HudItemView in the scene has already had Bind() called on it (the
        /// same ordering HudItemView's own doc comment already requires for
        /// LoadFromMdix/RestoreState). Toggling edit mode before any
        /// HudItemView exists to react to EditModeChanged isn't wrong, just
        /// a silent no-op -- there would be nothing to actually let the
        /// player drag yet.
        /// </summary>
        public static void RegisterEditHudButton(SettingsRuntime settingsRuntime, HudLayoutRuntime hudLayoutRuntime)
        {
            settingsRuntime.RegisterDefinition(new SettingDefinition
            {
                SettingId = "editHUD",
                DisplayName = "Edit HUD Layout",
                Description = "Reposition and resize HUD elements.",
                Category = SettingsCategory.General,
                SubCategory = SettingsSubCategory.General,
                ControlType = SettingControlType.Button,
                AvailableIn = SettingsContext.InGame,
            });

            settingsRuntime.RegisterApplier("editHUD", _ => hudLayoutRuntime.ToggleEditMode());
        }
    }
}
