namespace MidManStudio.Inventorizz.UnityBinding
{
    /// <summary>
    /// The opaque payload dragndrop's Core carries around during a drag --
    /// exactly what <see cref="MidManStudio.DragDrop.Core.IDraggable.GetPayload"/>'s
    /// doc comment already anticipated ("Inventorz drags an item stack
    /// reference"). Deliberately just the address (which container, which
    /// slot), not a cached copy of the item/count -- every consumer
    /// (<see cref="InventorySlotDropTarget"/>, <see cref="InventoryContainerDropTarget"/>)
    /// re-queries <see cref="Core.InventorizzRuntime"/> fresh with this
    /// address rather than trusting anything captured at drag-start, so a
    /// mutation elsewhere mid-drag can never make a drop act on stale data.
    /// </summary>
    public sealed class InventorySlotPayload
    {
        public string ContainerInstanceId { get; }
        public int SlotIndex { get; }

        public InventorySlotPayload(string containerInstanceId, int slotIndex)
        {
            ContainerInstanceId = containerInstanceId;
            SlotIndex = slotIndex;
        }
    }
}
