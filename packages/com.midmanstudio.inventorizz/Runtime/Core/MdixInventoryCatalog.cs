using System;
using System.Collections.Generic;
using MidManStudio.Mdix.Core;

namespace MidManStudio.Inventorizz.Core
{
    /// <summary>
    /// Loads <c>items.*</c>/<c>container_types.*</c> from a real
    /// <see cref="MdixDatabase"/>, mirroring <c>MdixQuestTable</c>'s
    /// pattern exactly: enumerate ids via <c>GetKeys</c>, deserialize each
    /// one's <c>identity.core</c> path.
    /// </summary>
    public sealed class MdixInventoryCatalog : IInventoryCatalog
    {
        private readonly Dictionary<string, ItemDefinition> _itemsById;
        private readonly Dictionary<string, ContainerTypeDefinition> _typesById;
        private readonly List<ItemDefinition> _itemsList;
        private readonly List<ContainerTypeDefinition> _typesList;

        public IReadOnlyList<ItemDefinition> AllItems => _itemsList;
        public IReadOnlyList<ContainerTypeDefinition> AllContainerTypes => _typesList;

        public event Action? DefinitionsChanged;

        private MdixInventoryCatalog(
            Dictionary<string, ItemDefinition> itemsById,
            Dictionary<string, ContainerTypeDefinition> typesById)
        {
            _itemsById = itemsById;
            _typesById = typesById;
            _itemsList = new List<ItemDefinition>(itemsById.Values);
            _typesList = new List<ContainerTypeDefinition>(typesById.Values);
        }

        public ItemDefinition? FindItem(string itemId) =>
            _itemsById.TryGetValue(itemId, out var item) ? item : null;

        public ContainerTypeDefinition? FindContainerType(string containerTypeId) =>
            _typesById.TryGetValue(containerTypeId, out var type) ? type : null;

        /// <summary>
        /// Reloads in place and fires <see cref="DefinitionsChanged"/> --
        /// for a host that wants to swap content at runtime.
        /// <see cref="InventorizzRuntime"/> itself does not subscribe to
        /// this (see its class doc for why); a host wiring hot-reload
        /// alongside live container instances needs to decide its own
        /// reconciliation policy for existing stacks.
        /// </summary>
        public MdixResult<Unit> Reload(MdixDatabase db)
        {
            var loaded = LoadFrom(db);
            if (loaded.IsFailure) return MdixResult<Unit>.Err(loaded.Error);

            _itemsById.Clear();
            foreach (var kv in loaded.SuccessResult._itemsById) _itemsById[kv.Key] = kv.Value;
            _itemsList.Clear();
            _itemsList.AddRange(_itemsById.Values);

            _typesById.Clear();
            foreach (var kv in loaded.SuccessResult._typesById) _typesById[kv.Key] = kv.Value;
            _typesList.Clear();
            _typesList.AddRange(_typesById.Values);

            DefinitionsChanged?.Invoke();
            return MdixResult<Unit>.Ok(Unit.Value);
        }

        public static MdixResult<MdixInventoryCatalog> LoadFrom(MdixDatabase db)
        {
            var itemsById = new Dictionary<string, ItemDefinition>();
            var itemIdsResult = db.GetKeys("items");
            if (itemIdsResult.IsSuccess)
            {
                foreach (var id in itemIdsResult.SuccessResult)
                {
                    var result = db.Deserialize<ItemDefinition>($"items.{id}.identity.core");
                    if (result.IsFailure) return MdixResult<MdixInventoryCatalog>.Err(result.Error);
                    itemsById[id] = result.SuccessResult;
                }
            }
            else if (itemIdsResult.Error.Kind != MdixErrorKind.NotFound)
            {
                return MdixResult<MdixInventoryCatalog>.Err(itemIdsResult.Error);
            }

            var typesById = new Dictionary<string, ContainerTypeDefinition>();
            var typeIdsResult = db.GetKeys("container_types");
            if (typeIdsResult.IsSuccess)
            {
                foreach (var id in typeIdsResult.SuccessResult)
                {
                    var result = db.Deserialize<ContainerTypeDefinition>($"container_types.{id}.identity.core");
                    if (result.IsFailure) return MdixResult<MdixInventoryCatalog>.Err(result.Error);
                    typesById[id] = result.SuccessResult;
                }
            }
            else if (typeIdsResult.Error.Kind != MdixErrorKind.NotFound)
            {
                return MdixResult<MdixInventoryCatalog>.Err(typeIdsResult.Error);
            }

            return MdixResult<MdixInventoryCatalog>.Ok(new MdixInventoryCatalog(itemsById, typesById));
        }
    }
}
