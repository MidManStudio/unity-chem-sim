using System.Collections.Generic;

namespace MidManStudio.Inventorizz.Core
{
    /// <summary>
    /// One live slot's contents, held in <see cref="InventorizzRuntime"/>'s
    /// per-instance slot list. An empty slot is <see cref="ItemId"/> ==
    /// "" (equivalently <see cref="Count"/> &lt;= 0) at its own stable
    /// <see cref="SlotIndex"/> -- not the absence of a row. No InstanceId
    /// here (it's implicit from which instance's list this row lives in);
    /// contrast <see cref="ContainerSlotSaveRow"/>, the flattened form
    /// persistence actually saves, which does carry it.
    /// </summary>
    public sealed class ContainerSlotRow
    {
        public int SlotIndex { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    /// <summary>A live container instance's identity -- which type it is and who owns it. Created at runtime via <see cref="InventorizzRuntime.CreateContainer"/>, never mdix-authored content.</summary>
    public sealed class ContainerInstanceRow
    {
        public string InstanceId { get; set; } = string.Empty;
        public string ContainerTypeId { get; set; } = string.Empty;

        /// <summary>Opaque, host-defined -- a player id, an NPC id, a world-object id for a chest, whatever. Inventorizz never interprets this, only groups by it (see <see cref="InventorizzRuntime.GetContainersForOwner"/>).</summary>
        public string OwnerId { get; set; } = string.Empty;
    }

    /// <summary>
    /// One slot's saved contents. Only non-empty slots get a row --
    /// an empty slot is "no row for that SlotIndex", not a row with
    /// ItemId = "". Flattened with <see cref="InstanceId"/> included
    /// because the runtime groups slots per instance internally, but the
    /// saved/mdix shape needs to name which instance each row belongs to,
    /// same reason QuestObjectiveProgressRow carries QuestId alongside
    /// ObjectiveId.
    /// </summary>
    public sealed class ContainerSlotSaveRow
    {
        public string InstanceId { get; set; } = string.Empty;
        public int SlotIndex { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    /// <summary>Host-owned persistence payload -- <see cref="InventorizzRuntime.CaptureState"/>/<see cref="InventorizzRuntime.RestoreState"/>'s currency. A plain POCO, not a file.</summary>
    public sealed class InventoryProgressSnapshot
    {
        public List<ContainerInstanceRow> Instances { get; set; } = new();
        public List<ContainerSlotSaveRow> Slots { get; set; } = new();
    }

    /// <summary>Nested so serialization naturally produces <c>progress.instances</c>/<c>progress.slots</c>, same reasoning as Questly's QuestProgressGroup.</summary>
    public sealed class InventoryProgressGroup
    {
        public List<ContainerInstanceRow> Instances { get; set; } = new();
        public List<ContainerSlotSaveRow> Slots { get; set; } = new();
    }

    /// <summary>Package-owned save-file shape for <see cref="InventorizzRuntime.SaveToMdix"/>/<see cref="InventorizzRuntime.LoadFromMdix"/>.</summary>
    public sealed class InventoryProgressSaveFile
    {
        public string SaveSlot { get; set; } = string.Empty;
        public string SavedAt { get; set; } = string.Empty;
        public InventoryProgressGroup Progress { get; set; } = new();
    }
}
