using System.Collections.Generic;
using MidManStudio.Mdix.Core;
using MidManStudio.Questly.Generated;

namespace MidManStudio.Questly.Core
{
    /// <summary>
    /// Content-quality validation for a Questly database, built on
    /// <c>MdixSchema</c>'s existing framework. Two layers: cheap structural
    /// checks via <see cref="MdixSchemaBuilder"/> (does <c>database_name</c>/
    /// <c>database_version</c> exist), and Questly-specific referential
    /// integrity over the whole loaded quest graph (dangling references,
    /// cycles, duplicates) via <see cref="ValidateQuestGraph"/> — the latter
    /// is a pure function over already-loaded <see cref="QuestDefinition"/>s
    /// with no <c>MdixDatabase</c> dependency, so it's fully testable
    /// without native FFI, the same way <see cref="QuestRuntime"/>'s own
    /// logic is.
    ///
    /// Deliberately out of scope for this pass: checking that a quest's
    /// mdix path segment (e.g. <c>quests.foo</c>) matches its own
    /// <c>identity.core.id</c> field. That needs the raw path keys
    /// alongside each loaded quest, which <see cref="MdixQuestTable"/>
    /// doesn't currently expose — a reasonable small follow-up, not bundled
    /// in here to avoid re-touching already-landed loader code for a
    /// separate concern.
    /// </summary>
    public sealed class QuestlySchema : IMdixSchemaSource
    {
        public MdixValidationReport Validate(MdixDatabase db)
        {
            var report = new MdixValidationReport();
            report.Errors.AddRange(BuildStructuralSchema().Validate(db).Errors);

            var loaded = MdixQuestTable.Load(db);
            if (loaded.IsFailure)
            {
                report.Errors.Add(new MdixValidationError
                {
                    Path = "quests",
                    Kind = MdixValidationErrorKind.Missing,
                    Message = $"Failed to load quest definitions: {loaded.Error.Message}",
                });
                return report;
            }

            report.Errors.AddRange(ValidateQuestGraph(loaded.SuccessResult.AllQuests));
            return report;
        }

        private static MdixSchemaBuilder BuildStructuralSchema() =>
            new MdixSchemaBuilder()
                .RequireString("database_name")
                .RequireString("database_version");

        /// <summary>
        /// Pure referential-integrity pass over an already-loaded quest
        /// graph — no <c>MdixDatabase</c> involved, so this is
        /// unit-testable with hand-built <see cref="QuestDefinition"/>
        /// lists the same way <see cref="QuestRuntime"/>'s logic is.
        /// </summary>
        public static List<MdixValidationError> ValidateQuestGraph(IReadOnlyList<QuestDefinition> quests)
        {
            var errors = new List<MdixValidationError>();

            var questsById = new Dictionary<string, QuestDefinition>();
            foreach (var quest in quests)
            {
                var id = quest.Identity.Id;
                if (questsById.ContainsKey(id))
                {
                    errors.Add(Error($"quests.{id}", $"Duplicate quest id \"{id}\" — two quests share the same identity.id."));
                    continue;
                }
                questsById[id] = quest;
            }

            foreach (var quest in questsById.Values)
                ValidateOneQuest(quest, questsById, errors);

            ValidateNoCircularQuestDependencies(questsById, errors);

            return errors;
        }

