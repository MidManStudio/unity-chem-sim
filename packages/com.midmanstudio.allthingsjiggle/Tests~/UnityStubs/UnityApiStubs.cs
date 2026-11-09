// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests~/UnityStubs"
// ============================================================================
// The parts of the Unity 2022.3 API that the Unity layer, the editor code and the editor tests use, written from the
// Unity scripting reference with the same names, namespaces and signatures. Nothing here runs. The projects next to
// this file compile the Unity layer against it to catch what a build without Unity can catch: a misspelled member,
// a wrong argument, a missing using, a unsafe block in the wrong place, a Core internal used across assemblies. It does
// not tell whether the real engine behaves as the code expects, and a member added here that does not exist in Unity
// would hide an error, so every addition is checked against the reference.
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
        public static void DontDestroyOnLoad(Object target) { }
        public static T FindFirstObjectByType<T>() where T : Object { return null; }
    }

    public class Component : Object
    {
        public Transform transform { get; }
        public GameObject gameObject { get; }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled { get; }
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform { get; }
        public int layer { get; set; }
        public T AddComponent<T>() where T : Component { return null; }
    }

    public sealed class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 lossyScale { get; }
        public Vector3 right { get; }
        public Vector3 up { get; }
        public Vector3 forward { get; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Transform parent { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public int childCount { get; }
        public Matrix4x4 localToWorldMatrix { get; }
        public Transform GetChild(int index) { return null; }
        public Vector3 TransformPoint(Vector3 position) { return position; }
        public Vector3 InverseTransformPoint(Vector3 position) { return position; }
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
        public static float Distance(Vector3 a, Vector3 b) { return 0f; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
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
        public static Quaternion Inverse(Quaternion rotation) { return rotation; }
        public static Quaternion Euler(float x, float y, float z) { return identity; }
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) { return lhs; }
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
        public float m30, m31, m32, m33;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) { return value < min ? min : (value > max ? max : value); }
        public static float Clamp(float value, float min, float max) { return value < min ? min : (value > max ? max : value); }
        public static float Clamp01(float value) { return value < 0f ? 0f : (value > 1f ? 1f : value); }
        public static float Abs(float f) { return f < 0f ? -f : f; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Sin(float f) { return (float)Math.Sin(f); }
    }

    public class Camera : Behaviour
    {
        public static Camera main { get { return null; } }
    }

    public struct Plane
    {
        public Vector3 normal { get; set; }
        public float distance { get; set; }
    }

    public static class GeometryUtility
    {
        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes) { }
    }

    public struct LayerMask
    {
        public int value { get; set; }
        public static implicit operator int(LayerMask mask) { return mask.value; }
        public static implicit operator LayerMask(int intVal) { LayerMask l = new LayerMask(); l.value = intVal; return l; }
    }

    public sealed class AnimationCurve
    {
        public int length { get; }
        public static AnimationCurve Constant(float timeStart, float timeEnd, float value) { return new AnimationCurve(); }
        public static AnimationCurve Linear(float timeStart, float valueStart, float timeEnd, float valueEnd) { return new AnimationCurve(); }
        public float Evaluate(float time) { return 0f; }
    }

    public static class Application
    {
        public static bool isPlaying { get; }
    }

    public static class Time
    {
        public static float deltaTime { get; }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
    }

    public static class Gizmos
    {
        public static Color color { get; set; }
        public static void DrawLine(Vector3 from, Vector3 to) { }
        public static void DrawSphere(Vector3 center, float radius) { }
        public static void DrawWireSphere(Vector3 center, float radius) { }
    }

    public sealed class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string[] fontnames, int size) { return null; }
    }

    public sealed class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public Font font { get; set; }
        public bool wordWrap { get; set; }
    }

    public class GUILayoutOption
    {
    }

    public static class GUILayout
    {
        public static int Toolbar(int selected, string[] texts, params GUILayoutOption[] options) { return selected; }
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4,
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float min) { }
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

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { }
    }
}

namespace UnityEngine.Jobs
{
    public struct TransformAccess
    {
        public bool isValid { get; }
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Matrix4x4 localToWorldMatrix { get; }
    }

    public struct TransformAccessArray : IDisposable
    {
        public TransformAccessArray(Transform[] transforms, int desiredJobCount) { length = 0; isCreated = true; }
        public TransformAccessArray(int capacity, int desiredJobCount) { length = 0; isCreated = true; }
        public int length { get; }
        public bool isCreated { get; }
        public void Dispose() { }
    }

    public interface IJobParallelForTransform
    {
        void Execute(int index, TransformAccess transform);
    }

