// ==========================================
// Assets/CoreEngine/Rendering/LightSourceBuffer.cs
//
// PHASE 7, §13 file 1: "the flat point-light buffer (§3.8)."
//
// §3.8, which is the entire specification for this file:
//
//     "v1 uses a flat GraphicsBuffer<LightSource> (position, color, range --
//      A.6). ... a naive 'sum all lights in range per pixel' loop is 2M pixels
//      x N lights -- 64 torches in a cave ~= 133M attenuation calcs/frame,
//      breaching the GPU budget underground. v1 default: a single small GLOBAL
//      cap MAX_ACTIVE_LIGHTS (§0.2) on the total number of simultaneously-
//      active lights the raymarcher considers ... Per-brick clustering (each
//      brick carrying its own <=8 nearby light indices) is DEFERRED TO v1.5."
//
// and the acceptance clause that makes the cap a behaviour rather than a
// number:
//
//     "(Phase 7 test: place MAX_ACTIVE_LIGHTS torches in view, confirm the
//      raymarch budget holds; PLACING MORE THAN THE CAP SHOULD DEGRADE
//      GRACEFULLY -- FURTHEST-FROM-PLAYER LIGHTS DROP FIRST -- NOT SILENTLY
//      IGNORE NEW TORCHES.)"
//
// ---------------------------------------------------------------------------
// THE DISTINCTION THAT MAKES "NOT SILENTLY IGNORE NEW TORCHES" TRUE
// ---------------------------------------------------------------------------
// REGISTRATION IS UNCAPPED. SELECTION IS CAPPED. Those are two different
// collections and conflating them is the only way to get this wrong.
//
// A torch that is placed is ALWAYS recorded in `_registered`, however many
// already exist -- so placing the 33rd torch is a real edit that really
// happened, the voxel is really there, and removing a nearer torch really does
// bring it back. What the cap bounds is `SelectActive`: the at-most-32 lights
// handed to the shader this frame, chosen nearest-first from the player.
//
// Had the cap been applied at registration instead, the 33rd torch would place
// a voxel that emits nothing, forever, with no feedback -- which is precisely
// the "silently ignore new torches" failure §3.8 names. The HUD shows
// `active/registered/cap` for the same reason: the degradation has to be
// visible, not merely graceful.
//
// ---------------------------------------------------------------------------
// WHY NEAREST-FIRST IS A FULL SORT AND NOT A HEAP
// ---------------------------------------------------------------------------
// §0.1 invariant 9: the dumbest implementation that works, wins. The cap is 32
// and v1 lights are, in §3.8's own words, "genuinely sparse -- a handful of
// placed torches, not a lit city". Sorting a few dozen entries once a frame is
// not measurable next to a 2M-pixel dispatch, and a partial-selection heap
// would be a clever answer to a problem this scope does not have. If v1.5's
// per-brick clustering ever arrives, it replaces this wholesale anyway.
//
// NO PER-FRAME ALLOCATION (§0.1 invariant 3). The scratch list, the key array
// and the staging array are all allocated once at construction and reused; the
// sort is in-place over a pre-sized array.
// ==========================================

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

namespace VoxelEngine.Rendering
{
    public sealed class LightSourceBuffer : IDisposable
    {
        /// One placed emissive voxel. The VOXEL is the identity -- a light is a
        /// terrain byte's consequence, not an entity with a lifetime of its own
        /// (§6.6, §0.1 invariant 1), so its key is where it is.
        public struct Registered
        {
            public int3 voxel;
            public byte material;
            public float r, g, b;
            public float range;
        }

        private readonly Dictionary<long, Registered> _registered = new Dictionary<long, Registered>();

        // Pre-allocated scratch, sized to the cap where the cap applies and
        // grown only when the REGISTERED set outgrows it (which is not a hot
        // path -- it happens on placement, not per frame).
        private Registered[] _scratch = new Registered[64];
        private float[] _scratchDist = new float[64];
        private readonly LightSource[] _staging = new LightSource[EngineConfig.MAX_ACTIVE_LIGHTS];

        private GraphicsBuffer _gpu;

        /// How many lights exist in the world right now, ignoring the cap.
        public int RegisteredCount => _registered.Count;

        /// How many were uploaded on the last Upload() -- at most
        /// MAX_ACTIVE_LIGHTS. The HUD shows this beside RegisteredCount so the
        /// §3.8 degradation is visible rather than merely graceful.
        public int ActiveCount { get; private set; }

