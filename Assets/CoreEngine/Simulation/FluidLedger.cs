// ==========================================
// Assets/CoreEngine/Simulation/FluidLedger.cs
//
// Phase 5a's conservation counter. §13 Phase 5a, build steps: "Build the
// conservation counter BEFORE the fluid rules -- it turns 'the water looks
// weird' into 'we lost exactly N drops on tick M.'"
//
// This file therefore contains no fluid rules at all and does not reference
// FluidReferenceCPU. It is a double-entry ledger over one quantity:
//
//     MOBILE BYTES = the count of voxels in the sandbox array whose material
//                    is IsFluid or IsFallingSolid (MaterialRules.IsMobileMask).
//
// §7.3's mass-conservation argument says that count changes for exactly three
// reasons and no others:
//   1. an EDIT put mobile material in                 -> RecordExternalAdd
//   2. an EDIT took mobile material out               -> RecordExternalRemove
//   3. a REACTION turned mobile material into
//      something that is not mobile (§7.6)            -> RecordReactionConsumed
// Motion itself -- Clear/Intent/Commit -- must change it by ZERO, because a
// commit writes one destination and clears one source. Any other delta is the
// Air-Only rule being violated, which is the failure signature §13 names.
//
// The ledger is the EXPECTATION. FluidReferenceCPU.CountMobileBytes() is the
// OBSERVATION (a full rescan of the array, deliberately dumb -- §0.1 invariant
// 9 -- so it cannot share a bug with the thing it audits). Check() compares
// them and, on a mismatch, reports the tick and the signed byte delta, which
// is the standard §13 sets for this phase.

using System.Text;

public struct FluidLedgerCheck
{
    public bool Ok;
    public int Tick;
    public long Expected;
    public long Actual;
    /// Actual minus expected. Negative = drops were LOST (the Air-Only rule was
    /// violated, per §13's failure signature). Positive = drops were DUPLICATED.
    public long Delta;
    public string Message;

    public override string ToString() => Message;
}

public sealed class FluidLedger
{
    private long _expected;
    private long _externalAdded;
    private long _externalRemoved;
    private long _reactionConsumed;

    private int _lastFailTick = -1;
    private long _lastFailDelta;

    /// The count of mobile bytes the sandbox array is required to hold.
    public long Expected => _expected;

    public long ExternalAdded => _externalAdded;
    public long ExternalRemoved => _externalRemoved;
    public long ReactionConsumed => _reactionConsumed;

    /// Tick of the first mismatch seen since Reset, or -1 if the ledger has
    /// never been out of balance. Kept so a multi-tick run can report where it
    /// first went wrong rather than only where it happened to be checked.
    public int FirstFailTick => _lastFailTick;
    public long FirstFailDelta => _lastFailDelta;

    /// Re-baseline to a known observed count. Used when seeding a scenario
    /// before ticking, so a test's arrange step does not have to be expressed
    /// as a sequence of Record* calls.
    public void ResetTo(long observedMobileBytes)
    {
        _expected = observedMobileBytes;
        _externalAdded = 0;
        _externalRemoved = 0;
        _reactionConsumed = 0;
        _lastFailTick = -1;
        _lastFailDelta = 0;
    }

    /// An edit wrote mobile material into a cell that did not hold any.
    public void RecordExternalAdd(long bytes)
    {
        _expected += bytes;
        _externalAdded += bytes;
    }

    /// An edit overwrote or removed mobile material (mining a drop, placing a
    /// block into a stream).
    public void RecordExternalRemove(long bytes)
    {
        _expected -= bytes;
        _externalRemoved += bytes;
    }

    /// §7.6: a reaction turned mobile material into a non-mobile product (or
    /// into Air). This is the ONLY way the simulation itself may legally reduce
    /// the count -- which is exactly why it is recorded separately from motion
    /// rather than folded into a single "expected" fudge.
    public void RecordReactionConsumed(long bytes)
    {
        _expected -= bytes;
        _reactionConsumed += bytes;
    }

    public FluidLedgerCheck Check(int tick, long actualMobileBytes)
    {
        long delta = actualMobileBytes - _expected;
        var result = new FluidLedgerCheck
        {
            Ok = delta == 0,
            Tick = tick,
            Expected = _expected,
            Actual = actualMobileBytes,
            Delta = delta,
        };

        if (result.Ok)
        {
            result.Message = $"tick {tick}: {actualMobileBytes} mobile bytes, ledger balanced";
            return result;
        }

        if (_lastFailTick < 0)
        {
            _lastFailTick = tick;
            _lastFailDelta = delta;
        }

        var sb = new StringBuilder();
        sb.Append("CONSERVATION BROKEN on tick ").Append(tick).Append(": expected ")
          .Append(_expected).Append(" mobile bytes, counted ").Append(actualMobileBytes)
          .Append(" -- ")
          .Append(delta < 0 ? "LOST " : "GAINED ")
          .Append(delta < 0 ? -delta : delta)
          .Append(delta < 0
              ? " (§13 failure signature: a drop claimed a cell that was not Air at tick-start; check the Air-Only comparison in Intent)"
              : " (a source was committed to more than one destination, or a source home was not cleared)");
        sb.Append("\n  ledger since reset: +").Append(_externalAdded).Append(" placed by edits, -")
          .Append(_externalRemoved).Append(" removed by edits, -")
          .Append(_reactionConsumed).Append(" consumed by reactions");
        if (_lastFailTick >= 0 && _lastFailTick != tick)
            sb.Append("\n  first went out of balance on tick ").Append(_lastFailTick)
              .Append(" by ").Append(_lastFailDelta);
        result.Message = sb.ToString();
        return result;
    }
}
