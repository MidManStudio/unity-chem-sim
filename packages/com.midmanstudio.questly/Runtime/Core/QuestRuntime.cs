using System;
using System.Collections.Generic;
using MidManStudio.Mdix.Core;
using MidManStudio.Questly.Generated;
using MidManStudio.Questly.Objectives;

namespace MidManStudio.Questly.Core
{
    /// <summary>
    /// Zero game-specific logic lives here — every hook (evaluators,
    /// rewards, giver ids, category tags) is opaque data or a host-supplied
    /// callback. A consuming game (GTG or otherwise) layers its own
    /// interpretation on top, the same relationship GTG has with the
    /// chemistry package.
    /// </summary>
    public sealed class QuestRuntime
    {
        private sealed class ObjectiveRuntimeState
        {
            public ObjectiveState State;
            public float Value;
        }

        private readonly IQuestTable _table;

        private readonly Dictionary<string, QuestState> _questStates = new();
        private readonly Dictionary<string, Dictionary<string, ObjectiveRuntimeState>> _objectiveStates = new();
        private readonly Dictionary<string, HashSet<string>> _rootObjectiveIds = new();

        // objective_id -> ids of COMPOSITE_* objectives (same quest) that list it as a child.
        private readonly Dictionary<string, Dictionary<string, List<string>>> _compositeParentsByQuest = new();

        // Reverse-dependency index feeding the watcher, keyed by what a LOCKED quest is waiting on.
        private readonly Dictionary<string, List<string>> _watchersByCompletedQuest = new();          // quest_id -> dependent quest ids
        private readonly Dictionary<string, List<string>> _watchersByObjectiveKey = new();             // "questId::objectiveId" -> dependent quest ids
        private readonly HashSet<string> _questsWithExternalPrereqs = new();

        private readonly Dictionary<string, IQuestObjectiveEvaluator> _objectiveEvaluators = new();
        private readonly Dictionary<string, IQuestPrereqEvaluator> _prereqEvaluators = new();

        public event EventHandler<QuestStateChangedEventArgs>? QuestStateChanged;
        public event EventHandler<QuestObjectiveProgressEventArgs>? ObjectiveProgress;
        public event EventHandler<QuestCompletedEventArgs>? QuestCompleted;

        public QuestRuntime(IQuestTable table)
        {
            _table = table;

            foreach (var quest in table.AllQuests)
                InitializeQuest(quest, isNewQuest: true);

            _table.DefinitionsChanged += Reconcile;
        }

        private void InitializeQuest(QuestDefinition quest, bool isNewQuest)
        {
            var questId = quest.Identity.Id;

            if (isNewQuest)
                _questStates[questId] = quest.Prereqs.Count == 0 ? QuestState.AVAILABLE : QuestState.LOCKED;

            var oldObjectiveStates = _objectiveStates.TryGetValue(questId, out var existing)
                ? existing
                : new Dictionary<string, ObjectiveRuntimeState>();

            var newObjectiveStates = new Dictionary<string, ObjectiveRuntimeState>();
            foreach (var objective in quest.Objectives)
                newObjectiveStates[objective.Id] = oldObjectiveStates.TryGetValue(objective.Id, out var kept)
                    ? kept
                    : new ObjectiveRuntimeState { State = ObjectiveState.INCOMPLETE, Value = 0f };
            _objectiveStates[questId] = newObjectiveStates;

            _rootObjectiveIds[questId] = ComputeRootObjectiveIds(quest);
            _compositeParentsByQuest[questId] = ComputeCompositeParents(quest);

            RemoveFromAllIndexes(questId);
            foreach (var prereq in quest.Prereqs)
                IndexPrereq(questId, prereq);
        }

        /// <summary>
        /// Reconciles runtime state against <see cref="_table"/>'s current
        /// content after a hot reload. Existing quest <i>progress</i>
        /// (<see cref="QuestState"/>) is left completely untouched for
        /// quests that already existed — editing an objective's target
        /// mid-session doesn't reset a player's progress on it, it just
        /// changes what "complete" means going forward. Objective values
        /// are kept by id the same way; a renamed/removed objective id
        /// simply starts fresh if it's ever reintroduced. A quest id
        /// removed from the source entirely is left as orphaned runtime
        /// state — deleting an in-progress quest mid-session isn't a
        /// supported hot-reload scenario, only additive/tweak changes are.
        /// </summary>
        private void Reconcile()
        {
            foreach (var quest in _table.AllQuests)
            {
                var isNewQuest = !_questStates.ContainsKey(quest.Identity.Id);
                InitializeQuest(quest, isNewQuest);
            }

            var locked = new List<string>();
            foreach (var (questId, state) in _questStates)
                if (state == QuestState.LOCKED)
                    locked.Add(questId);
            RunWatcher(locked);
        }

