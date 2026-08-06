#ifndef STRUCT_HEADERS_INCLUDED
#define STRUCT_HEADERS_INCLUDED

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