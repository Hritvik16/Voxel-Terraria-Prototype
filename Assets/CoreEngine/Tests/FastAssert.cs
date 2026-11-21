// Assets/CoreEngine/Tests/FastAssert.cs
//
// Task 184b (test-suite trim): NUnit's Assert.AreEqual / CollectionAssert.AreEqual on two
// 2 MB voxel arrays compares element by element through boxed objects. When the arrays
// are equal (the passing case, every run) that is pure overhead. BytesEqual checks the
// bytes first and returns only when they are identical; on ANY difference (or a null /
// a length mismatch) it calls NUnit's own Assert.AreEqual, so a failing test fails the
// same way with the same message. Same assertion, no weakening.
using System;
using System.Collections.Generic;
using NUnit.Framework;

public static class FastAssert
{
    public static bool Same(byte[] expected, byte[] actual)
    {
        if (expected == null || actual == null || expected.Length != actual.Length) return false;
        for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) return false;
        return true;
    }

    /// Assert.AreEqual(expected, actual, message) for byte arrays.
    public static void BytesEqual(byte[] expected, byte[] actual, string message)
    {
        if (Same(expected, actual)) return;
        Assert.AreEqual(expected, actual, message);
    }

    public static bool Same<T>(T[] expected, T[] actual) where T : IEquatable<T>
    {
        if (expected == null || actual == null || expected.Length != actual.Length) return false;
        var eq = EqualityComparer<T>.Default;
        for (int i = 0; i < expected.Length; i++) if (!eq.Equals(expected[i], actual[i])) return false;
        return true;
    }

    /// CollectionAssert.AreEqual(expected, actual, message) for arrays of a primitive.
    public static void ArrayCollectionEqual<T>(T[] expected, T[] actual, string message) where T : IEquatable<T>
    {
        if (Same(expected, actual)) return;
        CollectionAssert.AreEqual(expected, actual, message);
    }

    /// CollectionAssert.AreEqual(expected, actual, message) for byte arrays.
    public static void BytesCollectionEqual(byte[] expected, byte[] actual, string message)
    {
        if (Same(expected, actual)) return;
        CollectionAssert.AreEqual(expected, actual, message);
    }
}
