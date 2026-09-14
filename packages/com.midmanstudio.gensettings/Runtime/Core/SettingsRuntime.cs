using System;
using System.Collections.Generic;
using MidManStudio.Mdix.Core;

namespace MidManStudio.GenSettings.Core
{
    public sealed class SettingValueChangedEventArgs : EventArgs
    {
        public string SettingId { get; }
        public object? Value { get; }
        public SettingValueChangedEventArgs(string settingId, object? value)
        {
            SettingId = settingId;
            Value = value;
        }
    }

    /// <summary>
    /// Zero game-specific logic -- a definition's category/subcategory,
    /// an applier's actual effect, and every value are opaque to this
    /// class the same way GTG's own interpretation of a quest evaluator
    /// or a HUD item type is opaque to Questly/HudMan. Register every
    /// SettingDefinition and every ISettingApplier BEFORE calling
    /// RestoreState/LoadFromMdix, same ordering contract HudItemView
    /// documents for HudLayoutRuntime -- a saved row for a setting
    /// nothing has registered yet is skipped, not queued.
    /// </summary>
    public sealed class SettingsRuntime
    {
        private readonly Dictionary<string, SettingDefinition> _definitions = new();
        private readonly Dictionary<string, object?> _currentValues = new();
        private readonly Dictionary<string, ISettingApplier> _appliers = new();

        public event EventHandler<SettingValueChangedEventArgs>? ValueChanged;

        public IReadOnlyCollection<string> RegisteredSettingIds => _definitions.Keys;

        // ── Registration ──────────────────────────────────────────────────────

        /// <summary>Registers a definition and seeds its current value to the definition's own default. Safe to call again for the same id -- overwrites both silently, same re-registration posture as HudLayoutRuntime.Register.</summary>
        public void RegisterDefinition(SettingDefinition definition)
        {
            _definitions[definition.SettingId] = definition;
            _currentValues[definition.SettingId] = definition.GetDefaultValue();
        }

        public void RegisterApplier(string settingId, ISettingApplier applier) => _appliers[settingId] = applier;

        /// <summary>Convenience overload for a one-line lambda instead of a whole ISettingApplier class.</summary>
        public void RegisterApplier(string settingId, Action<object?> apply) =>
            _appliers[settingId] = new DelegateApplier(apply);

        public void UnregisterDefinition(string settingId)
        {
            _definitions.Remove(settingId);
            _currentValues.Remove(settingId);
        }

        public bool IsRegistered(string settingId) => _definitions.ContainsKey(settingId);

        // ── Reading / writing values ──────────────────────────────────────────

        public SettingDefinition GetDefinition(string settingId)
        {
            if (_definitions.TryGetValue(settingId, out var def)) return def;
            throw new InvalidOperationException($"'{settingId}' is not registered with this SettingsRuntime. Call RegisterDefinition() first.");
        }

        public bool TryGetDefinition(string settingId, out SettingDefinition? definition) =>
            _definitions.TryGetValue(settingId, out definition);

        public object? GetValue(string settingId)
        {
            if (_currentValues.TryGetValue(settingId, out var value)) return value;
            throw new InvalidOperationException($"'{settingId}' is not registered with this SettingsRuntime. Call RegisterDefinition() first.");
        }

        public T GetValue<T>(string settingId) => (T)GetValue(settingId)!;

        /// <summary>
        /// Sets a value, fires <see cref="ValueChanged"/>, then invokes
        /// the registered ISettingApplier for this id (if any) --
        /// replicates the original LoadValue()/OnXChanged() pattern of
        /// always ending in ApplyValue(). No-op for an unregistered id.
        /// </summary>
        public void SetValue(string settingId, object? value)
        {
            if (!_definitions.ContainsKey(settingId)) return;
            _currentValues[settingId] = value;
            ValueChanged?.Invoke(this, new SettingValueChangedEventArgs(settingId, value));
            if (_appliers.TryGetValue(settingId, out var applier)) applier.Apply(settingId, value);
        }

        /// <summary>For Button-type settings: fires the registered applier with a null value. Does nothing else -- buttons have no value to change or persist, matching the original "Buttons don't have values" / "Buttons don't save values" comments exactly.</summary>
        public void TriggerAction(string settingId)
        {
            if (!_definitions.ContainsKey(settingId)) return;
            if (_appliers.TryGetValue(settingId, out var applier)) applier.Apply(settingId, null);
        }

        // ── Reset ─────────────────────────────────────────────────────────────

        public void ResetToDefault(string settingId)
        {
            if (!_definitions.TryGetValue(settingId, out var def)) return;
            SetValue(settingId, def.GetDefaultValue());
        }

        public void ResetAllToDefaults()
        {
            foreach (var id in new List<string>(_definitions.Keys))
                ResetToDefault(id);
        }

        // ── Persistence: host-owned ──────────────────────────────────────────

        public SettingsSnapshot CaptureState()
        {
            var snapshot = new SettingsSnapshot();
            foreach (var (id, def) in _definitions)
                snapshot.Values.Add(SettingValueRow.FromValue(id, def.ControlType, _currentValues[id]));
            return snapshot;
        }

        /// <summary>
        /// Applies every row the snapshot mentions for a currently
        /// registered setting -- including firing its applier, since a
        /// restored setting needs to actually take effect (vSync,
        /// volume, fullscreen, ...), not just sit in memory. Rows for
        /// settings this build doesn't have are silently skipped, same
        /// "don't crash on a save file that's ahead of or behind the
        /// current game version" posture QuestRuntime.RestoreState takes.
        /// </summary>
        public void RestoreState(SettingsSnapshot snapshot)
        {
            foreach (var row in snapshot.Values)
            {
                if (!_definitions.TryGetValue(row.SettingId, out var def)) continue;
                SetValue(row.SettingId, row.ToValue(def.ControlType));
            }
        }

        // ── Persistence: package-owned (optional) ────────────────────────────

        public MdixResult<Unit> SaveToMdix(string path)
        {
            var saveFile = new SettingsSaveFile { SavedAt = DateTimeOffset.UtcNow.ToString("O") };
            saveFile.Settings.Values = CaptureState().Values;

            using var builder = MdixBuilder.Create();
            var serializeResult = builder.Serialize(saveFile);
            if (serializeResult.IsFailure) return serializeResult;

            return builder.Save(path);
        }

        public MdixResult<Unit> LoadFromMdix(string path)
        {
            var dbResult = MdixDatabase.Load(path);
            if (dbResult.IsFailure) return MdixResult<Unit>.Err(dbResult.Error);

            using var db = dbResult.SuccessResult;
            var fileResult = db.Deserialize<SettingsSaveFile>();
            if (fileResult.IsFailure) return MdixResult<Unit>.Err(fileResult.Error);

            RestoreState(new SettingsSnapshot { Values = fileResult.SuccessResult.Settings.Values });
            return MdixResult<Unit>.Ok(Unit.Value);
        }

        private sealed class DelegateApplier : ISettingApplier
        {
            private readonly Action<object?> _apply;
            public DelegateApplier(Action<object?> apply) => _apply = apply;
            public void Apply(string settingId, object? value) => _apply(value);
        }
    }
}
