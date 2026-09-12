// ==========================================
// Assets/CoreEngine/Tests/FluidConservationTests.cs
//
// Phase 5a, file 2 of §13's ordered list. Runs entirely against
// FluidReferenceCPU -- no ChunkStore, no StreamManager, no GPU.
//
// THE REPORTING STANDARD THIS FILE IS HELD TO
// §13 Phase 5a: the conservation counter exists so that "the water looks
// weird" becomes "we lost exactly N drops on tick M." Every conservation
// assertion in this file therefore fails with the TICK and the SIGNED BYTE
// DELTA, never a bare pass/fail -- that is what FluidLedgerCheck.Message
// carries and what Assert is handed.
//
// TEST ORDER MIRRORS THE BUILD ORDER
// §13's build steps require gravity-only proven before down-diagonals, and
// down-diagonals before horizontals. The regions below are in that order and
// were added in that order; each region's tests were green before the next
// tier existed in FluidReferenceCPU.cs.

using NUnit.Framework;
using Unity.Mathematics;

public class FluidConservationTests
{
    // A 16 x 32 x 16 voxel sandbox (2 x 4 x 2 bricks). Small enough that a
    // failing test can be dumped by eye, tall enough for a real fall.
    private const int BX = 2, BY = 4, BZ = 2;

    // Every FluidReferenceCPU any test builds is registered here so [TearDown]
    // can check the whole fixture's worth of them, not just the one a test
    // happened to return. Construct through Track(...) and nowhere else.
    private readonly System.Collections.Generic.List<FluidReferenceCPU> _built =
        new System.Collections.Generic.List<FluidReferenceCPU>();

    private FluidReferenceCPU Track(FluidReferenceCPU sim)
    {
        _built.Add(sim);
        return sim;
    }

    [SetUp]
    public void ClearTrackedSims() => _built.Clear();

    /// Runs after EVERY test in this fixture.
    ///
    /// The ownership guard in FluidReferenceCPU.IntentPass is documented as
    /// unreachable in the current single-threaded design. That claim was
    /// established by experiment (a hard throw in place of the guard, full
    /// suite, nothing thrown) -- but an experiment run once is a fact about one
    /// afternoon, not an invariant. This turns it into something the suite
    /// re-checks on every run: if any change ever makes that branch reachable,
    /// the test that did it fails BY NAME rather than the comment silently
    /// becoming a lie.
    ///
    /// Deliberately NOT an [OneTimeTearDown]: NUnit reports a one-time-teardown
    /// failure at the fixture level, and run-editmode-tests.sh tallies
    /// test-case elements -- so it could fail without changing FAIL 0.
    [TearDown]
    public void AssertOwnershipGuardNeverFired()
    {
        for (int i = 0; i < _built.Count; i++)
        {
            Assert.AreEqual(0L, _built[i].OwnershipGuardFiredTotal,
                $"the ownership guard fired on sandbox #{i} -- it is documented as " +
                "unreachable in the single-threaded path. Either the design changed " +
                "(update the comment in IntentPass) or something is genuinely wrong.");
            Assert.AreEqual(0, _built[i].CountDuplicateSlotOwnership(),
                $"sandbox #{i} ended the test with two live slots owning one cell");
        }
    }

    private FluidReferenceCPU NewSandbox() => Track(new FluidReferenceCPU(BX, BY, BZ));

    /// Pins the sandbox to a specific level of §7.4's Intent hierarchy, so the
    /// tests written against an earlier build tier keep proving that tier after
    /// the next one lands.
    private FluidReferenceCPU NewSandbox(FluidMotionTiers tiers)
    {
        var sim = Track(new FluidReferenceCPU(BX, BY, BZ));
        sim.EnabledTiers = tiers;
        return sim;
    }

    /// A 1-voxel-wide vertical shaft with stone walls at x=8,z=8, floor at y=0.
    /// Used where a test needs a "falling stream" that stays a stream at every
    /// tier -- without walls, the diagonal and horizontal tiers legitimately let
    /// the water leave the column and the scenario stops being the one under test.
    private static void BuildWalledShaft(FluidReferenceCPU sim)
    {
        for (int z = 7; z <= 9; z++)
        for (int x = 7; x <= 9; x++)
        for (int y = 0; y < sim.SizeYVoxels; y++)
        {
            bool isShaft = x == 8 && z == 8 && y > 0;
            if (!isShaft) sim.EditVoxel(x, y, z, Materials.Stone);
        }
    }

    /// Solid floor at y=0 across the whole footprint. Nothing else -- gravity
    /// tier tests do not need walls.
    private static void BuildFloor(FluidReferenceCPU sim)
    {
        sim.FillBox(new int3(0, 0, 0),
                    new int3(sim.SizeXVoxels - 1, 0, sim.SizeZVoxels - 1),
                    Materials.Stone);
    }

