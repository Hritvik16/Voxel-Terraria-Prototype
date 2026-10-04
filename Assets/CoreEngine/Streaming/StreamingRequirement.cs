// Assets/CoreEngine/Streaming/StreamingRequirement.cs
//
// TASK 16b (owner, 2026-10-03: "How fast chunks load and unload should be
// mathematically derived ... calculate the WORST CASE and go from there").
// The ONE absolute streaming requirement, computed from the LIVE constants so a
// constant change moves the requirement instead of silently invalidating it.
// Pure arithmetic, no runtime effect: nothing in the engine reads it; the
// -streamprobe report (PlaygroundCapture.StreamProbe.cs) compares measured
// capacity against it. Derivation and the measured gap: KNOWN_ISSUES.md
// "STREAMING REQUIREMENT (worst case, derived 2026-10-03)".
//
// THE WORST CASE (every factor at its worst):
//   - speed v: the brief's Decided row "late-game flight: fast, about 60 m/s".
//     Nothing in the game goes faster: the player's terminal fall speed is
//     55 m/s (PlayerConfig.terminalSpeedMps) with 5.2 m/s of run on top. The
//     dev fly camera (PlaygroundFlyCamera, up to 120 x4 m/s) is a tool, not a
//     game movement.
//   - heading: the 3-D direction that brings the most new chunks per metre.
//     Crossing an X or Z chunk plane adds a face of side x L chunks; crossing a
//     Y layer adds side x side. So the rate is (|vx|+|vz|) side L / E +
//     |vy| side^2 / E, maximised over |v| = v by Cauchy-Schwarz:
//     v / E * sqrt(2 (side L)^2 + side^4) -- a diagonal dive at ~65 deg below
//     horizontal along a mountainside. A fast fall uses the centred band rule
//     (StreamManager.FALL_SPEED_CENTRED), which follows the camera layer.
//   - terrain: skimming the ground, so no chunk is air (no uniform fast path):
//     every chunk of the band costs a full generation.
//   - timing: a chunk is requested in the Update of the frame the camera crosses
//     a chunk plane (StreamManager.Update -> RebuildPendingSet), so it can be up
//     to one frame late. There is no request-side hysteresis in X/Z; in Y the
//     centred band moves V_STREAM_LAG + 1 layers at once (a burst).
//   - R_d, the voxel-detail radius: the far band takes over at the first
//     NON-resident chunk column (Raymarch.compute FarStartT), so voxel detail
//     ends exactly where residency ends: R_d = the load radius. A chunk is
//     requested when its near face is r x E away, i.e. already ON R_d. The
//     prefetch margin (r_request - r_detail) x E is ZERO today.
namespace VoxelEngine.Streaming
{
    public static class StreamingRequirement
    {
        /// The brief's "Decided" row: late-game flight, about 60 m/s.
        public const float V_MAX_MPS = 60f;
        /// The frame budget the burst must land in: 60 FPS.
        public const float FRAME_HZ = 60f;
        /// CoordMath.WorldToVoxel multiplies by 10 (0.1 m voxels).
        public const float VOXELS_PER_METRE = 10f;

        public struct Inputs
        {
            public float vMaxMps;
            public float chunkEdgeM;          // E
            public int requestRadiusChunks;   // Chebyshev radius chunks are requested to
            public int detailRadiusChunks;    // R_d in chunks: where the far band takes over
            public int bandLayers;            // L: generated Y layers
            public int verticalShiftLayers;   // layers one Y recentre adds at once
            public float frameHz;
        }

        public struct Result
        {
            public int side;                  // 2 r + 1
            public double aHorizontal;        // chunks/s, level diagonal flight
            public double aVertical;          // chunks/s, straight up/down
            public double aMax;               // chunks/s, the worst 3-D heading
            public int faceBurst;             // one X or Z plane crossing
            public int cornerBurst;           // X and Z in the same frame
            public int verticalBurst;         // one Y recentre
            public int bandChunks;            // a cold start / teleport fills this many
            public double marginM;            // prefetch margin, request point -> R_d
            public double latencyBudgetS;     // request -> on GPU, worst case (p99); 0 if none
            public bool latencyMeetable;      // false: no throughput can meet the worst case
            public double admitsPerFrame;     // aMax / frameHz (= evictions per frame too)
            public double coldFillS;          // bandChunks / aMax (proposed teleport target)
        }

        /// The live engine constants for the vertically streamed (tg2) world.
        public static Inputs Live() => new Inputs
        {
            vMaxMps = V_MAX_MPS,
            chunkEdgeM = EngineConfig.CHUNK_EDGE_VOXELS / VOXELS_PER_METRE,
            requestRadiusChunks = EngineConfig.TG2_LOAD_RADIUS_CHUNKS,
            // FarStartT hands rays to the far band at the residency edge, so the
            // detail radius IS the load radius until something decouples them.
            detailRadiusChunks = EngineConfig.TG2_LOAD_RADIUS_CHUNKS,
            bandLayers = 2 * StreamManager.V_LOAD_LAYERS + 1,
            verticalShiftLayers = StreamManager.V_STREAM_LAG + 1,
            frameHz = FRAME_HZ,
        };

        public static Result Compute(Inputs i)
        {
            var r = new Result();
            int side = 2 * i.requestRadiusChunks + 1;
            double face = (double)side * i.bandLayers;    // chunks per X or Z plane crossing
            double layer = (double)side * side;           // chunks per Y layer crossing
            double perM = i.vMaxMps / i.chunkEdgeM;       // plane crossings per second per unit heading
            r.side = side;
            r.aHorizontal = System.Math.Sqrt(2.0) * perM * face;
            r.aVertical = perM * layer;
            r.aMax = perM * System.Math.Sqrt(2.0 * face * face + layer * layer);
            r.faceBurst = side * i.bandLayers;
            r.cornerBurst = (2 * side - 1) * i.bandLayers;
            r.verticalBurst = i.verticalShiftLayers * side * side;
            r.bandChunks = side * side * i.bandLayers;
            r.marginM = (i.requestRadiusChunks - i.detailRadiusChunks) * (double)i.chunkEdgeM;
            // The latency-worst heading is along an axis (closing speed v on the
            // face); the request can be one frame late.
            double budget = r.marginM / i.vMaxMps - 1.0 / i.frameHz;
            r.latencyMeetable = budget > 0.0;
            r.latencyBudgetS = r.latencyMeetable ? budget : 0.0;
            r.admitsPerFrame = r.aMax / i.frameHz;
            r.coldFillS = r.bandChunks / r.aMax;
            return r;
        }

        /// The largest speed the worst-case heading sustains at a given throughput
        /// (chunks/s). Information only, not a target.
        public static double SustainedSpeedMps(Inputs i, double capacityPerS)
        {
            int side = 2 * i.requestRadiusChunks + 1;
            double face = (double)side * i.bandLayers, layer = (double)side * side;
            return capacityPerS * i.chunkEdgeM / System.Math.Sqrt(2.0 * face * face + layer * layer);
        }

        /// "REQUIRED x, CAPACITY y -> MET / NOT MET (short by fx)" for a quantity
        /// where bigger is better.
        public static string Verdict(double required, double capacity, string unit, string fmt = "F1")
        {
            string head = $"REQUIRED {required.ToString(fmt)}{unit}, CAPACITY {capacity.ToString(fmt)}{unit} -> ";
            if (capacity >= required) return head + "MET";
            return head + (capacity > 0 ? $"NOT MET (short by {required / capacity:F1}x)" : "NOT MET (capacity 0)");
        }
    }
}
