// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleColliderLab.cs"
// ============================================================================
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The lab of the colliders. It builds a chain that collides, a ball that sweeps through it and a floor, then lets you
    /// turn on the things the colliders do: pushing bones out, the layer rule, segment collision, and waking a chain that
    /// sleeps when a collider moves up to it. Play mode only.
    /// </summary>
    internal static class JiggleColliderLab
    {
        // The layer the ball goes on, so ignoring it does not also ignore the floor. Layer 2 is Ignore Raycast, which every
        // project has.
        private const int BallLayer = 2;

        private static GameObject root;
        private static JiggleChain chain;
        private static JiggleColliderShape ball;
        private static JiggleColliderShape floor;
        private static bool sweep = true;
        private static float range = 0.3f;
        private static float speed = 1.2f;
        private static bool ignoreBall;
        private static double sweepStart;
        private static float elapsed;

        internal static void Draw()
        {
            EditorGUILayout.HelpBox(
                "A chain collides with the Jiggle Collider components of the scene while Collide is on in its settings. This builds a chain, a ball that sweeps through it and a floor under it. Watch the bones get pushed out, turn the ball's layer off for the chain, and let the chain fall asleep to see a moving collider wake it.",
                MessageType.None);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode. The setup is made there and is gone when play mode ends.", MessageType.Info);
                return;
            }
            if (GUILayout.Button(root == null ? "Create the test setup" : "Rebuild the test setup"))
            {
                Create();
            }
            if (root == null || chain == null || ball == null)
            {
                return;
            }
            JiggleManager m = JiggleManager.TryGet();

            EditorGUILayout.Space();
            sweep = EditorGUILayout.ToggleLeft("Sweep the ball through the chain", sweep);
            range = EditorGUILayout.Slider("Sweep range (m)", range, 0.05f, 1f);
            speed = EditorGUILayout.Slider("Sweep speed", speed, 0.1f, 6f);
            EditorGUI.BeginChangeCheck();
            bool ignore = EditorGUILayout.ToggleLeft("Chain ignores the ball's layer (a set bit means no collision)", ignoreBall);
            if (EditorGUI.EndChangeCheck())
            {
                ignoreBall = ignore;
                ApplyLayer();
            }
            EditorGUI.BeginChangeCheck();
            bool segment = EditorGUILayout.ToggleLeft("Segment collision (the whole bone, not only its end)", chain.settings.segmentCollision);
            bool allowSleep = EditorGUILayout.ToggleLeft("Chain may fall asleep", chain.allowSleep);
            if (EditorGUI.EndChangeCheck())
            {
                chain.settings.segmentCollision = segment;
                chain.allowSleep = allowSleep;
                chain.SettingsChanged();
            }
            if (m != null)
            {
                m.sleepFrames = EditorGUILayout.IntSlider("Manager sleep frames (0 is off)", m.sleepFrames, 0, 240);
                m.colliderMargin = EditorGUILayout.Slider("Collider margin (m)", m.colliderMargin, 0f, 2f);
            }

            EditorGUILayout.Space();
            if (m != null)
            {
                JiggleFrameStats st = m.Stats;
                EditorGUILayout.LabelField("Colliders written", st.Colliders.ToString(CultureInfo.InvariantCulture));
                EditorGUILayout.LabelField("Collider tests listed over all rigs", st.ColliderRefs.ToString(CultureInfo.InvariantCulture));
            }
            EditorGUILayout.LabelField("Chain asleep", chain.IsAsleep ? "yes" : "no");
            EditorGUILayout.LabelField("Closest bone to the ball's surface", Gap());
            if (GUILayout.Button("Remove the test setup"))
            {
                JiggleLabScene.Remove(root);
                root = null;
                chain = null;
                ball = null;
                floor = null;
            }
        }

        private static string Gap()
        {
            if (chain == null || ball == null || !chain.IsSimulating)
            {
                return "-";
            }
            Vector3 c = ball.transform.position;
            float best = float.MaxValue;
            for (int i = 1; i <= chain.BoneCount; i++)
            {
                Vector3 p;
                if (chain.TryGetParticle(i, out p))
                {
                    float gap = Vector3.Distance(p, c) - ball.radius - chain.settings.collisionRadius;
                    if (gap < best)
                    {
                        best = gap;
                    }
                }
            }
            return best == float.MaxValue ? "-" : best.ToString("F3", CultureInfo.InvariantCulture) + " m (below 0 is inside)";
        }

        private static void ApplyLayer()
        {
            LayerMask mask = new LayerMask();
            mask.value = ignoreBall ? (1 << BallLayer) : 0;
            chain.ignoreLayers = mask;
        }

        private static void Create()
        {
            JiggleLabScene.Remove(root);
            root = JiggleLabScene.Root("Jiggle Lab - Colliders");
            JiggleChainSettings s = new JiggleChainSettings();
            s.collide = true;
            s.collisionRadius = 0.04f;
            s.stiffness = 0.55f;
            s.stretch = 0.05f;
            chain = JiggleLabScene.Chain(root.transform, "Chain", new Vector3(0f, 1.5f, 0f), 10, 0.1f, s);

            GameObject b = new GameObject("Ball");
            b.transform.SetParent(root.transform, false);
            b.transform.position = new Vector3(0f, 1.0f, 0f);
            b.layer = BallLayer;
            ball = b.AddComponent<JiggleColliderShape>();
            ball.shape = JiggleShapeKind.Sphere;
            ball.radius = 0.12f;

            GameObject f = new GameObject("Floor");
            f.transform.SetParent(root.transform, false);
            f.transform.position = new Vector3(0f, 0.7f, 0f);
            floor = f.AddComponent<JiggleColliderShape>();
            floor.shape = JiggleShapeKind.Plane;
            floor.staticShape = true;

            ignoreBall = false;
            sweepStart = EditorApplication.timeSinceStartup;
            elapsed = 0f;
        }

        /// <summary>Moves the ball. Called from the editor update, so it runs every frame of the editor, not only when the window draws.</summary>
        internal static void Tick()
        {
            if (!Application.isPlaying || root == null || ball == null)
            {
                return;
            }
            if (!sweep)
            {
                sweepStart = EditorApplication.timeSinceStartup - (double)elapsed;
                return;
            }
            float t = (float)(EditorApplication.timeSinceStartup - sweepStart);
            elapsed = t;
            ball.transform.position = new Vector3(Mathf.Sin(t * speed) * range, 1.0f, 0f);
        }
    }
}
