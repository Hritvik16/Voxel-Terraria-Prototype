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

        // NOTE: left unchanged from the original (52.0, 1.0, 50.0) - out of scope
        // for this fix. Known issue carried forward: this position is 2m in Z
        // from the only carved air at this height (the pocket is at Z=52), so
        // the camera spawns embedded in solid stone here unless manually moved,
        // as seen earlier in testing. Not touched here since the requested fix
        // was window-dims only; flagging so it isn't mistaken for resolved.
        if (Camera.main != null)
        {
            Camera.main.transform.position = new Vector3(52.0f, 1.0f, 50.0f);
            Camera.main.transform.rotation = Quaternion.identity;
        }
    }

    void OnDestroy()
    {
        Clipmap?.Dispose();
        _pool?.Dispose();
    }
}