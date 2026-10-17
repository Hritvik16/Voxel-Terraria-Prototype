// Assets/CoreEngine/Simulation/FluidFall.cs
//
// Task 94 (AMENDMENT_8_16_FLUID_FALL.md): GRAVITY FALL, the same for every fluid.
//
// A fluid cell with air below it falls several voxels per tick, faster each tick
// it keeps falling. Its only state is k = the number of consecutive ticks it has
// been falling (8 bits; GPU: FluidSlot.stateFlags [15:8], CPU oracle: a side
// array). How far tick k moves is read from ONE integer table, built here once
// per tick period and given to both FluidReferenceCPU and FluidCA.compute, so
// the oracle and the shader cannot disagree by a float rounding:
//
//   Table[k] = whole voxels fallen after k ticks of free fall, k = 0..255;
//   tick k (1-based) moves Table[k] - Table[k-1] voxels (k saturates at 255).
//
// The table is the EXACT distance g t^2 / 2 (t = k * dt), capped at a terminal
// speed, floored to voxels -- so the front is within one voxel of the integrated
// fall at every tick. Two adjustments, both recorded in the amendment:
//   1. The terminal speed is rounded to a WHOLE number of voxels per tick
//      (12 m/s nominal -> 27 voxels/tick = 12.15 m/s at 4.5 Hz). Once the cap is
//      reached every tick then moves exactly that many voxels, so k saturating
//      at 255 loses nothing (at 4.5 Hz the cap is reached at k = 6).
//   2. Table[k] >= k: never slower than the pre-94 one voxel per tick (only
//      matters at high tick rates, where g dt^2 / 2 is under one voxel).
using System;

public static class FluidFall
{
    public const double GravityMps2 = 9.81;
    /// Nominal terminal speed (m/s). Worker's pick (task 94): a falling water
    /// drop's real terminal speed is ~9 m/s, a falling sheet's more; 12 m/s
    /// is reached after 1.2 s / 7.3 m of fall.
    public const double TerminalMps = 12.0;
    public const double VoxelM = 0.1;
    public const int TableSize = 256;
    /// k's field: FluidSlot.stateFlags [15:8] on the GPU.
    public const int StateShift = 8;
    public const uint StateMask = 0xFFu << StateShift;

    /// The tick period every fluid sim uses unless told otherwise: the fixed
    /// rate of task 91's FluidTickScheduler.
    public static double DefaultTickSeconds => 1.0 / 4.5;

    /// Voxels per tick at the terminal speed, a whole number (>= 1).
    public static int TerminalVoxelsPerTick(double tickSeconds)
        => Math.Max(1, (int)Math.Round(TerminalMps * tickSeconds / VoxelM));

    /// The terminal speed actually used (m/s): TerminalVoxelsPerTick's.
    public static double TerminalMpsFor(double tickSeconds)
        => TerminalVoxelsPerTick(tickSeconds) * VoxelM / tickSeconds;

    /// Exact fall distance in METRES after t seconds from rest: g t^2 / 2 up to
    /// the (rounded) terminal speed, then linear.
    public static double DistanceM(double t, double tickSeconds)
    {
        double vc = TerminalMpsFor(tickSeconds);
        double tc = vc / GravityMps2;
        if (t <= tc) return 0.5 * GravityMps2 * t * t;
        return 0.5 * GravityMps2 * tc * tc + vc * (t - tc);
    }

    /// Speed (m/s) after t seconds of fall.
    public static double SpeedMps(double t, double tickSeconds)
        => Math.Min(GravityMps2 * t, TerminalMpsFor(tickSeconds));

    public static int[] BuildTable(double tickSeconds)
    {
        if (!(tickSeconds > 0.0)) throw new ArgumentOutOfRangeException(nameof(tickSeconds));
        var t = new int[TableSize];
        for (int k = 1; k < TableSize; k++)
        {
            // +1e-9: the linear tail is an exact integer per tick; keep a value
            // like 53.999999999 from flooring to 53.
            double vox = DistanceM(k * tickSeconds, tickSeconds) / VoxelM + 1e-9;
            long f = (long)Math.Floor(vox);
            if (f > int.MaxValue / 2) f = int.MaxValue / 2;
            t[k] = Math.Max((int)f, Math.Max(k, t[k - 1] + 1));
        }
        return t;
    }

    /// Voxels tick k (1-based, saturated at TableSize - 1) moves.
    public static int Step(int[] table, int k)
    {
        if (k < 1) k = 1;
        if (k > TableSize - 1) k = TableSize - 1;
        return table[k] - table[k - 1];
    }

    /// The largest step in the table: how far one tick can reach.
    public static int MaxStep(int[] table)
    {
        int m = 1;
        for (int k = 1; k < TableSize; k++) m = Math.Max(m, table[k] - table[k - 1]);
        return m;
    }
}
