// Assets/CoreEngine/Simulation/FluidTickScheduler.cs
//
// Engine step 17 (task 91, E6, amendment 8.14 row 5 "fluid tick contract"):
// THE FLUID CA TICKS AT A FIXED RATE, independent of frame rate. This
// supersedes Phase 5's decision 11 (one tick whenever the pipeline allows,
// which measured ~6-7 frames per tick, so fluid speed followed FPS:
// FLUID_TICK_DIAGNOSIS.md section 1, 4.6 ticks/s at 33 FPS, 9.6 at 59).
//
// This is option C of FLUID_TICK_DIAGNOSIS.md section 3, and only C:
//   * a tick is DUE every 1/R seconds of wall-clock time (phase-locked to a
//     deadline, so frame quantisation does not lose rate: at 30 and 60 FPS the
//     average is R exactly, not 1/ceil(frames per period));
//   * a due tick is issued only when the pipeline allows it (`canIssue`, i.e.
//     FluidOpListReadback.CanIssue -- one op-list in flight, MaxFramesInFlight
//     is NOT raised);
//   * at most ONE tick is ever banked: if the frame rate is too low to deliver
//     R, whole missed periods are DROPPED and counted (MissedTicks), never
//     replayed as a burst. From the first call, T seconds issue at most
//     R*T + 1 ticks; any T-second window fewer than R*T + 2 (one banked tick).
// Lava keeps its own ratio: MaterialRules.TickInterval (lava every 6th CA
// tick) is unchanged, so lava moves at R / 6 per second, also FPS-independent.
//
// Pure arithmetic over a caller-supplied clock so EditMode can pin it with
// fake 30/60 FPS clocks (FluidTickSchedulerTests); the caller passes
// Time.unscaledTimeAsDouble.

using System;

namespace VoxelEngine.Simulation
{
    public sealed class FluidTickScheduler
    {
        /// R, in CA ticks per second. 4.5 Hz = today's pipeline capacity at the
        /// diagnosis's worst measured case (33 FPS: 4.6 ticks/s), so 30+ FPS
        /// delivers it. With option B (adaptive drain) it could rise to ~6.5,
        /// with A+B to ~8 (FLUID_TICK_DIAGNOSIS.md section 3).
        public const float DefaultRateHz = 4.5f;

        /// Ticks/s; <= 0 = LEGACY (pre-task-91 decision 11: issue whenever
        /// canIssue, fluid speed follows frame rate). Kept as a comparison arm
        /// only (`-fluidhz 0`).
        public float RateHz { get; }
        public bool Legacy => RateHz <= 0f;
        public double PeriodSeconds => Legacy ? 0.0 : 1.0 / RateHz;

        private double _nextDue = double.NaN;

        /// Ticks this scheduler allowed.
        public long IssuedTicks { get; private set; }
        /// SHORTFALL: due ticks dropped because the pipeline could not deliver
        /// R (the frame rate was too low, or a hitch). Fluid slowed by this many.
        public long MissedTicks { get; private set; }
        /// Frames on which a tick was due but canIssue was false.
        public long BlockedFrames { get; private set; }

        public FluidTickScheduler(float rateHz = DefaultRateHz)
        {
            if (float.IsNaN(rateHz) || float.IsInfinity(rateHz))
                throw new ArgumentOutOfRangeException(nameof(rateHz));
            RateHz = rateHz;
        }

        /// Call once per frame. True = run the CA tick and issue its readback
        /// now. Never true while `canIssue` is false.
        public bool ShouldIssue(double now, bool canIssue)
        {
            if (Legacy)
            {
                if (!canIssue) return false;
                IssuedTicks++;
                return true;
            }

            double period = 1.0 / RateHz;
            if (double.IsNaN(_nextDue)) _nextDue = now;      // first call: due at once

            // More than one whole period late: drop the extra due ticks (count
            // them) so at most one is banked. Never replayed as a burst.
            double late = now - _nextDue;
            if (late >= period)
            {
                long drop = (long)Math.Floor(late / period);
                MissedTicks += drop;
                _nextDue += drop * period;
            }

            if (now < _nextDue) return false;
            if (!canIssue) { BlockedFrames++; return false; }

            _nextDue += period;
            IssuedTicks++;
            return true;
        }
    }
}
