// Assets/Game/RaymarchDebugProbe.cs
//
// Diagnostic tool - not part of the frozen Phase 2 API surface.
//
// RunMacroSkip2DSweep() - 2D grid sweep using the FULL sweepRadius (the
//                         previous version silently capped radius at 8,
//                         which excluded the artifact pixels at ~19px from
//                         center - that "all clear" result was therefore
//                         not evidence about the artifact at all). Prints
//                         summary stats plus ONLY anomalous cells, so a
//                         wide radius stays readable.
// TraceSinglePixel()    - full per-iteration trace (macro-skips AND
//                         fallback steps) for one pixel, for once the
//                         sweep locates the failing coordinates.
using System.Text;
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class RaymarchDebugProbe : MonoBehaviour
{
    public int pixelX = 960;
    public int pixelY = 540;
    public int screenWidth = 1920;
    public int screenHeight = 1080;

    [Tooltip("Sweep radius in pixels around (pixelX,pixelY). Used in full - no internal cap.")]
    public int sweepRadius = 40;

    [Tooltip("Cells at or above this step count are reported as anomalies in the sweep.")]
    public int anomalyThreshold = 150;

    private (float3 rayStart, float3 rayDir) ComputeRayForPixel(int px, int py)
    {
        Camera cam = Camera.main;
        float2 uv = new float2(
            (px + 0.5f) / screenWidth,
            (py + 0.5f) / screenHeight
        ) * 2.0f - 1.0f;

        Matrix4x4 invProj = cam.projectionMatrix.inverse;
        Matrix4x4 invView = cam.cameraToWorldMatrix;

        Vector4 target4 = invProj * new Vector4(uv.x, uv.y, 1f, 1f);
        Vector3 targetDir = new Vector3(target4.x, target4.y, target4.z) / target4.w;
        targetDir.Normalize();

        Vector3 rayDirWorld = invView.MultiplyVector(targetDir);
        rayDirWorld.Normalize();

        float3 rayStart = ((float3)(Vector3)cam.transform.position) * 10.0f;
        float3 rayDir = new float3(rayDirWorld.x, rayDirWorld.y, rayDirWorld.z);
        return (rayStart, rayDir);
    }

    // Faithful port of Raymarch.compute INCLUDING the monotonic-progress
    // guard. Optionally records a full per-iteration trace.
    private (int steps, int macroSkips, int fallbackSteps, bool hit, int3 finalVoxel, float finalDist)
        RunFixedAlgorithmFor(float3 rayStart, float3 rayDir, ChunkStore store, int hardCap, StringBuilder trace, int traceLimit)
    {
        int3 step = new int3(
            rayDir.x >= 0f ? 1 : -1,
            rayDir.y >= 0f ? 1 : -1,
            rayDir.z >= 0f ? 1 : -1
        );

        float3 tDelta = new float3(
            rayDir.x == 0f ? 1e7f : math.abs(1f / rayDir.x),
            rayDir.y == 0f ? 1e7f : math.abs(1f / rayDir.y),
            rayDir.z == 0f ? 1e7f : math.abs(1f / rayDir.z)
        );

        int3 voxel = CoordMath.WorldToVoxel(new float3(rayStart.x, rayStart.y, rayStart.z) * 0.1f);

        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;

        float currentDist = 0f;
        int steps = 0;
        int macroSkips = 0;
        int fallbackSteps = 0;
        bool hit = false;
        byte mat = 0;
        const float MIN_PROGRESS = 1e-4f;
        int traced = 0;

        while (currentDist < 1280f && steps < hardCap)
        {
            steps++;

            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            int3 chunkCoord = CoordMath.BrickToChunk(brickCoord);
            Chunk chunk = store.GetChunk(chunkCoord);
            bool isDense;

            if (chunk == null) { mat = 0; isDense = false; }
            else if (chunk.isUniform) { mat = chunk.uniformMaterial; isDense = false; }
            else
            {
                int3 localBrick = CoordMath.LocalBrickIndex3D(brickCoord);
                int brickFlatIdx = CoordMath.LocalBrickIndex(localBrick);
                uint handleData = chunk.bricks[brickFlatIdx].data;
                isDense = (handleData & 0x80000000) != 0;
                mat = isDense ? (byte)0 : (byte)(handleData & 0xFF);
            }

            if (!isDense)
            {
                if (mat == 0) // MACRO SKIP
                {
                    int3 bMin = brickCoord * 8;
                    int3 bMax = bMin + 7;

                    float3 tExit = new float3(
                        step.x > 0 ? (bMax.x + 1f - rayStart.x) : (rayStart.x - bMin.x),
                        step.y > 0 ? (bMax.y + 1f - rayStart.y) : (rayStart.y - bMin.y),
                        step.z > 0 ? (bMax.z + 1f - rayStart.z) : (rayStart.z - bMin.z)
                    ) * tDelta;

                    float tMin = math.min(math.min(tExit.x, tExit.y), tExit.z);

                    if (tMin > currentDist + MIN_PROGRESS)
                    {
                        macroSkips++;
                        float prevDist = currentDist;
                        currentDist = tMin;
                        float3 entryPos = rayStart + rayDir * (currentDist + 0.001f);
                        int3 prevVoxel = voxel;
                        voxel = (int3)math.floor(entryPos);

                        if (trace != null && traced < traceLimit)
                        {
                            trace.AppendLine($"  step {steps}: SKIP brick={brickCoord} tExit=({tExit.x:F3},{tExit.y:F3},{tExit.z:F3}) tMin={tMin:F4} dist {prevDist:F4}->{currentDist:F4} voxel {prevVoxel}->{voxel}");
                            traced++;
                        }

                        tMax = new float3(
                            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
                            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
                            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
                        ) * tDelta;
                        continue;
                    }
                    else
                    {
                        fallbackSteps++;
                        int3 prevVoxel = voxel;
                        string axis;
                        if (tMax.x < tMax.y)
                        {
                            if (tMax.x < tMax.z) { voxel.x += step.x; currentDist = tMax.x; tMax.x += tDelta.x; axis = "X"; }
                            else { voxel.z += step.z; currentDist = tMax.z; tMax.z += tDelta.z; axis = "Z"; }
                        }
                        else
                        {
                            if (tMax.y < tMax.z) { voxel.y += step.y; currentDist = tMax.y; tMax.y += tDelta.y; axis = "Y"; }
                            else { voxel.z += step.z; currentDist = tMax.z; tMax.z += tDelta.z; axis = "Z"; }
                        }

                        if (trace != null && traced < traceLimit)
                        {
                            trace.AppendLine($"  step {steps}: FALLBACK({axis}) brick={brickCoord} tExit=({tExit.x:F3},{tExit.y:F3},{tExit.z:F3}) tMin={tMin:F4} dist->{currentDist:F4} voxel {prevVoxel}->{voxel} tMax=({tMax.x:F3},{tMax.y:F3},{tMax.z:F3})");
                            traced++;
                        }
                        continue;
                    }
                }
                else { hit = true; break; }
            }
            else { hit = true; break; }
        }

        return (steps, macroSkips, fallbackSteps, hit, voxel, currentDist);
    }

    [ContextMenu("Run Macro-Skip 2D Sweep (full radius, anomalies only)")]
    public void RunMacroSkip2DSweep()
    {
        ChunkStore store = Phase2Bootstrapper.Store;
        if (store == null) { Debug.LogError("RaymarchDebugProbe: Phase2Bootstrapper.Store is null."); return; }

        StringBuilder log = new StringBuilder();
        int hardCap = 2000;
        int r = sweepRadius; // FULL radius - no internal cap this time.

        int minSteps = int.MaxValue, maxSteps = -1;
        long totalStepsSum = 0;
        int cells = 0, anomalies = 0, misses = 0, caps = 0;
        int worstSteps = -1, worstX = 0, worstY = 0;

        log.AppendLine($"[2D Sweep v2] grid around ({pixelX},{pixelY}), radius {r} (FULL - not capped), fixed algorithm. Reporting cells with steps >= {anomalyThreshold} or MISS:");

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                var (rayStart, rayDir) = ComputeRayForPixel(pixelX + dx, pixelY + dy);
                var (steps, macroSkips, fallback, hit, finalVoxel, finalDist) = RunFixedAlgorithmFor(rayStart, rayDir, store, hardCap, null, 0);

                cells++;
                totalStepsSum += steps;
                if (steps < minSteps) minSteps = steps;
                if (steps > maxSteps) maxSteps = steps;
                if (steps > worstSteps) { worstSteps = steps; worstX = dx; worstY = dy; }
                if (!hit) misses++;
                if (steps >= 400) caps++;

                if (steps >= anomalyThreshold || !hit)
                {
                    anomalies++;
                    if (anomalies <= 60) // don't flood the log
                    {
                        log.AppendLine($"  ANOMALY at pixel ({pixelX + dx},{pixelY + dy}) offset ({dx},{dy}): steps={steps} (m={macroSkips} f={fallback}) hit={hit} finalVoxel={finalVoxel} dist={finalDist:F2}{(steps >= 400 ? "  <-- EXCEEDS GPU CAP" : "")}");
                    }
                }
            }
        }

        log.AppendLine($"[2D Sweep v2] {cells} cells. steps min={minSteps} max={maxSteps} avg={(double)totalStepsSum / cells:F1}. anomalies={anomalies} (misses={misses}, >=400cap={caps}).");
        log.AppendLine($"[2D Sweep v2] Worst: offset ({worstX},{worstY}) = pixel ({pixelX + worstX},{pixelY + worstY}) with {worstSteps} steps.");
        if (anomalies > 60) log.AppendLine($"[2D Sweep v2] ({anomalies - 60} additional anomalies suppressed from log.)");
        Debug.Log(log.ToString());
    }

    [ContextMenu("Trace Single Pixel (full iteration log)")]
    public void TraceSinglePixel()
    {
        ChunkStore store = Phase2Bootstrapper.Store;
        if (store == null) { Debug.LogError("RaymarchDebugProbe: Phase2Bootstrapper.Store is null."); return; }

        var (rayStart, rayDir) = ComputeRayForPixel(pixelX, pixelY);
        StringBuilder trace = new StringBuilder();
        trace.AppendLine($"[Trace] pixel ({pixelX},{pixelY}) rayStart={rayStart} rayDir=({rayDir.x:E5},{rayDir.y:E5},{rayDir.z:E5})");
        trace.AppendLine($"[Trace] rayStart on brick boundary? X:{(math.abs(math.frac(rayStart.x / 8f)) < 1e-6f)} Y:{(math.abs(math.frac(rayStart.y / 8f)) < 1e-6f)} Z:{(math.abs(math.frac(rayStart.z / 8f)) < 1e-6f)}");

        var (steps, macroSkips, fallback, hit, finalVoxel, finalDist) = RunFixedAlgorithmFor(rayStart, rayDir, store, 2000, trace, 120);

        trace.AppendLine($"[Trace] DONE: steps={steps} (m={macroSkips} f={fallback}) hit={hit} finalVoxel={finalVoxel} dist={finalDist:F2} (GPU cap 400)");
        Debug.Log(trace.ToString());
    }
}