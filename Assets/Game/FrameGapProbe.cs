// ==========================================
// Assets/Game/FrameGapProbe.cs
//
// WHERE does a 1000ms frame actually spend its wall clock?
//
// This is the one question ten prior experiments never asked. Every instrument
// so far measured a PHASE OF OUR OWN WORK -- upload staging, mip rebuild,
// downsample, cascade writes -- and every one of them stayed under a
// millisecond while Time.unscaledDeltaTime recorded 1000-2900ms. Commit
// f702643 recorded the contradiction precisely: Unity's own
// cpuMainThreadFrameTime reports a healthy ~21ms for a frame whose wall clock
// is 1000ms+. So the missing ~979ms is not in any span anyone has timed. It is
// in the GAPS BETWEEN the spans.
//
// A Unity frame on the main thread is, in order:
//     [engine frame start: input, physics, coroutine resume, ...]
//     Update()          <- every MonoBehaviour
//     LateUpdate()      <- every MonoBehaviour
//     [render submission, culling, present]
//     WaitForEndOfFrame <- coroutine resumes here, after rendering
//
// Timestamping the boundaries splits the wall clock into three intervals whose
// sum is the frame:
//     preUpdate  = endOfFrame(N-1) -> Update(N)      engine frame start
//     update     = Update(N)       -> LateUpdate(N)  our Update work
//     postLate   = LateUpdate(N)   -> endOfFrame(N)  render submit + present
//
// Whichever interval swells on a stutter frame names the subsystem. This
// distinguishes hypotheses that all look identical from the outside:
//   preUpdate dominant  -> the main thread was not scheduled, or blocked in
//                          engine frame-start (which includes the GC
//                          stop-the-world suspend, since Boehm suspends
//                          threads at safepoints)
//   update dominant     -> our own code, contradicting every phase timer
//   postLate dominant   -> render-thread sync / present / driver
//
// READ-ONLY. Records timestamps and nothing else. Changes no contract, no
// allocation behaviour, no ordering. It allocates its lists once, up front.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

public class FrameGapProbe : MonoBehaviour
{
    private const int CAPACITY = 20000;

    private readonly List<double> _frameMs    = new List<double>(CAPACITY);
    private readonly List<double> _preUpdate  = new List<double>(CAPACITY);
    private readonly List<double> _update     = new List<double>(CAPACITY);
    private readonly List<double> _postLate   = new List<double>(CAPACITY);
    private readonly List<int>    _gc0        = new List<int>(CAPACITY);
    private readonly List<int>    _gc1        = new List<int>(CAPACITY);
    private readonly List<int>    _gc2        = new List<int>(CAPACITY);
    private readonly List<double> _heapMb     = new List<double>(CAPACITY);

    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private double _tEofPrev = -1, _tUpdate, _tLate;
    private bool _armed;

    /// Gate the probe so it only records during the legs we care about.
    public bool Recording { get; set; }
    public int SampleCount => _frameMs.Count;

    private double Now => _sw.Elapsed.TotalMilliseconds;

    void Start() { StartCoroutine(EndOfFrameLoop()); }

    void Update()
    {
        _tUpdate = Now;
        _armed = Recording && _tEofPrev >= 0;
    }

    void LateUpdate() { _tLate = Now; }

    private IEnumerator EndOfFrameLoop()
    {
        var eof = new WaitForEndOfFrame();
        while (true)
        {
            yield return eof;
            double tEof = Now;
            if (_armed && _frameMs.Count < CAPACITY)
            {
                _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
                _preUpdate.Add(_tUpdate - _tEofPrev);
                _update.Add(_tLate - _tUpdate);
                _postLate.Add(tEof - _tLate);
                _gc0.Add(GC.CollectionCount(0));
                _gc1.Add(GC.CollectionCount(1));
                _gc2.Add(GC.CollectionCount(2));
                _heapMb.Add(GC.GetTotalMemory(false) / 1048576.0);
            }
            _tEofPrev = tEof;
            _armed = false;
        }
    }

    /// Clears the series so each gate reports its own attribution rather than
    /// a blend of Gate C's traversal and Gate E's soak.
    public void Reset()
    {
        _frameMs.Clear(); _preUpdate.Clear(); _update.Clear(); _postLate.Clear();
        _gc0.Clear(); _gc1.Clear(); _gc2.Clear(); _heapMb.Clear();
    }

