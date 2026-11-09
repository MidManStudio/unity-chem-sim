// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleLodLab.cs"
// ============================================================================
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The lab of the level of detail. It edits the limits of the manager while the game runs, builds a row of chains at
    /// growing distances in front of the camera, and lists what level each chain has, so you can see chains stop colliding
    /// and stop simulating as they get farther away or out of view. Play mode only.
    /// </summary>
    internal static class JiggleLodLab
    {
        private const int RowCount = 12;
        private const int ListMax = 24;

        private static GameObject root;
        private static readonly List<JiggleChain> listed = new List<JiggleChain>();

        internal static void Draw()
        {
            EditorGUILayout.HelpBox(
                "Chains farther than the collision distance stop testing colliders. Farther than the cull distance, or out of the camera's view for the delay, they are not simulated and show their pose. A chain that comes back snaps to its pose. A distance of 0 turns that limit off. The camera is the main camera unless the manager names one.",
                MessageType.None);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode.", MessageType.Info);
                return;
            }
            JiggleManager m = JiggleManager.TryGet();
            if (m == null)
            {
                EditorGUILayout.HelpBox("There is no manager yet. Create the row below, or enable any Jiggle Chain.", MessageType.Info);
            }
            else
            {
                m.lodCollisionDistance = EditorGUILayout.Slider("Collision distance (m)", m.lodCollisionDistance, 0f, 100f);
                m.lodCullDistance = EditorGUILayout.Slider("Cull distance (m)", m.lodCullDistance, 0f, 200f);
                m.lodHysteresis = EditorGUILayout.Slider("Hysteresis (m)", m.lodHysteresis, 0f, 10f);
                m.lodCullOffscreen = EditorGUILayout.ToggleLeft("Cull chains the camera does not see", m.lodCullOffscreen);
                m.lodOffscreenDelay = EditorGUILayout.Slider("Offscreen delay (s)", m.lodOffscreenDelay, 0f, 5f);
            }
            if (GUILayout.Button(root == null ? "Create a row of chains in front of the camera" : "Rebuild the row"))
            {
                Create();
            }
            if (m == null)
            {
                return;
            }
            JiggleFrameStats st = m.Stats;
            EditorGUILayout.LabelField("Chains", st.Rigs.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Not testing colliders", st.LodNoCollision.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Not simulated", st.LodOff.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Asleep", st.Asleep.ToString(CultureInfo.InvariantCulture));

            Camera cam = m.lodCamera != null ? m.lodCamera : Camera.main;
            m.GetChains(listed);
            EditorGUILayout.Space();
            for (int i = 0; i < listed.Count && i < ListMax; i++)
            {
                JiggleChain c = listed[i];
                if (c == null)
                {
                    continue;
                }
                string distance = "-";
                if (cam != null && c.Anchor != null)
                {
                    distance = Vector3.Distance(cam.transform.position, c.Anchor.position).ToString("F1", CultureInfo.InvariantCulture) + " m";
                }
                string state = c.LodLevel.ToString() + (c.IsAsleep ? ", asleep" : "") + (c.IsSimulating ? "" : ", not simulated");
                EditorGUILayout.LabelField(c.Anchor != null ? c.Anchor.name : c.name, distance + "  " + state);
            }
            if (listed.Count > ListMax)
            {
                EditorGUILayout.LabelField("and " + (listed.Count - ListMax) + " more");
            }
            if (cam == null)
            {
                EditorGUILayout.HelpBox("There is no camera, so every chain is simulated in full.", MessageType.Warning);
            }
        }

        private static void Create()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("The LOD lab needs a main camera to place the chains in front of.");
                return;
            }
            JiggleLabScene.Remove(root);
            root = JiggleLabScene.Root("Jiggle Lab - Level of detail");
            Transform ct = cam.transform;
            for (int i = 0; i < RowCount; i++)
            {
                float d = 3f + 4f * (float)i;
                float side = (i & 1) == 0 ? -0.5f : 0.5f;
                Vector3 p = ct.position + ct.forward * d + ct.right * side + ct.up * 0.5f;
                JiggleChainSettings s = new JiggleChainSettings();
                s.collide = true;
                s.collisionRadius = 0.04f;
                JiggleLabScene.Chain(root.transform, "Chain " + (i + 1) + " at " + d.ToString("F0", CultureInfo.InvariantCulture) + " m", p, 6, 0.1f, s);
            }
        }
    }
}