        // ── Registration ─────────────────────────────────────────────────────

        public void RegisterObjectiveEvaluator(string evaluatorKey, IQuestObjectiveEvaluator evaluator) =>
            _objectiveEvaluators[evaluatorKey] = evaluator;

        public void RegisterPrereqEvaluator(string evaluatorKey, IQuestPrereqEvaluator evaluator) =>
            _prereqEvaluators[evaluatorKey] = evaluator;

        // ── Reads ────────────────────────────────────────────────────────────

        public QuestState GetQuestState(string questId) =>
            _questStates.TryGetValue(questId, out var state) ? state : QuestState.LOCKED;

        public ObjectiveState GetObjectiveState(string questId, string objectiveId) =>
            _objectiveStates.TryGetValue(questId, out var byId) && byId.TryGetValue(objectiveId, out var state)
                ? state.State
                : ObjectiveState.INCOMPLETE;

        public float GetObjectiveValue(string questId, string objectiveId) =>
            _objectiveStates.TryGetValue(questId, out var byId) && byId.TryGetValue(objectiveId, out var state)
                ? state.Value
                : 0f;

        // ── Manual control ───────────────────────────────────────────────────

        /// <summary>
        /// Sets any quest's state directly, bypassing prereq evaluation
        /// entirely — for cutscene triggers, dialogue-driven unlocks,
        /// debug/cheat unlocks, or anything that doesn't map cleanly to the
        /// declarative prereq system. Also re-runs the watcher afterward,
        /// since forcing a quest to COMPLETED can unlock others.
        /// </summary>
        public void ForceState(string questId, QuestState newState)
        {
            SetQuestState(questId, newState);
            RunWatcher(CandidatesFor(questId, objectiveId: null));
        }

        /// <summary>Convenience wrapper: moves a quest from AVAILABLE to ACTIVE. No-op if it isn't currently AVAILABLE.</summary>
        public void StartQuest(string questId)
        {
            if (GetQuestState(questId) == QuestState.AVAILABLE)
                SetQuestState(questId, QuestState.ACTIVE);
        }

        // ── Push-based progress ──────────────────────────────────────────────

        /// <summary>Adds <paramref name="amount"/> to an objective's current value — for COUNTER/TIMER objectives.</summary>
        public void ReportProgress(string questId, string objectiveId, float amount)
        {
            if (!TryGetRuntimeState(questId, objectiveId, out var state)) return;
            state!.Value += amount;
            OnObjectiveValueChanged(questId, objectiveId);
        }

        /// <summary>Sets an objective's value directly (1/0) — for FLAG objectives.</summary>
        public void SetFlag(string questId, string objectiveId, bool value)
        {
            if (!TryGetRuntimeState(questId, objectiveId, out var state)) return;
            state!.Value = value ? 1f : 0f;
            OnObjectiveValueChanged(questId, objectiveId);
        }

        /// <summary>
        /// Re-checks a quest's EXTERNAL objectives/prereqs on demand, for
        /// cases where no ReportProgress/SetFlag call naturally fired but
        /// the host knows some external condition may have changed (e.g. an
        /// "IsQuestVisible"-style polling tick).
        /// </summary>
        public void RecheckExternal(string questId)
        {
            RecomputeObjectiveStatesAndCascade(questId, changedObjectiveId: null);
            RunWatcher(new[] { questId });
        }

        // ── Persistence: host-owned ──────────────────────────────────────────

        public QuestProgressSnapshot CaptureState()
        {
            var snapshot = new QuestProgressSnapshot();
            foreach (var (questId, state) in _questStates)
                snapshot.Quests.Add(new QuestProgressRow { QuestId = questId, State = state });

            foreach (var (questId, objectives) in _objectiveStates)
                foreach (var (objectiveId, runtime) in objectives)
                    snapshot.Objectives.Add(new QuestObjectiveProgressRow
                    {
                        QuestId = questId,
                        ObjectiveId = objectiveId,
                        State = runtime.State,
                        Value = runtime.Value,
                    });

            return snapshot;
        }

