// ==========================================
// Assets/CoreEngine/Streaming/ChunkLifecycle.cs
//
// Phase 4, file 2 of §13 Phase 4's ordered list: "the state transitions."
//
// §4.4 gives the machine as a TABLE, not as prose, and says so explicitly:
// "Explicit transition table (8.2 -- so the state logic is a contract, not
// prose)". This file is that table, transcribed literally, plus the
// forbidden-transition asserts §4.4 demands. It contains no policy: WHEN to
// evict, WHAT to prefetch, and HOW MANY chunks to admit per frame all live in
// StreamManager. This file only answers "is this transition legal, and were
// its preconditions met."
//
// That split is deliberate and load-bearing. §4.4's failure signature is
// "why did this chunk revert / double-save", and those bugs come from a
// transition happening in the wrong order, not from a bad eviction heuristic.
// Keeping the legality check in one tiny, fully-tested file means a violation
// names itself instead of surfacing three frames later as corrupted state.
//
// §0.3 lists "the streaming state machine (§4.4)" under "AI may NOT modify
// without explicit review". Treat the table below as frozen: it is a
// transcription, and any edit to it is an edit to the spec, not to code.
//
// ---------------------------------------------------------------------------
// WHY VIOLATIONS THROW RATHER THAN LOG
// ---------------------------------------------------------------------------
// §4.4: "These asserts are cheap and catch the entire class of 'why did this
// chunk revert / double-save' bugs at the source." A logged-and-ignored
// violation does not catch anything at the source -- it lets the drain
// continue with a chunk in a state the rest of the system has already
// concluded is impossible, which is precisely how the undiagnosable-bug class
// this rebuild exists to avoid gets started.
//
// Throwing kills the frame. That is the intended cost: StreamManager's drain
// is single-writer main-thread code (§3.2), so a throw there is loud,
// immediate, and lands with a stack trace pointing at the offending
// transition. The Phase 4 acceptance rig treats any thrown violation as a
// hard FAIL rather than catching it.
//
// NOTE ON §0.1 INVARIANT 11 ("memory-safety and save-integrity never
// degrade"): throwing does not risk save integrity. Deltas are written
// atomically via .tmp -> rename (§4.2, DeltaCodec.WriteAtomic), so an
// interrupted process leaves either the old file or the new one. There is no
// window in which a throw can produce a half-written save.
using System;
using Unity.Mathematics;

namespace VoxelEngine.Streaming
{
    /// A.1's `byte state` field, named. Values match A.1's stated encoding
    /// exactly (0 Unloaded, 1 Loading, 2 Resident, 3 Saving) so the enum can be
    /// cast to/from the serialized byte without a mapping table.
    public enum ChunkState : byte
    {
        Unloaded = 0,
        Loading  = 1,
        Resident = 2,
        Saving   = 3,
    }

    /// Appendix A.1 ChunkRecord -- CPU-only (§3.2). This is the Sparse Chunk
    /// Table's value type: "every chunk ever generated/edited", answering
    /// "is this resident, where?" and (via deltaByteLength>0) "ever edited?"
    ///
    /// A CLASS, not a struct, despite A.1 writing it as `struct`. A.1's structs
    /// are GPU-visible layouts; this one never crosses to the GPU (§3.2 calls it
    /// CPU-only), and the table is a Dictionary whose entries are mutated in
    /// place every frame. A value type there means every state change is a
    /// dictionary re-assignment, and a missed re-assignment is a silent no-op --
    /// the exact bug class the asserts below exist to catch, reintroduced by
    /// the storage choice. Field order and widths are otherwise A.1's.
    public class ChunkRecord
    {
        public const int NONE = -1;

        public int3 coord;
        public int residentSlot = NONE;   // index into resident window, or NONE
        public ChunkState state = ChunkState.Unloaded;
        public ushort generation;
        public uint lastTouchedFrame;     // LRU key (§3.6, §4.5)
        public uint deltaByteLength;      // 0 => pristine baseline (§4.1)
        public uint crc32;

