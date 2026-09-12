// Assets/CoreEngine/Simulation/OpDrainCursor.cs
//
// The carry-forward arithmetic behind §8.5's frame budget on the fluid apply,
// pulled out as a pure struct so EditMode can pin it.
//
// WHY IT IS SEPARATE. The apply path itself cannot be unit tested -- it needs
// a real FluidGpuSimulation, which needs a ComputeShader, which the batchmode
// test runner does not have. But the part that can silently DROP or DUPLICATE
// an op is not the GPU: it is this index arithmetic. "Never drop an op" is the
// whole contract of carrying work forward, and a half-open-range off-by-one
// would violate it while every existing test stayed green.
//
// The guarantees this type exists to make testable:
//   * every staged op is yielded EXACTLY ONCE across any sequence of Takes
//   * ops are yielded in strictly increasing index order, never reordered --
//     two writes to the same voxel must resolve the way the CA intended
//   * a new batch can only be staged once the previous one is fully drained,
//     which is what bounds the buffer to one batch

using System;

namespace VoxelEngine.Simulation
{
    /// A half-open cursor [Head, Count) over one staged batch of ops.
    public struct OpDrainCursor
    {
        private int _head;
        private int _count;

        /// Ops staged but not yet applied.
        public int Remaining => _count - _head;
        public bool IsEmpty => Remaining == 0;
        public int Head => _head;
        public int Count => _count;

        /// Stages a batch of `count` ops.
        ///
        /// Refuses while ops are still pending: the buffer holds exactly one
        /// batch, and overwriting a partly-drained batch is precisely how ops
        /// would go missing.
        public void Stage(int count)
        {
            if (!IsEmpty)
                throw new InvalidOperationException(
                    $"cannot stage over {Remaining} undrained ops -- they would be lost");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            _head = 0;
            _count = count;
        }

        /// Claims up to `budget` ops, returning the half-open range [from, to)
        /// to apply now. Returns false when there is nothing left.
        ///
        /// The cursor advances by exactly what it hands out, so the next call
        /// resumes where this one stopped -- that is the carry-forward.
        public bool Take(int budget, out int from, out int to)
        {
            from = _head;
            to = _head;
            if (budget <= 0 || IsEmpty) return false;

            to = _head + Math.Min(budget, Remaining);
            _head = to;
            // Fully drained: reset so the next Stage is accepted.
            if (_head >= _count) { _head = 0; _count = 0; }
            return true;
        }

        /// True when this Take did not finish the batch, i.e. the budget bit.
        public bool WouldCarry(int budget) => !IsEmpty && budget < Remaining;
    }
}
