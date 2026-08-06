// Assets/Game/RaymarchGpuDebugReadback.cs
//
// Reads back RaymarchFeature.DebugBuffer (populated by Raymarch.compute for
// the pixel at RaymarchFeature.DebugPixel) and logs it in the same format
// as RaymarchDebugProbe.RunMacroSkipProbe's output, for direct line-by-line
// comparison against the C# sibling's numbers.
//
// Usage: attach anywhere, set Pixel X/Y to match what you probed on the CPU
// side, enter Play mode, let a frame render, then right-click -> "Read GPU
// Debug Buffer". GraphicsBuffer.GetData blocks until the GPU catches up, so
// this is safe to call from the Inspector context menu after the fact.
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
        int iterCount = Mathf.RoundToInt(data[6]);
        int totalSteps = Mathf.RoundToInt(data[7]);

        Debug.Log($"[GPU Debug] pixel ({pixelX},{pixelY}) rayDir=({rayDirX:F6},{rayDirY:F6},{rayDirZ:F6}) " +
                  $"tDelta=({tDeltaX:F4},{tDeltaY:F4},{tDeltaZ:F4}) totalLoopSteps={totalSteps}");

        int capturedIters = Mathf.Min(iterCount, 20);
        for (int i = 0; i < capturedIters; i++)
        {
            int o = 8 + i * 6;
            float tExitX = data[o + 0], tExitY = data[o + 1], tExitZ = data[o + 2];
            float tMin = data[o + 3];
            float brickY = data[o + 4];
            float voxelYAfter = data[o + 5];

            Debug.Log($"  [GPU macroSkip #{i + 1}] brickY={brickY} tExit=({tExitX:F2},{tExitY:F2},{tExitZ:F2}) " +
                      $"tMin={tMin:F4} voxelY-after={voxelYAfter}");
        }

        if (iterCount >= 20)
        {
            Debug.LogWarning($"[GPU Debug] iterCount hit the 20-iteration capture cap while totalLoopSteps={totalSteps} - " +
                              $"macro-skip did not resolve within the captured window. Compare totalLoopSteps against the " +
                              $"400-step GPU cap and against the C# sibling's 49-step result.");
        }
    }
}
