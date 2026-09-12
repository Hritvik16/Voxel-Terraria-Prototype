// Assets/CoreEngine/Tests/FluidReadbackInvariantTests.cs
//
// Pins FluidOpListReadback.MaxFramesInFlight = 1.
//
// WHY THIS IS A TEST AND NOT A COMMENT. That constant looks exactly like a
// throughput knob -- "only one op-list in flight, surely we can pipeline more".
//
// ORIGINALLY it was the conservation fix: the CA decides moves from terrain
// (§7.3 Air-Only), its own commits only reach terrain after the CPU applies the
// op-list and re-uploads, and a second tick run before that landed decided
// against terrain missing its own previous decisions and committed the same
// source twice. Measured at the time: sand_column GPU 25 vs CPU 24, a GAIN.
//
// THAT IS NO LONGER WHY IT IS 1, AND THIS COMMENT WAS CORRECTED RATHER THAN
// LEFT TO ROT. The atomic self-validating move op added afterwards -- one
// record naming both cells, re-checked against current terrain and applied
// both-or-neither -- now guarantees conservation independently. Re-measured
// tonight with the throttle deliberately disabled (-inflight3), full sweep:
//     conserved count MATCHED on all five scenarios (150,24,62,150,150)
// So raising it no longer loses or gains mass.
//
// WHAT IT COSTS NOW, measured in the same run:
//     stale ops dropped: 9535 / 786 / 62 / 9966 / 10446 per scenario --
//         roughly 60% of ALL emitted ops thrown away as decided-against-stale
//     steady-state match fell from 4 of 5 (at 1) to 3 of 5 (at 3)
// So at >1 the CA burns most of its work re-deciding moves that will be
// discarded, and settles less well. It is now an EFFICIENCY AND QUALITY bound
// rather than a safety one.
//
// Raising it is therefore a defensible design discussion -- and it is a
// DISCUSSION, not a silent edit, which is what this test enforces.
//
// To watch it for yourself:
//     open -n -W Builds/Phase5bValidation.app --args -phase5brig -inflight3
// and read the "stale dropped" and match counts.
using NUnit.Framework;
using VoxelEngine.Simulation;

public class FluidReadbackInvariantTests
{
    [Test]
    public void MaxFramesInFlight_DefaultIsOne()
    {
        Assert.AreEqual(1, FluidOpListReadback.MaxFramesInFlightDefault,
            "MaxFramesInFlight is pinned at 1. Conservation no longer depends on it " +
            "(the self-validating move op holds that), but at 3 the sweep drops ~60% " +
            "of its ops as stale and steady-state match falls from 4/5 to 3/5. " +
            "Raising it is a design discussion, not a silent edit.");
    }

    [Test]
    public void MaxFramesInFlight_RuntimeValueStartsAtTheDefault()
    {
        // The settable property exists only for the rig's -inflight override and
        // for this test. Anything that leaves it raised has broken the invariant
        // for every subsequent scene in the process.
        Assert.AreEqual(FluidOpListReadback.MaxFramesInFlightDefault,
                        FluidOpListReadback.MaxFramesInFlight,
            "something raised MaxFramesInFlight and did not restore it");
    }

    [Test]
    public void RaisingIt_IsPossibleButLoudlyDocumented()
    {
        // Proves the override actually works, so the -inflight reproduction path
        // cannot silently stop functioning, then restores the invariant.
        int original = FluidOpListReadback.MaxFramesInFlight;
        try
        {
            FluidOpListReadback.MaxFramesInFlight = 3;
            Assert.AreEqual(3, FluidOpListReadback.MaxFramesInFlight);
        }
        finally
        {
            FluidOpListReadback.MaxFramesInFlight = original;
        }
        Assert.AreEqual(1, FluidOpListReadback.MaxFramesInFlight, "the invariant was not restored");
    }
}
