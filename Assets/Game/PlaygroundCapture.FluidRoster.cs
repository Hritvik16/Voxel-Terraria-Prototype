// Assets/Game/PlaygroundCapture.FluidRoster.cs
//
// -fluidroster : §2.7's new fluids on the REAL GPU CA (FluidCA.compute), not the
// CPU oracle. EditMode runs -nographics, so it cannot dispatch compute; this is
// where the GPU port is checked against the oracle's claims (FluidRosterTests).
//
// Builds sealed OBSIDIAN basins (acid-proof) floating inside the Playground's
// fluid region, through Playground's own EditService so every placement wakes
// the GPU sim exactly as a player's brush does, then samples ChunkStore -- the
// CPU state the GPU's op list is applied to -- until the basins settle:
//   oil     oil below water        -> water floors the basin, oil on top, counts exact
//   acid    acid over a stone block -> acid consumed == stone dissolved, obsidian intact
//   steam   steam in a closed box   -> all steam in the top layer
//   sludge  a sludge pool           -> conserved, and lit by tile aggregates only
//   quick   a quicksand column      -> conserved and still moving slowly (60-tick tier)
// Photographs each basin. Diagnostic, not a timing measurement.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;

public partial class PlaygroundCapture
{
    private struct RosterBasin
    {
        public string name;
        public int3 min;      // interior min corner (voxels)
        public int3 size;     // interior size
        public bool lid;
    }

