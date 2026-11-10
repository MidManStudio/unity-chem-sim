// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleBodyLab.cs"
// ============================================================================
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The lab of the body colliders. It builds a standing figure of empty joints, 1.7 meters tall, fits the body colliders to
    /// it, hangs a hair strand from the back of its head with the hair preset, and leans and turns the figure so the strand
    /// is swept across the shoulders and the back. It shows how close the strand gets to the body. Play mode only.
    /// </summary>
    internal static class JiggleBodyLab
    {
        private static GameObject root;
        private static JiggleBodyColliders body;
        private static JiggleChain hair;
        private static Transform hips;
        private static float colliderScale = 1f;
        private static bool sway = true;
        private static float speed = 1.5f;
        private static double swayStart;
        private static float elapsed;
        private static readonly List<JiggleColliderShape> built = new List<JiggleColliderShape>();

        internal static void Draw()
        {
            EditorGUILayout.HelpBox(
                "Body colliders are fitted to the joints of a figure that stands in a T pose. The hair strand hangs from the back of the head and its pose runs through the body, so the colliders have to push it out. Lean and turn the figure to sweep it over the shoulders. The body colliders are drawn in the scene view while the root object is selected.",
                MessageType.None);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode. The setup is made there and is gone when play mode ends.", MessageType.Info);
                return;
            }
            if (GUILayout.Button(root == null ? "Create the test figure" : "Rebuild the test figure"))
            {
                Create();
            }
            if (root == null || body == null || hair == null)
            {
                return;
            }

            EditorGUILayout.Space();
            colliderScale = EditorGUILayout.Slider("Collider scale", colliderScale, 0.3f, 2f);
            if (GUILayout.Button("Rebuild the colliders with this scale"))
            {
                body.scale = colliderScale;
                body.Build();
            }
            sway = EditorGUILayout.ToggleLeft("Lean and turn the figure", sway);
            speed = EditorGUILayout.Slider("Speed", speed, 0.2f, 6f);
            EditorGUI.BeginChangeCheck();
            bool collide = EditorGUILayout.ToggleLeft("The strand collides with the body", hair.settings.collide);
            if (EditorGUI.EndChangeCheck())
            {
                hair.settings.collide = collide;
                hair.SettingsChanged();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Height of the body", body.ResolveHeight().ToString("F2", CultureInfo.InvariantCulture) + " m");
            EditorGUILayout.LabelField("Colliders built", body.BuiltCount.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Closest strand bone to the body surface", Gap());
            if (GUILayout.Button("Remove the test figure"))
            {
                JiggleLabScene.Remove(root);
                root = null;
                body = null;
                hair = null;
                hips = null;
            }
        }

        // A joint: an empty object at a world position, a child of another joint.
        private static Transform Joint(string name, Transform parent, float x, float y, float z)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = new Vector3(x, y, z);
            return g.transform;
        }

        private static void Create()
        {
            JiggleLabScene.Remove(root);
            root = JiggleLabScene.Root("Jiggle Lab - Body colliders");
            Transform r = root.transform;
            hips = Joint("Hips", r, 0f, 0.95f, 0f);
            Transform neck = Joint("Neck", hips, 0f, 1.48f, 0f);
            Transform head = Joint("Head", neck, 0f, 1.55f, 0f);
            JiggleBodyJoints j = new JiggleBodyJoints();
            j.hips = hips;
            j.neck = neck;
            j.head = head;
            // The left side of the character is at negative X when it faces +Z.
            Transform lUpperArm = Joint("LeftUpperArm", hips, -0.18f, 1.42f, 0f);
            Transform lLowerArm = Joint("LeftLowerArm", lUpperArm, -0.46f, 1.42f, 0f);
            j.leftUpperArm = lUpperArm;
            j.leftLowerArm = lLowerArm;
            j.leftHand = Joint("LeftHand", lLowerArm, -0.72f, 1.42f, 0f);
            Transform rUpperArm = Joint("RightUpperArm", hips, 0.18f, 1.42f, 0f);
            Transform rLowerArm = Joint("RightLowerArm", rUpperArm, 0.46f, 1.42f, 0f);
            j.rightUpperArm = rUpperArm;
            j.rightLowerArm = rLowerArm;
            j.rightHand = Joint("RightHand", rLowerArm, 0.72f, 1.42f, 0f);
            Transform lUpperLeg = Joint("LeftUpperLeg", hips, -0.09f, 0.92f, 0f);
            Transform lLowerLeg = Joint("LeftLowerLeg", lUpperLeg, -0.09f, 0.5f, 0f);
            Transform lFoot = Joint("LeftFoot", lLowerLeg, -0.09f, 0.08f, 0f);
            j.leftUpperLeg = lUpperLeg;
            j.leftLowerLeg = lLowerLeg;
            j.leftFoot = lFoot;
            j.leftToes = Joint("LeftToes", lFoot, -0.09f, 0.04f, 0.12f);
            Transform rUpperLeg = Joint("RightUpperLeg", hips, 0.09f, 0.92f, 0f);
            Transform rLowerLeg = Joint("RightLowerLeg", rUpperLeg, 0.09f, 0.5f, 0f);
            Transform rFoot = Joint("RightFoot", rLowerLeg, 0.09f, 0.08f, 0f);
            j.rightUpperLeg = rUpperLeg;
            j.rightLowerLeg = rLowerLeg;
            j.rightFoot = rFoot;
            j.rightToes = Joint("RightToes", rFoot, 0.09f, 0.04f, 0.12f);

            body = root.AddComponent<JiggleBodyColliders>();
            body.joints = j;
            body.scale = colliderScale;
            body.Build();

            // The strand hangs 0.12 m behind the head joint, inside the radius of the trunk once it is below the shoulders,
            // so the body has to hold it out.
            hair = JiggleLabScene.Chain(head, "Hair", new Vector3(0f, 1.62f, -0.12f), 8, 0.09f, new JiggleChainSettings());
            hair.ApplyPreset(JigglePresetKind.Hair);
            swayStart = EditorApplication.timeSinceStartup;
            elapsed = 0f;
        }

        private static float SegmentDistance(JVec3 p, JVec3 a, JVec3 b)
        {
            JVec3 ab = b - a;
            float len2 = ab.LengthSq();
            float t = len2 > 1e-12f ? JVec3.Dot(p - a, ab) / len2 : 0f;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return (p - (a + ab * t)).Length();
        }

        // The distance from a point to the surface of a solver shape. Only spheres and capsules are made by the body colliders.
        private static float Surface(in JiggleCollider c, JVec3 p)
        {
            JVec3 center = JVec3.From4(c.Center);
            if (c.Kind == JiggleConst.ColliderCapsule)
            {
                JVec3 axis = JVec3.From4(c.Axis);
                return SegmentDistance(p, center - axis * c.HalfHeight, center + axis * c.HalfHeight) - c.Radius;
            }
            return (p - center).Length() - c.Radius;
        }

        private static string Gap()
        {
            if (hair == null || body == null || !hair.IsSimulating)
            {
                return "-";
            }
            body.GetBuilt(built);
            if (built.Count == 0)
            {
                return "-";
            }
            float best = float.MaxValue;
            for (int i = 1; i <= hair.BoneCount; i++)
            {
                Vector3 w;
                if (!hair.TryGetParticle(i, out w))
                {
                    continue;
                }
                JVec3 p = new JVec3(w.x, w.y, w.z);
                for (int s = 0; s < built.Count; s++)
                {
                    float gap = Surface(built[s].Peek(), p) - hair.settings.collisionRadius;
                    if (gap < best)
                    {
                        best = gap;
                    }
                }
            }
            return best == float.MaxValue ? "-" : best.ToString("F3", CultureInfo.InvariantCulture) + " m (below 0 is inside)";
        }

        /// <summary>Leans and turns the figure. Called from the editor update, so it runs every frame of the editor.</summary>
        internal static void Tick()
        {
            if (!Application.isPlaying || root == null || hips == null)
            {
                return;
            }
            if (!sway)
            {
                swayStart = EditorApplication.timeSinceStartup - (double)elapsed;
                return;
            }
            float t = (float)(EditorApplication.timeSinceStartup - swayStart);
            elapsed = t;
            hips.localRotation = Quaternion.Euler(Mathf.Sin(t * speed) * 14f, Mathf.Sin(t * speed * 0.7f) * 30f, 0f);
        }
    }
}
