// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests~/DotnetHarness"
// ============================================================================
// Runs the NUnit fixtures in Tests/Editor outside Unity. Usage: TerrainHarness [name filter]
using System;
using System.Reflection;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Harness
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            int passed = 0;
            int failed = 0;
            foreach (Type type in typeof(Program).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(TestFixtureAttribute), false).Length == 0)
                {
                    continue;
                }
                object fixture = Activator.CreateInstance(type);
                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                Array.Sort(methods, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (MethodInfo method in methods)
                {
                    if (method.GetCustomAttributes(typeof(TestAttribute), false).Length == 0)
                    {
                        continue;
                    }
                    string name = type.Name + "." + method.Name;
                    if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    try
                    {
                        method.Invoke(fixture, null);
                        passed++;
                        Console.WriteLine("PASS " + name);
                    }
                    catch (TargetInvocationException e)
                    {
                        failed++;
                        Console.WriteLine("FAIL " + name + ": " + e.InnerException);
                    }
                }
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            Console.WriteLine(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            return failed == 0 ? 0 : 1;
        }
    }
}
