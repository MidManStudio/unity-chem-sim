using System;
using System.Collections.Generic;
using MidManStudio.Mdix.Core;
using MidManStudio.Inventorizz.Generated;

namespace MidManStudio.Inventorizz.Core
{
    /// <summary>
    /// Generic container-instance runtime. Zero UnityEngine dependency by
    /// discipline (folder convention, not a hard assembly split -- see this
    /// package's asmdef notes), zero game-specific logic -- item taxonomy
    /// and container roles are entirely the host's business via
    /// <see cref="ItemDefinition.Tags"/>/<see cref="ContainerTypeDefinition.Tags"/>.
    ///
    /// Container TYPES are mdix-authored content (<see cref="IInventoryCatalog"/>);
    /// container INSTANCES are created at runtime by a host
    /// (<see cref="CreateContainer"/>) and never derived from the catalog
    /// automatically -- there is no "one instance per definition" relationship
    /// the way Questly has one live QuestState per QuestDefinition or HudMan
    /// has one HudItemData per HudItemType. Because of that, this class
    /// deliberately does NOT subscribe to <see cref="IInventoryCatalog.DefinitionsChanged"/>
    /// itself: there's no well-defined "reconcile all instances" operation
    /// analogous to Questly's Reconcile() when the set of live things isn't
    /// derived from the definition set in the first place. A host hot-reloading
    /// item/container-type content while instances already hold stacks needs
    /// its own policy for what happens to those stacks (a shrunk max_slots on
    /// a live container, an item id that no longer exists, etc.) -- this class
    /// tolerates a missing item/type definition gracefully wherever it's
    /// looked up live (treated as zero weight, capacity checks default to
    /// unlimited) rather than throwing, but does not attempt to auto-heal or
    /// resize anything on its own.
    ///
    /// Internal model, per this package's own design discussion: EVERY
    /// container is a positionally-addressable list of slots (so a
    /// grid-style drag-and-drop UI and a flat-bag UI can share one adapter
    /// shape), but the two capacity families behave differently:
    ///   SLOT_COUNT / WEIGHT_AND_SLOTS ("fixed-grid"): the slot list is
    ///     pre-sized to MaxSlots at creation and never grows or shrinks --
    ///     empty slots are real, addressable, stay at their index forever.
    ///   UNLIMITED / WEIGHT ("flat-bag"): the slot list starts empty and
    ///     grows on add; there's no fixed slot count to enforce, so a slot
    ///     index there just means "current position in the bag", not a
    ///     UI-meaningful grid cell.
    /// </summary>
    public sealed class InventorizzRuntime
    {
        private readonly IInventoryCatalog _catalog;
        private readonly Dictionary<string, ContainerInstanceRow> _instances = new();
        private readonly Dictionary<string, List<ContainerSlotRow>> _slots = new();
        private readonly Dictionary<string, bool> _locked = new();

        public event EventHandler<ContainerCreatedEventArgs>? ContainerCreated;
        public event EventHandler<ContainerDestroyedEventArgs>? ContainerDestroyed;
        public event EventHandler<ContainerSlotChangedEventArgs>? SlotChanged;
        public event EventHandler<ContainerLockChangedEventArgs>? ContainerLockChanged;

        public InventorizzRuntime(IInventoryCatalog catalog) => _catalog = catalog;

        // ── Container lifecycle ─────────────────────────────────────────────

        /// <summary>Creates a new instance of <paramref name="containerTypeId"/> owned by <paramref name="ownerId"/>. Returns null if the type doesn't exist. A fixed-grid type gets its full slot array pre-allocated (all empty) immediately -- a fresh 8-slot backpack has 8 addressable empty slots from the start, not zero.</summary>
        public string? CreateContainer(string ownerId, string containerTypeId, string? instanceId = null)
        {
            var type = _catalog.FindContainerType(containerTypeId);
            if (type == null) return null;

            var id = string.IsNullOrEmpty(instanceId) ? Guid.NewGuid().ToString("N") : instanceId!;
            _instances[id] = new ContainerInstanceRow { InstanceId = id, ContainerTypeId = containerTypeId, OwnerId = ownerId };
            _slots[id] = BuildEmptySlots(type);

            ContainerCreated?.Invoke(this, new ContainerCreatedEventArgs(id, ownerId, containerTypeId));
            return id;
        }

