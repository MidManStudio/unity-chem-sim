// Generates SettingsCategory.cs and SettingsSubCategory.cs from
// SettingsCategoryProviderSO / SettingsSubCategoryProviderSO assets.
// Same block/priority/lock pattern as com.midmanstudio.hudman's
// HudItemTypeGenerator (itself adapted from com.midmanstudio.utilities'
// EffectTypeGenerator), run twice against a shared internal DTO rather
// than duplicating the algorithm per enum.
// Open via: MidManStudio > GenSettings > Settings Enum Generator

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MidManStudio.GenSettings.Generator;

namespace MidManStudio.GenSettings.Editor
{
    public class SettingsGenerationResult
    {
        public bool Success;
        public int CategoryBlocksWritten, SubCategoryBlocksWritten;
        public List<string> Errors = new();
        public List<string> Warnings = new();
        public bool HasErrors => Errors.Count > 0;
        public void AddError(string m) => Errors.Add(m);
        public void AddWarning(string m) => Warnings.Add(m);
    }

    internal class SettingsProviderData
    {
        public string PackageId, DisplayName;
        public int Priority;
        public List<(string name, string comment, int offset)> Entries;
    }

    internal class SettingsResolvedBlock
    {
        public string PackageId, DisplayName;
        public int Priority, BlockStart, BlockSize;
        public List<(string Name, int Value, string Comment, bool Pinned)> Entries = new();
    }

    [Serializable] internal class SettingsEnumLockEntry { public string packageId, name; public int value; }
    [Serializable]
    internal class SettingsEnumLockFile
    {
        public List<SettingsEnumLockEntry> categoryEntries = new();
        public List<SettingsEnumLockEntry> subCategoryEntries = new();
    }

    public static class SettingsEnumGeneratorCore
    {
        public static SettingsEnumGeneratorSettingsSO FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:SettingsEnumGeneratorSettingsSO");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<SettingsEnumGeneratorSettingsSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public static SettingsGenerationResult Generate(SettingsEnumGeneratorSettingsSO settings)
        {
            var result = new SettingsGenerationResult();
            if (settings == null)
            {
                result.AddError("No SettingsEnumGeneratorSettings found. Create one via MidManStudio > GenSettings > Settings Enum Generator Settings.");
                return result;
            }

            var lockFile = LoadLock(settings.lockFilePath);

            var categoryProviders = CollectCategoryProviders();
            var categoryBlocks = AssignBlocks(categoryProviders, settings.minimumBlockSize, lockFile.categoryEntries, result);
            if (result.HasErrors) return result;

            var subProviders = CollectSubCategoryProviders();
            var subBlocks = AssignBlocks(subProviders, settings.minimumBlockSize, lockFile.subCategoryEntries, result);
            if (result.HasErrors) return result;

            WriteEnum(categoryBlocks, settings.categoryEnumOutputPath, settings.generatedNamespace, "SettingsCategory");
            WriteEnum(subBlocks, settings.subCategoryEnumOutputPath, settings.generatedNamespace, "SettingsSubCategory");

            UpdateLock(categoryBlocks, lockFile.categoryEntries);
            UpdateLock(subBlocks, lockFile.subCategoryEntries);
            SaveLock(lockFile, settings.lockFilePath);

            result.CategoryBlocksWritten = categoryBlocks.Count;
            result.SubCategoryBlocksWritten = subBlocks.Count;

            AssetDatabase.Refresh();
            result.Success = true;
            return result;
        }

        // ── Provider collection ───────────────────────────────────────────────

        private static List<SettingsProviderData> CollectCategoryProviders()
        {
            var list = new List<SettingsProviderData>();
            foreach (var g in AssetDatabase.FindAssets($"t:{nameof(SettingsCategoryProviderSO)}"))
            {
                var a = AssetDatabase.LoadAssetAtPath<SettingsCategoryProviderSO>(AssetDatabase.GUIDToAssetPath(g));
                if (a == null) continue;
                list.Add(new SettingsProviderData { PackageId = a.packageId, DisplayName = a.displayName, Priority = a.priority, Entries = a.entries.Select(e => (e.entryName, e.comment, e.explicitOffset)).ToList() });
            }
            return list;
        }

        private static List<SettingsProviderData> CollectSubCategoryProviders()
        {
            var list = new List<SettingsProviderData>();
            foreach (var g in AssetDatabase.FindAssets($"t:{nameof(SettingsSubCategoryProviderSO)}"))
            {
                var a = AssetDatabase.LoadAssetAtPath<SettingsSubCategoryProviderSO>(AssetDatabase.GUIDToAssetPath(g));
                if (a == null) continue;
                list.Add(new SettingsProviderData { PackageId = a.packageId, DisplayName = a.displayName, Priority = a.priority, Entries = a.entries.Select(e => (e.entryName, e.comment, e.explicitOffset)).ToList() });
            }
            return list;
        }

