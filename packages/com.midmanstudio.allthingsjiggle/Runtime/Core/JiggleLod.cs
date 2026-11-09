// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleLod.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>How much of a chain is simulated. The levels are ordered: a higher one costs less.</summary>
    public enum JiggleLodLevel
    {
        /// <summary>Simulated, with its colliders.</summary>
        Full = 0,
        /// <summary>Simulated without testing colliders.</summary>
        NoCollision = 1,
        /// <summary>Not simulated. The bones follow their pose.</summary>
        Off = 2
    }

    /// <summary>
    /// The limits of a chain's level of detail. A distance of 0 turns that limit off. The limits are distances
    /// from the camera to the anchor of the chain, in meters.
    /// </summary>
    public struct JiggleLodSettings
    {
        /// <summary>Farther than this the chain stops testing colliders. 0 means never.</summary>
        public float CollisionDistance;
        /// <summary>Farther than this the chain is not simulated. 0 means never.</summary>
        public float CullDistance;
        /// <summary>
        /// How much closer than a limit the chain has to come before it goes back to the level it left, in meters,
        /// so a chain that sits on a limit does not flicker between two levels.
        /// </summary>
        public float Hysteresis;
        /// <summary>Whether a chain that the camera does not see is not simulated.</summary>
        public bool CullOffscreen;
        /// <summary>How long the camera must not have seen the chain before it is not simulated, in seconds.</summary>
        public float OffscreenDelay;

        /// <summary>No limits at all: the chain is always simulated in full.</summary>
        public static JiggleLodSettings Always()
        {
            return new JiggleLodSettings();
        }
    }

    /// <summary>What the level of detail remembers about one chain between frames.</summary>
    public struct JiggleLodTracker
    {
        /// <summary>The level the chain was given last.</summary>
        public JiggleLodLevel Level;
        /// <summary>How long the camera has not seen the chain, in seconds.</summary>
        public float OffscreenTime;
    }

    /// <summary>
    /// Decides the level of detail of a chain from its distance and whether the camera sees it. Pure functions, no
    /// engine, so the rules are tested outside Unity.
    /// </summary>
    public static class JiggleLod
    {
        /// <summary>
        /// Gives the level for this frame and remembers it in <paramref name="tracker"/>. A distance that is not a finite
        /// number keeps the level the chain had. <paramref name="visible"/> is whether any camera sees the chain, and
        /// <paramref name="dt"/> the time of the frame in seconds.
        /// </summary>
        public static JiggleLodLevel Decide(
            in JiggleLodSettings s, ref JiggleLodTracker tracker, float distance, bool visible, float dt)
        {
            float h = s.Hysteresis > 0.0f ? s.Hysteresis : 0.0f;
            JiggleLodLevel before = tracker.Level;
            JiggleLodLevel level = JiggleLodLevel.Full;

            if (JiggleMath.IsFinite(distance))
            {
                if (s.CullDistance > 0.0f)
                {
                    float limit = before == JiggleLodLevel.Off ? s.CullDistance - h : s.CullDistance;
                    if (distance >= limit)
                    {
                        level = JiggleLodLevel.Off;
                    }
                }
                if (level == JiggleLodLevel.Full && s.CollisionDistance > 0.0f)
                {
                    float limit = before != JiggleLodLevel.Full ? s.CollisionDistance - h : s.CollisionDistance;
                    if (distance >= limit)
                    {
                        level = JiggleLodLevel.NoCollision;
                    }
                }
            }
            else
            {
                level = before;
            }

            if (s.CullOffscreen && !visible)
            {
                float step = JiggleMath.IsFinite(dt) && dt > 0.0f ? dt : 0.0f;
                tracker.OffscreenTime += step;
                if (tracker.OffscreenTime >= s.OffscreenDelay)
                {
                    level = JiggleLodLevel.Off;
                }
            }
            else
            {
                tracker.OffscreenTime = 0.0f;
            }

            tracker.Level = level;
            return level;
        }

        /// <summary>
        /// Whether a sphere touches the inside of a view frustum given as planes. A plane is its normal in X, Y and Z,
        /// pointing into the frustum, and its distance in W, so the signed distance of a point is
        /// <c>dot(normal, point) + w</c>, which is how Unity gives them. Fewer than one plane means no frustum, which sees
        /// everything. A sphere that is not finite counts as seen.
        /// </summary>
        public static bool SphereInFrustum(JVec4[] planes, int count, JVec3 center, float radius)
        {
            if (planes == null || count <= 0 || !center.IsFinite() || !JiggleMath.IsFinite(radius))
            {
                return true;
            }
            if (count > planes.Length)
            {
                count = planes.Length;
            }
            float r = radius > 0.0f ? radius : 0.0f;
            for (int i = 0; i < count; i++)
            {
                JVec4 p = planes[i];
                float d = p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W;
                if (d < -r)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
