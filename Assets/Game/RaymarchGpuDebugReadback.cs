// Assets/Game/RaymarchGpuDebugReadback.cs
//
// Reads back RaymarchFeature.DebugBuffer for the pixel at
// RaymarchFeature.DebugPixel and logs it.
//
// [SPLIT DIAGNOSTIC] DebugOut[9] = exit-axis inner iterations, DebugOut[10] =
// non-exit-axis inner iterations, for the whole ray (sum across every LeapSpan
// call this thread makes). DebugOut[8] is their total (kept for continuity
// with the earlier single-counter reading). This split exists to find out
// which loop family (the exact-integer exit-axis for-loop, or the
// tight-guarded non-exit while-loops) is the real hidden cost, before
// optimizing either further.
//
// Usage: attach anywhere, set Pixel X/Y (in DISPATCH-resolution coordinates -
// check the on-screen DISPATCH line, NOT screen resolution, if gate resolution
// is forced), enter Play mode, let a frame render, then right-click -> "Read
// GPU Debug Buffer". GraphicsBuffer.GetData blocks until the GPU catches up.
using UnityEngine;

public class RaymarchGpuDebugReadback : MonoBehaviour
{
    public int pixelX = 960;
    public int pixelY = 540;

    [ContextMenu("Read GPU Debug Buffer")]
    public void ReadBack()
    {
        RaymarchFeature.DebugPixel = new Vector2Int(pixelX, pixelY);

        if (RaymarchFeature.DebugBuffer == null)
        {
            Debug.LogError("RaymarchGpuDebugReadback: DebugBuffer is null - has the RaymarchFeature pass run at least one frame since DebugPixel was set? Setting DebugPixel just now only takes effect on the NEXT dispatch - if this is the first read, wait a frame and try again.");
            return;
        }

        float[] data = new float[128];
        RaymarchFeature.DebugBuffer.GetData(data);

        float rayDirX = data[0], rayDirY = data[1], rayDirZ = data[2];
        float tDeltaX = data[3], tDeltaY = data[4], tDeltaZ = data[5];
        int totalOuterSteps = Mathf.RoundToInt(data[7]);
        int totalInner = Mathf.RoundToInt(data[8]);
        int exitIters = Mathf.RoundToInt(data[9]);
        int nonExitIters = Mathf.RoundToInt(data[10]);
        int denseMicroSteps = Mathf.RoundToInt(data[13]);
int gpuTraversalMode = Mathf.RoundToInt(data[11]);
int chainLeapsTotal = Mathf.RoundToInt(data[12]);

        if (rayDirX == 0f && rayDirY == 0f && rayDirZ == 0f && totalOuterSteps == 0)
        {
            Debug.LogWarning($"[GPU Debug] pixel ({pixelX},{pixelY}) read all-zero - this usually means the pixel " +
                "coordinate is OUTSIDE the current dispatch resolution (check the on-screen DISPATCH line), so the " +
                "debug write never fired this frame. Verify pixelX < dispatch width and pixelY < dispatch height.");
            return;
        }

        Debug.Log($"[GPU Debug] pixel ({pixelX},{pixelY}) rayDir=({rayDirX:F6},{rayDirY:F6},{rayDirZ:F6}) " +
            $"tDelta=({tDeltaX:F4},{tDeltaY:F4},{tDeltaZ:F4})\n" +
            $"  OUTER steps (what StepHeat colors) = {totalOuterSteps}\n" +
            $"  TOTAL inner iterations             = {totalInner}\n" +
            $"  EXIT-axis inner iterations          = {exitIters}\n" +
            $"  NON-exit-axis inner iterations      = {nonExitIters}\n" +
            $"  DENSE per-voxel DDA outer steps     = {denseMicroSteps}  <- of the {totalOuterSteps} total outer steps, " +
    $"how many were spent single-stepping inside a dense brick (each one re-pays the full 4-level mip probe)\n" +
    $"  GPU-SIDE _TraversalMode reading     = {gpuTraversalMode}  <- what the shader actually received this dispatch (0=LeapSpan,1=Reseed,2=OccupancyChain) - compare against the HUD's MODE line\n" +
    $"  chainLeapsTotal (mode 2 only)       = {chainLeapsTotal}\n" +$"  (this reading uses the TIGHT non-exit guard - compare totals against pre-fix readings " +
            $"to see how much the tight guard alone reduced non-exit iterations)");
    }
}