        public void DestroyContainer(string instanceId)
        {
            if (!_instances.Remove(instanceId)) return;
            _slots.Remove(instanceId);
            _locked.Remove(instanceId);
            ContainerDestroyed?.Invoke(this, new ContainerDestroyedEventArgs(instanceId));
        }

        public bool ContainerExists(string instanceId) => _instances.ContainsKey(instanceId);

        public ContainerInstanceRow? GetInstance(string instanceId) =>
            _instances.TryGetValue(instanceId, out var row) ? row : null;

        // ── Locking (gates TryAddItem/TryRemoveItem/TryRemoveFromSlot/TryMoveStack) ──

        /// <summary>False for a container instance that doesn't exist or was never locked -- unlocked is the default, matching every other container instance's starting state.</summary>
        public bool IsLocked(string instanceId) => _locked.TryGetValue(instanceId, out var locked) && locked;

        /// <summary>
        /// No-op for a container instance that doesn't exist, and a no-op
        /// (no event fired) if already in the requested state -- mirrors
        /// HudLayoutRuntime.SetEditMode's idempotency exactly. While locked,
        /// TryAddItem/TryRemoveItem/TryRemoveFromSlot/TryMoveStack all fail
        /// (return false, mutate nothing) against this instance -- CreateContainer
        /// and DestroyContainer are unaffected, since locking is about blocking
        /// player-initiated content changes, not administrative lifecycle.
        /// A host decides what "locked" corresponds to (e.g. "away from base");
        /// this class only tracks and enforces the flag.
        /// </summary>
        public void SetLocked(string instanceId, bool locked)
        {
            if (!_instances.ContainsKey(instanceId)) return;
            if (IsLocked(instanceId) == locked) return;
            _locked[instanceId] = locked;
            ContainerLockChanged?.Invoke(this, new ContainerLockChangedEventArgs(instanceId, locked));
        }

        public IReadOnlyList<string> GetContainersForOwner(string ownerId)
        {
            var result = new List<string>();
            foreach (var instance in _instances.Values)
                if (instance.OwnerId == ownerId)
                    result.Add(instance.InstanceId);
            return result;
        }

        public IReadOnlyList<ContainerSlotRow> GetSlots(string instanceId) =>
            _slots.TryGetValue(instanceId, out var slots) ? slots : Array.Empty<ContainerSlotRow>();

        private static List<ContainerSlotRow> BuildEmptySlots(ContainerTypeDefinition type)
        {
            var slots = new List<ContainerSlotRow>();
            if (IsFixedGrid(type))
                for (var i = 0; i < type.MaxSlots; i++)
                    slots.Add(EmptyRow(i));
            return slots;
        }

        private static ContainerSlotRow EmptyRow(int index) => new() { SlotIndex = index, ItemId = string.Empty, Count = 0 };
        private static bool IsEmpty(ContainerSlotRow row) => string.IsNullOrEmpty(row.ItemId) || row.Count <= 0;
        private static bool IsFixedGrid(ContainerTypeDefinition type) => type.CapacityKind is CapacityKind.SLOT_COUNT or CapacityKind.WEIGHT_AND_SLOTS;
        private static bool ChecksWeight(ContainerTypeDefinition type) => type.CapacityKind is CapacityKind.WEIGHT or CapacityKind.WEIGHT_AND_SLOTS;

        // ── Capacity queries ─────────────────────────────────────────────────

