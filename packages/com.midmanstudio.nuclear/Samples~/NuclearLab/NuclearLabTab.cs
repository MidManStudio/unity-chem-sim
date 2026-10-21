// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabTab.cs"
// ============================================================================
using System;
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// The base of the four tabs. A tab draws cached text and rows only. Every
    /// click goes through <see cref="Do"/>, which queues the work for the next
    /// Update and shows any failure as a red line instead of a console error.
    /// </summary>
    internal abstract class NuclearLabTab : IDisposable
    {
        private readonly NuclearLabQueue queue;
        private string error = string.Empty;

        protected NuclearLabTab(NuclearLabQueue queue)
        {
            this.queue = queue;
        }

        /// <summary>The text on the tab button.</summary>
        public abstract string Title { get; }

        /// <summary>Draws the tab inside the panel's scroll view.</summary>
        public abstract void Draw(NuclearLabStyles styles);

        /// <summary>
        /// Called once a frame from Update with the seconds the frame took, for a tab that animates. A tab
        /// changes what it shows here and never inside <see cref="Draw"/>, so that every pass of one OnGUI
        /// frame sees the same state.
        /// </summary>
        public virtual void Tick(float seconds)
        {
        }

        /// <summary>Frees native handles and textures the tab owns.</summary>
        public virtual void Dispose()
        {
        }

        /// <summary>
        /// Runs <paramref name="action"/> from the next Update. A failure the library or the
        /// input parsing reports becomes the red line of this tab, and a success clears it.
        /// </summary>
        protected void Do(Action action)
        {
            queue.Add(delegate { Attempt(action); });
        }

        /// <summary>Runs <paramref name="action"/> now. Only for the tab's own start-up.</summary>
        protected void Attempt(Action action)
        {
            try
            {
                error = string.Empty;
                action();
            }
            catch (NuclearException e)
            {
                error = e.Message;
            }
            catch (FormatException e)
            {
                error = e.Message;
            }
            catch (ArgumentException e)
            {
                error = e.Message;
            }
            catch (InvalidOperationException e)
            {
                error = e.Message;
            }
        }

        /// <summary>Draws the red line when the last action failed.</summary>
        protected void ShowError(NuclearLabStyles styles)
        {
            if (error.Length > 0)
            {
                GUILayout.Label(error, styles.Error);
            }
        }

        /// <summary>A row of fixed-width cells.</summary>
        protected static void Cells(GUIStyle style, float[] widths, params string[] texts)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < texts.Length; i++)
            {
                GUILayout.Label(texts[i], style, GUILayout.Width(widths[i]));
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>A label followed by a text field, on the current horizontal line.</summary>
        protected static string Field(string label, string value, float labelWidth, float fieldWidth)
        {
            GUILayout.Label(label, GUILayout.Width(labelWidth));
            return GUILayout.TextField(value, GUILayout.Width(fieldWidth));
        }

        /// <summary>A grid of preset buttons, <paramref name="perRow"/> to a line. Returns the index pressed, or -1.</summary>
        protected static int PresetButtons(string[] labels, int perRow, float width)
        {
            int pressed = -1;
            for (int i = 0; i < labels.Length; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + perRow && j < labels.Length; j++)
                {
                    if (GUILayout.Button(labels[j], GUILayout.Width(width)))
                    {
                        pressed = j;
                    }
                }
                GUILayout.EndHorizontal();
            }
            return pressed;
        }
    }
}