        /// True when the cap is actually biting, i.e. lights exist that are not
        /// being drawn. Surfaced for the HUD and for the rig's gate 5.
        public bool CapIsBinding => RegisteredCount > EngineConfig.MAX_ACTIVE_LIGHTS;

        /// The GPU-side A.6 buffer. Always MAX_ACTIVE_LIGHTS entries so the
        /// shader binds one fixed-size buffer and reads ActiveCount of it --
        /// resizing a StructuredBuffer per frame would be an allocation on the
        /// hot path (§0.1 invariant 3).
        public GraphicsBuffer Buffer
        {
            get
            {
                if (_gpu == null)
                    _gpu = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                                              EngineConfig.MAX_ACTIVE_LIGHTS,
                                              LightSource.SizeBytes);
                return _gpu;
            }
        }

        // =================================================================
        // Registration -- uncapped, keyed by voxel
        // =================================================================

        /// Voxel -> dictionary key. Uniqueness is all that is needed (the
        /// Registered record carries the real coordinate), so the fields are
        /// masked rather than sign-extended. 21 bits per axis covers +-1,048,576
        /// voxels = +-104 km, far past any world §2.4 defines.
        private static long Key(int3 v) =>
            ((long)(v.x & 0x1FFFFF) << 42) | ((long)(v.y & 0x1FFFFF) << 21) | (long)(v.z & 0x1FFFFF);

        /// §6.6's "emissives additionally enqueue into the light system when
        /// placed." Returns true if this voxel is now a light.
        ///
        /// Idempotent: registering the same voxel twice replaces the record
        /// rather than adding a second light at the same place. That matters
        /// because EditService calls this on every write, including writes that
        /// re-place the same material.
        public bool Register(int3 voxel, byte material)
        {
            if (!MaterialRules.TryGetLight(material, out float r, out float g, out float b,
                                           out float rangeScale))
                return false;

            _registered[Key(voxel)] = new Registered
            {
                voxel = voxel,
                material = material,
                r = r, g = g, b = b,
                range = LightingConfig.Active.lightRangeM * rangeScale,
            };
            return true;
        }

        /// Removes the light at a voxel, if there was one. Returns true if
        /// something was removed. Called when an emissive voxel is dug out or
        /// overwritten -- §6.6 only says emissives enqueue when placed, but a
        /// torch that keeps lighting a room after you mine it is a bug in any
        /// reading.
        public bool Unregister(int3 voxel) => _registered.Remove(Key(voxel));

        /// Drops every light. Used when a world is torn down or a rig resets;
        /// never on the hot path.
        public void Clear() { _registered.Clear(); ActiveCount = 0; }

        public bool IsRegistered(int3 voxel) => _registered.ContainsKey(Key(voxel));

        // =================================================================
        // Selection -- §3.8's cap, and its degradation rule
        // =================================================================

        /// Fills `dest` with at most MAX_ACTIVE_LIGHTS lights, NEAREST TO
        /// `playerPosM` FIRST, and returns how many were written.
        ///
        /// §3.8: "furthest-from-player lights drop first." Sorting ascending by
        /// distance and truncating is exactly that statement, and the
        /// truncation is the only place the cap applies.
        ///
        /// STATIC AND PURE OVER ITS INPUTS so EditMode can prove the
        /// degradation rule without a GPU, a scene or a ChunkStore -- the same
        /// reason PlayerConfig.Parse is a pure static function. The mutation
        /// check for this method inverts the comparator to drop NEAREST first
        /// and confirms LightCapTests goes red.
        /// `all`/`allCount` is a plain array and a length rather than an
        /// IReadOnlyList, and that is not a style preference. Passing a struct
        /// view through an interface parameter BOXES it -- one heap allocation
        /// per frame, on the render path, which is exactly what §0.1 invariant
        /// 3 forbids ("the per-frame render/edit/fluid paths allocate nothing").
        /// The first draft of this file did that; an array plus a count cannot.
        public static int SelectActive(Registered[] all, int allCount, float3 playerPosM,
                                       Registered[] scratch, float[] scratchDist,
                                       LightSource[] dest)
        {
            int n = allCount;
            if (n > scratch.Length || n > scratchDist.Length)
                throw new ArgumentException("scratch arrays are smaller than the registered set");
            if (n > all.Length)
                throw new ArgumentException("allCount exceeds the source array");

            for (int i = 0; i < n; i++)
            {
                Registered reg = all[i];
                scratch[i] = reg;
                float3 p = (new float3(reg.voxel.x, reg.voxel.y, reg.voxel.z) + 0.5f) * 0.1f;
                scratchDist[i] = math.distancesq(p, playerPosM);
            }

            // Insertion sort, ascending by squared distance to the player.
            // Chosen over Array.Sort deliberately: Array.Sort with a comparator
            // allocates a comparer and boxes on some runtimes, and §0.1
            // invariant 3 forbids hot-path allocation. At n in the tens this is
            // also simply faster.
            for (int i = 1; i < n; i++)
            {
                float d = scratchDist[i];
                Registered v = scratch[i];
                int j = i - 1;
                while (j >= 0 && scratchDist[j] > d)
                {
                    scratchDist[j + 1] = scratchDist[j];
                    scratch[j + 1] = scratch[j];
                    j--;
                }
                scratchDist[j + 1] = d;
                scratch[j + 1] = v;
            }

            int count = math.min(n, math.min(dest.Length, EngineConfig.MAX_ACTIVE_LIGHTS));
            float k = LightingConfig.Active.pointFalloffK;
            float intensity = LightingConfig.Active.lightIntensity;
            for (int i = 0; i < count; i++)
            {
                Registered reg = scratch[i];
                float3 p = (new float3(reg.voxel.x, reg.voxel.y, reg.voxel.z) + 0.5f) * 0.1f;
                dest[i] = new LightSource
                {
                    x = p.x, y = p.y, z = p.z,
                    r = reg.r * intensity, g = reg.g * intensity, b = reg.b * intensity,
                    range = reg.range,
                    k = k,
                };
            }
            return count;
        }

