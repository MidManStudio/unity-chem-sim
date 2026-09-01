using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using MidManStudio.Alembic.Core;

namespace MidManStudio.Alembic.Rendering
{
    /// <summary>
    /// Renders every live angle triple in a chemistry_core context,
    /// colored by <see cref="AngleGeometry.DeltaRadians"/> (how far the
    /// current angle sits from its equilibrium) — the angle-side sibling
    /// of <see cref="BondRenderer"/>'s strain coloring. Same dual-path
    /// (GPU instanced / combined-mesh) design and the same
    /// <see cref="InstancingSupport"/> decision logic as
    /// <see cref="AtomRenderer"/>/<see cref="BondRenderer"/> — but the two
    /// paths deliberately draw <b>different geometry</b> here, which
    /// neither sibling renderer does, for a real geometric reason
    /// explained below.
    ///
    /// <b>No dedup needed, unlike BondRenderer.</b> Bonds are stored
    /// symmetrically (both atoms in a pair hold an entry for the same
    /// edge), so BondRenderer has to skip half of what it walks to avoid
    /// drawing every edge twice. Angle triples are stored only on their
    /// <i>vertex</i> atom (see chemistry_core's own AngleInfo doc) — never
    /// on the two arm atoms — so walking every live atom's own angle list
    /// visits each real triple exactly once. There is no equivalent of
    /// BondRenderer's <c>partner.Index &lt; handle.Index</c> check here
    /// because there is nothing to deduplicate.
    ///
    /// <b>Why the two paths draw different shapes.</b> A bond is a
    /// cylinder between two points — any bond, any length, any
    /// orientation, is just one rigid-body transform (translate/rotate/
    /// scale) away from the same unit cylinder, which is exactly what
    /// makes GPU instancing a bond trivial: one shared mesh, one TRS
    /// matrix per instance. An angle's visual "sweep" has no such
    /// property — a 20-degree wedge and a 170-degree wedge are genuinely
    /// different shapes, not affine transforms of the same one, so they
    /// can't share a single instanced mesh the way two bonds of different
    /// lengths can share a cylinder. The honest options were: (a) a
    /// custom per-instance shader parameter with fragment-level angular
    /// clipping, or (b) accept that the instanced path draws something
    /// simpler than a true sweep. (a) means writing and trusting new
    /// shader logic before the *existing* shader has even had its first
    /// real Editor/URP compile (see the Rendering module's own outstanding
    /// verification item) — compounding one unverified risk on top of
    /// another, at exactly the wrong moment. So this takes (b):
    /// <list type="bullet">
    /// <item><b>Instanced path:</b> a small marker sphere at the vertex,
    /// colored by delta — true GPU instancing, reuses the exact same
    /// primitive-mesh-and-MaterialPropertyBlock machinery
    /// <see cref="AtomRenderer"/> already uses, zero new shader risk. Marks
    /// *that* a bend is strained and *how much*, not its literal sweep.</item>
    /// <item><b>Combined-mesh path:</b> a real per-triple double-sided fan
    /// wedge, built fresh from the vertex/arm positions via
    /// <see cref="Vector3.Slerp"/> between the two arm directions — genuine
    /// sweep geometry, because this path was never constrained to one
    /// shared mesh in the first place (see <see cref="BondRenderer"/>'s own
    /// combined-mesh path for the analogous "rebuild every frame,
    /// necessarily slower, but under no rigid-instancing constraint"
    /// tradeoff).</item>
    /// </list>
    /// A future pass could close this gap with a proper per-instance
    /// angular-clip shader technique once the base shader has had its own
    /// first real Editor verification — flagged, not built speculatively.
    /// </summary>
    [ExecuteAlways]
    public sealed class AngleRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private Material _material;
        [Tooltip("Combined-mesh path only. Number of triangles in each wedge's arc — higher looks smoother, costs more per triple. 8 is a reasonable default; this isn't meaningful for the instanced path's marker sphere.")]
        [SerializeField] private int _wedgeSegments = 8;
        [SerializeField] private bool _forceCombinedMesh;
        [Tooltip("How far the wedge (combined-mesh path) or marker (instanced path) extends from the vertex, in Angstroms — same position-space units as the simulation itself. Automatically clamped shorter if either arm's real bond is shorter than this, so the wedge never visually pokes past a bonded atom.")]
        [SerializeField] private float _markerRadius = 0.35f;

