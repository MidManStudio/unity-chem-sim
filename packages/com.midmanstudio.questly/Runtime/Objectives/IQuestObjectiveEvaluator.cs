namespace MidManStudio.Questly.Objectives
{
    /// <summary>
    /// Host-registered, polled evaluator for <c>ObjectiveKind.EXTERNAL</c>
    /// objectives. Questly ships zero built-in evaluators — inventory
    /// checks, spatial/proximity checks, dialogue-state checks, or anything
    /// else domain-specific stays entirely on the host side of this
    /// interface, keyed by whatever <c>evaluator_key</c> string the quest
    /// database uses.
    /// </summary>
    public interface IQuestObjectiveEvaluator
    {
        /// <summary>
        /// <paramref name="paramsJson"/> is the objective's raw <c>params</c>
        /// object from the schema (e.g. <c>{ "region_id": "old_ruins" }</c>)
        /// — entirely opaque to Questly, meaningful only to this evaluator.
        /// </summary>
        bool Evaluate(string questId, string objectiveId, string paramsJson);
    }

    /// <summary>Same idea as <see cref="IQuestObjectiveEvaluator"/>, for <c>PrereqKind.EXTERNAL</c>.</summary>
    public interface IQuestPrereqEvaluator
    {
        bool Evaluate(string paramsJson);
    }
}
