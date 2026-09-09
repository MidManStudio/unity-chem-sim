using System;
using UnityEngine;
using UnityEngine.Rendering;
using MidManStudio.Alembic.Core;
using MidManStudio.Alembic.Adapters;

namespace MidManStudio.Alembic.Rendering
{
    /// <summary>
    /// Renders every live bond in a chemistry_core context as a thin
    /// cylinder between its two atoms, colored by
    /// <see cref="BondDrawInfo.Strain"/> — the actual payoff of that
    /// value's own stated purpose ("show a bond visually straining before
    /// it snaps"). Same dual-path (GPU instanced / combined-mesh) design
    /// as <see cref="AtomRenderer"/>, same <see cref="InstancingSupport"/>
    /// decision logic.
    ///
    /// Reads bonds via <see cref="ChemistryLib.chem_bonds_ptr"/> — a
    /// flat, deduplicated, zero-copy snapshot of every live edge, one
    /// bulk fetch per frame — then hands it to
    /// <see cref="BondBatchAdapter.FillBatch"/> to compute TRS + strain
    /// for all of them at once. This replaced an earlier per-bond
    /// P/Invoke walk (<c>chem_bond_count</c> + <c>chem_bond_partner_at</c>
    /// + <c>chem_get_atom</c> ×2 + <c>chem_bond_geometry_at</c>, once per
    /// edge, every frame) once that walk's own doc comment flagged it as
    /// the thing to fix if bond counts ever got large — this is that
    /// fix. <see cref="AtomState"/> positions still come from
    /// <see cref="ChemistryLib.chem_atoms_ptr"/> (same zero-copy accessor
    /// <see cref="AtomRenderer"/> already uses), never cached across
    /// frames — same "re-fetch every frame" contract that accessor's own
    /// doc already establishes, now shared by <see cref="ChemistryLib.chem_bonds_ptr"/>
    /// too.
    ///
    /// Bonds are already deduplicated on the Rust side (see
    /// <c>chem_bonds_ptr</c>'s own doc for the exact rule) — no dedup
    /// logic needed here anymore, unlike the old per-atom walk which had
    /// to skip the reverse direction of each symmetric edge itself.
    /// </summary>
    [ExecuteAlways]
    public sealed class BondRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private Material _material;
        [Tooltip("Optional. Defaults to a plain cylinder (Unity's built-in primitive mesh) if left unassigned.")]
        [SerializeField] private Mesh _bondMeshOverride;
        [SerializeField] private bool _forceCombinedMesh;
        [Tooltip("Cylinder radius in Angstroms — same position-space units as the simulation itself.")]
        [SerializeField] private float _bondRadius = 0.15f;

        [Header("Strain Coloring")]
        [SerializeField] private Color _relaxedColor    = new Color(0.85f, 0.85f, 0.85f, 1f);
        [SerializeField] private Color _compressedColor = new Color(0.25f, 0.45f, 1f, 1f);
        [SerializeField] private Color _stretchedColor  = new Color(1f, 0.25f, 0.15f, 1f);
        [Tooltip("Strain magnitude that maps to fully-saturated compressed/stretched color. Tune in Editor — there's no single physically-correct value, it's a visualization choice.")]
        [SerializeField] private float _strainColorScale = 0.3f;

        // ── Instanced-path scratch ──
        private Matrix4x4[] _matrices;
        private Vector4[]   _instanceColors;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");

        // ── Combined-mesh path ──
        private Mesh _defaultCylinderMesh;
        private Mesh _combinedMesh;
        private Mesh      _cachedSourceMesh;
        private Vector3[] _srcVerts;
        private Vector3[] _srcNormals;
        private int[]     _srcTris;
        private Vector3[] _combinedVerts;
        private Vector3[] _combinedNormals;
        private Color32[] _combinedColors;
        private int[]     _combinedTris;

