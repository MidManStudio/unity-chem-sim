using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MidManStudio.GenSettings.UnityBinding
{
    public class SliderSettingItemView : SettingItemViewBase
    {
        [Header("Slider References")]
        [SerializeField] private Slider slider;
        [SerializeField] private TMP_Text valueText;

        public override void Bind(Core.SettingsRuntime runtime)
        {
            base.Bind(runtime);
            slider.minValue = definition.MinValue;
            slider.maxValue = definition.MaxValue;
            slider.wholeNumbers = definition.WholeNumbers;
        }

        private void Awake() => slider.onValueChanged.AddListener(OnSliderChanged);

        protected override void ApplyToWidget(object value)
        {
            float v = value is float f ? f : 0f;
            slider.SetValueWithoutNotify(v);
            if (valueText != null) valueText.text = definition.WholeNumbers ? v.ToString("F0") : v.ToString("F2");
        }

        private void OnSliderChanged(float value)
        {
            if (valueText != null) valueText.text = definition.WholeNumbers ? value.ToString("F0") : value.ToString("F2");
            ReportValue(value);
        }
    }
}
