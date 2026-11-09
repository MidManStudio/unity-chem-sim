// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleColliderSet.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Shapes in world space made the way the solver wants them, so a host does not repeat the layout rules:
    /// which field holds the point of a plane, whether an axis must be a unit vector, what a box rounds by.
    /// </summary>
    public static class JiggleShapes
    {
        /// <summary>A sphere.</summary>
        public static JiggleCollider Sphere(JVec3 center, float radius)
        {
            JiggleCollider c = new JiggleCollider();
            c.Kind = JiggleConst.ColliderSphere;
            c.Radius = Positive(radius);
            Put(ref c.Center, center);
            return c;
        }

        /// <summary>
        /// A capsule: the segment of length 2 * halfHeight centered on <paramref name="center"/> along
        /// <paramref name="axis"/>, thickened by <paramref name="radius"/>. The axis is made a unit vector.
        /// </summary>
        public static JiggleCollider Capsule(JVec3 center, JVec3 axis, float halfHeight, float radius)
        {
            JiggleCollider c = new JiggleCollider();
            c.Kind = JiggleConst.ColliderCapsule;
            c.Radius = Positive(radius);
            c.HalfHeight = Positive(halfHeight);
            Put(ref c.Center, center);
            Put(ref c.Axis, axis.NormalizeSafe(JVec3.Up));
            return c;
        }

        /// <summary>
        /// A box with the given half extents turned by <paramref name="rotation"/>, optionally rounded by
        /// <paramref name="rounding"/>, which grows it by that much on every side.
        /// </summary>
        public static JiggleCollider Box(JVec3 center, JVec3 halfExtents, JQuat rotation, float rounding)
        {
            JiggleCollider c = new JiggleCollider();
            c.Kind = JiggleConst.ColliderBox;
            c.Radius = Positive(rounding);
            Put(ref c.Center, center);
            Put(ref c.Extents, new JVec3(Positive(halfExtents.X), Positive(halfExtents.Y), Positive(halfExtents.Z)));
            c.Rot = rotation.To4();
            return c;
        }

        /// <summary>A one sided plane through <paramref name="point"/>. Particles are kept on the side the normal points to.</summary>
        public static JiggleCollider Plane(JVec3 point, JVec3 normal)
        {
            JiggleCollider c = new JiggleCollider();
            c.Kind = JiggleConst.ColliderPlane;
            Put(ref c.Center, point);
            Put(ref c.Axis, normal.NormalizeSafe(JVec3.Up));
            return c;
        }

        /// <summary>A sphere that keeps particles inside it.</summary>
        public static JiggleCollider InsideSphere(JVec3 center, float radius)
        {
            JiggleCollider c = new JiggleCollider();
            c.Kind = JiggleConst.ColliderInsideSphere;
            c.Radius = Positive(radius);
            Put(ref c.Center, center);
            return c;
        }

        private static float Positive(float v)
        {
            return JiggleMath.IsFinite(v) && v > 0.0f ? v : 0.0f;
        }

        private static void Put(ref JVec4 slot, JVec3 v)
        {
            v.Write4(ref slot);
            slot.W = 0.0f;
        }

        /// <summary>
        /// A field with every number set: <paramref name="direction"/> is the way a directional field blows or the axis
        /// of a vortex, and <paramref name="gustScale"/> is how fast the gusts change from place to place, in cycles per
        /// meter.
        /// </summary>
        public static JiggleField Field(
            uint kind, uint mode, JVec3 center, JVec3 direction, float gustScale, float strength, float radius, uint power,
            float turbulence, float frequency)
        {
            JiggleField f = new JiggleField();
            f.Kind = kind;
            f.Mode = mode;
            f.Power = power;
            Put(ref f.Center, center);
            JVec3 d = direction.NormalizeSafe(JVec3.Up);
            f.Dir = new JVec4(d.X, d.Y, d.Z, JiggleMath.IsFinite(gustScale) ? gustScale : 0.0f);
            f.Radius = Positive(radius);
            f.Strength = JiggleMath.IsFinite(strength) ? strength : 0.0f;
            f.Turbulence = Positive(turbulence);
            f.Frequency = JiggleMath.IsFinite(frequency) ? frequency : 0.0f;
            return f;
        }

        /// <summary>
        /// A velocity change given once to everything within <paramref name="radius"/> of <paramref name="point"/>, away
        /// from it, which fades by <paramref name="power"/>. A negative velocity pulls toward the point instead.
        /// </summary>
        public static JiggleField Burst(JVec3 point, float radius, float velocityChange, uint power)
        {
            return Field(
                JiggleConst.FieldRadial, JiggleConst.FieldImpulse, point, JVec3.Up, 0.0f, velocityChange, radius, power,
                0.0f, 0.0f);
        }
    }

    /// <summary>
    /// The colliders of a scene for one frame and the lists that say which of them each rig tests. The scene adds its
    /// shapes in world space with <see cref="Add"/>, each with a layer, and <see cref="Write"/> puts them into a
    /// <see cref="JiggleHost"/>. A rig only gets the colliders that can reach it, which keeps the solver from testing
    /// every particle against every collider, and only those on layers the rig does not ignore.
    /// Layers follow one rule: a set bit means no collision. It has no Unity references.
    /// </summary>
    public sealed unsafe class JiggleColliderSet
    {
        private JiggleCollider[] shapes = new JiggleCollider[16];
        private uint[] layers = new uint[16];
        private bool[] moved = new bool[16];
        private uint[] list = new uint[16];
        private int count;

        /// <summary>
        /// How far past its reach a collider still counts as near a rig, in meters. It covers what the extent of a rig
        /// does not: bones that stretch, and the move of the chain since the anchor was read.
        /// </summary>
        public float Margin = 0.25f;

        /// <summary>Number of shapes added since the last <see cref="Clear"/>.</summary>
        public int Count
        {
            get { return count; }
        }

        /// <summary>Forgets every shape. Call it at the start of a frame, then add the shapes of the scene.</summary>
        public void Clear()
        {
            count = 0;
        }

        /// <summary>
        /// Adds a shape in world space and returns its index. <paramref name="layer"/> is 0 to 31, anything else counts as
        /// layer 0. <paramref name="hasMoved"/> says the shape moved since the last frame, which wakes sleeping rigs it is
        /// near: a sleeping rig does not notice a collider that moves up to it.
        /// </summary>
        public int Add(in JiggleCollider shape, int layer, bool hasMoved)
        {
            if (count == shapes.Length)
            {
                int n = count * 2;
                Array.Resize(ref shapes, n);
                Array.Resize(ref layers, n);
                Array.Resize(ref moved, n);
            }
            shapes[count] = shape;
            layers[count] = layer >= 0 && layer < 32 ? (1u << layer) : 1u;
            moved[count] = hasMoved;
            count++;
            return count - 1;
        }

        /// <summary>
        /// Whether the shape can reach a particle that is within <paramref name="reach"/> of <paramref name="anchor"/>. A
        /// plane and a sphere that holds particles inside have no edge, so they always can.
        /// </summary>
        public static bool Near(in JiggleCollider c, JVec3 anchor, float reach)
        {
            float own;
            switch (c.Kind)
            {
                case JiggleConst.ColliderSphere:
                    own = c.Radius;
                    break;
                case JiggleConst.ColliderCapsule:
                    own = c.HalfHeight + c.Radius;
                    break;
                case JiggleConst.ColliderBox:
                    own = JVec3.From4(c.Extents).Length() + c.Radius;
                    break;
                default:
                    return true;
            }
            JVec3 center = JVec3.From4(c.Center);
            if (!center.IsFinite() || !JiggleMath.IsFinite(own))
            {
                return true;
            }
            return (anchor - center).Length() <= own + reach;
        }

        /// <summary>
        /// Writes the shapes into the host and gives every rig its list. The old shapes and lists are replaced, so the
        /// call must come before the frame runs and after the last chain was added.
        /// <paramref name="anchors"/> is where the anchor of each rig is now, or null to use the one the host has, which
        /// is from the last frame. <paramref name="ignore"/> is the layers each rig does not collide with, a set bit
        /// meaning no collision, or null for none. A rig that must test nothing has all bits set.
        /// A sleeping rig wakes when a shape on its list has moved. Returns the number of indices written to the lists.
        /// </summary>
        public int Write(JiggleHost host, JVec3[] anchors, uint[] ignore)
        {
            if (host == null)
            {
                throw new ArgumentNullException("host");
            }
            host.ClearColliders();
            int rigs = host.RigCount;
            if (count == 0)
            {
                return 0;
            }
            for (int i = 0; i < count; i++)
            {
                host.AddCollider(shapes[i]);
            }
            if (list.Length < count)
            {
                list = new uint[shapes.Length];
            }
            int written = 0;
            for (int r = 0; r < rigs; r++)
            {
                uint mask = ignore != null && r < ignore.Length ? ignore[r] : 0u;
                if (mask == uint.MaxValue)
                {
                    continue;
                }
                JiggleRig rig = host.Rigs[r];
                float radius = ParticleRadius(host, rig);
                if (radius < 0.0f)
                {
                    continue;
                }
                JVec3 anchor = anchors != null && r < anchors.Length ? anchors[r] : rig.AnchorWorld;
                float reach = rig.Extent + radius + (Margin > 0.0f ? Margin : 0.0f);
                bool asleep = (rig.Flags & JiggleConst.RigAsleep) != 0;
                bool wake = false;
                int n = 0;
                for (int i = 0; i < count; i++)
                {
                    if ((layers[i] & mask) != 0u || !Near(shapes[i], anchor, reach))
                    {
                        continue;
                    }
                    list[n++] = (uint)i;
                    wake |= moved[i];
                }
                if (n > 0)
                {
                    fixed (uint* p = list)
                    {
                        host.SetColliderList(r, p, n);
                    }
                    written += n;
                }
                if (asleep && wake)
                {
                    host.Wake(r);
                }
            }
            return written;
        }

        // The largest collision radius among the particles that collide, which grows the reach of the rig, or -1 when no
        // particle collides, so the rig needs no list and no wake.
        private static float ParticleRadius(JiggleHost host, in JiggleRig rig)
        {
            float best = -1.0f;
            int offset = (int)rig.ParticleOffset;
            int n = (int)rig.ParticleCount;
            for (int i = 0; i < n; i++)
            {
                JiggleParticleDef* d = host.Defs + offset + i;
                if ((d->Flags & JiggleConst.FlagCollide) != 0 && d->CollisionRadius > 0.0f && d->CollisionRadius > best)
                {
                    best = d->CollisionRadius;
                }
            }
            return best;
        }
    }
}
