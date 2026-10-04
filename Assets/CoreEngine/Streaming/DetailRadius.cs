// Assets/CoreEngine/Streaming/DetailRadius.cs
//
// TASK 24 (owner Q13 (a), 2026-10-04): the voxel-detail radius at speed.
// OPT-IN, OFF by default (`-detailspeed 1`, last match wins; Playground F5).
// Shader: kernel CSMainDetail (VE_DETAIL_SPEED), dispatched only while the
// square is active; not combined with -checker (CSMainChecker wins).
// With it on, Raymarch.compute hands a ray to the far band at
// min(first non-resident chunk column, the camera-centred XZ square of
// half-width R_d(v)) and stops the voxel march at that square, so a chunk
// requested at r x E has StreamingRequirement.DETAIL_LATENCY_TARGET_S to load
// before the hand-over reaches it. Residency, requests and eviction are
// UNCHANGED: this only moves where voxels stop being DRAWN.
//
// The speed fed to StreamingRequirement.DetailRadiusM is the camera's
// HORIZONTAL path speed averaged over the last WINDOW_S seconds
// (DetailSpeedFilter): sum of per-frame XZ path lengths / sum of their frame
// times. Frame-time jitter cancels (a 30 ms frame moves 3x a 10 ms frame's
// distance), a single fast frame moves the mean by at most its extra distance
// / WINDOW_S, and a still camera reads exactly 0 -- so standing still is
// today's hand-over bit for bit (the shader uniform is 0, the clamp is off).
using Unity.Mathematics;

namespace VoxelEngine.Streaming
{
    public sealed class DetailSpeedFilter
    {
        public const float WINDOW_S = 0.5f;
        /// One frame moving further than this is a teleport, not motion: the
        /// window restarts (the streamer refills the band anyway).
        public const float TELEPORT_M = 128f;
        private const int CAP = 1024;

        private readonly float[] _tEnd = new float[CAP];
        private readonly float[] _dist = new float[CAP];
        private readonly float[] _dt = new float[CAP];
        private int _head, _count;
        private bool _has;
        private float _lastT;
        private float2 _lastP;

        public float SpeedMps { get; private set; }

        public void Reset()
        {
            _head = 0; _count = 0; _has = false; SpeedMps = 0f;
        }

        /// One sample per frame: the time (s) and the camera's XZ position (m).
        public float Push(float timeS, float2 posXZ)
        {
            if (!_has) { _has = true; _lastT = timeS; _lastP = posXZ; SpeedMps = 0f; return 0f; }
            float dt = timeS - _lastT;
            if (!(dt > 0f)) return SpeedMps;
            float seg = math.distance(posXZ, _lastP);
            _lastT = timeS; _lastP = posXZ;
            if (seg > TELEPORT_M) { _count = 0; SpeedMps = 0f; return 0f; }

            _tEnd[_head] = timeS; _dist[_head] = seg; _dt[_head] = dt;
            _head = (_head + 1) & (CAP - 1);
            if (_count < CAP) _count++;
            // Drop the oldest samples that ended before the window; keep >= 1.
            while (_count > 1 && _tEnd[(_head - _count + CAP) & (CAP - 1)] <= timeS - WINDOW_S) _count--;

            double d = 0, t = 0;
            for (int k = 0; k < _count; k++)
            {
                int i = (_head - 1 - k + CAP) & (CAP - 1);
                d += _dist[i]; t += _dt[i];
            }
            SpeedMps = t > 0 ? (float)(d / t) : 0f;
            return SpeedMps;
        }
    }

    public static class DetailRadius
    {
        /// `-detailspeed 1` turns the speed-dependent hand-over on. OFF by default.
        public static bool Enabled = ReadIntArg("-detailspeed", 0) != 0;

        public static readonly float LoadRadiusM =
            EngineConfig.TG2_LOAD_RADIUS_CHUNKS * (EngineConfig.CHUNK_EDGE_VOXELS / StreamingRequirement.VOXELS_PER_METRE);

        private static readonly DetailSpeedFilter _filter = new DetailSpeedFilter();
        private static int _frame = -1;

        /// Smoothed horizontal camera speed (m/s). Tracked whether or not the flag
        /// is on, so probes report the same R_d(v) in both arms.
        public static float SpeedMps => _filter.SpeedMps;
        /// R_d(v) in metres (LoadRadiusM when still).
        public static float RadiusM { get; private set; } = LoadRadiusM;

        /// Once per frame (repeat calls in the same frame are ignored).
        public static void Tick(int frame, float timeS, float3 camPosM)
        {
            if (frame == _frame) return;
            _frame = frame;
            _filter.Push(timeS, camPosM.xz);
            RadiusM = (float)StreamingRequirement.DetailRadiusM(_filter.SpeedMps, LoadRadiusM, StreamingRequirement.FRAME_HZ);
        }

        /// Raymarch.compute's _DetailRadiusVoxels: 0 = no clamp (flag off, or a
        /// still camera, where the hand-over is today's exactly).
        public static float ShaderRadiusVoxels =>
            Enabled && _filter.SpeedMps > 0f ? RadiusM * StreamingRequirement.VOXELS_PER_METRE : 0f;

        private static int ReadIntArg(string flag, int fallback)
        {
            string[] a = System.Environment.GetCommandLineArgs();
            int v = fallback;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == flag && int.TryParse(a[i + 1], out int p)) v = p;
            return v;
        }
    }
}
