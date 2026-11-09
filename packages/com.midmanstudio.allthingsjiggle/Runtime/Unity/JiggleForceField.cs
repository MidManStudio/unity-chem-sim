// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleForceField.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>The ways a <see cref="JiggleForceField"/> pushes.</summary>
    public enum JiggleFieldShape
    {
        /// <summary>Along the forward direction of the object, like wind. With a radius of 0 everywhere, otherwise inside the sphere.</summary>
        Directional = 0,
        /// <summary>Away from the center, or toward it with a negative strength.</summary>
        Radial = 1,
        /// <summary>Around the up axis of the object, which makes bones swirl.</summary>
        Vortex = 2
    }

    /// <summary>
    /// A force in the scene that pushes the bones of jiggle chains: wind, a fan, a blast, a whirl. It pushes all the time
    /// with <see cref="strength"/> as an acceleration while it is enabled, and <see cref="Fire"/> gives a single shove of
    /// <see cref="impulseStrength"/> as a change of velocity. How much a chain takes of either is set with its
    /// wind response and impulse response. A chain that sleeps wakes when a field reaches it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MidManStudio/AllThingsJiggle/Jiggle Force Field")]
    public sealed class JiggleForceField : MonoBehaviour
    {
        /// <summary>How the field pushes.</summary>
        public JiggleFieldShape shape = JiggleFieldShape.Directional;

        /// <summary>Acceleration in meters per second squared while enabled. Negative pulls a radial field in. 0 pushes nothing.</summary>
        public float strength = 5f;

        /// <summary>Reach in meters. A directional field with 0 reaches everywhere. Radial and vortex fields need a radius.</summary>
        [Min(0f)] public float radius = 0f;

        /// <summary>How the push fades to the edge: 0 does not fade, 1 is linear, higher stays strong near the center.</summary>
        [Range(0, 4)] public int falloff = 1;

        /// <summary>Gusts: how much the direction of a directional field wanders. 0 is steady.</summary>
        [Min(0f)] public float turbulence = 0f;

        /// <summary>Gusts: how fast they change, in cycles per second.</summary>
        [Min(0f)] public float frequency = 1f;

        /// <summary>Gusts: how fast they change from place to place, in cycles per meter. 0 gusts everywhere at once.</summary>
        [Min(0f)] public float gustScale = 0.5f;

        /// <summary>The change of velocity <see cref="Fire"/> gives, in meters per second. Negative pulls a radial field in.</summary>
        public float impulseStrength = 4f;

        private void OnEnable()
        {
            JiggleManager m = JiggleManager.Instance;
            if (m != null)
            {
                m.Register(this);
            }
        }

        private void OnDisable()
        {
            JiggleManager m = JiggleManager.TryGet();
            if (m != null)
            {
                m.Unregister(this);
            }
        }

        private void OnValidate()
        {
            falloff = Mathf.Clamp(falloff, 0, 4);
        }

        private JiggleField Make(uint mode, float amount)
        {
            Transform t = transform;
            Vector3 p = t.position;
            Vector3 d = shape == JiggleFieldShape.Vortex ? t.up : t.forward;
            uint kind = shape == JiggleFieldShape.Radial ? JiggleConst.FieldRadial
                : (shape == JiggleFieldShape.Vortex ? JiggleConst.FieldVortex : JiggleConst.FieldDirectional);
            return JiggleShapes.Field(
                kind, mode, new JVec3(p.x, p.y, p.z), new JVec3(d.x, d.y, d.z), gustScale, amount, radius, (uint)falloff,
                turbulence, frequency);
        }

        /// <summary>The field as the solver takes it for this frame: an acceleration with the position and direction of the transform.</summary>
        public JiggleField Build()
        {
            return Make(JiggleConst.FieldAcceleration, strength);
        }

        /// <summary>
        /// Gives every chain in reach a single shove of <see cref="impulseStrength"/> on the next frame, shaped like this
        /// field and from where it is now. It works whether the component is enabled or not. Returns false when there
        /// is no manager, which is outside play mode.
        /// </summary>
        public bool Fire()
        {
            JiggleManager m = JiggleManager.Instance;
            if (m == null)
            {
                return false;
            }
            m.Enqueue(Make(JiggleConst.FieldImpulse, impulseStrength));
            return true;
        }
    }
}