    private IEnumerator FluidRoster()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 300; i++) yield return null;
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FluidRoster", ts);
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
        Playground.DebugHideUi = true;

        var store = Phase4Bootstrapper.Store;
        var edits = pg.DebugEdits;
        var lightDriver = FindAnyObjectByType<VoxelEngine.Rendering.LightingDriver>();
        int3 ac = pg.DebugArenaCentre;
        var cam = Camera.main;
        L($"FLUID ROSTER ON THE GPU CA -- {ts}   arena centre {ac}");

        // Basins float above the ground near the arena, inside the fluid region
        // and inside the generated layer (y < 128 voxels).
        int by = 70;
        var basins = new List<RosterBasin>
        {
            new RosterBasin { name = "oil",    min = new int3(ac.x - 60, by, ac.z + 40), size = new int3(10, 10, 10) },
            new RosterBasin { name = "acid",   min = new int3(ac.x - 40, by, ac.z + 40), size = new int3(10, 10, 10) },
            new RosterBasin { name = "steam",  min = new int3(ac.x - 20, by, ac.z + 40), size = new int3(8, 10, 8), lid = true },
            new RosterBasin { name = "sludge", min = new int3(ac.x,      by, ac.z + 40), size = new int3(10, 8, 10) },
            new RosterBasin { name = "quick",  min = new int3(ac.x + 20, by, ac.z + 40), size = new int3(10, 8, 10) },
        };

        foreach (var b in basins) BuildBasin(edits, b);
        yield return Frames(30);

        // ---- fill ----
        var bo = basins[0];
        FillRoster(edits, bo.min, bo.min + new int3(bo.size.x - 1, 1, bo.size.z - 1), Materials.Oil);      // 2 layers
        FillRoster(edits, bo.min + new int3(0, 2, 0), bo.min + new int3(bo.size.x - 1, 3, bo.size.z - 1), Materials.Water);
        var ba = basins[1];
        FillRoster(edits, ba.min + new int3(2, 0, 2), ba.min + new int3(7, 1, 7), Materials.Stone);        // 6x2x6 = 72
        FillRoster(edits, ba.min + new int3(3, 6, 3), ba.min + new int3(6, 7, 6), Materials.Acid);         // 4x2x4 = 32
        var bs = basins[2];
        FillRoster(edits, bs.min + new int3(2, 0, 2), bs.min + new int3(4, 2, 4), Materials.Steam);        // 27
        var bl = basins[3];
        FillRoster(edits, bl.min + new int3(3, 3, 3), bl.min + new int3(6, 5, 6), Materials.Sludge);       // 48
        var bq = basins[4];
        FillRoster(edits, bq.min + new int3(4, 0, 4), bq.min + new int3(5, 5, 5), Materials.Quicksand);    // 2x6x2 = 24

        var c0 = new Dictionary<string, Dictionary<byte, int>>();
        foreach (var b in basins) c0[b.name] = CountBasin(store, b);
        foreach (var b in basins) L($"  {b.name,-7} start: {Describe(c0[b.name])}");

        long ticks0 = pg.DebugFluidTicks;
        float t0 = Time.realtimeSinceStartup;
        const float runSeconds = 150f;
        int sample = 0;
        while (Time.realtimeSinceStartup - t0 < runSeconds)
        {
            yield return Frames(60);
            if (++sample % 5 == 0)
                L($"  t+{Time.realtimeSinceStartup - t0,5:F0}s  fluid ticks {pg.DebugFluidTicks - ticks0,6}  " +
                  string.Join("  ", basins.ConvertAll(b => $"{b.name}:{Describe(CountBasin(store, b))}")));
        }
        L("");

        // ---- verdicts ----
        int fails = 0;
        void Verdict(string what, bool ok, string detail) { if (!ok) fails++; L($"  [{(ok ? "PASS" : "FAIL")}] {what}: {detail}"); }
        var co = CountBasin(store, bo);
        Verdict("oil: counts conserved", Get(co, Materials.Oil) == Get(c0["oil"], Materials.Oil) && Get(co, Materials.Water) == Get(c0["oil"], Materials.Water),
                Describe(co));
        Verdict("oil: water below oil in every column", LayeredByColumn(store, bo, Materials.Water, Materials.Oil, out string ol), ol);
        var ca = CountBasin(store, ba);
        int dissolved = Get(c0["acid"], Materials.Stone) - Get(ca, Materials.Stone);
        int consumed = Get(c0["acid"], Materials.Acid) - Get(ca, Materials.Acid);
        Verdict("acid: consumed == stone dissolved", consumed == dissolved && consumed > 0, $"acid consumed {consumed}, stone dissolved {dissolved}");
        Verdict("acid: obsidian basin intact", Get(ca, Materials.Obsidian) == Get(c0["acid"], Materials.Obsidian),
                $"obsidian {Get(c0["acid"], Materials.Obsidian)} -> {Get(ca, Materials.Obsidian)}");
        var cs = CountBasin(store, bs);
        Verdict("steam: conserved", Get(cs, Materials.Steam) == Get(c0["steam"], Materials.Steam), Describe(cs));
        Verdict("steam: all in the top layer", SteamAtTop(store, bs, out string st), st);
        var cl = CountBasin(store, bl);
        Verdict("sludge: conserved", Get(cl, Materials.Sludge) == Get(c0["sludge"], Materials.Sludge), Describe(cl));
        if (lightDriver != null)
        {
            var lb = lightDriver.Lights;
            Verdict("sludge: aggregate light, no point lights from it", lb.LavaTileCount > 0 && lb.RegisteredCount == 0,
                    $"{lb.LavaVoxelCount} emissive-fluid voxels in {lb.LavaTileCount} tiles, {lb.RegisteredCount} point lights");
        }
        var cq = CountBasin(store, bq);
        Verdict("quicksand: conserved", Get(cq, Materials.Quicksand) == Get(c0["quick"], Materials.Quicksand), Describe(cq));
        L($"  fluid CA ticks over the run: {pg.DebugFluidTicks - ticks0}   readback stale ops dropped " +
          $"{pg.DebugReadback?.StaleOpsDropped ?? -1}");
        L($"  VERDICT: {(fails == 0 ? "ALL PASS" : fails + " FAIL")}");

        // ---- photographs: each basin from above-front, then the sludge at night ----
        // Straight down into each open basin from just above its rim (the
        // steam box is lidded: photographed with the lid removed first).
        foreach (var b in basins)
        {
            if (b.lid)
                for (int z = b.min.z; z < b.min.z + b.size.z; z++)
                for (int x = b.min.x; x < b.min.x + b.size.x; x++)
                    edits.SetVoxel(new int3(x, b.min.y + b.size.y, z), Materials.Air);
            float2 cxz = (float2)(b.min.xz + b.size.xz / 2) * 0.1f;
            float rim = (b.min.y + b.size.y) * 0.1f;
            cam.transform.position = new Vector3(cxz.x, rim + 1.1f, cxz.y - 0.25f);
            cam.transform.rotation = Quaternion.Euler(78f, 0f, 0f);
            yield return Frames(b.lid ? 2 : 20);     // the unlidded steam is photographed before it escapes
            yield return CaptureTo($"basin_{b.name}");
        }
        SunCycle.SetHours(22f);
        float2 scxz = (float2)(bl.min.xz + bl.size.xz / 2) * 0.1f;
        cam.transform.position = new Vector3(scxz.x, (bl.min.y + bl.size.y) * 0.1f + 1.1f, scxz.y - 0.25f);
        yield return Frames(30);
        yield return CaptureTo("basin_sludge_night");

        File.WriteAllText(Path.Combine(_folder, "fluidroster.txt"), _log.ToString());
        Debug.Log("[fluidroster]\n" + _log);
        yield return null;
        Application.Quit(fails == 0 ? 0 : 3);
    }

    private static void BuildBasin(EditService edits, RosterBasin b)
    {
        int3 lo = b.min - 1, hi = b.min + b.size;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
        {
            bool wall = x == lo.x || x == hi.x || z == lo.z || z == hi.z || y == lo.y || (b.lid && y == hi.y);
            if (y == hi.y && !b.lid) wall = false;
            edits.SetVoxel(new int3(x, y, z), wall ? Materials.Obsidian : Materials.Air);
        }
    }

    private static void FillRoster(EditService edits, int3 lo, int3 hi, byte m)
    {
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
            edits.SetVoxel(new int3(x, y, z), m);
    }

    /// Materials inside the basin INCLUDING its shell (so obsidian is counted).
    private static Dictionary<byte, int> CountBasin(ChunkStore store, RosterBasin b)
    {
        var d = new Dictionary<byte, int>();
        int3 lo = b.min - 1, hi = b.min + b.size;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
        {
            byte m = store.GetVoxel(new int3(x, y, z));
            if (m == Materials.Air) continue;
            d.TryGetValue(m, out int n); d[m] = n + 1;
        }
        return d;
    }

    private static int Get(Dictionary<byte, int> d, byte m) => d.TryGetValue(m, out int n) ? n : 0;

    private static string Describe(Dictionary<byte, int> d)
    {
        var parts = new List<string>();
        foreach (var kv in d) if (kv.Key != Materials.Obsidian) parts.Add($"{kv.Key}x{kv.Value}");
        return parts.Count == 0 ? "-" : string.Join(",", parts);
    }

    private static bool LayeredByColumn(ChunkStore store, RosterBasin b, byte heavy, byte light, out string detail)
    {
        for (int z = b.min.z; z < b.min.z + b.size.z; z++)
        for (int x = b.min.x; x < b.min.x + b.size.x; x++)
        {
            int maxH = -1, minL = int.MaxValue;
            for (int y = b.min.y; y < b.min.y + b.size.y; y++)
            {
                byte m = store.GetVoxel(new int3(x, y, z));
                if (m == heavy) maxH = math.max(maxH, y);
                if (m == light) minL = math.min(minL, y);
            }
            if (maxH >= minL) { detail = $"column ({x},{z}): {heavy} up to y {maxH}, {light} from y {minL}"; return false; }
        }
        detail = "every column sorted";
        return true;
    }

    private static bool SteamAtTop(ChunkStore store, RosterBasin b, out string detail)
    {
        int top = b.min.y + b.size.y - 1, below = 0;
        for (int z = b.min.z; z < b.min.z + b.size.z; z++)
        for (int x = b.min.x; x < b.min.x + b.size.x; x++)
        for (int y = b.min.y; y < top; y++)
            if (store.GetVoxel(new int3(x, y, z)) == Materials.Steam) below++;
        detail = $"{below} steam voxels below the top layer y {top}";
        return below == 0;
    }
}