        private static void ValidateOneQuest(QuestDefinition quest, Dictionary<string, QuestDefinition> questsById, List<MdixValidationError> errors)
        {
            var questId = quest.Identity.Id;

            var objectiveIds = new HashSet<string>();
            foreach (var objective in quest.Objectives)
            {
                if (!objectiveIds.Add(objective.Id))
                    errors.Add(Error($"quests.{questId}.objectives", $"Duplicate objective id \"{objective.Id}\" within quest \"{questId}\"."));

                if (objective.Kind == ObjectiveKind.EXTERNAL && string.IsNullOrEmpty(objective.EvaluatorKey))
                {
                    errors.Add(Error($"quests.{questId}.objectives.{objective.Id}",
                        $"Objective \"{objective.Id}\" is EXTERNAL but evaluator_key is empty — it can never resolve to a registered evaluator."));
                }

                if (objective.Kind == ObjectiveKind.COMPOSITE_ALL || objective.Kind == ObjectiveKind.COMPOSITE_ANY)
                {
                    foreach (var childId in objective.ChildIds)
                    {
                        if (childId == objective.Id)
                        {
                            errors.Add(Error($"quests.{questId}.objectives.{objective.Id}.child_ids",
                                $"Composite objective \"{objective.Id}\" lists itself as a child — this can never complete."));
                            continue;
                        }

                        var childExists = false;
                        foreach (var candidate in quest.Objectives)
                            if (candidate.Id == childId) { childExists = true; break; }

                        if (!childExists)
                        {
                            errors.Add(Error($"quests.{questId}.objectives.{objective.Id}.child_ids",
                                $"Composite objective \"{objective.Id}\" references child \"{childId}\", which doesn't exist as an objective in quest \"{questId}\"."));
                        }
                    }
                }
            }

            foreach (var prereq in quest.Prereqs)
            {
                if (prereq.Kind == PrereqKind.EXTERNAL)
                {
                    if (string.IsNullOrEmpty(prereq.EvaluatorKey))
                        errors.Add(Error($"quests.{questId}.prereqs", $"An EXTERNAL prereq on quest \"{questId}\" has an empty evaluator_key — it can never resolve."));
                    continue;
                }

                if (!questsById.TryGetValue(prereq.QuestId, out var targetQuest))
                {
                    errors.Add(Error($"quests.{questId}.prereqs", $"Prereq on quest \"{questId}\" references quest_id \"{prereq.QuestId}\", which doesn't exist."));
                    continue;
                }

                if (prereq.Kind == PrereqKind.OBJECTIVE_COMPLETE || prereq.Kind == PrereqKind.FLAG_VALUE || prereq.Kind == PrereqKind.COUNTER_AT_LEAST)
                {
                    var objectiveExists = false;
                    foreach (var candidate in targetQuest.Objectives)
                        if (candidate.Id == prereq.ObjectiveId) { objectiveExists = true; break; }

                    if (!objectiveExists)
                    {
                        errors.Add(Error($"quests.{questId}.prereqs",
                            $"Prereq on quest \"{questId}\" references objective_id \"{prereq.ObjectiveId}\" on quest \"{prereq.QuestId}\", which doesn't exist."));
                    }
                }
            }
        }

        /// <summary>DFS cycle detection over QUEST_COMPLETED prereq edges only — the only prereq kind that creates a quest-to-quest dependency graph.</summary>
        private static void ValidateNoCircularQuestDependencies(Dictionary<string, QuestDefinition> questsById, List<MdixValidationError> errors)
        {
            var state = new Dictionary<string, int>(); // absent = unvisited, 1 = in progress, 2 = done
            var reported = new HashSet<string>();

            void Visit(string questId, List<string> path)
            {
                if (state.TryGetValue(questId, out var s))
                {
                    if (s == 2) return;
                    if (s == 1)
                    {
                        var cycleDescription = string.Join(" -> ", path) + " -> " + questId;
                        if (reported.Add(cycleDescription))
                            errors.Add(Error($"quests.{questId}.prereqs", $"Circular QUEST_COMPLETED dependency: {cycleDescription}"));
                        return;
                    }
                }

                state[questId] = 1;
                path.Add(questId);

                if (questsById.TryGetValue(questId, out var quest))
                {
                    foreach (var prereq in quest.Prereqs)
                        if (prereq.Kind == PrereqKind.QUEST_COMPLETED && questsById.ContainsKey(prereq.QuestId))
                            Visit(prereq.QuestId, new List<string>(path));
                }

                state[questId] = 2;
            }

            foreach (var questId in questsById.Keys)
                Visit(questId, new List<string>());
        }

        private static MdixValidationError Error(string path, string message) => new()
        {
            Path = path,
            Kind = MdixValidationErrorKind.InvalidValue,
            Message = message,
        };
    }
}