        [Header("Delta Coloring")]
        [Tooltip("Color when current angle is at/near its equilibrium.")]
        [SerializeField] private Color _relaxedColor    = new Color(0.85f, 0.85f, 0.85f, 1f);
        [Tooltip("Color when the current angle is narrower than equilibrium.")]
        [SerializeField] private Color _compressedColor = new Color(0.25f, 0.45f, 1f, 1f);
        [Tooltip("Color when the current angle is wider than equilibrium.")]
        [SerializeField] private Color _stretchedColor  = new Color(1f, 0.25f, 0.15f, 1f);
        [Tooltip("Delta magnitude (radians) that maps to fully-saturated compressed/stretched color. Tune in Editor — there's no single physically-correct value, it's a visualization choice, same status BondRenderer's own _strainColorScale has.")]
        [SerializeField] private float _deltaColorScale = 0.35f;

        // ── Instanced-path scratch (marker sphere) ──
        private Matrix4x4[] _matrices;
        private Vector4[]   _instanceColors;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private Mesh _markerSphereMesh;

        // ── Combined-mesh path (real per-triple wedge fan) ──
        private Mesh _combinedMesh;
        private Vector3[] _combinedVerts;
        private Vector3[] _combinedNormals;
        private Color32[] _combinedColors;
        private int[]     _combinedTris;

        // Growable per-triple scratch, collected before baking — same
        // two-phase "collect, then bake" shape BondRenderer's combined-mesh
        // path already uses, and for the same reason: the number of live
        // triples this frame isn't known up front the way atom count is.
        private Vector3[] _vertexScratch;
        private Vector3[] _dirAScratch;
        private Vector3[] _dirBScratch;
        private float[]   _radiusScratch;
        private Color32[] _colorScratch;

        private void Awake()
        {
            _markerSphereMesh = AlembicMeshUtility.CreatePrimitiveMesh(PrimitiveType.Sphere, "AlembicAngles_MarkerSphere");

            _matrices       = new Matrix4x4[InstancingSupport.MaxBatchSize];
            _instanceColors = new Vector4[InstancingSupport.MaxBatchSize];
            _mpb            = new MaterialPropertyBlock();

            _combinedMesh = new Mesh { name = "AlembicAngles_Combined" };
            _combinedMesh.MarkDynamic();
            _combinedMesh.indexFormat = IndexFormat.UInt32;
        }

        private void OnDestroy()
        {
            AlembicMeshUtility.DestroyMesh(_markerSphereMesh);
            AlembicMeshUtility.DestroyMesh(_combinedMesh);
        }

        /// <summary>
        /// Draw every live angle triple in <paramref name="ctx"/>. Safe to
        /// call with a stale/zero context or zero atoms/triples — all
        /// silent no-ops, same contract <see cref="AtomRenderer.Render"/>/
        /// <see cref="BondRenderer.Render"/> already document.
        /// </summary>
        public unsafe void Render(IntPtr ctx)
        {
            if (_material == null || ctx == IntPtr.Zero) return;

            int atomCount = ChemistryLib.chem_atom_count(ctx);
            if (atomCount <= 0) return;

            IntPtr handlesPtr = ChemistryLib.chem_handles_ptr(ctx);
            if (handlesPtr == IntPtr.Zero) return;
            AtomHandle* handles = (AtomHandle*)handlesPtr;

            if (InstancingSupport.DecidePath(_forceCombinedMesh) == InstancingSupport.RenderPath.Instanced)
                RenderInstanced(ctx, handles, atomCount);
            else
                RenderCombined(ctx, handles, atomCount);
        }

