// A small stand-in for the parts of NUnit 3 that NuclearNativeTests uses, so the
// same test file compiles and runs under plain .NET. The signatures match NUnit's.
using System;

namespace NUnit.Framework
{
    public delegate void TestDelegate();

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TestFixtureAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
    }

    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message)
        {
        }
    }

    public static class Assert
    {
        public static void Fail(string message)
        {
            throw new AssertionException(message);
        }

        public static void IsTrue(bool condition)
        {
            IsTrue(condition, "expected true");
        }

        public static void IsTrue(bool condition, string message)
        {
            if (!condition)
            {
                Fail(message);
            }
        }

        public static void IsFalse(bool condition)
        {
            IsTrue(!condition, "expected false");
        }

        public static void AreEqual(object expected, object actual)
        {
            AreEqual(expected, actual, string.Empty);
        }

        public static void AreEqual(object expected, object actual, string message)
        {
            if (!SameValue(expected, actual))
            {
                Fail("expected <" + expected + "> but was <" + actual + "> " + message);
            }
        }

        public static void AreEqual(double expected, double actual, double delta)
        {
            AreEqual(expected, actual, delta, string.Empty);
        }

        public static void AreEqual(double expected, double actual, double delta, string message)
        {
            if (!(Math.Abs(expected - actual) <= delta))
            {
                Fail("expected <" + expected + "> +/- " + delta + " but was <" + actual + "> " + message);
            }
        }

        public static void AreNotEqual(object expected, object actual)
        {
            if (SameValue(expected, actual))
            {
                Fail("expected a value different from <" + expected + ">");
            }
        }

        public static void Greater(double arg1, double arg2)
        {
            IsTrue(arg1 > arg2, "expected " + arg1 + " > " + arg2);
        }

        public static void Less(double arg1, double arg2)
        {
            IsTrue(arg1 < arg2, "expected " + arg1 + " < " + arg2);
        }

        public static T Throws<T>(TestDelegate code) where T : Exception
        {
            try
            {
                code();
            }
            catch (T e)
            {
                return e;
            }
            catch (Exception e)
            {
                Fail("expected " + typeof(T).Name + " but got " + e.GetType().Name + ": " + e.Message);
            }
            Fail("expected " + typeof(T).Name + " but nothing was thrown");
            return null;
        }

        private static bool IsNumber(object o)
        {
            return o is sbyte || o is byte || o is short || o is ushort || o is int || o is uint
                || o is long || o is ulong || o is float || o is double || o is decimal;
        }

        private static bool SameValue(object expected, object actual)
        {
            if (expected == null || actual == null)
            {
                return expected == null && actual == null;
            }
            if (IsNumber(expected) && IsNumber(actual))
            {
                if (expected is double || expected is float || actual is double || actual is float)
                {
                    return Convert.ToDouble(expected) == Convert.ToDouble(actual);
                }
                return Convert.ToDecimal(expected) == Convert.ToDecimal(actual);
            }
            return expected.Equals(actual);
        }
    }
}