    /// Runs `ticks` ticks, asserting conservation after EVERY one. Returns the
    /// tick index at which ChangedCellsThisTick first hit zero (i.e. the sim
    /// came to rest), or -1 if it never did.
    private static int TickAndAssertConservation(FluidReferenceCPU sim, int ticks)
    {
        int restTick = -1;
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, check.Message);
            if (restTick < 0 && sim.ChangedCellsThisTick == 0) restTick = sim.TickCount;
        }
        return restTick;
    }

    // =====================================================================
    // #region The conservation counter itself -- built and proven BEFORE any
    // fluid rule was exercised through it (§13 build steps).
    // =====================================================================

    [Test]
    public void Ledger_SingleStaticDrop_ZeroTicks_IsBalanced()
    {
        // The trivial case the counter has to get right before it is worth
        // pointing at a simulation: one drop, no ticks, no rules run at all.
        var sim = NewSandbox();
        sim.EditVoxel(8, 20, 8, Materials.Water);

        Assert.AreEqual(1, sim.CountMobileBytes(), "one placed drop should be one mobile byte");
        Assert.AreEqual(1, sim.Ledger.Expected, "the ledger should have booked the placement");

        var check = sim.CheckConservation();
        Assert.IsTrue(check.Ok, check.Message);
        Assert.AreEqual(0, check.Delta);
        Assert.AreEqual(0, sim.TickCount, "no tick should have run");
    }

    [Test]
    public void Ledger_EmptySandbox_IsBalanced()
    {
        var sim = NewSandbox();
        var check = sim.CheckConservation();
        Assert.IsTrue(check.Ok, check.Message);
        Assert.AreEqual(0, check.Expected);
    }

    [Test]
    public void Ledger_NonMobileMaterialIsNotCounted()
    {
        // Stone and Obsidian are not fluid and not falling solids, so they must
        // not enter the mobile-byte count -- otherwise a reaction would look
        // like conservation holding when mass actually moved between classes.
        var sim = NewSandbox();
        sim.EditVoxel(4, 4, 4, Materials.Stone);
        sim.EditVoxel(5, 4, 4, Materials.Obsidian);
        Assert.AreEqual(0, sim.CountMobileBytes());
        Assert.IsTrue(sim.CheckConservation().Ok);
    }

    [Test]
    public void Ledger_ReportsExactTickAndSignedDelta()
    {
        // Proves the REPORTING contract directly, without needing a broken
        // simulation to produce it: §13 wants "we lost exactly N drops on tick
        // M", so both numbers must be in the message.
        var ledger = new FluidLedger();
        ledger.ResetTo(100);

        var lost = ledger.Check(37, 97);
        Assert.IsFalse(lost.Ok);
        Assert.AreEqual(-3, lost.Delta);
        Assert.AreEqual(37, lost.Tick);
        StringAssert.Contains("tick 37", lost.Message);
        StringAssert.Contains("LOST 3", lost.Message);

        var gained = ledger.Check(38, 104);
        Assert.IsFalse(gained.Ok);
        Assert.AreEqual(4, gained.Delta);
        StringAssert.Contains("GAINED 4", gained.Message);
        StringAssert.Contains("first went out of balance on tick 37", gained.Message);
    }

    [Test]
    public void Ledger_EditsAreBookedBothWays()
    {
        var sim = NewSandbox();
        sim.EditVoxel(8, 20, 8, Materials.Water);
        Assert.AreEqual(1, sim.Ledger.Expected);
        sim.EditVoxel(8, 20, 8, Materials.Stone);   // overwrite the drop
        Assert.AreEqual(0, sim.Ledger.Expected);
        Assert.AreEqual(1, sim.Ledger.ExternalAdded);
        Assert.AreEqual(1, sim.Ledger.ExternalRemoved);
        Assert.IsTrue(sim.CheckConservation().Ok);
    }

    // #endregion

    // =====================================================================
    // #region TIER A -- gravity only
    // =====================================================================

    [Test]
    public void GravityOnly_FallingColumn_ConservesEveryTick()
    {
        var sim = NewSandbox(FluidMotionTiers.Gravity);
        BuildFloor(sim);

        // 8 drops stacked in a shaft, well clear of the floor.
        for (int y = 20; y < 28; y++) sim.EditVoxel(8, y, 8, Materials.Water);
        Assert.AreEqual(8, sim.CountMobileBytes());

        TickAndAssertConservation(sim, 64);

        Assert.AreEqual(8, sim.CountMobileBytes(),
            "8 drops in, 8 drops out after 64 ticks");
    }

    [Test]
    public void GravityOnly_SingleDrop_FallsExactlyOneCellPerTick()
    {
        // §7.3: "A packed column shifts one cell per tick -- at 60Hz this reads
        // as pouring." One cell per tick is the rate the Air-Only rule implies,
        // and if it is ever more than that, a drop skipped a cell.
        var sim = NewSandbox(FluidMotionTiers.Gravity);
        BuildFloor(sim);
        sim.EditVoxel(8, 20, 8, Materials.Water);

        for (int t = 1; t <= 10; t++)
        {
            sim.Tick();
            Assert.IsTrue(sim.CheckConservation().Ok, sim.CheckConservation().Message);
            Assert.AreEqual(Materials.Water, sim.GetVoxel(8, 20 - t, 8),
                $"after {t} ticks the drop should be at y={20 - t}");
            Assert.AreEqual(Materials.Air, sim.GetVoxel(8, 20 - t + 1, 8),
                "the vacated cell must be Air -- Commit clears the source home");
        }
    }

    [Test]
    public void GravityOnly_ColumnLandsOnFloor_AndComesToRest()
    {
        var sim = NewSandbox(FluidMotionTiers.Gravity);
        BuildFloor(sim);
        for (int y = 20; y < 28; y++) sim.EditVoxel(8, y, 8, Materials.Water);

        // Budget: the lowest drop must fall 19 cells, the highest 26, at one
        // cell per tick, then FLUID_SLEEP_TICKS more for the stack to stop
        // re-bidding and sleep. 64 is ~2x that, with slack for the one-tick
        // stagger as each drop waits for the one below it to vacate.
        int restTick = TickAndAssertConservation(sim, 64);

        Assert.Greater(restTick, 0, "the column never came to rest within 64 ticks");
        Assert.AreEqual(8, sim.CountMobileBytes());
        for (int y = 1; y <= 8; y++)
            Assert.AreEqual(Materials.Water, sim.GetVoxel(8, y, 8),
                $"settled column should occupy y=1..8; y={y} is {sim.GetVoxel(8, y, 8)}");
        Assert.AreEqual(0, sim.ChangedCellsThisTick, "at rest, nothing may change");
        Assert.AreEqual(0, sim.ActiveSlotCount,
            "every settled slot should have slept (§7.6) -- the bytes stay, the slots do not");
    }

    [Test]
    public void MineAFallingDrop_OrphanSelfFreesNextTick_AndCounterReturnsToZero()
    {
        // §13: "Mine a falling drop -> orphan self-frees next tick; orphan
        // counter 0 at steady state."
        var sim = NewSandbox();
        BuildFloor(sim);
        sim.EditVoxel(8, 24, 8, Materials.Water);

        sim.Tick();
        sim.Tick();
        Assert.AreEqual(1, sim.ActiveSlotCount, "the falling drop should hold one slot");
        Assert.AreEqual(0, sim.OrphansFreedTotal, "nothing has been mined yet");

        // Mine it mid-fall: find it, delete it.
        int3 at = new int3(8, 22, 8);
        Assert.AreEqual(Materials.Water, sim.GetVoxel(at), "drop should be at y=22 after 2 ticks");
        sim.EditVoxel(at, Materials.Air);

        // The slot is still allocated at this instant -- §7.3 makes the slot
        // free ITSELF on its next Intent, which is the whole mechanism.
        Assert.AreEqual(1, sim.ActiveSlotCount, "the orphan is not freed eagerly by the edit");

        sim.Tick();
        Assert.AreEqual(1, sim.OrphansFreedThisTick,
            "the orphan must self-free on the very next tick, in Intent, before movement");
        Assert.AreEqual(0, sim.ActiveSlotCount);
        Assert.AreEqual(0, sim.CountMobileBytes(), "the mined drop is gone for good");
        Assert.IsTrue(sim.CheckConservation().Ok, sim.CheckConservation().Message);

        // Steady state: no further orphans are produced from nothing.
        for (int t = 0; t < 10; t++)
        {
            sim.Tick();
            Assert.AreEqual(0, sim.OrphansFreedThisTick, "orphan counter must be 0 at steady state");
            Assert.IsTrue(sim.CheckConservation().Ok, sim.CheckConservation().Message);
        }
        Assert.AreEqual(0, sim.ActiveSlotCount);
    }

    [Test]
    public void PlaceBlockIntoStream_Repeatedly_NoLostDrops()
    {
        // §13: "Place a block into a falling stream repeatedly -> no lost
        // drops; ledger clean."
        var sim = NewSandbox();
        BuildWalledShaft(sim);

        int placed = 0, mined = 0;
        for (int t = 0; t < 90; t++)
        {
            // Keep pouring at the top of the shaft.
            if (sim.GetVoxel(8, 30, 8) == Materials.Air)
            {
                sim.EditVoxel(8, 30, 8, Materials.Water);
                placed++;
            }

            // Every 7th tick, slam a stone block into the middle of the stream;
            // two ticks later, mine it out again. Both are edits, and both are
            // booked -- what must never happen is the ledger drifting.
            if (t % 7 == 0)
            {
                if (MaterialRules.IsMobile(sim.GetVoxel(8, 16, 8))) mined++;
                sim.EditVoxel(8, 16, 8, Materials.Stone);
            }
            if (t % 7 == 2) sim.EditVoxel(8, 16, 8, Materials.Air);

            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, check.Message);
        }

        Assert.Greater(placed, 20, "the pour should have run, not stalled");
        Assert.Greater(mined, 0, "at least one block placement should have landed on a drop");
        Assert.AreEqual(placed - mined, sim.CountMobileBytes(),
            "every drop poured, minus every drop a block overwrote, must still be in the array");
    }

    [Test]
    public void Determinism_SameScenarioTwice_ProducesIdenticalArrays()
    {
        // §7.8: "The CPU reference IS deterministic given a fixed tick order --
        // this is exactly why it's the correctness oracle."
        var a = NewSandbox();
        var b = NewSandbox();
        foreach (var sim in new[] { a, b })
        {
            BuildFloor(sim);
            for (int y = 18; y < 26; y++) sim.EditVoxel(7, y, 9, Materials.Water);
            for (int t = 0; t < 40; t++) sim.Tick();
        }

        for (int z = 0; z < a.SizeZVoxels; z++)
        for (int y = 0; y < a.SizeYVoxels; y++)
        for (int x = 0; x < a.SizeXVoxels; x++)
            Assert.AreEqual(a.GetVoxel(x, y, z), b.GetVoxel(x, y, z),
                $"runs diverged at ({x},{y},{z})");
    }

    [Test]
    public void SlotPoolExhaustion_IsAGuardedNoOp_NotALoss()
    {
        // §7.7: "Underflow on promotion is a guarded no-op -- retry next tick.
        // No invalid write, ever." Mass must survive a pool that is too small.
        var sim = Track(new FluidReferenceCPU(BX, BY, BZ, slotCapacity: 4));
        BuildFloor(sim);
        for (int y = 20; y < 28; y++) sim.EditVoxel(8, y, 8, Materials.Water);

        Assert.AreEqual(8, sim.CountMobileBytes());
        Assert.LessOrEqual(sim.ActiveSlotCount, 4, "the pool cap must bind");
        Assert.Greater(sim.PromotionsFailedTotal, 0, "promotion should have been refused, not thrown");

        TickAndAssertConservation(sim, 40);
        Assert.AreEqual(8, sim.CountMobileBytes(), "an exhausted pool freezes fluid, it never eats it");
    }

    // #endregion

    // =====================================================================
    // #region TIER B -- gravity + down-diagonals (§7.4 step 2, §7.5 repose)
    // Written only after the tier A region above was green.
    // =====================================================================

    // A 32^3 sandbox: the pile tests need room for a base wider than the 16x16
    // footprint of the small one.
    private FluidReferenceCPU NewBigSandbox(FluidMotionTiers tiers)
    {
        var sim = Track(new FluidReferenceCPU(4, 4, 4));
        sim.EnabledTiers = tiers;
        sim.FillBox(new int3(0, 0, 0), new int3(31, 0, 31), Materials.Stone);
        return sim;
    }

    /// Ticks until nothing changes for `quietTicks` consecutive ticks, asserting
    /// conservation every tick. Returns the tick the sim actually came to rest
    /// on, or -1 if it never did within `maxTicks`.
    ///
    /// ONLY VALID FOR TICK-INTERVAL-1 MATERIALS (Water, Sand). Every caller
    /// below uses one of those, deliberately. A slow-viscosity fluid is idle on
    /// most ticks by design -- lava acts 1 tick in 6, honey 1 in 30 -- so a
    /// quiet window shorter than its interval reports rest that has not
    /// happened. See the FALSE POSITIVE note on
    /// FluidReferenceCPU.ChangedCellsThisTick for the observed case. If you add
    /// a lava or honey settle test, size quietTicks above that material's
    /// MaterialRules.TickInterval, or assert on ActiveSlotCount == 0 instead.
    private static int TickToRest(FluidReferenceCPU sim, int maxTicks, int quietTicks = 4)
    {
        int quiet = 0;
        for (int t = 0; t < maxTicks; t++)
        {
            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, check.Message);
            quiet = sim.ChangedCellsThisTick == 0 ? quiet + 1 : 0;
            if (quiet >= quietTicks) return sim.TickCount - quiet + 1;
        }
        return -1;
    }

    [Test]
    public void DownDiagonals_ColumnOntoFloor_ConservesEveryTick()
    {
        var sim = NewBigSandbox(FluidMotionTiers.Gravity | FluidMotionTiers.DownDiagonals);
        for (int y = 20; y < 30; y++) sim.EditVoxel(16, y, 16, Materials.Water);
        Assert.AreEqual(10, sim.CountMobileBytes());

        int rest = TickToRest(sim, 400);
        Assert.Greater(rest, 0, "the pile never came to rest within 400 ticks");
        Assert.AreEqual(10, sim.CountMobileBytes(),
            "the diagonal tier must not create or destroy a single drop");
        TestContext.WriteLine($"[tierB] column of 10 came to rest on tick {rest}");
    }

    [Test]
    public void DownDiagonals_DropSlidesOffAPeak()
    {
        // A single drop landing on a 1-cell peak has no legal straight-down
        // move and exactly four legal down-diagonals. It must take one of them,
        // not sit on the point.
        var sim = NewBigSandbox(FluidMotionTiers.Gravity | FluidMotionTiers.DownDiagonals);
        sim.EditVoxel(16, 1, 16, Materials.Stone);       // the peak
        sim.EditVoxel(16, 5, 16, Materials.Water);

        TickToRest(sim, 40);

        Assert.AreEqual(Materials.Air, sim.GetVoxel(16, 2, 16),
            "the drop must not come to rest balanced on the peak");
        int settled = 0;
        int3[] around = { new int3(15,1,16), new int3(17,1,16), new int3(16,1,15), new int3(16,1,17) };
        foreach (var p in around)
            if (sim.GetVoxel(p) == Materials.Water) settled++;
        Assert.AreEqual(1, settled, "the drop should be resting on the floor beside the peak");
        Assert.AreEqual(1, sim.CountMobileBytes());
    }

    [Test]
    public void DownDiagonals_TieBreakIsNotDirectionallyBiased()
    {
        // §13 failure signature: "Water drifts one direction consistently ->
        // parity/tie-break bias; add the per-frame hash (C.7)."
        // 100 independent drops, each on its own peak, each with exactly four
        // equally legal down-diagonals. If the tie-break were biased, one bucket
        // would take most or all of them.
        var sim = NewBigSandbox(FluidMotionTiers.Gravity | FluidMotionTiers.DownDiagonals);

        var peaks = new System.Collections.Generic.List<int3>();
        for (int z = 2; z <= 29; z += 3)
        for (int x = 2; x <= 29; x += 3)
        {
            sim.EditVoxel(x, 1, z, Materials.Stone);
            sim.EditVoxel(x, 4, z, Materials.Water);
            peaks.Add(new int3(x, 1, z));
        }
        int n = peaks.Count;
        Assert.AreEqual(n, sim.CountMobileBytes());

        TickToRest(sim, 200);

        int plusX = 0, minusX = 0, plusZ = 0, minusZ = 0, unresolved = 0;
        foreach (var p in peaks)
        {
            if (sim.GetVoxel(p.x + 1, 1, p.z) == Materials.Water) plusX++;
            else if (sim.GetVoxel(p.x - 1, 1, p.z) == Materials.Water) minusX++;
            else if (sim.GetVoxel(p.x, 1, p.z + 1) == Materials.Water) plusZ++;
            else if (sim.GetVoxel(p.x, 1, p.z - 1) == Materials.Water) minusZ++;
            else unresolved++;
        }
        string counts = $"+X={plusX} -X={minusX} +Z={plusZ} -Z={minusZ} unresolved={unresolved} of {n}";
        TestContext.WriteLine($"[tierB] tie-break distribution: {counts}");

        Assert.AreEqual(0, unresolved, $"every drop should have slid off its peak; {counts}");
        Assert.AreEqual(n, sim.CountMobileBytes(), $"conservation across 100 slides; {counts}");
        // Perfectly uniform would be 25 each; binomial sigma at n=100,p=0.25 is
        // 4.33, so 8..45 is ~4 sigma of slack -- loose enough never to flake,
        // tight enough that a systematic bias (one bucket taking 60+) fails.
        foreach (var pair in new[] { ("+X", plusX), ("-X", minusX), ("+Z", plusZ), ("-Z", minusZ) })
            Assert.That(pair.Item2, Is.InRange(8, 45),
                $"direction {pair.Item1} is over/under-represented -- tie-break bias. {counts}");
    }

    [Test]
    public void SandPilesAtRoughly45Degrees()
    {
        // §7.5: "Same pipeline; Intent limited to down + down-diagonals -- ~45
        // degree angle of repose."
        var sim = NewBigSandbox(FluidMotionTiers.Full);

        int grains = 0;
        for (int t = 0; t < 600 && grains < 120; t++)
        {
            if (sim.GetVoxel(16, 30, 16) == Materials.Air)
            {
                sim.EditVoxel(16, 30, 16, Materials.Sand);
                grains++;
            }
            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, check.Message);
        }
        Assert.AreEqual(120, grains, "the pour stalled before 120 grains were placed");

        int rest = TickToRest(sim, 600);
        Assert.Greater(rest, 0, "the sand pile never came to rest");
        Assert.AreEqual(120, sim.CountMobileBytes(), "grains poured must equal grains present");

        // Measure the pile: apex height above the floor, and the half-width of
        // its lowest layer. A 45-degree face means one cell of height per cell
        // of horizontal run, i.e. apexHeight - 1 == baseHalfWidth.
        int apexY = 0, baseHalfWidth = 0;
        for (int y = 1; y < 32; y++)
        for (int z = 0; z < 32; z++)
        for (int x = 0; x < 32; x++)
        {
            if (sim.GetVoxel(x, y, z) != Materials.Sand) continue;
            if (y > apexY) apexY = y;
            if (y == 1)
            {
                int r = math.max(math.abs(x - 16), math.abs(z - 16));
                if (r > baseHalfWidth) baseHalfWidth = r;
            }
        }

        double degrees = System.Math.Atan2(apexY - 1, baseHalfWidth) * (180.0 / System.Math.PI);
        string shape = $"apexY={apexY} baseHalfWidth={baseHalfWidth} slope={degrees:F1} deg (rest tick {rest})";
        TestContext.WriteLine($"[tierB] sand pile: {shape}");

        Assert.Greater(baseHalfWidth, 1, $"the pile did not spread at all; {shape}");
        Assert.That(degrees, Is.InRange(30.0, 60.0),
            $"angle of repose is not ~45 degrees; {shape}");
    }

    [Test]
    public void FallingSolids_DoNotMoveHorizontally()
    {
        // §7.5 limits a falling solid's Intent to down + down-diagonals. With
        // the horizontal tier enabled for fluids, sand must still refuse it:
        // a grain in a flat-bottomed one-cell pit stays put, where water would
        // spread out of it.
        var sim = NewBigSandbox(FluidMotionTiers.Full);
        sim.EditVoxel(16, 1, 16, Materials.Sand);
        TickToRest(sim, 30);
        Assert.AreEqual(Materials.Sand, sim.GetVoxel(16, 1, 16),
            "a grain resting on the floor has no down or down-diagonal move and must not slide sideways");
    }

    // #endregion

    // =====================================================================
    // #region TIER C -- + horizontals (§7.4 step 3, pooling)
    // Written only after the tier A and tier B regions above were green.
    // =====================================================================

    /// An enclosed basin: stone floor and stone walls, open interior.
    /// §13's Phase 5a scene is "an enclosed basin", and that matters -- an open
    /// floor is not a container and water spreading off its edge is not the
    /// scenario the acceptance test describes.
    private FluidReferenceCPU NewBasin(int interior, out int3 innerMin, out int3 innerMax)
    {
        var sim = Track(new FluidReferenceCPU(4, 4, 4));   // 32^3
        int lo = (32 - interior) / 2;
        int hi = lo + interior - 1;
        sim.FillBox(new int3(lo - 1, 0, lo - 1), new int3(hi + 1, 0, hi + 1), Materials.Stone);
        for (int y = 1; y < 32; y++)
        {
            sim.FillBox(new int3(lo - 1, y, lo - 1), new int3(hi + 1, y, lo - 1), Materials.Stone);
            sim.FillBox(new int3(lo - 1, y, hi + 1), new int3(hi + 1, y, hi + 1), Materials.Stone);
            sim.FillBox(new int3(lo - 1, y, lo - 1), new int3(lo - 1, y, hi + 1), Materials.Stone);
            sim.FillBox(new int3(hi + 1, y, lo - 1), new int3(hi + 1, y, hi + 1), Materials.Stone);
        }
        innerMin = new int3(lo, 1, lo);
        innerMax = new int3(hi, 30, hi);
        return sim;
    }

    [Test]
    public void PouredColumn_SettlesFlat_AndOccupancyDeltaReachesZero()
    {
        // §13's primary behavioural assertion: "A column pours and settles flat;
        // occupancy -> 0 within N ticks of rest."
        //
        // N = 400, justified rather than picked: the basin interior is 4x4 and
        // the column is 16 drops, so the settled state is exactly one full
        // layer. A drop falls at most 16 cells and then travels at most 3+3
        // cells laterally to its resting column, one cell per tick; the stack
        // releases one drop per tick from the bottom (Air-Only, §7.3), so the
        // last drop cannot start before tick ~16. That is ~16+16+6 = 38 ticks
        // of genuine work, plus FLUID_SLEEP_TICKS for the surface to stop
        // shuffling. 400 is ~8x that -- large enough that a pass is not luck,
        // and the test reports the tick it ACTUALLY rested on, so the real
        // margin is visible rather than assumed.
        var sim = NewBasin(4, out int3 innerMin, out int3 innerMax);
        int interiorArea = 4 * 4;

        // A single vertical column of exactly one layer's worth of water,
        // stacked in the basin's corner so it has to spread as well as fall.
        for (int i = 0; i < interiorArea; i++)
            sim.EditVoxel(innerMin.x, 2 + i, innerMin.z, Materials.Water);
        Assert.AreEqual(interiorArea, sim.CountMobileBytes());

        // quietTicks = 12 > FLUID_SLEEP_TICKS (8): resting and having SLEPT are
        // different states one tick apart, and the assertion below is about the
        // second one. Stopping at the first quiet tick would assert "no slots"
        // before the slots have had time to age out.
        int rest = TickToRest(sim, 400, quietTicks: 12);
        TestContext.WriteLine($"[tierC] {interiorArea}-drop column came to rest on tick {rest}");

        Assert.Greater(rest, 0, "the basin never reached occupancy delta 0 within 400 ticks");
        Assert.AreEqual(interiorArea, sim.CountMobileBytes(), "conservation across the whole pour");
        Assert.AreEqual(0, sim.ChangedCellsThisTick);

        // Settled flat: exactly the y=1 layer of the interior is water, and
        // nothing above it is.
        for (int z = innerMin.z; z <= innerMax.z; z++)
        for (int x = innerMin.x; x <= innerMax.x; x++)
        {
            Assert.AreEqual(Materials.Water, sim.GetVoxel(x, 1, z),
                $"the settled surface should be flat at y=1; ({x},1,{z}) is {sim.GetVoxel(x, 1, z)}");
            Assert.AreEqual(Materials.Air, sim.GetVoxel(x, 2, z),
                $"nothing should remain above the flat surface; ({x},2,{z}) is {sim.GetVoxel(x, 2, z)}");
        }
        Assert.AreEqual(0, sim.ActiveSlotCount, "a settled pool holds no slots (§7.6)");
    }

    [Test]
    public void PartiallyFilledSurface_StillReachesRest()
    {
        // The awkward case the sleep rule exists for: a pour that does NOT
        // complete its top layer, so surface drops always have an adjacent Air
        // cell at their own level to shuffle into. It must still come to rest.
        var sim = NewBasin(6, out int3 innerMin, out _);
        for (int i = 0; i < 20; i++)                     // 20 drops into a 36-cell layer
            sim.EditVoxel(innerMin.x + 2, 2 + i, innerMin.z + 2, Materials.Water);

        int rest = TickToRest(sim, 400, quietTicks: 8);
        TestContext.WriteLine($"[tierC] partial layer (20 of 36) came to rest on tick {rest}");
        Assert.Greater(rest, 0, "a partly-filled surface must still settle, not shuffle forever");
        Assert.AreEqual(20, sim.CountMobileBytes());
        Assert.AreEqual(0, sim.ActiveSlotCount);
    }

    [Test]
    public void Horizontals_ConserveEveryTick_UnderRepeatedPouring()
    {
        var sim = NewBasin(8, out int3 innerMin, out _);
        int poured = 0;
        for (int t = 0; t < 300; t++)
        {
            if (poured < 150 && sim.GetVoxel(innerMin.x + 3, 18, innerMin.z + 3) == Materials.Air)
            {
                sim.EditVoxel(innerMin.x + 3, 18, innerMin.z + 3, Materials.Water);
                poured++;
            }
            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, check.Message);
        }
        Assert.AreEqual(150, poured);
        Assert.AreEqual(150, sim.CountMobileBytes(), "150 poured, 150 present");
    }

    // #endregion

    // =====================================================================
    // #region Reactions and viscosity (§7.6, §7.4)
    // =====================================================================

    [Test]
    public void WaterPlusLava_ProducesObsidian_AndFreesBothSlots()
    {
        // §13: "water+lava -> obsidian, both slots freed."
        var sim = NewBigSandbox(FluidMotionTiers.Full);
        sim.EditVoxel(16, 1, 16, Materials.Lava);
        sim.EditVoxel(17, 1, 16, Materials.Water);
        sim.ResetLedgerToCurrentState();

        Assert.AreEqual(2, sim.ActiveSlotCount, "both fluids should hold a slot before the reaction");
        Assert.AreEqual(2, sim.CountMobileBytes());

        sim.Tick();
        Assert.AreEqual(1, sim.ReactionsThisTick, "adjacency should react on the first tick");
        var check = sim.CheckConservation();
        Assert.IsTrue(check.Ok, check.Message);

        Assert.AreEqual(1, sim.CountMaterial(Materials.Obsidian), "the lava cell becomes obsidian");
        Assert.AreEqual(0, sim.CountMaterial(Materials.Lava), "the lava is consumed");
        Assert.AreEqual(0, sim.CountMaterial(Materials.Water), "the water is consumed");
        Assert.AreEqual(0, sim.CountMobileBytes(), "no mobile material survives the reaction");
        Assert.AreEqual(2L, sim.Ledger.ReactionConsumed,
            "both consumed bytes must be booked as a reaction, not silently lost");

        // "Both slots freed": the scanning slot frees inside Intent, its
        // counterpart on its own next Intent via the orphan check (§7.3).
        sim.Tick();
        Assert.AreEqual(0, sim.ActiveSlotCount, "both slots must be free at steady state");
        Assert.IsTrue(sim.CheckConservation().Ok, sim.CheckConservation().Message);
    }

    [Test]
    public void LavaFallingIntoWater_Reacts_AndConservesThroughout()
    {
        var sim = NewBasin(6, out int3 innerMin, out _);
        for (int z = innerMin.z; z < innerMin.z + 6; z++)
        for (int x = innerMin.x; x < innerMin.x + 6; x++)
            sim.EditVoxel(x, 1, z, Materials.Water);
        sim.EditVoxel(innerMin.x + 2, 12, innerMin.z + 2, Materials.Lava);
        sim.ResetLedgerToCurrentState();

        long before = sim.CountMobileBytes();
        for (int t = 0; t < 120; t++)
        {
            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, check.Message);
        }

        Assert.AreEqual(1, sim.CountMaterial(Materials.Obsidian),
            "the falling lava should have made exactly one obsidian on contact");
        Assert.AreEqual(0, sim.CountMaterial(Materials.Lava));
        Assert.AreEqual(before - 2, sim.CountMobileBytes(),
            "one lava and one water consumed, nothing else");
        Assert.AreEqual(2L, sim.Ledger.ReactionConsumed);
    }

    [Test]
    public void Viscosity_LavaMovesOnceEverySixTicks_HoneyOnceEveryThirty()
    {
        // §7.4: "Viscosity via tick intervals: Water every tick, Lava every 6,
        // Honey every 30."
        Assert.AreEqual(1u, MaterialRules.TickInterval(Materials.Water));
        Assert.AreEqual(6u, MaterialRules.TickInterval(Materials.Lava));
        Assert.AreEqual(30u, MaterialRules.TickInterval(Materials.Honey));

        foreach (var (material, interval) in new[]
                 { (Materials.Water, 1), (Materials.Lava, 6), (Materials.Honey, 30) })
        {
            var sim = NewBigSandbox(FluidMotionTiers.Full);
            sim.EditVoxel(16, 30, 16, material);   // 29 cells of fall available

            int firstMoveTick = -1;
            for (int t = 0; t < 100 && firstMoveTick < 0; t++)
            {
                sim.Tick();
                Assert.IsTrue(sim.CheckConservation().Ok, sim.CheckConservation().Message);
                if (sim.MovesThisTick > 0) firstMoveTick = sim.TickCount;
            }
            Assert.AreEqual(interval, firstMoveTick,
                $"{material} should first move on tick {interval}, not {firstMoveTick}");

            // And it must keep to that cadence, not sprint after the first move.
            // Window = interval*10 ticks => exactly 10 further moves. Sized off
            // the interval so the 29-cell fall budget, not the cadence, is never
            // what ends the count.
            int window = interval * 10;
            int moves = 0;
            for (int t = 0; t < window; t++) { sim.Tick(); moves += sim.MovesThisTick; }
            Assert.AreEqual(10, moves,
                $"{material} should move 10 more times in {window} ticks, not {moves}");
        }
    }

    [Test]
    public void Viscosity_DoesNotAgeAGatedSlotTowardSleep()
    {
        // Honey ticks once every 30. If the gated ticks aged the sleep counter,
        // FLUID_SLEEP_TICKS (8) would free the slot before it ever got a turn
        // and honey would be immobile rather than slow.
        var sim = NewBigSandbox(FluidMotionTiers.Full);
        sim.EditVoxel(16, 20, 16, Materials.Honey);
        for (int t = 0; t < 29; t++) sim.Tick();
        Assert.AreEqual(1, sim.ActiveSlotCount,
            "a viscosity-gated slot must not sleep while waiting for its turn");
        sim.Tick();
        Assert.AreEqual(1, sim.MovesThisTick, "honey should move on its 30th tick");
    }

    [Test]
    public void ForceDemote_OutsideActiveRadius_FreesTheSlotButNotTheMass()
    {
        // §7.4 near-player scope / §7.7 forced demotion: "distant fluid freezes
        // mid-flow, no state lost."
        var sim = NewBigSandbox(FluidMotionTiers.Full);
        sim.PlayerVoxel = new int3(2, 2, 2);
        sim.ActiveRadiusVoxels = 6;

        sim.EditVoxel(28, 20, 28, Materials.Water);     // far away
        sim.EditVoxel(3, 4, 3, Materials.Water);        // near the player
        sim.ResetLedgerToCurrentState();

        sim.Tick();
        Assert.AreEqual(1, sim.ForceDemotedThisTick, "the distant drop should be demoted");
        Assert.AreEqual(2, sim.CountMobileBytes(), "demotion frees a slot, never a byte");

        for (int t = 0; t < 30; t++)
        {
            sim.Tick();
            Assert.IsTrue(sim.CheckConservation().Ok, sim.CheckConservation().Message);
        }
        Assert.AreEqual(Materials.Water, sim.GetVoxel(28, 20, 28),
            "the distant drop should be frozen exactly where it was, mid-air");
        Assert.AreEqual(2, sim.CountMobileBytes());
    }

    // #endregion

    // =====================================================================
    // #region Slot ownership -- the _slotAt duplicate-ownership regression
    // =====================================================================

    [Test]
    public void OrphanedSlot_ThenDifferentSlotMovesIntoSameCell_NoDuplicateOwnership()
    {
        // The exact scenario FluidReferenceCPU's _slotAt comment claims to
        // defend against, staged deliberately:
        //   slot A owns cell C holding Water; C is mined to Air, orphaning A;
        //   a DIFFERENT Water slot B then moves into C. If A survived, its
        //   cached-material comparison would match the Water B just delivered,
        //   two live slots would own C, and committing both would report a
        //   conservation GAIN.
        //
        // A 1-wide walled shaft is used so neither drop has a diagonal or
        // horizontal escape -- B's only legal destination is C itself, which is
        // what makes the collision certain rather than incidental.
        var sim = NewSandbox();
        BuildWalledShaft(sim);

        int3 C = new int3(8, 5, 8);
        int3 B = new int3(8, 6, 8);
        sim.EditVoxel(new int3(8, 4, 8), Materials.Stone);   // floor under C
        sim.EditVoxel(C, Materials.Water);                   // slot A
        sim.EditVoxel(B, Materials.Water);                   // slot B, directly above

        Assert.AreEqual(2, sim.ActiveSlotCount, "both drops should hold a slot");
        Assert.AreEqual(0, sim.CountDuplicateSlotOwnership());

        // Mine C. A is orphaned but NOT yet freed -- §7.3 makes it free itself
        // on its next Intent, which is the window this test is about.
        sim.EditVoxel(C, Materials.Air);
        Assert.AreEqual(2, sim.ActiveSlotCount, "the orphan is not freed eagerly by the edit");
        Assert.AreEqual(0, sim.CountDuplicateSlotOwnership(),
            "the mined cell is still recorded as A's, and A is still the only owner");

        // The tick in which A must self-free AND B must move into C.
        sim.Tick();
        var check = sim.CheckConservation();
        Assert.IsTrue(check.Ok, check.Message);
        Assert.AreEqual(Materials.Water, sim.GetVoxel(C), "B should have moved into the mined cell");
        Assert.AreEqual(Materials.Air, sim.GetVoxel(B), "B's old home must be cleared");
        Assert.AreEqual(1, sim.ActiveSlotCount,
            "exactly one slot may survive: A orphan-freed, B moved");
        Assert.AreEqual(0, sim.CountDuplicateSlotOwnership(),
            "two live slots must never own the same cell");

        // And it must stay that way -- a duplicate that only shows up once the
        // pair separates would still be a duplicate.
        for (int t = 0; t < 20; t++)
        {
            sim.Tick();
            Assert.AreEqual(0, sim.CountDuplicateSlotOwnership(),
                $"duplicate slot ownership appeared on tick {sim.TickCount}");
            var c = sim.CheckConservation();
            Assert.IsTrue(c.Ok, c.Message);
        }
        Assert.AreEqual(1, sim.CountMobileBytes(), "one drop mined, one drop left");
    }

    [Test]
    public void Chaos_RandomEditsInterleavedWithTicks_NeverDoubleOwnsOrDrifts()
    {
        // A net, not a scenario. The targeted test above stages ONE ordering of
        // orphan-then-refill; this hammers every ordering the edit path can
        // produce -- mining live drops, overwriting fluid with fluid, dropping
        // blocks onto streams, refilling just-vacated cells -- and checks the
        // two invariants that a duplicate would break: the ledger, and slot
        // ownership. Deterministic seed (§7.8), so a failure is reproducible.
        var sim = NewBigSandbox(FluidMotionTiers.Full);
        byte[] palette = { Materials.Water, Materials.Water, Materials.Sand,
                           Materials.Lava, Materials.Air, Materials.Stone };

        uint rng = 0x5EED1234u;
        int Next(int bound)
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (int)(rng % (uint)bound);
        }

        for (int t = 0; t < 600; t++)
        {
            int edits = Next(4);
            for (int e = 0; e < edits; e++)
            {
                int x = 1 + Next(30), y = 1 + Next(28), z = 1 + Next(30);
                sim.EditVoxel(x, y, z, palette[Next(palette.Length)]);
            }

            sim.Tick();

            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, check.Message);
            Assert.AreEqual(0, sim.CountDuplicateSlotOwnership(),
                $"duplicate slot ownership on tick {sim.TickCount}");
        }

        TestContext.WriteLine(
            $"[ownership] 600 chaos ticks: ownership guard fired " +
            $"{sim.OwnershipGuardFiredTotal} time(s), orphans freed {sim.OrphansFreedTotal}, " +
            $"mobile bytes {sim.CountMobileBytes()}, active slots {sim.ActiveSlotCount}");
    }

    [Test]
    public void DuplicateOwnershipHunt_EveryOrphanRefillTiming()
    {
        // A wider net than the single staged ordering above. The duplicate the
        // _slotAt comment describes depends on WHEN the refill lands relative
        // to the orphaning edit, so sweep that: vary how far above the victim
        // cell the second drop starts (1..4 cells), how many ticks elapse
        // before the mine (0..4), and whether the mined cell is left as Air or
        // immediately re-filled with the SAME material by a second edit (the
        // case where the stale byte comparison would match on the very next
        // Intent). Every combination is checked for a conservation GAIN and for
        // two live slots sharing one home.
        int combos = 0;
        for (int gap = 1; gap <= 4; gap++)
        for (int delay = 0; delay <= 4; delay++)
        for (int refill = 0; refill <= 1; refill++)
        {
            combos++;
            var sim = NewSandbox();
            BuildWalledShaft(sim);

            int3 C = new int3(8, 5, 8);
            sim.EditVoxel(new int3(8, 4, 8), Materials.Stone);   // floor under C
            sim.EditVoxel(C, Materials.Water);                   // slot A
            sim.EditVoxel(new int3(8, 5 + gap, 8), Materials.Water);  // slot B above

            string where = $"gap={gap} delay={delay} refill={refill}";
            for (int t = 0; t < delay; t++)
            {
                sim.Tick();
                AssertNoDuplicate(sim, where);
            }

            sim.EditVoxel(C, Materials.Air);                     // orphan A
            if (refill == 1) sim.EditVoxel(C, Materials.Water);  // stale byte now matches again
            AssertNoDuplicate(sim, where + " (immediately after the edit)");

            for (int t = 0; t < 24; t++)
            {
                sim.Tick();
                AssertNoDuplicate(sim, where + $" tick {sim.TickCount}");
            }

            int expected = refill == 1 ? 2 : 1;
            Assert.AreEqual(expected, sim.CountMobileBytes(),
                $"mobile byte count wrong for {where}");
        }
        TestContext.WriteLine($"[ownership] swept {combos} orphan/refill timings, no duplicates");
    }

    private static void AssertNoDuplicate(FluidReferenceCPU sim, string where)
    {
        var check = sim.CheckConservation();
        Assert.IsTrue(check.Ok, $"{where}: {check.Message}");
        Assert.AreEqual(0, sim.CountDuplicateSlotOwnership(),
            $"{where}: two live slots share one home cell");
    }

    // #endregion
}