        public float GetCurrentWeight(string instanceId)
        {
            if (!_slots.TryGetValue(instanceId, out var slots)) return 0f;
            var total = 0f;
            foreach (var slot in slots)
            {
                if (IsEmpty(slot)) continue;
                var item = _catalog.FindItem(slot.ItemId);
                total += (item?.Weight ?? 0f) * slot.Count;
            }
            return total;
        }

        /// <summary><see cref="float.PositiveInfinity"/> for a container whose type doesn't check weight (UNLIMITED/SLOT_COUNT) or that no longer exists.</summary>
        public float GetRemainingWeightCapacity(string instanceId)
        {
            var type = GetType(instanceId);
            if (type == null || !ChecksWeight(type)) return float.PositiveInfinity;
            return Math.Max(0f, type.MaxWeight - GetCurrentWeight(instanceId));
        }

        public int GetUsedSlotCount(string instanceId)
        {
            if (!_slots.TryGetValue(instanceId, out var slots)) return 0;
            var used = 0;
            foreach (var slot in slots)
                if (!IsEmpty(slot)) used++;
            return used;
        }

        /// <summary><see cref="int.MaxValue"/> for a container whose type doesn't check slots (UNLIMITED/WEIGHT) or that no longer exists.</summary>
        public int GetRemainingSlotCapacity(string instanceId)
        {
            var type = GetType(instanceId);
            if (type == null || !IsFixedGrid(type)) return int.MaxValue;
            return type.MaxSlots - GetUsedSlotCount(instanceId);
        }

        private ContainerTypeDefinition? GetType(string instanceId) =>
            _instances.TryGetValue(instanceId, out var instance) ? _catalog.FindContainerType(instance.ContainerTypeId) : null;

        // ── Add / remove ─────────────────────────────────────────────────────

        /// <summary>
        /// Best-effort by design: fills as much of <paramref name="count"/>
        /// as actually fits (existing under-full stacks of the same item
        /// first, then empty/new slots for the remainder) and reports how
        /// much via <paramref name="added"/>, rather than failing outright
        /// if the whole amount doesn't fit. A loot-and-carry game generally
        /// wants "grab what you can carry", not "can't take any of this
        /// because your bag is one slot short" -- <see cref="TryTransferItem"/>
        /// is the atomic, all-or-nothing sibling for when partial success
        /// would be the wrong call (e.g. a sale transaction).
        /// </summary>
        public bool TryAddItem(string instanceId, string itemId, int count, out int added)
        {
            added = 0;
            if (count <= 0) return false;
            if (!_instances.TryGetValue(instanceId, out var instance)) return false;
            if (IsLocked(instanceId)) return false;
            var type = _catalog.FindContainerType(instance.ContainerTypeId);
            var item = _catalog.FindItem(itemId);
            if (type == null || item == null) return false;

            var slots = _slots[instanceId];
            var maxStack = Math.Max(1, item.MaxStack);
            var remaining = count;
            var weightBudget = ChecksWeight(type) ? GetRemainingWeightCapacity(instanceId) : float.PositiveInfinity;

            int AffordableByWeight(int wanted)
            {
                if (float.IsPositiveInfinity(weightBudget) || item.Weight <= 0f) return wanted;
                var affordable = (int)(weightBudget / item.Weight);
                return Math.Min(wanted, Math.Max(0, affordable));
            }

            // Pass 1: top off existing same-item stacks under max_stack.
            foreach (var slot in slots)
            {
                if (remaining <= 0) break;
                if (slot.ItemId != itemId || slot.Count >= maxStack) continue;

                var take = Math.Min(remaining, maxStack - slot.Count);
                take = AffordableByWeight(take);
                if (take <= 0) break;

                slot.Count += take;
                remaining -= take;
                added += take;
                weightBudget -= take * item.Weight;
                SlotChanged?.Invoke(this, new ContainerSlotChangedEventArgs(instanceId, slot.SlotIndex, slot.ItemId, slot.Count));
            }

            // Pass 2: place the remainder into empty slots (fixed-grid) or append new ones (flat-bag).
            while (remaining > 0)
            {
                ContainerSlotRow? target = null;

                if (IsFixedGrid(type))
                {
                    foreach (var slot in slots)
                        if (IsEmpty(slot)) { target = slot; break; }
                    if (target == null) break; // out of slots
                }
                else
                {
                    target = EmptyRow(slots.Count);
                    slots.Add(target);
                }

                var take = Math.Min(remaining, maxStack);
                take = AffordableByWeight(take);
                if (take <= 0)
                {
                    if (!IsFixedGrid(type)) slots.Remove(target); // undo the speculative append -- nothing actually fit
                    break;
                }

                target.ItemId = itemId;
                target.Count = take;
                remaining -= take;
                added += take;
                weightBudget -= take * item.Weight;
                SlotChanged?.Invoke(this, new ContainerSlotChangedEventArgs(instanceId, target.SlotIndex, target.ItemId, target.Count));
            }

            return added > 0;
        }

