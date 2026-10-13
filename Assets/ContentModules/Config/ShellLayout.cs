// Assets/ContentModules/Config/ShellLayout.cs
//
// Amendment 8.14 §5 row 1 (engine step 11a, task 77): the LOD SHELLS' sizes,
// built once at start-up by a pure function of the quality preset, read-only
// afterwards. Every shell pool, directory and ring is allocated from it.
//
// THE RULES (8.14 §1-§2, rule A, owner Q2 (a)); nothing below is a free number:
//   c          = rows / vFOV (radians), vFOV 60 deg (LODConfig.DESIGN_VFOV_DEG)
//   shell k    voxel V = 0.1 * 2^k m, chunk E = 128 V = 12.8 * 2^k m
//   hand-over  shell k ends at r_k = c * V_k (capped at 6.6 km), where its
//              own voxel shrinks to 1 px; the next voxel arrives at 2 px
//   stored     chunk offsets within +-R_k = ceil(r_k / E_k) of the camera's
//              chunk, MINUS the "hole" the finer shell fully covers,
//              +-H_k = floor(r_(k-1) / E_k) - 1 (no hole when < 0), all axes
//   rings      XZ = pow2 >= 2 R_k + 1; Y = min(XZ, pow2 >= ceil(1,100 m / E_k) + 1)
//              (the world is 1.1 km tall, design doc §2). POWERS OF TWO,
//              asserted: indexed with & only (§6.2 phantom terrain, CLAUDE.md)
//   columns    stored chunk columns: min(2 R + 1, ceil(8 km / E) + 1)^2 - (2 H + 1)^2
//   bricks     est. = columns * 256 * density (3.36 / 2.93 / 2.33 dense bricks per
//              brick column at 0.1 / 0.2 / 0.4 m, MEASURED in task 55's ledger;
//              2.0 est. coarser); pool cap = est. * 1.25 until a measured
//              PeakUsed replaces it (row 1). Reproduces §2a (High), §2b
//              (Medium) and §H.2's caps: 288 k / 475 k / 665 k; Ultra 1,170 k
//              against §H.2's 1,158 k (its shell 6 world cut is ~51 columns, here 72).
// Shell 0 is the 0.1 m CPU window and its GPU mirror (TerrainClipmap); its
// directory box, pool cap and load radius are §H.2's rows, copied here.
// Rigs and capture modes pin High (row 16); High is the M1 8 GB headline.
using System;
using Unity.Mathematics;

public enum QualityPreset { Low = 0, Medium = 1, High = 2, Ultra = 3 }

public sealed class ShellLayout
{
    public const int MAX_SHELLS = 8;                 // shader arrays are sized for 8 (row 1)
    public const float VFOV_DEG = 60f;               // LODConfig.DESIGN_VFOV_DEG (§H.6: unchanged)
    public const float RANGE_CAP_M = 6600f;          // the last shell's reach (§2)
    public const float WORLD_EXTENT_M = 8000f;       // tg2: TerrainSpec.worldSizeVoxels 80,000
    public const float WORLD_HEIGHT_M = 1100f;       // -500 .. +600 m (design doc §2)
    public const float SHELL0_MARGIN_M = 12.8f;      // one 0.1 m chunk of prefetch (§2)
    public const float CAP_HEADROOM = 1.25f;         // peak x 1.25 (row 1)
    /// Directory pages (16 KB of brick handles each, one per NON-uniform
    /// chunk) per stored chunk column. NOT in the amendment: an est. of
    /// 11a, refused (row 15) when exceeded; the debug fill reports the peak.
    public const int PAGES_PER_COLUMN = 2;
    /// Shells 1-2 (task 83): MEASURED non-uniform chunks per column at seed 42's
    /// spawn, High (Logs/Shells/need83.txt: 1,232 and 1,113 over 160 columns;
    /// 11a's 2 ran out first, task 80), times CAP_HEADROOM (row 1).
    public const float PAGES_PER_COLUMN_S1 = 7.70f, PAGES_PER_COLUMN_S2 = 6.96f;

    /// Page cap of shell k over `cols` stored columns.
    public static int PageCapFor(int k, int cols)
        => k == 1 ? (int)Math.Ceiling(cols * PAGES_PER_COLUMN_S1 * CAP_HEADROOM)
         : k == 2 ? (int)Math.Ceiling(cols * PAGES_PER_COLUMN_S2 * CAP_HEADROOM)
         : cols * PAGES_PER_COLUMN;

    public struct Shell
    {
        public int Index;
        public float VoxelM, ChunkM;
        public float InnerM, OuterM;     // hand-over distances: this shell draws (InnerM, OuterM]
        public int Reach;                // R: chunk offsets stored, +-R
        public int Hole;                 // H: offsets the finer shell covers, +-H (-1 = none)
        public int3 Ring;                // pow2 ring dims, in this shell's chunks
        public int Columns;              // stored chunk columns (hollow, world-cut)
        public int EstBricks, PoolCap, PageCap;
        public int RingSlots => Ring.x * Ring.y * Ring.z;
        /// What 11a allocates on the GPU for this shell: bodies + pages + directory.
        public long BytesAllocated => (long)PoolCap * ShellBytes.BODY + (long)PageCap * ShellBytes.PAGE + (long)RingSlots * ShellBytes.DIR_ENTRY;
        /// 8.14 §2's MB column: bodies + 64 B occupancy per brick (est.) + directory, at the cap.
        public long BytesAmendmentFormula => (long)PoolCap * (ShellBytes.BODY + ShellBytes.OCCUPANCY_EST) + (long)RingSlots * ShellBytes.DIR_ENTRY;
    }

