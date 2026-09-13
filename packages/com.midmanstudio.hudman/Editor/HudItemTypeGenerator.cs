// Generates HudItemType.cs from HudItemTypeProviderSO assets.
// Follows the same block/priority/lock pattern as
// com.midmanstudio.utilities' EffectTypeGenerator, reduced to a single
// enum since HudMan doesn't need a Category/Type split.
// Open via: MidManStudio > HudMan > Hud Item Type Generator

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MidManStudio.HudMan.Generator;

namespace MidManStudio.HudMan.Editor
{
    // ── Result ────────────────────────────────────────────────────────────────

    public class HudGenerationResult
    {
        public bool Success;
        public int BlocksWritten;
        public List<string> Errors = new();
        public List<string> Warnings = new();
        public bool HasErrors => Errors.Count > 0;
        public void AddError(string m) => Errors.Add(m);
        public void AddWarning(string m) => Warnings.Add(m);
    }

    // ── Internal data ─────────────────────────────────────────────────────────

    internal class HudProviderData
    {
        public string PackageId, DisplayName;
        public int Priority;
        public List<(string name, string comment, int offset)> Entries;
    }

    internal class HudResolvedBlock
    {
        public string PackageId, DisplayName;
        public int Priority, BlockStart, BlockSize;
        public List<(string Name, int Value, string Comment, bool Pinned)> Entries = new();
    }

    // ── Lock file ─────────────────────────────────────────────────────────────

    [Serializable]
    internal class HudTypeLockFile
    {
        public List<HudTypeLockEntry> entries = new();
    }

    [Serializable]
    internal class HudTypeLockEntry
    {
        public string packageId, name;
        public int value;
    }

    // ── Generator core ────────────────────────────────────────────────────────

    public static class HudItemTypeGeneratorCore
    {
        public static HudItemTypeGeneratorSettingsSO FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:HudItemTypeGeneratorSettingsSO");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<HudItemTypeGeneratorSettingsSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public static HudGenerationResult Generate(HudItemTypeGeneratorSettingsSO settings)
        {
            var result = new HudGenerationResult();
            if (settings == null)
            {
                result.AddError("No HudItemTypeGeneratorSettings found. Create one via MidManStudio > HudMan > Hud Item Type Generator Settings.");
                return result;
            }

            var lockFile = LoadLock(settings.lockFilePath);

            var providers = Collect();
            var blocks = AssignBlocks(providers, settings.minimumBlockSize, lockFile.entries, result);
            if (result.HasErrors) return result;

            WriteEnum(blocks, settings.enumOutputPath, settings.generatedNamespace);
            UpdateLock(blocks, lockFile.entries);
            result.BlocksWritten = blocks.Count;

            SaveLock(lockFile, settings.lockFilePath);
            AssetDatabase.Refresh();
            result.Success = true;
            return result;
        }

        // ── Provider collection ───────────────────────────────────────────────

        private static List<HudProviderData> Collect()
        {
            var list = new List<HudProviderData>();
            var guids = AssetDatabase.FindAssets($"t:{nameof(HudItemTypeProviderSO)}");
            foreach (var g in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<HudItemTypeProviderSO>(AssetDatabase.GUIDToAssetPath(g));
                if (asset == null) continue;
                list.Add(new HudProviderData
                {
                    PackageId = asset.packageId,
                    DisplayName = asset.displayName,
                    Priority = asset.priority,
                    Entries = asset.entries.Select(e => (e.entryName, e.comment, e.explicitOffset)).ToList(),
                });
            }
            return list;
        }

        // ── Block assignment — identical algorithm to EffectTypeGeneratorCore ─

        private static List<HudResolvedBlock> AssignBlocks(
            List<HudProviderData> providers, int minBlock,
            List<HudTypeLockEntry> lockEntries, HudGenerationResult result)
        {
            var sorted = providers.OrderBy(p => p.Priority).ThenBy(p => p.PackageId).ToList();

            foreach (var d in sorted.GroupBy(p => p.PackageId).Where(g => g.Count() > 1).Select(g => g.Key))
                result.AddError($"Duplicate packageId '{d}'.");
            if (result.HasErrors) return null;

            foreach (var d in sorted.SelectMany(p => p.Entries.Select(e => e.name))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key))
                result.AddError($"Duplicate entry name '{d}'.");
            if (result.HasErrors) return null;

