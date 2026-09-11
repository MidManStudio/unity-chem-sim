using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.Questly.Core
{
    /// <summary>
    /// Converts between the live <see cref="QuestDefinition"/> model and the
    /// Unity-serializable <see cref="BakedQuest"/> DTOs. Pure data mapping —
    /// no <c>MdixDatabase</c>, no <c>AssetDatabase</c> — so both directions
    /// are unit-testable without a Unity Editor. The Editor-only parts of
    /// baking (writing the result to disk as a <c>.asset</c> file) live in
    /// Questly Studio, not here.
    /// </summary>
    public static class QuestDataBaker
    {
        public static QuestDataAsset Bake(IReadOnlyList<QuestDefinition> quests)
        {
            var asset = ScriptableObject.CreateInstance<QuestDataAsset>();
            PopulateExisting(asset, quests);
            return asset;
        }

        /// <summary>
        /// Fills an already-existing asset in place rather than creating a
        /// new instance — used when the Editor Bake tab targets an asset
        /// the user already selected, so its identity/GUID (and anything
        /// else already referencing it) is preserved.
        /// </summary>
        public static void PopulateExisting(QuestDataAsset target, IReadOnlyList<QuestDefinition> quests)
        {
            target.quests = new List<BakedQuest>(quests.Count);
            foreach (var quest in quests)
                target.quests.Add(ToBaked(quest));
        }

        public static BakedQuest ToBaked(QuestDefinition quest) => new()
        {
            identity = new BakedQuestIdentity
            {
                id = quest.Identity.Id,
                type = quest.Identity.Type,
                titleKey = quest.Identity.TitleKey,
                giverId = quest.Identity.GiverId,
                categoryTags = new List<string>(quest.Identity.CategoryTags),
            },
            objectives = Map(quest.Objectives, o => new BakedObjective
            {
                id = o.Id,
                kind = o.Kind,
                labelKey = o.LabelKey,
                target = o.Target,
                durationSeconds = o.DurationSeconds,
                childIds = new List<string>(o.ChildIds),
                evaluatorKey = o.EvaluatorKey,
                paramsJson = o.ParamsJson,
            }),
            prereqs = Map(quest.Prereqs, p => new BakedPrereq
            {
                kind = p.Kind,
                questId = p.QuestId,
                objectiveId = p.ObjectiveId,
                expected = p.Expected,
                minValue = p.MinValue,
                evaluatorKey = p.EvaluatorKey,
                paramsJson = p.ParamsJson,
            }),
            rewards = Map(quest.Rewards, r => new BakedReward
            {
                rewardId = r.RewardId,
                payloadJson = r.PayloadJson,
            }),
        };

        public static QuestDefinition FromBaked(BakedQuest baked)
        {
            var identity = new QuestIdentity
            {
                Id = baked.identity.id,
                Type = baked.identity.type,
                TitleKey = baked.identity.titleKey,
                GiverId = baked.identity.giverId,
                CategoryTags = new List<string>(baked.identity.categoryTags),
            };

            var objectives = Map(baked.objectives, o => new ObjectiveDefinition
            {
                Id = o.id,
                Kind = o.kind,
                LabelKey = o.labelKey,
                Target = o.target,
                DurationSeconds = o.durationSeconds,
                ChildIds = new List<string>(o.childIds),
                EvaluatorKey = o.evaluatorKey,
                ParamsJson = o.paramsJson,
            });

            var prereqs = Map(baked.prereqs, p => new PrereqDefinition
            {
                Kind = p.kind,
                QuestId = p.questId,
                ObjectiveId = p.objectiveId,
                Expected = p.expected,
                MinValue = p.minValue,
                EvaluatorKey = p.evaluatorKey,
                ParamsJson = p.paramsJson,
            });

            var rewards = Map(baked.rewards, r => new RewardDefinition
            {
                RewardId = r.rewardId,
                PayloadJson = r.payloadJson,
            });

            return new QuestDefinition(identity, objectives, prereqs, rewards);
        }

        private static List<TOut> Map<TIn, TOut>(IReadOnlyList<TIn> source, Func<TIn, TOut> project)
        {
            var result = new List<TOut>(source.Count);
            foreach (var item in source) result.Add(project(item));
            return result;
        }
    }
}