        /// <summary>Atomic: removes exactly <paramref name="count"/> of <paramref name="itemId"/> across however many slots it takes, or fails and mutates nothing if the container doesn't hold at least that much.</summary>
        public bool TryRemoveItem(string instanceId, string itemId, int count)
        {
            if (count <= 0) return false;
            if (!_slots.TryGetValue(instanceId, out var slots)) return false;
            if (IsLocked(instanceId)) return false;

            var available = 0;
            foreach (var slot in slots)
                if (slot.ItemId == itemId) available += slot.Count;
            if (available < count) return false;

            var remaining = count;
            foreach (var slot in slots)
            {
                if (remaining <= 0) break;
                if (slot.ItemId != itemId) continue;

                var take = Math.Min(remaining, slot.Count);
                slot.Count -= take;
                remaining -= take;
                if (slot.Count <= 0) { slot.ItemId = string.Empty; slot.Count = 0; }
                SlotChanged?.Invoke(this, new ContainerSlotChangedEventArgs(instanceId, slot.SlotIndex, slot.ItemId, slot.Count));
            }
            return true;
        }

        /// <summary>Atomic, exact-slot version -- fails without mutating if the slot doesn't hold at least <paramref name="count"/>. This is what a drag-and-drop "split a stack" interaction calls.</summary>
        public bool TryRemoveFromSlot(string instanceId, int slotIndex, int count)
        {
            if (count <= 0) return false;
            if (!_slots.TryGetValue(instanceId, out var slots)) return false;
            if (IsLocked(instanceId)) return false;
            if (slotIndex < 0 || slotIndex >= slots.Count) return false;

            var slot = slots[slotIndex];
            if (slot.Count < count) return false;

            slot.Count -= count;
            if (slot.Count <= 0) { slot.ItemId = string.Empty; slot.Count = 0; }
            SlotChanged?.Invoke(this, new ContainerSlotChangedEventArgs(instanceId, slot.SlotIndex, slot.ItemId, slot.Count));
            return true;
        }

        // ── Moving between slots (the drag-and-drop primitive) ──────────────