            var blocks = new List<HudResolvedBlock>();
            int cursor = 0;

            foreach (var p in sorted)
            {
                int n = p.Entries.Count;
                int blockSize = Math.Max(minBlock, (int)Math.Ceiling((double)n / minBlock) * minBlock);
                int start = cursor;
                var entries = ResolveEntries(p.PackageId, p.Entries, start, blockSize, lockEntries, result);
                if (result.HasErrors) return null;

                blocks.Add(new HudResolvedBlock { PackageId = p.PackageId, DisplayName = p.DisplayName, Priority = p.Priority, BlockStart = start, BlockSize = blockSize, Entries = entries });
                cursor = start + blockSize;
            }
            return blocks;
        }

        private static List<(string Name, int Value, string Comment, bool Pinned)> ResolveEntries(
            string pkg, List<(string name, string comment, int offset)> raw,
            int start, int size, List<HudTypeLockEntry> lockEntries, HudGenerationResult result)
        {
            var resolved = new List<(string, int, string, bool)>();
            var slotMap = new Dictionary<int, string>();

            foreach (var (name, comment, offset) in raw)
            {
                if (offset < 0) continue;
                if (offset >= size) { result.AddError($"'{name}' in '{pkg}' pins to offset {offset} but block size is {size}."); return null; }
                int abs = start + offset;
                if (slotMap.ContainsKey(abs)) { result.AddError($"Collision at offset {offset} in '{pkg}'."); return null; }
                slotMap[abs] = name;
                resolved.Add((name, abs, comment, true));
            }

            int autoSlot = start;
            foreach (var (name, comment, offset) in raw)
            {
                if (offset >= 0) continue;
                var locked = lockEntries.FirstOrDefault(l => l.packageId == pkg && l.name == name);
                int target;
                if (locked != null && locked.value >= start && locked.value < start + size && !slotMap.ContainsKey(locked.value))
                    target = locked.value;
                else
                {
                    while (slotMap.ContainsKey(autoSlot) && autoSlot < start + size) autoSlot++;
                    if (autoSlot >= start + size) { result.AddError($"Block overflow for '{pkg}'."); return null; }
                    target = autoSlot++;
                }
                if (locked != null && locked.value != target)
                    result.AddWarning($"'{name}' in '{pkg}' changed {locked.value} → {target}.");
                slotMap[target] = name;
                resolved.Add((name, target, comment, false));
            }

            resolved.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return resolved;
        }

        // ── File writing ──────────────────────────────────────────────────────

        private static void WriteEnum(List<HudResolvedBlock> blocks, string path, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// AUTO-GENERATED by MidManStudio Hud Item Type Generator.");
            sb.AppendLine("// DO NOT edit this file manually.");
            sb.AppendLine("// Regenerate via: MidManStudio > HudMan > Hud Item Type Generator");
            sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>HUD item catalog IDs. AUTO-GENERATED — do not edit manually.</summary>");
            sb.AppendLine("    public enum HudItemType");
            sb.AppendLine("    {");

            for (int b = 0; b < blocks.Count; b++)
            {
                var blk = blocks[b];
                sb.AppendLine($"        // ── {blk.DisplayName}  [{blk.PackageId}]  (block {blk.BlockStart}–{blk.BlockStart + blk.BlockSize - 1})  priority={blk.Priority}  ──");
                if (blk.Entries.Count == 0) { sb.AppendLine("        // (no entries defined)"); }
                else foreach (var (name, value, comment, pinned) in blk.Entries)
                {
                    string pin = pinned ? " //[pinned]" : "";
                    string cmt = string.IsNullOrWhiteSpace(comment) ? pin : $" // {comment}{pin}";
                    sb.AppendLine($"        {name} = {value},{cmt}");
                }
                if (b < blocks.Count - 1) sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[HudItemTypeGenerator] Wrote HudItemType → {path}");
        }

        // ── Lock file ─────────────────────────────────────────────────────────

        private static HudTypeLockFile LoadLock(string path)
        {
            if (!File.Exists(path)) return new HudTypeLockFile();
            try { return JsonUtility.FromJson<HudTypeLockFile>(File.ReadAllText(path)) ?? new HudTypeLockFile(); }
            catch { Debug.LogWarning("[HudItemTypeGenerator] Could not parse lock file — starting fresh."); return new HudTypeLockFile(); }
        }

        private static void SaveLock(HudTypeLockFile lf, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(lf, prettyPrint: true), Encoding.UTF8);
        }

