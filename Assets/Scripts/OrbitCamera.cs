using UnityEngine;

/// <summary>
/// Click-and-drag orbit camera. The camera stays focused on a target point (typically the
/// TetrisGrid's center) and lets the player rotate around it like a globe by dragging the
/// mouse. Scroll wheel zooms. All axes are clamped to reasonable bounds so the camera
/// never flips under the floor or ends up inside the playfield.
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The point the camera orbits around. If null, will try to find a TetrisGrid and use its Center.")]
    [SerializeField] private Transform target;
    [Tooltip("Fallback grid reference — used to auto-compute the target if 'target' is null.")]
    [SerializeField] private TetrisGrid grid;

    [Header("Orbit")]
    [SerializeField] private float distance = 18f;
    [SerializeField] private float minDistance = 8f;
    [SerializeField] private float maxDistance = 40f;

    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] private float rotationSpeed = 0.3f;
    [Tooltip("Vertical angle clamp so camera doesn't flip over. (min, max) in degrees.")]
    [SerializeField] private Vector2 pitchLimits = new Vector2(-20f, 80f);

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 4f;

    [Header("Smoothing")]
    [Tooltip("Higher = snappier camera. 0 = no smoothing.")]
    [SerializeField] private float smoothing = 12f;

    [Header("Input")]
    [Tooltip("Which mouse button initiates drag-to-rotate. 0=Left, 1=Right, 2=Middle.")]
    [SerializeField] private int dragMouseButton = 0;

    // Current and target spherical angles (degrees).
    private float yaw = 35f;
    private float pitch = 25f;
    private float targetYaw;
    private float targetPitch;
    private float targetDistance;

    private Vector3 targetPoint;

    private void Start()
    {
        targetYaw = yaw;
        targetPitch = pitch;
        targetDistance = distance;
        ResolveTargetPoint();
        ApplyTransformImmediate();
    }

    private void LateUpdate()
    {
        ResolveTargetPoint();
        HandleMouseInput();
        SmoothTransform();
    }

    private void ResolveTargetPoint()
    {
        if (target != null) { targetPoint = target.position; return; }
        if (grid   != null) { targetPoint = grid.Center;     return; }
        // Fallback: world origin.
        targetPoint = Vector3.zero;
    }

    private void HandleMouseInput()
    {
        // Orbit on drag.
        if (Input.GetMouseButton(dragMouseButton))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            // Mouse axes are already frame-rate independent; scale by rotationSpeed and a constant.
            targetYaw   += dx * rotationSpeed * 60f;
            targetPitch -= dy * rotationSpeed * 60f;
            targetPitch = Mathf.Clamp(targetPitch, pitchLimits.x, pitchLimits.y);
        }

        // Zoom on scroll.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            targetDistance = Mathf.Clamp(targetDistance - scroll * zoomSpeed * 10f,
                                         minDistance, maxDistance);
        }
    }

    private void SmoothTransform()
    {
        if (smoothing <= 0f)
        {
            yaw = targetYaw; pitch = targetPitch; distance = targetDistance;
        }
        else
        {
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime); // framerate-independent lerp
            yaw      = Mathf.LerpAngle(yaw, targetYaw, t);
            pitch    = Mathf.Lerp(pitch, targetPitch, t);
            distance = Mathf.Lerp(distance, targetDistance, t);
        }

        ApplyTransformImmediate();
    }

    private void ApplyTransformImmediate()
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, -distance);
        transform.position = targetPoint + offset;
        transform.rotation = Quaternion.LookRotation(targetPoint - transform.position, Vector3.up);
    }
}
