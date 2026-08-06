// Assets/CoreEngine/Simulation/IFluidSampler.cs
using Unity.Mathematics;

public interface IFluidSampler
{
    byte SampleFluid(int3 worldVoxelCoord);
}