// ==========================================
// Assets/CoreEngine/Simulation/FluidReferenceCPU.cs
//
// Phase 5a, file 1 of §13's ordered list: the single-threaded CPU reference
// implementation of the fluid CA. §7.2: "The GPU version is what ships; the
// CPU version is what tells you the GPU version is right."
//
// -------------------------------------------------------------------------
// SCOPE -- WHAT THIS FILE IS NOT ALLOWED TO TOUCH
// -------------------------------------------------------------------------
// §13 Phase 5a: "a self-contained CPU basin test against the memory model,
// touching no streaming/delta/eviction machinery at all". Accordingly this
// class owns a PRIVATE byte array and reaches nothing else: no ChunkStore, no
// BrickDataPool, no StreamManager, no TerrainClipmap, no EditService, no
// IEditService.SetVoxel. §0.1 invariant 1 ("the CPU is the only thing that
// ever writes terrain") is not at stake here because this array is not
// terrain -- it is a sandbox that exists to be an oracle. When 5b ports these
// rules to HLSL, the moves come back as a write-op list and are applied
// through the real SetVoxel; that wiring is 5b's job, not this file's.
//
// -------------------------------------------------------------------------
// THREADING -- SINGLE-THREADED, DELIBERATELY
// -------------------------------------------------------------------------
// §7.3: the brick-granularity red-black IJobParallelFor decomposition is "not
// required if the CPU reference stays single-threaded, which is the simpler
// and recommended default." It stays single-threaded. There is consequently
// no claim RACE to resolve on this path, so there is no lock, no atomic and no
// Interlocked anywhere in this file, and there must not be one: 8.4's
// plain-write claim rule is a statement about GPU hardware, and importing
// synchronisation here would be solving a problem this implementation does not
// have. The claim array is still written and read exactly as the GPU will
// write and read it (last writer wins, §7.3), so the port stays a translation.
//
// -------------------------------------------------------------------------
// ADDRESSING (§0.1 invariant 2, A.5)
// -------------------------------------------------------------------------
// The sandbox is a rectangular block of BRICKS (8^3 voxels each), power-of-two
// in every dimension, addressed exactly the way the engine addresses brick
// bodies: voxelIndex = (brickIndex << 9) | localVoxelIndex. That is what lets
// FluidSlotCPU carry A.5's real field layout -- brickDataIndex +
// localVoxelOffset -- rather than an invented flat coordinate, so 5b's port
// does not have to re-derive the home address format. All of it is `>>`/`&`;
// there is no division or modulo on any coordinate in this file, and the
// power-of-two requirement is asserted in the constructor rather than trusted
// (the §6.2 phantom-terrain aliasing bug came from exactly that assumption
// being left to a comment).
//
// -------------------------------------------------------------------------
// BUILD TIER: C -- GRAVITY + DOWN-DIAGONALS + HORIZONTALS (complete)
// -------------------------------------------------------------------------
// §13: "Start gravity-only (no diagonals, no pooling); verify conservation;
// then add down-diagonals; then horizontals -- one tier at a time, re-running
// conservation after each." Tier A (gravity only) was green at 12 tests / 0
// failures before tier B was written; tier B (down-diagonals, §7.5 repose
// measured at 38.7 degrees) was green at 17 tests / 0 failures before this
// tier was written.
//
// EnabledTiers exists so tier A does not stop being provable once tier B lands:
// the gravity-only tests pin the tier they were written against and stay in the
// suite as regressions. It is also the knob §13's "if stuck 3 days" advice
// needs -- "the tier you just added is your suspect" is only actionable if the
// tier can be switched off (§0.1 invariant 5, one place to look).

using System;
using Unity.Mathematics;

/// Which levels of §7.4's Intent hierarchy are live. Tiers are cumulative in
/// the order §13's build steps require them, and a tier can be switched off to
/// isolate a regression to the tier that introduced it.
[System.Flags]
public enum FluidMotionTiers
{
    None = 0,
    /// §7.4 step 1: straight down.
    Gravity = 1 << 0,
    /// §7.4 step 2: the four down-diagonals, order-randomised.
    DownDiagonals = 1 << 1,
    /// §7.4 step 3: the four horizontals, order-randomised (pooling).
    Horizontals = 1 << 2,

    Full = Gravity | DownDiagonals | Horizontals,
}

/// A.5 FluidSlot, CPU mirror. §3.5: "A same-layout CPU mirror of the slot
/// struct backs FluidReferenceCPU, the Phase 5a correctness oracle."
/// Field names and meanings are A.5's verbatim; do not add fields without
/// adding them to A.5 first, or 5b's port stops being a translation.
public struct FluidSlotCPU
{
    /// Home brick within the sandbox. When the slot is FREE this holds the
    /// next free slot index instead (A.5: "next-free when free, intrusive list").
    public int brickDataIndex;
    /// 0..511 within the home brick (C.1).
    public ushort localVoxelOffset;
    /// Cached material, validated against the home byte every tick -- this
    /// comparison IS the orphan self-free check (§7.3).
    public byte materialID;
    public byte sleepCounter;
    public byte viscosityPhase;
    /// Appendix B: [0] Awake, [1] BidPlaced, [2] WonClaim, [3] ForceDemote.
    public byte stateFlags;
}

public sealed class FluidReferenceCPU : IFluidSampler
{
    // ---- Appendix B stateFlags bits ----
    private const byte FLAG_AWAKE       = 1 << 0;
    private const byte FLAG_BID_PLACED  = 1 << 1;
    private const byte FLAG_WON_CLAIM   = 1 << 2;
    private const byte FLAG_FORCE_DEMOTE = 1 << 3;
    /// One of Appendix B's reserved [7:4] bits, used only by this CPU
    /// reference. Marks "this slot got as far as evaluating destinations this
    /// tick", which distinguishes "tried and failed" (ages toward sleep) from
    /// "was viscosity-gated and never got a turn" (must not age, or Honey would
    /// sleep 30x faster than Water). The GPU port needs the same distinction;
    /// it is called out here so 5b does not have to rediscover it.
    private const byte FLAG_EVALUATED   = 1 << 4;
    /// Also one of Appendix B's reserved [7:4] bits. Set by Commit when the
    /// move that was applied actually went DOWN, as opposed to sideways.
    /// See the sleep rule in PostCommitSweep for why the distinction is needed.
    private const byte FLAG_DESCENDED   = 1 << 5;

