using MidManStudio.DragDrop.Core;
using MidManStudio.Inventorizz.Core;

namespace MidManStudio.Inventorizz.UnityBinding
{
    /// <summary>Wraps one (container, slot) address as something dragndrop's Core can pick up.</summary>
    public sealed class InventorySlotDraggableLogic : IDraggable
    {
        public string ContainerInstanceId { get; set; } = string.Empty;
        public int SlotIndex { get; set; }

        public object GetPayload() => new InventorySlotPayload(ContainerInstanceId, SlotIndex);
    }

    /// <summary>
    /// Exact-slot drop target -- what a grid-style (fixed-grid) inventory
    /// UI wires one per cell. Always re-queries <see cref="InventorizzRuntime"/>
    /// fresh rather than trusting the payload's cached identity, since
    /// something else could have mutated the source slot between drag-start
    /// and drop. Mirrors <see cref="InventorizzRuntime.TryMoveStack"/>'s own
    /// no-swap rule exactly -- CanAccept rejects a slot occupied by a
    /// DIFFERENT item rather than let a confusing drop silently fail later.
    /// </summary>
    public sealed class InventorySlotDropTargetLogic : IDropTarget
    {
        private readonly InventorizzRuntime _runtime;
        public string ContainerInstanceId { get; set; } = string.Empty;
        public int SlotIndex { get; set; }

        public InventorySlotDropTargetLogic(InventorizzRuntime runtime) => _runtime = runtime;

        public bool CanAccept(object payload)
        {
            if (payload is not InventorySlotPayload p) return false;

            var sourceSlots = _runtime.GetSlots(p.ContainerInstanceId);
            if (p.SlotIndex < 0 || p.SlotIndex >= sourceSlots.Count) return false;
            var source = sourceSlots[p.SlotIndex];
            if (string.IsNullOrEmpty(source.ItemId) || source.Count <= 0) return false;

            var destSlots = _runtime.GetSlots(ContainerInstanceId);
            if (SlotIndex < 0 || SlotIndex >= destSlots.Count) return false;
            var dest = destSlots[SlotIndex];
            if (!string.IsNullOrEmpty(dest.ItemId) && dest.ItemId != source.ItemId) return false;

            return true;
        }

        public void OnDrop(object payload)
        {
            if (payload is not InventorySlotPayload p) return;
            _runtime.TryMoveStack(p.ContainerInstanceId, p.SlotIndex, ContainerInstanceId, SlotIndex);
        }
    }

    /// <summary>
    /// Whole-container drop target -- what a flat-bag inventory UI (no
    /// fixed cells to target individually) wires once for the panel.
    /// Fixed-grid containers can use this too as a catch-all, but
    /// <see cref="InventorySlotDropTargetLogic"/> is the one that gives
    /// exact placement control. Rejects dropping a container onto itself
    /// -- there's no meaningful "transfer into the container it's already
    /// in" through this target (that's what <see cref="InventorySlotDropTargetLogic"/>
    /// or plain reordering is for).
    /// </summary>
    public sealed class InventoryContainerDropTargetLogic : IDropTarget
    {
        private readonly InventorizzRuntime _runtime;
        public string ContainerInstanceId { get; set; } = string.Empty;

        public InventoryContainerDropTargetLogic(InventorizzRuntime runtime) => _runtime = runtime;

        public bool CanAccept(object payload)
        {
            if (payload is not InventorySlotPayload p) return false;
            if (p.ContainerInstanceId == ContainerInstanceId) return false;

            var sourceSlots = _runtime.GetSlots(p.ContainerInstanceId);
            if (p.SlotIndex < 0 || p.SlotIndex >= sourceSlots.Count) return false;
            var source = sourceSlots[p.SlotIndex];
            return !string.IsNullOrEmpty(source.ItemId) && source.Count > 0;
        }

        public void OnDrop(object payload)
        {
            if (payload is not InventorySlotPayload p) return;

            var sourceSlots = _runtime.GetSlots(p.ContainerInstanceId);
            if (p.SlotIndex < 0 || p.SlotIndex >= sourceSlots.Count) return;
            var slot = sourceSlots[p.SlotIndex];
            if (string.IsNullOrEmpty(slot.ItemId) || slot.Count <= 0) return;

            _runtime.TryTransferItem(p.ContainerInstanceId, ContainerInstanceId, slot.ItemId, slot.Count);
        }
    }
}
