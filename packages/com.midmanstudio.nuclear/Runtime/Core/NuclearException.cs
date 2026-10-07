// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearException.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear
{
    /// <summary>Status codes the native library returns. Zero is success.</summary>
    public enum NuclearStatus
    {
        /// <summary>Success.</summary>
        Ok = 0,
        /// <summary>A required pointer argument was null.</summary>
        NullPointer = -1,
        /// <summary>An argument was out of range or not usable.</summary>
        Invalid = -2,
        /// <summary>The nuclide, or the data asked for it, is not in the tables.</summary>
        NotInTable = -3,
        /// <summary>A count would pass the largest 64-bit value.</summary>
        Overflow = -4,
        /// <summary>The pile holds less than the operation needs.</summary>
        NotEnough = -5,
        /// <summary>No fusion channel runs for this pair at this temperature.</summary>
        NoReaction = -6,
        /// <summary>The nucleus has too few protons for the fission model.</summary>
        NotFissionable = -7,
        /// <summary>A decay lineage is too large to solve.</summary>
        TooManyStates = -8,
        /// <summary>The native library panicked.</summary>
        Panic = -9,
    }

    /// <summary>A native call failed. <see cref="Status"/> says how and the message says why.</summary>
    public sealed class NuclearException : Exception
    {
        /// <summary>The status code the native call returned.</summary>
        public NuclearStatus Status { get; }

        /// <summary>Creates the exception.</summary>
        public NuclearException(NuclearStatus status, string message) : base(message)
        {
            Status = status;
        }
    }
}