    private const int NONE = -1;

    // ---- Sandbox geometry ----
    private readonly int _bricksX, _bricksY, _bricksZ;
    private readonly int _shiftBX, _shiftBY;    // log2 of _bricksX / _bricksY
    private readonly int _sizeX, _sizeY, _sizeZ;   // voxels
    private readonly int _voxelCount;

    /// THE sandbox array. Material truth lives here and nowhere else (§3.10 --
    /// the authoritative-byte rule holds inside the oracle too; slots are a
    /// pure motion overlay).
    private readonly byte[] _voxels;

    // ---- Claim plane (§7.3) ----
    private readonly int[] _claim;            // voxelIndex -> winning slot, or NONE
    private readonly int[] _claimedList;      // cells touched this tick, for an O(changes) Clear
    private int _claimedCount;

    // ---- Reaction reservation (§7.6) ----
    // Cells a reaction has spoken for this tick. They are excluded from being
    // legal claim destinations, so a reaction product and a move can never
    // land on the same cell.
    private readonly bool[] _reacted;
    private readonly int[] _reactedList;
    private int _reactedCount;
    private readonly int[] _reactionOpCell;      // pending writes, applied in Commit
    private readonly byte[] _reactionOpMaterial;
    private int _reactionOpCount;

    /// voxelIndex -> owning slot index, or NONE.
    ///
    /// WHY THIS EXISTS, given §7.3 says the orphan check needs no voxel->slot
    /// table: it is not used FOR the orphan check, which is still the byte
    /// comparison the spec specifies and is still what the orphan counter
    /// counts. It exists because this reference PROMOTES eagerly from its own
    /// edit path, so it must be able to answer "does this cell already have a
    /// slot?" without an O(slots) scan. That promotion-dedupe role is real and
    /// load-bearing: TryPromote is the only thing stopping a second slot being
    /// handed to a cell that already has one.
    ///
    /// WHY IT HOLDS THE OWNER AND NOT JUST A BIT. An earlier version of this
    /// comment said the owner was needed to close a live duplication hole in
    /// this implementation -- "slot A is orphaned out of cell C, another
    /// same-material slot B later moves INTO C, A's byte comparison matches
    /// again, two slots on one cell". THAT IS NOT TRUE OF THE CURRENT
    /// SINGLE-THREADED PATH, and the claim was never tested when it was
    /// written. The ownership guard in IntentPass that reads this field is
    /// DEFENCE-IN-DEPTH, not a live hazard: see that guard for the evidence.
    ///
    /// The owner is still worth storing for two reasons that ARE real:
    ///   - CountDuplicateSlotOwnership() needs it to detect the anomaly at all,
    ///     and that is what the regression tests assert on.
    ///   - A parallelised CPU reference (§7.3's optional brick-granularity
    ///     red-black decomposition) or the 5b GPU port would reintroduce the
    ///     ordering freedom the single-threaded path removes, and at that point
    ///     the owner is what makes the check possible rather than academic.
    private readonly int[] _slotAt;

    // ---- Slot pool (§3.5) ----
    private readonly FluidSlotCPU[] _slots;
    private readonly int _slotCapacity;
    private int _freeHead;
    private int _slotHighWater;     // slots [0.._slotHighWater) have ever been allocated
    private int _activeCount;

    // ---- Simulation state ----
    private int _tick;

    // ---- Per-tick counters (the debug overlay and the tests read these) ----
    private int _movesThisTick;
    private int _lateralMovesThisTick;
    private int _orphansFreedThisTick;
    private int _sleepFreedThisTick;
    private int _forceDemotedThisTick;
    private int _reactionsThisTick;
    private int _changedCellsThisTick;
    private long _orphansFreedTotal;
    private long _promotionsFailedTotal;
    private long _ownershipGuardFiredTotal;

    public FluidLedger Ledger { get; } = new FluidLedger();

    /// §7.4 Intent hierarchy levels in effect. Full by default; tests that pin
    /// an earlier build tier narrow it.
    public FluidMotionTiers EnabledTiers { get; set; } = FluidMotionTiers.Full;

    /// §7.4 near-player active scope. Defaults to EngineConfig's shipped value;
    /// a test that wants to exercise force-demotion narrows it explicitly.
    public int ActiveRadiusVoxels { get; set; } = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;
    public int3 PlayerVoxel { get; set; }

    /// Perturbs the ORDER in which equally-legal destinations are tried, without
    /// changing which destinations are legal.
    ///
    /// EXISTS FOR ONE EXPERIMENT: §7.8 says the GPU port's scheduling is not
    /// order-stable and must be compared on steady-state invariants, "never
    /// frame-exact positions". Whether the CPU-vs-GPU distribution gap is that
    /// sanctioned variance or a real translation bug is answerable by asking
    /// the ORACLE the same question -- if merely reordering its own tie-breaks
    /// moves its final distribution, then exact layout was never a property of
    /// the rules, only of one arbitrary ordering.
    ///
    /// Default 0 reproduces the shipped ordering exactly, so no existing test
    /// changes behaviour. §7.8's determinism guarantee still holds for any FIXED
    /// value of this.
    public int TieBreakSalt { get; set; }

    public int SizeXVoxels => _sizeX;
    public int SizeYVoxels => _sizeY;
    public int SizeZVoxels => _sizeZ;
    public int TickCount => _tick;
    public int ActiveSlotCount => _activeCount;
    public int SlotCapacity => _slotCapacity;

    public int MovesThisTick => _movesThisTick;
    /// Moves that went sideways rather than down. Split out because a basin
    /// that will not come to rest looks identical to a busy one on MovesThisTick
    /// alone, and the difference is the whole diagnosis.
    public int LateralMovesThisTick => _lateralMovesThisTick;
    public int OrphansFreedThisTick => _orphansFreedThisTick;
    public int SleepFreedThisTick => _sleepFreedThisTick;
    public int ForceDemotedThisTick => _forceDemotedThisTick;
    public int ReactionsThisTick => _reactionsThisTick;
    public long OrphansFreedTotal => _orphansFreedTotal;
    public long PromotionsFailedTotal => _promotionsFailedTotal;