        /// <summary>
        /// Restores exactly what the snapshot says, then runs the watcher
        /// once over any quest the snapshot doesn't mention at all — quests
        /// added by a game update since the save was made get a fair
        /// initial evaluation instead of silently staying LOCKED forever.
        /// </summary>
        public void RestoreState(QuestProgressSnapshot snapshot)
        {
            var mentioned = new HashSet<string>();

            foreach (var row in snapshot.Quests)
            {
                mentioned.Add(row.QuestId);
                if (_questStates.ContainsKey(row.QuestId))
                    _questStates[row.QuestId] = row.State;
            }

            foreach (var row in snapshot.Objectives)
            {
                if (_objectiveStates.TryGetValue(row.QuestId, out var byId) &&
                    byId.TryGetValue(row.ObjectiveId, out var runtime))
                {
                    runtime.State = row.State;
                    runtime.Value = row.Value;
                }
            }

            var unmentioned = new List<string>();
            foreach (var questId in _questStates.Keys)
                if (!mentioned.Contains(questId))
                    unmentioned.Add(questId);

            RunWatcher(unmentioned);
        }

        // ── Persistence: package-owned (optional) ───────────────────────────

        /// <summary>
        /// Writes current progress as a standalone <c>.mdix</c> file shaped
        /// like <c>Samples~/example_progress_save.mdix</c>. Entirely
        /// optional — a host using <see cref="CaptureState"/>/<see cref="RestoreState"/>
        /// never needs to call this.
        /// </summary>
        public MdixResult<Unit> SaveToMdix(string path, string saveSlot)
        {
            var saveFile = new QuestProgressSaveFile
            {
                SaveSlot = saveSlot,
                SavedAt = DateTimeOffset.UtcNow.ToString("O"),
            };

            var snapshot = CaptureState();
            saveFile.Progress.Quests = snapshot.Quests;
            saveFile.Progress.Objectives = snapshot.Objectives;

            using var builder = MdixBuilder.Create();
            var serializeResult = builder.Serialize(saveFile);
            if (serializeResult.IsFailure)
                return serializeResult;

            return builder.Save(path);
        }

        public MdixResult<Unit> LoadFromMdix(string path)
        {
            var dbResult = MdixDatabase.Load(path);
            if (dbResult.IsFailure)
                return MdixResult<Unit>.Err(dbResult.Error);

            using var db = dbResult.SuccessResult;
            var fileResult = db.Deserialize<QuestProgressSaveFile>();
            if (fileResult.IsFailure)
                return MdixResult<Unit>.Err(fileResult.Error);

            var saveFile = fileResult.SuccessResult;
            RestoreState(new QuestProgressSnapshot
            {
                Quests = saveFile.Progress.Quests,
                Objectives = saveFile.Progress.Objectives,
            });

            return MdixResult<Unit>.Ok(Unit.Value);
        }

        // ── Internal: state transitions ──────────────────────────────────────

        private void SetQuestState(string questId, QuestState newState)
        {
            var previous = GetQuestState(questId);
            if (previous == newState) return;

            _questStates[questId] = newState;
            QuestStateChanged?.Invoke(this, new QuestStateChangedEventArgs(questId, previous, newState));

            if (newState == QuestState.COMPLETED)
            {
                var quest = _table.Find(questId);
                if (quest != null)
                    QuestCompleted?.Invoke(this, new QuestCompletedEventArgs(questId, quest.Rewards));
            }
        }

        private bool TryGetRuntimeState(string questId, string objectiveId, out ObjectiveRuntimeState? state)
        {
            state = null;
            if (!_objectiveStates.TryGetValue(questId, out var byId)) return false;
            if (!byId.TryGetValue(objectiveId, out var found)) return false;
            state = found;
            return true;
        }

        private void OnObjectiveValueChanged(string questId, string objectiveId)
        {
            RecomputeObjectiveStatesAndCascade(questId, objectiveId);
            RunWatcher(CandidatesFor(questId, objectiveId));
        }

