using UnityEngine;
using TMPro;
using MidManStudio.GenSettings.Core;

namespace MidManStudio.GenSettings.UnityBinding
{
    public class InputFieldSettingItemView : SettingItemViewBase
    {
        [Header("Input Field Reference")]
        [UnityEngine.SerializeField] private TMP_InputField inputField;

        public override void Bind(SettingsRuntime runtime)
        {
            base.Bind(runtime);
            inputField.characterLimit = definition.CharacterLimit;
            inputField.contentType = ToTmpContentType(definition.ContentType);
        }

        private void Awake() => inputField.onEndEdit.AddListener(OnInputEndEdit);

        protected override void ApplyToWidget(object value) =>
            inputField.SetTextWithoutNotify(value as string ?? string.Empty);

        private void OnInputEndEdit(string value) => ReportValue(value);

        /// <summary>SettingsInputContentType exists purely so Core doesn't need a UnityEngine.UI/TMPro reference -- this is the one place it gets converted to the real TMP_InputField.ContentType.</summary>
        private static TMP_InputField.ContentType ToTmpContentType(SettingsInputContentType t) => t switch
        {
            SettingsInputContentType.Autocorrected => TMP_InputField.ContentType.Autocorrected,
            SettingsInputContentType.IntegerNumber => TMP_InputField.ContentType.IntegerNumber,
            SettingsInputContentType.DecimalNumber => TMP_InputField.ContentType.DecimalNumber,
            SettingsInputContentType.Alphanumeric => TMP_InputField.ContentType.Alphanumeric,
            SettingsInputContentType.Name => TMP_InputField.ContentType.Name,
            SettingsInputContentType.EmailAddress => TMP_InputField.ContentType.EmailAddress,
            SettingsInputContentType.Password => TMP_InputField.ContentType.Password,
            SettingsInputContentType.Pin => TMP_InputField.ContentType.Pin,
            SettingsInputContentType.Custom => TMP_InputField.ContentType.Custom,
            _ => TMP_InputField.ContentType.Standard,
        };
    }
}
