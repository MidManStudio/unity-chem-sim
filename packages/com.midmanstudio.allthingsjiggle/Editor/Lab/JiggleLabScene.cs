// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleLabScene.cs"
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// Builds the objects the labs play with. They are made in play mode only, so they vanish when play mode ends and the
    /// scene of the project is never touched. Everything hangs under one root object that a lab removes with its button.
    /// </summary>
    internal static class JiggleLabScene
    {
        /// <summary>A new empty root object for a lab, selected so the person can find it.</summary>
        internal static GameObject Root(string name)
        {
            GameObject go = new GameObject(name);
            Selection.activeGameObject = go;
            return go;
        }

        /// <summary>
        /// A chain of <paramref name="bones"/> bones hanging straight down from <paramref name="position"/>, each
        /// <paramref name="spacing"/> meters below the last. The anchor is an empty object at the position, the component is on
        /// the first bone below it, as the inspector asks. The settings are the ones the chain starts with.
        /// </summary>
        internal static JiggleChain Chain(
            Transform parent, string name, Vector3 position, int bones, float spacing, JiggleChainSettings settings)
        {
            GameObject anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = position;
            Transform above = anchor.transform;
            Transform first = null;
            for (int i = 0; i < bones; i++)
            {
                GameObject bone = new GameObject(name + " bone " + (i + 1));
                bone.transform.SetParent(above, false);
                bone.transform.localPosition = new Vector3(0f, -spacing, 0f);
                above = bone.transform;
                if (i == 0)
                {
                    first = bone.transform;
                }
            }
            JiggleChain chain = first.gameObject.AddComponent<JiggleChain>();
            chain.settings = settings;
            return chain;
        }

        /// <summary>The tip of a chain, the last simulated particle before the virtual end, or false when it is not simulated.</summary>
        internal static bool Tip(JiggleChain chain, out Vector3 tip)
        {
            tip = Vector3.zero;
            return chain != null && chain.IsSimulating && chain.TryGetParticle(chain.BoneCount, out tip);
        }

        /// <summary>Removes a lab's objects. Safe to call with null.</summary>
        internal static void Remove(GameObject root)
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
