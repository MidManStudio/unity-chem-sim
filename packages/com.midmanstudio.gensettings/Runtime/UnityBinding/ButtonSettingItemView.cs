using UnityEngine.UI;

namespace MidManStudio.GenSettings.UnityBinding
{
    /// <summary>
    /// Buttons have no value -- ApplyToWidget is a no-op (nothing to push
    /// onto a button from state), same as the original ButtonSettingItem's
    /// empty ApplyValue(). A click calls SettingsRuntime.TriggerAction,
    /// which fires this settingId's ISettingApplier with a null value --
    /// the applier IS the action (e.g. "open the HUD editor"), replacing
    /// the original's own switch(settingId) inside OnButtonClicked().
    /// </summary>
    public class ButtonSettingItemView : SettingItemViewBase
    {
        [UnityEngine.Header("Button Reference")]
        [UnityEngine.SerializeField] private Button button;

        private void Awake() => button.onClick.AddListener(OnClicked);

        private void OnClicked() => runtime?.TriggerAction(SettingId);

        protected override void ApplyToWidget(object value) { /* no-op: buttons have no value */ }
    }
}
