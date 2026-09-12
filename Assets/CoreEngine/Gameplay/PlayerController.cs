// ==========================================
// Assets/CoreEngine/Gameplay/PlayerController.cs
//
// §13 Phase 6, file 1 of 6. The MonoBehaviour shell: input, the per-frame
// PlayerConfig hot-reload poll, and the camera. All movement physics is in
// PlayerMotor, which has no Unity lifecycle so EditMode can drive it directly.
//
// This file was a stub that threw NotImplementedException. It is now the
// probe-based controller of §8.1's fork -- NOT the 540-collider treadmill. See
// PlayerMotor's header for which side of that fork this is and why, and for the
// one deviation from "~6-12 probes" that is flagged rather than slipped in.
//
// WHAT LIVES HERE AND WHAT DOES NOT:
//   here          input sampling, config hot reload, camera follow, spawn
//   PlayerMotor   acceleration, friction, gravity, jump, collision, step-up
//   NOT here      swept CCD at 60 m/s (§8.2, file 2), mining/building (§8.3,
//                 file 3), buoyancy (§8.6, file 6). This controller does not
//                 pretend to do any of them.
//
// §8.2's speed clamp is also NOT here. It belongs with the swept CCD in file 2,
// because the thing it clamps against -- op-list readback staleness -- is only
// dangerous at the speeds that file exists to handle.

using Unity.Mathematics;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Wiring (optional -- found automatically when left empty)")]
    [SerializeField] private Camera _camera;

    [Header("Spawn")]
    [Tooltip("Feet position in METRES. Y is resolved upward out of terrain on spawn.")]
    [SerializeField] private Vector3 _spawnM = new Vector3(1280f, 40f, 1268f);
    [SerializeField] private bool _resolveSpawnUpward = true;

    [Header("Camera")]
    [SerializeField] private float _eyeHeightM = 1.6f;
    [SerializeField] private float _mouseSensitivity = 2.2f;
    [SerializeField] private bool _captureMouse = true;

    [Header("Hot reload (§8.1)")]
    [Tooltip("Poll PlayerConfig.json for edits every frame. This is the tuning " +
             "loop §8.1 calls the highest-value tool for finding feel.")]
    [SerializeField] private bool _hotReload = true;

    private PlayerMotor _motor;
    private IWorldQuery _world;
    private IVoxelResidency _residency;
    private float _yawDeg, _pitchDeg;
    private bool _ready;

    /// The motor, for rigs and for anything that needs to read player state.
    /// Null until Bind has been called.
    public PlayerMotor Motor => _motor;
    public bool Ready => _ready;

    // =====================================================================

    /// THE GAME LAYER INJECTS THE WORLD. CoreEngine cannot reach
    /// Phase4Bootstrapper -- that is in Assembly-CSharp, which no asmdef
    /// assembly may reference -- and it should not want to: the engine has no
    /// business knowing which scene booted it. ChunkStore satisfies both
    /// interfaces, so a caller normally passes it twice.
    ///
    /// Both are required. Passing null residency would silently reinstate the
    /// §9.4 bug this controller is written to avoid -- unloaded chunks reading
    /// as Air and the player falling through them -- so it is rejected loudly
    /// instead. Tests that want that behaviour pass a fake that always returns
    /// true, which says so at the call site.
    public void Bind(IWorldQuery world, IVoxelResidency residency)
    {
        if (world == null) throw new System.ArgumentNullException(nameof(world));
        if (residency == null)
            throw new System.ArgumentNullException(nameof(residency),
                "PlayerController needs a residency source: ChunkStore.GetVoxel reports " +
                "Air for a chunk that is merely not loaded (frozen behaviour, §12), and " +
                "movement must not read that as empty space. Pass the ChunkStore, or an " +
                "explicit always-resident fake in a test.");

        _world = world;
        _residency = residency;

        // PlayerConfig.Initialize runs at BeforeSceneLoad, so Active is already
        // the on-disk config here, not defaults.
        _motor = new PlayerMotor(_world, _residency, PlayerConfig.Active);
        _motor.Teleport(new float3(_spawnM.x, _spawnM.y, _spawnM.z));
        if (_resolveSpawnUpward && !_motor.ResolveSpawn())
            Debug.LogWarning($"[PlayerController] could not free the spawn at {_spawnM} " +
                             "within 64 voxels -- is it deep inside terrain?");

        _yawDeg = transform.eulerAngles.y;
        _ready = true;
    }

    private void Start()
    {
        if (_camera == null) _camera = Camera.main;

        if (_captureMouse && !Application.isBatchMode)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        if (!_ready) return;

        // §8.1's hot reload. Two stat() calls when nothing changed.
        if (_hotReload && PlayerConfig.ReloadIfChanged())
            _motor.Config = PlayerConfig.Active;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        LookFromMouse();

        // Movement is relative to where the camera is looking, flattened. Yaw
        // only: looking up must not slow the player down.
        float2 wish = float2.zero;
        if (Input.GetKey(KeyCode.W)) wish.y += 1f;
        if (Input.GetKey(KeyCode.S)) wish.y -= 1f;
        if (Input.GetKey(KeyCode.D)) wish.x += 1f;
        if (Input.GetKey(KeyCode.A)) wish.x -= 1f;

        float yawRad = math.radians(_yawDeg);
        float sin = math.sin(yawRad), cos = math.cos(yawRad);
        float2 world = new float2(wish.x * cos + wish.y * sin,
                                  wish.y * cos - wish.x * sin);

        _motor.Step(dt, world, Input.GetKeyDown(KeyCode.Space));

        ApplyToTransform();
    }

    private void LookFromMouse()
    {
        if (!_captureMouse || Application.isBatchMode) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;
        _yawDeg += Input.GetAxisRaw("Mouse X") * _mouseSensitivity;
        _pitchDeg = Mathf.Clamp(_pitchDeg - Input.GetAxisRaw("Mouse Y") * _mouseSensitivity, -89f, 89f);
    }

    /// Pushes motor state onto the transform and the camera. The transform is a
    /// VIEW of the motor, never the source of truth -- writing to it from
    /// outside will be overwritten next frame. Use Motor.Teleport instead.
    private void ApplyToTransform()
    {
        transform.position = new Vector3(_motor.PositionM.x, _motor.PositionM.y, _motor.PositionM.z);
        transform.rotation = Quaternion.Euler(0f, _yawDeg, 0f);
        if (_camera != null)
        {
            _camera.transform.position = transform.position + Vector3.up * _eyeHeightM;
            _camera.transform.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        }
    }

    // =====================================================================
    // Rig surface. Phase6PlayerRig only; not part of the playable surface.
    //
    // PUBLIC, not internal, and that is forced rather than chosen: the rig lives
    // in Assembly-CSharp and this class lives in the CoreEngine asmdef, so
    // `internal` would be invisible to it. (Playground could use `internal` for
    // its own rig surface only because both sides are in Assembly-CSharp.)
    // =====================================================================

    public void DebugStep(float dt, float2 wishXZ, bool jump)
    {
        if (!_ready) return;
        if (_hotReload && PlayerConfig.ReloadIfChanged()) _motor.Config = PlayerConfig.Active;
        _motor.Step(dt, wishXZ, jump);
        ApplyToTransform();
    }

    public void DebugSetLook(float yawDeg, float pitchDeg)
    {
        _yawDeg = yawDeg;
        _pitchDeg = pitchDeg;
    }

    /// Disables this component's own Update so a rig can drive DebugStep at a
    /// fixed timestep without real input or a variable frame time racing it.
    public void DebugTakeControl() { enabled = false; }
}
