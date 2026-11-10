// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/JigglePresetEditors.cs"
// ============================================================================
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>The pieces of preset editing that the asset inspector, the chain inspector and the preset lab share.</summary>
    internal static class JigglePresetGui
    {
        private static string[] names;

        /// <summary>The step rate the drag is shown for: the manager's while it exists, else 60.</summary>
        internal static float Hz()
        {
            JiggleManager m = Application.isPlaying ? JiggleManager.TryGet() : null;
            return m != null ? JiggleDamping.CleanHz(m.fixedHz) : JiggleDamping.DefaultHz;
        }

        private static string[] Names()
        {
            if (names == null)
            {
                names = new string[JigglePresets.Count];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = JigglePresets.Name((JigglePresetKind)i);
                }
            }
            return names;
        }

        /// <summary>A popup of the built in kinds.</summary>
        internal static JigglePresetKind KindPopup(string label, JigglePresetKind kind)
        {
            int i = EditorGUILayout.Popup(label, (int)kind, Names());
            return (JigglePresetKind)Mathf.Clamp(i, 0, JigglePresets.Count - 1);
        }

        private static float Per(float perSecond)
        {
            return JiggleDamping.PerStep(perSecond, Hz());
        }

        /// <summary>
        /// Draws every value of a preset as sliders, with the two frictions also shown per step at the current step rate.
        /// Returns true when the person changed one.
        /// </summary>
        internal static bool Values(ref JigglePresetValues v)
        {
            EditorGUI.BeginChangeCheck();
            v.Stiffness = EditorGUILayout.Slider("Stiffness", v.Stiffness, 0f, 1f);
            v.StiffnessAtTip = EditorGUILayout.Slider("Stiffness at the tip (multiplier)", v.StiffnessAtTip, 0f, 2f);
            v.Soften = EditorGUILayout.Slider("Soften", v.Soften, 0f, 1f);
            v.RootStretch = EditorGUILayout.Slider("Root stretch", v.RootStretch, 0f, 1f);
            v.AngleLimited = EditorGUILayout.ToggleLeft("Angle limited", v.AngleLimited);
            v.AngleLimit = EditorGUILayout.Slider("Angle limit (1 is 90 degrees)", v.AngleLimit, 0f, 1f);
            v.AngleLimitSoften = EditorGUILayout.Slider("Angle limit soften", v.AngleLimitSoften, 0f, 1f);
            v.Stretch = EditorGUILayout.Slider("Stretch", v.Stretch, 0f, 1f);
            v.DampingPerSecond = Mathf.Clamp01(EditorGUILayout.FloatField("Damping per second (0 to 1)", v.DampingPerSecond));
            EditorGUILayout.LabelField("   as drag per step", Per(v.DampingPerSecond).ToString("F4", CultureInfo.InvariantCulture) + " at " + Hz().ToString("F0", CultureInfo.InvariantCulture) + " Hz");
            v.AirDampingPerSecond = Mathf.Clamp01(EditorGUILayout.FloatField("Air damping per second (0 to 1)", v.AirDampingPerSecond));
            EditorGUILayout.LabelField("   as air drag per step", Per(v.AirDampingPerSecond).ToString("F4", CultureInfo.InvariantCulture));
            v.Gravity = EditorGUILayout.Slider("Gravity", v.Gravity, -1f, 3f);
            v.Blend = EditorGUILayout.Slider("Blend", v.Blend, 0f, 1f);
            v.IgnoreRootMotion = EditorGUILayout.Slider("Ignore root motion", v.IgnoreRootMotion, 0f, 1f);
            v.Mass = EditorGUILayout.Slider("Mass (0 is no coupling)", v.Mass, 0f, 5f);
            v.Collide = EditorGUILayout.ToggleLeft("Collide", v.Collide);
            v.CollisionRadius = EditorGUILayout.Slider("Collision radius (m)", v.CollisionRadius, 0f, 0.3f);
            v.SegmentCollision = EditorGUILayout.ToggleLeft("Segment collision", v.SegmentCollision);
            v.WindResponse = EditorGUILayout.Slider("Wind response", v.WindResponse, 0f, 4f);
            v.ImpulseResponse = EditorGUILayout.Slider("Impulse response", v.ImpulseResponse, 0f, 4f);
            v.TipScale = EditorGUILayout.Slider("Tip scale", v.TipScale, 0.01f, 3f);
            return EditorGUI.EndChangeCheck();
        }
    }

    /// <summary>The inspector of a preset asset: a button to start from a built in kind, and every value.</summary>
    [CustomEditor(typeof(JigglePreset))]
    public sealed class JigglePresetEditor : UnityEditor.Editor
    {
        private JigglePresetKind starting = JigglePresetKind.Hair;

        public override void OnInspectorGUI()
        {
            JigglePreset p = (JigglePreset)target;
            EditorGUILayout.HelpBox(
                "Apply this to a chain from its inspector, or with JiggleChain.ApplyPreset. The numbers of the built in kinds are starting points that nobody has judged by eye: tune them in the Presets tab of the Jiggle Lab.",
                MessageType.None);
            EditorGUILayout.BeginHorizontal();
            starting = JigglePresetGui.KindPopup("Start from", starting);
            if (GUILayout.Button("Load"))
            {
                Undo.RecordObject(p, "Load jiggle preset");
                p.values = JigglePresets.Get(starting);
                EditorUtility.SetDirty(p);
            }
            EditorGUILayout.EndHorizontal();
            JigglePresetValues v = p.values;
            if (JigglePresetGui.Values(ref v))
            {
                Undo.RecordObject(p, "Edit jiggle preset");
                p.values = v;
                EditorUtility.SetDirty(p);
            }
        }
    }
}
