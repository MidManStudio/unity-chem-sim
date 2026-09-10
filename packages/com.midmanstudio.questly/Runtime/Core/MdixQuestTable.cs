using System;
using System.Collections.Generic;
using MidManStudio.Mdix.Core;

namespace MidManStudio.Questly.Core
{
    /// <summary>Live <see cref="IQuestTable"/> — reads quest definitions straight from a loaded <see cref="MdixDatabase"/>.</summary>
    public sealed class MdixQuestTable : IQuestTable
    {
        private readonly Dictionary<string, QuestDefinition> _byId;
        private readonly List<QuestDefinition> _all;

        public IReadOnlyList<QuestDefinition> AllQuests => _all;

        public QuestDefinition? Find(string questId) =>
            _byId.TryGetValue(questId, out var quest) ? quest : null;

        private MdixQuestTable(List<QuestDefinition> all)
        {
            _all = all;
            _byId = new Dictionary<string, QuestDefinition>(all.Count);
            foreach (var quest in all)
                _byId[quest.Identity.Id] = quest;
        }

        /// <summary>
        /// Loads every quest under the <c>quests.*</c> prefix. A quest with
        /// no <c>prereqs::</c>/<c>rewards::</c> path is valid, not an
        /// error — Questly's schema convention treats a missing path as
        /// "empty", per <c>core/builders.mdix</c>'s own documented rule.
        /// </summary>
        public static MdixResult<MdixQuestTable> Load(MdixDatabase db)
        {
            var idsResult = db.GetKeys("quests");
            if (idsResult.IsFailure)
                return MdixResult<MdixQuestTable>.Err(idsResult.Error);

            var all = new List<QuestDefinition>();
            foreach (var id in idsResult.SuccessResult)
            {
                var loaded = LoadOne(db, id);
                if (loaded.IsFailure)
                    return MdixResult<MdixQuestTable>.Err(loaded.Error);
                all.Add(loaded.SuccessResult);
            }

            return MdixResult<MdixQuestTable>.Ok(new MdixQuestTable(all));
        }

        private static MdixResult<QuestDefinition> LoadOne(MdixDatabase db, string id)
        {
            var identity = db.Deserialize<QuestIdentity>($"quests.{id}.identity.core");
            if (identity.IsFailure)
                return MdixResult<QuestDefinition>.Err(identity.Error);

            var objectivesResult = GetArrayOrEmpty<ObjectiveDefinition>(db, $"quests.{id}.objectives");
            if (objectivesResult.IsFailure)
                return MdixResult<QuestDefinition>.Err(objectivesResult.Error);
            FillOpaqueJson(db, $"quests.{id}.objectives", objectivesResult.SuccessResult, "params",
                (o, json) => o.ParamsJson = json);

            var prereqsResult = GetArrayOrEmpty<PrereqDefinition>(db, $"quests.{id}.prereqs");
            if (prereqsResult.IsFailure)
                return MdixResult<QuestDefinition>.Err(prereqsResult.Error);
            FillOpaqueJson(db, $"quests.{id}.prereqs", prereqsResult.SuccessResult, "params",
                (p, json) => p.ParamsJson = json);

            var rewardsResult = GetArrayOrEmpty<RewardDefinition>(db, $"quests.{id}.rewards");
            if (rewardsResult.IsFailure)
                return MdixResult<QuestDefinition>.Err(rewardsResult.Error);
            FillOpaqueJson(db, $"quests.{id}.rewards", rewardsResult.SuccessResult, "payload",
                (r, json) => r.PayloadJson = json);

            return MdixResult<QuestDefinition>.Ok(new QuestDefinition(
                identity.SuccessResult,
                objectivesResult.SuccessResult,
                prereqsResult.SuccessResult,
                rewardsResult.SuccessResult));
        }

        /// <summary>
        /// A missing array path is Questly's documented "empty list"
        /// convention, not a load failure — only <see cref="MdixErrorKind.NotFound"/>
        /// is downgraded to an empty list; every other error kind (a real
        /// <see cref="MdixErrorKind.TypeMismatch"/>, say) still surfaces normally.
        /// </summary>
        private static MdixResult<List<T>> GetArrayOrEmpty<T>(MdixDatabase db, string path)
        {
            var result = db.GetArray<T>(path);
            if (result.IsFailure && result.Error.Kind == MdixErrorKind.NotFound)
                return MdixResult<List<T>>.Ok(new List<T>());
            return result;
        }

        /// <summary>
        /// MdixSerializer's automatic PascalCase-to-snake_case mapping
        /// doesn't cover a fully dynamic <c>object</c> field, so the opaque
        /// params/payload blob on each array item is fetched separately as
        /// raw JSON and stitched back on after the main deserialize pass.
        /// </summary>
        private static void FillOpaqueJson<T>(
            MdixDatabase db, string arrayPath, List<T> items, string fieldName, Action<T, string> assign)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var json = db.GetJson($"{arrayPath}[{i}].{fieldName}");
                assign(items[i], json.UnwrapOr("{}"));
            }
        }
    }
}