        // ── Block assignment — identical algorithm to HudItemTypeGeneratorCore ─

        private static List<SettingsResolvedBlock> AssignBlocks(
            List<SettingsProviderData> providers, int minBlock,
            List<SettingsEnumLockEntry> lockEntries, SettingsGenerationResult result)
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

            var blocks = new List<SettingsResolvedBlock>();
            int cursor = 0;

            foreach (var p in sorted)
            {
                int n = p.Entries.Count;
                int blockSize = Math.Max(minBlock, (int)Math.Ceiling((double)n / minBlock) * minBlock);
                int start = cursor;
                var entries = ResolveEntries(p.PackageId, p.Entries, start, blockSize, lockEntries, result);
                if (result.HasErrors) return null;

                blocks.Add(new SettingsResolvedBlock { PackageId = p.PackageId, DisplayName = p.DisplayName, Priority = p.Priority, BlockStart = start, BlockSize = blockSize, Entries = entries });
                cursor = start + blockSize;
            }
            return blocks;
        }

        private static List<(string Name, int Value, string Comment, bool Pinned)> ResolveEntries(
            string pkg, List<(string name, string comment, int offset)> raw,
            int start, int size, List<SettingsEnumLockEntry> lockEntries, SettingsGenerationResult result)
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

        private static void WriteEnum(List<SettingsResolvedBlock> blocks, string path, string ns, string enumName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// AUTO-GENERATED by MidManStudio Settings Enum Generator.");
            sb.AppendLine("// DO NOT edit this file manually.");
            sb.AppendLine("// Regenerate via: MidManStudio > GenSettings > Settings Enum Generator");
            sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>AUTO-GENERATED — do not edit manually.</summary>");
            sb.AppendLine($"    public enum {enumName}");
            sb.AppendLine("    {");

            for (int b = 0; b < blocks.Count; b++)
            {
                var blk = blocks[b];
                sb.AppendLine($"        // ── {blk.DisplayName}  [{blk.PackageId}]  (block {blk.BlockStart}–{blk.BlockStart + blk.BlockSize - 1})  priority={blk.Priority}  ──");
                if (blk.Entries.Count == 0) sb.AppendLine("        // (no entries defined)");
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
            Debug.Log($"[SettingsEnumGenerator] Wrote {enumName} → {path}");
        }

        // ── Lock file ─────────────────────────────────────────────────────────

        private static SettingsEnumLockFile LoadLock(string path)
        {
            if (!File.Exists(path)) return new SettingsEnumLockFile();
            try { return JsonUtility.FromJson<SettingsEnumLockFile>(File.ReadAllText(path)) ?? new SettingsEnumLockFile(); }
            catch { Debug.LogWarning("[SettingsEnumGenerator] Could not parse lock file — starting fresh."); return new SettingsEnumLockFile(); }
        }

        private static void SaveLock(SettingsEnumLockFile lf, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(lf, prettyPrint: true), Encoding.UTF8);
        }

