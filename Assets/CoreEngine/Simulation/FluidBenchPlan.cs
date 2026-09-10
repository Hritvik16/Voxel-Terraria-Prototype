// Assets/CoreEngine/Simulation/FluidBenchPlan.cs
//
// THE PURE PLANNING MATH BEHIND THE DENSE-vs-TILED WALL-CLOCK A/B
// (Assets/Game/FluidABBenchmark.cs is the MonoBehaviour that runs it).
//
// WHY IT LIVES IN CoreEngine AND NOT BESIDE THE RIG. Assets/Game has no
// asmdef, so it compiles into Assembly-CSharp, which CoreEngine.Tests cannot
// reference. Anything that needs EditMode coverage has to be here. This file
// is therefore ONLY the parts a test can pin: config parsing, pocket layout,
// and the dense region sizing. Nothing in it touches the GPU, the ChunkStore,
// or Unity's frame loop.
//
// WHAT THE A/B IS ACTUALLY COMPARING, because it decides every number below.
// FluidGpuSimulation.DispatchCells is:
//
//     Tiles == null ? _regionCellCount            (DENSE: the whole box, every
//                                                  tick, however little fluid)
//                   : _activeTileCount * TileCells (TILED: only resident tiles)
//
// So the two paths do not differ in the CA's arithmetic at all -- they differ
// in HOW MANY CELLS get dispatched to reach the same fluid. Dense pays for its
// box; tiled pays for 32^3 = 32,768 cells per touched tile. That means tiling
// is NOT unconditionally cheaper: one small pool inside a tight dense box can
// dispatch FEWER cells than the tile that contains it. The ladder below exists
// to find where the crossover is, not to confirm a preferred answer.
//
// THE DENSE REGION IS DELIBERATELY STEELMANNED. SizeDenseRegion returns the
// SMALLEST power-of-two box that holds the scenario plus its spread margin --
// not the fixed 64^3/128^3 box the shipped dense path actually used. A dense
// number produced this way is the best dense could possibly do, so any tiled
// win measured against it is a conservative one. Stated here because a reader
// comparing these figures to run-fluid-activity's would otherwise have no way
// to know the box was re-sized per config.
//
// POWERS OF TWO ARE NOT COSMETIC. FluidGpuSimulation's constructor calls
// RequirePow2 on every region dimension, and §6.2's phantom-terrain bug is
// what happens when a non-power-of-two ring aliases silently instead of
// failing. SizeDenseRegion rounds UP to a power of two for that reason, and
// the EditMode suite pins it.

using System;
using Unity.Mathematics;

namespace VoxelEngine.Simulation
{
    /// One scenario in the A/B: which addressing path, how much fluid, and how
    /// scattered it is.
    public struct FluidBenchConfig
    {
        /// Raw label as it appeared on the command line, driftcheck suffix and
        /// all. This is what gets written to the CSV, so a row can always be
        /// traced back to the exact launch that produced it.
        public string Label;

        /// true = sparse tiles (_Tiled == 1), false = the dense region path.
        public bool Tiled;

        /// Voxels the scenario intends to place. The rig reports what it
        /// ACTUALLY placed and what actually went live beside this, because a
        /// target is an intention and neither of those is.
        public int TargetVoxels;

        /// How many disconnected pockets that volume is split across. 1 is a
        /// single contiguous body (step 1); >1 is step 2's scatter ladder.
        public int Pockets;

        /// A REPEAT_driftcheck run measures the same thing as its twin and
        /// exists only so cross-sweep drift is visible. It is never averaged
        /// with the original -- Amendment 8_9 §0 Rule 2 is explicit that a
        /// drifted driftcheck is a signal to re-run, not data to fold in.
        public bool IsDriftcheck;
    }

    /// One pocket of fluid: an inclusive-exclusive voxel box.
    public struct FluidBenchPocket
    {
        public int3 Lo;      // inclusive
        public int3 Size;    // voxels per axis
        public int Voxels => Size.x * Size.y * Size.z;
    }

    public static class FluidBenchPlan
    {
        /// Pockets are laid out on a grid this many voxels apart. It must
        /// exceed ChunkFluidMask.TILE_EDGE (32) or two "separate" pockets share
        /// a tile and the scatter ladder measures nothing -- 48 puts every
        /// pocket in its own tile with room to spread before they merge.
        public const int PocketSpacingVoxels = 48;

