using UnityEngine;

namespace MidManStudio.Alembic.Rendering
{
    /// <summary>
    /// Small shared helper — both <see cref="AtomRenderer"/> and
    /// <see cref="BondRenderer"/> need the same instanced-vs-combined-mesh
    /// decision and the same Unity hard limit, so it lives here once
    /// instead of twice. Deliberately NOT a shared base class for the
    /// renderers themselves — atoms and bonds pull from genuinely
    /// different data sources (the dense atom array vs. a per-atom bond
    /// walk), and forcing them through one inheritance hierarchy for the
    /// sake of a threshold check and a constant isn't worth the coupling.
    /// </summary>
    public static class InstancingSupport
    {
        public enum RenderPath { Instanced, CombinedMesh }

        /// <summary>
        /// Unity's own actual hard limit for a single DrawMeshInstanced
        /// call — not a tuning knob, this is the real ceiling the API
        /// itself imposes. Both renderers batch in chunks of this size
        /// regardless of how many atoms/bonds exist in total.
        /// </summary>
        public const int MaxBatchSize = 1023;

        /// <summary>
        /// True GPU instancing support, or the deliberate opt-out
        /// (<paramref name="forceCombinedMesh"/>) — the same "check the
        /// hardware, but let a developer override it" pattern
        /// MidManStudio_Unity's own ProjectileRenderer2D already uses
        /// (its own <c>_forceDrawMesh</c> inspector toggle). Exists here
        /// for exactly the reason it was asked for: not every development
        /// machine actually supports GPU instancing, and this system
        /// needs to work — not just degrade ungracefully — on those too.
        /// </summary>
        public static RenderPath DecidePath(bool forceCombinedMesh) =>
            (!forceCombinedMesh && SystemInfo.supportsInstancing)
                ? RenderPath.Instanced
                : RenderPath.CombinedMesh;
    }
}
