// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleColliderShape.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>The shapes a <see cref="JiggleColliderShape"/> can have.</summary>
    public enum JiggleShapeKind
    {
        /// <summary>A ball.</summary>
        Sphere = 0,
        /// <summary>A capsule along one local axis.</summary>
        Capsule = 1,
        /// <summary>A box, optionally with rounded edges.</summary>
        Box = 2,
        /// <summary>A one sided plane, solid on the side its up direction points away from.</summary>
        Plane = 3,
        /// <summary>A ball that holds the bones inside it, like a pouch.</summary>
        InsideSphere = 4
    }

    /// <summary>The local axis a capsule lies along.</summary>
    public enum JiggleShapeAxis
    {
        /// <summary>The local X axis.</summary>
        X = 0,
        /// <summary>The local Y axis.</summary>
        Y = 1,
        /// <summary>The local Z axis.</summary>
        Z = 2
    }

    /// <summary>
    /// A shape the bones of jiggle chains collide with. It is not a Unity collider: it exists only for the jiggle solver, so
    /// it costs nothing in the physics engine and a chain can collide with things physics does not know, like a body part
    /// that is only a transform. The layer of the object decides which chains collide with it: a chain ignores the layers
    /// set in its <see cref="JiggleChain.ignoreLayers"/>, so a set bit means no collision. A chain only collides with shapes
    /// when <see cref="JiggleChainSettings.collide"/> is on.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MidManStudio/AllThingsJiggle/Jiggle Collider")]
    public sealed class JiggleColliderShape : MonoBehaviour
    {
        /// <summary>The shape.</summary>
        public JiggleShapeKind shape = JiggleShapeKind.Sphere;

        /// <summary>Where the shape sits, in the local space of this transform.</summary>
        public Vector3 center = Vector3.zero;

        /// <summary>Radius of a sphere, a capsule, an inside sphere, and the rounding of a box, in local units.</summary>
        [Min(0f)] public float radius = 0.1f;

        /// <summary>Length of a capsule from end to end, caps included, in local units.</summary>
        [Min(0f)] public float height = 0.5f;

        /// <summary>The axis a capsule lies along.</summary>
        public JiggleShapeAxis axis = JiggleShapeAxis.Y;

        /// <summary>Size of a box in local units, edge to edge.</summary>
        public Vector3 size = new Vector3(0.2f, 0.2f, 0.2f);

        /// <summary>
        /// Check it for a shape that never moves, turns or changes after it is enabled. The manager then builds the shape
        /// once, and a sleeping chain does not have to be watched for it. A shape that changes with this on is not noticed.
        /// </summary>
        public bool staticShape = false;

        private bool built;
        private JiggleCollider cache;
        private JiggleCollider last;
        private bool hasLast;

        private void OnEnable()
        {
            built = false;
            hasLast = false;
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
            built = false;
            if (radius < 0f)
            {
                radius = 0f;
            }
            if (height < 0f)
            {
                height = 0f;
            }
        }

        /// <summary>
        /// The shape in world space as the solver takes it, from the transform of this frame. Returns whether it moved,
        /// turned or changed since the last call. A shape marked <see cref="staticShape"/> is built once.
        /// </summary>
        public JiggleCollider Build(out bool moved)
        {
            if (staticShape && built)
            {
                moved = false;
                return cache;
            }
            JiggleCollider c = Make();
            moved = !hasLast || Differs(c, last);
            last = c;
            hasLast = true;
            cache = c;
            built = true;
            return c;
        }

        /// <summary>
        /// The shape in world space as it is now, without taking part in the detection of movement. For tools and gizmos,
        /// which must not change what the manager sees as moved.
        /// </summary>
        public JiggleCollider Peek()
        {
            return Make();
        }

        private JiggleCollider Make()
        {
            Transform t = transform;
            Vector3 s = t.lossyScale;
            float sx = Mathf.Abs(s.x);
            float sy = Mathf.Abs(s.y);
            float sz = Mathf.Abs(s.z);
            float biggest = Mathf.Max(sx, Mathf.Max(sy, sz));
            Vector3 w = t.TransformPoint(center);
            JVec3 world = new JVec3(w.x, w.y, w.z);
            switch (shape)
            {
                case JiggleShapeKind.Capsule:
                {
                    Vector3 dir = axis == JiggleShapeAxis.X ? t.right : (axis == JiggleShapeAxis.Y ? t.up : t.forward);
                    float along = axis == JiggleShapeAxis.X ? sx : (axis == JiggleShapeAxis.Y ? sy : sz);
                    float across = axis == JiggleShapeAxis.X ? Mathf.Max(sy, sz) : (axis == JiggleShapeAxis.Y ? Mathf.Max(sx, sz) : Mathf.Max(sx, sy));
                    float r = radius * across;
                    float half = height * along * 0.5f - r;
                    return JiggleShapes.Capsule(world, new JVec3(dir.x, dir.y, dir.z), half > 0f ? half : 0f, r);
                }
                case JiggleShapeKind.Box:
                {
                    Quaternion q = t.rotation;
                    JVec3 half = new JVec3(size.x * sx * 0.5f, size.y * sy * 0.5f, size.z * sz * 0.5f);
                    return JiggleShapes.Box(world, half, new JQuat(q.x, q.y, q.z, q.w), radius * biggest);
                }
                case JiggleShapeKind.Plane:
                {
                    Vector3 up = t.up;
                    return JiggleShapes.Plane(world, new JVec3(up.x, up.y, up.z));
                }
                case JiggleShapeKind.InsideSphere:
                    return JiggleShapes.InsideSphere(world, radius * biggest);
                default:
                    return JiggleShapes.Sphere(world, radius * biggest);
            }
        }

        // Whether a shape differs from the one of the last frame by more than float noise.
        private static bool Differs(in JiggleCollider a, in JiggleCollider b)
        {
            const float eps = 1e-6f;
            return a.Kind != b.Kind
                || System.Math.Abs(a.Radius - b.Radius) > eps
                || System.Math.Abs(a.HalfHeight - b.HalfHeight) > eps
                || Far(a.Center, b.Center, eps)
                || Far(a.Axis, b.Axis, eps)
                || Far(a.Extents, b.Extents, eps)
                || Far(a.Rot, b.Rot, eps);
        }

        private static bool Far(in JVec4 a, in JVec4 b, float eps)
        {
            return System.Math.Abs(a.X - b.X) > eps || System.Math.Abs(a.Y - b.Y) > eps
                || System.Math.Abs(a.Z - b.Z) > eps || System.Math.Abs(a.W - b.W) > eps;
        }
    }
}
