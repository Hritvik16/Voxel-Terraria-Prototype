// Assets/Game/Playground.Grapple.cs
//
// Task 100 (GAMEPLAY_PHASE_PLAN.md step 7): the grappling hook in play. The
// physics is PlayerMotor's (FireHook / StepReel / StepSwing); this file only
// reads the keys and draws the rope.
//   F (or the middle mouse button) in WALK with movement abilities: fire at
//   the solid under the crosshair, up to hookRangeM (25 m); HOLD to reel in;
//   let go in the air to swing; Space lets go (and still jumps). In fly mode
//   (and on rigs, which have no abilities) F keeps its old toy, "go to the basin".
// The rope is a screen-space line from the right hand to the anchor; while
// not hooked, a ring on the crosshair says the hook would bite there.

using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    /// F / middle mouse belong to the hook (walk, abilities on).
    private bool HookKeysActive => _moveAbilities && _mode == MoveMode.Walk;

    private void ReadHookInput(ref MotorInput input, bool keys)
    {
        if (!keys || !HookKeysActive) return;
        input.HookPressed = Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(2);
        input.HookHeld = Input.GetKey(KeyCode.F) || Input.GetMouseButton(2);
    }

    // The aim probe, once per frame (OnGUI runs several times a frame).
    private int _hookAimFrame = -1;
    private bool _hookAimHit;
    private float _hookAimDist;

    private void DrawHookOverlay(Camera cam, float k)
    {
        if (!HookKeysActive || _player == null || _player.Motor == null || VitalsDead) return;
        var m = _player.Motor;
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        if (m.Hooked)
        {
            Vector3 anchor = new Vector3(m.HookAnchorM.x, m.HookAnchorM.y, m.HookAnchorM.z);
            Vector3 sa = cam.WorldToScreenPoint(anchor);
            Transform t = cam.transform;
            Vector3 hand = t.position + t.right * 0.22f - t.up * 0.28f + t.forward * 0.45f;
            Vector3 sh = cam.WorldToScreenPoint(hand);
            if (sa.z > 0f && sh.z > 0f)
            {
                var a = new Vector2(sh.x, Screen.height - sh.y);
                var b = new Vector2(sa.x, Screen.height - sa.y);
                DrawLine(a, b, new Color(0.08f, 0.05f, 0.02f, 0.8f), 5f * k);
                DrawLine(a, b, new Color(0.93f, 0.8f, 0.55f, 0.98f), 2.4f * k);
                float r = 6f * k;
                Color ring = m.HookArrived ? new Color(0.5f, 1f, 0.55f, 0.98f) : new Color(1f, 0.85f, 0.35f, 0.98f);
                for (int pass = 0; pass < 2; pass++)
                {
                    Color c = pass == 0 ? new Color(0f, 0f, 0f, 0.7f) : ring;
                    float w = pass == 0 ? 4f * k : 2f * k;
                    DrawLine(b + new Vector2(-r, 0f), b + new Vector2(0f, -r), c, w);
                    DrawLine(b + new Vector2(0f, -r), b + new Vector2(r, 0f), c, w);
                    DrawLine(b + new Vector2(r, 0f), b + new Vector2(0f, r), c, w);
                    DrawLine(b + new Vector2(0f, r), b + new Vector2(-r, 0f), c, w);
                }
            }
            return;
        }

        if (_hookAimFrame != Time.frameCount)
        {
            _hookAimFrame = Time.frameCount;
            Vector3 p = cam.transform.position, f = cam.transform.forward;
            _hookAimHit = m.RayHook(new float3(p.x, p.y, p.z), new float3(f.x, f.y, f.z), m.Config.hookRangeM,
                                    out _, out _, out _hookAimDist);
        }
        if (!_hookAimHit) return;
        // In range: a small diamond round the crosshair.
        float d = 19f * k;
        var o = new Vector2(cx, cy);
        for (int pass = 0; pass < 2; pass++)
        {
            Color c = pass == 0 ? new Color(0f, 0f, 0f, 0.6f) : new Color(1f, 0.85f, 0.35f, 0.9f);
            float w = pass == 0 ? 3.5f * k : 1.5f * k;
            DrawLine(o + new Vector2(-d, 0f), o + new Vector2(0f, -d), c, w);
            DrawLine(o + new Vector2(0f, -d), o + new Vector2(d, 0f), c, w);
            DrawLine(o + new Vector2(d, 0f), o + new Vector2(0f, d), c, w);
            DrawLine(o + new Vector2(0f, d), o + new Vector2(-d, 0f), c, w);
        }
    }

    /// The hook's line in the movement HUD ("" when nothing to say).
    private string HookState(PlayerMotor m)
    {
        if (!m.Hooked) return "";
        float dist = math.length(m.HookAnchorM - m.RopeAttachM);
        if (m.HookArrived) return $"<b>HOOKED</b> {dist:F1} m  hold <b>F</b> to hang, let go to swing, <b>Space</b> lets go";
        if (m.Reeling) return $"<b>REELING</b> {dist:F1} m to go  let go of <b>F</b> to swing";
        if (!m.Grounded) return $"<b>SWINGING</b> rope {m.RopeLengthM:F1} m  <b>WASD</b> pump  <b>F</b> reel  <b>Space</b> lets go";
        return $"<b>HOOKED</b> rope {m.RopeLengthM:F1} m  hold <b>F</b> to reel in  <b>Space</b> lets go";
    }
}
