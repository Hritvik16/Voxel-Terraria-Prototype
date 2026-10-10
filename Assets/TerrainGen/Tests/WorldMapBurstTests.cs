// Assets/TerrainGen/Tests/WorldMapBurstTests.cs
//
// ENGINE PLAN STEP 1 (owner Q26 (a), 2026-10-09): ONE compiled world-map
// generator path. The map is built by a Burst job (MapJob.cs) in the Editor
// and in the IL2CPP player alike, so the map these tests, PlayMetricsReport
// and the review renders read IS the map the player draws.
//
// Evidence, not assumption: the player prints "map v4 Height FNV <hash>" in its
// tg2 report (./run-tg2world.sh, the line after "seed 42: world 8 km");
// PLAYER_V4_SEED42 is that hash, copied from the log named beside it. When the
// generator changes on purpose, rebuild the player (BUILD=1 ./run-tg2world.sh),
// read the new hash from its log and re-pin it here -- never from the Editor.
using System;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class WorldMapBurstTests
    {
        /// seed 42, map v4: the IL2CPP player's WorldMap.HeightHash(), from
        /// Logs/tg2world_20261010_005419.txt (BUILD=1 ./run-tg2world.sh, task 45):
        /// "map v4 Height FNV 639552DD962C4B28  cell(613,259) 1068.80900".
        public const ulong PLAYER_V4_SEED42 = 0x639552DD962C4B28UL;

        [Test]
        public void Seed42V4_EditorMapIsThePlayersMap()
        {
            var map = SharedMaps.Get(TerrainSpec.SizeClass2(42u));
            ulong h = map.HeightHash();
            Debug.Log($"[Engine01] EDITOR map v{map.Spec.mapVersion} Height FNV {h:X16}  cell(613,259) {map.Height[259 * map.Spec.mapCells + 613]:F5}");
            Assert.AreEqual(PLAYER_V4_SEED42.ToString("X16"), h.ToString("X16"),
                "the Editor's seed 42 v4 map differs from the IL2CPP player's (WorldMap.HeightHash): two generator paths again?");
        }

        /// The one path is Burst's: a run that reports it executed as managed
        /// code (Burst off) must throw, never hand back a map.
        [Test]
        public unsafe void ManagedRun_Throws()
        {
            var saved = MapJobs.Runner;
            try
            {
                MapJobs.Runner = io => { ((GenIO*)io)->ranManaged = 1; MapJobs.Execute((GenIO*)io); };
                Assert.Throws<InvalidOperationException>(() => Offsets.For(42u), "a managed generator run must throw");
                MapJobs.Runner = null;
                Assert.Throws<InvalidOperationException>(() => Offsets.For(42u), "no registered runner must throw");
            }
            finally { MapJobs.Runner = saved; }
        }

        /// Burst self-consistency: a second build of the same spec is the same
        /// map and record, byte for byte.
        [Test]
        public void Seed42V4_SecondBuildIsIdentical()
        {
            var a = SharedMaps.Get(TerrainSpec.SizeClass2(42u));
            using var b = WorldMap.Build(TerrainSpec.SizeClass2(42u));
            Debug.Log($"[Engine01] map build arena peak {MapJobs.LastPeakBytes / (1 << 20)} MB of {MapArena.DEFAULT_BYTES >> 20} MB reserved");
            Assert.AreEqual(a.HeightHash().ToString("X16"), b.HeightHash().ToString("X16"), "Height");
            Assert.AreEqual(DangerTests.MapHash(a).ToString("X16"), DangerTests.MapHash(b).ToString("X16"), "map planes");
            Assert.AreEqual(a.Record.Hash().ToString("X16"), b.Record.Hash().ToString("X16"), "record");
        }
    }
}