        /// <summary>
        /// Moves the entire source stack into a specific destination slot
        /// -- same container or a different one. Succeeds by merging (same
        /// item, destination has room) or placing (destination empty).
        /// Deliberately does NOT support swapping onto a slot occupied by a
        /// DIFFERENT item this pass -- fails cleanly (returns false,
        /// nothing mutated) rather than guessing what the player wanted;
        /// flagged as a known follow-up if two-way swap turns out to matter.
        /// For a flat-bag destination, pass its current <c>Slots.Count</c>
        /// as <paramref name="toSlotIndex"/> to mean "append as a new stack".
        /// </summary>
        public bool TryMoveStack(string fromInstanceId, int fromSlotIndex, string toInstanceId, int toSlotIndex)
        {
            if (!_slots.TryGetValue(fromInstanceId, out var fromSlots) || fromSlotIndex < 0 || fromSlotIndex >= fromSlots.Count)
                return false;
            if (!_slots.TryGetValue(toInstanceId, out var toSlots))
                return false;
            if (IsLocked(fromInstanceId) || IsLocked(toInstanceId)) return false; // either endpoint locked blocks the move -- pulling FROM a locked container counts as modifying it too

            var source = fromSlots[fromSlotIndex];
            if (IsEmpty(source)) return false;

            if (ReferenceEquals(fromSlots, toSlots) && fromSlotIndex == toSlotIndex)
                return true; // dropped onto itself -- trivially already "moved"

            var toType = GetType(toInstanceId);
            if (toType == null) return false;

            var isAppend = !IsFixedGrid(toType) && toSlotIndex == toSlots.Count;
            if (!isAppend && (toSlotIndex < 0 || toSlotIndex >= toSlots.Count)) return false;

            var dest = isAppend ? EmptyRow(toSlotIndex) : toSlots[toSlotIndex];
            var item = _catalog.FindItem(source.ItemId);
            var maxStack = item != null ? Math.Max(1, item.MaxStack) : int.MaxValue;

            if (!IsEmpty(dest) && dest.ItemId != source.ItemId) return false; // no swap support this pass

            var weightBudget = ChecksWeight(toType) ? GetRemainingWeightCapacity(toInstanceId) : float.PositiveInfinity;
            var roomInStack = maxStack - dest.Count; // 0 if dest is empty and Count defaults to 0 -> maxStack, correct
            var affordableByWeight = (item == null || item.Weight <= 0f || float.IsPositiveInfinity(weightBudget))
                ? int.MaxValue
                : (int)(weightBudget / item.Weight);

            var take = Math.Min(source.Count, Math.Min(roomInStack, affordableByWeight));
            if (take <= 0) return false;

            if (isAppend) toSlots.Add(dest);

            dest.ItemId = source.ItemId;
            dest.Count += take;
            source.Count -= take;
            if (source.Count <= 0) { source.ItemId = string.Empty; source.Count = 0; }

            SlotChanged?.Invoke(this, new ContainerSlotChangedEventArgs(toInstanceId, dest.SlotIndex, dest.ItemId, dest.Count));
            SlotChanged?.Invoke(this, new ContainerSlotChangedEventArgs(fromInstanceId, source.SlotIndex, source.ItemId, source.Count));
            return true;
        }

        /// <summary>
        /// Atomic convenience over <see cref="TryRemoveItem"/> + <see cref="TryAddItem"/>:
        /// either all of <paramref name="count"/> ends up in
        /// <paramref name="toInstanceId"/>, or neither container is
        /// touched. Snapshot-and-rollback rather than a separate dry-run
        /// calculator, so there's exactly one code path that decides "does
        /// it fit" -- <see cref="TryAddItem"/> itself. Inherits lock
        /// enforcement for free through those two calls -- no separate
        /// <see cref="IsLocked"/> check needed here.
        /// </summary>
        public bool TryTransferItem(string fromInstanceId, string toInstanceId, string itemId, int count)
        {
            if (!_slots.TryGetValue(fromInstanceId, out var fromSlots) || !_slots.TryGetValue(toInstanceId, out var toSlots))
                return false;

            var fromBackup = Snapshot(fromSlots);
            var toBackup = Snapshot(toSlots);

            if (!TryRemoveItem(fromInstanceId, itemId, count))
                return false;

            if (!TryAddItem(toInstanceId, itemId, count, out var added) || added < count)
            {
                Restore(fromSlots, fromBackup);
                Restore(toSlots, toBackup);
                return false;
            }

            return true;
        }

        private static List<(int SlotIndex, string ItemId, int Count)> Snapshot(List<ContainerSlotRow> slots)
        {
            var backup = new List<(int, string, int)>(slots.Count);
            foreach (var slot in slots) backup.Add((slot.SlotIndex, slot.ItemId, slot.Count));
            return backup;
        }