        /// <summary>
        /// TRS + strain for this frame's bonds, filled once per
        /// <see cref="Render"/> call by <see cref="BondBatchAdapter.FillBatch"/>
        /// and consumed by whichever of <see cref="RenderInstanced"/>/
        /// <see cref="RenderCombined"/> runs after it — grown (doubled),
        /// never shrunk, same policy every other scratch buffer in this
        /// class already uses.
        /// </summary>
        private BondDrawInfo[] _drawInfoScratch = Array.Empty<BondDrawInfo>();

        private void Awake()
        {
            _defaultCylinderMesh = AlembicMeshUtility.CreatePrimitiveMesh(PrimitiveType.Cylinder, "AlembicBonds_DefaultCylinder");

            _matrices       = new Matrix4x4[InstancingSupport.MaxBatchSize];
            _instanceColors = new Vector4[InstancingSupport.MaxBatchSize];
            _mpb            = new MaterialPropertyBlock();

            _combinedMesh = new Mesh { name = "AlembicBonds_Combined" };
            _combinedMesh.MarkDynamic();
            _combinedMesh.indexFormat = IndexFormat.UInt32;
        }

        private void OnDestroy()
        {
            AlembicMeshUtility.DestroyMesh(_defaultCylinderMesh);
            AlembicMeshUtility.DestroyMesh(_combinedMesh);
        }

        /// <summary>
        /// Draw every live bond in <paramref name="ctx"/>. Safe to call
        /// with a stale/zero context or zero atoms/bonds — all silent
        /// no-ops.
        /// </summary>
        public unsafe void Render(IntPtr ctx)
        {
            if (_material == null || ctx == IntPtr.Zero) return;

            int atomCount = ChemistryLib.chem_atom_count(ctx);
            if (atomCount <= 0) return;

            IntPtr atomsPtr = ChemistryLib.chem_atoms_ptr(ctx);
            if (atomsPtr == IntPtr.Zero) return;
            AtomState* atoms = (AtomState*)atomsPtr;

            IntPtr bondsPtr = ChemistryLib.chem_bonds_ptr(ctx, out int bondCount);
            if (bondCount <= 0 || bondsPtr == IntPtr.Zero) return;
            BondRecord* bonds = (BondRecord*)bondsPtr;

            EnsureDrawInfoCapacity(bondCount);
            BondBatchAdapter.FillBatch(bonds, bondCount, atoms, atomCount, _drawInfoScratch, _bondRadius);

            Mesh mesh = _bondMeshOverride != null ? _bondMeshOverride : _defaultCylinderMesh;

            if (InstancingSupport.DecidePath(_forceCombinedMesh) == InstancingSupport.RenderPath.Instanced)
                RenderInstanced(mesh, bondCount);
            else
                RenderCombined(mesh, bondCount);
        }

        private void EnsureDrawInfoCapacity(int needed)
        {
            if (_drawInfoScratch.Length < needed)
                _drawInfoScratch = new BondDrawInfo[Math.Max(needed, _drawInfoScratch.Length * 2)];
        }

        private Color ColorForStrain(float strain)
        {
            float t = Mathf.Clamp(strain / Mathf.Max(_strainColorScale, 1e-5f), -1f, 1f);
            return t < 0f
                ? Color.Lerp(_relaxedColor, _compressedColor, -t)
                : Color.Lerp(_relaxedColor, _stretchedColor, t);
        }

        // ── Instanced path ──────────────────────────────────────────────────

        private void RenderInstanced(Mesh mesh, int bondCount)
        {
            int n = 0;
            for (int i = 0; i < bondCount; i++)
            {
                BondDrawInfo info = _drawInfoScratch[i];
                if (!info.Valid) continue; // degenerate near-zero-length edge -- see BondDrawInfo.Valid's own doc

                _matrices[n] = info.Trs; // implicit float4x4 -> Matrix4x4 (Unity.Mathematics' own conversion operator)
                Color c = ColorForStrain(info.Strain);
                _instanceColors[n] = new Vector4(c.r, c.g, c.b, c.a);
                n++;

                if (n == InstancingSupport.MaxBatchSize)
                {
                    FlushInstancedBatch(mesh, n);
                    n = 0;
                }
            }
            if (n > 0) FlushInstancedBatch(mesh, n);
        }

