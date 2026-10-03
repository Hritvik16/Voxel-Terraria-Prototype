// Assets/TerrainGen/Tests/ReportOnly.cs
//
// Report-only tests (they write numbers or pictures and assert nothing) are
// opt-in (task 11, 2026-10-02), like PlayMetricsReport's PLAYMETRICS=1:
//   REPORTS=1 ./run-editmode-tests.sh                  every report
//   REPORTS=1 tools/run-editmode-filter.sh <name>      one of them
// Without it they are Ignored, so they count as SKIP and still keep their
// place (and full name) in TestResults.xml.
using System;
using NUnit.Framework;

namespace TerrainGen.Tests
{
    public static class ReportOnly
    {
        public static void Require()
        {
            if (Environment.GetEnvironmentVariable("REPORTS") != "1")
                Assert.Ignore("report only: set REPORTS=1");
        }
    }
}