        /// True iff this chunk has ever been edited, i.e. a .delta exists on
        /// disk for it. §4.1: "the ABSENCE of a delta file is itself
        /// information: that chunk is bit-exactly its baseline."
        public bool HasDelta => deltaByteLength > 0;
    }

    /// Why a transition was rejected. Reported so a violation names the rule it
    /// broke rather than just the state pair -- "Resident->Saving with
    /// deltaDirty==false" is a different bug from "Saving->Resident".
    public enum TransitionVerdict
    {
        Legal = 0,
        NotInTable,            // state pair appears nowhere in §4.4's table
        ForbiddenAbandonLoad,  // Loading -> Unloaded (§4.4 forbidden list)
        ForbiddenSaveInterrupt,// Saving -> Loading / Saving -> Resident (the Saving-eviction lock)
        PreconditionFailed,    // pair is in the table but its Condition column was not met
    }

    public static class ChunkLifecycle
    {
        // =====================================================================
        // §4.4's table, transcribed. Read this against the spec, not against
        // the code that calls it.
        //
        //   From      To         Trigger                        Condition
        //   --------  ---------  -----------------------------  ------------------------------
        //   Unloaded  Loading    streaming request (enter win)  free residency slot exists
        //   Loading   Resident   generation/decode completes    CRC passes (loaded) or gen done
        //   Resident  Saving     eviction selected              deltaDirty == true
        //   Resident  Unloaded   eviction selected              deltaDirty == false
        //   Saving    Unloaded   save completes                 atomic rename succeeded
        //
        // Forbidden (assert on violation):
        //   Loading -> Unloaded   "finish or fail explicitly, never abandon mid-load"
        //   Saving  -> Loading    the Saving-eviction lock
        //   Saving  -> Resident   "a chunk mid-save is immutable until the rename lands"
        //   ...and ANY transition not in the table above.
        // =====================================================================

        /// Pure predicate: is `from -> to` legal given `conditionMet`?
        /// No mutation, no logging, no throwing -- so tests can enumerate the
        /// full 4x4 state-pair matrix cheaply and assert the table exactly.
        /// StreamManager calls Transition() instead, which enforces.
        public static TransitionVerdict Classify(ChunkState from, ChunkState to, bool conditionMet)
        {
            // Self-transitions are not in the table. They are almost always a
            // double-drain of the same queue entry, which is a real bug worth
            // surfacing rather than quietly absorbing as a no-op.
            if (from == to) return TransitionVerdict.NotInTable;

            // ---- Explicitly forbidden pairs, named individually so the
            //      verdict distinguishes them (§4.4 forbidden list). ----
            if (from == ChunkState.Loading && to == ChunkState.Unloaded)
                return TransitionVerdict.ForbiddenAbandonLoad;
            if (from == ChunkState.Saving && (to == ChunkState.Loading || to == ChunkState.Resident))
                return TransitionVerdict.ForbiddenSaveInterrupt;

            // ---- The five legal pairs. ----
            switch (from)
            {
                case ChunkState.Unloaded:
                    if (to == ChunkState.Loading)
                        return conditionMet ? TransitionVerdict.Legal : TransitionVerdict.PreconditionFailed;
                    break;

                case ChunkState.Loading:
                    if (to == ChunkState.Resident)
                        return conditionMet ? TransitionVerdict.Legal : TransitionVerdict.PreconditionFailed;
                    break;

                case ChunkState.Resident:
                    // Both Resident exits are legal pairs; which one is correct
                    // is decided by deltaDirty at the CALL SITE, and passed in
                    // as conditionMet. Encoding "deltaDirty" here would put
                    // knowledge of Chunk's fields into the state machine and
                    // give the condition two homes.
                    if (to == ChunkState.Saving || to == ChunkState.Unloaded)
                        return conditionMet ? TransitionVerdict.Legal : TransitionVerdict.PreconditionFailed;
                    break;

                case ChunkState.Saving:
                    if (to == ChunkState.Unloaded)
                        return conditionMet ? TransitionVerdict.Legal : TransitionVerdict.PreconditionFailed;
                    break;
            }

            return TransitionVerdict.NotInTable;
        }

