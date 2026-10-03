// Assets/TerrainGen/Tests/SharedMaps.cs
//
// One world map per spec per test run (task 11, 2026-10-02). A sizeClass 2 map
// takes ~27 s to build and a full run used to build seed 42 nine times. Every
// fixture that only READS a map takes it from here; the cache is keyed on the
// whole TerrainSpec (not just the seed), so a spec changed in any field gets
// its own build.
//
// Rules:
//   - Never Dispose a shared map (the cache owns it for the whole domain).
//   - Never write into its arrays. The determinism tests compare a FRESH
//     WorldMap.Build against the shared one, so a test that mutated it would
//     show up there as a determinism failure.
//   - A test that must prove two builds agree still calls WorldMap.Build
//     itself; the shared map is only ever ONE of the two sides.
using System.Collections.Generic;

namespace TerrainGen.Tests
{
    public static class SharedMaps
    {
        public sealed class Entry
        {
            public TerrainSpec Spec;
            public WorldMap Map;
            public Erosion.Stats ErosionStats;   // WorldMap.LastErosion right after this build
            public double BuildMs;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static WorldMap Get(TerrainSpec spec) => GetEntry(spec).Map;

        public static Entry GetEntry(TerrainSpec spec)
        {
            foreach (var e in _entries)
                if (e.Spec.Equals(spec)) return e;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var map = WorldMap.Build(spec);
            var entry = new Entry { Spec = spec, Map = map, ErosionStats = WorldMap.LastErosion, BuildMs = sw.Elapsed.TotalMilliseconds };
            _entries.Add(entry);
            return entry;
        }
    }
}
