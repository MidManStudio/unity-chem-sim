// AlembicPlaygroundHud.cs
//
// Everything you actually poke at while eyeballing the sim: readouts
// (atom/bond count, temperature, kinetic energy), transport controls
// (pause/step/reheat/reset), live dt/steps-per-frame sliders, and
// number-key quick-spawn at the camera's forward point. Talks to
// AlembicPlaygroundController's public API only — never touches
// ChemistryLib directly except for the couple of read-only queries
// (atom/bond count, temperature, KE) that don't belong on the controller's
// own already-busy public surface.
//
// Plain OnGUI, not uGUI — zero scene setup beyond "this component exists
// somewhere," which matters for a drop-in test tool more than OnGUI's
// well-known perf cost ever will at these atom counts.

using System;
using UnityEngine;
using MidManStudio.Alembic.Core;

namespace MidManStudio.Alembic.Samples.Playground
{
    public sealed class AlembicPlaygroundHud : MonoBehaviour
    {
        [SerializeField] private AlembicPlaygroundController _controller;

        [Header("Quick-spawn fallback")]
        [Tooltip("Used only if _controller has no Config assigned — same shape as " +
                 "AlembicPlaygroundConfig.QuickSpawnElements.")]
        [SerializeField] private int[] _fallbackQuickSpawn = { 1, 6, 7, 8, 26, 79 };
        [SerializeField] private float _fallbackQuickSpawnDistance = 5f;

        [Header("Layout")]
        [SerializeField] private Vector2 _panelPosition = new Vector2(12f, 12f);
        [SerializeField] private float _panelWidth = 340f;

        private void Reset()
        {
            _controller = GetComponent<AlembicPlaygroundController>();
        }

        private void Update()
        {
            if (_controller == null) return;
            HandleInput();
        }

        private void HandleInput()
        {
            // Gated on the symbol Unity itself defines when the legacy
            // Input Manager backend is active (Project Settings > Active
            // Input Handling = "Input Manager (Old)" or "Both") — a
            // project running the new Input System exclusively simply
            // won't compile UnityEngine.Input calls at all, and this
            // sample has no business forcing that project-wide setting
            // choice on whoever drops it in.
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) _controller.TogglePause();
            if (Input.GetKeyDown(KeyCode.Period)) _controller.RequestSingleStep();
            if (Input.GetKeyDown(KeyCode.R)) _controller.ResetScenario();
            if (Input.GetKeyDown(KeyCode.T)) _controller.Reheat();

            int[] palette = (_controller.Config != null && _controller.Config.QuickSpawnElements != null)
                ? _controller.Config.QuickSpawnElements
                : _fallbackQuickSpawn;
            float spawnDistance = _controller.Config != null
                ? _controller.Config.QuickSpawnDistance
                : _fallbackQuickSpawnDistance;

            int slots = Mathf.Min(9, palette.Length);
            for (int i = 0; i < slots; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    SpawnAtCameraForward(palette[i], spawnDistance);
            }
#endif
        }

