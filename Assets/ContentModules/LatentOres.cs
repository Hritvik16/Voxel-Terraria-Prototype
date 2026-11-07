// Assets/ContentModules/LatentOres.cs
//
// Task 181 (owner R2 (a), brief "New ores after progress"): LATENT ORES. The
// generator (TerrainGen.OreRules, `-ores 1`) writes cobalt and emberstone as a
// TWIN material per host rock. While the world stage (world.state, task 101) is
// below the ore's unlock stage, a twin is its HOST everywhere content looks:
// colour (MaterialPalette, and the shader's own tables + _WorldStage), hardness
// and pick tier (Tools), the dropped item and its name (Items). From the unlock
// stage on, it is the ORE. The stored voxel byte never changes: no ground is
// rewritten, the save does not grow, generation stays a pure function of the seed.
//
// ContentModules does not reference TerrainGen, so the codes are written out
// (as Tools / Items do) and pinned by LatentOreTests against OreRules.TWINS.
// THREADING: SetStage is called on the main thread (world load, a stage flip);
// readers are main-thread content code.
public static class LatentOres
{
    // (twin GenMat code, ore GenMat code, host GenMat code), OreRules.TWINS' order
    private static readonly byte[] TWINS = {
        26, 68, 34,   // cobalt in shale (forest cavern rock)
        27, 68, 43,   // cobalt in maroon mudstone (desert)
        28, 68, 52,   // cobalt in basalt (snow)
        29, 68, 61,   // cobalt in black basalt (jungle)
        38, 68, 37,   // cobalt in gneiss (savanna)
        39, 68, 49,   // cobalt in plum shale (badlands)
        94, 69, 72,   // emberstone in ash
        95, 69, 73,   // emberstone in obsidian
        59, 69, 74,   // emberstone in magma crust
    };
    public const byte CobaltCode = 68, EmberstoneCode = 69;
    public const byte CobaltStage = 1, EmberstoneStage = 2;

    /// The world stage content resolves against (0 until row S1 flips it).
    public static byte Stage { get; private set; }

    private static readonly byte[] _resolve = new byte[256];
    private static readonly byte[] _ore = new byte[256];    // engine id of the twin's ore, 0 = not a twin
    private static readonly byte[] _host = new byte[256];   // engine id of the twin's host rock

    static LatentOres()
    {
        for (int i = 0; i < TWINS.Length; i += 3)
        {
            byte t = Materials.FromTg2(TWINS[i]);
            _ore[t] = Materials.FromTg2(TWINS[i + 1]);
            _host[t] = Materials.FromTg2(TWINS[i + 2]);
        }
        Rebuild();
    }

    public static int TwinCount => TWINS.Length / 3;
    public static byte TwinCode(int i) => TWINS[i * 3];
    public static byte OreCode(int i) => TWINS[i * 3 + 1];
    public static byte HostCode(int i) => TWINS[i * 3 + 2];

    public static bool IsTwin(byte material) => _ore[material] != 0;
    public static byte UnlockStage(byte oreEngine) =>
        oreEngine == Materials.FromTg2(CobaltCode) ? CobaltStage : oreEngine == Materials.FromTg2(EmberstoneCode) ? EmberstoneStage : (byte)0;

    /// The material content treats `material` as at the current stage.
    public static byte Resolve(byte material) => _resolve[material];

    /// Sets the stage (world load: StreamManager.World.Stage) and the shader's
    /// _WorldStage. Returns true when any twin changed what it resolves to.
    public static bool SetStage(byte stage)
    {
        bool changed = stage != Stage;
        Stage = stage;
        Rebuild();
        UnityEngine.Shader.SetGlobalInt("_WorldStage", stage);
        return changed;
    }

    private static void Rebuild()
    {
        for (int m = 0; m < 256; m++)
        {
            byte ore = _ore[m];
            _resolve[m] = ore == 0 ? (byte)m : Stage >= UnlockStage(ore) ? ore : _host[m];
        }
    }
}