        private Color ColorForDelta(float deltaRadians)
        {
            float t = Mathf.Clamp(deltaRadians / Mathf.Max(_deltaColorScale, 1e-5f), -1f, 1f);
            return t < 0f
                ? Color.Lerp(_relaxedColor, _compressedColor, -t)
                : Color.Lerp(_relaxedColor, _stretchedColor, t);
        }

        /// <summary>
        /// Shared setup for both paths: resolve one triple's vertex/arm
        /// positions and reduce them to the (vertex position, unit arm
        /// directions, effective radius, color) tuple both a marker and a
        /// wedge fan are built from. Returns false (out params
        /// unspecified, caller must skip) for a handful of degenerate
        /// cases:
        /// <list type="bullet">
        /// <item>Either arm atom failed to resolve (shouldn't happen —
        /// chemistry_core keeps triples in sync with live bonds — but a
        /// renderer polling every frame shouldn't trust that invariant
        /// blindly over an FFI boundary.)</item>
        /// <item>Either arm sits (numerically) on top of the vertex — no
        /// meaningful direction to compute.</item>
        /// <item>The two arms are within ~2.6 degrees of exactly
        /// collinear (<c>cos(theta) &lt; -0.999</c>) — <see cref="Vector3.Slerp"/>'s
        /// choice of interpolation axis for near-antiparallel inputs isn't
        /// well-defined, the same reason chemistry_core's own
        /// <c>compute_angles</c> skips applying force to a triple this
        /// close to the singularity, just checked here in C# rather than
        /// shared code with that Rust guard.</item>
        /// </list>
        /// </summary>
        private bool TryResolveTriple(
            IntPtr ctx, AtomHandle vertex, AtomHandle armA, AtomHandle armB, AngleGeometry geom,
            out Vector3 vertexPos, out Vector3 dirA, out Vector3 dirB, out float radius, out Color32 color)
        {
            vertexPos = default; dirA = default; dirB = default; radius = default; color = default;

            if (!ChemistryLib.TryGetAtom(ctx, vertex, out AtomState stateV)) return false;
            if (!ChemistryLib.TryGetAtom(ctx, armA, out AtomState stateA)) return false;
            if (!ChemistryLib.TryGetAtom(ctx, armB, out AtomState stateB)) return false;

            float3 pv3 = stateV.Position, pa3 = stateA.Position, pb3 = stateB.Position;
            Vector3 pv = new Vector3(pv3.x, pv3.y, pv3.z);
            Vector3 pa = new Vector3(pa3.x, pa3.y, pa3.z);
            Vector3 pb = new Vector3(pb3.x, pb3.y, pb3.z);

            Vector3 toA = pa - pv;
            Vector3 toB = pb - pv;
            float lenA = toA.magnitude;
            float lenB = toB.magnitude;
            if (lenA < 1e-6f || lenB < 1e-6f) return false;

            Vector3 unitA = toA / lenA;
            Vector3 unitB = toB / lenB;
            if (Vector3.Dot(unitA, unitB) < -0.999f) return false; // near-collinear — see doc above

            float effectiveRadius = Mathf.Min(_markerRadius, lenA * 0.9f, lenB * 0.9f);
            if (effectiveRadius < 1e-4f) return false; // arms too short to draw anything meaningful

            vertexPos = pv;
            dirA = unitA;
            dirB = unitB;
            radius = effectiveRadius;
            color = ColorForDelta(geom.DeltaRadians);
            return true;
        }

        // ── Instanced path (marker sphere) ──────────────────────────────────