    private static double Pct(List<double> v, float p)
    {
        if (v.Count == 0) return -1;
        var c = new List<double>(v); c.Sort();
        return c[Mathf.Clamp((int)(c.Count * p), 0, c.Count - 1)];
    }

    /// Threshold matches the rig's existing stutter definition (>=100ms).
    public void AppendReport(StringBuilder sb, double stutterMs = 100.0)
    {
        sb.AppendLine("    --- FRAME WALL-CLOCK ATTRIBUTION (where the missing ms actually are) ---");
        if (_frameMs.Count == 0) { sb.AppendLine("      no samples"); return; }

        sb.AppendLine($"      {_frameMs.Count} frames. Intervals sum to the frame; whichever swells on a");
        sb.AppendLine("      stutter frame names the subsystem. preUpdate = engine frame start (includes");
        sb.AppendLine("      GC stop-the-world suspend and 'main thread not scheduled'); update = our");
        sb.AppendLine("      Update work; postLate = render submit + present.");
        sb.AppendLine($"      {"",-14}{"p50",9}{"p99",9}{"max",9}");
        void Row(string n, List<double> v) =>
            sb.AppendLine($"      {n,-14}{Pct(v,0.5f),9:F2}{Pct(v,0.99f),9:F2}{Pct(v,1.0f),9:F2}");
        Row("frame total", _frameMs);
        Row("preUpdate", _preUpdate);
        Row("update", _update);
        Row("postLate", _postLate);

        var stut = new List<int>();
        for (int i = 0; i < _frameMs.Count; i++) if (_frameMs[i] >= stutterMs) stut.Add(i);
        sb.AppendLine($"      stutter frames (>= {stutterMs:F0}ms): {stut.Count} of {_frameMs.Count}");
        if (stut.Count > 0)
        {
            double sPre = 0, sUpd = 0, sPost = 0, sTot = 0;
            foreach (int i in stut)
            { sPre += _preUpdate[i]; sUpd += _update[i]; sPost += _postLate[i]; sTot += _frameMs[i]; }
            sb.AppendLine($"      SHARE OF STUTTER WALL CLOCK:  preUpdate {sPre / sTot * 100,5:F1}%   " +
                          $"update {sUpd / sTot * 100,5:F1}%   postLate {sPost / sTot * 100,5:F1}%");
            sb.AppendLine($"      stutter means (ms):           preUpdate {sPre / stut.Count,8:F1}   " +
                          $"update {sUpd / stut.Count,8:F1}   postLate {sPost / stut.Count,8:F1}");

            int gc0 = 0, gc1 = 0, gc2 = 0;
            foreach (int i in stut)
                if (i > 0)
                { gc0 += _gc0[i] - _gc0[i-1]; gc1 += _gc1[i] - _gc1[i-1]; gc2 += _gc2[i] - _gc2[i-1]; }
            sb.AppendLine($"      GC collections DURING stutter frames: gen0 +{gc0}  gen1 +{gc1}  gen2 +{gc2}");
            sb.AppendLine("      (prior sessions instrumented gen0 ONLY; gen1/gen2 are new evidence, and the");
            sb.AppendLine("       256KB+32KB per-chunk worker arrays land in the large-object path.)");

            var worst = new List<int>(stut);
            worst.Sort((a, b) => _frameMs[b].CompareTo(_frameMs[a]));
            var line = new StringBuilder("      worst 8: ");
            for (int k = 0; k < Math.Min(8, worst.Count); k++)
            {
                int i = worst[k];
                line.Append($"{_frameMs[i]:F0}ms[pre {_preUpdate[i]:F0} upd {_update[i]:F0} post {_postLate[i]:F0}" +
                            $" gc2+{(i > 0 ? _gc2[i]-_gc2[i-1] : 0)} heap {_heapMb[i]:F0}MB]");
                if (k < Math.Min(8, worst.Count) - 1) line.Append(", ");
            }
            sb.AppendLine(line.ToString());
        }
        sb.AppendLine($"      managed heap: first {_heapMb[0]:F1}MB  last {_heapMb[_heapMb.Count-1]:F1}MB  " +
                      $"max {Pct(_heapMb,1.0f):F1}MB");
    }
}
