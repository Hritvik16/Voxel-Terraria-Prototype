// Assets/CoreEngine/Tests/UploadRingTests.cs
//
// Task 168e (cut E): the upload staging ring (UploadRing.cs) must leave every destination word exactly as
// sequential SetData would. Driven on CpuUploadRing, whose "GPU" runs each dispatch LATENCY frames after it
// was issued and reads the staging slot as it is THEN -- so reusing a slot before its copies ran corrupts
// the result, exactly the GPU hazard the readback guard exists for. Random writes (uint[] / int[] /
// NativeArray sources, sizes from 1 word to more than a slot, overlapping writes inside one flush, flushes
// forgotten across frames), random latency 0-4 frames; after a drain the three destinations == a reference
// built with plain Array.Copy in call order, byte for byte. Plus a NEGATIVE CONTROL: the same run with the
// guards ignored must differ (the test can see a hazard), and no dispatch may hold overlapping records.
using System;
using NUnit.Framework;
using Unity.Collections;
using VoxelEngine.Memory;

public class UploadRingTests
{
    private static int Run(int seed, bool ignoreGuards, double frameEndP, int latency, out CpuUploadRing ring)
    {
        var rnd = new System.Random(seed);
        const int N = 20000;
        var dst = new[] { new uint[N], new uint[N], new uint[N] };
        var refd = new[] { new uint[N], new uint[N], new uint[N] };
        ring = new CpuUploadRing(slots: 4, slotUints: 4096, recCap: 48) { Latency = latency >= 0 ? latency : rnd.Next(0, 5), DebugIgnoreGuards = ignoreGuards };
        var na = new NativeArray<uint>(9000, Allocator.Temp);
        try
        {
            int lastDst = 0, lastAt = 0, lastN = 0;
            for (int op = 0; op < 3000; op++)
            {
                int d = rnd.Next(3);
                int count = rnd.NextDouble() < 0.02 ? rnd.Next(4097, 9000) : rnd.NextDouble() < 0.3 ? rnd.Next(1, 40) : rnd.Next(1, 1500);
                int at = rnd.Next(0, N - count);
                if (rnd.NextDouble() < 0.1 && lastN > 0) { d = lastDst; at = math_clamp(lastAt + rnd.Next(-lastN, lastN), 0, N - count); }   // overlap the previous write
                int kind = rnd.Next(3);
                if (kind == 0)
                {
                    var src = new uint[count + 7];
                    for (int i = 0; i < src.Length; i++) src[i] = (uint)rnd.Next() ^ ((uint)op << 20);
                    ring.Write(dst[d], src, 7, at, count);
                    Array.Copy(src, 7, refd[d], at, count);
                }
                else if (kind == 1)
                {
                    var src = new int[count];
                    for (int i = 0; i < src.Length; i++) src[i] = rnd.Next(int.MinValue, int.MaxValue);
                    ring.Write(dst[d], src, 0, at, count);
                    for (int i = 0; i < count; i++) refd[d][at + i] = unchecked((uint)src[i]);
                }
                else
                {
                    int s0 = rnd.Next(0, 9000 - count + 1);
                    for (int i = 0; i < count; i++) na[s0 + i] = (uint)rnd.Next() + (uint)op;
                    ring.Write(dst[d], na, s0, at, count);
                    for (int i = 0; i < count; i++) refd[d][at + i] = na[s0 + i];
                }
                lastDst = d; lastAt = at; lastN = count;
                double r = rnd.NextDouble();
                if (r < 0.25) ring.Flush();
                if (r < frameEndP || (r > 0.95 && r < 0.95 + frameEndP / 7)) { if (rnd.NextDouble() < 0.8) ring.Tick(); ring.EndFrame(); }   // the second range: a frame ends with an unflushed pass
            }
            ring.Drain();
        }
        finally { na.Dispose(); }
        int diff = 0;
        for (int d = 0; d < 3; d++) for (int i = 0; i < N; i++) if (dst[d][i] != refd[d][i]) diff++;
        return diff;
    }

    private static int math_clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    [Test]
    public void Drained_EqualsSequentialSetData_ByteForByte()
    {
        long temps = 0, splits = 0, rotations = 0, waits = 0, forgotten = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            int diff = Run(seed, false, seed % 2 == 0 ? 0.35 : 0.05, -1, out var ring);
            Assert.AreEqual(0, diff, $"seed {seed}: {diff} words differ from sequential SetData ({ring.Report()})");
            Assert.AreEqual(0, ring.OverlapViolations, $"seed {seed}: a dispatch held overlapping records");
            Assert.LessOrEqual(ring.TempSlotUintsMax, ring.SlotUints, $"seed {seed}: a temporary larger than a slot (writes must go in slot-sized pieces)");
            temps += ring.TempsTotal; splits += ring.OverlapSplitsTotal; rotations += ring.RotationsTotal; waits += ring.WaitsTotal;
            forgotten += ring.FlushesTotal;
        }
        // The run exercised what it claims to: slot rotations, temporaries (writes over a slot and busy slots),
        // overlap splits inside a flush.
        Assert.Greater(rotations, 100, "rotations");
        Assert.Greater(temps, 0, "temporary slots");
        Assert.Greater(splits, 0, "overlap splits");
        UnityEngine.Debug.Log($"[UploadRingTests] 12 seeds: rotations {rotations} temps {temps} overlap-splits {splits} waits {waits} flushes {forgotten}");
    }

    [Test]
    public void NegativeControl_IgnoringTheGuards_Corrupts()
    {
        int corrupted = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            int diff = Run(seed, true, 0.05, 3, out var ring);
            if (diff > 0) corrupted++;
        }
        Assert.Greater(corrupted, 6, "reusing staging slots before the GPU ran their copies must show up as differences");
    }

    [Test]
    public void FrameBudget_OffOrNoGpu_IsTheDefault()
    {
        bool was = UploadRing.Enabled;
        try
        {
            UploadRing.Enabled = false;
            Assert.AreEqual(3 << 20, UploadRing.FrameBudget(3 << 20));
            UploadRing.Enabled = true;   // -nographics: no GPU ring can exist, so writes stay SetData and the cap stays
            if (!UploadRing.On) Assert.AreEqual(3 << 20, UploadRing.FrameBudget(3 << 20));
        }
        finally { UploadRing.Enabled = was; }
    }
}
