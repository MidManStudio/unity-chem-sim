// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleFieldQueue.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The fields of a scene for one frame, and the one shot impulses waiting to be given. The scene adds its standing
    /// fields with <see cref="Add"/> and the impulses with <see cref="Enqueue"/>, <see cref="Write"/> puts both into a
    /// <see cref="JiggleHost"/> before the frame, and <see cref="Commit"/> after it removes the impulses that were given.
    /// An impulse field acts on the first step of a frame only, so a frame that ran no step did not give it: the
    /// queue keeps it until a frame that did. It has no Unity references.
    /// </summary>
    public sealed class JiggleFieldQueue
    {
        private JiggleField[] fields = new JiggleField[8];
        private int fieldCount;
        private JiggleField[] pending = new JiggleField[8];
        private int pendingCount;

        /// <summary>Most impulses waiting at once. Past it the oldest is dropped, so a script that hits every frame while the
        /// game is paused does not grow the queue without end.</summary>
        public const int MaxPending = 256;

        /// <summary>Number of standing fields added since the last <see cref="Clear"/>.</summary>
        public int FieldCount
        {
            get { return fieldCount; }
        }

        /// <summary>Number of impulses waiting.</summary>
        public int PendingCount
        {
            get { return pendingCount; }
        }

        /// <summary>Forgets the standing fields. Call it at the start of a frame, then add the fields of the scene. The impulses stay.</summary>
        public void Clear()
        {
            fieldCount = 0;
        }

        /// <summary>Adds a standing field for this frame.</summary>
        public void Add(in JiggleField f)
        {
            if (fieldCount == fields.Length)
            {
                Array.Resize(ref fields, fieldCount * 2);
            }
            fields[fieldCount++] = f;
        }

        /// <summary>
        /// Queues a field to act once, as a velocity change on the first step of the next frame that runs a step. Its mode
        /// is set to <see cref="JiggleConst.FieldImpulse"/>.
        /// </summary>
        public void Enqueue(JiggleField f)
        {
            f.Mode = JiggleConst.FieldImpulse;
            if (pendingCount == MaxPending)
            {
                Array.Copy(pending, 1, pending, 0, pendingCount - 1);
                pendingCount--;
            }
            if (pendingCount == pending.Length)
            {
                Array.Resize(ref pending, pendingCount * 2);
            }
            pending[pendingCount++] = f;
        }

        /// <summary>Drops every waiting impulse.</summary>
        public void DropPending()
        {
            pendingCount = 0;
        }

        /// <summary>Replaces the fields of the host with the standing fields and then the waiting impulses.</summary>
        public void Write(JiggleHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException("host");
            }
            host.ClearFields();
            for (int i = 0; i < fieldCount; i++)
            {
                host.AddField(fields[i]);
            }
            for (int i = 0; i < pendingCount; i++)
            {
                host.AddField(pending[i]);
            }
        }

        /// <summary>
        /// Call it after the frame ran, with the number of fixed steps it ran. The impulses were given if there was at least
        /// one, and are removed. With no step they stay for the next frame.
        /// </summary>
        public void Commit(uint steps)
        {
            if (steps > 0)
            {
                pendingCount = 0;
            }
        }
    }
}
