// Assets/Game/PlaygroundCapture.PoolProbe.cs
//
// -poolprobe (overnight task 39): WHAT FILLS THE FLUID TILE POOL IN TG2, and
// does water placed while it reads FULL still flow? Diagnostic only -- no
// timing here is a measurement.
//
// Walks the camera from the play start (the arena column) to the nearest lake
// shore and then to the nearest sea coast at 12 m/s, eye 1.7 m over the map
// ground, logging every residency pass (Playground runs one every 20 frames):
// resident tiles, refusals that pass, and the cumulative exhaustion count. At
// each stop it takes a census of the tiles §7.4's wake radius wants (chunk
// resident + mask bit + nearest point inside the radius): how many, how far,
// which are resident, and what material they hold (sampled every 4th voxel).
// At the lake shore it places a 7x21x7 water box (the RMB edit path) and opens
// a water vent (the V key's emitter), and counts where the water is after.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Simulation;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator PoolProbe()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + (ArgInt("-poolfix", 1) != 0 ? "_fix" : "_old") + "_poolprobe");
        Directory.CreateDirectory(_folder);
        LightingDriver.ClockPaused = true;
        SunCycle.SetHours(12f);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
        Playground.DebugHideUi = true;
        var cam = Camera.main;
        var store = Phase4Bootstrapper.Store;
        var far = FarField.Active;
        var sm = Phase4Bootstrapper.Streamer;
        L($"POOL PROBE -- {ts}");
        if (!Phase4Bootstrapper.IsTg2World || far == null || !far.UsesTg2 || pg == null || pg.DebugTiles == null)
        {
            L("needs -world tg2 and the Playground's tiled fluid sim");
            File.WriteAllText(Path.Combine(_folder, "poolprobe.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var tiles = pg.DebugTiles;
        var fl = pg.DebugFluid;
        bool fix = ArgInt("-poolfix", 1) != 0;
        FluidTileResidency.LegacyScanOrder = !fix;
        fl.DisplaceForWakes = fix;
        L($"-poolfix {(fix ? "1: nearest-first residency + wakes displace farther tiles (task 39)" : "0: the OLD behaviour (scan-order residency, wakes into a full pool refused)")}");
        var map = far.Tg2Map;
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = far.Tg2SeaShift;
        L($"pool: {tiles.TileCapacity} tiles of {tiles.TileEdge}^3, GPU {tiles.GpuBytes(16) / 1048576.0:F0} MiB; " +
          $"wake {fl.ActiveRadiusVoxels}v sleep {fl.SleepRadiusVoxels}v; slot cap {fl.SlotCapacity}");

        // ---- the three stops, from the map ----
        pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
        yield return null;
        Vector3 start = cam.transform.position;
        int n = map.Spec.mapCells;
        float cv = map.Spec.mapCellVoxels;
        int2 CellOf(Vector3 m) => new int2((int)((m.x * 10f + off.x) / cv), (int)((m.z * 10f + off.y) / cv));
        Vector3 M(int2 c, float yM) => new Vector3(((c.x + 0.5f) * cv - off.x) * 0.1f, yM, ((c.y + 0.5f) * cv - off.y) * 0.1f);
        int2 s0 = CellOf(start);
        bool IsLake(int i) => map.WaterClass[i] == TerrainGen.Hydrology.LAKE;
        bool IsSea(int i) => map.Height[i] <= 0f && map.WaterClass[i] != TerrainGen.Hydrology.LAKE;
        bool IsDry(int i) => map.Height[i] > 5f && map.WaterClass[i] == TerrainGen.Hydrology.DRY;
        // nearest water cell of a kind, then the first dry cell on the way back to the start
        bool Shore(Func<int, bool> wet, out int2 shore, out int2 water)
        {
            shore = water = default; long best = long.MaxValue;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (!wet(z * n + x)) continue;
                long d = (long)(x - s0.x) * (x - s0.x) + (long)(z - s0.y) * (z - s0.y);
                if (d < best) { best = d; water = new int2(x, z); }
            }
            if (best == long.MaxValue) return false;
            float2 dir = math.normalizesafe((float2)(s0 - water));
            for (int k = 1; k < 200; k++)
            {
                int2 c = water + (int2)math.round(dir * k);
                if (math.any(c < 0) || math.any(c >= n)) break;
                if (IsDry(c.y * n + c.x)) { shore = c; return true; }
            }
            return false;
        }
        bool hasLake = Shore(IsLake, out int2 lakeShore, out int2 lakeCell);
        bool hasSea = Shore(IsSea, out int2 coastShore, out int2 seaCell);
        L($"start {start:F1} (map cell {s0}); lake {(hasLake ? $"cell {lakeCell}, shore {lakeShore} {Vector3.Distance(start, M(lakeShore, start.y)):F0} m" : "none")}; " +
          $"sea {(hasSea ? $"cell {seaCell}, shore {coastShore} {Vector3.Distance(start, M(coastShore, start.y)):F0} m" : "none")}");

        float Eye(Vector3 p)
        {
            float g = TgGround(map, p.x * 10f + off.x, p.z * 10f + off.y);
            return (g - seaShift) * 0.1f + 1.7f;
        }

        // ---- stop 1: the start ----
        start.y = Eye(start);
        cam.transform.position = start;
        cam.transform.rotation = Quaternion.Euler(10f, 45f, 0f);
        yield return NearSettle(far, sm);
        yield return Frames(120);
        L(""); L("==== STOP start ====");
        PoolCensus(pg, store, "start");

        // ---- walk to the lake shore, then the coast ----
        if (hasLake)
        {
            Vector3 ls = M(lakeShore, 0f); ls.y = Eye(ls);
            Vector3 lw = M(lakeCell, 0f); lw.y = ls.y;
            yield return PoolWalk(pg, far, sm, ls, "lake");
            L(""); L("==== STOP lake shore ====");
            PoolCensus(pg, store, "lake");
            yield return PoolPlaceTest(pg, store, ls, (lw - ls).normalized, "lake");
        }
        if (hasSea)
        {
            Vector3 cs = M(coastShore, 0f); cs.y = Eye(cs);
            Vector3 cw = M(seaCell, 0f); cw.y = cs.y;
            yield return PoolWalk(pg, far, sm, cs, "coast");
            L(""); L("==== STOP coast ====");
            PoolCensus(pg, store, "coast");
            yield return PoolPlaceTest(pg, store, cs, (cw - cs).normalized, "coast");
            cam.transform.position = cs;
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(cw.x - cs.x, -0.15f * Vector3.Distance(cw, cs), cw.z - cs.z).normalized, Vector3.up);
            yield return Frames(30);
            yield return CaptureTo("coast_view");
        }

        L(""); L($"END: wake displacements {fl.WakeTilesDisplacedTotal:N0}; refusals total {tiles.PoolExhaustionsTotal:N0} over {pg.DebugResidencyRefreshes} residency passes; " +
                 $"acquired {tiles.TilesAcquiredTotal:N0} released {tiles.TilesReleasedTotal:N0} peak {tiles.PeakResidentTiles}");
        MemLedgerIfAsked();
        File.WriteAllText(Path.Combine(_folder, "poolprobe.txt"), _log.ToString());
        Debug.Log($"[poolprobe] report {_folder}");
        Application.Quit(0);
    }

    /// 12 m/s straight line to `to` (teleporting to 540 m short of it first if
    /// further), logging each residency pass on the way.
    private IEnumerator PoolWalk(Playground pg, FarField far, StreamManager sm, Vector3 to, string name)
    {
        var cam = Camera.main;
        var tiles = pg.DebugTiles;
        int2 off = far.Tg2OffsetVoxels; int seaShift = far.Tg2SeaShift; var map = far.Tg2Map;
        Vector3 from = cam.transform.position;
        float dist = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z));
        if (dist > 540f)
        {
            from = Vector3.Lerp(to, from, 540f / dist);
            from.y = (TgGround(map, from.x * 10f + off.x, from.z * 10f + off.y) - seaShift) * 0.1f + 1.7f;
            cam.transform.position = from;
            L($"  teleported to {from:F1} ({dist - 540f:F0} m), settling");
            yield return NearSettle(far, sm);
            dist = 540f;
        }
        Vector3 dir = new Vector3(to.x - from.x, 0f, to.z - from.z).normalized;
        cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        L(""); L($"---- WALK to {name}: {from:F1} -> {to:F1}, {dist:F0} m at 12 m/s ----");
        L("  frame   along_m  resident  chunks  inR_cand  acq  already  REFUSED  relR  relO  displ  exhaustions_total");
        long lastPass = pg.DebugResidencyRefreshes;
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            float along = math.min(t * 12f, dist);
            Vector3 p = from + dir * along;
            p.y = (TgGround(map, p.x * 10f + off.x, p.z * 10f + off.y) - seaShift) * 0.1f + 1.7f;
            cam.transform.position = p;
            yield return null;
            if (pg.DebugResidencyRefreshes != lastPass)
            {
                lastPass = pg.DebugResidencyRefreshes;
                var st = pg.DebugLastResidency;
                int cand = st.TilesAcquired + st.TilesAlreadyResident + st.TilesRefusedPoolFull;
                L($"  {Time.frameCount,6}  {along,7:F0}  {tiles.ResidentTiles,8}  {st.ChunksScanned,6}  {cand,8}  {st.TilesAcquired,3}  {st.TilesAlreadyResident,7}  " +
                  $"{st.TilesRefusedPoolFull,7}  {st.TilesReleasedByRadius,4}  {st.TilesReleasedOrphaned,4}  {st.TilesDisplaced,5}  {tiles.PoolExhaustionsTotal,10:N0}");
            }
            if (along >= dist) break;
        }
        yield return NearSettle(far, sm);
        yield return Frames(60);
    }

    private struct PoolBand { public int cand, res, water, sand, mixed, lava, other, unsampled, full; }

    /// Every tile §7.4's radius wants right now, by distance band and material.
    private void PoolCensus(Playground pg, ChunkStore store, string tag)
    {
        var tiles = pg.DebugTiles;
        var fl = pg.DebugFluid;
        int3 c = fl.PlayerVoxel;
        int R = fl.ActiveRadiusVoxels;
        long R2 = (long)R * R;
        float[] edges = { 160f, 320f, 640f, 960f, R };
        var bands = new PoolBand[edges.Length];
        float nearestRefused = float.MaxValue, farthestRes = 0f;
        var otherMats = new Dictionary<byte, int>();
        int3 lo = CoordMath.VoxelToChunk(c - R), hi = CoordMath.VoxelToChunk(c + R);
        for (int cz = lo.z; cz <= hi.z; cz++)
        for (int cy = lo.y; cy <= hi.y; cy++)
        for (int cx = lo.x; cx <= hi.x; cx++)
        {
            var cc = new int3(cx, cy, cz);
            Chunk ch = store.GetChunk(cc);
            if (ch == null) continue;
            ulong mask = ch.fluidTileMask;
            while (mask != 0UL)
            {
                int bit = math.tzcnt(mask); mask &= mask - 1UL;
                int3 tic = new int3(bit & 3, (bit >> 2) & 3, (bit >> 4) & 3);
                ChunkFluidMask.TileBounds(cc, tic, out int3 tLo, out int3 tHi);
                int3 d = math.max(math.max(tLo - c, c - tHi), 0);
                long d2 = (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z;
                if (d2 > R2) continue;
                float dv = math.sqrt(d2);
                int b = 0; while (b < edges.Length - 1 && dv > edges[b]) b++;
                bool res = tiles.TryGetSlot(ChunkFluidMask.AbsoluteTile(cc, tic)) != FluidTileMap.NO_TILE;
                if (res) farthestRes = math.max(farthestRes, dv); else nearestRefused = math.min(nearestRefused, dv);
                int w = 0, s = 0, l = 0, o = 0, mob = 0;
                for (int z = 0; z < 32; z += 4)
                for (int y = 0; y < 32; y += 4)
                for (int x = 0; x < 32; x += 4)
                {
                    byte m = store.GetVoxel(tLo + new int3(x, y, z));
                    if (!MaterialRules.IsMobile(m)) continue;
                    mob++;
                    if (m == Materials.Water) w++;
                    else if (m == Materials.Sand) s++;
                    else if (m == Materials.Lava) l++;
                    else { o++; otherMats.TryGetValue(m, out int k); otherMats[m] = k + 1; }
                }
                ref PoolBand pb = ref bands[b];
                pb.cand++; if (res) pb.res++;
                if (mob == 0) pb.unsampled++;
                else if (l > 0) pb.lava++;
                else if (w > 0 && s == 0 && o == 0) pb.water++;
                else if (s > 0 && w == 0 && o == 0) pb.sand++;
                else if (o > 0 && w == 0 && s == 0) pb.other++;
                else pb.mixed++;
                if (mob == 512) pb.full++;
            }
        }
        fl.ReadSlotCounters(out uint hw, out uint ever);
        uint free = fl.ReadFreeSlotCount();
        L($"  [{tag}] centre voxel {c}; resident {tiles.ResidentTiles}/{tiles.TileCapacity}; refusals total {tiles.PoolExhaustionsTotal:N0}; " +
          $"slots: high-water {hw:N0} ever-allocated {ever:N0} free-list {free:N0} -> live ~{(long)hw - math.min(free, hw):N0} (cap {fl.SlotCapacity:N0})");
        L("    band_m      cand  resident  water  sand  mixed  lava  other  mobile-between-samples  all-512-mobile");
        int tot = 0, totRes = 0;
        float prev = 0f;
        for (int b = 0; b < edges.Length; b++)
        {
            var pb = bands[b];
            tot += pb.cand; totRes += pb.res;
            L($"    {prev * 0.1f,3:F0}-{edges[b] * 0.1f,3:F0}  {pb.cand,8}  {pb.res,8}  {pb.water,5}  {pb.sand,4}  {pb.mixed,5}  {pb.lava,4}  {pb.other,5}  {pb.unsampled,22}  {pb.full,14}");
            prev = edges[b];
        }
        L($"    total     {tot,8}  {totRes,8}   nearest NON-resident {(nearestRefused == float.MaxValue ? "none" : $"{nearestRefused * 0.1f:F1} m")}, farthest resident {farthestRes * 0.1f:F1} m");
        // What a re-centre's seed would promote right now (CSWakeScan's
        // _RecentreSeed test, on the CPU store): mobile cells in resident tiles
        // with AIR in one of the five cells below them, that cell's tile resident.
        long seedW = 0, seedS = 0, seedO = 0, seedStepW = 0;
        var cells = new Dictionary<byte, long>();
        foreach (int3 t in tiles.ResidentTileCoords())
        {
            int3 o0 = t * tiles.TileEdge;
            for (int z = 0; z < 32; z++)
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int3 v = o0 + new int3(x, y, z);
                byte m = store.GetVoxel(v);
                if (!MaterialRules.IsMobile(m)) continue;
                int vy = MaterialRules.IsGasMaterial(m) ? 1 : -1;
                bool air = false, straight = false;
                for (int b = 0; b < 5 && !air; b++)
                {
                    int3 nb = v + (b == 0 ? new int3(0, vy, 0) : b == 1 ? new int3(-1, vy, 0) : b == 2 ? new int3(1, vy, 0)
                                 : b == 3 ? new int3(0, vy, -1) : new int3(0, vy, 1));
                    if (tiles.SlotForVoxel(nb) == FluidTileMap.NO_TILE) continue;
                    if (store.GetVoxel(nb) == Materials.Air) { air = true; straight = b == 0; }
                }
                if (!air) continue;
                if (m == Materials.Water) { seedW++; if (!straight) seedStepW++; }
                else if (m == Materials.Sand) seedS++;
                else { seedO++; cells.TryGetValue(m, out long k); cells[m] = k + 1; }
            }
        }
        var so = new System.Text.StringBuilder();
        foreach (var kv in cells) so.Append($" {kv.Key}x{kv.Value:N0}");
        L($"    seed-eligible cells now (CPU store): water {seedW:N0} (of which only a DIAGONAL below is air: {seedStepW:N0}), sand {seedS:N0}, other{so}");
        if (otherMats.Count > 0)
        {
            var sb = new System.Text.StringBuilder("    other mobile materials (samples):");
            foreach (var kv in otherMats) sb.Append($" {kv.Key}x{kv.Value}");
            L(sb.ToString());
        }
    }

    private static int CountMat(ChunkStore store, int3 lo, int3 hi, byte m)
    {
        int k = 0;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
            if (store.GetVoxel(new int3(x, y, z)) == m) k++;
        return k;
    }

    /// RMB's edit path (a water box) and the V key's vent, 2 m inland of the shore.
    private IEnumerator PoolPlaceTest(Playground pg, ChunkStore store, Vector3 shore, Vector3 toLake, string tag)
    {
        var cam = Camera.main;
        var tiles = pg.DebugTiles;
        var fl = pg.DebugFluid;
        toLake.y = 0f; toLake.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, toLake).normalized;
        Vector3 p = shore + toLake * 2f;
        float g = StoreGroundM(store, p.x, p.z, p.y + 10f);
        if (float.IsNaN(g)) { g = p.y - 1.7f; L("  place: no ground found under the shore point; using the map's"); }
        int3 gv = CoordMath.WorldToVoxel(new float3(p.x, g, p.z));
        int3 wLo = gv + new int3(-3, 20, -3), wHi = gv + new int3(3, 40, 3);
        int3 vent = gv + new int3((int)math.round(right.x * 60f), 18, (int)math.round(right.z * 60f));
        Vector3 eye = p - toLake * 9f + right * 3f; eye.y = g + 4f;
        cam.transform.position = eye;
        cam.transform.rotation = Quaternion.LookRotation((new Vector3(p.x, g + 1f, p.z) + right * 3f - eye).normalized, Vector3.up);
        yield return Frames(90);
        int3 tc = tiles.TileOf(gv + new int3(0, 30, 0));
        L($"  place: ground {g:F1} m at {gv}; water box {wLo}..{wHi} (tile {tc}, resident {tiles.TryGetSlot(tc) != FluidTileMap.NO_TILE}); " +
          $"vent at {vent} (tile {tiles.TileOf(vent)}, resident {tiles.TryGetSlot(tiles.TileOf(vent)) != FluidTileMap.NO_TILE}); " +
          $"pool {tiles.ResidentTiles}/{tiles.TileCapacity}, refusals {tiles.PoolExhaustionsTotal:N0}");
        yield return CaptureTo(tag + "_before");
        long q0 = fl.WakeRequestsQueuedTotal, r0 = fl.WakeRejectedOutOfRegion;
        int nW = pg.DebugEdits.SetBox(wLo, wHi, Materials.Water);
        pg.DebugOpenWaterVent(vent);
        int f0 = Time.frameCount;
        L($"  PLACED water {nW} voxels + vent open, frame {f0}: wakes queued +{fl.WakeRequestsQueuedTotal - q0}, " +
          $"refused (no tile) +{fl.WakeRejectedOutOfRegion - r0}; box tile resident now {tiles.TryGetSlot(tc) != FluidTileMap.NO_TILE}");
        int3 vLo = vent + new int3(-12, -4, -12), vHi = vent + new int3(12, 4, 12);
        L("    +frames  box_water  vent_layer_water  box_tile_res  vent_tile_res  pool  refusals  wake_refused");
        foreach (int k in new[] { 1, 20, 60, 120, 240, 480 })
        {
            while (Time.frameCount - f0 < k) yield return null;
            L($"    {k,7}  {CountMat(store, wLo, wHi, Materials.Water),9}  {CountMat(store, vLo, vHi, Materials.Water),16}  " +
              $"{tiles.TryGetSlot(tc) != FluidTileMap.NO_TILE,12}  {tiles.TryGetSlot(tiles.TileOf(vent)) != FluidTileMap.NO_TILE,13}  " +
              $"{tiles.ResidentTiles,4}  {tiles.PoolExhaustionsTotal,8:N0}  {fl.WakeRejectedOutOfRegion - r0,12}");
            if (k == 120) yield return CaptureTo(tag + "_after_2s");
            if (k == 480) yield return CaptureTo(tag + "_after_8s");
        }
        pg.DebugCloseVents();
        PoolCensus(pg, store, tag + ", after placing");
    }
}
