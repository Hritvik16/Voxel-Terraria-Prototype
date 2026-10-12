// Assets/CoreEngine/Memory/IEditOps.cs
//
// Amendment 8.14 row 8 (task 81): the far-edit API, NEW beside the frozen
// IEditService. IEditService.SetVoxel keeps its contract (refused outside
// loaded chunks); an edit anywhere is recorded here as a D.4 op instead and
// applied when its area generates. Main thread only.
using VoxelEngine.Streaming;

public interface IEditOps
{
    /// Records `op` (its seq is assigned and returned) and applies it at once to
    /// the chunks that are resident; the rest get it when they generate.
    uint Record(EditOp op);
}
