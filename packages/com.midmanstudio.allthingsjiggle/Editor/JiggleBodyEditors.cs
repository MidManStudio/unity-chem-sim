// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/JiggleBodyEditors.cs"
// ============================================================================
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The inspector of the body colliders: the settings, what a build would make, and the buttons that fill the joints from the
    /// animator and build or remove the colliders. Building in the editor makes real objects under the joints, which are saved
    /// with the scene or the prefab, and every button can be undone.
    /// </summary>
    [CustomEditor(typeof(JiggleBodyColliders))]
    public sealed class JiggleBodyCollidersEditor : UnityEditor.Editor
    {
        private readonly List<JiggleBodyPlan> plan = new List<JiggleBodyPlan>();
        private readonly List<JiggleColliderShape> built = new List<JiggleColliderShape>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            JiggleBodyColliders c = (JiggleBodyColliders)target;
            EditorGUILayout.Space();
            string problem;
            if (c.Plan(plan, out problem))
            {
                EditorGUILayout.HelpBox(
                    "A build would make " + plan.Count + " colliders for a body " + c.ResolveHeight().ToString("F2", CultureInfo.InvariantCulture)
                    + " m tall. They are drawn in the scene view while this is selected.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Nothing to build yet: " + problem, MessageType.Warning);
            }
            EditorGUILayout.LabelField("Built now", c.BuiltCount.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.HelpBox(
                "Build in the pose the character is bound in, a T pose or an A pose. The sizes come from the distances between the joints at that moment. The colliders are children of the joints and follow the animation. Chains collide with them while Collide is on in their settings, except for the layers they ignore.",
                MessageType.None);

            if (GUILayout.Button("Fill empty joints from the Animator"))
            {
                Undo.RecordObject(c, "Fill jiggle body joints");
                int n = c.AutoAssign();
                EditorUtility.SetDirty(c);
                if (n == 0)
                {
                    Debug.Log("Nothing was filled. The Animator is missing, has no humanoid avatar, or every joint it knows is set already.");
                }
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(c.BuiltCount > 0 ? "Rebuild the colliders" : "Build the colliders"))
            {
                Rebuild(c);
            }
            if (GUILayout.Button("Remove the colliders"))
            {
                Remove(c);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void Remove(JiggleBodyColliders c)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove jiggle body colliders");
            Undo.RecordObject(c, "Remove jiggle body colliders");
            c.GetBuilt(built);
            for (int i = 0; i < built.Count; i++)
            {
                Undo.DestroyObjectImmediate(built[i].gameObject);
            }
            c.Clear();
            EditorUtility.SetDirty(c);
            Undo.CollapseUndoOperations(group);
        }

        private void Rebuild(JiggleBodyColliders c)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build jiggle body colliders");
            Undo.RecordObject(c, "Build jiggle body colliders");
            c.GetBuilt(built);
            for (int i = 0; i < built.Count; i++)
            {
                Undo.DestroyObjectImmediate(built[i].gameObject);
            }
            c.Build();
            c.GetBuilt(built);
            for (int i = 0; i < built.Count; i++)
            {
                Undo.RegisterCreatedObjectUndo(built[i].gameObject, "Build jiggle body colliders");
            }
            EditorUtility.SetDirty(c);
            Undo.CollapseUndoOperations(group);
        }
    }

    /// <summary>Draws what a selected body component would build, or what it has built.</summary>
    internal static class JiggleBodyGizmos
    {
        private static readonly List<JiggleBodyPlan> plan = new List<JiggleBodyPlan>();
        private static readonly List<JiggleColliderShape> built = new List<JiggleColliderShape>();

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void Draw(JiggleBodyColliders body, GizmoType type)
        {
            body.GetBuilt(built);
            if (built.Count > 0)
            {
                for (int i = 0; i < built.Count; i++)
                {
                    JiggleShapeGizmos.Shape(built[i].Peek(), new Color(0.2f, 0.9f, 0.4f));
                }
                return;
            }
            string problem;
            if (!body.Plan(plan, out problem))
            {
                return;
            }
            for (int i = 0; i < plan.Count; i++)
            {
                JiggleShapeGizmos.Shape(plan[i].Shape.ToCollider(), new Color(0.3f, 0.7f, 1f));
            }
        }
    }
}
