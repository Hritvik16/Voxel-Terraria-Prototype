using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class Phase2Bootstrapper : MonoBehaviour
{
    public static TerrainClipmap Clipmap { get; private set; }

    // Exposed for diagnostic tooling (RaymarchDebugProbe) so it can run
    // RaymarchReference.TracerRaycast against the same live ChunkStore the
    // shader is reading via the clipmap - the CPU sibling comparison per
    // §0.1/§0.3's Sibling Pattern. Not a behavioral change to Phase 2 itself.
    public static ChunkStore Store { get; private set; }

    [Header("Camera spawn (for benchmarking / captures)")]
    [Tooltip("If true, the camera is moved to the pose below on play. " +
             "Untick to keep whatever position the camera has in the scene " +
             "(e.g. one you set by hand for a specific GPU capture).")]
    [SerializeField] private bool _overrideCameraOnStart = true;

    [Tooltip("World-space camera position applied on play when the override " +
             "is enabled. For the air-walk worst-case StepHeat capture, set a " +
             "high top-down position (e.g. 52, 84, 52).")]
    [SerializeField] private Vector3 _cameraSpawnPosition = new Vector3(52.0f, 84.0f, 52.0f);

    [Tooltip("Euler angles (degrees) applied on play when the override is " +
             "enabled. (90, 0, 0) looks straight down; (0,0,0) looks forward.")]
    [SerializeField] private Vector3 _cameraSpawnEuler = new Vector3(90.0f, 0.0f, 0.0f);

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    void Start()
    {
        // Both the pool capacity and the clipmap's window size now come from
        // EngineConfig - the single source of truth per §0.2/§4.3 - instead of
        // separate literals here that could (and did) silently disagree with
        // what ChunkStore itself uses.
        _pool = new BrickDataPool(EngineConfig.BRICK_POOL_CAP);
        _allocator = new ChunkHandleAllocator(100);
        _store = new ChunkStore(_pool, _allocator);
        Store = _store;

        int3 windowChunks = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        Clipmap = new TerrainClipmap(windowChunks, _pool.Capacity);

        for (int cz = 0; cz < 8; cz++)
            for (int cx = 0; cx < 8; cx++)
            {
                int3 coord = new int3(cx, 0, cz);
                Chunk chunk = new Chunk();
                ChunkGenerator.GenerateChunk(42, coord, ref chunk, _allocator, _pool);
                _store.InsertChunk(chunk);
                Clipmap.MarkDirty(coord);
            }

        Clipmap.UploadDirty(_store, _pool);

        // Phase 2 Acceptance: Carve a 3x3x3 pocket securely underground
        // int3 pocketCenter = new int3(520, 10, 520); // World position (52.0, 1.0, 52.0)
        // for(int x = -1; x <= 1; x++)
        // for(int y = -1; y <= 1; y++)
        // for(int z = -1; z <= 1; z++)
        // {
        //     _store.SetVoxel(pocketCenter + new int3(x, y, z), 0); // Air
        // }

        // Clipmap.MarkDirty(CoordMath.VoxelToChunk(pocketCenter));
        // Clipmap.UploadDirty(_store, _pool);

        ClipmapValidator.ValidateRegion(Clipmap, _pool, _store);

        // Amendment 8.7 Step 3: verify the GPU air-mip pyramid exactly matches a
        // fresh CPU rebuild. Full-buffer compare (every cell, every level), so a
        // toroidal-wrap or level-dim bug shows here. Nothing on the GPU READS the
        // mip yet (shader unchanged until Step 4), so Beauty must be unchanged;
        // this line only certifies the buffers are correct before Step 4 trusts
        // them.
        AirMipValidator.ValidateAll(Clipmap, _store);

        // Camera spawn is now Inspector-driven (see fields above) instead of a
        // hardcoded (52,1,50). The old position placed the camera at Y=1,
        // embedded in solid stone AND at the cheapest possible raymarch view
        // (almost no air to traverse) - useless for the air-walk cost capture,
        // which needs a HIGH top-down view where air-brick traversal dominates.
        // Default is now (52,84,52) looking down; untick _overrideCameraOnStart
        // to keep a hand-placed camera for a specific capture.
        if (_overrideCameraOnStart && Camera.main != null)
        {
            Camera.main.transform.position = _cameraSpawnPosition;
            Camera.main.transform.rotation = Quaternion.Euler(_cameraSpawnEuler);
        }
    }

    void OnDestroy()
    {
        Clipmap?.Dispose();
        _pool?.Dispose();
    }
}