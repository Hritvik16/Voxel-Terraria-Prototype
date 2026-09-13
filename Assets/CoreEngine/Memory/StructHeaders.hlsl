#ifndef STRUCT_HEADERS_INCLUDED
#define STRUCT_HEADERS_INCLUDED

// A.6 LightSource - GPU, flat point-light buffer (§3.8)
// Field order is Appendix A.6's, verbatim, and must stay identical to
// StructHeaders.cs's LightSource. LightSourceLayoutTests parses BOTH and fails
// if they drift -- the same guard PaletteSyncTests puts on the palette, and for
// the same reason: a silent CPU/GPU layout mismatch reads as "lighting is
// subtly wrong" for a month.
struct LightSource
{
    float x, y, z;      // world position
    float r, g, b;      // color
    float range;        // cutoff radius
    float k;            // falloff constant (C.11)
};

// A.7 MaterialData - GPU, 32B
struct MaterialData 
{
    float albedoR;
    float albedoG;
    float albedoB;
    float emissive;
    float viscosityDrag;
    float density;
    uint flags;
    uint tickInterval;
};

// A.8 Terrain Clipmap entry - 4B per brick slot
// [31] 1=uniform (material in [7:0]) | 0=dense (index into GPU Brick Data in [30:0])

#endif