using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using MidManStudio.Alembic.Core;

namespace MidManStudio.Alembic.Rendering
{
    /// <summary>
    /// Renders every live bond in a chemistry_core context as a thin
    /// cylinder between its two atoms, colored by
    /// <see cref="BondGeometry.Strain"/> — the actual payoff of that FFI
    /// accessor's own stated purpose ("show a bond visually straining
    /// before it snaps"). Same dual-path (GPU instanced / combined-mesh)
    /// design as <see cref="AtomRenderer"/>, same
    /// <see cref="InstancingSupport"/> decision logic.
    ///
    /// A real, worth-knowing scaling difference from
    /// <see cref="AtomRenderer"/>: atoms have a genuine zero-copy bulk
    /// accessor (<see cref="ChemistryLib.chem_atoms_ptr"/>), but bonds
    /// don't — chemistry_core's FFI surface only offers per-atom,
    /// per-index bond queries
    /// (<see cref="ChemistryLib.TryGetBondPartner"/>/
    /// <see cref="ChemistryLib.TryGetBondGeometry"/>), so this walks
    /// every live atom's bond list one P/Invoke call at a time. Correct
    /// and fully functional at the scales this was actually tested
    /// against, but genuinely more P/Invoke-call-heavy per frame than
    /// atom rendering is at very large (six-figure) bond counts — if
    /// that ever becomes a real bottleneck, the right fix is a bulk
    /// bonds accessor mirroring how <see cref="ChemistryLib.chem_handles_ptr"/>
    /// mirrors <see cref="ChemistryLib.chem_atoms_ptr"/>, not something
    /// this file tries to work around on its own.
    ///
    /// Bonds are stored symmetrically on the Rust side (both atoms in a
    /// pair hold an entry for the same edge) — walked naively, every
    /// edge would be visited, and drawn, twice. Deduplicated here by only
    /// drawing an edge when <c>handle.Index &lt; partner.Index</c> —
    /// cheap, no hash set needed, and correct given both handles being
    /// compared are for atoms that are, by construction, live right now.
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

            IntPtr handlesPtr = ChemistryLib.chem_handles_ptr(ctx);
            if (handlesPtr == IntPtr.Zero) return;
            AtomHandle* handles = (AtomHandle*)handlesPtr;

            Mesh mesh = _bondMeshOverride != null ? _bondMeshOverride : _defaultCylinderMesh;

