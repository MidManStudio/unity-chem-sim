// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests~/UnityStubs"
// ============================================================================
// The parts of the Unity 2022.3 API that the Unity layer, the editor code and the Unity side tests use, written from the
// Unity scripting reference with the same names, namespaces and signatures. Nothing here runs. The projects next to this
// file compile the Unity layer against it to catch what a build without Unity can catch: a misspelled member, a wrong
// argument, a missing using. It does not tell whether the engine behaves as the code expects, and a member added here
// that does not exist in Unity would hide an error, so every addition is checked against the reference.
#pragma warning disable CS0067, CS0108, CS0114, CS0169, CS0649, CS0659, CS0660, CS0661, CS8981, CS0414
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !ReferenceEquals(a, b); }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static T Instantiate<T>(T original) where T : Object { return original; }
        public static T Instantiate<T>(T original, Transform parent) where T : Object { return original; }
    }

    public class Component : Object
    {
        public Transform transform { get; }
        public GameObject gameObject { get; }
        public T GetComponent<T>() { return default; }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject { return null; }
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform { get; }
        public int layer { get; set; }
        public bool isStatic { get; set; }
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default; }
    }

    public sealed class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion localRotation { get; set; }
        public Transform parent { get; set; }
        public int childCount { get; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public Transform GetChild(int index) { return null; }
    }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float d) { return a; }
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0f, 0f, 0f, 1f); } }
        public static Quaternion Euler(float x, float y, float z) { return identity; }
        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection) { return identity; }
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) { return lhs; }
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) { return value < min ? min : (value > max ? max : value); }
        public static float Clamp(float value, float min, float max) { return value < min ? min : (value > max ? max : value); }
        public static float Clamp01(float value) { return value < 0f ? 0f : (value > 1f ? 1f : value); }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int RoundToInt(float f) { return (int)Math.Round(f); }
    }

    public class Camera : Behaviour
    {
        public static Camera main { get { return null; } }
    }

    public static class Application
    {
        public static bool isPlaying { get; }
    }

    public static class Time
    {
        public static float unscaledDeltaTime { get; }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
    }

    public enum MeshTopology
    {
        Triangles = 0,
        Quads = 2,
        Lines = 3,
        LineStrip = 4,
        Points = 5,
    }

    public sealed class Mesh : Object
    {
        public Mesh() { }
        public int vertexCount { get; }
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Color32[] colors32 { get; set; }
        public int[] triangles { get; set; }
        public Bounds bounds { get; set; }
        public UnityEngine.Rendering.IndexFormat indexFormat { get; set; }
        public void Clear() { }
        public void SetIndices(int[] indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
    }

    public class Renderer : Component
    {
        public Material sharedMaterial { get; set; }
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public sealed class MeshRenderer : Renderer
    {
    }

    public sealed class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
    }

    public class Collider : Component
    {
        public bool enabled { get; set; }
    }

    public sealed class MeshCollider : Collider
    {
        public Mesh sharedMesh { get; set; }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) { return null; }
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Shader shader { get; set; }
    }

    public enum FilterMode
    {
        Point = 0,
        Bilinear = 1,
        Trilinear = 2,
    }

    public enum TextureFormat
    {
        Alpha8 = 1,
        RGB24 = 3,
        RGBA32 = 4,
    }

    public enum ScaleMode
    {
        StretchToFill = 0,
        ScaleAndCrop = 1,
        ScaleToFit = 2,
    }

    public class Texture : Object
    {
        public int width { get; }
        public int height { get; }
        public FilterMode filterMode { get; set; }
    }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public bool isReadable { get; }
        public Color32[] GetPixels32() { return null; }
        public void SetPixels32(Color32[] colors) { }
        public void Apply() { }
    }

    public class GUILayoutOption
    {
    }

    public static class GUI
    {
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode) { }
    }

    public static class GUILayout
    {
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static GUILayoutOption Width(float width) { return null; }
        public static GUILayoutOption Height(float height) { return null; }
    }

    public static class GUILayoutUtility
    {
        public static Rect GetRect(float width, float height) { return default; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float min) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; }
        public string menuName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AddComponentMenu : Attribute
    {
        public AddComponentMenu(string menuName) { }
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat
    {
        UInt16 = 0,
        UInt32 = 1,
    }

    public enum ShadowCastingMode
    {
        Off = 0,
        On = 1,
        TwoSided = 2,
        ShadowsOnly = 3,
    }
}