        private unsafe void RenderInstanced(IntPtr ctx, AtomHandle* handles, int atomCount)
        {
            int n = 0;
            for (int i = 0; i < atomCount; i++)
            {
                AtomHandle vertex = handles[i];
                int angleCount = ChemistryLib.chem_angle_count(ctx, vertex);
                for (int k = 0; k < angleCount; k++)
                {
                    if (!ChemistryLib.TryGetAngleArms(ctx, vertex, k, out AtomHandle armA, out AtomHandle armB)) continue;
                    if (!ChemistryLib.TryGetAngleGeometry(ctx, vertex, k, out AngleGeometry geom)) continue;
                    if (!TryResolveTriple(ctx, vertex, armA, armB, geom, out Vector3 pv, out _, out _, out float radius, out Color32 col))
                        continue;

                    _matrices[n] = Matrix4x4.TRS(pv, Quaternion.identity, Vector3.one * (radius * 2f)); // *2: default sphere primitive has radius 0.5 (diameter 1) at scale 1
                    _instanceColors[n] = new Vector4(col.r / 255f, col.g / 255f, col.b / 255f, col.a / 255f);
                    n++;

                    if (n == InstancingSupport.MaxBatchSize)
                    {
                        FlushInstancedBatch(n);
                        n = 0;
                    }
                }
            }
            if (n > 0) FlushInstancedBatch(n);
        }

        private void FlushInstancedBatch(int n)
        {
            _mpb.SetVectorArray(ColorPropId, _instanceColors);
            Graphics.DrawMeshInstanced(
                _markerSphereMesh, 0, _material, _matrices, n, _mpb,
                ShadowCastingMode.On, receiveShadows: true, layer: gameObject.layer);
        }

        // ── Combined-mesh path (real per-triple wedge fan) ──────────────────

