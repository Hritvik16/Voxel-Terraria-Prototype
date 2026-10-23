// Assets/CoreEngine/Tests/CubeLookTests.cs
//
// Task 120 (VISUAL_REVIEW_114 #1 + #2 + #9): cube face lighting in the far-shading kernel -- ground bounce,
// filtered cube light (a staircase's mean over a fixed distance), one colour per drawn cell. The look is
// checked with stills (run-look-review.sh); this covers the flags, the shader text, and the filter's
// face weights against a brute-force staircase (the claim that a front-facing staircase hides none of
// its own faces, so face area x cosine is what a pixel covering many cubes sees).
using System;
using System.IO;
using NUnit.Framework;

public class CubeLookTests
{
    [Test]
    public void Flags_DefaultOn_AndAreBound()
    {
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("CubeLook = ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-cubelook\")", cs);
        StringAssert.Contains("CubeBounce = CubeLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-cubebounce\")", cs);
        StringAssert.Contains("CubeFilter = CubeLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-cubefilter\")", cs);
        StringAssert.Contains("CellHash = CubeLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-cellhash\")", cs);
        StringAssert.Contains("\"_CubeBounce\", CubeBounce ? CubeBounceStrength : 0f", cs, "the flags reach the shader");
        StringAssert.Contains("\"_CubeFilter\", CubeFilter ? 1 : 0", cs);
        StringAssert.Contains("\"_CellHash\", CellHash ? 1 : 0", cs);
        StringAssert.Contains("\"_CubeFilterRange\"", cs);
    }

    [Test]
    public void Shader_UsesAreInTheFarShadeKernelOnly()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        foreach (string use in new[] {
                     "CubeLight(nFace, nSlope, cubeW, rayDir, sd, sunExposure, skyOpen, direct, skyT);",
                     "CubeLight(nFaceF, nSlopeF, cubeWF, rayDir, sd, sunExposure, 1.0, direct, skyF);",
                     "_CellHash != 0 ? (voxel >> cellShift) << cellShift : voxel" })
        {
            int at = src.IndexOf(use);
            Assert.Greater(at, 0, use);
            Assert.Greater(src.LastIndexOf("#ifdef VE_FAR_SHADE", at), src.LastIndexOf("#endif", at),
                use + ": inside #ifdef VE_FAR_SHADE, so the other kernels stay byte for byte");
        }
        StringAssert.Contains("else if (!selfLit) nF = FarShadeNormal(", src, "-cubefilter 0 keeps task 85b's switch");
    }

    // The shader's weights (CubeLight): face area |nS.i| x its cosine to the eye, back faces 0.
    static double[] FilterWeights(double nx, double ny, double rx, double ry)
    {
        double fx = nx >= 0 ? 1 : -1, fy = ny >= 0 ? 1 : -1;
        double wx = Math.Abs(nx) * Math.Max(0, -fx * rx), wy = Math.Abs(ny) * Math.Max(0, -fy * ry);
        return new[] { wx / (wx + wy), wy / (wx + wy) };
    }

    // Brute force in 2D: column c (x in [c, c+1)) is solid below floor(-c / run), a staircase rising toward -x
    // with risers facing +x. Parallel rays along (rx, ry) walk the grid (DDA); the share whose first solid
    // cell was entered across a vertical line, i.e. through a riser.
    static double RiserShare(int run, double rx, double ry)
    {
        double len = Math.Sqrt(rx * rx + ry * ry); rx /= len; ry /= len;
        int risers = 0, hits = 0;
        const int N = 2000;
        for (int i = 0; i < N; i++)
        {
            double s = (i + 0.5) / N * 200.0;
            double ox = -ry * s - rx * 300.0, oy = rx * s - ry * 300.0 + 50.0;
            int cx = (int)Math.Floor(ox), cy = (int)Math.Floor(oy);
            if (cy < Height(cx, run)) continue;
            int sx = rx > 0 ? 1 : -1, sy = ry > 0 ? 1 : -1;
            double tdx = rx != 0 ? Math.Abs(1 / rx) : 1e30, tdy = ry != 0 ? Math.Abs(1 / ry) : 1e30;
            double tmx = rx != 0 ? (rx > 0 ? cx + 1 - ox : ox - cx) * tdx : 1e30;
            double tmy = ry != 0 ? (ry > 0 ? cy + 1 - oy : oy - cy) * tdy : 1e30;
            for (int k = 0; k < 5000; k++)
            {
                bool vert = tmx < tmy;
                if (vert) { cx += sx; tmx += tdx; } else { cy += sy; tmy += tdy; }
                if (cy < Height(cx, run)) { hits++; if (vert) risers++; break; }
            }
        }
        return (double)risers / hits;
    }

    static int Height(int c, int run) => (int)Math.Floor(-(double)c / run);

    [Test]
    public void FilterWeights_MatchABruteForceStaircase()
    {
        foreach (int run in new[] { 1, 3 })
        foreach (var dir in new[] { (-1.0, -0.3), (-1.0, -1.0), (-0.3, -1.0), (0.2, -1.0) })
        {
            // Smooth normal of the staircase: rises 1 per `run` toward -x -> n ~ (1, run) normalised.
            double l = Math.Sqrt(1 + run * run);
            double[] w = FilterWeights(1.0 / l, run / l, dir.Item1, dir.Item2);
            double brute = RiserShare(run, dir.Item1, dir.Item2);
            Assert.AreEqual(brute, w[0], 0.03, $"run {run}, ray {dir}: riser share");
        }
    }
}
