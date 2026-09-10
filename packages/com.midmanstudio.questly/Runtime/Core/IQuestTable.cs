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
    /// (FFI first, C#/rendering later).
    /// </summary>
    public interface IQuestTable
    {
        IReadOnlyList<QuestDefinition> AllQuests { get; }
        QuestDefinition? Find(string questId);
    }
}