        /// Voxels of clearance added around a scenario's own extent before the
        /// dense box is sized, so fluid that spreads or falls a little does not
        /// immediately leave the region and stop being comparable.
        public const int SpreadMarginVoxels = 24;

        /// How far above the terrain surface a scenario is placed. Non-zero on
        /// purpose: fluid dropped from a height is still MOVING during the
        /// sampling window, and a settled pool measures the cost of doing
        /// nothing. The value is modest so the body is still airborne for most
        /// of the window without leaving the region.
        public const int DropHeightVoxels = 24;

        // =================================================================
        // Config parsing
        // =================================================================

        /// Grammar: <dense|tiled>[_s<pockets>]_v<voxels>[_REPEAT_driftcheck]
        ///
        ///   dense_v8000                  step 1, one contiguous body
        ///   tiled_v8000                  the same body, tiled addressing
        ///   tiled_s64_v8000              step 2, same volume in 64 pockets
        ///   tiled_v8000_REPEAT_driftcheck
        ///
        /// Throws on anything it does not understand rather than defaulting.
        /// A benchmark that silently measures a different config than the one
        /// named is worse than one that refuses to start: the CSV row would
        /// carry the label you asked for and the numbers of something else.
        public static FluidBenchConfig Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("empty benchmark config", nameof(raw));

            var cfg = new FluidBenchConfig { Label = raw, Pockets = 1 };
            string s = raw;

            const string drift = "_REPEAT_driftcheck";
            if (s.EndsWith(drift, StringComparison.Ordinal))
            {
                cfg.IsDriftcheck = true;
                s = s.Substring(0, s.Length - drift.Length);
            }

            string[] parts = s.Split('_');
            if (parts.Length < 2)
                throw new ArgumentException($"config '{raw}' needs at least <path>_v<voxels>");

            if (parts[0] == "tiled") cfg.Tiled = true;
            else if (parts[0] == "dense") cfg.Tiled = false;
            else throw new ArgumentException($"config '{raw}': path must be 'dense' or 'tiled', got '{parts[0]}'");

            bool sawVolume = false;
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length < 2) throw new ArgumentException($"config '{raw}': empty field");

