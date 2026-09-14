using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MidManStudio.GenSettings.Core;

namespace MidManStudio.GenSettings.UnityBinding
{
    /// <summary>
    /// Radio-button-style single-select (e.g. Quality: Low/Medium/High/Ultra)
    /// -- the original MultiOptionSettingItem instantiates one toggle per
    /// label into a ToggleGroup rather than using a dropdown, so this
    /// view does too, for real behavioural fidelity rather than quietly
    /// collapsing it into DropdownSettingItemView because the two looked
    /// similar on paper.
    /// </summary>
    public class MultiOptionSettingItemView : SettingItemViewBase
    {
        [Header("Multi-Option References")]
        [SerializeField] private Transform optionsContainer;
        [SerializeField] private GameObject optionTogglePrefab; // must carry a Toggle + a TMP_Text label

        private readonly List<Toggle> _optionToggles = new();
        private ToggleGroup _toggleGroup;
        private bool _building;

        public override void Bind(SettingsRuntime runtime)
        {
            this.runtime = runtime;
            definition = runtime.GetDefinition(settingId);

            _toggleGroup = optionsContainer.GetComponent<ToggleGroup>();
            if (_toggleGroup == null) _toggleGroup = optionsContainer.gameObject.AddComponent<ToggleGroup>();
            _toggleGroup.allowSwitchOff = false;

            BuildOptionToggles();
            base.Bind(runtime);
        }

        private void BuildOptionToggles()
        {
            foreach (Transform child in optionsContainer) Destroy(child.gameObject);
            _optionToggles.Clear();

            for (int i = 0; i < definition.MultiOptionLabels.Count; i++)
            {
                var optionObj = Instantiate(optionTogglePrefab, optionsContainer);
                var toggle = optionObj.GetComponent<Toggle>();
                toggle.group = _toggleGroup;

                var label = optionObj.GetComponentInChildren<TMPro.TMP_Text>();
                if (label != null) label.text = definition.MultiOptionLabels[i];

                int index = i; // capture by value
                toggle.onValueChanged.AddListener(isOn => { if (isOn && !_building) ReportValue(index); });
                _optionToggles.Add(toggle);
            }
        }

        protected override void ApplyToWidget(object value)
        {
            int index = value is int i ? i : 0;
            _building = true;
            for (int t = 0; t < _optionToggles.Count; t++)
                _optionToggles[t].SetIsOnWithoutNotify(t == index);
            _building = false;
        }
    }
}
