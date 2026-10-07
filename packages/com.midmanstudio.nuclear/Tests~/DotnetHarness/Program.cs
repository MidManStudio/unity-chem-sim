// Runs the NUnit fixtures in Tests/Editor against a real nuclear_core library,
// outside Unity. Usage: NuclearHarness <path to library> [--expect-exports N]
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace MidManStudio.Nuclear.Harness
{
    internal static class Program
    {
        private static IntPtr library;

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: NuclearHarness <library path> [--expect-exports N]");
                return 2;
            }
            int expectExports = -1;
            for (int i = 1; i + 1 < args.Length; i++)
            {
                if (args[i] == "--expect-exports")
                {
                    expectExports = int.Parse(args[i + 1]);
                }
            }
            library = NativeLibrary.Load(Path.GetFullPath(args[0]));
            NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, Resolve);

            int failures = CheckExports(expectExports) + RunFixtures();
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
        {
            return name == "nuclear_core" ? library : IntPtr.Zero;
        }

        // Every DllImport must resolve to a symbol in the library, including the
        // ones no test calls, and the counts must agree so a function added in
        // Rust cannot be forgotten in C#.
        private static int CheckExports(int expected)
        {
            int imports = 0;
            int failures = 0;
            MethodInfo[] methods = typeof(NuclearNative).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (MethodInfo method in methods)
            {
                if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
                {
                    continue;
                }
                imports++;
                IntPtr address;
                if (!NativeLibrary.TryGetExport(library, method.Name, out address))
                {
                    Console.WriteLine("MISSING EXPORT " + method.Name);
                    failures++;
                }
            }
            Console.WriteLine("DllImports: " + imports);
            if (expected >= 0 && imports != expected)
            {
                Console.WriteLine("EXPORT COUNT MISMATCH: the library exports " + expected + " nuc_ functions");
                failures++;
            }
            return failures;
        }

        private static int RunFixtures()
        {
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
                    try
                    {
                        method.Invoke(fixture, null);
                        passed++;
                        Console.WriteLine("PASS " + type.Name + "." + method.Name);
                    }
                    catch (TargetInvocationException e)
                    {
                        failed++;
                        Console.WriteLine("FAIL " + type.Name + "." + method.Name + ": " + e.InnerException);
                    }
                }
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed;
        }
    }
}
