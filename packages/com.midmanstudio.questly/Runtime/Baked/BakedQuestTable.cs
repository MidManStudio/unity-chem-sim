using System;
using System.Collections.Generic;

namespace MidManStudio.Questly.Core
{
    /// <summary>
    /// Baked <see cref="IQuestTable"/> — reads quest definitions from a
    /// pre-baked <see cref="QuestDataAsset"/>, no <c>MdixDatabase</c> /
    /// native mdix-ffi involved at all. For shipped builds; use
    /// <see cref="MdixQuestTable"/> during development instead, the same
    /// Live-vs-Baked split com.midmanstudio.mdix.localization already uses.
    /// </summary>
    public sealed class BakedQuestTable : IQuestTable
    {
        private readonly List<QuestDefinition> _all;
        private readonly Dictionary<string, QuestDefinition> _byId;

        public IReadOnlyList<QuestDefinition> AllQuests => _all;

        public QuestDefinition? Find(string questId) =>
            _byId.TryGetValue(questId, out var quest) ? quest : null;

        /// <summary>
        /// A baked asset never changes at runtime — shipped builds have
        /// nothing to hot-reload from, so this never fires. The empty
        /// accessor pair satisfies <see cref="IQuestTable"/> without an
        /// unused field-like-event compiler warning.
        /// </summary>
        public event Action? DefinitionsChanged { add { } remove { } }

        public BakedQuestTable(QuestDataAsset asset)
        {
            _all = new List<QuestDefinition>(asset.quests.Count);
            _byId = new Dictionary<string, QuestDefinition>(asset.quests.Count);

            foreach (var baked in asset.quests)
            {
                var quest = QuestDataBaker.FromBaked(baked);
                _all.Add(quest);
                _byId[quest.Identity.Id] = quest;
            }
        }
    }
}