    public static class IJobParallelForTransformExtensions
    {
        public static Unity.Jobs.JobHandle Schedule<T>(this T jobData, TransformAccessArray transforms, Unity.Jobs.JobHandle dependsOn = default)
            where T : struct, IJobParallelForTransform
        {
            return default;
        }

        public static Unity.Jobs.JobHandle ScheduleReadOnly<T>(
            this T jobData, TransformAccessArray transforms, int batchSize, Unity.Jobs.JobHandle dependsOn = default)
            where T : struct, IJobParallelForTransform
        {
            return default;
        }
    }
}

namespace Unity.Jobs
{
    public struct JobHandle
    {
        public void Complete() { }
    }

    public interface IJob
    {
        void Execute();
    }

    public interface IJobParallelFor
    {
        void Execute(int index);
    }

    public static class IJobExtensions
    {
        public static JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = default) where T : struct, IJob { return default; }
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default)
            where T : struct, IJobParallelFor
        {
            return default;
        }
    }
}

namespace Unity.Jobs.LowLevel.Unsafe
{
    public static class JobsUtility
    {
        public static int JobWorkerCount { get; set; }
    }
}

namespace Unity.Collections
{
    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        TempJob = 3,
        Persistent = 4,
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NativeDisableUnsafePtrRestrictionAttribute : Attribute
    {
    }

    public static unsafe class UnsafeUtility
    {
        public static void* Malloc(long size, int alignment, Allocator allocator) { return null; }
        public static void Free(void* memory, Allocator allocator) { }
        public static void MemClear(void* destination, long size) { }
        public static int SizeOf<T>() where T : struct { return 0; }
        public static int AlignOf<T>() where T : struct { return 0; }
    }
}

namespace Unity.Burst
{
    public enum FloatMode
    {
        Default = 0,
        Strict = 1,
        Deterministic = 2,
        Fast = 3,
    }

    public enum FloatPrecision
    {
        Standard = 0,
        High = 1,
        Medium = 2,
        Low = 3,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Assembly)]
    public sealed class BurstCompileAttribute : Attribute
    {
        public FloatMode FloatMode { get; set; }
        public FloatPrecision FloatPrecision { get; set; }
        public bool CompileSynchronously { get; set; }
        public BurstCompileAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BurstDiscardAttribute : Attribute
    {
    }
}

namespace UnityEditor
{
    public class EditorWindow : UnityEngine.ScriptableObjectStub
    {
        public static T GetWindow<T>(string title) where T : EditorWindow { return null; }
        public void Repaint() { }
    }

    public class Editor : UnityEngine.ScriptableObjectStub
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

    [Flags]
    public enum GizmoType
    {
        NonSelected = 32,
        Selected = 1,
        Active = 8,
        Pickable = 2,
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DrawGizmo : Attribute
    {
        public DrawGizmo(GizmoType gizmo) { }
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
        public static Enum EnumPopup(string label, Enum selected, params UnityEngine.GUILayoutOption[] options) { return selected; }
        public static int Popup(string label, int selectedIndex, string[] displayedOptions, params UnityEngine.GUILayoutOption[] options) { return selectedIndex; }
        public static int IntSlider(string label, int value, int leftValue, int rightValue, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static float Slider(string label, float value, float leftValue, float rightValue, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static bool ToggleLeft(string label, bool value, params UnityEngine.GUILayoutOption[] options) { return value; }
        public static string TextField(string label, string text, params UnityEngine.GUILayoutOption[] options) { return text; }
        public static string TextArea(string text, UnityEngine.GUIStyle style, params UnityEngine.GUILayoutOption[] options) { return text; }
        public static UnityEngine.Vector2 BeginScrollView(UnityEngine.Vector2 scrollPosition, params UnityEngine.GUILayoutOption[] options) { return scrollPosition; }
        public static void EndScrollView() { }
        public static void BeginHorizontal(params UnityEngine.GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
    }

    public static class EditorStyles
    {
        public static UnityEngine.GUIStyle textArea { get; }
    }

    public static class EditorGUIUtility
    {
        public static string systemCopyBuffer { get; set; }
    }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction update;
        public static double timeSinceStartup { get { return 0.0; } }
    }

    public static class Selection
    {
        public static UnityEngine.GameObject activeGameObject { get; set; }
    }

    public static class EditorUtility
    {
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static bool DisplayCancelableProgressBar(string title, string info, float progress) { return false; }
        public static void ClearProgressBar() { }
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) { return ""; }
    }
}

namespace UnityEngine
{
    public class ScriptableObjectStub : Object
    {
    }
}