    /// How many times Intent's ownership guard has actually caught a slot whose
    /// home cell is owned by someone else. Exposed so "this guard is
    /// load-bearing" can be a measurement instead of an argument in a comment.
    public long OwnershipGuardFiredTotal => _ownershipGuardFiredTotal;

    /// Live slots whose home cell is NOT recorded as theirs -- i.e. the
    /// duplicate-ownership anomaly itself, counted directly rather than
    /// inferred from a conservation delta. Two live slots sharing one home is
    /// the state that would commit two destinations from one source cell.
    /// O(slots); for tests and the debug overlay, not the tick.
    public int CountDuplicateSlotOwnership()
    {
        int bad = 0;
        for (int s = 0; s < _slotHighWater; s++)
        {
            if ((_slots[s].stateFlags & FLAG_AWAKE) == 0) continue;
            if (_slotAt[HomeVoxelIndex(in _slots[s])] != s) bad++;
        }
        return bad;
    }

    /// Number of sandbox cells whose material actually changed this tick.
    /// §13's settle criterion ("occupancy -> 0 within N ticks of rest") is this
    /// reaching zero: nothing moved, nothing reacted, the basin is at rest.
    ///
    /// KNOWN FALSE POSITIVE WITH SLOW-VISCOSITY FLUIDS -- READ BEFORE USING
    /// THIS AS A SETTLE TEST FOR LAVA OR HONEY. This counts CHANGE, not
    /// QUIESCENCE, and §7.4's viscosity intervals mean a material can be
    /// legitimately idle while still very much in motion: lava evaluates one
    /// tick in 6, so five of every six ticks report 0 here while a lava stream
    /// is visibly falling; honey is 29 of every 30. Observed directly in the
    /// Phase5aBasin harness, which printed "<at rest>" and a first-rest tick of
    /// 317 during a window in which a lava column was demonstrably still
    /// descending (a drop takes 30 x 6 = 180 ticks to fall the basin's height).
    ///
    /// Everything currently asserted against this is Water or Sand, both of
    /// which are tick-interval 1, so nothing in the suite is affected today.
    /// WHOEVER WRITES A LAVA OR HONEY SETTLE-TIME TEST: do not use a plain
    /// "N consecutive ticks with 0 changed cells" window. Either require the
    /// quiet window to exceed the slowest live material's TickInterval, or
    /// assert on ActiveSlotCount reaching 0 instead, which has no such blind
    /// spot because a viscosity-gated slot is still an allocated slot.
    /// DELIBERATELY NOT FIXED -- it affects nothing tested, and changing the
    /// metric now would change what the green tests mean.
    public int ChangedCellsThisTick => _changedCellsThisTick;

    /// <param name="slotCapacity">0 = size the pool to the sandbox (every cell
    /// could in principle be mobile), clamped to §0.2's MAX_ACTIVE_FLUID.</param>
    public FluidReferenceCPU(int bricksX, int bricksY, int bricksZ, int slotCapacity = 0)
    {
        RequirePowerOfTwo(bricksX, nameof(bricksX));
        RequirePowerOfTwo(bricksY, nameof(bricksY));
        RequirePowerOfTwo(bricksZ, nameof(bricksZ));

        _bricksX = bricksX; _bricksY = bricksY; _bricksZ = bricksZ;
        _shiftBX = Log2(bricksX);
        _shiftBY = Log2(bricksY);

        _sizeX = bricksX * EngineConfig.BRICK_EDGE;
        _sizeY = bricksY * EngineConfig.BRICK_EDGE;
        _sizeZ = bricksZ * EngineConfig.BRICK_EDGE;
        _voxelCount = bricksX * bricksY * bricksZ * EngineConfig.BRICK_BODY_BYTES;

        _voxels = new byte[_voxelCount];
        _claim = new int[_voxelCount];
        _claimedList = new int[_voxelCount];
        _reacted = new bool[_voxelCount];
        _reactedList = new int[_voxelCount];
        _reactionOpCell = new int[_voxelCount];
        _reactionOpMaterial = new byte[_voxelCount];
        _slotAt = new int[_voxelCount];
        for (int i = 0; i < _voxelCount; i++)
        {
            _claim[i] = NONE;
            _slotAt[i] = NONE;
        }

        _slotCapacity = slotCapacity > 0
            ? Math.Min(slotCapacity, EngineConfig.MAX_ACTIVE_FLUID)
            : Math.Min(_voxelCount, EngineConfig.MAX_ACTIVE_FLUID);
        _slots = new FluidSlotCPU[_slotCapacity];
        for (int i = 0; i < _slotCapacity; i++)
            _slots[i].brickDataIndex = i + 1 < _slotCapacity ? i + 1 : NONE;
        _freeHead = 0;

        PlayerVoxel = new int3(_sizeX >> 1, _sizeY >> 1, _sizeZ >> 1);
    }

