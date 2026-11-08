// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleLabWindow.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The lab of the runtime. Live shows the manager and checks that Burst and the native library work. Parity runs
    /// a scene on all three backends frame for frame and compares them. Bench times the scene table on the three
    /// backends on this machine, which is the comparison the Rust bench cannot make on its own.
    /// </summary>
    public sealed class JiggleLabWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Live", "Parity", "Bench" };

        private int tab;
        private Vector2 scroll;
        private string burstText = "not checked";
        private string nativeText = "not checked";

        private JiggleBenchSpec[] scenes;
        private string[] sceneNames;
        private bool[] sceneOn;
        private bool useBurst = true;
        private bool useNative = true;
        private bool useManaged = true;
        private string threadsText = "1,2,4";
        private float scale = 0.2f;
        private int rigsPerBatch = 8;
        private readonly List<JiggleBenchRow> rows = new List<JiggleBenchRow>();
        private string benchText = "";

        private int paritySceneIndex = 2;
        private int parityFrames = 120;
        private string parityText = "";

        private GUIStyle mono;

        [MenuItem("Tools/MidMan Studio/Jiggle Lab")]
        public static void Open()
        {
            GetWindow<JiggleLabWindow>("Jiggle Lab");
        }

        private void OnEnable()
        {
            scenes = JiggleBenchScenes.All();
            sceneNames = new string[scenes.Length];
            sceneOn = new bool[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                sceneNames[i] = scenes[i].Name;
                sceneOn[i] = scenes[i].Particles <= 12000 && scenes[i].Name != "hair-10k";
            }
        }

        private void OnInspectorUpdate()
        {
            if (tab == 0 && Application.isPlaying)
            {
                Repaint();
            }
        }

        private GUIStyle Mono()
        {
            if (mono == null)
            {
                mono = new GUIStyle(EditorStyles.textArea);
                mono.font = Font.CreateDynamicFontFromOSFont(new string[] { "Consolas", "Menlo", "Courier New" }, 11);
                mono.wordWrap = false;
            }
            return mono;
        }

        private void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, TabNames);
            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0)
            {
                DrawLive();
            }
            else if (tab == 1)
            {
                DrawParity();
            }
            else
            {
                DrawBench();
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ live

        private void DrawLive()
        {
            EditorGUILayout.LabelField("Burst", burstText);
            if (GUILayout.Button("Check that Burst compiles the jobs"))
            {
                burstText = JiggleBurstSolver.IsBurstCompiled() ? "compiled" : "NOT compiled, jobs run as managed code";
            }
            EditorGUILayout.LabelField("Native library", nativeText);
            if (GUILayout.Button("Check the native library"))
            {
                try
                {
                    JiggleLibrary.Verify();
                    nativeText = "loaded, version " + JiggleLibrary.Version;
                }
                catch (Exception e)
                {
                    nativeText = "NOT usable: " + e.Message;
                }
            }
            EditorGUILayout.Space();
            JiggleManager m = Application.isPlaying ? JiggleManager.TryGet() : null;
            if (m == null)
            {
                EditorGUILayout.HelpBox("Enter play mode with a JiggleChain enabled to see the manager here.", MessageType.Info);
                return;
            }
            EditorGUI.BeginChangeCheck();
            JiggleRuntimeBackend asked = (JiggleRuntimeBackend)EditorGUILayout.EnumPopup("Backend", m.backend);
            if (EditorGUI.EndChangeCheck())
            {
                m.backend = asked;
            }
            JiggleFrameStats st = m.Stats;
            EditorGUILayout.LabelField("Running on", m.ActiveBackend.ToString());
            if (m.NativeError.Length > 0)
            {
                EditorGUILayout.HelpBox(m.NativeError, MessageType.Warning);
            }
            EditorGUILayout.LabelField("Chains", m.ChainCount.ToString());
            EditorGUILayout.LabelField("Rigs simulated", st.Rigs.ToString());
            EditorGUILayout.LabelField("Particles", st.Particles.ToString());
            EditorGUILayout.LabelField("Steps last frame", st.Steps.ToString());
            EditorGUILayout.LabelField("Frame time", st.FrameMs.ToString("F3", CultureInfo.InvariantCulture) + " ms");
            EditorGUILayout.LabelField("Solver status", st.Status.ToString());
            EditorGUILayout.LabelField("Rebuilds", st.Rebuilds.ToString());
        }

        // ------------------------------------------------------------------ parity

        private void DrawParity()
        {
            EditorGUILayout.HelpBox(
                "Runs one scene on the managed, native and Burst backends with the same poses. Native has to equal managed in every bit. Burst is allowed to differ a little.",
                MessageType.None);
            paritySceneIndex = EditorGUILayout.Popup("Scene", paritySceneIndex, sceneNames);
            parityFrames = EditorGUILayout.IntSlider("Frames", parityFrames, 10, 600);
            rigsPerBatch = EditorGUILayout.IntSlider("Rigs per Burst batch", rigsPerBatch, 1, 256);
            if (GUILayout.Button("Run parity"))
            {
                RunParity();
            }
            if (parityText.Length > 0)
            {
                EditorGUILayout.TextArea(parityText, Mono());
            }
        }

        private void RunParity()
        {
            JiggleBenchSpec s = scenes[paritySceneIndex];
            try
            {
                EditorUtility.DisplayProgressBar("Jiggle parity", s.Name, 0.5f);
                JiggleParityResult r = JiggleLabRunner.Parity(s, parityFrames, rigsPerBatch);
                StringBuilder b = new StringBuilder();
                b.AppendLine("scene " + r.Scene + ", " + r.Frames + " frames, " + s.Rigs + " rigs");
                if (r.NativeAvailable)
                {
                    b.AppendLine("native vs managed: " + (r.NativeFramesDiffering == 0 ? "identical bits in every frame" : r.NativeFramesDiffering + " frames DIFFER"));
                }
                else
                {
                    b.AppendLine("native: not available (" + r.NativeError + ")");
                }
                b.AppendLine("burst vs managed: " + (r.BurstFramesDiffering == 0 ? "identical bits in every frame" : r.BurstFramesDiffering + " frames differ in some bit"));
                b.AppendLine("  largest position difference " + r.BurstMaxPosition.ToString("E3", CultureInfo.InvariantCulture) + " m");
                b.AppendLine("  largest rotation difference " + r.BurstMaxRotation.ToString("E3", CultureInfo.InvariantCulture));
                b.AppendLine("all burst values finite: " + r.Finite);
                parityText = b.ToString();
            }
            catch (Exception e)
            {
                parityText = "failed: " + e;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------ bench

        private void DrawBench()
        {
            EditorGUILayout.HelpBox(
                "Times the scene table of the Rust scene bench on the three backends, here, in this editor. Times are wall clock microseconds per frame. Turn Burst safety checks, the jobs debugger and leak detection off and use a Release or an IL2CPP player for numbers you want to publish. Editor numbers show the order of things.",
                MessageType.None);
            EditorGUILayout.LabelField("Scenes");
            for (int i = 0; i < scenes.Length; i++)
            {
                sceneOn[i] = EditorGUILayout.ToggleLeft(
                    scenes[i].Name + "  (" + scenes[i].Rigs + " rigs x " + scenes[i].Bones + " bones, " + scenes[i].Frames + " frames)", sceneOn[i]);
            }
            useBurst = EditorGUILayout.ToggleLeft("Burst jobs", useBurst);
            useNative = EditorGUILayout.ToggleLeft("Native (Rust), threads below", useNative);
            useManaged = EditorGUILayout.ToggleLeft("Managed, threads below", useManaged);
            threadsText = EditorGUILayout.TextField("Threads (native, managed)", threadsText);
            scale = EditorGUILayout.Slider("Frame count scale", scale, 0.02f, 1f);
            rigsPerBatch = EditorGUILayout.IntSlider("Rigs per Burst batch", rigsPerBatch, 1, 256);
            if (GUILayout.Button("Run bench"))
            {
                RunBench();
            }
            if (rows.Count > 0)
            {
                EditorGUILayout.TextArea(benchText, Mono());
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy CSV"))
                {
                    EditorGUIUtility.systemCopyBuffer = Csv();
                }
                if (GUILayout.Button("Save CSV..."))
                {
                    string path = EditorUtility.SaveFilePanel("Save the bench", "", "jiggle-unity-bench.csv", "csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        File.WriteAllText(path, Csv());
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private int[] ParseThreads()
        {
            List<int> v = new List<int>();
            string[] parts = threadsText.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                int t;
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out t) && t >= 1 && !v.Contains(t))
                {
                    v.Add(t);
                }
            }
            if (v.Count == 0)
            {
                v.Add(1);
            }
            return v.ToArray();
        }

        private void RunBench()
        {
            rows.Clear();
            int[] threads = ParseThreads();
            string nativeError;
            bool native = useNative && JiggleLabRunner.NativeAvailable(out nativeError);
            if (useNative && !native)
            {
                Debug.LogWarning("The native backend is not available, so it is left out of the bench.");
            }
            List<JiggleBenchSpec> jobs = new List<JiggleBenchSpec>();
            for (int i = 0; i < scenes.Length; i++)
            {
                if (sceneOn[i])
                {
                    jobs.Add(scenes[i]);
                }
            }
            try
            {
                int done = 0;
                int total = 0;
                for (int j = 0; j < jobs.Count; j++)
                {
                    total += (useBurst ? 1 : 0) + (native ? threads.Length : 0) + (useManaged ? threads.Length : 0);
                }
                for (int j = 0; j < jobs.Count; j++)
                {
                    JiggleBenchSpec s = jobs[j];
                    if (useBurst)
                    {
                        if (Step(ref done, total, s.Name + " burst"))
                        {
                            return;
                        }
                        rows.Add(JiggleLabRunner.Run(s, JiggleRuntimeBackend.Burst, 1, scale, rigsPerBatch));
                    }
                    for (int t = 0; t < threads.Length; t++)
                    {
                        int th = Math.Min(threads[t], s.Rigs);
                        if (t > 0 && th == Math.Min(threads[t - 1], s.Rigs))
                        {
                            done += (native ? 1 : 0) + (useManaged ? 1 : 0);
                            continue;
                        }
                        if (native)
                        {
                            if (Step(ref done, total, s.Name + " native x" + th))
                            {
                                return;
                            }
                            rows.Add(JiggleLabRunner.Run(s, JiggleRuntimeBackend.Native, th, scale, rigsPerBatch));
                        }
                        if (useManaged)
                        {
                            if (Step(ref done, total, s.Name + " managed x" + th))
                            {
                                return;
                            }
                            rows.Add(JiggleLabRunner.Run(s, JiggleRuntimeBackend.Managed, th, scale, rigsPerBatch));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("The bench stopped: " + e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                benchText = Table();
            }
        }

        // Shows the progress bar. True when the person pressed cancel.
        private static bool Step(ref int done, int total, string what)
        {
            done++;
            return EditorUtility.DisplayCancelableProgressBar("Jiggle bench", what, total > 0 ? (float)done / (float)total : 1f);
        }

        private string Table()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-14} {1,-8} {2,3} {3,6} {4,5} {5,9} {6,9} {7,9} {8,9} {9,9} {10,8} {11,6}",
                "scene", "backend", "thr", "rigs", "bones", "wall", "pose", "step", "resolve", "ns/part", "x man.1T", "asleep"));
            for (int i = 0; i < rows.Count; i++)
            {
                JiggleBenchRow r = rows[i];
                double baseline = 0.0;
                for (int k = 0; k < rows.Count; k++)
                {
                    if (rows[k].Scene == r.Scene && rows[k].Backend == "managed" && rows[k].Threads == 1)
                    {
                        baseline = rows[k].WallUs;
                    }
                }
                string ratio = baseline > 0.0 ? (baseline / r.WallUs).ToString("F2", CultureInfo.InvariantCulture) : "-";
                b.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-14} {1,-8} {2,3} {3,6} {4,5} {5,9:F1} {6,9:F1} {7,9:F1} {8,9:F1} {9,9:F1} {10,8} {11,6}{12}",
                    r.Scene, r.Backend, r.Threads, r.Rigs, r.Bones, r.WallUs, r.PoseUs, r.StepUs, r.ResolveUs, r.NsPerParticle, ratio, r.Asleep,
                    r.Finite ? "" : "  NOT FINITE"));
            }
            b.AppendLine();
            b.AppendLine("Times are wall clock microseconds per frame. pose, step and resolve are the wall time of each phase.");
            b.AppendLine("ns/part is the wall time of step and resolve per particle. It is a cost per particle only for 1 thread.");
            return b.ToString();
        }

        private string Csv()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(JiggleBenchRow.CsvHeader);
            for (int i = 0; i < rows.Count; i++)
            {
                b.AppendLine(rows[i].Csv());
            }
            return b.ToString();
        }
    }
}
