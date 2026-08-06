using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    // A.2 Chunk - CPU resident-window entry
    public class Chunk
    {
        public int3 coord;
        public bool isUniform;
        public byte uniformMaterial;
        
        // Inlined, allocated iff populated (backed by pooled allocator)
        public BrickHandle[] bricks; 
        
        public bool dirty;             // changed since last clipmap upload
        public bool deltaDirty;        // edited since last save
    }

    // A.3 BrickHandle - CPU, 4B packed uint, 4096 per populated chunk
    [StructLayout(LayoutKind.Sequential)]
    public struct BrickHandle
    {
        public uint data;

        // [31]    1=dense (index in [29:0] into Brick Data Pool) | 0=uniform (material in [7:0])
        // [30]    Volatile: dense brick created by fluid entering uniform-air
    }

    // A.7 MaterialData - GPU, 32B x 256
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct MaterialData
    {
        public float albedoR;
        public float albedoG;
        public float albedoB;
        public float emissive;
        public float viscosityDrag;
        public float density;
        public uint flags;
        public uint tickInterval;
    }
}