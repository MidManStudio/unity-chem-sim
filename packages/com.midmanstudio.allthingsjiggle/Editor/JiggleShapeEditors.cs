// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/JiggleShapeEditors.cs"
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>The inspector of a collider shape: the settings and what the layer of the object does.</summary>
    [CustomEditor(typeof(JiggleColliderShape))]
    public sealed class JiggleColliderShapeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            JiggleColliderShape s = (JiggleColliderShape)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Layer " + s.gameObject.layer + ". A chain ignores this shape when bit " + s.gameObject.layer
                + " is set in its Ignore Layers. A set bit means no collision. A chain only collides while Collide is on in its settings.",
                MessageType.None);
            if (s.shape == JiggleShapeKind.Plane)
            {
                EditorGUILayout.HelpBox(
                    "A plane is solid on the side its up direction points away from. Bones are kept on the up side.",
                    MessageType.None);
            }
        }
    }

    /// <summary>The inspector of a force field: the settings, what the direction is, and a button to fire an impulse in play mode.</summary>
    [CustomEditor(typeof(JiggleForceField))]
    public sealed class JiggleForceFieldEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            JiggleForceField f = (JiggleForceField)target;
            EditorGUILayout.Space();
            string direction = f.shape == JiggleFieldShape.Directional
                ? "It blows along the forward (blue) axis of this object."
                : (f.shape == JiggleFieldShape.Vortex
                    ? "It swirls around the up (green) axis of this object."
                    : "It pushes away from the center of this object, or toward it with a negative strength.");
            EditorGUILayout.HelpBox(direction, MessageType.None);
            if (f.shape != JiggleFieldShape.Directional && f.radius <= 0f)
            {
                EditorGUILayout.HelpBox("A radial or vortex field with no radius pushes nothing. Give it a radius.", MessageType.Warning);
            }
            if (Application.isPlaying && GUILayout.Button("Fire the impulse once"))
            {
                f.Fire();
            }
        }
    }

    /// <summary>Draws the shape of a selected collider and the reach and direction of a selected force field.</summary>
    internal static class JiggleShapeGizmos
    {
        private static Vector3 V(JVec3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        private static JVec3 At(JVec4 v)
        {
            return JVec3.From4(v);
        }

        private static void Ring(JVec3 center, JVec3 u, JVec3 w, float r)
        {
            const int segments = 24;
            JVec3 last = center + u * r;
            for (int i = 1; i <= segments; i++)
            {
                float a = (float)i / (float)segments * 6.2831853f;
                JVec3 next = center + u * (r * (float)System.Math.Cos(a)) + w * (r * (float)System.Math.Sin(a));
                Gizmos.DrawLine(V(last), V(next));
                last = next;
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawCollider(JiggleColliderShape shape, GizmoType type)
        {
            JiggleCollider c = shape.Peek();
            Shape(c, c.Kind == JiggleConst.ColliderInsideSphere ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.2f, 0.9f, 0.4f));
        }

        /// <summary>Draws a solver shape in world space in the given color. The body colliders use it for their preview.</summary>
        internal static void Shape(in JiggleCollider c, Color color)
        {
            JVec3 center = At(c.Center);
            Gizmos.color = color;
            switch (c.Kind)
            {
                case JiggleConst.ColliderCapsule:
                {
                    JVec3 axis = At(c.Axis);
                    JVec3 u = JiggleMath.Perpendicular(axis).NormalizeSafe(JVec3.Up);
                    JVec3 w = JVec3.Cross(axis, u);
                    JVec3 a = center + axis * c.HalfHeight;
                    JVec3 b = center - axis * c.HalfHeight;
                    Gizmos.DrawWireSphere(V(a), c.Radius);
                    Gizmos.DrawWireSphere(V(b), c.Radius);
                    Gizmos.DrawLine(V(a + u * c.Radius), V(b + u * c.Radius));
                    Gizmos.DrawLine(V(a - u * c.Radius), V(b - u * c.Radius));
                    Gizmos.DrawLine(V(a + w * c.Radius), V(b + w * c.Radius));
                    Gizmos.DrawLine(V(a - w * c.Radius), V(b - w * c.Radius));
                    break;
                }
                case JiggleConst.ColliderBox:
                {
                    JQuat q = JQuat.From4(c.Rot);
                    JVec3 h = At(c.Extents) + new JVec3(c.Radius, c.Radius, c.Radius);
                    JVec3[] corner = new JVec3[8];
                    for (int i = 0; i < 8; i++)
                    {
                        JVec3 local = new JVec3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
                        corner[i] = center + q.Rotate(local);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        for (int bit = 1; bit < 8; bit <<= 1)
                        {
                            if ((i & bit) == 0)
                            {
                                Gizmos.DrawLine(V(corner[i]), V(corner[i | bit]));
                            }
                        }
                    }
                    break;
                }
                case JiggleConst.ColliderPlane:
                {
                    JVec3 n = At(c.Axis);
                    JVec3 u = JiggleMath.Perpendicular(n).NormalizeSafe(JVec3.Up);
                    JVec3 w = JVec3.Cross(n, u);
                    const float half = 1f;
                    for (int i = -2; i <= 2; i++)
                    {
                        float t = half * (float)i / 2f;
                        Gizmos.DrawLine(V(center + u * t - w * half), V(center + u * t + w * half));
                        Gizmos.DrawLine(V(center + w * t - u * half), V(center + w * t + u * half));
                    }
                    Gizmos.DrawLine(V(center), V(center + n * 0.5f));
                    break;
                }
                default:
                    Gizmos.DrawWireSphere(V(center), c.Radius);
                    break;
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawField(JiggleForceField field, GizmoType type)
        {
            JiggleField f = field.Build();
            JVec3 center = At(f.Center);
            JVec3 dir = new JVec3(f.Dir.X, f.Dir.Y, f.Dir.Z);
            Gizmos.color = new Color(0.4f, 0.8f, 1f);
            if (f.Radius > 0f)
            {
                Gizmos.DrawWireSphere(V(center), f.Radius);
            }
            switch (f.Kind)
            {
                case JiggleConst.FieldRadial:
                {
                    float r = f.Radius > 0f ? f.Radius : 1f;
                    JVec3[] axes = { JVec3.Up, -JVec3.Up, new JVec3(1f, 0f, 0f), new JVec3(-1f, 0f, 0f), new JVec3(0f, 0f, 1f), new JVec3(0f, 0f, -1f) };
                    for (int i = 0; i < axes.Length; i++)
                    {
                        JVec3 tip = f.Strength >= 0f ? center + axes[i] * r : center;
                        JVec3 tail = f.Strength >= 0f ? center : center + axes[i] * r;
                        Gizmos.DrawLine(V(tail), V(tip));
                    }
                    break;
                }
                case JiggleConst.FieldVortex:
                {
                    JVec3 u = JiggleMath.Perpendicular(dir).NormalizeSafe(JVec3.Up);
                    JVec3 w = JVec3.Cross(dir, u);
                    float r = f.Radius > 0f ? f.Radius : 1f;
                    Ring(center, u, w, r * 0.5f);
                    Ring(center, u, w, r);
                    Gizmos.DrawLine(V(center - dir * r), V(center + dir * r));
                    break;
                }
                default:
                {
                    JVec3 u = JiggleMath.Perpendicular(dir).NormalizeSafe(JVec3.Up);
                    JVec3 w = JVec3.Cross(dir, u);
                    float sign = f.Strength >= 0f ? 1f : -1f;
                    for (int i = -1; i <= 1; i++)
                    {
                        JVec3 o = u * (0.25f * (float)i);
                        Gizmos.DrawLine(V(center + o), V(center + o + dir * sign));
                        JVec3 p = u * (0.25f * (float)i) + w * 0.25f;
                        Gizmos.DrawLine(V(center + p), V(center + p + dir * sign));
                    }
                    break;
                }
            }
        }
    }
}
