using UnityEngine;
using UnityEngine.UI;

namespace MidManStudio.GenSettings.UnityBinding
{
    public class ToggleSettingItemView : SettingItemViewBase
    {
        [Header("Toggle Reference")]
        [SerializeField] private Toggle toggle;

        private void Awake() => toggle.onValueChanged.AddListener(OnToggleChanged);

        protected override void ApplyToWidget(object value) =>
            toggle.SetIsOnWithoutNotify(value is bool b && b);

        private void OnToggleChanged(bool value) => ReportValue(value);
    }
}
