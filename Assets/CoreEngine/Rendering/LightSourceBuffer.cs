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

        /// A CPU-side mirror of what is currently on the GPU, so Upload can skip
        /// a SetData whose bytes would be identical. See the fix note in Upload.
        private readonly LightSource[] _uploaded = new LightSource[EngineConfig.MAX_ACTIVE_LIGHTS];
        private int _uploadedCount = -1;
        private bool _everUploaded;

        /// SetData calls actually issued. The point of the fix is that this stays
        /// flat while the player moves without touching a light, so a test can
        /// assert on it rather than on a frame time.
        /// STAGE H SEAM. When false (the default, and what ships unless the
        /// owner decides otherwise) a mobile emissive -- lava -- registers no
        /// point light and therefore does not illuminate its neighbours. When
        /// true it does, at whatever cost the measurement finds.
        ///
        /// Static rather than per-instance so a capture mode can flip it before
        /// the world is edited; it changes REGISTRATION, so flipping it after
        /// lava is already placed does nothing retroactively, and any harness
        /// using it must set it first and say so.
        public static bool AllowMobileEmissives = false;

        public long UploadsPerformed { get; private set; }

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
            // -----------------------------------------------------------------
            // MOBILE EMISSIVES ARE NOT POINT LIGHTS. §3.8's SCOPE, ENFORCED.
            //
            // §3.8 sizes this entire subsystem for "a handful of placed torches,
            // not a lit city", and §6.5 calls v1 lights "genuinely sparse".
            // Lava is `IsFluid | IsEmissive` -- emissive AND mobile -- so
            // without this predicate every lava CELL became a point light. The
            // Playground's lava vent budget alone is 12,000 voxels, and each one
            // went through §6.6's enqueue on the §8.3 edit path.
            //
            // MEASURED CONSEQUENCE (PHASE_7_COMPLETION §A.6): with lava flowing,
            // Upload() insertion-sorted thousands of entries every frame and the
            // moving frame cost 42 ms against 10.5 ms with the upload suspended.
            // My own comment on that sort read "at n in the tens this is also
            // simply faster" -- true, and the assumption behind it was wrong,
            // because a fluid can register itself.
            //
            // LAVA STILL READS HOT. MaterialPalette.IsSelfLit keeps its shading
            // cheat (it skips AO and the hemisphere term and is pushed above its
            // albedo), and A.7's `emissive` still contributes through C.9's
            // `+ emissive` term on the voxel itself. What it no longer does is
            // illuminate its NEIGHBOURS via the §3.8 buffer -- which §6.5 never
            // promised: "no propagation, no BFS ... light does not bend around
            // corners in v1".
            //
            // THE FLAGGED CONSEQUENCE: a lava lake no longer casts light onto
            // the cave around it. That is a visible change and it is the
            // owner's to accept or reject -- see PHASE_7_COMPLETION §A.6. The
            // alternative (per-brick light clustering, §3.8's MAX_LIGHTS_PER_BRICK)
            // is explicitly deferred to v1.5.
            // -----------------------------------------------------------------
            // STAGE H MADE THIS A MEASURABLE SEAM RATHER THAN A PERMANENT NO.
            //
            // The exclusion was added when selection was an O(n^2) insertion
            // sort and 12,000 lava voxels cost 30+ ms per frame. Selection is
            // now partial and O(n x 32), so the premise the exclusion rested on
            // no longer holds and the question deserves re-asking WITH A NUMBER
            // rather than inheriting the old answer. The default is unchanged
            // (excluded), because changing a default on an unmeasured guess is
            // how the first Stage A "fix" went wrong.
            if (!MaterialRules.TryGetLight(material, out float r, out float g, out float b,
                                           out float rangeScale))
                return false;

            // DECISION 8 (2026-09-24): a mobile emissive is still not a point
            // light of its own -- it is counted into its tile's AGGREGATE light
            // instead (see the LAVA AGGREGATE block below), which is what lets
            // lava light a cave without the two §H.1 failures. Returns false
            // because this voxel is not itself a registered light.
            if (!AllowMobileEmissives && MaterialRules.IsMobile(material))
            {
                AddLava(voxel, material);
                return false;
            }

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
        ///
        /// Also removes the voxel from its lava tile's aggregate, if it was
        /// lava; that removal does not count toward the return value, which
        /// stays "a registered point light was removed".
        public bool Unregister(int3 voxel)
        {
            RemoveLava(voxel);
            return _registered.Remove(Key(voxel));
        }

        /// Drops every light. Used when a world is torn down or a rig resets;
        /// never on the hot path.
        public void Clear()
        {
            _registered.Clear();
            _lavaVoxels.Clear();
            _lavaTiles.Clear();
            ActiveCount = 0;
            LavaActiveCount = 0;
        }

        // =================================================================
        // LAVA AGGREGATE -- one light per 32-voxel tile that contains lava
        // (owner decision 8; PHASE_7_COMPLETION §H.2 Option 2)
        // =================================================================
        //
        // §H.1 measured why lava cannot be one point light per voxel: the 32
        // nearest lava voxels are 32 adjacent cells, so C.11 sums 32 copies of
        // the same near-field light and saturates the wall to white; and the
        // cap is global, so a lake takes every slot and the player's own
        // torches go dark. One light per TILE fixes both: a 9,315-voxel lake
        // becomes a handful of lights 3.2 m apart, and those lights are capped
        // at LightingConfig.lavaLightMaxSlots, so placed torches always keep
        // MAX_ACTIVE_LIGHTS minus that share. §3.8's furthest-first rule holds
        // within each class.
        //
        // COST. The bookkeeping runs on the §8.3 edit path (one HashSet op and
        // a few integer adds per lava write -- the fluid apply writes at most
        // ~1,024 ops a frame). The per-frame part is over TILES, not voxels.
        //
        // WHERE THE LIGHT SITS: the tile's lava centroid in X/Z, and
        // lavaLightLiftM above the tile's TOP lava layer, found from a per-tile
        // layer histogram so it is exact under removal without rescanning.
        // Above the surface, not at the centroid, because a light inside a
        // pool would sit below the ground around it and N.L would leave that
        // ground unlit (decision 7 keeps point-light N.L on).
        //
        // THE TILE is 32 voxels, the same edge FluidTileMap uses by default.
        // It is not coupled to FluidTileMap (Rendering does not reference
        // Simulation); it only needs to be coarse enough that one light per
        // tile does not saturate, and fine enough that a pool's glow sits
        // where the pool is.
        public const int LavaTileShift = 5;
        private const int LavaTileEdge = 1 << LavaTileShift;

        private sealed class LavaTile
        {
            public int3 tile;
            public byte material;
            public int count;
            public long sumX, sumZ;
            public readonly int[] layer = new int[LavaTileEdge];
        }

        private readonly HashSet<long> _lavaVoxels = new HashSet<long>();
        private readonly Dictionary<long, LavaTile> _lavaTiles = new Dictionary<long, LavaTile>();
        private LightSource[] _lavaCand = new LightSource[16];
        private float[] _lavaCandDist = new float[16];

        /// Lava voxels counted into aggregates (not point lights).
        public int LavaVoxelCount => _lavaVoxels.Count;
        /// Tiles currently holding lava, i.e. aggregate lights that exist.
        public int LavaTileCount => _lavaTiles.Count;
        /// Aggregate lava lights uploaded on the last Upload() (<= the lava share).
        public int LavaActiveCount { get; private set; }

        private static long TileKey(int3 t) => Key(t);

        private void AddLava(int3 v, byte material)
        {
            if (!_lavaVoxels.Add(Key(v))) return;           // idempotent, like Register
            int3 t = v >> LavaTileShift;
            long tk = TileKey(t);
            if (!_lavaTiles.TryGetValue(tk, out LavaTile tile))
            {
                tile = new LavaTile { tile = t };
                _lavaTiles[tk] = tile;
            }
            tile.material = material;
            tile.count++;
            tile.sumX += v.x;
            tile.sumZ += v.z;
            tile.layer[v.y & (LavaTileEdge - 1)]++;
        }

        private void RemoveLava(int3 v)
        {
            if (!_lavaVoxels.Remove(Key(v))) return;
            int3 t = v >> LavaTileShift;
            long tk = TileKey(t);
            if (!_lavaTiles.TryGetValue(tk, out LavaTile tile)) return;
            tile.count--;
            tile.sumX -= v.x;
            tile.sumZ -= v.z;
            tile.layer[v.y & (LavaTileEdge - 1)]--;
            if (tile.count <= 0) _lavaTiles.Remove(tk);
        }

        /// The aggregate light a lava tile emits. PURE over its inputs so
        /// EditMode can pin the placement and scaling rules.
        public static LightSource LavaTileLight(int3 tile, int count, long sumX, long sumZ, int topLayer,
                                                byte material)
        {
            var c = LightingConfig.Active;
            MaterialRules.TryGetLight(material, out float r, out float g, out float b, out float rangeScale);
            float strength = c.lightIntensity * c.lavaLightIntensity *
                             math.sqrt(math.saturate(count / c.lavaLightFullCount));
            float topVoxelY = (tile.y << LavaTileShift) + topLayer + 1;       // top face of the top lava layer
            return new LightSource
            {
                x = ((float)sumX / count + 0.5f) * 0.1f,
                y = topVoxelY * 0.1f + c.lavaLightLiftM,
                z = ((float)sumZ / count + 0.5f) * 0.1f,
                r = r * strength, g = g * strength, b = b * strength,
                range = c.lightRangeM * rangeScale * c.lavaLightRangeScale,
                k = c.pointFalloffK,
            };
        }

        /// Appends up to `maxSlots` lava aggregates to `dest` from `start`,
        /// nearest to the player first. Returns how many were written.
        private int SelectLava(float3 playerPosM, LightSource[] dest, int start, int maxSlots)
        {
            int nt = _lavaTiles.Count;
            if (nt == 0 || maxSlots <= 0) return 0;
            if (_lavaCand.Length < nt)
            {
                // Grows only when the number of lava TILES grows -- on edits,
                // not per frame.
                int cap = math.max(nt, _lavaCand.Length * 2);
                _lavaCand = new LightSource[cap];
                _lavaCandDist = new float[cap];
            }
            int n = 0;
            foreach (LavaTile t in _lavaTiles.Values)
            {
                int top = LavaTileEdge - 1;
                while (top > 0 && t.layer[top] == 0) top--;
                LightSource ls = LavaTileLight(t.tile, t.count, t.sumX, t.sumZ, top, t.material);
                _lavaCand[n] = ls;
                _lavaCandDist[n] = math.distancesq(new float3(ls.x, ls.y, ls.z), playerPosM);
                n++;
            }
            int count = math.min(n, math.min(maxSlots, dest.Length - start));
            for (int slot = 0; slot < count; slot++)
            {
                int best = slot;
                for (int i = slot + 1; i < n; i++)
                    if (_lavaCandDist[i] < _lavaCandDist[best]) best = i;
                if (best != slot)
                {
                    (_lavaCandDist[slot], _lavaCandDist[best]) = (_lavaCandDist[best], _lavaCandDist[slot]);
                    (_lavaCand[slot], _lavaCand[best]) = (_lavaCand[best], _lavaCand[slot]);
                }
                dest[start + slot] = _lavaCand[slot];
            }
            return count;
        }

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
                                       LightSource[] dest,
                                       int maxCount = EngineConfig.MAX_ACTIVE_LIGHTS)
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

            // PARTIAL SELECTION, NOT A FULL SORT. Only the nearest
            // MAX_ACTIVE_LIGHTS are ever needed, so sorting the rest is work
            // whose result is discarded.
            //
            // THIS REPLACES A FULL INSERTION SORT, AND THE REASON IS A MEASURED
            // FAILURE RATHER THAN TIDINESS. That sort was O(n^2) and its own
            // comment justified it with "at n in the tens this is also simply
            // faster" -- correct for n in the tens, and the assumption that n
            // WOULD be in the tens came from §3.8's "a handful of placed
            // torches". Lava is emissive and mobile, so a lava flow registered
            // thousands of lights and the sort became ~144M operations per
            // frame (PHASE_7_COMPLETION §A.6).
            //
            // The predicate in Register() now stops mobile materials
            // registering at all, which addresses the CAUSE. This addresses the
            // HAZARD: selection is now O(n x cap) with cap = 32, so no future
            // caller -- an ore that glows, a v1.5 content pass -- can make this
            // quadratic again by registering more than expected. Defence in
            // depth, because the first assumption about n was wrong once
            // already.
            //
            // Still allocation-free (§0.1 invariant 3) and still exactly
            // §3.8's rule: the kept set is the nearest, so the dropped set is
            // the furthest.
            int count = math.min(n, math.min(dest.Length, math.min(maxCount, EngineConfig.MAX_ACTIVE_LIGHTS)));
            for (int slot = 0; slot < count; slot++)
            {
                int best = slot;
                for (int i = slot + 1; i < n; i++)
                    if (scratchDist[i] < scratchDist[best]) best = i;
                if (best != slot)
                {
                    (scratchDist[slot], scratchDist[best]) = (scratchDist[best], scratchDist[slot]);
                    (scratch[slot], scratch[best]) = (scratch[best], scratch[slot]);
                }
            }
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

            // ---------------------------------------------------------------
            // FAST PATH: NO LIGHTS AT ALL.
            //
            // This is the overwhelmingly common case -- a player who has not
            // placed a torch -- and before this it still cost a full
            // GraphicsBuffer.SetData of 32 zeroed entries EVERY FRAME. One
            // upload is still needed so the buffer is not whatever was in GPU
            // memory (§3.7's lesson), but exactly one, not one per frame.
            // ---------------------------------------------------------------
            if (n == 0 && _lavaTiles.Count == 0)
            {
                ActiveCount = 0;
                LavaActiveCount = 0;
                if (_uploadedCount != 0 || !_everUploaded)
                {
                    for (int j = 0; j < _staging.Length; j++) _staging[j] = default;
                    Buffer.SetData(_staging);
                    _uploadedCount = 0;
                    _everUploaded = true;
                    UploadsPerformed++;
                }
                return;
            }

            int i = 0;
            foreach (Registered reg in _registered.Values) _scratch[i++] = reg;

            // Lava's share first, so torches get everything else: placed
            // torches always keep MAX_ACTIVE_LIGHTS - lavaLightMaxSlots, and
            // all of it when no lava is near (decision 8).
            int lavaSlots = math.min(_lavaTiles.Count, LightingConfig.Active.lavaLightMaxSlots);
            int torches = SelectActive(_scratch, n, playerPosM,
                                       _scratchSecondary(n), _scratchDist, _staging,
                                       EngineConfig.MAX_ACTIVE_LIGHTS - lavaSlots);
            LavaActiveCount = SelectLava(playerPosM, _staging, torches, lavaSlots);
            ActiveCount = torches + LavaActiveCount;

            // Entries past ActiveCount are stale from a previous frame. They are
            // never read (the shader loops to _ActiveLightCount) but leaving a
            // dead torch's position in the buffer would make a hex dump lie, so
            // they are zeroed -- §3.7's "a fresh GraphicsBuffer contains
            // whatever was in GPU memory" lesson, applied to a buffer that is
            // re-uploaded rather than freshly allocated.
            for (int j = ActiveCount; j < _staging.Length; j++) _staging[j] = default;

            // ---------------------------------------------------------------
            // UPLOAD ONLY WHEN THE BYTES ACTUALLY CHANGED.
            //
            // THIS IS THE PHASE 7 STUTTER FIX, AND THE COMMENT THIS REPLACES
            // WAS WRONG IN A SPECIFIC, MEASURABLE WAY. It read: "there is
            // deliberately no dirty-check skipping the upload: the ACTIVE SET
            // depends on player position, so it changes when the player moves
            // even though no light did, and a 'skip if unchanged' test would
            // have to compare the selection anyway."
            //
            // Both halves of that were true and the conclusion was still wrong.
            // Comparing the selection IS cheap -- it is at most 32 structs of 8
            // floats, an array scan of ~1 KB. What is NOT cheap is
            // GraphicsBuffer.SetData on a buffer the raymarch kernel is reading:
            // it serialises against in-flight GPU work, and under camera motion
            // -- when the streamer is already uploading clipmap and cascade data
            // -- that stall dominates the frame.
            //
            // MEASURED, cooled, same session, moving camera with live fluid
            // (PHASE_7_COMPLETION §A):
            //     per-frame SetData      43.46 ms/frame   >33 ms on 1000 of 1000
            //     SetData suspended      11.25 ms/frame   >33 ms on 43.8 of 1000
            //     suspended + no probe    8.89 ms/frame   >33 ms on  6.3 of 1000
            //     pre-Phase-7 build       9.32 ms/frame
            // The comparison scan costs microseconds; the upload it avoids cost
            // roughly 32 ms. §0.1 invariant 9 ("do not optimize before
            // profiling") cuts both ways: this was not optimised before
            // profiling, it was PESSIMISED before profiling, by reasoning about
            // correctness and never measuring the cost of the conclusion.
            // ---------------------------------------------------------------
            if (!_everUploaded || _uploadedCount != ActiveCount || StagingDiffers())
            {
                Buffer.SetData(_staging);
                System.Array.Copy(_staging, _uploaded, _staging.Length);
                _uploadedCount = ActiveCount;
                _everUploaded = true;
                UploadsPerformed++;
            }
        }

        /// True when this frame's staging array differs from what is already on
        /// the GPU. Compares only the LIVE entries: everything past ActiveCount
        /// is zeroed on both sides by construction, so scanning it would be
        /// comparing zeroes to zeroes.
        ///
        /// Field-by-field rather than a memcmp because LightSource is a managed
        /// struct of floats and an exact equality test is what is wanted -- a
        /// light that moved by a float ULP genuinely did change, and NaN cannot
        /// occur here (positions come from integer voxel coords, colours and
        /// ranges from validated config).
        private bool StagingDiffers()
        {
            for (int i = 0; i < ActiveCount; i++)
            {
                LightSource a = _staging[i], b = _uploaded[i];
                if (a.x != b.x || a.y != b.y || a.z != b.z ||
                    a.r != b.r || a.g != b.g || a.b != b.b ||
                    a.range != b.range || a.k != b.k) return true;
            }
            return false;
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
