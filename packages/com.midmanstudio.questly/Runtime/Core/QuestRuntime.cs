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

        // questId -> ids of rewards already claimed. Never touched by InitializeQuest/Reconcile -- unlike
        // objective states, claim state has no shape to reconcile against a hot-reloaded definition, it's
        // purely "has this specific reward id been claimed for this specific quest id, yes or no".
        private readonly Dictionary<string, HashSet<string>> _claimedRewards = new();

        // Scheduling (0.3.0). Only ever populated for a quest whose Schedule.Kind
        // isn't NONE -- an unscheduled quest never touches any of these three.
        // CyclesCompleted only ever increases and feeds the per-cycle reward
        // track (CanClaimCycleReward) independently of QuestState, which for a
        // RECURRING quest keeps cycling through LOCKED/AVAILABLE/ACTIVE/COMPLETED
        // long after any individual cycle's own completion. LastGrantedCycle is
        // a re-entrancy/reload guard: it's the cycle index EvaluateSchedule last
        // granted AVAILABLE for, so (a) repeated AdvanceTime calls within one
        // still-open window don't reset objective progress over and over, and
        // (b) restoring a save mid-window doesn't misread "already in this
        // cycle" as "a new cycle just opened" and wrongly reset it.
        private readonly Dictionary<string, int> _cyclesCompleted = new();
        private readonly Dictionary<string, int> _lastGrantedCycle = new();
        private readonly Dictionary<string, HashSet<int>> _claimedCycleRewards = new();

        public event EventHandler<QuestStateChangedEventArgs>? QuestStateChanged;
        public event EventHandler<QuestObjectiveProgressEventArgs>? ObjectiveProgress;
        public event EventHandler<QuestCompletedEventArgs>? QuestCompleted;
        public event EventHandler<RewardClaimedEventArgs>? RewardClaimed;
        public event EventHandler<CycleRewardClaimedEventArgs>? CycleRewardClaimed;

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
                _questStates[questId] = (quest.Schedule.Kind == ScheduleKind.NONE && quest.Prereqs.Count == 0)
                    ? QuestState.AVAILABLE
                    : QuestState.LOCKED;

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
        ///
        /// On a scheduled quest (<see cref="ScheduleDefinition.Kind"/> isn't
        /// NONE), this is a one-time override, not a standing exemption --
        /// the next <see cref="AdvanceTime"/> call still evaluates that
        /// quest's window normally and can move it again (e.g. force it
        /// AVAILABLE outside its window and a RECURRING schedule will
        /// revert it to LOCKED on the very next tick).
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

        // ── Scheduling ───────────────────────────────────────────────────────

        /// <summary>
        /// The clock pump for every quest with a <see cref="ScheduleDefinition"/>
        /// (<see cref="ScheduleDefinition.Kind"/> other than NONE) -- a no-op
        /// for anything else. Call as often or as rarely as fits your game:
        /// every <c>Update()</c> with an accumulated float, a server's Unix
        /// timestamp, an abstract day-counter you bump on sunrise, whatever
        /// -- Questly never assumes an epoch or a unit, the same contract
        /// <see cref="ObjectiveKind.TIMER"/>'s duration already uses. The
        /// only requirement is that <paramref name="now"/> never decreases
        /// between calls.
        ///
        /// Per scheduled quest, independently:
        /// <list type="bullet">
        /// <item>ACTIVE is never touched, regardless of the window -- an
        /// attempt already under way always gets to finish or fail on its
        /// own terms, it's never cut off mid-attempt by a closing window.</item>
        /// <item>LOCKED/AVAILABLE (not yet started): entering an open window
        /// promotes to AVAILABLE, still gated by this quest's own ordinary
        /// prereqs if it has any; leaving one unstarted reverts a ONE_SHOT
        /// to the terminal EXPIRED state, or a RECURRING quest back to
        /// LOCKED to wait for its next window.</item>
        /// <item>COMPLETED: a ONE_SHOT quest is left alone (already a
        /// natural terminal state). A RECURRING quest re-arms for its next
        /// cycle's window (objectives reset fresh -- see
        /// <see cref="ResetObjectiveProgress"/>) unless
        /// <see cref="ScheduleDefinition.StopAfterFirstCompletion"/> is
        /// true, in which case it stays COMPLETED forever.</item>
        /// <item>FAILED/ABANDONED: a RECURRING quest re-arms for the next
        /// cycle exactly like COMPLETED-without-stop does -- failing or
        /// abandoning one cycle doesn't lock you out of the next one. A
        /// ONE_SHOT quest gets no free retry before its window closes, but
        /// isn't left in a different terminal state either -- once the
        /// window closes it becomes EXPIRED same as an untouched one would,
        /// one consistent "this one-shot opportunity is entirely gone"
        /// signal regardless of whether the player failed, abandoned, or
        /// never engaged it at all.</item>
        /// </list>
        /// </summary>
        public void AdvanceTime(double now)
        {
            foreach (var quest in _table.AllQuests)
            {
                if (quest.Schedule.Kind == ScheduleKind.NONE) continue;
                EvaluateSchedule(quest, now);
            }
        }

        private void EvaluateSchedule(QuestDefinition quest, double now)
        {
            var questId = quest.Identity.Id;
            var schedule = quest.Schedule;
            var state = GetQuestState(questId);

            if (state == QuestState.ACTIVE) return;

            if (schedule.Kind == ScheduleKind.ONE_SHOT)
            {
                if (state == QuestState.COMPLETED || state == QuestState.EXPIRED) return;

                var windowClose = schedule.WindowStart + schedule.WindowDuration;
                if (now >= windowClose)
                {
                    SetQuestState(questId, QuestState.EXPIRED);
                    return;
                }

                if (now >= schedule.WindowStart && state == QuestState.LOCKED && EvaluateAllPrereqs(quest))
                    SetQuestState(questId, QuestState.AVAILABLE);

                return;
            }

            // RECURRING from here down.
            if (state == QuestState.COMPLETED && schedule.StopAfterFirstCompletion) return;
            if (schedule.RecurrenceInterval <= 0 || now < schedule.WindowStart) return;

            var cycleIndex = (int)Math.Floor((now - schedule.WindowStart) / schedule.RecurrenceInterval);
            var cycleOpen = schedule.WindowStart + cycleIndex * schedule.RecurrenceInterval;
            var inWindow = now < cycleOpen + schedule.WindowDuration;

            if (!inWindow)
            {
                if (state == QuestState.AVAILABLE)
                    SetQuestState(questId, QuestState.LOCKED);
                return;
            }

            if (GetLastGrantedCycle(questId) == cycleIndex) return; // already arm/checked for this exact cycle

            var reArmable = state == QuestState.LOCKED || state == QuestState.COMPLETED ||
                             state == QuestState.FAILED || state == QuestState.ABANDONED;
            if (!reArmable || !EvaluateAllPrereqs(quest)) return;

            ResetObjectiveProgress(questId, quest);
            _lastGrantedCycle[questId] = cycleIndex;
            SetQuestState(questId, QuestState.AVAILABLE);
        }

        private int GetLastGrantedCycle(string questId) =>
            _lastGrantedCycle.TryGetValue(questId, out var cycle) ? cycle : -1;

        /// <summary>
        /// Fresh-start for a RECURRING quest's new cycle (locked-in default
        /// per this feature's own design discussion: each cycle's objectives
        /// reset rather than carrying progress over from the last one).
        /// Fires <see cref="ObjectiveProgress"/> for every objective so a
        /// UI bound to it sees the reset, same as any other progress change.
        /// </summary>
        private void ResetObjectiveProgress(string questId, QuestDefinition quest)
        {
            if (!_objectiveStates.TryGetValue(questId, out var states)) return;

            foreach (var objective in quest.Objectives)
            {
                if (!states.TryGetValue(objective.Id, out var runtime)) continue;
                runtime.State = ObjectiveState.INCOMPLETE;
                runtime.Value = 0f;
                ObjectiveProgress?.Invoke(this, new QuestObjectiveProgressEventArgs(questId, objective.Id, 0f, ObjectiveState.INCOMPLETE));
            }
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

        // ── Reward claiming ──────────────────────────────────────────────────

        public bool IsRewardClaimed(string questId, string rewardId) =>
            _claimedRewards.TryGetValue(questId, out var claimed) && claimed.Contains(rewardId);

        /// <summary>
        /// A reward can be claimed once its quest is COMPLETED, it hasn't
        /// already been claimed, and every reward listed before it on
        /// <see cref="QuestDefinition.Rewards"/> has already been claimed
        /// -- list order IS claim order. A quest with a single reward
        /// therefore always has exactly one, immediately-claimable entry;
        /// a quest with several has to be claimed one at a time in
        /// authored order, covering a battle-pass-style reward track
        /// without needing any new schema concept for "tiers".
        /// </summary>
        public bool CanClaimReward(string questId, string rewardId)
        {
            if (GetQuestState(questId) != QuestState.COMPLETED) return false;

            var quest = _table.Find(questId);
            if (quest == null) return false;

            foreach (var reward in quest.Rewards)
            {
                if (reward.RewardId == rewardId)
                    return !IsRewardClaimed(questId, rewardId);
                if (!IsRewardClaimed(questId, reward.RewardId))
                    return false; // an earlier reward in the sequence is still unclaimed
            }
            return false; // rewardId isn't one of this quest's rewards at all
        }

        /// <summary>
        /// Claims a reward if <see cref="CanClaimReward"/> allows it,
        /// firing <see cref="RewardClaimed"/> and returning true.
        /// Returns false with no exception otherwise, so a UI can wire a
        /// Claim button straight to this without needing to guard every
        /// call with its own CanClaimReward check first.
        /// </summary>
        public bool TryClaimReward(string questId, string rewardId)
        {
            if (!CanClaimReward(questId, rewardId)) return false;

            var quest = _table.Find(questId)!;
            RewardDefinition? reward = null;
            foreach (var r in quest.Rewards)
                if (r.RewardId == rewardId) { reward = r; break; }
            if (reward == null) return false;

            if (!_claimedRewards.TryGetValue(questId, out var claimed))
            {
                claimed = new HashSet<string>();
                _claimedRewards[questId] = claimed;
            }
            claimed.Add(rewardId);

            RewardClaimed?.Invoke(this, new RewardClaimedEventArgs(questId, reward));
            return true;
        }

        /// <summary>True once every reward on the quest has been claimed -- distinct from QuestState.COMPLETED, which only means the objectives are done, not that the player has collected everything from it yet.</summary>
        public bool AllRewardsClaimed(string questId)
        {
            var quest = _table.Find(questId);
            if (quest == null || quest.Rewards.Count == 0) return false;

            foreach (var reward in quest.Rewards)
                if (!IsRewardClaimed(questId, reward.RewardId))
                    return false;
            return true;
        }

        /// <summary>
        /// The single reward a missions-tab-style UI should currently
        /// highlight as claimable for this quest, or null if there isn't
        /// one (not completed yet, no rewards defined, or everything's
        /// already been claimed). Covers the common "just show me the
        /// next thing to claim" UI case without the caller needing to
        /// walk the whole Rewards list itself.
        /// </summary>
        public RewardDefinition? GetNextClaimableReward(string questId)
        {
            var quest = _table.Find(questId);
            if (quest == null) return null;

            foreach (var reward in quest.Rewards)
                if (!IsRewardClaimed(questId, reward.RewardId))
                    return CanClaimReward(questId, reward.RewardId) ? reward : null;
            return null;
        }

        // ── Per-cycle reward claiming (RECURRING quests only) ──────────────────
        // A completely separate mechanism from the whole-quest claim API just
        // above -- a quest uses one or the other, never both. Where the API
        // above gates on "is the quest currently COMPLETED", this one gates on
        // "how many times has it EVER completed" (see GetCyclesCompleted),
        // since a RECURRING quest's QuestState keeps moving on to the next
        // cycle's LOCKED/AVAILABLE/ACTIVE long after any one cycle's own
        // completion. Rewards[i] is cycle i's reward -- a capped day-1/day-2/
        // day-3 login-streak track needs nothing more than that list order,
        // no new schema concept for "which day".

        /// <summary>Number of times this quest has ever reached COMPLETED while RECURRING-scheduled. Always 0 for anything else (unscheduled, ONE_SHOT, or a RECURRING quest that hasn't completed a cycle yet).</summary>
        public int GetCyclesCompleted(string questId) =>
            _cyclesCompleted.TryGetValue(questId, out var count) ? count : 0;

        public bool IsCycleRewardClaimed(string questId, int cycleIndex) =>
            _claimedCycleRewards.TryGetValue(questId, out var claimed) && claimed.Contains(cycleIndex);

        /// <summary>
        /// True once cycle <paramref name="cycleIndex"/> has actually
        /// completed, the quest's <see cref="QuestDefinition.Rewards"/> list
        /// has an entry at that index, and it hasn't been claimed yet. Once
        /// true this never becomes false again except by claiming it --
        /// there's no time limit on claiming, so missing a cycle's window
        /// never costs you that cycle's reward.
        /// </summary>
        public bool CanClaimCycleReward(string questId, int cycleIndex)
        {
            if (cycleIndex < 0 || cycleIndex >= GetCyclesCompleted(questId)) return false;
            if (IsCycleRewardClaimed(questId, cycleIndex)) return false;

            var quest = _table.Find(questId);
            return quest != null && cycleIndex < quest.Rewards.Count;
        }

        /// <summary>
        /// Claims cycle <paramref name="cycleIndex"/>'s reward if
        /// <see cref="CanClaimCycleReward"/> allows it, firing
        /// <see cref="CycleRewardClaimed"/> and returning the claimed
        /// reward. Returns null with no exception otherwise, so a UI can
        /// wire a Claim button straight to this.
        /// </summary>
        public RewardDefinition? TryClaimCycleReward(string questId, int cycleIndex)
        {
            if (!CanClaimCycleReward(questId, cycleIndex)) return null;

            var quest = _table.Find(questId)!;
            var reward = quest.Rewards[cycleIndex];

            if (!_claimedCycleRewards.TryGetValue(questId, out var claimed))
            {
                claimed = new HashSet<int>();
                _claimedCycleRewards[questId] = claimed;
            }
            claimed.Add(cycleIndex);

            CycleRewardClaimed?.Invoke(this, new CycleRewardClaimedEventArgs(questId, cycleIndex, reward));
            return reward;
        }

        /// <summary>Every cycle index that's completed, has a defined reward, and isn't claimed yet -- what a missions-tab-style UI should currently show as claimable on this quest's track.</summary>
        public IEnumerable<int> GetClaimableCycleIndices(string questId)
        {
            var quest = _table.Find(questId);
            if (quest == null) yield break;

            var cap = Math.Min(GetCyclesCompleted(questId), quest.Rewards.Count);
            for (var i = 0; i < cap; i++)
                if (!IsCycleRewardClaimed(questId, i))
                    yield return i;
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

            foreach (var (questId, rewardIds) in _claimedRewards)
                foreach (var rewardId in rewardIds)
                    snapshot.RewardClaims.Add(new QuestRewardClaimRow { QuestId = questId, RewardId = rewardId });

            var scheduledQuestIds = new HashSet<string>(_cyclesCompleted.Keys);
            scheduledQuestIds.UnionWith(_lastGrantedCycle.Keys);
            foreach (var questId in scheduledQuestIds)
                snapshot.ScheduleStates.Add(new QuestScheduleStateRow
                {
                    QuestId = questId,
                    CyclesCompleted = GetCyclesCompleted(questId),
                    LastGrantedCycleIndex = GetLastGrantedCycle(questId),
                });

            foreach (var (questId, cycles) in _claimedCycleRewards)
                foreach (var cycleIndex in cycles)
                    snapshot.CycleRewardClaims.Add(new QuestCycleRewardClaimRow { QuestId = questId, CycleIndex = cycleIndex });

            return snapshot;
        }

        /// <summary>
        /// Restores exactly what the snapshot says, then runs the watcher
        /// once over any quest the snapshot doesn't mention at all — quests
        /// added by a game update since the save was made get a fair
        /// initial evaluation instead of silently staying LOCKED forever.
        ///
        /// Doesn't touch scheduling on its own: call <see cref="AdvanceTime"/>
        /// with the current <c>now</c> right after this, same as you would
        /// during normal play -- Questly never persists "now" itself (the
        /// host owns the clock), only the schedule bookkeeping (cycles
        /// completed, which cycle was last granted) that <see cref="AdvanceTime"/>
        /// needs to pick back up correctly once you do.
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

            foreach (var row in snapshot.RewardClaims)
            {
                if (!_claimedRewards.TryGetValue(row.QuestId, out var claimed))
                {
                    claimed = new HashSet<string>();
                    _claimedRewards[row.QuestId] = claimed;
                }
                claimed.Add(row.RewardId);
            }

            foreach (var row in snapshot.ScheduleStates)
            {
                _cyclesCompleted[row.QuestId] = row.CyclesCompleted;
                _lastGrantedCycle[row.QuestId] = row.LastGrantedCycleIndex;
            }

            foreach (var row in snapshot.CycleRewardClaims)
            {
                if (!_claimedCycleRewards.TryGetValue(row.QuestId, out var claimed))
                {
                    claimed = new HashSet<int>();
                    _claimedCycleRewards[row.QuestId] = claimed;
                }
                claimed.Add(row.CycleIndex);
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
            saveFile.Progress.RewardClaims = snapshot.RewardClaims;
            saveFile.Progress.ScheduleStates = snapshot.ScheduleStates;
            saveFile.Progress.CycleRewardClaims = snapshot.CycleRewardClaims;

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
                RewardClaims = saveFile.Progress.RewardClaims,
                ScheduleStates = saveFile.Progress.ScheduleStates,
                CycleRewardClaims = saveFile.Progress.CycleRewardClaims,
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
                {
                    if (quest.Schedule.Kind == ScheduleKind.RECURRING)
                        _cyclesCompleted[questId] = GetCyclesCompleted(questId) + 1;

                    QuestCompleted?.Invoke(this, new QuestCompletedEventArgs(questId, quest.Rewards));
                }
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
        /// call, not something this watcher ever does on its own.
        ///
        /// One disclosed exception: a scheduled quest (<see cref="ScheduleDefinition.Kind"/>
        /// isn't NONE) is skipped here entirely, in both directions --
        /// <see cref="AdvanceTime"/> is the only thing allowed to move it
        /// LOCKED/AVAILABLE/EXPIRED, driven by the clock rather than by
        /// prereq/objective events. Without this exception a scheduled quest
        /// with no *other* prereqs would be promoted to AVAILABLE the moment
        /// this watcher ran over it for any unrelated reason, regardless of
        /// whether its window was even open.
        /// </summary>
        private void RunWatcher(IEnumerable<string> candidateQuestIds)
        {
            foreach (var questId in candidateQuestIds)
            {
                if (GetQuestState(questId) != QuestState.LOCKED) continue;

                var quest = _table.Find(questId);
                if (quest == null) continue;

                if (quest.Schedule.Kind != ScheduleKind.NONE) continue;

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
