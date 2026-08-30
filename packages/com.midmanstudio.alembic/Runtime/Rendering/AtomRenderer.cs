using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using MidManStudio.Alembic.Core;

namespace MidManStudio.Alembic.Rendering
{
    /// <summary>
    /// Renders every live atom in a chemistry_core context as a sphere,
    /// sized by its real Van der Waals radius and colored by element
    /// (<see cref="AlembicElementColors"/>). Dual-path: GPU instanced
    /// where <see cref="SystemInfo.supportsInstancing"/> allows it (or
    /// always, via <see cref="_forceCombinedMesh"/>), a single combined
    /// mesh rebuilt every frame otherwise — real, working support for
    /// hardware that can't do GPU instancing at all, not a "todo" left
    /// unhandled. See <see cref="InstancingSupport"/> for the shared
    /// decision logic both this and <see cref="BondRenderer"/> use.
    ///
    /// Reads atoms via <see cref="ChemistryLib.chem_atoms_ptr"/> — zero-
    /// copy, straight into Rust's own array — every <see cref="Render"/>
    /// call, never cached across frames, per that method's own "re-fetch
    /// every frame" contract (a spawn/despawn/step since the last call
    /// can move or reallocate the array).
    ///
    /// Honest scaling note: the instanced path is what actually reaches
    /// the 100k-1M target — it's a handful of DrawMeshInstanced calls
    /// batched at Unity's real 1023-per-call limit, GPU-side per-instance
    /// data, no meaningful per-atom CPU cost beyond filling a matrix.
    /// The combined-mesh path rebuilds and re-uploads a full mesh from
    /// scratch every single frame — genuinely necessary for hardware that
    /// can't instance at all, but it will NOT hold up at six-figure atom
    /// counts the way the instanced path does; that's an inherent
    /// property of the fallback, not a bug in this implementation.
    /// </summary>
    [ExecuteAlways]
    public sealed class AtomRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private Material _material;
        [Tooltip("Optional. Defaults to a plain sphere (Unity's built-in primitive mesh) if left unassigned.")]
        [SerializeField] private Mesh _atomMeshOverride;
        [Tooltip("Force the combined-mesh fallback path even on instancing-capable hardware — useful for testing that path, or for deliberately matching a lower-end target device's behavior during development.")]
        [SerializeField] private bool _forceCombinedMesh;

        // ── Instanced-path scratch (fixed size — MaxBatchSize is Unity's own hard limit, not a growable buffer) ──
        private Matrix4x4[] _matrices;
        private Vector4[]   _instanceColors;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");

        // ── Combined-mesh path ──
        private Mesh _defaultSphereMesh;
        private Mesh _combinedMesh;

        // Cached source-mesh data — re-fetching Mesh.vertices/.normals/.triangles
        // does a full managed-array copy from native mesh data every call, so
        // this is cached once per source-mesh reference, not re-queried every
        // frame (a real, meaningful cost at six-figure atom counts, where the
        // combined-mesh path is already the slow path by nature).
        private Mesh      _cachedSourceMesh;
        private Vector3[] _srcVerts;
        private Vector3[] _srcNormals;
        private int[]     _srcTris;

        // Growable combined-mesh scratch — grown (doubled), never shrunk, so a
        // stable atom count (the common case frame-to-frame) causes zero GC
        // churn after the first few frames settle on a high-water mark.
        private Vector3[] _combinedVerts;
        private Vector3[] _combinedNormals;
        private Color32[] _combinedColors;
        private int[]     _combinedTris;

        private void Awake()
        {
            _defaultSphereMesh = AlembicMeshUtility.CreatePrimitiveMesh(PrimitiveType.Sphere, "AlembicAtoms_DefaultSphere");

            _matrices       = new Matrix4x4[InstancingSupport.MaxBatchSize];
            _instanceColors = new Vector4[InstancingSupport.MaxBatchSize];
            _mpb            = new MaterialPropertyBlock();

            _combinedMesh = new Mesh { name = "AlembicAtoms_Combined" };
            _combinedMesh.MarkDynamic();
            // 16-bit (UInt16) is Mesh's default, capping a single mesh at
            // 65535 vertices — trivially exceeded by a combined mesh of many
            // atoms even at a modest per-atom vertex count. UInt32 is
            // required here, not an optimization.
            _combinedMesh.indexFormat = IndexFormat.UInt32;
        }

        private void OnDestroy()
        {
            AlembicMeshUtility.DestroyMesh(_defaultSphereMesh);
            AlembicMeshUtility.DestroyMesh(_combinedMesh);
        }

        /// <summary>
        /// Draw every live atom in <paramref name="ctx"/>. Safe to call
        /// with a stale/zero context or zero atoms — both are silent
        /// no-ops, not errors, since a renderer polling every frame will
        /// legitimately hit both states often (before the sim starts,
        /// between despawn-all and the next spawn, etc.).
        /// </summary>
        public unsafe void Render(IntPtr ctx)
        {
            if (_material == null || ctx == IntPtr.Zero) return;

            int count = ChemistryLib.chem_atom_count(ctx);
            if (count <= 0) return;

            IntPtr arrPtr = ChemistryLib.chem_atoms_ptr(ctx);
            if (arrPtr == IntPtr.Zero) return;
            AtomState* atoms = (AtomState*)arrPtr;

            Mesh mesh = _atomMeshOverride != null ? _atomMeshOverride : _defaultSphereMesh;

            if (InstancingSupport.DecidePath(_forceCombinedMesh) == InstancingSupport.RenderPath.Instanced)
                RenderInstanced(atoms, count, mesh);
            else
                RenderCombined(atoms, count, mesh);
        }

