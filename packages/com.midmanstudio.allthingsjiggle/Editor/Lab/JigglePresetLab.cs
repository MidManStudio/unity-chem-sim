// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JigglePresetLab.cs"
// ============================================================================
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The lab of the presets. It builds one chain for every built in preset, plus a seventh that takes the values you edit,
    /// and moves all their anchors the same way, so you can see how each kind swings and how far. The numbers of the presets
    /// have never been judged by eye: this is where they get judged. The edited values can be sent to the chain you have
    /// selected or saved as a preset asset. Play mode only.
    /// </summary>
    internal static class JigglePresetLab
    {
        // How many bones and how far apart, for each kind, in the order of JigglePresetKind. A hair strand is long and thin, an ear short.
        private static readonly int[] Bones = { 8, 10, 3, 3, 2, 6 };
        private static readonly float[] Spacing = { 0.09f, 0.1f, 0.08f, 0.08f, 0.1f, 0.12f };

        private static GameObject root;
        private static JiggleChain[] chains;
        private static float[] drop;
        private static Vector3[] basePos;
        private static float[] peak;
        private static JigglePresetKind tunedFrom = JigglePresetKind.Hair;
        private static JigglePresetValues tuned = JigglePresets.Get(JigglePresetKind.Hair);
        private static bool sway = true;
        private static float range = 0.35f;
        private static float speed = 2f;
        private static double swayStart;
        private static float elapsed;

        internal static void Draw()
        {
            EditorGUILayout.HelpBox(
                "One chain for each built in preset, hung side by side and moved the same way, with a last one that takes the values you edit below. Peak sway is how far the tip got from where its pose puts it. Judge the feel by eye and tune the last chain, then apply it to a chain you have selected or save it as an asset.",
                MessageType.None);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode. The setup is made there and is gone when play mode ends.", MessageType.Info);
                return;
            }
            if (GUILayout.Button(root == null ? "Create the preset row" : "Rebuild the preset row"))
            {
                Create();
            }
            if (root == null || chains == null)
            {
                return;
            }

            EditorGUILayout.Space();
            sway = EditorGUILayout.ToggleLeft("Move the anchors from side to side", sway);
            range = EditorGUILayout.Slider("Move range (m)", range, 0.05f, 1f);
            speed = EditorGUILayout.Slider("Move speed", speed, 0.2f, 10f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hit every chain sideways"))
            {
                for (int i = 0; i < chains.Length; i++)
                {
                    if (chains[i] != null)
                    {
                        chains[i].AddImpulse(new Vector3(3f, 0f, 0f));
                    }
                }
            }
            if (GUILayout.Button("Reset the peaks"))
            {
                for (int i = 0; i < peak.Length; i++)
                {
                    peak[i] = 0f;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Peak sway of the tip (m)");
            for (int i = 0; i < chains.Length; i++)
            {
                string label = i < JigglePresets.Count ? JigglePresets.Name((JigglePresetKind)i) : "Tuned";
                EditorGUILayout.LabelField(label, peak[i].ToString("F3", CultureInfo.InvariantCulture) + "  now " + Sway(i).ToString("F3", CultureInfo.InvariantCulture));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("The tuned chain");
            EditorGUILayout.BeginHorizontal();
            tunedFrom = JigglePresetGui.KindPopup("Start from", tunedFrom);
            if (GUILayout.Button("Load"))
            {
                tuned = JigglePresets.Get(tunedFrom);
                ApplyTuned();
            }
            EditorGUILayout.EndHorizontal();
            if (JigglePresetGui.Values(ref tuned))
            {
                ApplyTuned();
            }

            EditorGUILayout.Space();
            GameObject selected = Selection.activeGameObject;
            JiggleChain target = selected != null ? selected.GetComponent<JiggleChain>() : null;
            if (target != null && GUILayout.Button("Apply the tuned values to the selected chain (" + target.name + ")"))
            {
                Undo.RecordObject(target, "Apply tuned jiggle preset");
                target.ApplyPreset(tuned);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("Save the tuned values as a preset asset..."))
            {
                string path = EditorUtility.SaveFilePanelInProject("Save the preset", "JigglePreset", "asset", "Where to save the preset");
                if (!string.IsNullOrEmpty(path))
                {
                    JigglePreset a = ScriptableObject.CreateInstance<JigglePreset>();
                    a.values = tuned;
                    AssetDatabase.CreateAsset(a, path);
                    AssetDatabase.SaveAssets();
                }
            }
            if (GUILayout.Button("Remove the preset row"))
            {
                JiggleLabScene.Remove(root);
                root = null;
                chains = null;
            }
        }

        private static void ApplyTuned()
        {
            JiggleChain c = chains != null ? chains[JigglePresets.Count] : null;
            if (c != null)
            {
                c.ApplyPreset(tuned);
            }
        }

        // How far the tip of a chain is from where its pose puts it, 0 when it is not simulated.
        private static float Sway(int i)
        {
            JiggleChain c = chains[i];
            Vector3 tip;
            if (c == null || c.Anchor == null || !JiggleLabScene.Tip(c, out tip))
            {
                return 0f;
            }
            Vector3 rest = c.Anchor.position + new Vector3(0f, -drop[i], 0f);
            float dx = tip.x - rest.x;
            float dy = tip.y - rest.y;
            float dz = tip.z - rest.z;
            return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static void Create()
        {
            JiggleLabScene.Remove(root);
            root = JiggleLabScene.Root("Jiggle Lab - Presets");
            int n = JigglePresets.Count + 1;
            chains = new JiggleChain[n];
            drop = new float[n];
            basePos = new Vector3[n];
            peak = new float[n];
            for (int i = 0; i < n; i++)
            {
                int k = i < JigglePresets.Count ? i : (int)tunedFrom;
                string name = i < JigglePresets.Count ? JigglePresets.Name((JigglePresetKind)i) : "Tuned";
                basePos[i] = new Vector3(-2.4f + 0.8f * (float)i, 1.8f, 0f);
                drop[i] = Bones[k] * Spacing[k];
                chains[i] = JiggleLabScene.Chain(root.transform, name, basePos[i], Bones[k], Spacing[k], new JiggleChainSettings());
                if (i < JigglePresets.Count)
                {
                    chains[i].ApplyPreset((JigglePresetKind)i);
                }
                else
                {
                    chains[i].ApplyPreset(tuned);
                }
            }
            swayStart = EditorApplication.timeSinceStartup;
            elapsed = 0f;
        }

        /// <summary>Moves the anchors and tracks the peaks. Called from the editor update, so it runs every frame of the editor.</summary>
        internal static void Tick()
        {
            if (!Application.isPlaying || root == null || chains == null)
            {
                return;
            }
            if (sway)
            {
                float t = (float)(EditorApplication.timeSinceStartup - swayStart);
                elapsed = t;
                float x = Mathf.Sin(t * speed) * range;
                for (int i = 0; i < chains.Length; i++)
                {
                    if (chains[i] != null && chains[i].Anchor != null)
                    {
                        chains[i].Anchor.position = basePos[i] + new Vector3(x, 0f, 0f);
                    }
                }
            }
            else
            {
                swayStart = EditorApplication.timeSinceStartup - (double)elapsed;
            }
            for (int i = 0; i < chains.Length; i++)
            {
                float s = Sway(i);
                if (s > peak[i])
                {
                    peak[i] = s;
                }
            }
        }
    }
}