    // =====================================================================
    // Addressing -- bitwise only (§0.1 invariant 2)
    // =====================================================================

    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)_sizeX && (uint)y < (uint)_sizeY && (uint)z < (uint)_sizeZ;

    public bool InBounds(int3 p) => InBounds(p.x, p.y, p.z);

    /// voxel coordinate -> flat sandbox index, in the engine's own
    /// (brickIndex << 9) | localVoxelIndex form (C.1).
    public int VoxelIndex(int x, int y, int z)
    {
        int brickIndex = ((z >> 3) << (_shiftBX + _shiftBY)) | ((y >> 3) << _shiftBX) | (x >> 3);
        int local = CoordMath.LocalVoxelIndex(CoordMath.LocalVoxelIndex3D(new int3(x, y, z)));
        return (brickIndex << 9) | local;
    }

    public int VoxelIndex(int3 p) => VoxelIndex(p.x, p.y, p.z);

    public int3 IndexToVoxel(int voxelIndex)
    {
        int brickIndex = voxelIndex >> 9;
        int local = voxelIndex & 511;
        int bx = brickIndex & (_bricksX - 1);
        int by = (brickIndex >> _shiftBX) & (_bricksY - 1);
        int bz = brickIndex >> (_shiftBX + _shiftBY);
        return new int3(
            (bx << 3) | (local & 7),
            (by << 3) | ((local >> 3) & 7),
            (bz << 3) | ((local >> 6) & 7));
    }

    // =====================================================================
    // Reads
    // =====================================================================

    public byte GetVoxel(int x, int y, int z) =>
        InBounds(x, y, z) ? _voxels[VoxelIndex(x, y, z)] : Materials.Air;

    public byte GetVoxel(int3 p) => GetVoxel(p.x, p.y, p.z);

    /// IFluidSampler (frozen Phase 0.5 interface). Returns the material only if
    /// it is a fluid, Air otherwise -- callers of this interface are asking
    /// "is there fluid here", not "what material is here" (use GetVoxel for that).
    public byte SampleFluid(int3 worldVoxelCoord)
    {
        byte m = GetVoxel(worldVoxelCoord);
        return MaterialRules.IsFluidMaterial(m) ? m : Materials.Air;
    }

    /// The OBSERVATION half of the conservation check: a full rescan, no
    /// incremental cleverness, so it cannot share a bug with the ledger it
    /// audits (§0.1 invariant 9).
    public int CountMobileBytes()
    {
        int n = 0;
        for (int i = 0; i < _voxelCount; i++)
            if (MaterialRules.IsMobile(_voxels[i])) n++;
        return n;
    }

    public int CountMaterial(byte material)
    {
        int n = 0;
        for (int i = 0; i < _voxelCount; i++)
            if (_voxels[i] == material) n++;
        return n;
    }

    public FluidLedgerCheck CheckConservation() => Ledger.Check(_tick, CountMobileBytes());

    /// Mobile voxels sitting with Air directly beneath them.
    ///
    /// At rest this should be ZERO: straight down is the first tier of §7.4's
    /// Intent hierarchy, so anything with air under it has a legal move and has
    /// no business being at rest. A non-zero count at rest means a drop stopped
    /// being asked to move while a move was still available -- i.e. it slept and
    /// nothing woke it. Added to chase the floating lava voxel seen in the
    /// Playground captures.
    public int CountFloatingMobile()
    {
        int n = 0;
        for (int i = 0; i < _voxelCount; i++)
        {
            if (!MaterialRules.IsMobile(_voxels[i])) continue;
            int3 c = IndexToVoxel(i);
            if (c.y <= 0) continue;
            if (_voxels[VoxelIndex(c.x, c.y - 1, c.z)] == Materials.Air) n++;
        }
        return n;
    }

    /// First floating mobile voxel found, for a failure message that names a
    /// coordinate instead of just a count.
    public bool TryFindFloatingMobile(out int3 found, out byte material)
    {
        for (int i = 0; i < _voxelCount; i++)
        {
            if (!MaterialRules.IsMobile(_voxels[i])) continue;
            int3 c = IndexToVoxel(i);
            if (c.y <= 0) continue;
            if (_voxels[VoxelIndex(c.x, c.y - 1, c.z)] != Materials.Air) continue;
            found = c; material = _voxels[i]; return true;
        }
        found = default; material = 0; return false;
    }

    // =====================================================================
    // Edits -- the sandbox's OWN private path
    // =====================================================================

    /// The sandbox's private edit entry point. This is deliberately NOT named
    /// SetVoxel and deliberately does NOT route through IEditService: §13 puts
    /// EditService's wake-scan hook in Phase 5b, and this file must not reach
    /// the shipped edit path. It exists so the basin harness and the tests can
    /// pour, mine and build, and it does locally what 5b's applied write-op
    /// list will do globally -- write the byte, then wake the neighbourhood.
    public void EditVoxel(int x, int y, int z, byte material)
    {
        if (!InBounds(x, y, z)) return;
        int idx = VoxelIndex(x, y, z);
        byte old = _voxels[idx];
        if (old == material) return;

        if (MaterialRules.IsMobile(old)) Ledger.RecordExternalRemove(1);
        if (MaterialRules.IsMobile(material)) Ledger.RecordExternalAdd(1);

        _voxels[idx] = material;

        // A slot sitting on this cell is now an orphan. It is NOT freed here:
        // §7.3 makes the slot free ITSELF on its next Intent by comparing its
        // cached material against the home byte, and that is the mechanism
        // Phase 5a's "mine a falling drop" acceptance test measures. Freeing it
        // eagerly here would make the test vacuous.

        if (MaterialRules.IsMobile(material)) TryPromote(idx);
        WakeNeighbourhood(idx);
    }

    public void EditVoxel(int3 p, byte material) => EditVoxel(p.x, p.y, p.z, material);

    /// Inclusive box fill, for building basins and columns.
    public void FillBox(int3 min, int3 max, byte material)
    {
        for (int z = min.z; z <= max.z; z++)
        for (int y = min.y; y <= max.y; y++)
        for (int x = min.x; x <= max.x; x++)
            EditVoxel(x, y, z, material);
    }

    /// Seed the sandbox without the ledger treating it as an edit -- used to
    /// build a scenario's static geometry, then baseline the ledger to what was
    /// actually placed.
    public void ResetLedgerToCurrentState() => Ledger.ResetTo(CountMobileBytes());

    // =====================================================================
    // Slot pool (§3.5, §7.7)
    // =====================================================================

    private int AllocSlot()
    {
        if (_freeHead == NONE)
        {
            // §7.7: "Underflow on promotion is a guarded no-op -- retry next
            // tick. No invalid write, ever." The byte stays in the array, so
            // the material is still there and still conserved; it simply does
            // not move until a slot frees up.
            _promotionsFailedTotal++;
            return NONE;
        }
        int s = _freeHead;
        _freeHead = _slots[s].brickDataIndex;
        if (s >= _slotHighWater) _slotHighWater = s + 1;
        _activeCount++;
        return s;
    }

    private void FreeSlot(int s)
    {
        ref FluidSlotCPU slot = ref _slots[s];
        int home = HomeVoxelIndex(in slot);
        if (_slotAt[home] == s) _slotAt[home] = NONE;

        slot.stateFlags = 0;              // clears Awake
        slot.materialID = Materials.Air;
        slot.sleepCounter = 0;
        slot.viscosityPhase = 0;
        slot.localVoxelOffset = 0;
        slot.brickDataIndex = _freeHead;  // A.5's intrusive free list
        _freeHead = s;
        _activeCount--;
    }

    private static int HomeVoxelIndex(in FluidSlotCPU slot) =>
        (slot.brickDataIndex << 9) | slot.localVoxelOffset;

    private static void SetHome(ref FluidSlotCPU slot, int voxelIndex)
    {
        slot.brickDataIndex = voxelIndex >> 9;
        slot.localVoxelOffset = (ushort)(voxelIndex & 511);
    }

    /// Promote a mobile byte to an active slot. Idempotent per cell.
    private int TryPromote(int voxelIndex)
    {
        if (_slotAt[voxelIndex] != NONE) return _slotAt[voxelIndex];
        byte m = _voxels[voxelIndex];
        if (!MaterialRules.IsMobile(m)) return NONE;

        int s = AllocSlot();
        if (s == NONE) return NONE;

        ref FluidSlotCPU slot = ref _slots[s];
        SetHome(ref slot, voxelIndex);
        slot.materialID = m;
        slot.sleepCounter = 0;
        slot.viscosityPhase = 0;
        slot.stateFlags = FLAG_AWAKE;
        _slotAt[voxelIndex] = s;
        return s;
    }

    /// Wakes only what could descend INTO a just-vacated cell: the cell
    /// directly above it, and the four cells that could reach it by a
    /// down-diagonal (§7.4 Intent tiers 1 and 2). Deliberately excludes the
    /// vacated cell's own lateral neighbours -- see the note at the call site.
    private void WakeAbove(int vacatedIndex)
    {
        int3 c = IndexToVoxel(vacatedIndex);
        int y = c.y + 1;
        WakeOne(c.x, y, c.z);
        WakeOne(c.x - 1, y, c.z);
        WakeOne(c.x + 1, y, c.z);
        WakeOne(c.x, y, c.z - 1);
        WakeOne(c.x, y, c.z + 1);
    }

    private void WakeOne(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return;
        int n = VoxelIndex(x, y, z);
        if (!MaterialRules.IsMobile(_voxels[n])) return;
        if (_slotAt[n] != NONE) return;
        TryPromote(n);
    }

    /// §7.6: "Any adjacent edit re-promotes: the edit path scans the edit's
    /// neighbourhood for fluid/falling materials and wakes GPU slots."
    /// Called after an edit AND after every committed move -- a committed move
    /// IS an edit as far as the neighbourhood is concerned, which is what stops
    /// a drop stalled above a draining column from sleeping through its turn.
    /// Full 3x3x3, because the Intent hierarchy includes diagonals.
    private void WakeNeighbourhood(int voxelIndex)
    {
        int3 c = IndexToVoxel(voxelIndex);
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int x = c.x + dx, y = c.y + dy, z = c.z + dz;
            if (!InBounds(x, y, z)) continue;
            int n = VoxelIndex(x, y, z);
            if (!MaterialRules.IsMobile(_voxels[n])) continue;
            // PROMOTE ONLY. An already-awake neighbour's sleep counter is
            // deliberately NOT reset here. Resetting it looks helpful and is
            // not: two adjacent drops shuffling along a pool surface would each
            // reset the other every tick and neither would ever sleep, so the
            // basin would never reach ChangedCellsThisTick == 0 and §13's
            // "occupancy -> 0 within N ticks of rest" could not be satisfied.
            // A slot that sleeps a tick too early costs nothing: the next
            // neighbouring move re-promotes it here with a fresh counter, which
            // is the same recovery path an edit uses.
            if (_slotAt[n] != NONE) continue;
            TryPromote(n);
        }
    }

    // =====================================================================
    // The tick: Clear -> Intent(+Claim) -> Commit (§7.3)
    // =====================================================================

    public void Tick()
    {
        _tick++;
        _movesThisTick = 0;
        _lateralMovesThisTick = 0;
        _orphansFreedThisTick = 0;
        _sleepFreedThisTick = 0;
        _forceDemotedThisTick = 0;
        _reactionsThisTick = 0;
        _changedCellsThisTick = 0;

        ClearPass();
        IntentPass();
        CommitPass();
        PostCommitSweep();
    }

    /// §7.3 Clear: "reset claim cells for active regions to a NONE sentinel."
    /// Only the cells actually written last tick are touched, which is what
    /// "for active regions" buys -- a full-array fill would be correct too but
    /// would make the tick cost proportional to the sandbox rather than to the
    /// activity in it.
    private void ClearPass()
    {
        for (int i = 0; i < _claimedCount; i++) _claim[_claimedList[i]] = NONE;
        _claimedCount = 0;

        for (int i = 0; i < _reactedCount; i++) _reacted[_reactedList[i]] = false;
        _reactedCount = 0;
        _reactionOpCount = 0;
    }

    private void IntentPass()
    {
        for (int s = 0; s < _slotHighWater; s++)
        {
            ref FluidSlotCPU slot = ref _slots[s];
            if ((slot.stateFlags & FLAG_AWAKE) == 0) continue;

            slot.stateFlags &= unchecked((byte)~(FLAG_BID_PLACED | FLAG_WON_CLAIM |
                                                 FLAG_EVALUATED | FLAG_DESCENDED));

            int home = HomeVoxelIndex(in slot);

            // ---- Orphan self-free (§7.3), BEFORE any movement ----
            // "read its home voxel byte; if it mismatches the cached material
            // (an edit or reaction changed it), free the slot and stop."
            if (_voxels[home] != slot.materialID)
            {
                FreeSlot(s);
                _orphansFreedThisTick++;
                _orphansFreedTotal++;
                // If the edit that orphaned this slot replaced the material
                // with ANOTHER mobile one (water -> lava), the cell still needs
                // a slot and nothing else will give it one: the wake scan at
                // edit time could not, because this now-freed slot still owned
                // the cell then. Detection is still the spec's byte comparison;
                // this is only the re-promotion that has to follow it.
                if (MaterialRules.IsMobile(_voxels[home])) TryPromote(home);
                continue;
            }

            // ---- Ownership guard: DEFENCE-IN-DEPTH, NOT A LIVE HAZARD ----
            //
            // THIS BRANCH IS UNREACHABLE IN THE CURRENT SINGLE-THREADED DESIGN.
            // It is kept for a parallelised CPU reference (§7.3's optional
            // brick-granularity red-black decomposition) or, conceivably, the
            // 5b GPU port -- both of which reintroduce the ordering freedom
            // that makes it reachable. It is not doing work today.
            //
            // EVIDENCE, not argument (2026-09-03). This block was replaced with
            //     if (_slotAt[home] != s) throw new InvalidOperationException(...)
            // and the FULL EditMode suite was run: 218 tests, 0 failures,
            // nothing thrown. Separately, with the block deleted outright the
            // same suite passed with CountDuplicateSlotOwnership() asserted 0 on
            // every tick, including a 600-tick randomised chaos test and a
            // 40-combination sweep of orphan/refill timings
            // (DuplicateOwnershipHunt_EveryOrphanRefillTiming). The claim is now
            // also checked automatically -- see the fixture's [TearDown], which
            // asserts OwnershipGuardFiredTotal == 0 after every single test, so
            // this comment cannot quietly go stale.
            //
            // WHY IT CANNOT FIRE HERE. For a live slot A homed at C to find
            // _slotAt[C] != A, some other slot must have taken C, and only two
            // places assign _slotAt:
            //   - CommitPass moving into C, which requires _voxels[C] == Air at
            //     claim time. But if C is Air while A is live caching a non-Air
            //     material, A's byte comparison above mismatches -- and Intent
            //     runs that comparison for EVERY live slot, EVERY tick, with
            //     nothing able to continue past it, STRICTLY BEFORE CommitPass.
            //     A is always freed first.
            //   - TryPromote(C), which requires _slotAt[C] == NONE. FreeSlot
            //     clears that entry only when the departing slot is the recorded
            //     owner, and CommitPass writes _slotAt[home]=NONE, _slotAt[d]=s
            //     and SetHome as consecutive statements BEFORE any
            //     WakeNeighbourhood call, so there is no window in which a live
            //     slot's home carries a NONE entry.
            // So §7.3's byte comparison is on its own sufficient here, exactly
            // as the spec says it is. Once ordering freedom exists, it is not.
            if (_slotAt[home] != s)
            {
                _ownershipGuardFiredTotal++;
                FreeSlot(s);
                _orphansFreedThisTick++;
                _orphansFreedTotal++;
                continue;
            }

            // A reaction already spoke for this cell earlier in this same pass.
            if (_reacted[home])
            {
                FreeSlot(s);
                continue;
            }

            // ---- §7.4 near-player scope / §7.7 forced demotion ----
            if (!WithinActiveRadius(home))
            {
                slot.stateFlags |= FLAG_FORCE_DEMOTE;
                FreeSlot(s);
                _forceDemotedThisTick++;
                continue;
            }

            // ---- §7.6 reactions, during the Intent neighbour scan ----
            if (TryReact(s, home, slot.materialID)) continue;

            // ---- §7.4 viscosity gate ----
            // Per-slot phase counter (A.5's viscosityPhase), not a global
            // modulo: it keeps the tick math free of `%` and matches the field
            // 5b will port.
            uint interval = MaterialRules.TickInterval(slot.materialID);
            slot.viscosityPhase++;
            if (slot.viscosityPhase < interval) continue;
            slot.viscosityPhase = 0;

            slot.stateFlags |= FLAG_EVALUATED;

            // ---- Destination selection: §7.4's Intent hierarchy, in order ----
            // "(1) straight down; (2) four down-diagonals, order-randomized;
            //  (3) four horizontals, order-randomized (pooling)."
            // The FIRST legal destination wins; a slot bids exactly once, which
            // is what makes §7.3's single-writer Commit argument hold.
            int3 c = IndexToVoxel(home);

            if ((EnabledTiers & FluidMotionTiers.Gravity) != 0 &&
                TryClaim(s, ref slot, c.x, c.y - 1, c.z)) continue;

            if ((EnabledTiers & FluidMotionTiers.DownDiagonals) != 0)
            {
                BuildLateralOrder(c.x, c.y, c.z, salt: 0);
                bool bid = false;
                for (int k = 0; k < 4 && !bid; k++)
                    bid = TryClaim(s, ref slot, c.x + _ordDX[k], c.y - 1, c.z + _ordDZ[k]);
                if (bid) continue;
            }

            // §7.4 step 3, pooling. Fluids only: §7.5 limits a falling solid's
            // Intent to "down + down-diagonals", which is exactly what gives it
            // an angle of repose instead of spreading flat like a liquid.
            if ((EnabledTiers & FluidMotionTiers.Horizontals) != 0 &&
                MaterialRules.IsFluidMaterial(slot.materialID))
            {
                BuildLateralOrder(c.x, c.y, c.z, salt: 1);
                bool bid = false;
                for (int k = 0; k < 4 && !bid; k++)
                    bid = TryClaim(s, ref slot, c.x + _ordDX[k], c.y, c.z + _ordDZ[k]);
                if (bid) continue;
            }
        }
    }

    /// §7.3 Claim, plain write. "If two sources target the same destination,
    /// both write the same 32-bit-aligned location -- a race, but a safe one."
    /// Single-threaded here, so the last writer in slot order wins; that is the
    /// same semantics the GPU gives, just with a defined winner.
    private bool TryClaim(int s, ref FluidSlotCPU slot, int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return false;
        int d = VoxelIndex(x, y, z);

        // THE conservation rule (§7.3): "a legal destination must have been Air
        // at tick-start." Nothing in Clear or Intent writes _voxels, so the
        // array still holds tick-start state at this point -- reactions defer
        // their writes to Commit precisely to keep that true.
        if (_voxels[d] != Materials.Air) return false;
        if (_reacted[d]) return false;

        if (_claim[d] == NONE) _claimedList[_claimedCount++] = d;
        _claim[d] = s;
        slot.stateFlags |= FLAG_BID_PLACED;
        return true;
    }

    /// §7.6: "Reactions during the Intent neighbour scan against Registry flag
    /// pairs." Face neighbours only. Returns true if this slot reacted (and was
    /// therefore freed).
    ///
    /// The WRITES are deferred to Commit rather than applied here, so that the
    /// array every other slot reads during Intent is still tick-start state --
    /// without that, a slot processed after a reaction could see a cell that
    /// "was Air at tick-start" in a state it was not, and Air-Only would stop
    /// meaning what §7.3 says it means.
    private bool TryReact(int s, int home, byte selfMaterial)
    {
        if (!MaterialRules.HasAnyReaction(selfMaterial)) return false;

        int3 c = IndexToVoxel(home);
        for (int f = 0; f < 6; f++)
        {
            int nx = c.x + FaceDX[f], ny = c.y + FaceDY[f], nz = c.z + FaceDZ[f];
            if (!InBounds(nx, ny, nz)) continue;
            int n = VoxelIndex(nx, ny, nz);
            if (_reacted[n]) continue;

            if (!MaterialRules.TryGetReaction(selfMaterial, _voxels[n],
                    out byte selfProduct, out byte otherProduct))
                continue;

            ReserveReaction(home, selfProduct, selfMaterial);
            ReserveReaction(n, otherProduct, _voxels[n]);
            _reactionsThisTick++;

            // This slot is gone now. Its counterpart -- if it has a slot at all
            // -- self-frees on its next Intent via the orphan check, because
            // Commit is about to change its home byte (§7.3). That is the
            // spec's mechanism; there is deliberately no reverse lookup here.
            FreeSlot(s);
            return true;
        }
        return false;
    }

    private void ReserveReaction(int cell, byte product, byte wasMaterial)
    {
        _reacted[cell] = true;
        _reactedList[_reactedCount++] = cell;
        _reactionOpCell[_reactionOpCount] = cell;
        _reactionOpMaterial[_reactionOpCount] = product;
        _reactionOpCount++;

        // Ledger: mobile material that becomes non-mobile is CONSUMED, not
        // lost. This is the only legal way the simulation itself may reduce the
        // mobile-byte count (§7.6).
        if (MaterialRules.IsMobile(wasMaterial) && !MaterialRules.IsMobile(product))
            Ledger.RecordReactionConsumed(1);
        else if (!MaterialRules.IsMobile(wasMaterial) && MaterialRules.IsMobile(product))
            Ledger.RecordReactionConsumed(-1);
    }

    /// §7.3 Commit, destination-driven: "a second pass dispatches over Air
    /// destination cells (not sources)."
    private void CommitPass()
    {
        for (int i = 0; i < _claimedCount; i++)
        {
            int d = _claimedList[i];
            int s = _claim[d];
            if (s == NONE) continue;

            ref FluidSlotCPU slot = ref _slots[s];
            if ((slot.stateFlags & FLAG_AWAKE) == 0) continue;   // freed by a reaction after bidding

            int home = HomeVoxelIndex(in slot);
            if (_voxels[home] != slot.materialID) continue;      // orphaned between Intent and Commit
            if (_slotAt[home] != s) continue;
            if (_voxels[d] != Materials.Air) continue;           // must still be the Air it claimed
            if (_reacted[d] || _reacted[home]) continue;

            // The destination is the only writer of both its own byte and the
            // winning source's vacated home byte (§7.3's single-writer argument).
            bool descended = IndexToVoxel(d).y < IndexToVoxel(home).y;

            _voxels[d] = slot.materialID;
            _voxels[home] = Materials.Air;
            _slotAt[home] = NONE;
            _slotAt[d] = s;
            SetHome(ref slot, d);
            slot.stateFlags |= FLAG_WON_CLAIM;
            if (descended) slot.stateFlags |= FLAG_DESCENDED;

            _movesThisTick++;
            if (!descended) _lateralMovesThisTick++;
            _changedCellsThisTick += 2;

            // The applied move is an edit as far as neighbours are concerned
            // (§7.6). In 5b this is the wake-scan the CPU runs while applying
            // the write-op list; here it is inline for the same reason.
            //
            // ONLY A DESCENDING MOVE WAKES. This is not an optimisation, it is
            // what makes a pool reach rest at all, and it was arrived at by
            // measurement, not by reasoning: with lateral moves waking too, a
            // partly-filled 4x4 basin surface was still committing 5-7 lateral
            // moves per tick at tick 2000 and never reached
            // ChangedCellsThisTick == 0. The cycle is exact -- a drop shuffles
            // sideways, ages, sleeps; a NEIGHBOUR's lateral shuffle re-promotes
            // it with a fresh counter; it shuffles again -- so the sleep
            // counter can never win and §13's "occupancy -> 0 within N ticks of
            // rest" is unreachable.
            //
            // A descending move genuinely changes the local fluid LEVEL: a cell
            // above was vacated or a cell below was filled, so the neighbours
            // have something new to evaluate. A lateral shuffle across a flat
            // surface changes no level, so there is nothing for a neighbour to
            // reconsider and propagating wakefulness from it only sustains the
            // shuffle. Spread still works: every drop that falls in, and every
            // drop that descends off a stack, hands its neighbourhood a fresh
            // lateral budget, and that is what carries a pour out to the walls.
            // ...BUT VACATING A CELL ALWAYS WAKES WHAT COULD FALL INTO IT.
            //
            // "Only a descending move wakes" is right about the DESTINATION
            // neighbourhood and wrong about the SOURCE. A lateral move still
            // empties the cell it left, and anything directly above that cell
            // now has a legal straight-down or down-diagonal move it did not
            // have a tick ago. With no wake at all on a lateral move, a drop
            // that had already slept above it never learns, and sits there with
            // Air underneath it forever.
            //
            // Measured, on a staircase floor in the oracle:
            //   Water_OnStaircase  1 voxel left floating at int3(3, 3, 22)
            //   Water_OnOverhang   1 voxel left floating at int3(5, 2, 16)
            // 5a and 5b only ever ran on FLAT floors, where a drop and the
            // cell below it drain together, so this never showed up.
            //
            // WakeAbove is deliberately much narrower than WakeNeighbourhood:
            // it touches only the five cells that can actually descend into the
            // vacated one (straight above plus the four down-diagonal sources).
            // It never wakes a lateral NEIGHBOUR at the same level, which is
            // what the shuffle-sustain cycle needed -- so this cannot
            // reintroduce the never-resting surface that "descending only"
            // was introduced to fix. PartiallyFilledSurface_StillReachesRest
            // is the test that pins that.
            WakeAbove(home);

            if (descended)
            {
                WakeNeighbourhood(home);
                WakeNeighbourhood(d);
            }
        }

        // Reaction products land last. They are disjoint from every committed
        // move by construction: a reserved cell was never a legal claim
        // destination, and a reacting slot never bid.
        for (int i = 0; i < _reactionOpCount; i++)
        {
            int cell = _reactionOpCell[i];
            byte product = _reactionOpMaterial[i];
            if (_voxels[cell] == product) continue;
            _voxels[cell] = product;
            if (_slotAt[cell] != NONE && !MaterialRules.IsMobile(product))
            {
                // Leave the slot in place; its own orphan check frees it next
                // tick. Only the ownership record is stale, and that is what
                // the orphan path repairs.
            }
            _changedCellsThisTick++;
            WakeNeighbourhood(cell);
        }
    }

    /// §7.6 sleep, plus §3.10's note that the CPU reference should do its
    /// bookkeeping as a post-commit single-threaded sweep rather than mid-tick.
    private void PostCommitSweep()
    {
        for (int s = 0; s < _slotHighWater; s++)
        {
            ref FluidSlotCPU slot = ref _slots[s];
            if ((slot.stateFlags & FLAG_AWAKE) == 0) continue;

            // Viscosity-gated this tick: it never got a turn, so it must not
            // age toward sleep.
            if ((slot.stateFlags & FLAG_EVALUATED) == 0) continue;

            // THE SLEEP RULE, AND THE ONE PLACE THIS FILE READS §7.6 RATHER
            // THAN QUOTES IT. §7.6 says "a no-move counter frees the slot at
            // threshold". Taken literally -- any move resets it -- a drop on a
            // partly-filled pool surface never sleeps: it always has an adjacent
            // Air cell at its own level, so it shuffles sideways forever, the
            // basin never reaches rest, and §13's "occupancy -> 0 within N ticks
            // of rest" becomes unsatisfiable for any pour that does not happen
            // to complete its top layer exactly.
            //
            // So the counter is reset by a move that made PROGRESS -- one that
            // went down -- and aged by one that did not. Pooling still works
            // because a spreading front's moves re-promote the cells around it
            // (WakeNeighbourhood), so lateral spread continues for as long as
            // anything nearby is actually moving and stops when nothing is.
            // Flagged as a reading rather than buried: 5b must port THIS rule,
            // not the literal sentence, or the GPU steady state will not match
            // this oracle's.
            if ((slot.stateFlags & (FLAG_WON_CLAIM | FLAG_DESCENDED)) ==
                (FLAG_WON_CLAIM | FLAG_DESCENDED))
            {
                slot.sleepCounter = 0;
                continue;
            }

            if (slot.sleepCounter < 255) slot.sleepCounter++;
            if (slot.sleepCounter >= EngineConfig.FLUID_SLEEP_TICKS)
            {
                // "nothing to write back, the byte already holds the material"
                // (§7.6) -- freeing a slot costs simulation, never mass.
                FreeSlot(s);
                _sleepFreedThisTick++;
            }
        }
    }

    private bool WithinActiveRadius(int voxelIndex)
    {
        int3 c = IndexToVoxel(voxelIndex);
        int3 d = c - PlayerVoxel;
        long r = ActiveRadiusVoxels;
        return (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z <= r * r;
    }

    // Order-randomisation scratch, allocated once (§0.1 invariant 3: no hidden
    // allocations on the hot path -- the reference is allowed to be slow, not
    // allowed to be a garbage generator, because 5b will be profiled against it).
    private readonly int[] _ordDX = new int[4];
    private readonly int[] _ordDZ = new int[4];

    // Face neighbours, for the reaction scan.
    private static readonly int[] FaceDX = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] FaceDY = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] FaceDZ = { 0, 0, 0, 0, 1, -1 };

    // =====================================================================
    // Helpers
    // =====================================================================

    /// Order-randomisation for the diagonal and horizontal tiers (§7.4:
    /// "order-randomized"). C.7 specifies "(X+Y+Z+frame) mod 2 + a per-frame
    /// hash"; this is that hash -- FNV-1a over the cell and the tick, with a
    /// final avalanche so neighbouring cells on the same tick do not get
    /// correlated orderings.
    ///
    /// It must stay a pure function of (cell, tick): §7.8 makes the CPU
    /// reference's determinism the reason it can be an oracle at all, so a
    /// System.Random with hidden state would disqualify it.
    private static uint Hash(int x, int y, int z, int tick, int salt)
    {
        uint h = 2166136261u;
        h = (h ^ (uint)x) * 16777619u;
        h = (h ^ (uint)y) * 16777619u;
        h = (h ^ (uint)z) * 16777619u;
        h = (h ^ (uint)tick) * 16777619u;
        h = (h ^ (uint)salt) * 16777619u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
        return h;
    }

    /// Fills _ordDX/_ordDZ with the four lateral directions (+X,-X,+Z,-Z) in a
    /// hash-derived order. Three bits pick which axis pair goes first and the
    /// sign order within each pair, so each of the four directions is tried
    /// FIRST with probability exactly 1/4 -- which is the property §13's
    /// "water drifts one direction consistently -> parity/tie-break bias"
    /// failure signature is about. A full 24-permutation shuffle would need a
    /// mod-3 and buys nothing over this (§0.1 invariant 9).
    /// <param name="salt">Distinguishes the diagonal tier's ordering from the
    /// horizontal tier's for the same cell on the same tick, so a cell that
    /// falls through to pooling does not inherit the preference it already
    /// tried and failed with one tier up.</param>
    private void BuildLateralOrder(int x, int y, int z, int salt)
    {
        uint h = Hash(x, y, z, _tick, salt + TieBreakSalt * 8191);
        bool zFirst = (h & 1u) != 0u;
        int px0 = (h & 2u) != 0u ? -1 : 1;
        int pz0 = (h & 4u) != 0u ? -1 : 1;

        if (zFirst)
        {
            _ordDX[0] = 0;    _ordDZ[0] = pz0;
            _ordDX[1] = 0;    _ordDZ[1] = -pz0;
            _ordDX[2] = px0;  _ordDZ[2] = 0;
            _ordDX[3] = -px0; _ordDZ[3] = 0;
        }
        else
        {
            _ordDX[0] = px0;  _ordDZ[0] = 0;
            _ordDX[1] = -px0; _ordDZ[1] = 0;
            _ordDX[2] = 0;    _ordDZ[2] = pz0;
            _ordDX[3] = 0;    _ordDZ[3] = -pz0;
        }
    }

    private static void RequirePowerOfTwo(int v, string name)
    {
        // §0.1 invariant 2 / the §6.2 phantom-terrain lesson: a non-power-of-two
        // does not fail loudly, it aliases silently. Assert instead of trusting.
        if (v <= 0 || (v & (v - 1)) != 0)
            throw new ArgumentException($"{name} must be a positive power of two (got {v}).", name);
    }

    private static int Log2(int powerOfTwo)
    {
        int n = 0;
        while ((powerOfTwo >> n) > 1) n++;
        return n;
    }
}
