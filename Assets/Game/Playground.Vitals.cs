// Assets/Game/Playground.Vitals.cs
//
// Task 93d1 (GAMEPLAY_PHASE_PLAN.md step 9a): health, fall damage, oxygen,
// lava/acid damage and a SOFT death for the interactive Survival walk.
// PlayerVitals (CoreEngine/Gameplay) holds the rules; this file ticks it after
// each motor step, runs the death screen and the respawn, and draws the bars.
//
// ON ONLY WHERE A PLAYER IS: an interactive run (_moveAbilities, i.e. the
// play-settings runs), in Survival, walking. Creative, fly mode, rigs and
// capture runs take no damage, so nothing they measure changes (plan §2,
// guardrail 5). Tuning: PlayerConfig.json (maxHealth, fall*, oxygen*, *DamagePerS,
// regen*, respawnDelayS), live-reloaded like the movement numbers.
//
// SOFT DEATH (P22 "soft = respawn, keep everything"): at 0 HP the body stops,
// the screen says why, and after respawnDelayS it goes back to the play start
// (task 93c1's R path) with full health and air. The inventory is not touched.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private PlayerVitals _vitals;
    /// The walk mode last frame: a switch INTO walk (Tab from fly, a respawn,
    /// the start pose, KEEP=1's resume) lands its first fall for free.
    private bool _vitalsWasWalking;
    private bool _vitalsSafeLanding;

    /// Off with -vitals 0 and in the older demos (their shots stay as they were). Set in Start.
    private bool _vitalsAllowed = true;

    private bool VitalsOn => _vitalsAllowed && _moveAbilities && SurvivalMode && _player != null && _player.Motor != null;
    private bool VitalsDead => _vitals != null && _vitals.Dead;

    /// After the motor step and buoyancy (DriveWalk): what the body touches
    /// now, and the landing the motor just reported.
    private void TickVitals(float dt)
    {
        if (!VitalsOn) { _vitalsWasWalking = false; return; }
        var c = PlayerConfig.Active;
        if (_vitals == null) _vitals = new PlayerVitals(c);
        var m = _player.Motor;
        if (!_vitalsWasWalking) _vitalsSafeLanding = true;
        _vitalsWasWalking = true;

        var input = PlayerVitals.Sense(_store, _store, m.PositionM, c.bodyHeightM, m.LastImpactDownMps);
        if (_vitalsSafeLanding && input.ImpactDownMps > 0f) { _vitalsSafeLanding = false; input.ImpactDownMps = 0f; }
        _vitals.Step(dt, input, c);
        if (_vitals.Dead && _vitals.DeadForS == 0f)
        {
            _status = $"you died: {DeathCause()} -- back to the start in {c.respawnDelayS:F0} s (you keep everything)";
            Debug.Log($"[Vitals] died ({_vitals.LastCause}) at {m.PositionM.x:F1},{m.PositionM.y:F1},{m.PositionM.z:F1} m, death {_vitals.Deaths}");
        }
    }

    /// While dead: the screen counts down in any mode, then the soft respawn.
    /// The body and the mouse stay still meanwhile (Update skips them).
    private void TickDeath(float dt)
    {
        if (_mode != MoveMode.Walk) _vitalsWasWalking = false;
        if (!VitalsDead) return;
        var c = PlayerConfig.Active;
        _vitals.Step(dt, default(VitalsInput), c);
        if (!_vitals.RespawnDue(c)) return;
        _vitals.Respawn(c);
        _vitalsWasWalking = false;
        CancelStroke();
        if (Phase4Bootstrapper.PlayStartActive) StartCoroutine(RespawnAtPlayStart());
        else Respawn();
        Debug.Log($"[Vitals] respawned ({(Phase4Bootstrapper.PlayStartActive ? "play start" : "surface under the death")})");
    }

    private string DeathCause()
    {
        switch (_vitals.LastCause)
        {
            case DamageCause.Fall: return $"fell {_vitals.LastFallHeightM:F0} m";
            case DamageCause.Drowning: return "drowned";
            case DamageCause.Lava: return "burned in lava";
            case DamageCause.Acid: return "dissolved in acid";
            default: return "died";
        }
    }

    /// The bars: health on the left above the hotbar, air on the right while
    /// it is not full; a red flash on damage; the death screen.
    private void DrawVitalsHud(float vw, float vh)
    {
        if (_vitals == null || !SurvivalMode || !_moveAbilities) return;
        var c = PlayerConfig.Active;

        if (_vitals.LastHitAgeS < 0.45f && !_vitals.Dead)
            Box(new Rect(0, 0, vw, vh), new Color(0.85f, 0.05f, 0.02f, 0.28f * (1f - _vitals.LastHitAgeS / 0.45f)));

        if (_vitals.Dead)
        {
            Box(new Rect(0, 0, vw, vh), new Color(0.25f, 0f, 0f, 0.6f));
            float left = math.max(0f, c.respawnDelayS - _vitals.DeadForS);
            GUI.Label(new Rect(0, vh * 0.5f - 60f, vw, 50f), "<b>YOU DIED</b>", Label(40, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(0, vh * 0.5f - 6f, vw, 26f), DeathCause(), Label(18, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(0, vh * 0.5f + 22f, vw, 24f),
                      $"back to the start in {math.ceil(left):F0} s  --  you keep everything", Label(14, TextAnchor.MiddleCenter));
        }
        if (_mode != MoveMode.Walk && !_vitals.Dead) return;

        // On the hotbar caption's row ("SURVIVAL  F6 switches to Creative",
        // centred): health at its left end, air at its right end.
        const float slot = 58f, pad = 6f, h = 14f, w = 150f;
        float total = Inventory.HotbarSize * (slot + pad) - pad;
        float x0 = (vw - total) * 0.5f;
        float y = vh - slot - 58f - 18f;

        float hp = c.maxHealth > 0f ? Mathf.Clamp01(_vitals.Health / c.maxHealth) : 0f;
        Box(new Rect(x0 - 1, y - 1, w + 2, h + 2), new Color(0f, 0f, 0f, 0.6f));
        Color hpCol = hp > 0.5f ? new Color(0.85f, 0.18f, 0.16f, 0.95f)
                    : hp > 0.25f ? new Color(0.95f, 0.45f, 0.1f, 0.95f) : new Color(1f, 0.15f, 0.1f, 0.95f);
        Box(new Rect(x0, y, w * hp, h), hpCol);
        GUI.Label(new Rect(x0 + 6, y - 2, w, h + 4),
                  $"<b>HEALTH</b>  {math.ceil(_vitals.Health):F0} / {c.maxHealth:F0}", Label(11, TextAnchor.MiddleLeft));

        bool showAir = _vitals.OxygenS < c.oxygenSeconds - 1e-3f;
        if (showAir)
        {
            float air = c.oxygenSeconds > 0f ? Mathf.Clamp01(_vitals.OxygenS / c.oxygenSeconds) : 0f;
            float xa = x0 + total - w;
            Box(new Rect(xa - 1, y - 1, w + 2, h + 2), new Color(0f, 0f, 0f, 0.6f));
            Box(new Rect(xa, y, w * air, h), air > 0f ? new Color(0.35f, 0.7f, 1f, 0.95f) : new Color(0.2f, 0.3f, 0.5f, 0.9f));
            GUI.Label(new Rect(xa + 6, y - 2, w, h + 4),
                      air > 0f ? $"<b>AIR</b>  {math.ceil(_vitals.OxygenS):F0} s" : "<b>AIR</b>  drowning!",
                      Label(11, TextAnchor.MiddleLeft));
        }
    }
}
