// Assets/Game/Playground.ReactionDemo.cs
//
// -reactiondemo (task 93e1, GAMEPLAY_PHASE_PLAN row RX1): one screenshot per
// adjacency pair, through the REAL GPU fluid CA of a play session (run-play.sh
// EXTRA="-reactiondemo"). Four marble trays (3 m inside) ahead of the play
// start, one per pair, built and filled through EditService; noon, clock
// paused, the camera looks away from the sun. Each tray's materials are
// counted from ChunkStore after the GPU's ops were applied (the GPU-path
// evidence; ReactionPairTests is the CPU reference's).
//   1  water poured onto a lava floor   -> grey cooled-stone crust + steam
//   2  water poured onto a loam floor   -> dark mud under the shallow water
//   3  a lava block set on sand         -> glass (near half of the lava cleared to show it)
//   4  a lava block set on snow         -> melt water, then steam + cooled stone
// Shots: persistentDataPath/ReactionDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator ReactionDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready || (!_poseReady && Time.unscaledTime - t0 < 60f))
            yield return null;
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "ReactionDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Walk;
        SnapPlayerToCamera();
        var m = _player.Motor;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int2 fw = new int2((int)math.round(math.sin(math.radians(awayYaw))), (int)math.round(math.cos(math.radians(awayYaw))));
        if (fw.x != 0 && fw.y != 0) fw.y = 0;   // an axis, so the trays are boxes
        int2 side = new int2(-fw.y, fw.x);
        float axisYaw = math.degrees(math.atan2(fw.x, fw.y));
        int3 sv = CoordMath.WorldToVoxel(m.PositionM);
        Debug.Log($"[ReactionDemo] start {sv}, sun-away yaw {awayYaw:F0} -> axis {axisYaw:F0}");

        byte marble = Materials.FromTg2(TerrainGen.GenMat.Marble);
        const float Pitch = 40f;
        int3 At(int k, int l, int y) => new int3(sv.x + fw.x * k + side.x * l, y, sv.z + fw.y * k + side.y * l);
        // Box from two corners given in (ahead, side) offsets around a centre.
        void Box(int3 c, int k0, int l0, int k1, int l1, int y0, int y1, byte mat)
        {
            int3 a = new int3(c.x + fw.x * k0 + side.x * l0, y0, c.z + fw.y * k0 + side.y * l0);
            int3 b = new int3(c.x + fw.x * k1 + side.x * l1, y1, c.z + fw.y * k1 + side.y * l1);
            _edits.SetBox(math.min(a, b), math.max(a, b), mat);
        }
        // A marble tray, inner 30 x 30 voxels, its inner floor `depth` voxels of `floor`.
        // Returns the first row above the floor material.
        int Tray(int3 c, byte floor, int depth)
        {
            int gy = -1;
            for (int k = -16; k <= 16; k += 4) for (int l = -16; l <= 16; l += 4)
                gy = math.max(gy, DryTopY(c.x + fw.x * k + side.x * l, c.z + fw.y * k + side.y * l));
            if (gy < 0) gy = sv.y - 1;
            Box(c, -20, -20, 19, 19, gy + 1, gy + 50, Materials.Air);             // clear above (slopes, plants)
            Box(c, -16, -16, 15, 15, gy - 8, gy + depth + 1, marble);             // the tray block: rim 0.3 m
            Box(c, -15, -15, 14, 14, gy - 2, gy + depth + 1, Materials.Air);      // above the floor; hollow it
            Box(c, -15, -15, 14, 14, gy - 2, gy + depth - 3, floor);              // the floor material
            return gy + depth - 2;
        }
        void Count(string tag, int3 c, int y0)
        {
            var n = new System.Collections.Generic.SortedDictionary<string, int>();
            for (int k = -15; k <= 14; k++) for (int l = -15; l <= 14; l++)
            for (int y = y0 - 6; y < y0 + 60; y++)
            {
                int3 v = new int3(c.x + fw.x * k + side.x * l, y, c.z + fw.y * k + side.y * l);
                byte b = _store.GetVoxel(v);
                if (b == Materials.Air || b == marble) continue;
                string key = Items.Name(b);
                n[key] = n.TryGetValue(key, out int q) ? q + 1 : 1;
            }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in n) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(", ");
            Debug.Log($"[ReactionDemo] {tag} (GPU CA, read from ChunkStore): {sb}");
        }
        IEnumerator Wait(float seconds, float pitch)
        {
            float tw = Time.unscaledTime;
            while (Time.unscaledTime - tw < seconds) { _cam?.SetLook(axisYaw, pitch); _demoInput = default(MotorInput); yield return null; }
        }
        // A marble pillar 4.5 m behind the tray's centre, its top 1.8 m above the
        // tray floor: the eye looks down ~40 deg over the rim into the tray.
        IEnumerator StandBehind(int3 c, int yFloor)
        {
            int top = yFloor + 18;
            Box(c, -49, -4, -41, 4, top, top + 40, Materials.Air);
            Box(c, -48, -3, -42, 3, yFloor - 40, top - 1, marble);
            int3 s = new int3(c.x - fw.x * 45, 0, c.z - fw.y * 45);
            m.Teleport(new float3(s.x * 0.1f + 0.05f, top * 0.1f + 0.02f, s.z * 0.1f + 0.05f));
            yield return Wait(1.5f, Pitch);
        }

        // ---- 1. Water + lava -> steam + cooled stone.
        int3 cA = At(50, -96, 0);
        int yA = Tray(cA, Materials.Lava, 3);
        yield return StandBehind(cA, yA);
        Box(cA, -5, -5, 4, 4, yA + 6, yA + 13, Materials.Water);   // 800 voxels, 0.6 m above the lava
        yield return Wait(8f, Pitch);
        Count("1 water+lava", cA, yA);
        yield return DemoShot(dir, "1_water_lava");

        // ---- 2. Water + loam -> mud (the water stays).
        int3 cB = At(50, -32, 0);
        int yB = Tray(cB, Materials.FromTg2(TerrainGen.GenMat.Loam), 3);
        yield return StandBehind(cB, yB);
        Box(cB, -3, -3, 2, 2, yB + 2, yB + 5, Materials.Water);    // 144 voxels: a puddle, not a flood
        yield return Wait(10f, Pitch);
        Count("2 water+loam", cB, yB);
        yield return DemoShot(dir, "2_water_loam");

        // ---- 3. Sand + lava -> glass (the lava stays).
        int3 cC = At(50, 32, 0);
        int yC = Tray(cC, Materials.FromTg2(TerrainGen.GenMat.Sand), 3);
        yield return StandBehind(cC, yC);
        Box(cC, -4, -4, 3, 3, yC, yC + 3, Materials.Lava);    // 256 voxels resting on the sand
        yield return Wait(10f, Pitch);
        Count("3 sand+lava (before clearing)", cC, yC);
        Box(cC, -15, -15, -1, 14, yC, yC + 12, Materials.Air); // clear the near half's lava: the glass under it shows
        yield return Wait(0.3f, Pitch);
        Count("3 sand+lava (near half cleared)", cC, yC);
        yield return DemoShot(dir, "3_sand_lava");

        // ---- 4. Snow + lava -> water, which then quenches the lava.
        int3 cD = At(50, 96, 0);
        int yD = Tray(cD, Materials.FromTg2(TerrainGen.GenMat.Snow), 3);
        yield return StandBehind(cD, yD);
        Box(cD, -4, -4, 3, 3, yD, yD + 3, Materials.Lava);
        yield return Wait(8f, Pitch);
        Count("4 snow+lava", cD, yD);
        yield return DemoShot(dir, "4_snow_lava");

        Debug.Log("[ReactionDemo] done");
        Application.Quit();
    }
}