        /// Snapshot of the registered set, in no particular order. ALLOCATES,
        /// so it is for tests and diagnostics only -- Upload() copies into its
        /// own pre-allocated scratch and never calls this.
        public Registered[] SnapshotRegistered()
        {
            var a = new Registered[_registered.Count];
            int i = 0;
            foreach (Registered r in _registered.Values) a[i++] = r;
            return a;
        }

        // =================================================================
        // Upload -- §3.9's CPU -> GPU light-sources row
        // =================================================================

        /// Selects and uploads this frame's lights. §3.9's sync-contract table
        /// already lists "light sources" as a CPU->GPU row with mechanism
        /// "SetData or LockBufferForWrite per §3.7's per-buffer test" and
        /// staleness "none (dispatch after write)".
        ///
        /// SetData, not LockBufferForWrite, and the choice follows §3.7's own
        /// rule rather than being picked: this buffer is written rarely (only
        /// when lights move or the player crosses far enough to reorder them)
        /// and read per-pixel by the raymarcher -- the same shape as the
        /// clipmap, for which Phase 1 measured SetData the winner because it
        /// lands device-local for fast reads. 1 KB is not worth a second
        /// benchmark to confirm a decision already made for a 244 MB buffer.
        public void Upload(float3 playerPosM)
        {
            int n = _registered.Count;
            if (_scratch.Length < n)
            {
                // Grows only when the REGISTERED set grows, i.e. on placement,
                // never per frame -- so this is not a hot-path allocation.
                int cap = math.max(n, _scratch.Length * 2);
                _scratch = new Registered[cap];
                _scratchDist = new float[cap];
            }

            int i = 0;
            foreach (Registered reg in _registered.Values) _scratch[i++] = reg;

            ActiveCount = SelectActive(_scratch, n, playerPosM,
                                       _scratchSecondary(n), _scratchDist, _staging);

            // Entries past ActiveCount are stale from a previous frame. They are
            // never read (the shader loops to _ActiveLightCount) but leaving a
            // dead torch's position in the buffer would make a hex dump lie, so
            // they are zeroed -- §3.7's "a fresh GraphicsBuffer contains
            // whatever was in GPU memory" lesson, applied to a buffer that is
            // re-uploaded rather than freshly allocated.
            for (int j = ActiveCount; j < _staging.Length; j++) _staging[j] = default;

            Buffer.SetData(_staging);
        }

        // A second scratch array for the sort, so SelectActive never sorts the
        // array it is reading from.
        private Registered[] _sortScratch = new Registered[64];
        private Registered[] _scratchSecondary(int n)
        {
            if (_sortScratch.Length < n) _sortScratch = new Registered[math.max(n, _sortScratch.Length * 2)];
            return _sortScratch;
        }

        public void Dispose()
        {
            _gpu?.Release();
            _gpu = null;
        }
    }
}