        /// Enforcing form. Applies the transition on success; throws on any
        /// violation (see the file header for why throwing rather than logging).
        ///
        /// `conditionMet` is the Condition column for this specific pair, and
        /// the caller must supply the RIGHT one:
        ///   Unloaded->Loading   free residency slot exists
        ///   Loading ->Resident  CRC passed (loaded) or generation completed
        ///   Resident->Saving    record's chunk has deltaDirty == true
        ///   Resident->Unloaded  record's chunk has deltaDirty == false
        ///   Saving  ->Unloaded  atomic rename succeeded
        public static void Transition(ChunkRecord record, ChunkState to, bool conditionMet, string context = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            ChunkState from = record.state;
            TransitionVerdict verdict = Classify(from, to, conditionMet);

            if (verdict != TransitionVerdict.Legal)
                throw new InvalidOperationException(BuildViolationMessage(record, from, to, conditionMet, verdict, context));

            record.state = to;

            // `generation` (A.1) increments on every full residency cycle, i.e.
            // when a chunk returns to Unloaded. Its purpose is to invalidate
            // in-flight async work: a generation job that completes AFTER its
            // chunk was evicted carries a stale generation and its result is
            // dropped rather than inserted. Without this, a chunk that leaves
            // and re-enters the window fast enough can be overwritten by the
            // completion of its own PREVIOUS load -- an ordering bug that
            // reproduces only at speed, which is the worst kind to hunt.
            // Deliberately allowed to wrap: ushort gives 65,536 cycles between
            // collisions, and a stale job surviving that many evictions of the
            // same chunk is not a scenario the queue can produce.
            if (to == ChunkState.Unloaded) unchecked { record.generation++; }
        }

        /// Non-throwing form for paths that must not take down the frame (the
        /// acceptance rig's own probing, chiefly). Returns the verdict and
        /// applies the transition only when Legal.
        public static bool TryTransition(ChunkRecord record, ChunkState to, bool conditionMet, out TransitionVerdict verdict)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            verdict = Classify(record.state, to, conditionMet);
            if (verdict != TransitionVerdict.Legal) return false;

            record.state = to;
            if (to == ChunkState.Unloaded) unchecked { record.generation++; }
            return true;
        }

        private static string BuildViolationMessage(
            ChunkRecord record, ChunkState from, ChunkState to,
            bool conditionMet, TransitionVerdict verdict, string context)
        {
            string why = verdict switch
            {
                TransitionVerdict.ForbiddenAbandonLoad =>
                    "§4.4 forbids Loading->Unloaded: 'finish or fail explicitly, never abandon mid-load'. " +
                    "A load in flight must be allowed to complete (or be explicitly failed into Resident) " +
                    "before the chunk can leave; abandoning it orphans whatever the worker is holding.",

                TransitionVerdict.ForbiddenSaveInterrupt =>
                    "§4.4 forbids Saving->Loading and Saving->Resident (the Saving-eviction lock): " +
                    "'a chunk mid-save is immutable until the rename lands'. Re-admitting a chunk whose " +
                    "delta write is still in flight is how an edit gets written twice or lost entirely.",

                TransitionVerdict.NotInTable =>
                    "this state pair appears nowhere in §4.4's transition table, and §4.4 forbids " +
                    "'any transition not in the table above'.",

                TransitionVerdict.PreconditionFailed =>
                    "the pair is legal but its Condition column was not satisfied. Check the caller is " +
                    "passing the condition for THIS pair (free slot / CRC-or-gen-done / deltaDirty / " +
                    "rename-succeeded), not a different one.",

                _ => "unclassified violation.",
            };

            return $"[ChunkLifecycle] ILLEGAL TRANSITION {from} -> {to} " +
                   $"for chunk {record.coord} (slot={record.residentSlot}, gen={record.generation}, " +
                   $"deltaBytes={record.deltaByteLength}, conditionMet={conditionMet}). " +
                   $"Verdict={verdict}. {why}" +
                   (string.IsNullOrEmpty(context) ? "" : $" Context: {context}");
        }
    }
}