namespace UnityEditor
{
    public class EditorWindow : UnityEngine.ScriptableObject
    {
        public static T GetWindow<T>(string title) where T : EditorWindow { return null; }
        public UnityEngine.Rect position { get; set; }
        public void Show() { }
        public void Repaint() { }
    }

    public class Editor : UnityEngine.ScriptableObject
    {
        public UnityEngine.Object target { get; }
        public virtual void OnInspectorGUI() { }
        public bool DrawDefaultInspector() { return false; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CustomEditorAttribute : Attribute
    {
        public CustomEditorAttribute(Type inspectedType) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string itemName) { }
    }

    public enum MessageType
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public static class EditorGUI
    {
        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() { return false; }
    }

    public static class EditorGUILayout
    {
        public static void Space() { }
        public static void LabelField(string label) { }
        public static void LabelField(string label, string label2, params UnityEngine.GUILayoutOption[] options) { }
        public static void HelpBox(string message, MessageType type) { }
        public static UnityEngine.Object ObjectField(string label, UnityEngine.Object obj, Type objType, bool allowSceneObjects, params UnityEngine.GUILayoutOption[] options) { return obj; }
        public static Enum EnumPopup(string label, Enum selected, params UnityEngine.GUILayoutOption[] options) { return selected; }
        public static int Popup(string label, int selectedIndex, string[] displayedOptions, params UnityEngine.GUILayoutOption[] options) { return selectedIndex; }
        public static int IntField(string label, int value, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static float FloatField(string label, float value, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static double DoubleField(string label, double value, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static bool Toggle(string label, bool value, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static string TextField(string label, string text, params UnityEngine.GUILayoutOption[] options) { return text; }
        public static float Slider(string label, float value, float leftValue, float rightValue, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static bool Foldout(bool foldout, string content) { return foldout; }
        public static UnityEngine.Vector2 BeginScrollView(UnityEngine.Vector2 scrollPosition, params UnityEngine.GUILayoutOption[] options) { return scrollPosition; }
        public static void EndScrollView() { }
        public static void BeginHorizontal(params UnityEngine.GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
    }

    public static class Undo
    {
        public static void RecordObject(UnityEngine.Object objectToUndo, string name) { }
        public static void RegisterCreatedObjectUndo(UnityEngine.Object objectToUndo, string name) { }
        public static void SetCurrentGroupName(string name) { }
        public static int GetCurrentGroup() { return 0; }
        public static void CollapseUndoOperations(int groupIndex) { }
    }

    public static class AssetDatabase
    {
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
        public static bool IsValidFolder(string path) { return false; }
        public static string CreateFolder(string parentFolder, string newFolderName) { return ""; }
    }

    public static class Selection
    {
        public static UnityEngine.Object activeObject { get; set; }
        public static UnityEngine.GameObject activeGameObject { get; set; }
    }

    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok, string cancel) { return false; }
        public static bool DisplayCancelableProgressBar(string title, string info, float progress) { return false; }
        public static void ClearProgressBar() { }
        public static string SaveFilePanelInProject(string title, string defaultName, string extension, string message) { return ""; }
        public static void SetDirty(UnityEngine.Object target) { }
    }

    public static class PrefabUtility
    {
        public static UnityEngine.Object InstantiatePrefab(UnityEngine.Object assetComponentOrGameObject, UnityEngine.Transform parent) { return null; }
    }
}
