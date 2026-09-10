using System;
using System.Collections.Generic;

namespace MidManStudio.Questly.Core
{
    /// <summary>
    /// Abstraction over where quest <i>definitions</i> come from — mirrors
    /// com.midmanstudio.mdix.localization's <c>ILocaleTable</c> split
    /// exactly. <see cref="MdixQuestTable"/> (this v1) reads straight from
    /// <c>MdixDatabase</c>; a future <c>BakedQuestTable</c>
    /// (ScriptableObject-backed, for shipped builds) is deferred until the
    /// schema stabilizes — the same staged order alembic itself followed
    /// (FFI first, C#/rendering later). A baked table simply never raises
    /// <see cref="DefinitionsChanged"/> — shipped builds have nothing to
    /// hot-reload from.
    /// </summary>
    public interface IQuestTable
    {
        IReadOnlyList<QuestDefinition> AllQuests { get; }
        QuestDefinition? Find(string questId);

        /// <summary>
        /// Raised after this table's own content has already been refreshed
        /// in place — subscribers read <see cref="AllQuests"/>/<see cref="Find"/>
        /// fresh from inside the handler, there's no separate "new table"
        /// object to swap to.
        /// </summary>
        event Action? DefinitionsChanged;
    }
}
