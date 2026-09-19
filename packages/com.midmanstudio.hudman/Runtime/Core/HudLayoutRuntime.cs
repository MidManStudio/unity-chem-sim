using System;
using System.Collections.Generic;
using MidManStudio.Mdix.Core;
using MidManStudio.HudMan.Generated;

namespace MidManStudio.HudMan.Core
{
    public sealed class HudItemChangedEventArgs : EventArgs
    {
        public HudItemType ItemType { get; }
        public HudItemData Data { get; }
        public HudItemChangedEventArgs(HudItemType itemType, HudItemData data)
        {
            ItemType = itemType;
            Data = data;
        }
    }

    public sealed class HudSelectionChangedEventArgs : EventArgs
    {
        public HudItemType? Previous { get; }
        public HudItemType? Current { get; }
        public HudSelectionChangedEventArgs(HudItemType? previous, HudItemType? current)
        {
            Previous = previous;
            Current = current;
        }
    }

    public sealed class HudEditModeChangedEventArgs : EventArgs
    {
        public bool IsEditMode { get; }
        public HudEditModeChangedEventArgs(bool isEditMode) => IsEditMode = isEditMode;
    }

    /// <summary>
    /// Zero game-specific logic and zero UnityEngine dependency -- a
    /// consuming game's own HudItemType catalog (generated per
    /// Runtime/Generator/) is opaque data to everything here, and the
    /// actual RectTransform/CanvasGroup a HUD item lives on is
    /// <see cref="MidManStudio.HudMan.UnityBinding.HudItemView"/>'s
    /// concern, not this class's. Items register themselves (typically
    /// once, from a HudItemView's own startup) with whatever their
    /// current on-screen state is; that becomes the reset target until
    /// something overwrites it via <see cref="SetData"/>,
    /// <see cref="RestoreState"/>, or <see cref="LoadFromMdix"/>.
    /// </summary>
    public sealed class HudLayoutRuntime
    {
        private readonly Dictionary<HudItemType, HudItemData> _current = new();
        private readonly Dictionary<HudItemType, HudItemData> _defaults = new();

        public string CurrentLayoutName { get; set; } = "Default";
        public HudItemType? SelectedItem { get; private set; }

        /// <summary>
        /// Whether the host is currently in HUD-editing mode. Off by
        /// default -- dragging a HUD item (see
        /// <see cref="MidManStudio.HudMan.UnityBinding.HudItemDraggableAdapter"/>)
        /// is only enabled while this is true, so a normal player never
        /// repositions their HUD by accident during ordinary play. A host
        /// flips this from wherever it wants to expose the toggle -- a
        /// settings menu button (<c>gensettings</c>' "editHUD" example),
        /// a debug hotkey, whatever fits the game.
        /// </summary>
        public bool IsEditMode { get; private set; }

        public event EventHandler<HudItemChangedEventArgs>? ItemDataChanged;
        public event EventHandler<HudSelectionChangedEventArgs>? SelectionChanged;
        public event EventHandler<HudEditModeChangedEventArgs>? EditModeChanged;

        public IReadOnlyCollection<HudItemType> RegisteredItems => _current.Keys;

        // ── Registration ──────────────────────────────────────────────────────

        /// <summary>
        /// Registers an item with its current on-screen state as both the
        /// live value and the reset target. Safe to call again for the
        /// same type (e.g. a scene reload) -- overwrites both silently
        /// rather than throwing, since re-registration is the expected
        /// path, not an error case.
        /// </summary>
        public void Register(HudItemType itemType, HudItemData currentData)
        {
            _current[itemType] = currentData;
            _defaults[itemType] = Clone(currentData);
        }

        public void Unregister(HudItemType itemType)
        {
            _current.Remove(itemType);
            _defaults.Remove(itemType);
            if (SelectedItem == itemType) Select(null);
        }

        public bool IsRegistered(HudItemType itemType) => _current.ContainsKey(itemType);

        // ── Reading / writing item state ─────────────────────────────────────

        public HudItemData GetData(HudItemType itemType)
        {
            if (_current.TryGetValue(itemType, out var data)) return data;
            throw new InvalidOperationException(
                $"'{itemType}' is not registered with this HudLayoutRuntime. Call Register() first.");
        }

        public bool TryGetData(HudItemType itemType, out HudItemData? data) =>
            _current.TryGetValue(itemType, out data);