        private static void UpdateLock(List<SettingsResolvedBlock> blocks, List<SettingsEnumLockEntry> entries)
        {
            entries.Clear();
            foreach (var b in blocks) foreach (var (name, value, _, _) in b.Entries)
                entries.Add(new SettingsEnumLockEntry { packageId = b.PackageId, name = name, value = value });
        }
    }

    public class SettingsEnumGeneratorWindow : EditorWindow
    {
        private SettingsEnumGeneratorSettingsSO _settings;
        private SettingsGenerationResult _lastResult;
        private Vector2 _scroll;
        private bool _foldCat = true, _foldSub = true;

        [MenuItem("MidManStudio/GenSettings/Settings Enum Generator", priority = 193)]
        public static void Open()
        {
            var w = GetWindow<SettingsEnumGeneratorWindow>("Settings Enum Generator");
            w.minSize = new Vector2(520, 480);
        }

        private void OnEnable() => _settings = SettingsEnumGeneratorCore.FindSettings();

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("MidManStudio — Settings Enum Generator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Generates SettingsCategory.cs and SettingsSubCategory.cs from provider assets.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSettings();
            EditorGUILayout.Space(4);
            DrawProviderList<SettingsCategoryProviderSO>("Settings Category Providers", ref _foldCat);
            EditorGUILayout.Space(4);
            DrawProviderList<SettingsSubCategoryProviderSO>("Settings SubCategory Providers", ref _foldSub);
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
            _settings = (SettingsEnumGeneratorSettingsSO)EditorGUILayout.ObjectField("Generator Settings", _settings, typeof(SettingsEnumGeneratorSettingsSO), false);

            if (_settings == null)
            {
                EditorGUILayout.HelpBox("No SettingsEnumGeneratorSettings found.\nCreate one: MidManStudio > GenSettings > Settings Enum Generator Settings", MessageType.Warning);
                if (GUILayout.Button("Create Default Settings")) CreateDefaultSettings();
            }
            else
            {
                var so = new SerializedObject(_settings); so.Update();
                EditorGUILayout.PropertyField(so.FindProperty("categoryEnumOutputPath"));
                EditorGUILayout.PropertyField(so.FindProperty("subCategoryEnumOutputPath"));
                EditorGUILayout.PropertyField(so.FindProperty("lockFilePath"));
                EditorGUILayout.PropertyField(so.FindProperty("minimumBlockSize"));
                EditorGUILayout.PropertyField(so.FindProperty("generatedNamespace"));
                EditorGUILayout.PropertyField(so.FindProperty("autoGenerateOnAssetChange"));
                so.ApplyModifiedProperties();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawProviderList<T>(string title, ref bool fold) where T : UnityEngine.Object
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            fold = EditorGUILayout.Foldout(fold, $"{title}  ({guids.Length})", true);
            if (fold)
            {
                if (guids.Length == 0) EditorGUILayout.HelpBox("None found.", MessageType.Info);
                foreach (var g in guids)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
                    if (asset == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(asset, typeof(T), false);
                    if (GUILayout.Button("Ping", GUILayout.Width(40))) EditorGUIUtility.PingObject(asset);
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _settings != null;
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.85f, 0.3f);
            if (GUILayout.Button("⚙  Generate Now", GUILayout.Height(36)))
            {
                _lastResult = SettingsEnumGeneratorCore.Generate(_settings);
                if (_lastResult.Success)
                    EditorUtility.DisplayDialog("Settings Enum Generator", $"Done!\nCategory blocks: {_lastResult.CategoryBlocksWritten}\nSubCategory blocks: {_lastResult.SubCategoryBlocksWritten}", "OK");
            }
            GUI.backgroundColor = old; GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("To add your own categories:\n  1. Create a Settings Category Provider (and/or SubCategory Provider) above.\n  2. Set packageId, priority >= 100, and entry names.\n  3. Click Generate Now.", MessageType.None);
        }

        private void DrawResults()
        {
            if (_lastResult == null) return;
            EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_lastResult.Success) EditorGUILayout.HelpBox($"✓ Category blocks: {_lastResult.CategoryBlocksWritten}   SubCategory blocks: {_lastResult.SubCategoryBlocksWritten}", MessageType.Info);
            foreach (var w in _lastResult.Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            foreach (var e in _lastResult.Errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            EditorGUILayout.EndVertical();
        }

        private void CreateDefaultSettings()
        {
            const string dir = "Assets/MidManStudio/Generated/GenSettings";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var asset = ScriptableObject.CreateInstance<SettingsEnumGeneratorSettingsSO>();
            AssetDatabase.CreateAsset(asset, dir + "/SettingsEnumGeneratorSettings.asset");
            AssetDatabase.SaveAssets(); _settings = asset;
            Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
        }
    }

    internal class SettingsEnumAssetPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> Watched = new()
        {
            nameof(SettingsCategoryProviderSO), nameof(SettingsSubCategoryProviderSO), nameof(SettingsEnumGeneratorSettingsSO)
        };

        private static void OnPostprocessAllAssets(string[] imp, string[] del, string[] mov, string[] movFrom)
        {
            bool relevant = imp.Concat(del).Concat(mov).Any(path =>
            {
                if (!path.EndsWith(".asset")) return false;
                var t = AssetDatabase.GetMainAssetTypeAtPath(path);
                return t != null && Watched.Contains(t.Name);
            });
            if (!relevant) return;
            var settings = SettingsEnumGeneratorCore.FindSettings();
            if (settings == null || !settings.autoGenerateOnAssetChange) return;
            EditorApplication.delayCall += () => { var r = SettingsEnumGeneratorCore.Generate(settings); if (r.HasErrors) foreach (var e in r.Errors) Debug.LogError($"[SettingsEnumGen Auto] {e}"); };
        }
    }
}
#endif
