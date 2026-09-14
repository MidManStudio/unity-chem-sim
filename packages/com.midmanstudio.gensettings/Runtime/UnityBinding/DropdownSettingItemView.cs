using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MidManStudio.GenSettings.Core;

namespace MidManStudio.GenSettings.UnityBinding
{
    public class DropdownSettingItemView : SettingItemViewBase
    {
        [Header("Dropdown Reference")]
        [UnityEngine.SerializeField] private TMP_Dropdown dropdown;

        public override void Bind(SettingsRuntime runtime)
        {
            // Populate options before the base call applies the current value,
            // otherwise SetValueWithoutNotify(index) has nothing to select.
            this.runtime = runtime;
            definition = runtime.GetDefinition(settingId);
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(definition.DropdownOptions));

            base.Bind(runtime);
        }

        private void Awake() => dropdown.onValueChanged.AddListener(OnDropdownChanged);

        protected override void ApplyToWidget(object value) =>
            dropdown.SetValueWithoutNotify(value is int i ? i : 0);

        private void OnDropdownChanged(int index) => ReportValue(index);
    }
}
