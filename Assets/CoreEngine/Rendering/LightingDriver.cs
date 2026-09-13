// ==========================================
// Assets/CoreEngine/Rendering/LightingDriver.cs
//
// PHASE 7. The one component that makes lighting LIVE, so no scene has to
// remember three separate calls.
//
// Per frame it does exactly three things, and nothing else:
//   1. LightingConfig.ReloadIfChanged()  -- §8.1's hot-reload loop, so editing
//      the JSON changes the next frame with no recompile and no restart.
//   2. SunCycle.Advance(dt)              -- §6.5's "two animated uniforms from
//      the in-game clock", unless the scrubber has paused it.
//   3. LightSourceBuffer.Upload(player)  -- §3.9's CPU -> GPU light-sources row,
//      which is where §3.8's furthest-first selection actually happens.
//
// WHY THIS EXISTS RATHER THAN THREE CALLS IN EACH SCENE. There are 27 run-*.sh
// rigs in this project and several scene builders. A lighting path that only
// works in scenes whose author remembered to call Upload() is a path that
// works until someone builds a new rig, at which point torches silently stop
// lighting anything and the rig reports a clean pass. One component, added
// once per scene, is the version that cannot be half-wired.
//
// IT DOES NOT OWN THE WORLD. It takes the EditService and the player transform
// from whoever built them; it never creates a ChunkStore, never writes terrain,
// and never decides what a light is. §6.6's decision ("is this material
// emissive?") lives in MaterialRules and the enqueue lives in EditService --
// this only pumps the result across to the GPU. That keeps §0.1 invariant 1's
// direction of travel obvious: lights are a CONSEQUENCE of terrain edits, never
// a parallel authority over them.
//
// UPLOAD COST, and why it is not throttled. The selection is an insertion sort
// over the registered set (tens of entries) plus a 1 KB SetData -- §11.3
// budgets ~1 MB for this row and 32 lights is 1,024 B. There is deliberately no
// dirty-check skipping the upload: the ACTIVE SET depends on player position,
// so it changes when the player moves even though no light did, and a
// "skip if unchanged" test would have to compare the selection anyway.
// ==========================================

using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Rendering
{
    public class LightingDriver : MonoBehaviour
    {
        [Tooltip("Transform the §3.8 cap measures distance from -- the player, or " +
                 "the camera in a fly/capture scene. Falls back to Camera.main.")]
        [SerializeField] private Transform _centre;

        [Tooltip("Advance the in-game clock. Off = frozen, which is what the §13 " +
                 "scrubber and every rig gate want: a pose you can compare.")]
        [SerializeField] private bool _runClock = true;

        [Tooltip("Poll LightingConfig.json for edits. §8.1's loop; off only for a " +
                 "measurement arm that must not pick up a mid-run edit.")]
        [SerializeField] private bool _hotReload = true;

        /// The A.6 buffer this driver owns. Public so a rig can inspect the cap
        /// state (active vs registered) without reaching through the renderer.
        public LightSourceBuffer Lights { get; private set; }

        /// Set by a scrubber to hold time without disabling the component (which
        /// would also stop the hot reload and the light upload).
        public static bool ClockPaused;

        /// Frames uploaded, so a rig can assert the driver actually ran rather
        /// than inferring it from a screenshot.
        public static long UploadsRun { get; private set; }

        void Awake()
        {
            Lights = new LightSourceBuffer();
            RaymarchFeature.Lights = Lights;
        }

        /// Wire the §6.6 enqueue. Called by whoever built the EditService --
        /// the bootstrapper, the Playground, or a rig.
        public void Attach(EditService edits)
        {
            if (edits != null) edits.AttachLightBuffer(Lights);
        }

        void Update()
        {
            if (_hotReload) LightingConfig.ReloadIfChanged();
            if (_runClock && !ClockPaused) SunCycle.Advance(Time.unscaledDeltaTime);

            Transform t = _centre != null ? _centre
                        : (Camera.main != null ? Camera.main.transform : null);
            float3 p = t != null ? new float3(t.position.x, t.position.y, t.position.z) : float3.zero;

            Lights.Upload(p);
            UploadsRun++;
        }

        /// Test/rig seam: point the cap's distance measurement somewhere without
        /// a Transform.
        public void SetCentre(Transform t) => _centre = t;

        public void SetRunClock(bool run) => _runClock = run;

        void OnDestroy()
        {
            if (ReferenceEquals(RaymarchFeature.Lights, Lights)) RaymarchFeature.Lights = null;
            Lights?.Dispose();
            Lights = null;
        }
    }
}
