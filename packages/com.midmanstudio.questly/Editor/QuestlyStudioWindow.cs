using System;
using System.Collections.Generic;
using System.IO;
using MidManStudio.Mdix.Core;
using MidManStudio.Mdix.Unity;
using MidManStudio.Questly.Core;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.Questly.Editor
{
    /// <summary>
    /// Questly Studio editor window. Open via Window → MDIX → Questly Studio.
    ///
    /// Tabs:
    ///   Overview — scan project for .mdix assets (Questly doesn't try to
    ///              guess which ones are quest databases vs. locale/chemistry
    ///              files, same as Localization Studio's own Overview tab).
    ///   Validate — run QuestlySchema (MdixSchemaBuilder + the referential-
    ///              integrity pass) against a quest database and list issues.
    ///   Bake     — populate a QuestDataAsset SO from a quest .mdix asset,
    ///              for the zero-mdix-ffi runtime path in shipped builds.
    /// </summary>
    public sealed class QuestlyStudioWindow : EditorWindow
    {
        [MenuItem("Window/MDIX/Questly Studio", priority = 202)]
        public static void Open()
        {
            var window = GetWindow<QuestlyStudioWindow>("Questly Studio");
            window.minSize = new Vector2(480, 400);
        }

        // ── State ─────────────────────────────────────────────────────────────

        private int _tab;
        private readonly string[] _tabNames = { "Overview", "Validate", "Bake" };

        // Overview
        private readonly List<string> _foundAssets = new();
        private Vector2 _overviewScroll;
        private bool _overviewDirty = true;

        // Validate
        private MdixAsset? _valSource;
        private MdixValidationReport? _valReport;
        private Vector2 _valScroll;

        // Bake
        private MdixAsset? _bakeSource;
        private QuestDataAsset? _bakeTarget;
        private string _bakeStatus = string.Empty;
        private bool _bakeIsError;

        // ── IMGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _tab = GUILayout.Toolbar(_tab, _tabNames);
            EditorGUILayout.Space(8);

            switch (_tab)
            {
                case 0: DrawOverview(); break;
                case 1: DrawValidate(); break;
                case 2: DrawBake(); break;
            }
        }

        // ── Overview tab ──────────────────────────────────────────────────────

        private void DrawOverview()
        {
            EditorGUILayout.LabelField(".mdix assets in project", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            if (_overviewDirty || GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                _foundAssets.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:MdixAsset"))
                    _foundAssets.Add(AssetDatabase.GUIDToAssetPath(guid));
                _overviewDirty = false;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Every MdixAsset in the project shows up here, the same way Localization " +
                "Studio's Overview does -- Questly doesn't try to guess which ones are quest " +
                "databases versus locale or chemistry files. Pick the right one below.",
                MessageType.Info);
            EditorGUILayout.Space(4);

            _overviewScroll = EditorGUILayout.BeginScrollView(_overviewScroll);

            if (_foundAssets.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No MdixAsset files found. Create one via\nAssets → Create → MDIX → Blank File.",
                    MessageType.Info);
            }
            else
            {
                foreach (var path in _foundAssets)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(path, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Select", GUILayout.Width(56)))
                        Selection.activeObject = AssetDatabase.LoadAssetAtPath<MdixAsset>(path);

                    if (GUILayout.Button("Validate →", GUILayout.Width(80)))
                    {
                        _valSource = AssetDatabase.LoadAssetAtPath<MdixAsset>(path);
                        _tab = 1;
                    }

                    if (GUILayout.Button("Bake →", GUILayout.Width(60)))
                    {
                        _bakeSource = AssetDatabase.LoadAssetAtPath<MdixAsset>(path);
                        _tab = 2;
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Validate tab ──────────────────────────────────────────────────────

        private void DrawValidate()
        {
            EditorGUILayout.LabelField("Validate quest database", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Checks: duplicate quest/objective ids, dangling prereq references, dangling\n" +
                "COMPOSITE_* child_ids, a composite listing itself as its own child, an empty\n" +
                "evaluator_key on an EXTERNAL objective/prereq, and circular QUEST_COMPLETED\n" +
                "dependency chains.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            _valSource = (MdixAsset?)EditorGUILayout.ObjectField(
                "Quest database (.mdix)", _valSource, typeof(MdixAsset), false);

            EditorGUILayout.Space(6);

            EditorGUI.BeginDisabledGroup(_valSource == null);
            if (GUILayout.Button("Validate"))
                RunValidation();
            EditorGUI.EndDisabledGroup();

            if (_valReport == null) return;

            EditorGUILayout.Space(6);

            if (_valReport.IsValid)
            {
                EditorGUILayout.HelpBox("Validation passed — no issues found.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField($"{_valReport.Errors.Count} issue(s) found", EditorStyles.boldLabel);

            _valScroll = EditorGUILayout.BeginScrollView(_valScroll);
            foreach (var error in _valReport.Errors)
            {
                var messageType = error.Kind == MdixValidationErrorKind.Missing
                    ? MessageType.Error
                    : MessageType.Warning;
                EditorGUILayout.HelpBox($"[{error.Kind}]  {error.Path}\n{error.Message}", messageType);
            }
            EditorGUILayout.EndScrollView();
        }

        private void RunValidation()
        {
            if (_valSource == null) return;

            var dbResult = _valSource.Load();
            if (dbResult.IsFailure)
            {
                EditorUtility.DisplayDialog("Error",
                    $"Failed to load quest database: {dbResult.Error.Message}", "OK");
                return;
            }

            using var db = dbResult.SuccessResult;
            _valReport = db.Validate(new QuestlySchema());
        }

        // ── Bake tab ──────────────────────────────────────────────────────────

        private void DrawBake()
        {
            EditorGUILayout.LabelField("Bake quest database into ScriptableObject", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Baking populates a QuestDataAsset from a .mdix quest database. Construct\n" +
                "BakedQuestTable from the resulting .asset in shipped builds to skip the\n" +
                "mdix-ffi native dependency entirely -- MdixQuestTable stays the dev-time path.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            _bakeSource = (MdixAsset?)EditorGUILayout.ObjectField(
                "Source quests (.mdix)", _bakeSource, typeof(MdixAsset), false);

            _bakeTarget = (QuestDataAsset?)EditorGUILayout.ObjectField(
                "Target asset (.asset)", _bakeTarget, typeof(QuestDataAsset), false);

            EditorGUILayout.Space(4);

            if (_bakeTarget == null)
            {
                EditorGUILayout.LabelField(
                    "No target: a new QuestDataAsset will be created alongside the source.",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6);

            EditorGUI.BeginDisabledGroup(_bakeSource == null);
            if (GUILayout.Button("Bake", GUILayout.Height(28)))
                RunBake();
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(_bakeStatus))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_bakeStatus, _bakeIsError ? MessageType.Error : MessageType.Info);
            }
        }

        private void RunBake()
        {
            if (_bakeSource == null) return;

            var dbResult = _bakeSource.Load();
            if (dbResult.IsFailure)
            {
                _bakeStatus = $"Failed to load quest database: {dbResult.Error.Message}";
                _bakeIsError = true;
                return;
            }

            try
            {
                using var db = dbResult.SuccessResult;

                var tableResult = MdixQuestTable.Load(db);
                if (tableResult.IsFailure)
                {
                    _bakeStatus = $"Failed to load quests: {tableResult.Error.Message}";
                    _bakeIsError = true;
                    return;
                }

                var quests = tableResult.SuccessResult.AllQuests;

                var target = _bakeTarget;
                if (target == null)
                {
                    var sourceDir = Path.GetDirectoryName(_bakeSource.ProjectRelativePath) ?? "Assets";
                    var sourceName = Path.GetFileNameWithoutExtension(_bakeSource.ProjectRelativePath);
                    var outputPath = AssetDatabase.GenerateUniqueAssetPath($"{sourceDir}/{sourceName}_Baked.asset");

                    target = ScriptableObject.CreateInstance<QuestDataAsset>();
                    QuestDataBaker.PopulateExisting(target, quests);
                    AssetDatabase.CreateAsset(target, outputPath);
                }
                else
                {
                    QuestDataBaker.PopulateExisting(target, quests);
                }

                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                _bakeTarget = target;
                _bakeStatus = $"Baked {quests.Count} quest(s) into '{AssetDatabase.GetAssetPath(target)}'.";
                _bakeIsError = false;

                Selection.activeObject = target;
            }
            catch (Exception ex)
            {
                _bakeStatus = $"Bake error: {ex.Message}";
                _bakeIsError = true;
            }
        }
    }
}