        private void SpawnAtCameraForward(int atomicNumber, float distance)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[AlembicPlaygroundHud] No Camera.main found — can't resolve a quick-spawn point.");
                return;
            }
            Vector3 pos = cam.transform.position + cam.transform.forward * distance;
            _controller.TrySpawn(atomicNumber, pos);
        }

        private void OnGUI()
        {
            if (_controller == null)
            {
                GUI.Label(new Rect(_panelPosition.x, _panelPosition.y, _panelWidth, 24f),
                    "AlembicPlaygroundHud: no controller assigned.");
                return;
            }

            GUILayout.BeginArea(new Rect(_panelPosition.x, _panelPosition.y, _panelWidth, 420f), GUI.skin.box);
            GUILayout.Label("<b>Alembic Playground</b>", RichLabel());

            DrawReadouts();
            GUILayout.Space(6f);
            DrawTransportControls();
            GUILayout.Space(6f);
            DrawLiveSliders();
            GUILayout.Space(6f);
            DrawQuickSpawnLegend();

            GUILayout.EndArea();
        }

        private void DrawReadouts()
        {
            IntPtr ctx = _controller.Context;
            if (ctx == IntPtr.Zero)
            {
                GUILayout.Label("Not running (see Console for why).");
                return;
            }

            int atomCount = ChemistryLib.chem_atom_count(ctx);
            ChemistryLib.chem_bonds_ptr(ctx, out int bondCount); // count only -- pointer intentionally unused/undereferenced here
            float temperature = ChemistryLib.chem_temperature(ctx);
            float ke = ChemistryLib.chem_kinetic_energy(ctx);

            GUILayout.Label($"Atoms: {atomCount}   Bonds: {bondCount}");
            GUILayout.Label($"Temperature: {temperature:F1} K   KE: {ke:F3} eV");
            GUILayout.Label(_controller.IsRunning ? "State: running" : "State: paused");
        }

        private void DrawTransportControls()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_controller.IsRunning ? "Pause (Space)" : "Resume (Space)"))
                _controller.TogglePause();
            if (GUILayout.Button("Step (.)"))
                _controller.RequestSingleStep();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reheat (T)"))
                _controller.Reheat();
            if (GUILayout.Button("Reset (R)"))
                _controller.ResetScenario();
            GUILayout.EndHorizontal();
        }

        private void DrawLiveSliders()
        {
            GUILayout.Label($"dt: {_controller.DtFemtoseconds:F2} fs/step");
            _controller.DtFemtoseconds = GUILayout.HorizontalSlider(_controller.DtFemtoseconds, 0.05f, 5f);

            GUILayout.Label($"Steps/frame: {_controller.StepsPerFrame}");
            _controller.StepsPerFrame = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(_controller.StepsPerFrame, 1, 32));

            GUILayout.Label($"Target temperature: {_controller.TemperatureK:F0} K (Reheat to apply)");
            _controller.SetTemperatureK(GUILayout.HorizontalSlider(_controller.TemperatureK, 0f, 2000f));
        }

        private void DrawQuickSpawnLegend()
        {
            int[] palette = (_controller.Config != null && _controller.Config.QuickSpawnElements != null)
                ? _controller.Config.QuickSpawnElements
                : _fallbackQuickSpawn;

            int slots = Mathf.Min(9, palette.Length);
            var line = new System.Text.StringBuilder("Quick-spawn: ");
            for (int i = 0; i < slots; i++)
            {
                if (i > 0) line.Append("  ");
                line.Append(i + 1).Append('=').Append(ElementSymbol(palette[i]));
            }
            GUILayout.Label(line.ToString());
        }

        private static GUIStyle _richLabelCache;
        private static GUIStyle RichLabel()
        {
            if (_richLabelCache == null)
                _richLabelCache = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
            return _richLabelCache;
        }

        /// <summary>
        /// Short display symbols for the same 20 elements
        /// AlembicElementColors defines a real color for — kept local
        /// rather than added to that class, since a short display label is
        /// a HUD concern, not a rendering-color one. Anything outside that
        /// set (including any custom-registered reagent) just shows its
        /// raw Z, which is also exactly what AlembicElementColors falls
        /// back to visually (DefaultColor's eye-catching magenta) for the
        /// same "not one of the 20" reason.
        /// </summary>
        private static string ElementSymbol(int z) => z switch
        {
            1 => "H", 2 => "He", 3 => "Li", 4 => "Be", 5 => "B",
            6 => "C", 7 => "N", 8 => "O", 15 => "P", 16 => "S",
            26 => "Fe", 29 => "Cu", 30 => "Zn", 33 => "As", 47 => "Ag",
            50 => "Sn", 51 => "Sb", 79 => "Au", 80 => "Hg", 82 => "Pb",
            _ => $"Z{z}",
        };
    }
}