        private void FlushInstancedBatch(Mesh mesh, int n)
        {
            _mpb.SetVectorArray(ColorPropId, _instanceColors);
            Graphics.DrawMeshInstanced(
                mesh, 0, _material, _matrices, n, _mpb,
                ShadowCastingMode.On, receiveShadows: true, layer: gameObject.layer);
        }

        // ── Combined-mesh path ──────────────────────────────────────────────

        private void RenderCombined(Mesh sourceMesh, int bondCount)
        {
            EnsureSourceMeshCached(sourceMesh);

            int vertsPerBond = _srcVerts.Length;
            int trisPerBond  = _srcTris.Length;

            // bondCount is known up front now (chem_bonds_ptr's out-count),
            // unlike the old per-atom walk this replaced -- no more
            // gather-into-growable-scratch-then-bake two-pass dance, size
            // the mesh buffers directly. Some entries may end up !Valid
            // (degenerate edges) and get skipped below, so this is an
            // upper bound, not an exact final size -- harmless, same
            // "never shrink" policy the capacity helpers already use.
            EnsureCombinedMeshCapacity(bondCount * vertsPerBond, bondCount * trisPerBond);

            int actualBondN = 0;
            for (int i = 0; i < bondCount; i++)
            {
                BondDrawInfo info = _drawInfoScratch[i];
                if (!info.Valid) continue;

                Matrix4x4 trs = info.Trs;
                Color32 col = ColorForStrain(info.Strain);

                int vBase = actualBondN * vertsPerBond;
                for (int v = 0; v < vertsPerBond; v++)
                {
                    _combinedVerts[vBase + v]   = trs.MultiplyPoint3x4(_srcVerts[v]);
                    _combinedNormals[vBase + v] = trs.MultiplyVector(_srcNormals[v]).normalized;
                    _combinedColors[vBase + v]  = col;
                }
                int tBase = actualBondN * trisPerBond;
                for (int t = 0; t < trisPerBond; t++)
                    _combinedTris[tBase + t] = _srcTris[t] + vBase;

                actualBondN++;
            }

            int usedVerts = actualBondN * vertsPerBond;
            int usedTris  = actualBondN * trisPerBond;

            _combinedMesh.Clear();
            if (actualBondN > 0)
            {
                _combinedMesh.SetVertices(_combinedVerts, 0, usedVerts);
                _combinedMesh.SetNormals(_combinedNormals, 0, usedVerts);
                _combinedMesh.SetColors(_combinedColors, 0, usedVerts);
                _combinedMesh.SetTriangles(_combinedTris, 0, usedTris, 0);
                _combinedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1_000_000f);
                Graphics.DrawMesh(_combinedMesh, Matrix4x4.identity, _material, gameObject.layer);
            }
        }

        private void EnsureSourceMeshCached(Mesh sourceMesh)
        {
            if (_cachedSourceMesh == sourceMesh) return;
            _cachedSourceMesh = sourceMesh;
            _srcVerts   = sourceMesh.vertices;
            _srcNormals = sourceMesh.normals;
            _srcTris    = sourceMesh.triangles;
        }

        private void EnsureCombinedMeshCapacity(int neededVerts, int neededTris)
        {
            if (_combinedVerts == null || _combinedVerts.Length < neededVerts)
            {
                int cap = _combinedVerts == null ? Math.Max(neededVerts, 1) : Math.Max(neededVerts, _combinedVerts.Length * 2);
                _combinedVerts   = new Vector3[cap];
                _combinedNormals = new Vector3[cap];
                _combinedColors  = new Color32[cap];
            }
            if (_combinedTris == null || _combinedTris.Length < neededTris)
            {
                int cap = _combinedTris == null ? Math.Max(neededTris, 1) : Math.Max(neededTris, _combinedTris.Length * 2);
                _combinedTris = new int[cap];
            }
        }
    }
}