        private static void Restore(List<ContainerSlotRow> slots, List<(int SlotIndex, string ItemId, int Count)> backup)
        {
            slots.Clear();
            foreach (var (index, itemId, count) in backup)
                slots.Add(new ContainerSlotRow { SlotIndex = index, ItemId = itemId, Count = count });
        }

        // ── Persistence ──────────────────────────────────────────────────────

        public InventoryProgressSnapshot CaptureState()
        {
            var snapshot = new InventoryProgressSnapshot();

            foreach (var instance in _instances.Values)
                snapshot.Instances.Add(new ContainerInstanceRow
                {
                    InstanceId = instance.InstanceId,
                    ContainerTypeId = instance.ContainerTypeId,
                    OwnerId = instance.OwnerId,
                });

            foreach (var (instanceId, slots) in _slots)
                foreach (var slot in slots)
                    if (!IsEmpty(slot))
                        snapshot.Slots.Add(new ContainerSlotSaveRow
                        {
                            InstanceId = instanceId,
                            SlotIndex = slot.SlotIndex,
                            ItemId = slot.ItemId,
                            Count = slot.Count,
                        });

            return snapshot;
        }

        /// <summary>
        /// Fully replaces current state. Recreates each instance's slot
        /// array from the CURRENT catalog (not whatever it looked like
        /// when the save was made) via the same sizing logic
        /// <see cref="CreateContainer"/> uses, then overlays saved slot
        /// rows onto it. A saved row for a slot index that no longer
        /// exists (a fixed-grid container's max_slots shrunk since the
        /// save) is skipped, not crashed -- same leniency Questly's own
        /// RestoreState uses for a row referencing something not currently
        /// registered.
        /// </summary>
        public void RestoreState(InventoryProgressSnapshot snapshot)
        {
            _instances.Clear();
            _slots.Clear();

            foreach (var row in snapshot.Instances)
            {
                _instances[row.InstanceId] = row;
                var type = _catalog.FindContainerType(row.ContainerTypeId);
                _slots[row.InstanceId] = type != null ? BuildEmptySlots(type) : new List<ContainerSlotRow>();
            }

            foreach (var row in snapshot.Slots)
            {
                if (!_slots.TryGetValue(row.InstanceId, out var slots)) continue; // orphaned instance reference

                var type = GetType(row.InstanceId);
                var isFixedGrid = type != null && IsFixedGrid(type);

                if (row.SlotIndex < slots.Count)
                {
                    slots[row.SlotIndex].ItemId = row.ItemId;
                    slots[row.SlotIndex].Count = row.Count;
                }
                else if (!isFixedGrid)
                {
                    while (slots.Count <= row.SlotIndex) slots.Add(EmptyRow(slots.Count));
                    slots[row.SlotIndex].ItemId = row.ItemId;
                    slots[row.SlotIndex].Count = row.Count;
                }
                // else: fixed-grid container shrunk since the save, this row's slot no longer exists -- dropped.
            }
        }

        public MdixResult<Unit> SaveToMdix(string path, string saveSlot)
        {
            var snapshot = CaptureState();
            var saveFile = new InventoryProgressSaveFile
            {
                SaveSlot = saveSlot,
                SavedAt = DateTime.UtcNow.ToString("o"),
                Progress = new InventoryProgressGroup { Instances = snapshot.Instances, Slots = snapshot.Slots },
            };

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
            var fileResult = db.Deserialize<InventoryProgressSaveFile>();
            if (fileResult.IsFailure) return MdixResult<Unit>.Err(fileResult.Error);

            var saveFile = fileResult.SuccessResult;
            RestoreState(new InventoryProgressSnapshot
            {
                Instances = saveFile.Progress.Instances,
                Slots = saveFile.Progress.Slots,
            });

            return MdixResult<Unit>.Ok(Unit.Value);
        }
    }
}