        /// <summary>Overwrites an item's live state. No-op (does not throw) if the item was never registered, since a late-arriving save/network update for an item the current scene doesn't have shouldn't crash anything.</summary>
        public void SetData(HudItemType itemType, HudItemData data)
        {
            if (!_current.ContainsKey(itemType)) return;
            _current[itemType] = data;
            ItemDataChanged?.Invoke(this, new HudItemChangedEventArgs(itemType, data));
        }

        // ── Selection (the thing an editor's scale/opacity sliders act on) ───

        public void Select(HudItemType? itemType)
        {
            if (itemType.HasValue && !_current.ContainsKey(itemType.Value))
                throw new InvalidOperationException($"Cannot select '{itemType}' -- it is not registered.");
            if (SelectedItem == itemType) return;

            var previous = SelectedItem;
            SelectedItem = itemType;
            SelectionChanged?.Invoke(this, new HudSelectionChangedEventArgs(previous, itemType));
        }

        // ── Edit mode (gates whether HudItemDraggableAdapter allows dragging) ──

        /// <summary>
        /// No-op if already in the requested state. Turning edit mode off
        /// also clears the current selection -- selection only means
        /// anything while editing, so leaving one behind would just be
        /// stale state a UI has to remember to clear itself.
        /// </summary>
        public void SetEditMode(bool enabled)
        {
            if (IsEditMode == enabled) return;
            IsEditMode = enabled;
            if (!enabled) Select(null);
            EditModeChanged?.Invoke(this, new HudEditModeChangedEventArgs(enabled));
        }

        public void ToggleEditMode() => SetEditMode(!IsEditMode);

        // ── Reset ─────────────────────────────────────────────────────────────

        public void ResetItem(HudItemType itemType)
        {
            if (!_defaults.TryGetValue(itemType, out var def)) return;
            SetData(itemType, Clone(def));
        }

        public void ResetAll()
        {
            foreach (var itemType in new List<HudItemType>(_current.Keys))
                ResetItem(itemType);
        }

        // ── Persistence: host-owned ──────────────────────────────────────────

        public HudLayoutSnapshot CaptureState()
        {
            var snapshot = new HudLayoutSnapshot { LayoutName = CurrentLayoutName };
            foreach (var (itemType, data) in _current)
                snapshot.Items.Add(new HudItemData(
                    itemType, data.PositionX, data.PositionY, data.ScaleX, data.ScaleY, data.Opacity, data.IsVisible));
            return snapshot;
        }

        /// <summary>
        /// Applies every row the snapshot mentions for an item that's
        /// currently registered. Rows for item types this scene doesn't
        /// have are silently skipped -- same "don't crash on a save file
        /// that's ahead of or behind the current game version" posture
        /// QuestRuntime.RestoreState takes.
        /// </summary>
        public void RestoreState(HudLayoutSnapshot snapshot)
        {
            CurrentLayoutName = snapshot.LayoutName;
            foreach (var row in snapshot.Items)
                SetData(row.ItemType, row);
        }

        // ── Persistence: package-owned (optional) ────────────────────────────

        /// <summary>
        /// Writes the current layout as a standalone .mdix file shaped
        /// like <c>Samples~/example_layout_save.mdix</c>. Entirely
        /// optional -- a host using CaptureState/RestoreState never needs
        /// to call this. Note this is one file per named layout (the
        /// caller picks <paramref name="path"/>) -- listing which layouts
        /// exist on disk is a host-side directory convention, same as it
        /// is for Questly's save slots, so it isn't reimplemented here.
        /// </summary>
        public MdixResult<Unit> SaveToMdix(string path, string layoutName)
        {
            var snapshot = CaptureState();
            snapshot.LayoutName = layoutName;

            var saveFile = new HudLayoutSaveFile
            {
                LayoutName = layoutName,
                SavedAt = DateTimeOffset.UtcNow.ToString("O"),
            };
            saveFile.Layout.Items = snapshot.Items;

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
            var fileResult = db.Deserialize<HudLayoutSaveFile>();
            if (fileResult.IsFailure) return MdixResult<Unit>.Err(fileResult.Error);

            var saveFile = fileResult.SuccessResult;
            RestoreState(new HudLayoutSnapshot
            {
                LayoutName = saveFile.LayoutName,
                Items = saveFile.Layout.Items,
            });

            return MdixResult<Unit>.Ok(Unit.Value);
        }

        private static HudItemData Clone(HudItemData d) => new(
            d.ItemType, d.PositionX, d.PositionY, d.ScaleX, d.ScaleY, d.Opacity, d.IsVisible);
    }
}
