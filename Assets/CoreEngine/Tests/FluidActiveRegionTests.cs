// Assets/CoreEngine/Tests/FluidActiveRegionTests.cs
//
// §7.4's activity policy, proven without a GPU.
//
// These pin the two anti-thrash mechanisms and the fact that they are DIFFERENT
// mechanisms. The GPU half (that CSPromote and CSIntent actually consult these
// radii) is exercised by run-fluid-activity.sh; what is provable here is the
// policy itself, and that is where a thrash bug would live.

using NUnit.Framework;
using Unity.Mathematics;

public class FluidActiveRegionTests
{
    // =====================================================================
    // Hysteresis: the demote radius is strictly larger than the wake radius
    // =====================================================================

    [Test]
    public void TheSleepRadiusIsAlwaysStrictlyLargerThanTheWakeRadius()
    {
        // If these ever collapsed to the same value the hysteresis band would
        // vanish and boundary thrash would come back silently, so this is
        // checked across the range rather than at one convenient value.
        foreach (int r in new[] { 1, 2, 3, 7, 16, 64, 128, 640, 1280, 4096 })
            Assert.Greater(FluidActiveRegion.SleepRadiusFor(r), r,
                $"sleep radius must exceed wake radius at r={r}");
    }

    [Test]
    public void TheSleepRadiusFollowsTheDocumentedRatio()
    {
        Assert.AreEqual(1472, FluidActiveRegion.SleepRadiusFor(1280),
            "1280 * 1.15 = 1472, the shipped radius's band");
    }

    [Test]
    public void AZeroRadiusIsDegenerateButNotNegative()
    {
        Assert.AreEqual(0, FluidActiveRegion.SleepRadiusFor(0));
        Assert.AreEqual(0, FluidActiveRegion.SleepRadiusFor(-5));
    }

    [Test]
    public void ACellInsideTheWakeRadiusIsPromotable_AndNotDemotable()
    {
        int3 c = new int3(100, 50, 100);
        int wake = 40, sleep = FluidActiveRegion.SleepRadiusFor(wake);
        int3 v = c + new int3(30, 0, 0);

        Assert.IsTrue(FluidActiveRegion.WithinWakeRadius(v, c, wake));
        Assert.IsFalse(FluidActiveRegion.BeyondSleepRadius(v, c, sleep));
    }

    [Test]
    public void ACellBeyondTheSleepRadiusIsDemotable_AndNotPromotable()
    {
        int3 c = new int3(100, 50, 100);
        int wake = 40, sleep = FluidActiveRegion.SleepRadiusFor(wake);
        int3 v = c + new int3(sleep + 5, 0, 0);

        Assert.IsFalse(FluidActiveRegion.WithinWakeRadius(v, c, wake));
        Assert.IsTrue(FluidActiveRegion.BeyondSleepRadius(v, c, sleep));
    }

    [Test]
    public void TheBandBetweenTheRadiiIsNeitherPromotableNorDemotable()
    {
        // THE WHOLE POINT OF HYSTERESIS. A slot here keeps whatever state it
        // has: an awake one is not demoted, and a sleeping one is not woken.
        int3 c = new int3(0, 0, 0);
        int wake = 40, sleep = FluidActiveRegion.SleepRadiusFor(wake);   // 46

        int3 v = new int3(43, 0, 0);                 // 40 < 43 < 46
        Assert.IsTrue(FluidActiveRegion.InHysteresisBand(v, c, wake, sleep));
        Assert.IsFalse(FluidActiveRegion.WithinWakeRadius(v, c, wake),
            "not close enough to be newly promoted");
        Assert.IsFalse(FluidActiveRegion.BeyondSleepRadius(v, c, sleep),
            "and not far enough to be demoted");
    }

    [Test]
    public void DemotionIsNotSimplyTheNegationOfPromotion()
    {
        // The bug this rules out: using one radius for both directions, so
        // every cell is either promotable or demotable and the band is empty.
        int3 c = int3.zero;
        int wake = 100, sleep = FluidActiveRegion.SleepRadiusFor(wake);

        int band = 0;
        for (int x = 0; x <= 200; x++)
            if (FluidActiveRegion.InHysteresisBand(new int3(x, 0, 0), c, wake, sleep)) band++;

        Assert.Greater(band, 10,
            $"there must be a real band of cells in neither state (found {band}); " +
            "an empty band means one radius is doing both jobs");
    }

    // =====================================================================
    // Boundary thrash: the scenario, simulated
    // =====================================================================

    [Test]
    public void APlayerOscillatingAtTheBoundary_DoesNotFlipACellEveryFrame()
    {
        // THE SCENARIO STEP 1'S DESIGN OWES A PROOF. A player jitters back and
        // forth across the wake boundary. Without hysteresis the cell sitting
        // exactly on it would promote and demote on alternate frames forever.
        int wake = 100, sleep = FluidActiveRegion.SleepRadiusFor(wake);
        int3 cell = new int3(100, 0, 0);             // exactly at the wake radius

        int3 centre = int3.zero;
        bool awake = true;                           // promoted on arrival
        int flips = 0;

        for (int f = 0; f < 400; f++)
        {
            // +/- 3 voxels around the origin -- ordinary sub-metre movement.
            centre = new int3((f % 2 == 0) ? 3 : -3, 0, 0);

            bool wasAwake = awake;
            if (!awake && FluidActiveRegion.WithinWakeRadius(cell, centre, wake)) awake = true;
            else if (awake && FluidActiveRegion.BeyondSleepRadius(cell, centre, sleep)) awake = false;
            if (awake != wasAwake) flips++;
        }

        Assert.AreEqual(0, flips,
            $"the cell changed state {flips} times while the player jittered 6 voxels; " +
            "hysteresis exists precisely to make this 0");
    }