        /// <summary>
        /// Recomputes <paramref name="changedObjectiveId"/>'s completion
        /// (or every objective's, if null — used by <see cref="RecheckExternal"/>),
        /// cascades into any COMPOSITE_* parents in the same quest, fires
        /// <see cref="ObjectiveProgress"/> for anything that changed, then
        /// checks whether the quest as a whole just completed.
        /// </summary>
        private void RecomputeObjectiveStatesAndCascade(string questId, string? changedObjectiveId)
        {
            var quest = _table.Find(questId);
            if (quest == null) return;

            var pending = new Queue<string>();
            if (changedObjectiveId != null) pending.Enqueue(changedObjectiveId);
            else foreach (var objective in quest.Objectives) pending.Enqueue(objective.Id);

            var visited = new HashSet<string>();
            while (pending.Count > 0)
            {
                var objectiveId = pending.Dequeue();
                if (!visited.Add(objectiveId)) continue;

                var objective = FindObjective(quest, objectiveId);
                if (objective == null) continue;

                var runtime = _objectiveStates[questId][objectiveId];
                var wasComplete = runtime.State == ObjectiveState.COMPLETE;
                var isComplete = IsObjectiveComplete(quest, objective, _objectiveStates[questId]);
                runtime.State = isComplete ? ObjectiveState.COMPLETE : ObjectiveState.INCOMPLETE;

                if (runtime.State != (wasComplete ? ObjectiveState.COMPLETE : ObjectiveState.INCOMPLETE))
                    ObjectiveProgress?.Invoke(this, new QuestObjectiveProgressEventArgs(questId, objectiveId, runtime.Value, runtime.State));

                if (isComplete != wasComplete &&
                    _compositeParentsByQuest[questId].TryGetValue(objectiveId, out var parents))
                {
                    foreach (var parentId in parents) pending.Enqueue(parentId);
                }
            }

            CompleteQuestIfReady(questId, quest);
        }

        private void CompleteQuestIfReady(string questId, QuestDefinition quest)
        {
            if (GetQuestState(questId) != QuestState.ACTIVE) return;

            foreach (var rootId in _rootObjectiveIds[questId])
                if (_objectiveStates[questId][rootId].State != ObjectiveState.COMPLETE)
                    return;

            SetQuestState(questId, QuestState.COMPLETED);
        }

        private bool IsObjectiveComplete(QuestDefinition quest, ObjectiveDefinition objective, Dictionary<string, ObjectiveRuntimeState> states)
        {
            switch (objective.Kind)
            {
                case ObjectiveKind.FLAG:
                    return states[objective.Id].Value != 0f;

                case ObjectiveKind.COUNTER:
                    return states[objective.Id].Value >= objective.Target;

                case ObjectiveKind.TIMER:
                    return states[objective.Id].Value >= objective.DurationSeconds;

                case ObjectiveKind.COMPOSITE_ALL:
                    foreach (var childId in objective.ChildIds)
                        if (!states.TryGetValue(childId, out var childAll) || childAll.State != ObjectiveState.COMPLETE)
                            return false;
                    return true;

                case ObjectiveKind.COMPOSITE_ANY:
                    foreach (var childId in objective.ChildIds)
                        if (states.TryGetValue(childId, out var childAny) && childAny.State == ObjectiveState.COMPLETE)
                            return true;
                    return false;

                case ObjectiveKind.EXTERNAL:
                    return _objectiveEvaluators.TryGetValue(objective.EvaluatorKey, out var evaluator)
                        && evaluator.Evaluate(quest.Identity.Id, objective.Id, objective.ParamsJson);

                default:
                    return false;
            }
        }

        // ── Internal: watcher ────────────────────────────────────────────────

        private void IndexPrereq(string dependentQuestId, PrereqDefinition prereq)
        {
            switch (prereq.Kind)
            {
                case PrereqKind.QUEST_COMPLETED:
                    AddToIndex(_watchersByCompletedQuest, prereq.QuestId, dependentQuestId);
                    break;

                case PrereqKind.OBJECTIVE_COMPLETE:
                case PrereqKind.FLAG_VALUE:
                case PrereqKind.COUNTER_AT_LEAST:
                    AddToIndex(_watchersByObjectiveKey, ObjectiveKey(prereq.QuestId, prereq.ObjectiveId), dependentQuestId);
                    break;

                case PrereqKind.EXTERNAL:
                    // No structural key to index by — re-checked opportunistically
                    // via RunWatcher's own external pass, or explicitly via RecheckExternal.
                    _questsWithExternalPrereqs.Add(dependentQuestId);
                    break;
            }
        }

        private static void AddToIndex(Dictionary<string, List<string>> index, string key, string dependentQuestId)
        {
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<string>();
                index[key] = list;
            }
            if (!list.Contains(dependentQuestId))
                list.Add(dependentQuestId);
        }