            if (InstancingSupport.DecidePath(_forceCombinedMesh) == InstancingSupport.RenderPath.Instanced)
                RenderInstanced(ctx, handles, atomCount, mesh);
            else
                RenderCombined(ctx, handles, atomCount, mesh);
        }

        private Color ColorForStrain(float strain)
        {
            float t = Mathf.Clamp(strain / Mathf.Max(_strainColorScale, 1e-5f), -1f, 1f);
            return t < 0f
                ? Color.Lerp(_relaxedColor, _compressedColor, -t)
                : Color.Lerp(_relaxedColor, _stretchedColor, t);
        }

        /// <summary>
        /// TRS for a cylinder spanning posA..posB. Unity's built-in
        /// cylinder is 2 units tall along local Y (Y = -1..1), radius 0.5
        /// — hence scale.y = length/2 (not length) and scale.x/z =
        /// 2*_bondRadius. Returns false (matrix left as identity, caller
        /// must skip) for a degenerate near-zero-length pair — a
        /// same-position edge has no meaningful orientation, and
        /// Quaternion.FromToRotation on a near-zero vector is exactly the
        /// kind of near-singular input worth guarding rather than trusting
        /// to happen to behave.
        /// </summary>
        private bool TryComputeBondTRS(Vector3 posA, Vector3 posB, out Matrix4x4 trs)
        {
            Vector3 delta = posB - posA;
            float length = delta.magnitude;
            if (length < 1e-6f)
            {
                trs = Matrix4x4.identity;
                return false;
            }
            Vector3 mid = (posA + posB) * 0.5f;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, delta / length);
            Vector3 scale = new Vector3(_bondRadius * 2f, length * 0.5f, _bondRadius * 2f);
            trs = Matrix4x4.TRS(mid, rot, scale);
            return true;
        }

        // ── Instanced path ──────────────────────────────────────────────────

        private unsafe void RenderInstanced(IntPtr ctx, AtomHandle* handles, int atomCount, Mesh mesh)
        {
            int n = 0;
            for (int i = 0; i < atomCount; i++)
            {
                AtomHandle h = handles[i];
                int bondCount = ChemistryLib.chem_bond_count(ctx, h);
                for (int b = 0; b < bondCount; b++)
                {
                    if (!ChemistryLib.TryGetBondPartner(ctx, h, b, out AtomHandle partner)) continue;
                    if (partner.Index >= h.Index) continue; // dedup — see class doc

                    if (!ChemistryLib.TryGetAtom(ctx, h, out AtomState stateA)) continue;
                    if (!ChemistryLib.TryGetAtom(ctx, partner, out AtomState stateB)) continue;
                    if (!ChemistryLib.TryGetBondGeometry(ctx, h, b, out BondGeometry geom)) continue;

                    float3 pa3 = stateA.Position, pb3 = stateB.Position;
                    if (!TryComputeBondTRS(new Vector3(pa3.x, pa3.y, pa3.z), new Vector3(pb3.x, pb3.y, pb3.z), out Matrix4x4 trs))
                        continue;

                    _matrices[n] = trs;
                    Color c = ColorForStrain(geom.Strain);
                    _instanceColors[n] = new Vector4(c.r, c.g, c.b, c.a);
                    n++;

                    if (n == InstancingSupport.MaxBatchSize)
                    {
                        FlushInstancedBatch(mesh, n);
                        n = 0;
                    }
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

        private unsafe void RenderCombined(IntPtr ctx, AtomHandle* handles, int atomCount, Mesh sourceMesh)
        {
            EnsureSourceMeshCached(sourceMesh);

            int vertsPerBond = _srcVerts.Length;
            int trisPerBond  = _srcTris.Length;

            // Bond count isn't known up front the way atom count is (no
            // single chem_bond_count-for-everything call) — collect TRS +
            // color per bond into growable arrays first, THEN bake geometry,
            // rather than trying to size the mesh buffers before knowing how
            // many bonds actually exist this frame.
            EnsureBondScratchCapacity(atomCount * 4); // 4 as a starting guess — grows below if wrong, never shrinks
            int bondN = 0;

            for (int i = 0; i < atomCount; i++)
            {
                AtomHandle h = handles[i];
                int bondCount = ChemistryLib.chem_bond_count(ctx, h);
                for (int b = 0; b < bondCount; b++)
                {
                    if (!ChemistryLib.TryGetBondPartner(ctx, h, b, out AtomHandle partner)) continue;
                    if (partner.Index >= h.Index) continue;

                    if (!ChemistryLib.TryGetAtom(ctx, h, out AtomState stateA)) continue;
                    if (!ChemistryLib.TryGetAtom(ctx, partner, out AtomState stateB)) continue;
                    if (!ChemistryLib.TryGetBondGeometry(ctx, h, b, out BondGeometry geom)) continue;

                    float3 pa3 = stateA.Position, pb3 = stateB.Position;
                    if (!TryComputeBondTRS(new Vector3(pa3.x, pa3.y, pa3.z), new Vector3(pb3.x, pb3.y, pb3.z), out Matrix4x4 trs))
                        continue;

                    if (bondN >= _bondTrsScratch.Length) GrowBondScratch();
                    _bondTrsScratch[bondN] = trs;
                    _bondColorScratch[bondN] = ColorForStrain(geom.Strain);
                    bondN++;
                }
            }

            int neededVerts = bondN * vertsPerBond;
            int neededTris  = bondN * trisPerBond;
            EnsureCombinedMeshCapacity(neededVerts, neededTris);

            for (int i = 0; i < bondN; i++)
            {
                Matrix4x4 trs = _bondTrsScratch[i];
                Color32 col = _bondColorScratch[i];
                int vBase = i * vertsPerBond;
                for (int v = 0; v < vertsPerBond; v++)
                {
                    _combinedVerts[vBase + v]   = trs.MultiplyPoint3x4(_srcVerts[v]);
                    _combinedNormals[vBase + v] = trs.MultiplyVector(_srcNormals[v]).normalized;
                    _combinedColors[vBase + v]  = col;
                }
                int tBase = i * trisPerBond;
                for (int t = 0; t < trisPerBond; t++)
                    _combinedTris[tBase + t] = _srcTris[t] + vBase;
            }

            _combinedMesh.Clear();
            if (bondN > 0)
            {
                _combinedMesh.SetVertices(_combinedVerts, 0, neededVerts);
                _combinedMesh.SetNormals(_combinedNormals, 0, neededVerts);
                _combinedMesh.SetColors(_combinedColors, 0, neededVerts);
                _combinedMesh.SetTriangles(_combinedTris, 0, neededTris, 0);
                _combinedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1_000_000f);
                Graphics.DrawMesh(_combinedMesh, Matrix4x4.identity, _material, gameObject.layer);
            }
        }

        private Matrix4x4[] _bondTrsScratch;
        private Color32[]   _bondColorScratch;

        private void EnsureBondScratchCapacity(int needed)
        {
            if (_bondTrsScratch == null || _bondTrsScratch.Length < needed)
            {
                _bondTrsScratch = new Matrix4x4[needed];
                _bondColorScratch = new Color32[needed];
            }
        }

        private void GrowBondScratch()
        {
            int newCap = Math.Max(_bondTrsScratch.Length * 2, 16);
            Array.Resize(ref _bondTrsScratch, newCap);
            Array.Resize(ref _bondColorScratch, newCap);
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
