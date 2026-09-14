using TMPro;
using UnityEngine;
using MidManStudio.GenSettings.Core;

namespace MidManStudio.GenSettings.UnityBinding
{
    /// <summary>
    /// Template-method base mirroring the original BaseSettingItem's
    /// Initialize/LoadValue/SaveValue/ApplyValue shape, minus the
    /// per-settingId switch every concrete subclass used to hardcode --
    /// that's <see cref="ISettingApplier"/>'s job now, resolved by
    /// SettingsRuntime, not by anything in this class or its subclasses.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class SettingItemViewBase : MonoBehaviour
    {
        [Header("Common References")]
        [SerializeField] protected TMP_Text labelText;

        [Header("Identity")]
        [SerializeField] protected string settingId = string.Empty;

        protected SettingsRuntime runtime;
        protected SettingDefinition definition;

        public string SettingId => settingId;

        public virtual void Bind(SettingsRuntime runtime)
        {
            this.runtime = runtime;
            definition = runtime.GetDefinition(settingId);

            if (labelText != null) labelText.text = definition.DisplayName;

            runtime.ValueChanged += OnRuntimeValueChanged;
            ApplyToWidget(runtime.GetValue(settingId));
        }

        protected virtual void OnDestroy()
        {
            if (runtime != null) runtime.ValueChanged -= OnRuntimeValueChanged;
        }

        private void OnRuntimeValueChanged(object sender, SettingValueChangedEventArgs e)
        {
            if (e.SettingId == settingId) ApplyToWidget(e.Value);
        }

        /// <summary>Push a runtime value onto this view's concrete widget. Implementations must use the widget's "without notify" setter (e.g. Toggle.SetIsOnWithoutNotify) so pushing a value here never re-triggers the widget's own onValueChanged and loops back into ReportValue.</summary>
        protected abstract void ApplyToWidget(object value);

        /// <summary>Call from the widget's own onValueChanged/onClick handler.</summary>
        protected void ReportValue(object value) => runtime?.SetValue(settingId, value);
    }
}
