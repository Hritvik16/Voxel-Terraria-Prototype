// Assets/CoreEngine/Memory/IFarQuery.cs
//
// Amendment 8.15 §7, row Q1 (task 104): the FAR QUERY, NEW beside the frozen
// IWorldQuery. IWorldQuery.GetVoxel keeps its contract (resident chunks only,
// i.e. NEAR; an unloaded chunk reads as air). GetVoxels answers ANY voxel,
// exactly as its chunk would load: the resident chunk if there is one, else
// the D.1 delta's brick if the chunk's delta holds it (then ops recorded after
// that save), else the generator with every op. Game rules may use it
// anywhere; they must not assume GetVoxel reaches further than NEAR.
// Main thread only.
using Unity.Collections;
using Unity.Mathematics;

public interface IFarQuery
{
    /// results[i] = the engine material at world voxel points[i] (0.1 m voxels),
    /// for i < points.Length. Returns when every answer is written.
    void GetVoxels(NativeArray<int3> points, NativeArray<byte> results);
}
