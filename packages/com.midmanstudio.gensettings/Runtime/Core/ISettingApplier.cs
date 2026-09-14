namespace MidManStudio.GenSettings.Core
{
    /// <summary>
    /// Replaces every <c>switch (settingData.settingId) { case "vSyncEnabled": ... }</c>
    /// block the original six *SettingItem.cs classes hardcoded into
    /// ApplyValue()/OnButtonClicked(). A host registers one of these per
    /// settingId it cares about; gensettings ships zero built-in
    /// appliers, the same relationship Questly has with
    /// IQuestObjectiveEvaluator. For a Button-type setting, <paramref name="value"/>
    /// is always null -- the call itself is the action trigger.
    /// </summary>
    public interface ISettingApplier
    {
        void Apply(string settingId, object? value);
    }
}