    public QualityPreset Preset { get; private set; }
    public int SizingRows { get; private set; }
    public int ShellCount { get; private set; }      // including shell 0
    public float C { get; private set; }             // px per (m of voxel per m of distance)
    public float Shell0RadiusM { get; private set; } // c * 0.1 + margin: 59 / 65 / 82 / 116 m
    public int LoadRadiusChunks { get; private set; }
    public int3 Shell0Directory { get; private set; }
    public int Shell0PoolCap { get; private set; }
    /// Shells 1+ by 8.14 §2's MB formula at the caps; compare §H.2's "shells 1+" row.
    public float ShellsTotalMBEstimate { get; private set; }
    private Shell[] _shells;
    /// Shell k (0 .. ShellCount-1). Shell 0's Ring is the 0.1 m GPU directory box.
    public Shell this[int k] => _shells[k];

    public static int Pow2Ceil(int v) { int p = 1; while (p < v) p <<= 1; return p; }
    public static bool IsPow2(int v) => v > 0 && (v & (v - 1)) == 0;

    public static QualityPreset Parse(string s, QualityPreset fallback = QualityPreset.High)
    {
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "low": return QualityPreset.Low;
            case "medium": return QualityPreset.Medium;
            case "high": return QualityPreset.High;
            case "ultra": return QualityPreset.Ultra;
            default: return fallback;
        }
    }

    public static ShellLayout From(QualityPreset p)
    {
        // §H.2's rows, per preset.
        int rows, shells, load, pool0; int3 dir0;
        switch (p)
        {
            case QualityPreset.Low:    rows = 480;  shells = 6; load = 5;  pool0 = 130_000; dir0 = new int3(16, 16, 16); break;
            case QualityPreset.Medium: rows = 540;  shells = 7; load = 6;  pool0 = 182_000; dir0 = new int3(16, 16, 16); break;
            case QualityPreset.High:   rows = 720;  shells = 8; load = 7;  pool0 = 242_000; dir0 = new int3(16, 16, 16); break;
            case QualityPreset.Ultra:  rows = 1080; shells = 7; load = 10; pool0 = 474_000; dir0 = new int3(32, 8, 32); break;
            default: throw new ArgumentOutOfRangeException(nameof(p));
        }
        var L = new ShellLayout
        {
            Preset = p, SizingRows = rows, ShellCount = shells, LoadRadiusChunks = load,
            Shell0PoolCap = pool0, Shell0Directory = dir0,
            C = (float)(rows / (VFOV_DEG * Math.PI / 180.0)),
            _shells = new Shell[shells],
        };
        float r0 = L.C * 0.1f;
        L.Shell0RadiusM = (float)(Math.Round(r0) + Math.Round(SHELL0_MARGIN_M));   // §H.2 rounds each: 69 + 13
        L._shells[0] = new Shell
        {
            Index = 0, VoxelM = 0.1f, ChunkM = 12.8f, InnerM = 0f, OuterM = r0,
            Reach = -1, Hole = -1, Ring = dir0, PoolCap = pool0,
        };
        double mb = 0;
        for (int k = 1; k < shells; k++)
        {
            float V = 0.1f * (1 << k), E = 12.8f * (1 << k);
            float r = Math.Min(L.C * V, RANGE_CAP_M), rPrev = Math.Min(L.C * V * 0.5f, RANGE_CAP_M);
            int R = (int)Math.Ceiling(r / E - 1e-4);
            int H = (int)Math.Floor(rPrev / E + 1e-4) - 1;
            int xz = Pow2Ceil(2 * R + 1);
            int y = Math.Min(xz, Pow2Ceil((int)Math.Ceiling(WORLD_HEIGHT_M / E) + 1));
            int d = Math.Min(2 * R + 1, (int)Math.Ceiling(WORLD_EXTENT_M / E) + 1);
            int hole = H >= 0 ? 2 * H + 1 : 0;
            int cols = d * d - hole * hole;
            double dens = k == 1 ? 2.93 : k == 2 ? 2.33 : 2.0;
            int est = (int)Math.Round(cols * 256 * dens);
            var s = new Shell
            {
                Index = k, VoxelM = V, ChunkM = E, InnerM = L._shells[k - 1].OuterM, OuterM = r,
                Reach = R, Hole = H, Ring = new int3(xz, y, xz), Columns = cols,
                EstBricks = est, PoolCap = (int)Math.Round(est * CAP_HEADROOM), PageCap = PageCapFor(k, cols),
            };
            if (!IsPow2(s.Ring.x) || !IsPow2(s.Ring.y) || !IsPow2(s.Ring.z))
                throw new InvalidOperationException($"shell {k} ring {s.Ring} is not a power of two (§6.2)");
            if (2 * R + 1 > s.Ring.x)
                throw new InvalidOperationException($"shell {k}: +-{R} chunks do not fit the {s.Ring.x}-chunk ring");
            L._shells[k] = s;
            mb += s.BytesAmendmentFormula / 1e6;
        }
        L.ShellsTotalMBEstimate = (float)mb;
        return L;
    }

    public int ShellsPoolCapTotal
    {
        get { int n = 0; for (int k = 1; k < ShellCount; k++) n += _shells[k].PoolCap; return n; }
    }
}

public static class ShellBytes
{
    public const int BODY = 512;          // 8^3 one-byte voxels
    public const int OCCUPANCY_EST = 64;  // per brick, 8.14 §2's MB column (est.; built in 11b with the air mips)
    public const int PAGE = 4096 * 4;     // 16 KB of brick handles per non-uniform chunk
    public const int DIR_ENTRY = 4;
}
