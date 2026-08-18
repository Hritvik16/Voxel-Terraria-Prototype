using UnityEngine;

// Amendment 8.7/8.8 — standalone toggle for the three-way traversal A/B/C
// test, kept separate from RaymarchDebugControls. Attach to any active
// GameObject in the Phase 2 bootstrap scene.
//
// Press R to cycle RaymarchFeature.TraversalMode: 0 (original LeapSpan) ->
// 1 (LeapSpanReseed) -> 2 (LeapSpanReseed + chaining) -> back to 0. Logs the
// new state and mode name so it's unambiguous which path a screenshot/
// capture was taken under — pair with DebugOut[11] (mode) and DebugOut[12]
// (total chained leaps, mode 2 only) if you need to confirm from the GPU
// debug-pixel readout, not just this log line.
public class RaymarchTraversalModeToggleDebug : MonoBehaviour
{
    [Tooltip("Key that cycles RaymarchFeature.TraversalMode through 0/1/2.")]
    public KeyCode toggleKey = KeyCode.R;

    private static readonly string[] ModeNames = { "0 (LeapSpan)", "1 (LeapSpanReseed)", "2 (LeapSpanReseed+Chaining)" };

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            RaymarchFeature.TraversalMode = (RaymarchFeature.TraversalMode + 1) % 3;
            Debug.Log($"[TraversalModeToggle] TraversalMode = {ModeNames[RaymarchFeature.TraversalMode]}");
        }
    }
}