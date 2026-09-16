using System;
using MidManStudio.Inventorizz.Core;
using UnityEngine;

namespace MidManStudio.Inventorizz.UnityBinding
{
    /// <summary>
    /// Binds one (container instance, slot index) address to a scene
    /// object, subscribing to <see cref="InventorizzRuntime.SlotChanged"/>
    /// the same way <c>HudItemView</c> subscribes to <c>ItemDataChanged</c>
    /// -- an external mutation (another slot merging into this one, a
    /// transfer landing here, a load) pushes onto this view automatically,
    /// the host never has to remember to poll.
    ///
    /// Narrower than <c>HudItemView</c> on purpose: a HUD item's state
    /// (position/scale/opacity) maps directly onto generic RectTransform/
    /// CanvasGroup properties this package can own and set itself. A
    /// slot's state (which item, how many) doesn't -- rendering an item id
    /// as a sprite and a count as text is 100% game content, so this view
    /// only exposes the current data and fires <see cref="ContentsChanged"/>;
    /// a host's own presenter script owns the actual icon/label rendering.
    ///
    /// Same ordering contract as HudItemView: bind every view BEFORE
    /// calling <see cref="InventorizzRuntime.RestoreState"/>/<c>LoadFromMdix</c>
    /// if you want a load to visually refresh already-open slot UI; a
    /// restore for a slot with no bound view at the time simply has
    /// nothing to push onto, same "no retry queue" simplification
    /// documented on HudItemView.
    /// </summary>
    public sealed class InventorySlotView : MonoBehaviour
    {
        private InventorizzRuntime? _runtime;

        public string ContainerInstanceId { get; private set; } = string.Empty;
        public int SlotIndex { get; private set; } = -1;
        public string CurrentItemId { get; private set; } = string.Empty;
        public int CurrentCount { get; private set; }

        /// <summary>Fired with the freshly-updated (ItemId, Count) whenever this exact slot changes, for any reason.</summary>
        public event Action<string, int>? ContentsChanged;

        public void Bind(InventorizzRuntime runtime, string containerInstanceId, int slotIndex)
        {
            Unbind();

            _runtime = runtime;
            ContainerInstanceId = containerInstanceId;
            SlotIndex = slotIndex;
            _runtime.SlotChanged += OnSlotChanged;

            RefreshFromRuntime();
        }

        public void Unbind()
        {
            if (_runtime != null) _runtime.SlotChanged -= OnSlotChanged;
            _runtime = null;
        }

        private void OnDestroy() => Unbind();

        private void OnSlotChanged(object? sender, ContainerSlotChangedEventArgs e)
        {
            if (e.InstanceId != ContainerInstanceId || e.SlotIndex != SlotIndex) return;
            CurrentItemId = e.ItemId;
            CurrentCount = e.Count;
            ContentsChanged?.Invoke(CurrentItemId, CurrentCount);
        }

        private void RefreshFromRuntime()
        {
            if (_runtime == null) return;
            var slots = _runtime.GetSlots(ContainerInstanceId);
            if (SlotIndex < 0 || SlotIndex >= slots.Count) return;

            var slot = slots[SlotIndex];
            CurrentItemId = slot.ItemId;
            CurrentCount = slot.Count;
            ContentsChanged?.Invoke(CurrentItemId, CurrentCount);
        }
    }
}
