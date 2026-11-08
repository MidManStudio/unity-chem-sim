// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/JiggleChainEditor.cs"
// ============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>The inspector of a chain: the settings, what the chain would walk, and the buttons for play mode.</summary>
    [CustomEditor(typeof(JiggleChain))]
    public sealed class JiggleChainEditor : UnityEditor.Editor
    {
        private readonly List<Transform> walk = new List<Transform>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            JiggleChain chain = (JiggleChain)target;
            EditorGUILayout.Space();
            if (chain.Anchor == null)
            {
                EditorGUILayout.HelpBox(
                    "This transform has no parent. The parent is the anchor that holds the chain, so add the component to the first bone, below it.",
                    MessageType.Error);
                return;
            }
            string problem;
            if (!chain.TryWalk(walk, out problem))
            {
                EditorGUILayout.HelpBox("The chain is not valid: " + problem, MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox(
                "Anchor: " + chain.Anchor.name + "\nBones: " + walk.Count + " (" + walk[0].name + " to " + walk[walk.Count - 1].name + ")",
                MessageType.None);
            if (!Application.isPlaying)
            {
                return;
            }
            EditorGUILayout.LabelField("Simulated", chain.IsSimulating ? "yes" : "no");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset simulation"))
            {
                chain.ResetSimulation();
            }
            if (GUILayout.Button("Rebind to current pose"))
            {
                chain.Rebind();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>Draws the bones of a selected chain, and the simulated particles while the game runs.</summary>
    internal static class JiggleChainGizmos
    {
        private static readonly List<Transform> walk = new List<Transform>();

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void Draw(JiggleChain chain, GizmoType type)
        {
            string problem;
            if (!chain.TryWalk(walk, out problem) || walk.Count == 0)
            {
                return;
            }
            Gizmos.color = new Color(0.3f, 0.7f, 1f);
            Vector3 prev = chain.Anchor != null ? chain.Anchor.position : walk[0].position;
            for (int i = 0; i < walk.Count; i++)
            {
                Vector3 p = walk[i].position;
                Gizmos.DrawLine(prev, p);
                Gizmos.DrawWireSphere(p, 0.01f);
                prev = p;
            }
            if (!Application.isPlaying || !chain.IsSimulating)
            {
                return;
            }
            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            Vector3 last;
            if (!chain.TryGetParticle(0, out last))
            {
                return;
            }
            for (int i = 1; i <= chain.BoneCount + 1; i++)
            {
                Vector3 p;
                if (!chain.TryGetParticle(i, out p))
                {
                    break;
                }
                Gizmos.DrawLine(last, p);
                Gizmos.DrawSphere(p, 0.008f);
                last = p;
            }
        }
    }
}
