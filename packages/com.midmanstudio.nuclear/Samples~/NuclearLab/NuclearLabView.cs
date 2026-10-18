// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabView.cs"
// ============================================================================
using System;
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// The Nuclear Lab panel. Add this one component to any GameObject and press
    /// Play: it draws its own panel with plain IMGUI, so the scene needs no
    /// canvas, camera setup, material or input handling. Each tab calls the
    /// nuclear_core library through the package and shows what comes back.
    /// </summary>
    public sealed class NuclearLabView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Width of the panel in points, before the scale is applied.")]
        [SerializeField] private float _panelWidth = 640f;

        [Tooltip("Scale of the whole panel. 0 picks one from the screen height, which suits a phone.")]
        [SerializeField] private float _uiScale = 0f;

        [Tooltip("Gap between the panel and the screen edge, in points.")]
        [SerializeField] private float _margin = 12f;

        private readonly NuclearLabQueue _queue = new NuclearLabQueue();
        private NuclearLabTab[] _tabs;
        private string[] _titles;
        private Vector2[] _scroll;
        private NuclearLabStyles _styles;
        private string _fatal;
        private string _header = string.Empty;
        private int _tab;

        private void OnEnable()
        {
            _fatal = null;
            try
            {
                NuclearLibrary.Initialize();
                _header = "nuclear_core " + NuclearLibrary.Version + ", "
                    + NuclearLabFormat.Count((ulong)NuclearData.NuclideCount) + " nuclides in the mass table";
                _tabs = new NuclearLabTab[]
                {
                    new NuclideTab(_queue),
                    new DecayTab(_queue),
                    new FissionTab(_queue),
                    new FusionTab(_queue),
                };
                _titles = new string[_tabs.Length];
                _scroll = new Vector2[_tabs.Length];
                for (int i = 0; i < _tabs.Length; i++)
                {
                    _titles[i] = _tabs[i].Title;
                }
            }
            catch (Exception e)
            {
                DisposeTabs();
                _fatal = "The nuclear library could not start: " + e.Message
                    + "\n\nThe package needs the native nuclear_core binary for this platform in Runtime/Plugins/Native. "
                    + "See the README of this sample.";
                Debug.LogError("[NuclearLabView] " + e);
            }
        }

        private void OnDisable()
        {
            DisposeTabs();
        }

        private void Update()
        {
            _queue.RunAll();
        }

        private void OnGUI()
        {
            if (_styles == null)
            {
                _styles = NuclearLabStyles.Create();
            }
            float scale = _uiScale > 0f ? _uiScale : Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float width = Mathf.Min(_panelWidth, Screen.width / scale - 2f * _margin);
            float height = Screen.height / scale - 2f * _margin;
            GUILayout.BeginArea(new Rect(_margin, _margin, width, height), GUI.skin.box);
            GUILayout.Label("Nuclear Lab", _styles.Title);
            if (_fatal != null)
            {
                GUILayout.Label(_fatal, _styles.Error);
            }
            else if (_tabs != null)
            {
                GUILayout.Label(_header, _styles.Hint);
                int picked = GUILayout.Toolbar(_tab, _titles);
                if (picked != _tab)
                {
                    int next = picked;
                    _queue.Add(delegate { _tab = next; });
                }
                _scroll[_tab] = GUILayout.BeginScrollView(_scroll[_tab]);
                _tabs[_tab].Draw(_styles);
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
            GUI.matrix = saved;
        }

        private void DisposeTabs()
        {
            if (_tabs == null)
            {
                return;
            }
            foreach (NuclearLabTab tab in _tabs)
            {
                tab.Dispose();
            }
            _tabs = null;
        }
    }
}
