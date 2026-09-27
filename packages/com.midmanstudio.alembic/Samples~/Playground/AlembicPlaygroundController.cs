// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.alembic.md, section
// "Samples~/Playground/AlembicPlaygroundController.cs"
// ============================================================================
// AlembicPlaygroundController.cs
//
// The missing piece between "the FFI layer and renderers all compile" and
// "you can actually press Play and watch atoms move": owns one
// chemistry_core context for its GameObject's lifetime, spawns whatever
// AlembicPlaygroundConfig describes, steps the sim every frame, and hands
// the live context pointer to whichever Rendering.*Renderer components are
// assigned. Every other script in this sample either feeds this one
// (AlembicPlaygroundConfig) or drives it (AlembicPlaygroundHud) — this is
// the one actually touching ChemistryLib's context/spawn/step/destroy calls.
//
// Deliberately NOT [ExecuteAlways] the way the renderers are — a live
// native SimContext tied to Edit Mode's own reload/domain-reload cycle is
// a real footgun (a script recompile while a context is alive would leak
// the native allocation with nothing left holding the pointer to free it).
// Play Mode only, on purpose.

using System;
using System.Collections.Generic;
using UnityEngine;
using MidManStudio.Alembic.Core;
using MidManStudio.Alembic.Rendering;

namespace MidManStudio.Alembic.Samples.Playground
{
    public sealed class AlembicPlaygroundController : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Optional. Leave unassigned to run with this component's own built-in " +
                 "fallback scenario (a small random H/O cloud) — useful for a zero-setup " +
                 "'press Play and see something' first run before you make your own asset.")]
        [SerializeField] private AlembicPlaygroundConfig _config;

        [Header("Rendering (assign at least one to see anything)")]
        [SerializeField] private AtomRenderer _atomRenderer;
        [SerializeField] private BondRenderer _bondRenderer;
        [Tooltip("Optional — angle visualization is the least load-bearing of the three for " +
                 "just eyeballing whether the sim looks right.")]
        [SerializeField] private AngleRenderer _angleRenderer;

        // ── Runtime state (copied out of _config at spawn time, so live HUD ──
        // ── tweaks during Play Mode never permanently mutate the shared asset) ──
        private float _dtFemtoseconds;
        private int   _stepsPerFrame;
        private float _cutoffAngstrom;
        private float _temperatureK;

        private IntPtr _ctx;
        private bool   _running;
        private bool   _stepOnce;

        /// <summary>Read-only access to the assigned config, mainly so AlembicPlaygroundHud can read the quick-spawn palette without a second duplicate Inspector reference. May be null (the built-in fallback scenario).</summary>
        public AlembicPlaygroundConfig Config => _config;

        /// <summary>Live native context pointer — IntPtr.Zero before Start/after a failed init. HUD reads this for atom/bond-count/temperature queries.</summary>
        public IntPtr Context => _ctx;
        public bool IsRunning => _running;
        public float DtFemtoseconds { get => _dtFemtoseconds; set => _dtFemtoseconds = Mathf.Max(0.0001f, value); }
        public int StepsPerFrame { get => _stepsPerFrame; set => _stepsPerFrame = Mathf.Clamp(value, 1, 64); }

        private void Start()
        {
            if (!ChemistryLib.IsAvailable)
            {
                // ChemistryLib.IsAvailable already logs the specific
                // DllNotFoundException/EntryPointNotFoundException/
                // BadImageFormatException reason — nothing more useful to
                // add here beyond disabling ourselves cleanly instead of
                // throwing out of Start.
                enabled = false;
                return;
            }

            try
            {
                ChemistryLib.ValidateStructSizes();
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogError($"[AlembicPlaygroundController] Struct layout mismatch, refusing to start: {ex.Message}");
                enabled = false;
                return;
            }

            InitializeFromConfig();
        }

        private void InitializeFromConfig()
        {
            _dtFemtoseconds = _config != null ? _config.DtFemtoseconds : 1f;
            _stepsPerFrame  = _config != null ? _config.StepsPerFrame  : 4;
            _cutoffAngstrom = _config != null ? _config.CutoffAngstrom : 10f;
            _temperatureK   = _config != null ? _config.TemperatureK  : 300f;
            _running        = _config != null ? _config.RunOnStart    : true;

            _ctx = ChemistryLib.chem_context_create(_cutoffAngstrom);

            RegisterConfiguredCustomElements();
            SpawnInitialAtoms();

            uint seed = _config != null ? _config.Seed : 42u;
            ChemistryLib.chem_init(_ctx, _temperatureK, seed);
        }

        private void RegisterConfiguredCustomElements()
        {
            if (_config == null) return;
            foreach (var def in _config.CustomElements)
            {
                if (!ChemistryLib.RegisterElement(def))
                {
                    Debug.LogWarning(
                        $"[AlembicPlaygroundController] RegisterElement rejected custom element " +
                        $"Z={def.AtomicNumber} — atomic number must be >= 1000 and mass/radius/sigma " +
                        "non-negative. Skipped.");
                }
            }
        }

        private void SpawnInitialAtoms()
        {
            if (_config != null && _config.ExplicitAtoms.Count > 0)
            {
                foreach (var entry in _config.ExplicitAtoms)
                    TrySpawn(entry.AtomicNumber, entry.Position);
                return;
            }

            SpawnRandomCloud();
        }

        /// <summary>
        /// Random-cloud fallback — also what runs with no _config assigned
        /// at all (built-in default: a 2:1 H:O cloud, small enough to be an
        /// instant, obvious "yes, something is happening" first run).
        ///
        /// Deterministic and non-overlapping: a candidate position is only
        /// accepted once it clears chemistry_core's own LJ equilibrium
        /// separation (ChemistryLib.chem_bond_r_min) from every atom already
        /// placed this pass, scaled by CloudMinSpacingFactor. Placing a pair
        /// closer than that starts them deep inside the LJ potential's
        /// repulsive core — reads as a violent explosion the instant
        /// stepping begins, not a molecule assembling, since the default
        /// CloudAtomCount/CloudRadius pack atoms closer on average than a
        /// typical r_min. See docs/com.midmanstudio.alembic.md, this file's
        /// section, for the full diagnosis this replaced.
        ///
        /// Previously plain UnityEngine.Random.insideUnitSphere with no
        /// spacing check and no seed tie — positions came from Unity's
        /// global RNG regardless of AlembicPlaygroundConfig.Seed (only
        /// chem_init's velocity draw was ever seeded). Reset (R) now
        /// reproduces the exact same layout for the same Seed.
        /// </summary>
        private void SpawnRandomCloud()
        {
            int[] pool = (_config != null && _config.CloudElementPool != null && _config.CloudElementPool.Length > 0)
                ? _config.CloudElementPool
                : new[] { 1, 1, 8 };
            int   count            = _config != null ? _config.CloudAtomCount        : 40;
            float radius           = _config != null ? _config.CloudRadius           : 6f;
            float minSpacingFactor = _config != null ? _config.CloudMinSpacingFactor : 1f;
            uint  seed             = _config != null ? _config.Seed                  : 42u;

            // r_min doesn't depend on any spawned atom or context state —
            // precompute it once per distinct pair the pool can produce
            // instead of a P/Invoke call per rejection-sampling attempt
            // below (this loop can retry many times per atom under a
            // dense/small-radius config).
            var distinctZ = new List<int>();
            foreach (int z in pool)
                if (!distinctZ.Contains(z)) distinctZ.Add(z);

            var minSpacing = new Dictionary<(int, int), float>();
            foreach (int a in distinctZ)
            {
                foreach (int b in distinctZ)
                {
                    if (a > b) continue; // symmetric — store one ordering only
                    minSpacing[(a, b)] = ChemistryLib.chem_bond_r_min(a, b) * minSpacingFactor;
                }
            }

            float MinSpacingFor(int a, int b) => a <= b ? minSpacing[(a, b)] : minSpacing[(b, a)];

            // Local, seeded RNG — not UnityEngine.Random — so this method
            // depends only on Seed, never on whatever else in the scene
            // happened to draw from Unity's global RNG this frame.
            var rng              = new System.Random(unchecked((int)seed)); // wraps for seed > int.MaxValue, still deterministic
            var placedPositions  = new List<Vector3>(count);
            var placedElements   = new List<int>(count);

            const int kMaxAttemptsPerAtom = 64;
            int skipped = 0;

            for (int i = 0; i < count; i++)
            {
                int  z      = pool[rng.Next(pool.Length)];
                bool placed = false;

                for (int attempt = 0; attempt < kMaxAttemptsPerAtom; attempt++)
                {
                    Vector3 candidate = RandomPointInSphere(rng, radius);

                    bool tooClose = false;
                    for (int j = 0; j < placedPositions.Count; j++)
                    {
                        float need = MinSpacingFor(z, placedElements[j]);
                        if ((candidate - placedPositions[j]).sqrMagnitude < need * need)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (tooClose) continue;

                    placedPositions.Add(candidate);
                    placedElements.Add(z);
                    TrySpawn(z, transform.position + candidate);
                    placed = true;
                    break;
                }

                if (!placed) skipped++;
            }

            if (skipped > 0)
            {
                Debug.LogWarning(
                    $"[AlembicPlaygroundController] Cloud spawn: {skipped}/{count} atoms skipped — " +
                    $"couldn't find a slot clearing CloudMinSpacingFactor ({minSpacingFactor:0.##}x r_min) " +
                    $"from every already-placed atom within {kMaxAttemptsPerAtom} attempts. Raise " +
                    "CloudRadius, lower CloudAtomCount, or lower CloudMinSpacingFactor if this scenario " +
                    "genuinely needs a tighter starting pack.");
            }
        }

        /// <summary>
        /// Uniform-in-sphere sampling via cube rejection — reject and retry
        /// until a cube sample also lands inside the sphere. Same
        /// distribution UnityEngine.Random.insideUnitSphere produces, just
        /// driven by a local seeded System.Random instead of Unity's global
        /// RNG, so SpawnRandomCloud stays fully deterministic per Seed.
        /// Acceptance rate is sphere-volume-over-cube (~52%), so this
        /// practically always exits in a handful of iterations — the 32
        /// cap only guards the theoretical non-terminating case.
        /// </summary>
        private static Vector3 RandomPointInSphere(System.Random rng, float radius)
        {
            for (int i = 0; i < 32; i++)
            {
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                float y = (float)(rng.NextDouble() * 2.0 - 1.0);
                float z = (float)(rng.NextDouble() * 2.0 - 1.0);
                if (x * x + y * y + z * z <= 1f)
                    return new Vector3(x, y, z) * radius;
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Spawn one atom, skipping (with a console warning, not a silent
        /// ghost) any atomic number that isn't a real element or a
        /// previously-registered custom one. Guards against the exact
        /// footgun element_data.rs's own params() takes on faith isn't a
        /// caller's problem: an unregistered Z spawns with mass/radius
        /// silently defaulted to zero (Rust-side unwrap_or_default(), not a
        /// panic and not rejected) rather than being refused outright —
        /// invisible in the sim, indistinguishable from a real bug, exactly
        /// the kind of thing a test playground shouldn't ever demonstrate by
        /// accident.
        /// </summary>
        public void TrySpawn(int atomicNumber, Vector3 position)
        {
            if (_ctx == IntPtr.Zero) return;

            if (!ChemistryLib.IsElementRegistered(atomicNumber))
            {
                Debug.LogWarning(
                    $"[AlembicPlaygroundController] Atomic number {atomicNumber} isn't a real " +
                    "element (1-118) or a registered custom one — chemistry_core would spawn it " +
                    "with mass/radius silently defaulted to zero rather than reject it, so this " +
                    "spawn was skipped instead. Add it to the config's CustomElements list first " +
                    "if it's meant to be a fictional reagent.");
                return;
            }

            ChemistryLib.chem_spawn_atom(_ctx, atomicNumber, position.x, position.y, position.z);
        }

        private void Update()
        {
            if (_ctx == IntPtr.Zero) return;

            if (_running || _stepOnce)
            {
                int steps = _stepOnce ? 1 : _stepsPerFrame;
                for (int i = 0; i < steps; i++)
                    ChemistryLib.chem_step(_ctx, _dtFemtoseconds, _cutoffAngstrom);
                _stepOnce = false;
            }
        }

        private void LateUpdate()
        {
            if (_ctx == IntPtr.Zero) return;

            // Rendered every frame regardless of _running — a paused sim
            // should still be visible, just frozen, not blanked.
            if (_atomRenderer  != null) _atomRenderer.Render(_ctx);
            if (_bondRenderer  != null) _bondRenderer.Render(_ctx);
            if (_angleRenderer != null) _angleRenderer.Render(_ctx);
        }

        private void OnDestroy()
        {
            if (_ctx == IntPtr.Zero) return;
            ChemistryLib.chem_context_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }

        // ── HUD-facing controls ─────────────────────────────────────────────

        public void TogglePause() => _running = !_running;
        public void RequestSingleStep() => _stepOnce = true;

        /// <summary>Re-randomize velocities at the configured temperature without touching positions — a quick "reheat" to see the current layout respond differently.</summary>
        public void Reheat()
        {
            if (_ctx == IntPtr.Zero) return;
            uint seed = (uint)UnityEngine.Random.Range(0, int.MaxValue); // non-negative range -- safe to widen even under a checked build config
            ChemistryLib.chem_init(_ctx, _temperatureK, seed);
        }

        /// <summary>
        /// Full reset: destroys and recreates the context, then reruns the
        /// configured spawn. Simpler and more obviously correct than
        /// despawning every live atom by hand (there's no bulk despawn in
        /// the FFI surface, only per-handle) — a fresh context is cheap.
        /// </summary>
        public void ResetScenario()
        {
            if (_ctx != IntPtr.Zero)
                ChemistryLib.chem_context_destroy(_ctx);
            InitializeFromConfig();
        }

        public void SetTemperatureK(float kelvin) => _temperatureK = Mathf.Max(0f, kelvin);
        public float TemperatureK => _temperatureK;
    }
}
