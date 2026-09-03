// Assets/Game/Phase5bClaimStress.cs
//
// Drives FluidClaimStress.compute and reports, on screen and in the log,
// whether any surviving claim value was garbage -- i.e. not one of the
// candidates that actually contended for that cell.
//
// WHAT A PASS HERE DOES AND DOES NOT MEAN. A pass says: on THIS Mac, THIS
// Unity, THIS Metal driver, a contended plain 32-bit store never produced a
// torn or foreign value across the run. It is evidence, not a proof, and §7.3
// asks for exactly that evidence rather than trusting the stated guarantee.
// A single garbage value is a hard stop for the whole claim design.
using System.Text;
using UnityEngine;

public class Phase5bClaimStress : MonoBehaviour
{
    [SerializeField] private ComputeShader _stress;
    [Tooltip("Contested destination cells.")]
    [SerializeField] private int _destCount = 4096;
    [Tooltip("Contending sources per cell. §7.3 says 'hundreds'.")]
    [SerializeField] private int _sourcesPerDest = 512;
    [Tooltip("Repeat passes. Races are timing-dependent; one pass proves little.")]
    [SerializeField] private int _passes = 200;

    private string _report = "running...";
    private bool _done;

    void Start()
    {
        if (_stress == null) { _report = "FluidClaimStress.compute not assigned"; _done = true; return; }

        int kSeed = _stress.FindKernel("CSSeed");
        int kFlood = _stress.FindKernel("CSFlood");

        var claims = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _destCount, 4);
        var readback = new int[_destCount];

        long garbage = 0, untouched = 0, totalChecked = 0;
        int worstPass = -1;
        var distinct = new System.Collections.Generic.HashSet<int>();

        for (int pass = 0; pass < _passes; pass++)
        {
            _stress.SetInt("_DestCount", _destCount);
            _stress.SetInt("_SourcesPerDest", _sourcesPerDest);
            _stress.SetInt("_Pass", pass);

            _stress.SetBuffer(kSeed, "Claims", claims);
            _stress.Dispatch(kSeed, (_destCount + 63) / 64, 1, 1);

            _stress.SetBuffer(kFlood, "Claims", claims);
            int threads = _destCount * _sourcesPerDest;
            _stress.Dispatch(kFlood, (threads + 63) / 64, 1, 1);

            claims.GetData(readback);

            for (int d = 0; d < _destCount; d++)
            {
                int v = readback[d];
                totalChecked++;
                if (v == -1) { untouched++; continue; }
                long lo = (long)d * _sourcesPerDest;
                long hi = lo + _sourcesPerDest;
                // THE ASSERTION: the survivor must be one of the candidates that
                // contended for THIS cell. Anything else is a torn or foreign
                // word -- the failure §7.3 says cannot happen.
                if (v < lo || v >= hi) { garbage++; if (worstPass < 0) worstPass = pass; }
                else if (distinct.Count < 64) distinct.Add((int)(v - lo));
            }
        }

        claims.Dispose();

        var sb = new StringBuilder();
        sb.AppendLine("Phase 5b — plain-write claim stress (§7.3 Metal verification)");
        sb.AppendLine($"device        {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}");
        sb.AppendLine($"passes        {_passes}");
        sb.AppendLine($"cells         {_destCount} contested, {_sourcesPerDest} contending sources each");
        sb.AppendLine($"claims checked {totalChecked:N0}");
        sb.AppendLine($"untouched     {untouched:N0}   (should be 0 — every cell had writers)");
        sb.AppendLine($"GARBAGE       {garbage:N0}   (MUST be 0)");
        sb.AppendLine($"distinct winners sampled {distinct.Count} (>1 means the race is real, not serialised)");
        sb.AppendLine(garbage == 0
            ? "RESULT: PASS — every surviving claim was one of that cell's own candidates."
            : $"RESULT: FAIL — first garbage on pass {worstPass}. The plain-write claim is NOT safe here; STOP and report this.");
        _report = sb.ToString();
        _done = true;
        Debug.Log("[ClaimStress]\n" + _report);
    }

    void OnGUI()
    {
        GUI.Label(new Rect(16, 16, 900, 400),
            _done ? _report : "Phase 5b claim stress — running, this takes a few seconds...",
            new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true });
    }
}
