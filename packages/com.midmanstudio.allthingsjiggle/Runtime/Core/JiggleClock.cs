// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleClock.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>The fixed step accumulator, shared by <see cref="JiggleWorld"/> and <see cref="JiggleHost"/>.</summary>
    public static class JiggleClock
    {
        /// <summary>
        /// Adds frameDt to the accumulator and takes the whole fixed steps out of it. A frame time that is not finite
        /// or is negative counts as 0. A small tolerance keeps a frame time that is an exact multiple of the fixed
        /// step from losing a step to rounding. When more than maxSteps fit, maxSteps run and the rest is dropped.
        /// alpha is how far the time left over reaches into the next step, 0 to 1.
        /// </summary>
        public static void Advance(
            ref float accumulator, float frameDt, float fixedDt, uint maxSteps, out uint steps, out float alpha)
        {
            if (JiggleMath.IsFinite(frameDt) && frameDt > 0.0f)
            {
                accumulator += frameDt;
            }
            uint n = FloatToUint(accumulator / fixedDt + 1e-4f);
            if (n > maxSteps)
            {
                n = maxSteps;
                accumulator = 0.0f;
            }
            else
            {
                float rest = accumulator - (float)n * fixedDt;
                accumulator = rest > 0.0f ? rest : 0.0f;
            }
            steps = n;
            float a = accumulator / fixedDt;
            alpha = a > 1.0f ? 1.0f : a;
        }

        /// <summary>A float to uint conversion that follows Rust's `as u32`: NaN and negative values give 0, large ones stick.</summary>
        public static uint FloatToUint(float v)
        {
            if (JiggleMath.IsNaN(v) || v <= 0.0f)
            {
                return 0u;
            }
            if (v >= 4294967296.0f)
            {
                return uint.MaxValue;
            }
            return (uint)v;
        }
    }
}
