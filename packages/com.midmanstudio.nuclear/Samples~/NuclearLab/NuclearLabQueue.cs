// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabQueue.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// A first-in first-out list of actions. The panel adds an action when a
    /// button is clicked and the view runs the list from Update. IMGUI fails
    /// with a layout error when a click changes how many controls are drawn
    /// during the same pass, and running the change one frame later avoids that.
    /// Has no UnityEngine references. Not thread safe.
    /// </summary>
    public sealed class NuclearLabQueue
    {
        private readonly List<Action> pending = new List<Action>();

        /// <summary>Number of actions waiting.</summary>
        public int Count
        {
            get { return pending.Count; }
        }

        /// <summary>Adds an action to run on the next <see cref="RunAll"/>.</summary>
        public void Add(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }
            pending.Add(action);
        }

        /// <summary>
        /// Runs the actions that are waiting now, in the order they were added.
        /// Actions that those actions add wait for the next call. The list is
        /// emptied before the first action runs, so a throwing action does not run twice.
        /// </summary>
        public void RunAll()
        {
            if (pending.Count == 0)
            {
                return;
            }
            Action[] batch = pending.ToArray();
            pending.Clear();
            for (int i = 0; i < batch.Length; i++)
            {
                batch[i]();
            }
        }
    }
}