        // ── Instanced path ──────────────────────────────────────────────────

        private unsafe void RenderInstanced(AtomState* atoms, int count, Mesh mesh)
        {
            int start = 0;
            while (start < count)
            {
                int end = Mathf.Min(start + InstancingSupport.MaxBatchSize, count);
                int n = 0;

                for (int i = start; i < end; i++)
                {
                    // pm -> Angstrom: AtomState.Radius is chemistry_core's
                    // radius_vdw_pm field, unconverted (see element_data.rs's
                    // make_atom) — but every position/length elsewhere in the
                    // simulation (LJ sigma, bond equilibrium length) is in
                    // Angstroms. 1 Angstrom = 100 picometers.
                    float radiusA = atoms[i].Radius / 100f;
                    float3 p = atoms[i].Position;

                    _matrices[n] = Matrix4x4.TRS(
                        new Vector3(p.x, p.y, p.z),
                        Quaternion.identity,
                        Vector3.one * (radiusA * 2f)); // *2: default sphere primitive has radius 0.5 (diameter 1) at scale 1

                    Color c = AlembicElementColors.GetColor(atoms[i].AtomicNumber);
                    _instanceColors[n] = new Vector4(c.r, c.g, c.b, c.a);
                    n++;
                }

                if (n > 0)
                {
                    _mpb.SetVectorArray(ColorPropId, _instanceColors);
                    Graphics.DrawMeshInstanced(
                        mesh, 0, _material, _matrices, n, _mpb,
                        ShadowCastingMode.On, receiveShadows: true, layer: gameObject.layer);
                }

                start = end;
            }
        }

        // ── Combined-mesh path ──────────────────────────────────────────────

        private unsafe void RenderCombined(AtomState* atoms, int count, Mesh sourceMesh)
        {
            EnsureSourceMeshCached(sourceMesh);

            int vertsPerAtom = _srcVerts.Length;
            int trisPerAtom  = _srcTris.Length;
            int neededVerts  = count * vertsPerAtom;
            int neededTris   = count * trisPerAtom;

            EnsureCombinedCapacity(neededVerts, neededTris);

            for (int i = 0; i < count; i++)
            {
                float radiusA = atoms[i].Radius / 100f;
                float3 pf = atoms[i].Position;
                Vector3 pos = new Vector3(pf.x, pf.y, pf.z);
                float scale = radiusA * 2f;
                Color32 col = AlembicElementColors.GetColor(atoms[i].AtomicNumber);

                int vBase = i * vertsPerAtom;
                for (int v = 0; v < vertsPerAtom; v++)
                {
                    _combinedVerts[vBase + v]   = pos + _srcVerts[v] * scale;
                    _combinedNormals[vBase + v] = _srcNormals[v]; // uniform scale + identity rotation: normals pass through unchanged
                    _combinedColors[vBase + v]  = col;
                }

                int tBase = i * trisPerAtom;
                for (int t = 0; t < trisPerAtom; t++)
                    _combinedTris[tBase + t] = _srcTris[t] + vBase;
            }

            _combinedMesh.Clear();
            _combinedMesh.SetVertices(_combinedVerts, 0, neededVerts);
            _combinedMesh.SetNormals(_combinedNormals, 0, neededVerts);
            _combinedMesh.SetColors(_combinedColors, 0, neededVerts);
            _combinedMesh.SetTriangles(_combinedTris, 0, neededTris, 0);
            // Fixed, deliberately huge bounds rather than a real per-frame
            // recompute — same trick MidManStudio_Unity's own combined-mesh
            // renderer uses (Graphics.DrawMesh does its own frustum-relevant
            // work; this just avoids Unity recomputing tight bounds from
            // scratch every frame for a mesh that's about to be replaced
            // again next frame anyway).
            _combinedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1_000_000f);

            Graphics.DrawMesh(_combinedMesh, Matrix4x4.identity, _material, gameObject.layer);
        }

        private void EnsureSourceMeshCached(Mesh sourceMesh)
        {
            if (_cachedSourceMesh == sourceMesh) return;
            _cachedSourceMesh = sourceMesh;
            _srcVerts   = sourceMesh.vertices;
            _srcNormals = sourceMesh.normals;
            _srcTris    = sourceMesh.triangles;
        }

        private void EnsureCombinedCapacity(int neededVerts, int neededTris)
        {
            if (_combinedVerts == null || _combinedVerts.Length < neededVerts)
            {
                int cap = _combinedVerts == null ? neededVerts : Math.Max(neededVerts, _combinedVerts.Length * 2);
                _combinedVerts   = new Vector3[cap];
                _combinedNormals = new Vector3[cap];
                _combinedColors  = new Color32[cap];
            }
            if (_combinedTris == null || _combinedTris.Length < neededTris)
            {
                int cap = _combinedTris == null ? neededTris : Math.Max(neededTris, _combinedTris.Length * 2);
                _combinedTris = new int[cap];
            }
        }

    }
}