                if (p[0] == 'v')
                {
                    cfg.TargetVoxels = ParseCount(p, raw);
                    sawVolume = true;
                }
                else if (p[0] == 's')
                {
                    cfg.Pockets = ParseCount(p, raw);
                    if (cfg.Pockets < 1)
                        throw new ArgumentException($"config '{raw}': pockets must be >= 1");
                }
                else throw new ArgumentException($"config '{raw}': unknown field '{p}'");
            }

            if (!sawVolume) throw new ArgumentException($"config '{raw}': missing v<voxels>");
            if (cfg.TargetVoxels < 1) throw new ArgumentException($"config '{raw}': volume must be >= 1");
            return cfg;
        }

        private static int ParseCount(string field, string raw)
        {
            if (!int.TryParse(field.Substring(1), out int n))
                throw new ArgumentException($"config '{raw}': '{field}' is not a number");
            return n;
        }

        // =================================================================
        // Pocket layout
        // =================================================================

        /// Splits TargetVoxels across Pockets and lays them out on a square-ish
        /// XZ grid centred on `centre`, each pocket sitting DropHeightVoxels
        /// above `surfaceY`.
        ///
        /// XZ and not a 3-D grid on purpose: an explosion scatters across a
        /// landscape, and stacking pockets vertically would have them fall
        /// through each other and merge, which is the one thing the scatter
        /// ladder must not let happen.
        ///
        /// Every pocket is the SAME shape, sized by PocketShape to land close to
        /// TargetVoxels/Pockets. Uniform pockets matter more than an exact
        /// total: if pockets differed in size the ladder would vary two things
        /// at once. The small residual error is reported, not hidden.
        public static FluidBenchPocket[] Layout(FluidBenchConfig cfg, int3 centre, int surfaceY)
        {
            int perPocket = Math.Max(1, cfg.TargetVoxels / cfg.Pockets);
            int3 shape = PocketShape(perPocket);

            var pockets = new FluidBenchPocket[cfg.Pockets];
            int cols = (int)Math.Ceiling(Math.Sqrt(cfg.Pockets));
            // Centre the grid on `centre` so the scenario stays symmetric about
            // the player voxel -- §7.4's radius is measured from there, and an
            // off-centre scatter would clip on one side only.
            int span = (cols - 1) * PocketSpacingVoxels;
            int originX = centre.x - span / 2;
            int originZ = centre.z - span / 2;

            for (int i = 0; i < cfg.Pockets; i++)
            {
                int gx = i % cols, gz = i / cols;
                pockets[i] = new FluidBenchPocket
                {
                    Lo = new int3(originX + gx * PocketSpacingVoxels - shape.x / 2,
                                  surfaceY + DropHeightVoxels,
                                  originZ + gz * PocketSpacingVoxels - shape.z / 2),
                    Size = shape,
                };
            }
            return pockets;
        }

        /// The box closest to `n` voxels, as near cubic as `n` allows.
        ///
        /// A PLAIN CUBE IS NOT GOOD ENOUGH, and step 2 is where that showed.
        /// round(n^(1/3)) quantises hard at small n: 512 pockets of 15 voxels
        /// rounds to a 2-cube, which is 8 -- so an "8000 voxel" 512-pocket
        /// scatter placed 4096, half the volume of every other rung, and the
        /// scatter ladder would have been comparing volume as much as scatter.
        /// Extending one axis instead keeps the error under one layer.
        public static int3 PocketShape(int n)
        {
            // Round, NOT Floor. Math.Pow(1000, 1.0/3.0) is 9.999999999999998,
            // so Floor turns a perfect 10-cube into 9x9x13 -- the EditMode
            // suite caught exactly that. Round is exact on perfect cubes, and
            // ceil() on the depth keeps the box >= n either way.
            int s = Math.Max(1, (int)Math.Round(Math.Pow(n, 1.0 / 3.0)));
            int depth = Math.Max(1, (int)Math.Ceiling((double)n / (s * s)));
            return new int3(s, s, depth);
        }

        /// Total voxels a layout will actually place.
        public static int PlacedVoxels(FluidBenchPocket[] pockets)
        {
            int n = 0;
            foreach (var p in pockets) n += p.Voxels;
            return n;
        }

        // =================================================================
        // Dense region sizing
        // =================================================================

        /// The smallest power-of-two box that contains every pocket, the
        /// surface they land on, and SpreadMarginVoxels of clearance all round.
        ///
        /// `surfaceY` is passed separately from the pockets because the pockets
        /// start ABOVE it: the region has to reach down to where the fluid ends
        /// up, not just to where it started, or the dense path would drop
        /// voxels out of region mid-fall and measure a shorter simulation than
        /// the tiled path does.
        public static int3 SizeDenseRegion(FluidBenchPocket[] pockets, int surfaceY, out int3 origin)
        {
            if (pockets == null || pockets.Length == 0)
                throw new ArgumentException("no pockets to size a region around", nameof(pockets));

            int3 lo = pockets[0].Lo;
            int3 hi = pockets[0].Lo + pockets[0].Size;
            foreach (var p in pockets)
            {
                lo = math.min(lo, p.Lo);
                hi = math.max(hi, p.Lo + p.Size);
            }

            // Reach down to the landing surface, and leave headroom above.
            lo.y = math.min(lo.y, surfaceY);
            lo -= SpreadMarginVoxels;
            hi += SpreadMarginVoxels;
            lo.y = math.max(lo.y, 0);

            int3 need = hi - lo;
            int3 dims = new int3(NextPow2(need.x), NextPow2(need.y), NextPow2(need.z));

            // Centre the box on the scenario rather than anchoring at `lo`:
            // rounding up to a power of two can add a lot of slack on one axis,
            // and putting it all on one side would push the fluid against a
            // wall of the region.
            int3 slack = dims - need;
            origin = lo - slack / 2;
            origin.y = math.max(origin.y, 0);
            return dims;
        }

        /// Smallest power of two >= v, for v >= 1.
        public static int NextPow2(int v)
        {
            if (v < 1) return 1;
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
