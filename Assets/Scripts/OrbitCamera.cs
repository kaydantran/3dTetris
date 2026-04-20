using UnityEngine;

/// <summary>
/// Click-and-drag orbit camera. The camera stays focused on a target point and also exposes
/// snap helpers for face-to-face view changes driven by gameplay input.
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The point the camera orbits around. If null, will try to find a TetrisGrid and use its Center.")]
    [SerializeField] private Transform target;
    [Tooltip("Fallback grid reference — used to auto-compute the target if 'target' is null.")]
    [SerializeField] private TetrisGrid grid;

    [Header("Orbit")]
    [SerializeField] private float distance = 24f;
    [SerializeField] private float minDistance = 8f;
    [SerializeField] private float maxDistance = 56f;

    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] private float rotationSpeed = 0.3f;
    [Tooltip("Vertical angle clamp so camera doesn't flip over. (min, max) in degrees.")]
    [SerializeField] private Vector2 pitchLimits = new Vector2(-20f, 90f);

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 4f;

    [Header("Smoothing")]
    [Tooltip("Higher = snappier camera. 0 = no smoothing.")]
    [SerializeField] private float smoothing = 12f;

    [Header("Input")]
    [Tooltip("Which mouse button initiates drag-to-rotate. 0=Left, 1=Right, 2=Middle.")]
    [SerializeField] private int dragMouseButton = 0;

    [Header("Snapped Face Views")]
    [SerializeField] private float faceYawStep = 90f;
    [SerializeField] private float flatFacePitch = 0f;
    [SerializeField] private float elevatedFacePitch = 45f;
    [SerializeField] private float topDownFacePitch = 90f;

    [Header("Layer Clear Impact")]
    [SerializeField] private float clearShakeDistance = 0.16f;
    [SerializeField] private float clearShakeDuration = 0.14f;
    [SerializeField] private float clearShakeFrequency = 28f;
    [SerializeField] private float clearShakeRollDegrees = 0.8f;

    private float yaw = 35f;
    private float pitch = 25f;
    private float targetYaw;
    private float targetPitch;
    private float targetDistance;

    private Vector3 targetPoint;
    private float impactShakeTimeRemaining;
    private float impactShakeDuration;
    private float impactShakeDistance;
    private float impactRollDegrees;
    private float impactShakeSeed;

    private void Awake()
    {
        if (grid == null)
        {
            grid = FindAnyObjectByType<TetrisGrid>();
        }
    }

    private void Start()
    {
        yaw = 0f;
        targetYaw = 0f;
        pitch = Mathf.Clamp(elevatedFacePitch, pitchLimits.x, pitchLimits.y);
        targetPitch = pitch;
        targetDistance = distance;
        ResolveTargetPoint();
        ApplyTransformImmediate();
    }

    private void LateUpdate()
    {
        ResolveTargetPoint();
        HandleInput();
        SmoothTransform();
        ApplyImpactShake();
    }

    private void ResolveTargetPoint()
    {
        if (target != null)
        {
            targetPoint = target.position;
            return;
        }

        if (grid != null)
        {
            targetPoint = grid.Center;
            return;
        }

        targetPoint = Vector3.zero;
    }

    private void HandleInput()
    {
        if (Input.GetMouseButton(dragMouseButton))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            targetYaw += dx * rotationSpeed * 60f;
            targetPitch -= dy * rotationSpeed * 60f;
            targetPitch = Mathf.Clamp(targetPitch, pitchLimits.x, pitchLimits.y);
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            targetDistance = Mathf.Clamp(targetDistance - scroll * zoomSpeed * 10f, minDistance, maxDistance);
        }
    }

    public void RotateToLeftFace()
    {
        targetYaw = GetNearestFaceYaw(targetYaw) - faceYawStep;
    }

    public void RotateToRightFace()
    {
        targetYaw = GetNearestFaceYaw(targetYaw) + faceYawStep;
    }

    public void SetElevatedFaceView()
    {
        targetYaw = GetNearestFaceYaw(targetYaw);

        float elevatedPitch = Mathf.Clamp(elevatedFacePitch, pitchLimits.x, pitchLimits.y);
        float topDownPitch = Mathf.Clamp(topDownFacePitch, pitchLimits.x, pitchLimits.y);

        // Pressing the same modifier again steps from elevated to top-down.
        if (Mathf.Abs(targetPitch - elevatedPitch) <= 0.5f)
        {
            targetPitch = topDownPitch;
            return;
        }

        targetPitch = elevatedPitch;
    }

    public void SetFlatFaceView()
    {
        targetYaw = GetNearestFaceYaw(targetYaw);
        targetPitch = Mathf.Clamp(flatFacePitch, pitchLimits.x, pitchLimits.y);
    }

    public void PlayLayerClearImpact()
    {
        impactShakeDistance = clearShakeDistance;
        impactShakeDuration = clearShakeDuration;
        impactRollDegrees = clearShakeRollDegrees;
        impactShakeTimeRemaining = clearShakeDuration;
        impactShakeSeed = Random.Range(0f, 1000f);
    }

    private float GetNearestFaceYaw(float sourceYaw)
    {
        return Mathf.Round(sourceYaw / faceYawStep) * faceYawStep;
    }

    private void SmoothTransform()
    {
        if (smoothing <= 0f)
        {
            yaw = targetYaw;
            pitch = targetPitch;
            distance = targetDistance;
        }
        else
        {
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            yaw = Mathf.LerpAngle(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
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

    private void ApplyImpactShake()
    {
        if (impactShakeTimeRemaining <= 0f) return;

        impactShakeTimeRemaining = Mathf.Max(0f, impactShakeTimeRemaining - Time.deltaTime);

        float normalizedProgress = impactShakeDuration > 0f
            ? 1f - (impactShakeTimeRemaining / impactShakeDuration)
            : 1f;
        float envelope = 1f - normalizedProgress;
        envelope *= envelope;

        float time = Time.unscaledTime * clearShakeFrequency;
        float lateral = SampleSignedNoise(impactShakeSeed + 11.7f, time);
        float vertical = SampleSignedNoise(impactShakeSeed + 37.9f, time) * 0.65f;
        float depth = SampleSignedNoise(impactShakeSeed + 73.1f, time) * 0.35f;

        Vector3 positionalOffset =
            transform.right * lateral * impactShakeDistance * envelope +
            transform.up * vertical * impactShakeDistance * envelope +
            transform.forward * depth * impactShakeDistance * envelope;

        transform.position += positionalOffset;

        float roll = SampleSignedNoise(impactShakeSeed + 101.3f, time) * impactRollDegrees * envelope;
        transform.rotation = Quaternion.AngleAxis(roll, transform.forward) * transform.rotation;
    }

    private static float SampleSignedNoise(float seed, float time)
    {
        return Mathf.PerlinNoise(seed, time) * 2f - 1f;
    }
}
