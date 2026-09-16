using System;
using System.Collections.Generic;

namespace MidManStudio.Inventorizz.Core
{
    /// <summary>
    /// Read-only catalog of item and container-type definitions. Mirrors
    /// <c>IQuestTable</c>'s shape exactly -- an in-memory fake for tests,
    /// <see cref="MdixInventoryCatalog"/> for real content, anything else a
    /// host wants (a ScriptableObject-backed catalog, say) for the rest.
    /// </summary>
    public interface IInventoryCatalog
    {
        IReadOnlyList<ItemDefinition> AllItems { get; }
        IReadOnlyList<ContainerTypeDefinition> AllContainerTypes { get; }

        ItemDefinition? FindItem(string itemId);
        ContainerTypeDefinition? FindContainerType(string containerTypeId);

        /// <summary>Fired after a hot-reload finishes swapping in new definitions. Matches IQuestTable's DefinitionsChanged -- InventorizzRuntime doesn't subscribe to this itself in 0.1.0 (see InventorizzRuntime's class doc for why), but a future version or a host's own code can.</summary>
        event Action? DefinitionsChanged;
    }
}