    [Test]
    public void WithoutHysteresis_TheSameOscillationThrashes()
    {
        // The control. Same movement, one radius for both directions -- which
        // is what the shader did before _SleepRadiusVoxels existed. If this
        // does NOT thrash, the test above proves nothing.
        int r = 100;
        int3 cell = new int3(100, 0, 0);
        int3 centre;
        bool awake = true;
        int flips = 0;

        for (int f = 0; f < 400; f++)
        {
            centre = new int3((f % 2 == 0) ? 3 : -3, 0, 0);
            bool wasAwake = awake;
            awake = FluidActiveRegion.WithinWakeRadius(cell, centre, r);
            if (awake != wasAwake) flips++;
        }

        Assert.Greater(flips, 100,
            $"with a single radius the same movement must thrash (got {flips} flips) -- " +
            "otherwise the hysteresis test is vacuous");
    }

    // =====================================================================
    // The re-centre threshold: a SEPARATE mechanism
    // =====================================================================

    [Test]
    public void TheCentreDoesNotFollowMovementSmallerThanTheThreshold()
    {
        int3 c = new int3(1000, 40, 1000);
        Assert.IsFalse(FluidActiveRegion.ShouldRecentre(c, c, 16), "no movement at all");
        Assert.IsFalse(FluidActiveRegion.ShouldRecentre(c, c + new int3(15, 0, 0), 16));
        Assert.IsFalse(FluidActiveRegion.ShouldRecentre(c, c + new int3(9, 9, 0), 16),
            "12.7 voxels diagonally is still under 16");
    }

    [Test]
    public void TheCentreFollowsMovementAtOrBeyondTheThreshold()
    {
        int3 c = new int3(1000, 40, 1000);
        Assert.IsTrue(FluidActiveRegion.ShouldRecentre(c, c + new int3(16, 0, 0), 16));
        Assert.IsTrue(FluidActiveRegion.ShouldRecentre(c, c + new int3(0, 0, 40), 16));
        Assert.IsTrue(FluidActiveRegion.ShouldRecentre(c, c + new int3(12, 12, 0), 16),
            "17 voxels diagonally clears it");
    }

    [Test]
    public void AZeroThresholdFollowsEveryMove_ButStillNotAStationaryPlayer()
    {
        int3 c = new int3(5, 5, 5);
        Assert.IsFalse(FluidActiveRegion.ShouldRecentre(c, c, 0),
            "even with no threshold, standing still must not re-upload the centre");
        Assert.IsTrue(FluidActiveRegion.ShouldRecentre(c, c + new int3(1, 0, 0), 0));
    }

    [Test]
    public void AStationaryPlayerNeverMovesTheCentre_OverManyFrames()
    {
        // The cheapest possible thrash: re-uploading PlayerVoxel every frame
        // for a player who is not moving, sweeping the boundary by rounding.
        int3 centre = new int3(500, 30, 500);
        int moves = 0;
        for (int f = 0; f < 1000; f++)
            if (FluidActiveRegion.ShouldRecentre(centre, new int3(500, 30, 500),
                                                 FluidActiveRegion.DefaultRecentreThresholdVoxels))
                moves++;
        Assert.AreEqual(0, moves);
    }

    [Test]
    public void AWalkingPlayerRecentresSteadily_NotEveryFrame()
    {
        // 5.2 m/s at 60 Hz is 0.87 voxels per frame, so a 16-voxel threshold
        // should fire roughly every 18 frames -- often enough to track, rarely
        // enough not to churn.
        int3 centre = new int3(0, 0, 0);
        float x = 0f;
        int recentres = 0;
        for (int f = 0; f < 600; f++)
        {
            x += 0.867f;
            int3 p = new int3((int)x, 0, 0);
            if (FluidActiveRegion.ShouldRecentre(centre, p,
                    FluidActiveRegion.DefaultRecentreThresholdVoxels))
            {
                centre = p;
                recentres++;
            }
        }

        Assert.Greater(recentres, 20, "the centre must actually track a walking player");
        Assert.Less(recentres, 60,
            $"but not re-upload every few frames ({recentres} in 600 frames)");
        Assert.LessOrEqual(math.abs(520 - centre.x), 20,
            "and it must end up near the player, not lag arbitrarily far behind");
    }

    // =====================================================================
    // The two mechanisms are independent
    // =====================================================================

    [Test]
    public void TheThresholdAloneDoesNotPreventBoundaryThrash()
    {
        // Proves they are not duplicates: quantise WHEN the boundary moves but
        // use one radius, and a cell on the boundary still flips whenever the
        // centre jumps.
        int r = 100;
        int3 cell = new int3(100, 0, 0);
        int3 centre = int3.zero;
        bool awake = true;
        int flips = 0;

        for (int f = 0; f < 400; f++)
        {
            int3 p = new int3((f % 2 == 0) ? 20 : -20, 0, 0);   // 40 apart, clears a 16 threshold
            if (FluidActiveRegion.ShouldRecentre(centre, p, 16)) centre = p;

            bool wasAwake = awake;
            awake = FluidActiveRegion.WithinWakeRadius(cell, centre, r);
            if (awake != wasAwake) flips++;
        }

        Assert.Greater(flips, 100,
            "the re-centre threshold does not subsume hysteresis -- both are needed");
    }
}
