// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleFieldLab.cs"
// ============================================================================
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The lab of the force fields and the hits. It builds a chain and a wind field and has a button for every way to push
    /// a chain: a hit on the whole chain, a hit at a point, a fired field impulse, a burst, and a shove of everything. It
    /// shows how far the tip travelled after the last push, so you can compare them, and how the responses of the chain
    /// scale them. Play mode only.
    /// </summary>
    internal static class JiggleFieldLab
    {
        private static GameObject root;
        private static JiggleChain chain;
        private static JiggleForceField wind;
        private static float impulse = 4f;
        private static float hitRadius = 0.3f;
        private static Vector3 tipAtPush;
        private static float peak;
        private static bool tracking;

        internal static void Draw()
        {
            EditorGUILayout.HelpBox(
                "Force fields push all the time as an acceleration (wind, a fan). Hits and bursts shove once as a change of velocity. The chain scales both with its wind response and impulse response. This builds a chain and a wind field to try every way to push.",
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
            if (root == null || chain == null || wind == null)
            {
                return;
            }
            JiggleManager m = JiggleManager.TryGet();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Wind field");
            wind.enabled = EditorGUILayout.ToggleLeft("Wind on", wind.enabled);
            wind.shape = (JiggleFieldShape)EditorGUILayout.EnumPopup("Shape", wind.shape);
            wind.strength = EditorGUILayout.Slider("Strength (m/s^2)", wind.strength, -20f, 20f);
            wind.radius = EditorGUILayout.Slider("Radius (m, 0 is everywhere for a directional field)", wind.radius, 0f, 5f);
            wind.falloff = EditorGUILayout.IntSlider("Falloff", wind.falloff, 0, 4);
            wind.turbulence = EditorGUILayout.Slider("Turbulence (gusts)", wind.turbulence, 0f, 2f);
            wind.frequency = EditorGUILayout.Slider("Gust frequency (Hz)", wind.frequency, 0f, 6f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Chain");
            EditorGUI.BeginChangeCheck();
            float windResponse = EditorGUILayout.Slider("Wind response", chain.settings.windResponse, 0f, 3f);
            float impulseResponse = EditorGUILayout.Slider("Impulse response", chain.settings.impulseResponse, 0f, 3f);
            bool allowSleep = EditorGUILayout.ToggleLeft("May fall asleep", chain.allowSleep);
            if (EditorGUI.EndChangeCheck())
            {
                chain.settings.windResponse = windResponse;
                chain.settings.impulseResponse = impulseResponse;
                chain.allowSleep = allowSleep;
                chain.SettingsChanged();
            }
            if (m != null)
            {
                m.sleepFrames = EditorGUILayout.IntSlider("Manager sleep frames (0 is off)", m.sleepFrames, 0, 240);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pushes");
            impulse = EditorGUILayout.Slider("Velocity change (m/s)", impulse, -12f, 12f);
            hitRadius = EditorGUILayout.Slider("Hit radius (m)", hitRadius, 0.05f, 2f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hit the chain"))
            {
                Mark();
                chain.AddImpulse(new Vector3(impulse, 0f, 0f));
            }
            if (GUILayout.Button("Hit at the tip"))
            {
                Mark();
                Vector3 tip;
                if (JiggleLabScene.Tip(chain, out tip))
                {
                    chain.AddImpulseAt(tip, new Vector3(impulse, 0f, 0f), hitRadius);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fire the field impulse"))
            {
                Mark();
                wind.impulseStrength = impulse;
                wind.Fire();
            }
            if (GUILayout.Button("Burst under the tip"))
            {
                Mark();
                Vector3 tip;
                if (m != null && JiggleLabScene.Tip(chain, out tip))
                {
                    m.AddBurst(new Vector3(tip.x - 0.15f, tip.y - 0.1f, tip.z), hitRadius * 3f, impulse);
                }
            }
            if (GUILayout.Button("Shove every chain"))
            {
                Mark();
                if (m != null)
                {
                    m.AddShove(new Vector3(0f, 0f, impulse), Vector3.zero);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (m != null)
            {
                EditorGUILayout.LabelField("Fields written", m.Stats.Fields.ToString(CultureInfo.InvariantCulture));
                EditorGUILayout.LabelField("Impulses waiting", m.PendingImpulses.ToString(CultureInfo.InvariantCulture));
            }
            EditorGUILayout.LabelField("Chain asleep", chain.IsAsleep ? "yes" : "no");
            EditorGUILayout.LabelField("Tip travel since the last push", peak.ToString("F3", CultureInfo.InvariantCulture) + " m");
            if (GUILayout.Button("Remove the test setup"))
            {
                JiggleLabScene.Remove(root);
                root = null;
                chain = null;
                wind = null;
            }
        }

        private static void Mark()
        {
            peak = 0f;
            tracking = JiggleLabScene.Tip(chain, out tipAtPush);
        }

        private static void Create()
        {
            JiggleLabScene.Remove(root);
            root = JiggleLabScene.Root("Jiggle Lab - Fields");
            JiggleChainSettings s = new JiggleChainSettings();
            s.stiffness = 0.5f;
            s.drag = 0.08f;
            chain = JiggleLabScene.Chain(root.transform, "Chain", new Vector3(0f, 1.5f, 0f), 10, 0.1f, s);

            GameObject w = new GameObject("Wind");
            w.transform.SetParent(root.transform, false);
            w.transform.position = new Vector3(-1.5f, 1.0f, 0f);
            w.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            wind = w.AddComponent<JiggleForceField>();
            wind.shape = JiggleFieldShape.Directional;
            wind.strength = 0f;
            wind.turbulence = 0.4f;
            peak = 0f;
            tracking = false;
        }

        /// <summary>Follows the tip so the largest travel after a push is known, even between two draws of the window.</summary>
        internal static void Tick()
        {
            if (!tracking || !Application.isPlaying || chain == null)
            {
                return;
            }
            Vector3 tip;
            if (JiggleLabScene.Tip(chain, out tip))
            {
                float d = Vector3.Distance(tip, tipAtPush);
                if (d > peak)
                {
                    peak = d;
                }
            }
        }
    }
}
