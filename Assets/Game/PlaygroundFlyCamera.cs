// ==========================================
// Assets/Game/PlaygroundFlyCamera.cs
//
// Flycam for the Playground dogfood scene.
//
// A SEPARATE COMPONENT FROM SimpleFlyCamera ON PURPOSE. SimpleFlyCamera is used
// by the Phase 2/3/4 benchmark and acceptance scenes, whose captures are
// reproducible partly because their camera behaviour has not changed. Adding
// mouse capture to it would change how those scenes respond to input. This is a
// new file so the dogfood scene can have modern controls without touching
// anything a rig depends on.
//
// Mouse capture: click anywhere to lock the cursor and look freely; ESC
// releases it. Holding a button to look (SimpleFlyCamera's model) is fine for a
// scripted rig and miserable for actually flying around.

using UnityEngine;

public class PlaygroundFlyCamera : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 14f;      // m/s
    [SerializeField] private float _fastMultiplier = 4f;
    [SerializeField] private float _lookSensitivity = 2.4f;
    [SerializeField] private float _minSpeed = 2f;
    [SerializeField] private float _maxSpeed = 120f;

    private float _yaw, _pitch;
    private bool _captured;

    /// True while the cursor is locked, i.e. while mouse input belongs to the
    /// camera. Playground reads this so its own mouse actions only fire when the
    /// player is actually looking around rather than clicking to re-capture.
    public bool Captured => _captured;

    /// Scroll adjusts fly speed unless something else claims the wheel.
    public bool ScrollControlsSpeed { get; set; } = false;

    /// When false this component still CAPTURES THE MOUSE AND ACCUMULATES LOOK,
    /// but stops moving the transform. Playground turns it off in walk mode so
    /// PlayerController owns the position while mouse-look keeps working from
    /// one place -- two components both locking the cursor and both writing the
    /// camera transform fight, and the symptom is a camera that jitters or
    /// snaps back every frame.
    public bool MovementEnabled { get; set; } = true;

    /// Current look angles, so a controller driven by Playground can aim with
    /// the same mouse the flycam is reading.
    public float Yaw => _yaw;
    public float Pitch => _pitch;

    void Start()
    {
        Vector3 e = transform.rotation.eulerAngles;
        _pitch = e.x > 180f ? e.x - 360f : e.x;
        _yaw = e.y;
    }

    void Update()
    {
        // Click to capture, ESC to release. The first click of a session is
        // consumed by capture and deliberately does NOT also act in the world --
        // otherwise clicking back into the window mines whatever you happened to
        // be pointing at.
        if (!_captured && Input.GetMouseButtonDown(0))
        {
            _captured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _captured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (_captured)
        {
            _yaw += Input.GetAxisRaw("Mouse X") * _lookSensitivity;
            _pitch -= Input.GetAxisRaw("Mouse Y") * _lookSensitivity;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        if (ScrollControlsSpeed)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
                _moveSpeed = Mathf.Clamp(_moveSpeed * (scroll > 0 ? 1.2f : 1f / 1.2f), _minSpeed, _maxSpeed);
        }

        if (!MovementEnabled) return;

        float speed = _moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? _fastMultiplier : 1f);
        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
        if (move.sqrMagnitude > 0.0001f)
            transform.position += move.normalized * speed * Time.unscaledDeltaTime;
    }

    public float MoveSpeed => _moveSpeed;
}