        private static void UpdateLock(List<HudResolvedBlock> blocks, List<HudTypeLockEntry> entries)
        {
            entries.Clear();
            foreach (var b in blocks) foreach (var (name, value, _, _) in b.Entries)
                entries.Add(new HudTypeLockEntry { packageId = b.PackageId, name = name, value = value });
        }
    }

    // ── Editor Window ─────────────────────────────────────────────────────────

    public class HudItemTypeGeneratorWindow : EditorWindow
    {
        private HudItemTypeGeneratorSettingsSO _settings;
        private HudGenerationResult _lastResult;
        private Vector2 _scroll;
        private bool _fold = true;

        [MenuItem("MidManStudio/HudMan/Hud Item Type Generator", priority = 191)]
        public static void Open()
        {
            var w = GetWindow<HudItemTypeGeneratorWindow>("Hud Item Type Generator");
            w.minSize = new Vector2(480, 420);
        }

        private void OnEnable() => _settings = HudItemTypeGeneratorCore.FindSettings();

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("MidManStudio — Hud Item Type Generator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Generates HudItemType.cs from HudItemTypeProviderSO assets.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSettings();
            EditorGUILayout.Space(4);
            DrawProviders();
            EditorGUILayout.Space(4);
            DrawActions();
            EditorGUILayout.Space(4);
            DrawResults();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _settings = (HudItemTypeGeneratorSettingsSO)EditorGUILayout.ObjectField(
                "Generator Settings", _settings, typeof(HudItemTypeGeneratorSettingsSO), false);

            if (_settings == null)
            {
                EditorGUILayout.HelpBox("No HudItemTypeGeneratorSettings found.\nCreate one: MidManStudio > HudMan > Hud Item Type Generator Settings", MessageType.Warning);
                if (GUILayout.Button("Create Default Settings")) CreateDefaultSettings();
            }
            else
            {
                var so = new SerializedObject(_settings); so.Update();
                EditorGUILayout.PropertyField(so.FindProperty("enumOutputPath"), new GUIContent("Enum Output"));
                EditorGUILayout.PropertyField(so.FindProperty("lockFilePath"), new GUIContent("Lock File"));
                EditorGUILayout.PropertyField(so.FindProperty("minimumBlockSize"), new GUIContent("Min Block Size"));
                EditorGUILayout.PropertyField(so.FindProperty("generatedNamespace"), new GUIContent("Namespace"));
                EditorGUILayout.PropertyField(so.FindProperty("autoGenerateOnAssetChange"), new GUIContent("Auto-Generate on Change"));
                so.ApplyModifiedProperties();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawProviders()
        {
            EditorGUILayout.LabelField("Discovered Providers", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var guids = AssetDatabase.FindAssets($"t:{nameof(HudItemTypeProviderSO)}");
            _fold = EditorGUILayout.Foldout(_fold, $"Hud Item Type Providers  ({guids.Length})", true);
            if (_fold)
            {
                if (guids.Length == 0)
                {
                    EditorGUILayout.HelpBox("No HudItemTypeProviderSO assets found.", MessageType.Info);
                }
                else foreach (var g in guids)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<HudItemTypeProviderSO>(AssetDatabase.GUIDToAssetPath(g));
                    if (asset == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    var old = GUI.contentColor;
                    GUI.contentColor = asset.priority == 0 ? new Color(0.4f, 0.8f, 0.4f) : asset.priority <= 10 ? new Color(0.4f, 0.6f, 1f) : new Color(0.8f, 0.8f, 0.8f);
                    EditorGUILayout.LabelField($"[{asset.priority:D3}]", GUILayout.Width(36));
                    GUI.contentColor = old;
                    EditorGUILayout.LabelField($"{asset.displayName}  ({asset.packageId})  — {asset.EntryCount} entries", EditorStyles.miniLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(50))) Selection.activeObject = asset;
                    if (GUILayout.Button("Ping", GUILayout.Width(40))) EditorGUIUtility.PingObject(asset);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Create a provider for your game:", EditorStyles.miniLabel);
            if (GUILayout.Button("+ Hud Item Type Provider")) CreateProvider();
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _settings != null;
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.85f, 0.3f);
            if (GUILayout.Button("⚙  Generate Now", GUILayout.Height(36)))
            {
                _lastResult = HudItemTypeGeneratorCore.Generate(_settings);
                if (_lastResult.Success)
                    EditorUtility.DisplayDialog("Hud Item Type Generator", $"Done!\nBlocks written: {_lastResult.BlocksWritten}", "OK");
            }
            GUI.backgroundColor = old; GUI.enabled = true;
            if (GUILayout.Button("  Open Output Folder", GUILayout.Height(36)))
            {
                var dir = _settings != null ? Path.GetDirectoryName(_settings.enumOutputPath) : "packages/com.midmanstudio.hudman/Runtime/Generated";
                EditorUtility.RevealInFinder(string.IsNullOrEmpty(dir) ? "Assets" : dir);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("To add your own HUD items:\n  1. Click + Hud Item Type Provider above.\n  2. Set your packageId (e.g. com.mygame), priority >= 100, and add entry names.\n  3. Click Generate Now.", MessageType.None);
        }

        private void DrawResults()
        {
            if (_lastResult == null) return;
            EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_lastResult.Success) EditorGUILayout.HelpBox($"✓ Blocks written: {_lastResult.BlocksWritten}", MessageType.Info);
            foreach (var w in _lastResult.Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            foreach (var e in _lastResult.Errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            EditorGUILayout.EndVertical();
        }

        private void CreateDefaultSettings()
        {
            const string dir = "Assets/MidManStudio/Generated/HudMan";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var asset = ScriptableObject.CreateInstance<HudItemTypeGeneratorSettingsSO>();
            AssetDatabase.CreateAsset(asset, dir + "/HudItemTypeGeneratorSettings.asset");
            AssetDatabase.SaveAssets(); _settings = asset;
            Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
        }

        private static void CreateProvider()
        {
            const string dir = "Assets/MidManStudio/Generated/HudMan/MyProviders";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var asset = ScriptableObject.CreateInstance<HudItemTypeProviderSO>();
            AssetDatabase.CreateAsset(asset, $"{dir}/HudItemTypeProvider_MyGame.asset");
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
        }
    }

    // ── Auto-generate hook ───────────────────────────────────────────────────

    internal class HudTypeAssetPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> Watched = new() { "HudItemTypeProviderSO", "HudItemTypeGeneratorSettingsSO" };

        private static void OnPostprocessAllAssets(string[] imp, string[] del, string[] mov, string[] movFrom)
        {
            bool relevant = imp.Concat(del).Concat(mov).Any(path =>
            {
                if (!path.EndsWith(".asset")) return false;
                var t = AssetDatabase.GetMainAssetTypeAtPath(path);
                return t != null && Watched.Contains(t.Name);
            });
            if (!relevant) return;
            var settings = HudItemTypeGeneratorCore.FindSettings();
            if (settings == null || !settings.autoGenerateOnAssetChange) return;
            EditorApplication.delayCall += () => { var r = HudItemTypeGeneratorCore.Generate(settings); if (r.HasErrors) foreach (var e in r.Errors) Debug.LogError($"[HudItemTypeGen Auto] {e}"); };
        }
    }
}
#endif
