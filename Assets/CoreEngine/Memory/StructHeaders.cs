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

        // §7.2 SPARSE ACTIVE SET (DESIGN_NOTE_7_2 §9). One bit per 32-voxel
        // sub-tile -- a 128^3 chunk is exactly 4x4x4 = 64 of them, which is why
        // it fits one word. Set means "there may be fluid-capable material
        // here"; clear means "there is definitely none".
        //
        // DERIVED STATE, NOT PERSISTED. It is rebuilt from the chunk's own
        // voxels on generation and on delta-apply, so the D.1 delta format is
        // untouched (it is a frozen API, §12) and an evicted chunk regenerates
        // its mask on reload rather than trusting a stored one.
        //
        // A.2 describes Chunk as a CPU resident-window entry: it is not uploaded
        // to the GPU and not serialised, so this addition is additive and
        // changes no layout anything else depends on.
        public ulong fluidTileMask;

        // Tiles whose SET bit may now be stale because a mobile voxel was
        // overwritten. Clearing exactly costs a brick scan, so SetVoxel records
        // the suspicion here and the rescan happens off the write path. Deferring
        // it is SAFE because over-reporting only costs a tile that is acquired,
        // found empty and freed; under-reporting is fluid that never wakes.
        public ulong fluidTileDirty;
    }

    // A.3 BrickHandle - CPU, 4B packed uint, 4096 per populated chunk
    [StructLayout(LayoutKind.Sequential)]
    public struct BrickHandle
    {
        public uint data;

        // [31]    1=dense (index in [29:0] into Brick Data Pool) | 0=uniform (material in [7:0])
        // [30]    Volatile: dense brick created by fluid entering uniform-air
    }

    // A.6 LightSource - GPU, flat point-light buffer (§3.8) - 8.1: replaces
    // v8's LightBrickRecord.
    //
    // APPENDIX A.6 VERBATIM, FIELD FOR FIELD, IN ORDER:
    //     float x, y, z;      // world position
    //     float r, g, b;      // color
    //     float range;        // cutoff radius
    //     float k;            // falloff constant (C.11)
    //
    // Eight floats, 32 B. NOTHING ELSE. No intensity, no enabled flag, no
    // padding, no reordering "for alignment" -- Appendix A's opening line says
    // C# mirrors use LayoutKind.Sequential with field order identical to HLSL,
    // and §0.3 lists inventing or reordering a struct field as the exact
    // failure mode header-first prompting exists to prevent. The HLSL twin is
    // in StructHeaders.hlsl and the two are checked field-for-field by
    // LightSourceLayoutTests.
    //
    // WHY POSITION IS FLOAT AND NOT AN INT3 VOXEL COORD, since every other
    // spatial thing in this engine is integer (§0.1 inv 2): this struct is not
    // coordinate math. It is consumed by C.11's attenuation, which is a float
    // distance calculation on already-resolved world positions -- there is no
    // addressing, no masking, and no shifting done with it, so the bitwise-
    // integer invariant does not apply and A.6 says float. The VOXEL a torch
    // occupies stays integer everywhere it matters (the edit path, the
    // registry key); this is the shading-side view of it.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct LightSource
    {
        public float x, y, z;
        public float r, g, b;
        public float range;
        public float k;

        /// 32 B. Named rather than computed at every call site so a caller
        /// cannot get the GraphicsBuffer stride wrong.
        public const int SizeBytes = 32;
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