// Assets/Game/SimpleFlyCamera.cs
//
// The "bare debug fly-camera" the spec's cross-phase clarification prescribes
// for Phases 2–5 acceptance touring (§13: "These earlier phases use a bare
// debug fly-camera … never the Phase 6 player"). WASD + Q/E vertical, hold
// right mouse to look, Shift for 5x speed. No physics, no collision — it can
// and should fly through terrain and into caves.
//
// The Phase3AcceptanceRig disables this component while it drives the camera,
// and re-enables it only if there's no benchmark to chain into (manual-tour
// fallback). For a manual §13 3b island tour: leave the rig's Run On Start
// off, press Play, fly.
using UnityEngine;

public class SimpleFlyCamera : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 12f;   // m/s
    [SerializeField] private float _fastMultiplier = 5f;
    [SerializeField] private float _lookSensitivity = 2.2f;

    private float _yaw, _pitch;

    void OnEnable()
    {
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    void Update()
    {
        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * _lookSensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * _lookSensitivity;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        float speed = _moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? _fastMultiplier : 1f);
        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
        transform.position += move * (speed * Time.deltaTime);
    }
}
