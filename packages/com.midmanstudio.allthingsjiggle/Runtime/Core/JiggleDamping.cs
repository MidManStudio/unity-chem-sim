// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleDamping.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Converts a damping given per second into the share lost per fixed step, and back. The solver's drag values are per
    /// step, so the same number looks different at 30 and at 60 steps per second. A damping per second does not.
    /// It has no Unity references.
    /// </summary>
    public static class JiggleDamping
    {
        /// <summary>The step rate the package assumes when none is given.</summary>
        public const float DefaultHz = 60.0f;

        /// <summary>A step rate that is not a finite number of at least 1 counts as <see cref="DefaultHz"/>.</summary>
        public static float CleanHz(float hz)
        {
            return (JiggleMath.IsFinite(hz) && hz >= 1.0f) ? hz : DefaultHz;
        }

        private static float Clamp01(float v)
        {
            if (!JiggleMath.IsFinite(v) || v < 0.0f)
            {
                return 0.0f;
            }
            return v > 1.0f ? 1.0f : v;
        }

        /// <summary>
        /// The share of the motion lost in one fixed step when <paramref name="perSecond"/> of it is lost in one second:
        /// <c>1 - (1 - perSecond) ^ (1 / hz)</c>. 0 loses nothing and 1 loses everything. A step rate below 1 or that is not
        /// finite counts as 60.
        /// </summary>
        public static float PerStep(float perSecond, float hz)
        {
            double keep = 1.0 - (double)Clamp01(perSecond);
            double step = 1.0 - Math.Pow(keep, 1.0 / (double)CleanHz(hz));
            return Clamp01((float)step);
        }

        /// <summary>The inverse of <see cref="PerStep"/>: the share lost in one second when <paramref name="perStep"/> is lost each step.</summary>
        public static float PerSecond(float perStep, float hz)
        {
            double keep = 1.0 - (double)Clamp01(perStep);
            double second = 1.0 - Math.Pow(keep, (double)CleanHz(hz));
            return Clamp01((float)second);
        }
    }
}