        private unsafe void RenderCombined(IntPtr ctx, AtomHandle* handles, int atomCount)
        {
            EnsureTripleScratchCapacity(atomCount * 2); // 2 as a starting guess — grows below if wrong, never shrinks
            int tripleN = 0;

            for (int i = 0; i < atomCount; i++)
            {
                AtomHandle vertex = handles[i];
                int angleCount = ChemistryLib.chem_angle_count(ctx, vertex);
                for (int k = 0; k < angleCount; k++)
                {
                    if (!ChemistryLib.TryGetAngleArms(ctx, vertex, k, out AtomHandle armA, out AtomHandle armB)) continue;
                    if (!ChemistryLib.TryGetAngleGeometry(ctx, vertex, k, out AngleGeometry geom)) continue;
                    if (!TryResolveTriple(ctx, vertex, armA, armB, geom, out Vector3 pv, out Vector3 dirA, out Vector3 dirB, out float radius, out Color32 col))
                        continue;

                    if (tripleN >= _vertexScratch.Length) GrowTripleScratch();
                    _vertexScratch[tripleN] = pv;
                    _dirAScratch[tripleN]   = dirA;
                    _dirBScratch[tripleN]   = dirB;
                    _radiusScratch[tripleN] = radius;
                    _colorScratch[tripleN]  = col;
                    tripleN++;
                }
            }

            // Per-wedge vertex/triangle count is fixed once _wedgeSegments
            // is set (only the positions vary per triple, not the count) —
            // same "known shape, unknown instance count" capacity-planning
            // shape BondRenderer's combined-mesh path already uses.
            // Double-sided: front fan (vertex + segments+1 arc points) plus
            // an identical back copy with reversed winding and negated
            // normal, so the wedge stays visible regardless of which side
            // the camera is on — see class doc for why this can't just
            // reuse a cached, TRS-scaled template the way Bond/AtomRenderer
            // do.
            int segments = Mathf.Max(_wedgeSegments, 1);
            int vertsPerSide  = segments + 2;
            int vertsPerWedge = vertsPerSide * 2;
            int trisPerSide   = segments;
            int trisPerWedge  = trisPerSide * 2;

            int neededVerts = tripleN * vertsPerWedge;
            int neededTris  = tripleN * trisPerWedge * 3; // *3: SetTriangles wants a flat index list, not triangle count
            EnsureCombinedMeshCapacity(neededVerts, neededTris);

            for (int i = 0; i < tripleN; i++)
            {
                BuildWedge(
                    _vertexScratch[i], _dirAScratch[i], _dirBScratch[i], _radiusScratch[i], _colorScratch[i],
                    segments, i * vertsPerWedge, i * trisPerWedge * 3);
            }

            _combinedMesh.Clear();
            if (tripleN > 0)
            {
                _combinedMesh.SetVertices(_combinedVerts, 0, neededVerts);
                _combinedMesh.SetNormals(_combinedNormals, 0, neededVerts);
                _combinedMesh.SetColors(_combinedColors, 0, neededVerts);
                _combinedMesh.SetTriangles(_combinedTris, 0, neededTris, 0);
                _combinedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1_000_000f);
                Graphics.DrawMesh(_combinedMesh, Matrix4x4.identity, _material, gameObject.layer);
            }
        }

        /// <summary>
        /// Writes one double-sided wedge fan directly into
        /// <see cref="_combinedVerts"/>/<see cref="_combinedNormals"/>/
        /// <see cref="_combinedColors"/>/<see cref="_combinedTris"/> at the
        /// given offsets. <paramref name="dirA"/>/<paramref name="dirB"/>
        /// must already be unit length (see <see cref="TryResolveTriple"/>) —
        /// <see cref="Vector3.Slerp"/> keeps the interpolated direction
        /// unit length too as long as both inputs are, which is what keeps
        /// every arc point at exactly <paramref name="radius"/> from
        /// <paramref name="vertex"/> without an extra per-point
        /// renormalize.
        /// </summary>
        private void BuildWedge(
            Vector3 vertex, Vector3 dirA, Vector3 dirB, float radius, Color32 color,
            int segments, int vBase, int tBase)
        {
            Vector3 normal = Vector3.Cross(dirA, dirB).normalized;
            int vertsPerSide = segments + 2;

            // Front side: local index 0 = vertex, 1..segments+1 = arc points A -> B.
            _combinedVerts[vBase] = vertex;
            _combinedNormals[vBase] = normal;
            _combinedColors[vBase] = color;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                Vector3 dir = Vector3.Slerp(dirA, dirB, t);
                int vi = vBase + 1 + s;
                _combinedVerts[vi] = vertex + dir * radius;
                _combinedNormals[vi] = normal;
                _combinedColors[vi] = color;
            }
            for (int s = 0; s < segments; s++)
            {
                int ti = tBase + s * 3;
                _combinedTris[ti + 0] = vBase;
                _combinedTris[ti + 1] = vBase + 1 + s;
                _combinedTris[ti + 2] = vBase + 2 + s;
            }

            // Back side: identical positions, negated normal, reversed
            // winding so it faces the opposite direction.
            int backBase = vBase + vertsPerSide;
            int backTBase = tBase + segments * 3;
            _combinedVerts[backBase] = vertex;
            _combinedNormals[backBase] = -normal;
            _combinedColors[backBase] = color;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                Vector3 dir = Vector3.Slerp(dirA, dirB, t);
                int vi = backBase + 1 + s;
                _combinedVerts[vi] = vertex + dir * radius;
                _combinedNormals[vi] = -normal;
                _combinedColors[vi] = color;
            }
            for (int s = 0; s < segments; s++)
            {
                int ti = backTBase + s * 3;
                _combinedTris[ti + 0] = backBase;
                _combinedTris[ti + 1] = backBase + 2 + s;
                _combinedTris[ti + 2] = backBase + 1 + s;
            }
        }

        private void EnsureTripleScratchCapacity(int needed)
        {
            if (_vertexScratch == null || _vertexScratch.Length < needed)
            {
                _vertexScratch = new Vector3[needed];
                _dirAScratch   = new Vector3[needed];
                _dirBScratch   = new Vector3[needed];
                _radiusScratch = new float[needed];
                _colorScratch  = new Color32[needed];
            }
        }

        private void GrowTripleScratch()
        {
            int newCap = Math.Max(_vertexScratch.Length * 2, 16);
            Array.Resize(ref _vertexScratch, newCap);
            Array.Resize(ref _dirAScratch, newCap);
            Array.Resize(ref _dirBScratch, newCap);
            Array.Resize(ref _radiusScratch, newCap);
            Array.Resize(ref _colorScratch, newCap);
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
