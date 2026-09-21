using System;

namespace MidManStudio.Inventorizz.Core
{
    public sealed class ContainerCreatedEventArgs : EventArgs
    {
        public string InstanceId { get; }
        public string OwnerId { get; }
        public string ContainerTypeId { get; }

        public ContainerCreatedEventArgs(string instanceId, string ownerId, string containerTypeId)
        {
            InstanceId = instanceId;
            OwnerId = ownerId;
            ContainerTypeId = containerTypeId;
        }
    }

    public sealed class ContainerDestroyedEventArgs : EventArgs
    {
        public string InstanceId { get; }
        public ContainerDestroyedEventArgs(string instanceId) => InstanceId = instanceId;
    }

    /// <summary>Fired whenever one slot's contents change -- add/remove/move/transfer all funnel through here, one event per affected slot. A UI-binding view (see Runtime/UnityBinding) subscribes to this the same way HudItemView subscribes to ItemDataChanged.</summary>
    public sealed class ContainerSlotChangedEventArgs : EventArgs
    {
        public string InstanceId { get; }
        public int SlotIndex { get; }
        public string ItemId { get; }
        public int Count { get; }

        public ContainerSlotChangedEventArgs(string instanceId, int slotIndex, string itemId, int count)
        {
            InstanceId = instanceId;
            SlotIndex = slotIndex;
            ItemId = itemId;
            Count = count;
        }
    }

    /// <summary>Fired whenever a container instance's lock state changes via <see cref="InventorizzRuntime.SetLocked"/>.</summary>
    public sealed class ContainerLockChangedEventArgs : EventArgs
    {
        public string InstanceId { get; }
        public bool IsLocked { get; }

        public ContainerLockChangedEventArgs(string instanceId, bool isLocked)
        {
            InstanceId = instanceId;
            IsLocked = isLocked;
        }
    }
}