        /// <summary>
        /// Strips every watcher-index entry referencing <paramref name="dependentQuestId"/>
        /// before re-indexing its (possibly changed) prereqs — otherwise a
        /// prereq removed by a hot reload would leave a stale watcher entry
        /// behind forever.
        /// </summary>
        private void RemoveFromAllIndexes(string dependentQuestId)
        {
            foreach (var list in _watchersByCompletedQuest.Values) list.Remove(dependentQuestId);
            foreach (var list in _watchersByObjectiveKey.Values) list.Remove(dependentQuestId);
            _questsWithExternalPrereqs.Remove(dependentQuestId);
        }

        private static string ObjectiveKey(string questId, string objectiveId) => $"{questId}::{objectiveId}";

        private IEnumerable<string> CandidatesFor(string questId, string? objectiveId)
        {
            if (_watchersByCompletedQuest.TryGetValue(questId, out var byQuest))
                foreach (var id in byQuest) yield return id;

            if (objectiveId != null &&
                _watchersByObjectiveKey.TryGetValue(ObjectiveKey(questId, objectiveId), out var byObjective))
                foreach (var id in byObjective) yield return id;

            foreach (var id in _questsWithExternalPrereqs) yield return id;
        }

        /// <summary>
        /// LOCKED -&gt; AVAILABLE only, never the reverse — if a satisfied
        /// prereq's condition later goes false again, an already-available
        /// quest stays available. Re-locking is a <see cref="ForceState"/>
        /// call, not something the watcher ever does on its own.
        /// </summary>
        private void RunWatcher(IEnumerable<string> candidateQuestIds)
        {
            foreach (var questId in candidateQuestIds)
            {
                if (GetQuestState(questId) != QuestState.LOCKED) continue;

                var quest = _table.Find(questId);
                if (quest == null) continue;

                if (EvaluateAllPrereqs(quest))
                    SetQuestState(questId, QuestState.AVAILABLE);
            }
        }

        private bool EvaluateAllPrereqs(QuestDefinition quest)
        {
            foreach (var prereq in quest.Prereqs)
                if (!EvaluatePrereq(prereq))
                    return false;
            return true;
        }

        private bool EvaluatePrereq(PrereqDefinition prereq)
        {
            switch (prereq.Kind)
            {
                case PrereqKind.QUEST_COMPLETED:
                    return GetQuestState(prereq.QuestId) == QuestState.COMPLETED;

                case PrereqKind.OBJECTIVE_COMPLETE:
                    return GetObjectiveState(prereq.QuestId, prereq.ObjectiveId) == ObjectiveState.COMPLETE;

                case PrereqKind.FLAG_VALUE:
                    return (GetObjectiveValue(prereq.QuestId, prereq.ObjectiveId) != 0f) == prereq.Expected;

                case PrereqKind.COUNTER_AT_LEAST:
                    return GetObjectiveValue(prereq.QuestId, prereq.ObjectiveId) >= prereq.MinValue;

                case PrereqKind.EXTERNAL:
                    return _prereqEvaluators.TryGetValue(prereq.EvaluatorKey, out var evaluator)
                        && evaluator.Evaluate(prereq.ParamsJson);

                default:
                    return false;
            }
        }

        // ── Internal: precomputed graph helpers ─────────────────────────────

        private static ObjectiveDefinition? FindObjective(QuestDefinition quest, string objectiveId)
        {
            foreach (var objective in quest.Objectives)
                if (objective.Id == objectiveId)
                    return objective;
            return null;
        }

        /// <summary>Objectives never referenced as a child by any COMPOSITE_* objective in the same quest — a quest completes when all of these are COMPLETE.</summary>
        private static HashSet<string> ComputeRootObjectiveIds(QuestDefinition quest)
        {
            var childIds = new HashSet<string>();
            foreach (var objective in quest.Objectives)
                foreach (var childId in objective.ChildIds)
                    childIds.Add(childId);

            var roots = new HashSet<string>();
            foreach (var objective in quest.Objectives)
                if (!childIds.Contains(objective.Id))
                    roots.Add(objective.Id);
            return roots;
        }

        private static Dictionary<string, List<string>> ComputeCompositeParents(QuestDefinition quest)
        {
            var parents = new Dictionary<string, List<string>>();
            foreach (var objective in quest.Objectives)
            {
                foreach (var childId in objective.ChildIds)
                {
                    if (!parents.TryGetValue(childId, out var list))
                    {
                        list = new List<string>();
                        parents[childId] = list;
                    }
                    list.Add(objective.Id);
                }
            }
            return parents;
        }
    }
}
