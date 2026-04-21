using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Drives the currently falling piece: input, gravity, lock delay, hold/swap, 7-bag spawning,
/// ghost projection, and active-piece feedback.
/// </summary>
public class ActivePieceController : MonoBehaviour
{
    private const int ModernPreviewCount = 5;
    private const int IntroDemoSeed = 314159;

    [Header("References")]
    [SerializeField] private TetrisGrid grid;
    [SerializeField] private OrbitCamera orbitCamera;
    [SerializeField] private GameMaster gameMaster;
    [SerializeField] private Camera gameplayCamera;

    [Header("Piece Prefabs")]
    [Tooltip("Assign the standard seven tetromino prefabs here for exact 7-bag behaviour.")]
    [SerializeField] private GameObject[] piecePrefabs;

    [Header("Ghost Piece")]
    [Tooltip("Material applied to the ghost silhouette (should be transparent).")]
    [SerializeField] private Material ghostMaterial;
    [Tooltip("How much of the live piece color is carried into the ghost silhouette.")]
    [SerializeField, Range(0f, 1f)] private float ghostTintStrength = 0.2f;
    [Tooltip("Opacity of the ghost silhouette after tinting.")]
    [SerializeField, Range(0f, 1f)] private float ghostOpacity = 0.18f;

    [Header("Movement Feel")]
    [Tooltip("How long a held move key waits before it starts repeating.")]
    public float delayedAutoShift = 0.16f;
    [Tooltip("How quickly a held move key repeats after DAS. Set to 0 for instant wall slide.")]
    public float autoRepeatRate = 0.05f;

    [Header("Drop Speed")]
    [Tooltip("Seconds between automatic downward steps at the start of the game.")]
    [SerializeField] private float initialDropInterval = 2.0f;
    [Tooltip("Minimum cap on drop interval so gameplay never becomes literally impossible.")]
    [SerializeField] private float minDropInterval = 0.05f;
    [Tooltip("Multiplier applied each time IncreaseDropSpeed() is called. 0.85 = 15% faster per bump.")]
    [SerializeField, Range(0.5f, 0.99f)] private float speedIncreaseFactor = 0.85f;
    [Tooltip("Multiplier applied to drop interval while the soft-drop key is held.")]
    [SerializeField, Range(0.05f, 1f)] private float softDropMultiplier = 0.1f;

    [Header("Lock Delay")]
    [Tooltip("How long a piece can rest on the ground before it locks. Scales inversely with speed.")]
    [SerializeField] private float baseLockDelay = 0.5f;
    [Tooltip("Number of times a piece's lock timer can be reset by movement/rotation before forced lock.")]
    [SerializeField] private int maxLockResets = 15;

    [Header("Spawn")]
    [Tooltip("How far below the top of the grid (in cells) new pieces spawn.")]
    [SerializeField] private int spawnHeightOffset = 2;
    [SerializeField] private bool playIntroClearDemo = true;
    [SerializeField] private float introDemoStartDelay = 0.45f;
    [SerializeField] private float introDemoEndDelay = 0.2f;
    [SerializeField] private int introDemoLayerY = 0;
    [SerializeField] private float introDemoSetInterval = 0.18f;
    [SerializeField] private float introDemoPieceFallDuration = 0.95f;
    [SerializeField] private float introDemoSpawnHeight = 10f;
    [SerializeField] private int introDemoWaveSize = 4;

    [Header("Control Keys")]
    [SerializeField] private KeyCode moveLeftKey = KeyCode.A;
    [SerializeField] private KeyCode moveRightKey = KeyCode.D;
    [SerializeField] private KeyCode moveInwardKey = KeyCode.S;
    [SerializeField] private KeyCode moveOutwardKey = KeyCode.W;
    [SerializeField] private KeyCode holdKey = KeyCode.Q;
    [SerializeField] private KeyCode rotateCounterClockwiseKey = KeyCode.J;
    [SerializeField] private KeyCode rotateClockwiseKey = KeyCode.K;
    [SerializeField] private KeyCode rotateUpKey = KeyCode.U;
    [SerializeField] private KeyCode rotateDownKey = KeyCode.N;
    [SerializeField] private KeyCode cameraModifierKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode softDropKey = KeyCode.RightShift;
    [SerializeField] private KeyCode elevateViewKey = KeyCode.U;
    [SerializeField] private KeyCode flattenViewKey = KeyCode.N;

    [Header("Rotation Indicators")]
    [SerializeField] private bool showRotationIndicators = true;
    [SerializeField] private Color counterClockwiseIndicatorColor = new Color(1f, 0.55f, 0.35f, 0.95f);
    [SerializeField] private Color clockwiseIndicatorColor = new Color(0.35f, 0.9f, 1f, 0.95f);
    [SerializeField] private Color upwardRotationIndicatorColor = new Color(0.75f, 1f, 0.45f, 0.95f);
    [SerializeField] private Color downwardRotationIndicatorColor = new Color(1f, 0.7f, 0.35f, 0.95f);
    [SerializeField] private float rotationIndicatorHeightOffset = 0.6f;
    [SerializeField] private float rotationIndicatorPadding = 0.45f;

    [Header("Vertical Drop Trails")]
    [SerializeField] private bool showVerticalDropTrails = true;
    [SerializeField] private Color dropTrailColor = new Color(0.35f, 0.9f, 1f, 0.42f);
    [SerializeField] private float dropTrailInnerWidth = 0.045f;
    [SerializeField] private float dropTrailOuterWidth = 0.12f;
    [SerializeField] private float dropTrailCornerInset = 0.08f;

    [Header("Hard Drop Trail")]
    [SerializeField] private bool showHardDropTrail = true;
    [SerializeField] private Color hardDropTrailColor = new Color(1f, 1f, 1f, 0.75f);
    [SerializeField] private float hardDropTrailInnerWidth = 0.08f;
    [SerializeField] private float hardDropTrailOuterWidth = 0.18f;
    [SerializeField] private float hardDropTrailDuration = 0.14f;

    private GameObject activePiece;
    private GameObject ghostPiece;
    private GameObject rotationIndicatorRoot;
    private GameObject dropTrailRoot;

    private bool hasHeldPiece;
    private PieceIdentity heldPieceIdentity;
    private bool hasSwappedThisDrop;

    private readonly Queue<int> pieceBag = new Queue<int>();
    private readonly List<int> bagBuffer = new List<int>(7);
    private bool warnedAboutBagSize;
    private int activeRotationState;
    private int introPiecesInFlight;

    private float currentDropInterval;
    private float dropTimer;
    private bool softDropHeld;

    private bool isLocking;
    private float lockTimer;
    private int lockResetCount;
    private Coroutine flashRoutine;
    private Coroutine postLockRoutine;

    private readonly List<Renderer> activeRenderers = new List<Renderer>();
    private readonly List<TextMesh> indicatorLabels = new List<TextMesh>();
    private readonly List<LineRenderer> dropTrailLines = new List<LineRenderer>();
    private MaterialPropertyBlock flashBlock;

    private HeldMoveState[] moveStates;
    private Material indicatorLineMaterial;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    public event Action StateChanged;
    public event Action TimingSettingsChanged;

    private void Awake()
    {
        ApplyControlDefaults();
        flashBlock = new MaterialPropertyBlock();

        if (grid == null) grid = FindAnyObjectByType<TetrisGrid>();
        if (orbitCamera == null) orbitCamera = FindAnyObjectByType<OrbitCamera>();
        if (gameMaster == null) gameMaster = FindAnyObjectByType<GameMaster>();
        if (gameplayCamera == null)
        {
            gameplayCamera = orbitCamera != null ? orbitCamera.GetComponent<Camera>() : Camera.main;
        }

        RebuildMoveStates();

        currentDropInterval = initialDropInterval;
    }

    private void OnValidate()
    {
        ApplyControlDefaults();
        RebuildMoveStates();
    }

    private void Start()
    {
        StartCoroutine(BeginGameplaySequence());
    }

    private IEnumerator BeginGameplaySequence()
    {
        if (playIntroClearDemo)
        {
            yield return StartCoroutine(PlayIntroClearDemo());
        }

        gameMaster?.OnGameplayStarted();
        SpawnNextBagPiece();
    }

    private void ApplyControlDefaults()
    {
        moveLeftKey = KeyCode.A;
        moveRightKey = KeyCode.D;
        moveInwardKey = KeyCode.S;
        moveOutwardKey = KeyCode.W;
        holdKey = KeyCode.Q;
        rotateCounterClockwiseKey = KeyCode.J;
        rotateClockwiseKey = KeyCode.K;
        rotateUpKey = KeyCode.U;
        rotateDownKey = KeyCode.N;
        cameraModifierKey = KeyCode.LeftShift;
        softDropKey = KeyCode.RightShift;
        elevateViewKey = KeyCode.U;
        flattenViewKey = KeyCode.N;
    }

    private void RebuildMoveStates()
    {
        moveStates = new[]
        {
            new HeldMoveState(moveLeftKey, ViewMoveDirection.Left),
            new HeldMoveState(moveRightKey, ViewMoveDirection.Right),
            new HeldMoveState(moveOutwardKey, ViewMoveDirection.Forward),
            new HeldMoveState(moveInwardKey, ViewMoveDirection.Back)
        };
    }

    private void Update()
    {
        if (activePiece == null) return;

        HandleInput();
        HandleGravity();
        UpdateGhost();
        UpdateDropTrails();
        UpdateRotationIndicators();
    }

    private void OnDisable()
    {
        if (postLockRoutine != null)
        {
            StopCoroutine(postLockRoutine);
            postLockRoutine = null;
        }

        CancelLockDelay();
        DestroyGhost();
        DestroyDropTrails();
        DestroyRotationIndicators();
    }

    private void OnDestroy()
    {
        if (indicatorLineMaterial != null)
        {
            Destroy(indicatorLineMaterial);
        }
    }

    // ---------------------------------------------------------------------
    //  Input
    // ---------------------------------------------------------------------

    private void HandleInput()
    {
        if (IsTypingIntoUiInputField())
        {
            softDropHeld = false;
            ResetHeldMoveStates();
            return;
        }

        if (HandleCameraModifierInput())
        {
            softDropHeld = false;
            return;
        }

        softDropHeld = IsSoftDropHeld();

        if (Input.GetKeyDown(holdKey))
        {
            TryHold();
            return;
        }

        if (Input.GetKeyDown(rotateCounterClockwiseKey))
        {
            TryRotateOnViewedPlane(90f);
        }

        if (Input.GetKeyDown(rotateClockwiseKey))
        {
            TryRotateOnViewedPlane(-90f);
        }

        if (Input.GetKeyDown(rotateUpKey))
        {
            TryRotateOnHorizontalScreenAxis(90f);
        }

        if (Input.GetKeyDown(rotateDownKey))
        {
            TryRotateOnHorizontalScreenAxis(-90f);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            HardDrop();
            return;
        }

        HandleRepeatingMovement();
    }

    private bool HandleCameraModifierInput()
    {
        if (!Input.GetKey(cameraModifierKey)) return false;
        if (orbitCamera == null) return false;

        if (Input.GetKeyDown(rotateCounterClockwiseKey))
        {
            orbitCamera.RotateToRightFace();
            return true;
        }

        if (Input.GetKeyDown(rotateClockwiseKey))
        {
            orbitCamera.RotateToLeftFace();
            return true;
        }

        if (Input.GetKeyDown(elevateViewKey))
        {
            orbitCamera.SetElevatedFaceView();
            return true;
        }

        if (Input.GetKeyDown(flattenViewKey))
        {
            orbitCamera.SetFlatFaceView();
            return true;
        }

        return false;
    }

    private bool IsSoftDropHeld()
    {
        return Input.GetKey(softDropKey);
    }

    private void HandleRepeatingMovement()
    {
        if (moveStates == null) return;

        foreach (HeldMoveState moveState in moveStates)
        {
            HandleRepeatingMove(moveState);
        }
    }

    private void ResetHeldMoveStates()
    {
        if (moveStates == null) return;

        foreach (HeldMoveState moveState in moveStates)
        {
            moveState.Reset();
        }
    }

    private static bool IsTypingIntoUiInputField()
    {
        if (EventSystem.current == null) return false;

        GameObject selectedObject = EventSystem.current.currentSelectedGameObject;
        return selectedObject != null && selectedObject.GetComponent<UnityEngine.UI.InputField>() != null;
    }

    private void HandleRepeatingMove(HeldMoveState moveState)
    {
        if (Input.GetKeyDown(moveState.Key))
        {
            MoveInViewDirection(moveState.Direction);
            moveState.WaitingForAutoShift = true;
            moveState.NextRepeatTime = Time.time + Mathf.Max(0f, delayedAutoShift);
            return;
        }

        if (!Input.GetKey(moveState.Key))
        {
            moveState.Reset();
            return;
        }

        if (moveState.WaitingForAutoShift)
        {
            if (Time.time < moveState.NextRepeatTime) return;

            moveState.WaitingForAutoShift = false;

            if (autoRepeatRate <= 0f)
            {
                MoveUntilBlocked(ResolveViewMoveDelta(moveState.Direction));
                moveState.NextRepeatTime = float.PositiveInfinity;
                return;
            }

            MoveInViewDirection(moveState.Direction);
            moveState.NextRepeatTime = Time.time + autoRepeatRate;
            return;
        }

        if (autoRepeatRate > 0f && Time.time >= moveState.NextRepeatTime)
        {
            MoveInViewDirection(moveState.Direction);
            moveState.NextRepeatTime = Time.time + autoRepeatRate;
        }
    }

    // ---------------------------------------------------------------------
    //  Movement & rotation
    // ---------------------------------------------------------------------

    private bool MoveInViewDirection(ViewMoveDirection direction)
    {
        return TryMove(ResolveViewMoveDelta(direction));
    }

    private bool TryRotateOnViewedPlane(float angle)
    {
        Vector3 axis = GetCameraRelativeForward();
        return TryRotate(axis, angle);
    }

    private bool TryRotateOnHorizontalScreenAxis(float angle)
    {
        Vector3 axis = GetCameraRelativeRight();
        return TryRotate(axis, angle);
    }

    private void MoveUntilBlocked(Vector3Int delta, bool playerInitiated = true)
    {
        while (TryMove(delta, playerInitiated))
        {
        }
    }

    private Vector3Int ResolveViewMoveDelta(ViewMoveDirection direction)
    {
        Vector3Int forward = GetCameraRelativeForward();
        Vector3Int right = new Vector3Int(forward.z, 0, -forward.x);

        return direction switch
        {
            ViewMoveDirection.Left => -right,
            ViewMoveDirection.Right => right,
            ViewMoveDirection.Forward => forward,
            ViewMoveDirection.Back => -forward,
            _ => Vector3Int.zero
        };
    }

    private Vector3Int GetCameraRelativeForward()
    {
        Camera viewCamera = ResolveGameplayCamera();
        if (viewCamera == null) return Vector3Int.forward;

        Vector3 planarForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f) return Vector3Int.forward;

        planarForward.Normalize();

        Vector3Int[] directions =
        {
            Vector3Int.forward,
            Vector3Int.right,
            Vector3Int.back,
            Vector3Int.left
        };

        float bestDot = float.NegativeInfinity;
        Vector3Int bestDirection = Vector3Int.forward;

        foreach (Vector3Int direction in directions)
        {
            float dot = Vector3.Dot(planarForward, (Vector3)direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestDirection = direction;
            }
        }

        return bestDirection;
    }

    private Vector3Int GetCameraRelativeRight()
    {
        Vector3Int forward = GetCameraRelativeForward();
        return new Vector3Int(forward.z, 0, -forward.x);
    }

    private Camera ResolveGameplayCamera()
    {
        if (gameplayCamera != null) return gameplayCamera;
        gameplayCamera = orbitCamera != null ? orbitCamera.GetComponent<Camera>() : Camera.main;
        return gameplayCamera;
    }

    private bool TryMove(Vector3Int delta, bool playerInitiated = true)
    {
        if (activePiece == null || delta == Vector3Int.zero) return false;

        Vector3 newPos = activePiece.transform.position + new Vector3(delta.x, delta.y, delta.z);
        if (!grid.IsValidPosition(activePiece.transform, newPos, activePiece.transform.rotation))
        {
            return false;
        }

        activePiece.transform.position = newPos;
        if (playerInitiated)
        {
            OnPieceMovedOrRotated();
        }
        return true;
    }

    private bool TryRotate(Vector3 worldAxis, float angle)
    {
        if (activePiece == null) return false;

        string pieceCode = GetActivePieceCode();
        Quaternion delta = Quaternion.AngleAxis(angle, worldAxis);
        Quaternion newRotation = delta * activePiece.transform.rotation;
        int rotationDelta = angle < 0f ? 1 : -1;
        int fromState = activeRotationState;
        int toState = Mod(activeRotationState + rotationDelta, 4);

        if (grid.IsValidPosition(activePiece.transform, activePiece.transform.position, newRotation))
        {
            activePiece.transform.rotation = newRotation;
            activeRotationState = toState;
            OnPieceMovedOrRotated();
            return true;
        }

        Vector3 horizontalKickAxis = Vector3.Cross(Vector3.up, worldAxis);
        if (horizontalKickAxis.sqrMagnitude < 0.0001f)
        {
            horizontalKickAxis = Vector3.right;
        }
        else
        {
            horizontalKickAxis.Normalize();
        }

        Vector2Int[] kicks = GetKickOffsets(pieceCode, fromState, toState);

        foreach (Vector2Int kick in kicks)
        {
            Vector3 kickOffset = horizontalKickAxis * kick.x + Vector3.up * kick.y;
            Vector3 testPosition = activePiece.transform.position + kickOffset;
            if (!grid.IsValidPosition(activePiece.transform, testPosition, newRotation)) continue;

            activePiece.transform.position = testPosition;
            activePiece.transform.rotation = newRotation;
            activeRotationState = toState;
            OnPieceMovedOrRotated();
            return true;
        }

        return false;
    }

    private void OnPieceMovedOrRotated()
    {
        if (!isLocking) return;
        if (lockResetCount >= maxLockResets) return;

        lockResetCount++;
        lockTimer = GetEffectiveLockDelay();

        if (!IsTouchingGround())
        {
            CancelLockDelay();
        }
    }

    // ---------------------------------------------------------------------
    //  Gravity & locking
    // ---------------------------------------------------------------------

    private void HandleGravity()
    {
        float interval = softDropHeld ? currentDropInterval * softDropMultiplier : currentDropInterval;
        interval = Mathf.Max(interval, minDropInterval);

        dropTimer += Time.deltaTime;
        if (dropTimer < interval) return;

        dropTimer = 0f;
        StepDown();
    }

    private void StepDown()
    {
        if (TryMove(Vector3Int.down, playerInitiated: false))
        {
            if (isLocking && !IsTouchingGround())
            {
                CancelLockDelay();
            }

            return;
        }

        if (!isLocking)
        {
            BeginLockDelay();
        }
    }

    private bool IsTouchingGround()
    {
        Vector3 below = activePiece.transform.position + Vector3.down;
        return !grid.IsValidPosition(activePiece.transform, below, activePiece.transform.rotation);
    }

    private float GetEffectiveLockDelay()
    {
        float safeInitialInterval = Mathf.Max(0.001f, initialDropInterval);
        float speedRatio = currentDropInterval / safeInitialInterval;
        return Mathf.Max(0.05f, baseLockDelay * speedRatio);
    }

    private void BeginLockDelay()
    {
        isLocking = true;
        lockTimer = GetEffectiveLockDelay();
        lockResetCount = 0;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        flashRoutine = StartCoroutine(FlashAndLock());
    }

    private void CancelLockDelay()
    {
        isLocking = false;
        lockTimer = 0f;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        ClearFlashState();
    }

    private IEnumerator FlashAndLock()
    {
        float safeInitialInterval = Mathf.Max(0.001f, initialDropInterval);
        float speedRatio = Mathf.Clamp01(currentDropInterval / safeInitialInterval);
        float flashesPerSecond = Mathf.Lerp(12f, 2f, speedRatio);
        float halfCycle = 1f / (flashesPerSecond * 2f);
        bool flashOn = false;

        while (isLocking && lockTimer > 0f)
        {
            flashOn = !flashOn;
            SetFlashState(flashOn);

            float wait = Mathf.Min(halfCycle, lockTimer);
            yield return new WaitForSeconds(wait);
            lockTimer -= wait;

            if (!isLocking)
            {
                yield break;
            }
        }

        ClearFlashState();
        flashRoutine = null;
        LockPieceNow();
    }

    private void SetFlashState(bool enabled)
    {
        if (flashBlock == null)
        {
            flashBlock = new MaterialPropertyBlock();
        }

        foreach (Renderer renderer in activeRenderers)
        {
            if (renderer == null) continue;

            if (!enabled)
            {
                renderer.SetPropertyBlock(null);
                continue;
            }

            Material material = renderer.sharedMaterial;
            Color baseColor = GetRendererBaseColor(material);
            Color flashColor = Color.Lerp(baseColor, Color.white, 0.75f);

            flashBlock.Clear();

            if (material != null)
            {
                if (material.HasProperty(BaseColorID)) flashBlock.SetColor(BaseColorID, flashColor);
                if (material.HasProperty(ColorID)) flashBlock.SetColor(ColorID, flashColor);
                if (material.HasProperty(EmissionColorID)) flashBlock.SetColor(EmissionColorID, flashColor * 0.2f);
            }

            renderer.SetPropertyBlock(flashBlock);
        }
    }

    private void ClearFlashState()
    {
        foreach (Renderer renderer in activeRenderers)
        {
            if (renderer != null)
            {
                renderer.SetPropertyBlock(null);
            }
        }
    }

    private static Color GetRendererBaseColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorID)) return material.GetColor(BaseColorID);
        if (material.HasProperty(ColorID)) return material.GetColor(ColorID);
        return Color.white;
    }

    private void LockPieceNow()
    {
        if (activePiece == null) return;

        isLocking = false;
        flashRoutine = null;
        ClearFlashState();

        GridLockResult lockResult = grid.LockPiece(activePiece.transform);
        gameMaster?.PlayPlaceBlockSound();
        activePiece = null;
        activeRenderers.Clear();
        DestroyGhost();
        DestroyDropTrails();
        DestroyRotationIndicators();

        if (gameMaster != null)
        {
            gameMaster.OnPieceLocked();
        }

        if (postLockRoutine != null)
        {
            StopCoroutine(postLockRoutine);
        }

        postLockRoutine = StartCoroutine(ResolveLockedPiece(lockResult));
    }

    private IEnumerator ResolveLockedPiece(GridLockResult lockResult)
    {
        if (grid != null)
        {
            yield return StartCoroutine(grid.CheckAndClearLayersAnimated(lockResult.TouchedLayers, OnLayersClearedResolved));
        }

        postLockRoutine = null;

        if (lockResult.CrossedTopBoundary)
        {
            GameOver();
            yield break;
        }

        SpawnNextBagPiece();
    }

    private void OnLayersClearedResolved(int clearedCount)
    {
        if (clearedCount <= 0) return;

        gameMaster?.PlayClearSound();
        orbitCamera?.PlayLayerClearImpact();
        gameMaster?.OnLayersCleared(clearedCount);
    }

    // ---------------------------------------------------------------------
    //  Hard drop
    // ---------------------------------------------------------------------

    private void HardDrop()
    {
        if (activePiece == null) return;

        Vector3[] startCubePositions = showHardDropTrail ? GetActiveCubeWorldPositions() : null;
        int droppedCells = 0;

        while (grid.IsValidPosition(activePiece.transform, activePiece.transform.position + Vector3.down, activePiece.transform.rotation))
        {
            activePiece.transform.position += Vector3.down;
            droppedCells++;
        }

        if (droppedCells > 0 && showHardDropTrail)
        {
            SpawnHardDropTrail(startCubePositions, GetActiveCubeWorldPositions());
        }

        CancelLockDelay();
        LockPieceNow();
    }

    // ---------------------------------------------------------------------
    //  Ghost piece
    // ---------------------------------------------------------------------

    private void UpdateGhost()
    {
        if (ghostPiece == null || activePiece == null) return;

        ghostPiece.transform.rotation = activePiece.transform.rotation;
        Vector3 projected = activePiece.transform.position;

        while (grid.IsValidPosition(activePiece.transform, projected + Vector3.down, activePiece.transform.rotation))
        {
            projected += Vector3.down;
        }

        ghostPiece.transform.position = projected;
    }

    private void BuildGhost()
    {
        DestroyGhost();

        if (activePiece == null) return;

        ghostPiece = Instantiate(activePiece, activePiece.transform.position, activePiece.transform.rotation);
        ghostPiece.name = "Ghost";

        foreach (MonoBehaviour behaviour in ghostPiece.GetComponentsInChildren<MonoBehaviour>())
        {
            Destroy(behaviour);
        }

        foreach (Collider collider in ghostPiece.GetComponentsInChildren<Collider>())
        {
            Destroy(collider);
        }

        Renderer[] sourceRenderers = activePiece.GetComponentsInChildren<Renderer>();
        Renderer[] ghostRenderers = ghostPiece.GetComponentsInChildren<Renderer>();
        MaterialPropertyBlock ghostBlock = new MaterialPropertyBlock();

        for (int i = 0; i < ghostRenderers.Length; i++)
        {
            Renderer ghostRenderer = ghostRenderers[i];
            if (ghostRenderer == null) continue;

            if (ghostMaterial != null)
            {
                ghostRenderer.sharedMaterial = ghostMaterial;
            }

            Color sourceColor = i < sourceRenderers.Length
                ? GetRendererBaseColor(sourceRenderers[i].sharedMaterial)
                : Color.white;

            Color ghostColor = Color.Lerp(Color.white, sourceColor, ghostTintStrength);
            ghostColor.a = ghostOpacity;

            ghostBlock.Clear();

            Material ghostSharedMaterial = ghostRenderer.sharedMaterial;
            if (ghostSharedMaterial != null)
            {
                if (ghostSharedMaterial.HasProperty(BaseColorID)) ghostBlock.SetColor(BaseColorID, ghostColor);
                if (ghostSharedMaterial.HasProperty(ColorID)) ghostBlock.SetColor(ColorID, ghostColor);
                if (ghostSharedMaterial.HasProperty(EmissionColorID)) ghostBlock.SetColor(EmissionColorID, Color.black);
            }

            ghostRenderer.SetPropertyBlock(ghostBlock);
        }
    }

    private void DestroyGhost()
    {
        if (ghostPiece != null)
        {
            Destroy(ghostPiece);
            ghostPiece = null;
        }
    }

    // ---------------------------------------------------------------------
    //  Hold / swap
    // ---------------------------------------------------------------------

    private void TryHold()
    {
        if (activePiece == null || hasSwappedThisDrop) return;

        PieceIdentity currentIdentity = GetActivePieceIdentity();
        DestroyCurrentActivePiece();

        if (!hasHeldPiece)
        {
            heldPieceIdentity = currentIdentity;
            hasHeldPiece = true;
            SpawnNextBagPiece();
            hasSwappedThisDrop = true;
            return;
        }

        PieceIdentity identityToSpawn = heldPieceIdentity;
        heldPieceIdentity = currentIdentity;
        SpawnPiece(identityToSpawn);
        hasSwappedThisDrop = true;
    }

    private PieceIdentity GetActivePieceIdentity()
    {
        PieceTag tag = activePiece != null ? activePiece.GetComponent<PieceTag>() : null;
        return tag != null
            ? new PieceIdentity(tag.PrefabIndex)
            : new PieceIdentity(0);
    }

    private void DestroyCurrentActivePiece()
    {
        CancelLockDelay();
        DestroyGhost();
        DestroyDropTrails();
        DestroyRotationIndicators();
        activeRenderers.Clear();

        if (activePiece != null)
        {
            Destroy(activePiece);
            activePiece = null;
        }
    }

    // ---------------------------------------------------------------------
    //  Spawning
    // ---------------------------------------------------------------------

    private void SpawnNextBagPiece()
    {
        SpawnPiece(GetNextBagPieceIdentity());
    }

    private IEnumerator PlayIntroClearDemo()
    {
        if (grid == null || piecePrefabs == null || piecePrefabs.Length == 0)
        {
            yield break;
        }

        int targetLayer = Mathf.Clamp(introDemoLayerY, 0, Mathf.Max(0, grid.Height - 1));
        List<IntroDemoPlacement> introPlacements = BuildIntroDemoPlacements();
        if (introPlacements.Count == 0)
        {
            yield break;
        }

        List<IntroDemoPlacement> spawnSequence = BuildIntroDemoSpawnSequence(introPlacements);
        if (spawnSequence.Count == 0)
        {
            yield break;
        }

        if (introDemoStartDelay > 0f)
        {
            yield return new WaitForSeconds(introDemoStartDelay);
        }

        introPiecesInFlight = 0;
        int placementIndex = 0;
        bool spawnedFirstWave = false;
        while (placementIndex < spawnSequence.Count)
        {
            int piecesThisWave = spawnedFirstWave ? Mathf.Max(1, introDemoWaveSize) : 1;

            for (int i = 0; i < piecesThisWave && placementIndex < spawnSequence.Count; i++, placementIndex++)
            {
                IntroDemoPlacement placement = spawnSequence[placementIndex];
                StartCoroutine(AnimateIntroPieceToPlacement(placement, targetLayer));
            }

            spawnedFirstWave = true;

            if (placementIndex < spawnSequence.Count && introDemoSetInterval > 0f)
            {
                yield return new WaitForSeconds(introDemoSetInterval);
            }
        }

        while (introPiecesInFlight > 0)
        {
            yield return null;
        }

        yield return StartCoroutine(grid.CheckAndClearLayersAnimated(
            new[] { targetLayer },
            clearedCount =>
            {
                if (clearedCount <= 0) return;

                gameMaster?.PlayClearSound();
                orbitCamera?.PlayLayerClearImpact();
            }));

        if (introDemoEndDelay > 0f)
        {
            yield return new WaitForSeconds(introDemoEndDelay);
        }
    }

    private IEnumerator AnimateIntroPieceToPlacement(IntroDemoPlacement placement, int targetLayer)
    {
        GameObject introPiecePrefab = ResolveIntroDemoPiecePrefab(placement.PieceCode);
        if (introPiecePrefab == null)
        {
            yield break;
        }

        Quaternion targetRotation = ResolveIntroDemoRotation(introPiecePrefab, placement);
        if (targetRotation == Quaternion.identity && !IntroPlacementMatchesPrefab(introPiecePrefab, targetRotation, placement))
        {
            Debug.LogWarning($"Could not resolve intro rotation for {placement.PieceCode}.");
            yield break;
        }

        if (!TryResolveIntroPieceRootPosition(introPiecePrefab, targetRotation, placement, targetLayer, out Vector3 targetPosition))
        {
            Debug.LogWarning($"Could not resolve intro target position for {placement.PieceCode}.");
            yield break;
        }

        introPiecesInFlight++;

        Vector3 spawnPosition = BuildIntroSpawnPosition(targetPosition);

        GameObject introPiece = Instantiate(introPiecePrefab, spawnPosition, targetRotation);
        introPiece.name = $"Intro_{placement.PieceCode}_{placement.Id}";

        float duration = Mathf.Max(0.05f, introDemoPieceFallDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (introPiece == null)
            {
                introPiecesInFlight = Mathf.Max(0, introPiecesInFlight - 1);
                yield break;
            }

            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);
            introPiece.transform.position = Vector3.Lerp(spawnPosition, targetPosition, eased);
            yield return null;
        }

        if (introPiece != null)
        {
            introPiece.transform.SetPositionAndRotation(targetPosition, targetRotation);
            grid.LockPiece(introPiece.transform);
        }

        introPiecesInFlight = Mathf.Max(0, introPiecesInFlight - 1);
    }

    private Vector3 BuildIntroSpawnPosition(Vector3 targetPosition)
    {
        return targetPosition + Vector3.up * Mathf.Max(1f, introDemoSpawnHeight);
    }

    private GameObject ResolveIntroDemoPiecePrefab(string pieceCode)
    {
        foreach (GameObject piecePrefab in piecePrefabs)
        {
            if (piecePrefab == null) continue;
            if (piecePrefab.name.IndexOf($"Piece{pieceCode}", StringComparison.OrdinalIgnoreCase) >= 0) return piecePrefab;
        }

        Debug.LogWarning($"Intro clear demo requires a Piece{pieceCode} prefab in piecePrefabs.");
        return null;
    }

    private Quaternion ResolveIntroDemoRotation(GameObject piecePrefab, IntroDemoPlacement placement)
    {
        foreach (Quaternion rotation in BuildIntroRotationCandidates())
        {
            if (IntroPlacementMatchesPrefab(piecePrefab, rotation, placement))
            {
                return rotation;
            }
        }

        return Quaternion.identity;
    }

    private static IEnumerable<Quaternion> BuildIntroRotationCandidates()
    {
        List<Quaternion> candidates = new List<Quaternion>();

        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    Quaternion rotation = Quaternion.Euler(x * 90f, y * 90f, z * 90f);
                    bool isDuplicate = false;

                    foreach (Quaternion existing in candidates)
                    {
                        if (Mathf.Abs(Quaternion.Dot(existing, rotation)) > 0.9999f)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }

                    if (!isDuplicate)
                    {
                        candidates.Add(rotation);
                    }
                }
            }
        }

        candidates.Sort((a, b) =>
        {
            Vector3 aUp = a * Vector3.up;
            Vector3 bUp = b * Vector3.up;
            int aFlatScore = Mathf.Abs(aUp.y) < 0.01f ? 0 : 1;
            int bFlatScore = Mathf.Abs(bUp.y) < 0.01f ? 0 : 1;
            if (aFlatScore != bFlatScore) return aFlatScore.CompareTo(bFlatScore);

            Vector3 aForward = a * Vector3.forward;
            Vector3 bForward = b * Vector3.forward;
            if (!Mathf.Approximately(aForward.y, bForward.y))
            {
                return -aForward.y.CompareTo(bForward.y);
            }

            return 0;
        });

        return candidates;
    }

    private bool IntroPlacementMatchesPrefab(GameObject piecePrefab, Quaternion rotation, IntroDemoPlacement placement)
    {
        List<Vector2Int> prefabCells = GetNormalizedIntroPrefabCells(piecePrefab.transform, rotation);
        List<Vector2Int> placementCells = GetNormalizedIntroPlacementCells(placement);

        if (prefabCells.Count != placementCells.Count) return false;

        for (int i = 0; i < prefabCells.Count; i++)
        {
            if (prefabCells[i] != placementCells[i]) return false;
        }

        return true;
    }

    private bool TryResolveIntroPieceRootPosition(GameObject piecePrefab, Quaternion rotation, IntroDemoPlacement placement, int targetLayer, out Vector3 targetPosition)
    {
        targetPosition = Vector3.zero;
        if (piecePrefab == null || grid == null)
        {
            return false;
        }

        Dictionary<Vector2Int, Vector3Int> targetCellsByNormalizedOffset = BuildIntroPlacementCellMap(placement, targetLayer);
        if (targetCellsByNormalizedOffset.Count == 0)
        {
            return false;
        }

        List<Vector3Int> roundedOffsets = new List<Vector3Int>(piecePrefab.transform.childCount);
        List<Vector3> rotatedLocalPositions = new List<Vector3>(piecePrefab.transform.childCount);
        int minOffsetX = int.MaxValue;
        int minOffsetZ = int.MaxValue;

        for (int i = 0; i < piecePrefab.transform.childCount; i++)
        {
            Vector3 rotatedLocalPosition = rotation * piecePrefab.transform.GetChild(i).localPosition;
            Vector3Int roundedOffset = new Vector3Int(
                Mathf.RoundToInt(rotatedLocalPosition.x),
                Mathf.RoundToInt(rotatedLocalPosition.y),
                Mathf.RoundToInt(rotatedLocalPosition.z));

            rotatedLocalPositions.Add(rotatedLocalPosition);
            roundedOffsets.Add(roundedOffset);
            minOffsetX = Mathf.Min(minOffsetX, roundedOffset.x);
            minOffsetZ = Mathf.Min(minOffsetZ, roundedOffset.z);
        }

        bool hasResolvedRoot = false;
        Vector3 resolvedRoot = Vector3.zero;

        for (int i = 0; i < roundedOffsets.Count; i++)
        {
            Vector3Int roundedOffset = roundedOffsets[i];
            Vector2Int normalizedKey = new Vector2Int(roundedOffset.x - minOffsetX, roundedOffset.z - minOffsetZ);

            if (!targetCellsByNormalizedOffset.TryGetValue(normalizedKey, out Vector3Int targetCell))
            {
                return false;
            }

            Vector3 candidateRoot = grid.CellToWorldCenter(targetCell) - rotatedLocalPositions[i];
            if (!hasResolvedRoot)
            {
                resolvedRoot = candidateRoot;
                hasResolvedRoot = true;
                continue;
            }

            if ((candidateRoot - resolvedRoot).sqrMagnitude > 0.0001f)
            {
                return false;
            }
        }

        targetPosition = resolvedRoot;
        return hasResolvedRoot;
    }

    private Dictionary<Vector2Int, Vector3Int> BuildIntroPlacementCellMap(IntroDemoPlacement placement, int targetLayer)
    {
        Dictionary<Vector2Int, Vector3Int> targetCellsByNormalizedOffset = new Dictionary<Vector2Int, Vector3Int>();
        int minTargetX = int.MaxValue;
        int minTargetZ = int.MaxValue;

        foreach (Vector2Int cell in placement.Cells)
        {
            minTargetX = Mathf.Min(minTargetX, cell.x);
            minTargetZ = Mathf.Min(minTargetZ, cell.y);
        }

        foreach (Vector2Int cell in placement.Cells)
        {
            Vector2Int normalized = new Vector2Int(cell.x - minTargetX, cell.y - minTargetZ);
            targetCellsByNormalizedOffset[normalized] = new Vector3Int(cell.x, targetLayer, cell.y);
        }

        return targetCellsByNormalizedOffset;
    }

    private List<Vector2Int> GetNormalizedIntroPlacementCells(IntroDemoPlacement placement)
    {
        List<Vector2Int> normalizedCells = new List<Vector2Int>(placement.Cells.Length);
        int minX = int.MaxValue;
        int minZ = int.MaxValue;

        foreach (Vector2Int cell in placement.Cells)
        {
            minX = Mathf.Min(minX, cell.x);
            minZ = Mathf.Min(minZ, cell.y);
        }

        foreach (Vector2Int cell in placement.Cells)
        {
            normalizedCells.Add(new Vector2Int(cell.x - minX, cell.y - minZ));
        }

        normalizedCells.Sort((a, b) =>
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            return a.y.CompareTo(b.y);
        });

        return normalizedCells;
    }

    private List<Vector3Int> GetNormalizedIntroPrefabOffsets(Transform pieceRoot, Quaternion rotation)
    {
        List<Vector3Int> offsets = new List<Vector3Int>(pieceRoot.childCount);
        int minX = int.MaxValue;
        int minZ = int.MaxValue;

        for (int i = 0; i < pieceRoot.childCount; i++)
        {
            Vector3 rotated = rotation * pieceRoot.GetChild(i).localPosition;
            Vector3Int offset = new Vector3Int(
                Mathf.RoundToInt(rotated.x),
                Mathf.RoundToInt(rotated.y),
                Mathf.RoundToInt(rotated.z));

            offsets.Add(offset);
            minX = Mathf.Min(minX, offset.x);
            minZ = Mathf.Min(minZ, offset.z);
        }

        for (int i = 0; i < offsets.Count; i++)
        {
            Vector3Int offset = offsets[i];
            offsets[i] = new Vector3Int(offset.x - minX, 0, offset.z - minZ);
        }

        offsets.Sort((a, b) =>
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            return a.z.CompareTo(b.z);
        });

        return offsets;
    }

    private List<Vector2Int> GetNormalizedIntroPrefabCells(Transform pieceRoot, Quaternion rotation)
    {
        List<Vector3Int> offsets = GetNormalizedIntroPrefabOffsets(pieceRoot, rotation);
        List<Vector2Int> cells = new List<Vector2Int>(offsets.Count);

        foreach (Vector3Int offset in offsets)
        {
            cells.Add(new Vector2Int(offset.x, offset.z));
        }

        return cells;
    }

    private List<IntroDemoPlacement> BuildIntroDemoSpawnSequence(List<IntroDemoPlacement> placements)
    {
        List<IntroDemoPlacement> sequence = new List<IntroDemoPlacement>(placements);
        System.Random rng = new System.Random(IntroDemoSeed);

        for (int i = sequence.Count - 1; i > 0; i--)
        {
            int swapIndex = rng.Next(i + 1);
            (sequence[i], sequence[swapIndex]) = (sequence[swapIndex], sequence[i]);
        }

        return sequence;
    }

    private List<IntroDemoPlacement> BuildIntroDemoPlacements()
    {
        if (grid == null || grid.Width != 8 || grid.Depth != 8)
        {
            Debug.LogWarning("Intro clear demo mixed-piece layout currently expects an 8x8 playfield.");
            return new List<IntroDemoPlacement>();
        }

        return new List<IntroDemoPlacement>
        {
            new IntroDemoPlacement("I_00", "I", new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) }),
            new IntroDemoPlacement("J_00", "J", new[] { new Vector2Int(4, 0), new Vector2Int(5, 0), new Vector2Int(6, 0), new Vector2Int(6, 1) }),
            new IntroDemoPlacement("I_01", "I", new[] { new Vector2Int(7, 0), new Vector2Int(7, 1), new Vector2Int(7, 2), new Vector2Int(7, 3) }),
            new IntroDemoPlacement("O_00", "O", new[] { new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(1, 1), new Vector2Int(1, 2) }),
            new IntroDemoPlacement("L_00", "L", new[] { new Vector2Int(2, 1), new Vector2Int(2, 2), new Vector2Int(3, 1), new Vector2Int(4, 1) }),
            new IntroDemoPlacement("T_00", "T", new[] { new Vector2Int(5, 1), new Vector2Int(5, 2), new Vector2Int(5, 3), new Vector2Int(6, 2) }),
            new IntroDemoPlacement("O_01", "O", new[] { new Vector2Int(3, 2), new Vector2Int(3, 3), new Vector2Int(4, 2), new Vector2Int(4, 3) }),
            new IntroDemoPlacement("L_01", "L", new[] { new Vector2Int(0, 3), new Vector2Int(1, 3), new Vector2Int(1, 4), new Vector2Int(1, 5) }),
            new IntroDemoPlacement("S_00", "S", new[] { new Vector2Int(2, 3), new Vector2Int(2, 4), new Vector2Int(3, 4), new Vector2Int(3, 5) }),
            new IntroDemoPlacement("S_01", "S", new[] { new Vector2Int(6, 3), new Vector2Int(6, 4), new Vector2Int(7, 4), new Vector2Int(7, 5) }),
            new IntroDemoPlacement("I_02", "I", new[] { new Vector2Int(0, 4), new Vector2Int(0, 5), new Vector2Int(0, 6), new Vector2Int(0, 7) }),
            new IntroDemoPlacement("Z_00", "Z", new[] { new Vector2Int(4, 4), new Vector2Int(5, 4), new Vector2Int(5, 5), new Vector2Int(6, 5) }),
            new IntroDemoPlacement("Z_01", "Z", new[] { new Vector2Int(1, 6), new Vector2Int(1, 7), new Vector2Int(2, 5), new Vector2Int(2, 6) }),
            new IntroDemoPlacement("J_01", "J", new[] { new Vector2Int(4, 5), new Vector2Int(4, 6), new Vector2Int(5, 6), new Vector2Int(6, 6) }),
            new IntroDemoPlacement("T_01", "T", new[] { new Vector2Int(2, 7), new Vector2Int(3, 6), new Vector2Int(3, 7), new Vector2Int(4, 7) }),
            new IntroDemoPlacement("L_02", "L", new[] { new Vector2Int(5, 7), new Vector2Int(6, 7), new Vector2Int(7, 6), new Vector2Int(7, 7) })
        };
    }

    private PieceIdentity GetNextBagPieceIdentity()
    {
        EnsureUpcomingPieces(ModernPreviewCount + 1);
        if (pieceBag.Count == 0)
        {
            Debug.LogError("Piece bag is empty. Check your piece prefab assignments.");
            return new PieceIdentity(0);
        }

        int prefabIndex = pieceBag.Dequeue();
        return new PieceIdentity(prefabIndex);
    }

    private void EnsureUpcomingPieces(int requiredCount)
    {
        if (piecePrefabs == null || piecePrefabs.Length == 0)
        {
            Debug.LogError("No piece prefabs assigned to ActivePieceController.");
            return;
        }

        int bagSize = Mathf.Min(piecePrefabs.Length, 7);
        if (!warnedAboutBagSize && piecePrefabs.Length != 7)
        {
            warnedAboutBagSize = true;
            Debug.LogWarning($"7-bag randomizer expects 7 prefabs, but {piecePrefabs.Length} are assigned. Using the first {bagSize} entries.");
        }

        while (pieceBag.Count < requiredCount)
        {
            bagBuffer.Clear();
            for (int i = 0; i < bagSize; i++)
            {
                bagBuffer.Add(i);
            }

            for (int i = bagBuffer.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                (bagBuffer[i], bagBuffer[swapIndex]) = (bagBuffer[swapIndex], bagBuffer[i]);
            }

            foreach (int pieceIndex in bagBuffer)
            {
                pieceBag.Enqueue(pieceIndex);
            }
        }
    }

    private void SpawnPiece(PieceIdentity identity)
    {
        if (piecePrefabs == null || piecePrefabs.Length == 0)
        {
            Debug.LogError("No piece prefabs assigned to ActivePieceController.");
            return;
        }

        int prefabIndex = Mathf.Clamp(identity.PrefabIndex, 0, piecePrefabs.Length - 1);
        GameObject prefab = piecePrefabs[prefabIndex];

        Vector3 spawnPosition = grid.CellToWorldCenter(new Vector3Int(
            grid.Width / 2,
            Mathf.Clamp(grid.Height - spawnHeightOffset, 0, grid.Height - 1),
            grid.Depth / 2
        ));

        activePiece = Instantiate(prefab, spawnPosition, Quaternion.identity);
        activeRotationState = 0;

        PieceTag tag = activePiece.GetComponent<PieceTag>() ?? activePiece.AddComponent<PieceTag>();
        tag.PrefabIndex = prefabIndex;
        CacheActiveRenderers();
        EnsureUpcomingPieces(ModernPreviewCount);

        if (!grid.IsValidPosition(activePiece.transform, activePiece.transform.position, activePiece.transform.rotation))
        {
            GameOver("Spawn position blocked.");
            return;
        }

        ApplyHeldSpawnShift();
        hasSwappedThisDrop = false;
        softDropHeld = false;
        dropTimer = 0f;
        CancelLockDelay();
        BuildGhost();
        BuildDropTrails();
        BuildRotationIndicators();
        UpdateGhost();
        UpdateDropTrails();
        UpdateRotationIndicators();
        NotifyStateChanged();
    }

    private void CacheActiveRenderers()
    {
        activeRenderers.Clear();
        if (activePiece == null) return;

        activeRenderers.AddRange(activePiece.GetComponentsInChildren<Renderer>());
    }

    private void ApplyHeldSpawnShift()
    {
        if (activePiece == null) return;

        bool moveLeftHeld = Input.GetKey(moveLeftKey);
        bool moveRightHeld = Input.GetKey(moveRightKey);

        if (moveLeftHeld == moveRightHeld) return;

        Vector3Int shiftDelta = moveLeftHeld
            ? ResolveViewMoveDelta(ViewMoveDirection.Left)
            : ResolveViewMoveDelta(ViewMoveDirection.Right);

        MoveUntilBlocked(shiftDelta, playerInitiated: false);
    }

    // ---------------------------------------------------------------------
    //  Rotation indicators
    // ---------------------------------------------------------------------

    private void BuildRotationIndicators()
    {
        DestroyRotationIndicators();

        if (!showRotationIndicators || activePiece == null) return;

        rotationIndicatorRoot = new GameObject("RotationIndicators");

        Bounds pieceBounds = GetActivePieceBounds();
        float radius = Mathf.Max(pieceBounds.extents.x, pieceBounds.extents.z) + rotationIndicatorPadding;
        float indicatorHeight = pieceBounds.extents.y + rotationIndicatorHeightOffset;
        float verticalRadius = radius * 0.72f;

        CreateArcIndicator("RotateCCW", counterClockwiseIndicatorColor, indicatorHeight, radius, startDegrees: 210f, endDegrees: 35f);
        CreateArcIndicator("RotateCW", clockwiseIndicatorColor, indicatorHeight, radius, startDegrees: -30f, endDegrees: -205f);
        CreateVerticalArcIndicator("RotateUp", upwardRotationIndicatorColor, indicatorHeight, verticalRadius, startDegrees: 160f, endDegrees: 20f);
        CreateVerticalArcIndicator("RotateDown", downwardRotationIndicatorColor, indicatorHeight, verticalRadius, startDegrees: -20f, endDegrees: -160f);

        CreateKeyLabel(rotateCounterClockwiseKey.ToString(), new Vector3(-radius - 0.25f, indicatorHeight, 0f), counterClockwiseIndicatorColor);
        CreateKeyLabel(rotateClockwiseKey.ToString(), new Vector3(radius + 0.25f, indicatorHeight, 0f), clockwiseIndicatorColor);
        CreateKeyLabel(rotateUpKey.ToString(), new Vector3(0f, indicatorHeight + verticalRadius + 0.25f, 0f), upwardRotationIndicatorColor);
        CreateKeyLabel(rotateDownKey.ToString(), new Vector3(0f, indicatorHeight - verticalRadius - 0.25f, 0f), downwardRotationIndicatorColor);
    }

    private Bounds GetActivePieceBounds()
    {
        if (activeRenderers.Count == 0)
        {
            return new Bounds(activePiece != null ? activePiece.transform.position : Vector3.zero, Vector3.one);
        }

        Bounds bounds = activeRenderers[0].bounds;
        for (int i = 1; i < activeRenderers.Count; i++)
        {
            if (activeRenderers[i] != null)
            {
                bounds.Encapsulate(activeRenderers[i].bounds);
            }
        }

        return bounds;
    }

    private void UpdateRotationIndicators()
    {
        if (rotationIndicatorRoot == null || activePiece == null) return;

        Camera viewCamera = ResolveGameplayCamera();
        if (viewCamera == null) return;

        rotationIndicatorRoot.transform.position = activePiece.transform.position;
        rotationIndicatorRoot.transform.rotation = Quaternion.LookRotation(viewCamera.transform.forward, Vector3.up);

        foreach (TextMesh label in indicatorLabels)
        {
            if (label == null) continue;

            Transform labelTransform = label.transform;
            Vector3 toCamera = labelTransform.position - viewCamera.transform.position;
            if (toCamera.sqrMagnitude > 0.0001f)
            {
                labelTransform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }
        }
    }

    private void DestroyRotationIndicators()
    {
        indicatorLabels.Clear();

        if (rotationIndicatorRoot != null)
        {
            Destroy(rotationIndicatorRoot);
            rotationIndicatorRoot = null;
        }
    }

    private void BuildDropTrails()
    {
        DestroyDropTrails();

        if (!showVerticalDropTrails || activePiece == null) return;

        dropTrailRoot = new GameObject("VerticalDropTrails");
    }

    private void UpdateDropTrails()
    {
        if (dropTrailRoot == null || activePiece == null) return;

        List<Vector3> anchors = GetDropTrailAnchors();
        EnsureDropTrailLineCount(anchors.Count * 2);

        Color trailTint = GetDropTrailTint();

        for (int i = 0; i < anchors.Count; i++)
        {
            Vector3 start = anchors[i] + Vector3.down * 0.04f;
            float supportY = grid != null ? grid.GetSupportSurfaceWorldY(anchors[i]) : start.y - 1f;
            Vector3 end = new Vector3(start.x, supportY + 0.03f, start.z);
            bool visible = start.y - end.y > 0.02f;

            UpdateDropTrailRenderer(dropTrailLines[i * 2], start, end, trailTint, 0.16f, 0.02f, visible);
            UpdateDropTrailRenderer(dropTrailLines[i * 2 + 1], start, end, trailTint, 0.92f, 0.18f, visible);
        }

        for (int i = anchors.Count * 2; i < dropTrailLines.Count; i++)
        {
            if (dropTrailLines[i] != null)
            {
                dropTrailLines[i].enabled = false;
            }
        }
    }

    private void DestroyDropTrails()
    {
        dropTrailLines.Clear();

        if (dropTrailRoot != null)
        {
            Destroy(dropTrailRoot);
            dropTrailRoot = null;
        }
    }

    private List<Vector3> GetDropTrailAnchors()
    {
        List<Vector3> anchors = new List<Vector3>();
        if (activePiece == null) return anchors;

        Vector3Int[] cubeCells = GetActiveCubeCells();
        HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>(cubeCells);
        HashSet<Vector2Int> projectedFootprint = new HashSet<Vector2Int>();
        Dictionary<Vector2Int, int> projectedFootprintMinY = new Dictionary<Vector2Int, int>();

        for (int i = 0; i < cubeCells.Length; i++)
        {
            Vector3Int cell = cubeCells[i];
            if (occupiedCells.Contains(cell + Vector3Int.down)) continue;

            Vector2Int footprintCell = new Vector2Int(cell.x, cell.z);
            projectedFootprint.Add(footprintCell);

            if (!projectedFootprintMinY.TryGetValue(footprintCell, out int minY) || cell.y < minY)
            {
                projectedFootprintMinY[footprintCell] = cell.y;
            }
        }

        float inset = Mathf.Clamp(dropTrailCornerInset, 0f, 0.49f);
        HashSet<long> seenCorners = new HashSet<long>();

        AddFootprintVertexAnchors(projectedFootprint, projectedFootprintMinY, inset, anchors, seenCorners);

        anchors.Sort((a, b) =>
        {
            int xCompare = a.x.CompareTo(b.x);
            if (xCompare != 0) return xCompare;

            int zCompare = a.z.CompareTo(b.z);
            if (zCompare != 0) return zCompare;

            return a.y.CompareTo(b.y);
        });

        return anchors;
    }

    private Vector3Int[] GetActiveCubeCells()
    {
        if (activePiece == null) return Array.Empty<Vector3Int>();

        Vector3Int[] cells = new Vector3Int[activePiece.transform.childCount];
        for (int i = 0; i < activePiece.transform.childCount; i++)
        {
            Vector3 worldPosition = activePiece.transform.GetChild(i).position;
            cells[i] = grid != null
                ? grid.WorldToCell(worldPosition)
                : new Vector3Int(
                    Mathf.RoundToInt(worldPosition.x),
                    Mathf.RoundToInt(worldPosition.y),
                    Mathf.RoundToInt(worldPosition.z));
        }

        return cells;
    }

    private void AddFootprintVertexAnchors(HashSet<Vector2Int> footprint, IReadOnlyDictionary<Vector2Int, int> footprintMinY, float inset, List<Vector3> anchors, HashSet<long> seenCorners)
    {
        Dictionary<Vector2Int, int> cornerMasks = new Dictionary<Vector2Int, int>();

        foreach (Vector2Int cell in footprint)
        {
            if (!footprint.Contains(new Vector2Int(cell.x, cell.y - 1)))
            {
                AddCornerMask(cornerMasks, new Vector2Int(cell.x, cell.y), 1);
                AddCornerMask(cornerMasks, new Vector2Int(cell.x + 1, cell.y), 2);
            }

            if (!footprint.Contains(new Vector2Int(cell.x, cell.y + 1)))
            {
                AddCornerMask(cornerMasks, new Vector2Int(cell.x, cell.y + 1), 1);
                AddCornerMask(cornerMasks, new Vector2Int(cell.x + 1, cell.y + 1), 2);
            }

            if (!footprint.Contains(new Vector2Int(cell.x - 1, cell.y)))
            {
                AddCornerMask(cornerMasks, new Vector2Int(cell.x, cell.y), 4);
                AddCornerMask(cornerMasks, new Vector2Int(cell.x, cell.y + 1), 8);
            }

            if (!footprint.Contains(new Vector2Int(cell.x + 1, cell.y)))
            {
                AddCornerMask(cornerMasks, new Vector2Int(cell.x + 1, cell.y), 4);
                AddCornerMask(cornerMasks, new Vector2Int(cell.x + 1, cell.y + 1), 8);
            }
        }

        foreach (KeyValuePair<Vector2Int, int> entry in cornerMasks)
        {
            int mask = entry.Value;
            bool hasHorizontal = (mask & 3) != 0;
            bool hasVertical = (mask & 12) != 0;
            if (!hasHorizontal || !hasVertical) continue;

            Vector3 anchor = BuildFootprintVertexAnchor(entry.Key, footprint, footprintMinY, inset);
            long key = EncodeDropTrailAnchor(anchor);
            if (!seenCorners.Add(key)) continue;

            anchors.Add(anchor);
        }
    }

    private static long EncodeDropTrailAnchor(Vector3 anchor)
    {
        int x = Mathf.RoundToInt(anchor.x * 1000f);
        int y = Mathf.RoundToInt(anchor.y * 1000f);
        int z = Mathf.RoundToInt(anchor.z * 1000f);
        return (((long)x & 0x1FFFFF) << 42)
            ^ (((long)y & 0x1FFFFF) << 21)
            ^ ((long)z & 0x1FFFFF);
    }

    private static void AddCornerMask(Dictionary<Vector2Int, int> cornerMasks, Vector2Int corner, int mask)
    {
        cornerMasks.TryGetValue(corner, out int existingMask);
        cornerMasks[corner] = existingMask | mask;
    }

    private Vector3 BuildFootprintVertexAnchor(Vector2Int corner, HashSet<Vector2Int> footprint, IReadOnlyDictionary<Vector2Int, int> footprintMinY, float inset)
    {
        Vector2 interiorDirection = Vector2.zero;
        int anchorCellY = int.MaxValue;

        TryAccumulateFootprintCorner(new Vector2Int(corner.x - 1, corner.y - 1), new Vector2(-1f, -1f), footprint, footprintMinY, ref interiorDirection, ref anchorCellY);
        TryAccumulateFootprintCorner(new Vector2Int(corner.x, corner.y - 1), new Vector2(1f, -1f), footprint, footprintMinY, ref interiorDirection, ref anchorCellY);
        TryAccumulateFootprintCorner(new Vector2Int(corner.x - 1, corner.y), new Vector2(-1f, 1f), footprint, footprintMinY, ref interiorDirection, ref anchorCellY);
        TryAccumulateFootprintCorner(new Vector2Int(corner.x, corner.y), new Vector2(1f, 1f), footprint, footprintMinY, ref interiorDirection, ref anchorCellY);

        float insetX = interiorDirection.x == 0f ? 0f : Mathf.Sign(interiorDirection.x) * inset;
        float insetZ = interiorDirection.y == 0f ? 0f : Mathf.Sign(interiorDirection.y) * inset;
        float anchorY = anchorCellY == int.MaxValue ? -0.5f : anchorCellY - 0.5f;

        Vector3 origin = grid != null ? grid.transform.position : Vector3.zero;
        return origin + new Vector3(corner.x - 0.5f + insetX, anchorY, corner.y - 0.5f + insetZ);
    }

    private static void TryAccumulateFootprintCorner(
        Vector2Int footprintCell,
        Vector2 contribution,
        HashSet<Vector2Int> footprint,
        IReadOnlyDictionary<Vector2Int, int> footprintMinY,
        ref Vector2 interiorDirection,
        ref int anchorCellY)
    {
        if (!footprint.Contains(footprintCell)) return;

        interiorDirection += contribution;

        if (footprintMinY.TryGetValue(footprintCell, out int cellY) && cellY < anchorCellY)
        {
            anchorCellY = cellY;
        }
    }

    private void CreateArcIndicator(string name, Color color, float height, float radius, float startDegrees, float endDegrees)
    {
        const int segments = 18;
        Vector3[] points = new Vector3[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius + height, 0f);
        }

        CreateLineRenderer(name + "_Arc", color, 0.04f, points);

        Vector3 end = points[points.Length - 1];
        Vector3 previous = points[points.Length - 2];
        Vector3 direction = (end - previous).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * 0.14f;
        Vector3 arrowBase = end - direction * 0.18f;

        CreateLineRenderer(name + "_Head", color, 0.035f, arrowBase + side, end, arrowBase - side);
    }

    private void CreateVerticalArcIndicator(string name, Color color, float centerHeight, float radius, float startDegrees, float endDegrees)
    {
        const int segments = 18;
        Vector3[] points = new Vector3[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
            points[i] = new Vector3(0f, Mathf.Sin(angle) * radius + centerHeight, Mathf.Cos(angle) * radius * 0.6f);
        }

        CreateLineRenderer(name + "_Arc", color, 0.04f, points);

        Vector3 end = points[points.Length - 1];
        Vector3 previous = points[points.Length - 2];
        Vector3 direction = (end - previous).normalized;
        Vector3 side = Vector3.Cross(Vector3.right, direction).normalized * 0.14f;
        Vector3 arrowBase = end - direction * 0.18f;

        CreateLineRenderer(name + "_Head", color, 0.035f, arrowBase + side, end, arrowBase - side);
    }

    private void CreateKeyLabel(string labelText, Vector3 localPosition, Color color)
    {
        GameObject labelObject = new GameObject($"Key_{labelText}");
        labelObject.transform.SetParent(rotationIndicatorRoot.transform, false);
        labelObject.transform.localPosition = localPosition;

        TextMesh textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.text = labelText;
        textMesh.fontSize = 36;
        textMesh.characterSize = 0.09f;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = color;

        indicatorLabels.Add(textMesh);
    }

    private void CreateDropTrailLine(string name, float width)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(dropTrailRoot.transform, false);

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.material = GetLineMaterial();
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.numCapVertices = 8;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;

        dropTrailLines.Add(lineRenderer);
    }

    private void CreateLineRenderer(string name, Color color, float width, params Vector3[] positions)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(rotationIndicatorRoot.transform, false);

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = false;
        lineRenderer.positionCount = positions.Length;
        lineRenderer.SetPositions(positions);
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.material = GetLineMaterial();
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.numCapVertices = 4;
        lineRenderer.numCornerVertices = 2;
    }

    private void UpdateDropTrailRenderer(LineRenderer lineRenderer, Vector3 start, Vector3 end, Color tint, float startAlphaScale, float endAlphaScale, bool visible)
    {
        if (lineRenderer == null) return;

        lineRenderer.enabled = visible;
        if (!visible) return;

        Color startColor = tint;
        startColor.a *= startAlphaScale;

        Color endColor = tint;
        endColor.a *= endAlphaScale;

        lineRenderer.startColor = startColor;
        lineRenderer.endColor = endColor;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }

    private Color GetDropTrailTint()
    {
        if (activeRenderers.Count == 0) return dropTrailColor;

        Color accumulatedColor = Color.black;
        int sampleCount = 0;

        foreach (Renderer renderer in activeRenderers)
        {
            if (renderer == null) continue;

            accumulatedColor += GetRendererBaseColor(renderer.sharedMaterial);
            sampleCount++;
        }

        if (sampleCount == 0) return dropTrailColor;

        Color averageColor = accumulatedColor * (1f / sampleCount);
        Color tint = Color.Lerp(dropTrailColor, averageColor, 0.45f);
        tint.a = dropTrailColor.a;
        return tint;
    }

    private Material GetLineMaterial()
    {
        if (indicatorLineMaterial != null) return indicatorLineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        indicatorLineMaterial = shader != null ? new Material(shader) : null;
        return indicatorLineMaterial;
    }

    private void EnsureDropTrailLineCount(int requiredCount)
    {
        while (dropTrailLines.Count < requiredCount)
        {
            int trailIndex = dropTrailLines.Count / 2;
            bool isOuter = dropTrailLines.Count % 2 == 0;
            float width = isOuter ? dropTrailOuterWidth : dropTrailInnerWidth;
            string suffix = isOuter ? "Outer" : "Inner";
            CreateDropTrailLine($"DropTrail_{trailIndex}_{suffix}", width);
        }
    }

    private Vector3[] GetActiveCubeWorldPositions()
    {
        if (activePiece == null) return Array.Empty<Vector3>();

        Vector3[] positions = new Vector3[activePiece.transform.childCount];
        for (int i = 0; i < activePiece.transform.childCount; i++)
        {
            positions[i] = activePiece.transform.GetChild(i).position;
        }

        return positions;
    }

    private void SpawnHardDropTrail(IReadOnlyList<Vector3> startPositions, IReadOnlyList<Vector3> endPositions)
    {
        if (startPositions == null || endPositions == null) return;

        int streakCount = Mathf.Min(startPositions.Count, endPositions.Count);
        if (streakCount == 0) return;

        GameObject trailRoot = new GameObject("HardDropTrail");
        List<LineRenderer> streakLines = new List<LineRenderer>(streakCount * 2);

        for (int i = 0; i < streakCount; i++)
        {
            if ((startPositions[i] - endPositions[i]).sqrMagnitude < 0.0001f) continue;

            streakLines.Add(CreateTransientTrailRenderer(trailRoot.transform, $"HardDropTrail_{i}_Outer", hardDropTrailOuterWidth));
            streakLines.Add(CreateTransientTrailRenderer(trailRoot.transform, $"HardDropTrail_{i}_Inner", hardDropTrailInnerWidth));

            UpdateDropTrailRenderer(streakLines[streakLines.Count - 2], startPositions[i], endPositions[i], hardDropTrailColor, 0.18f, 0.02f, visible: true);
            UpdateDropTrailRenderer(streakLines[streakLines.Count - 1], startPositions[i], endPositions[i], hardDropTrailColor, 1f, 0.18f, visible: true);
        }

        if (streakLines.Count == 0)
        {
            Destroy(trailRoot);
            return;
        }

        Destroy(trailRoot, Mathf.Max(0.05f, hardDropTrailDuration + 0.05f));
        StartCoroutine(FadeTransientTrail(trailRoot, streakLines, hardDropTrailDuration));
    }

    private IEnumerator FadeTransientTrail(GameObject trailRoot, IReadOnlyList<LineRenderer> trailLines, float duration)
    {
        float safeDuration = Mathf.Max(0.01f, duration);
        Color[] startColors = new Color[trailLines.Count];
        Color[] endColors = new Color[trailLines.Count];

        for (int i = 0; i < trailLines.Count; i++)
        {
            LineRenderer trailLine = trailLines[i];
            if (trailLine == null) continue;

            startColors[i] = trailLine.startColor;
            endColors[i] = trailLine.endColor;
        }

        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            float alpha = 1f - (elapsed / safeDuration);

            for (int i = 0; i < trailLines.Count; i++)
            {
                LineRenderer trailLine = trailLines[i];
                if (trailLine == null) continue;

                Color startColor = startColors[i];
                startColor.a *= alpha;
                Color endColor = endColors[i];
                endColor.a *= alpha;

                trailLine.startColor = startColor;
                trailLine.endColor = endColor;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (trailRoot != null)
        {
            Destroy(trailRoot);
        }
    }

    private LineRenderer CreateTransientTrailRenderer(Transform parent, string name, float width)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(parent, false);

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.material = GetLineMaterial();
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.numCapVertices = 8;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        return lineRenderer;
    }

    // ---------------------------------------------------------------------
    //  Public progression API
    // ---------------------------------------------------------------------

    public void IncreaseDropSpeed()
    {
        currentDropInterval = Mathf.Max(minDropInterval, currentDropInterval * speedIncreaseFactor);
    }

    public void SetDropInterval(float seconds)
    {
        currentDropInterval = Mathf.Max(minDropInterval, seconds);
    }

    public float CurrentDropInterval => currentDropInterval;
    public float DelayedAutoShiftMilliseconds
    {
        get => delayedAutoShift * 1000f;
        set
        {
            delayedAutoShift = Mathf.Max(0f, value) / 1000f;
            ResetHeldMoveStates();
            NotifyTimingSettingsChanged();
        }
    }

    public float AutoRepeatRateMilliseconds
    {
        get => autoRepeatRate * 1000f;
        set
        {
            autoRepeatRate = Mathf.Max(0f, value) / 1000f;
            ResetHeldMoveStates();
            NotifyTimingSettingsChanged();
        }
    }

    public bool HasHeldPiece => hasHeldPiece;
    public int HeldPiecePrefabIndex => hasHeldPiece ? heldPieceIdentity.PrefabIndex : -1;
    public int PreviewPieceCount => ModernPreviewCount;

    public int[] GetUpcomingPiecePrefabIndices(int count)
    {
        if (count <= 0 || pieceBag.Count == 0)
        {
            return Array.Empty<int>();
        }

        int[] upcoming = new int[Mathf.Min(count, pieceBag.Count)];
        int index = 0;
        foreach (int pieceIndex in pieceBag)
        {
            if (index >= upcoming.Length) break;
            upcoming[index++] = pieceIndex;
        }

        return upcoming;
    }

    public string GetHeldPieceCode()
    {
        return hasHeldPiece ? GetPieceCode(heldPieceIdentity.PrefabIndex) : string.Empty;
    }

    public string[] GetUpcomingPieceCodes(int count)
    {
        int[] indices = GetUpcomingPiecePrefabIndices(count);
        string[] codes = new string[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            codes[i] = GetPieceCode(indices[i]);
        }

        return codes;
    }

    public string GetPieceCode(int prefabIndex)
    {
        if (piecePrefabs == null || prefabIndex < 0 || prefabIndex >= piecePrefabs.Length)
        {
            return string.Empty;
        }

        GameObject prefab = piecePrefabs[prefabIndex];
        if (prefab == null) return string.Empty;

        string prefabName = prefab.name.ToUpperInvariant();

        if (prefabName.Contains("PIECEI")) return "I";
        if (prefabName.Contains("PIECEO")) return "O";
        if (prefabName.Contains("PIECET")) return "T";
        if (prefabName.Contains("PIECES")) return "S";
        if (prefabName.Contains("PIECEZ")) return "Z";
        if (prefabName.Contains("PIECEJ")) return "J";
        if (prefabName.Contains("PIECEL")) return "L";

        return prefab.name;
    }

    private string GetActivePieceCode()
    {
        if (activePiece == null) return string.Empty;

        PieceTag tag = activePiece.GetComponent<PieceTag>();
        return tag != null ? GetPieceCode(tag.PrefabIndex) : string.Empty;
    }

    private static int Mod(int value, int modulus)
    {
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }

    private static Vector2Int[] GetKickOffsets(string pieceCode, int fromState, int toState)
    {
        if (string.Equals(pieceCode, "I", StringComparison.Ordinal))
        {
            return GetIKickOffsets(fromState, toState);
        }

        if (string.Equals(pieceCode, "J", StringComparison.Ordinal)
            || string.Equals(pieceCode, "L", StringComparison.Ordinal)
            || string.Equals(pieceCode, "S", StringComparison.Ordinal)
            || string.Equals(pieceCode, "T", StringComparison.Ordinal)
            || string.Equals(pieceCode, "Z", StringComparison.Ordinal))
        {
            return GetJlstzKickOffsets(fromState, toState);
        }

        return GetFallbackKickOffsets();
    }

    private static Vector2Int[] GetJlstzKickOffsets(int fromState, int toState)
    {
        if (fromState == 0 && toState == 1)
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(-1, 1),
                new Vector2Int(0, -2),
                new Vector2Int(-1, -2)
            };
        }

        if ((fromState == 1 && toState == 0) || (fromState == 1 && toState == 2))
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(1, -1),
                new Vector2Int(0, 2),
                new Vector2Int(1, 2)
            };
        }

        if (fromState == 2 && toState == 1)
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(-1, 1),
                new Vector2Int(0, -2),
                new Vector2Int(-1, -2)
            };
        }

        if ((fromState == 2 && toState == 3) || (fromState == 0 && toState == 3))
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(1, 1),
                new Vector2Int(0, -2),
                new Vector2Int(1, -2)
            };
        }

        if ((fromState == 3 && toState == 2) || (fromState == 3 && toState == 0))
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(-1, -1),
                new Vector2Int(0, 2),
                new Vector2Int(-1, 2)
            };
        }

        return GetFallbackKickOffsets();
    }

    private static Vector2Int[] GetIKickOffsets(int fromState, int toState)
    {
        if ((fromState == 0 && toState == 1) || (fromState == 3 && toState == 2))
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(-2, 0),
                new Vector2Int(1, 0),
                new Vector2Int(-2, -1),
                new Vector2Int(1, 2)
            };
        }

        if ((fromState == 1 && toState == 0) || (fromState == 2 && toState == 3))
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(2, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(2, 1),
                new Vector2Int(-1, -2)
            };
        }

        if ((fromState == 1 && toState == 2) || (fromState == 0 && toState == 3))
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(2, 0),
                new Vector2Int(-1, 2),
                new Vector2Int(2, -1)
            };
        }

        if ((fromState == 2 && toState == 1) || (fromState == 3 && toState == 0))
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(-2, 0),
                new Vector2Int(1, -2),
                new Vector2Int(-2, 1)
            };
        }

        return GetFallbackKickOffsets();
    }

    private static Vector2Int[] GetFallbackKickOffsets()
    {
        return new[]
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    private void NotifyTimingSettingsChanged()
    {
        TimingSettingsChanged?.Invoke();
        NotifyStateChanged();
    }

    // ---------------------------------------------------------------------
    //  Game over
    // ---------------------------------------------------------------------

    private void GameOver()
    {
        GameOver("A piece locked above the top boundary.");
    }

    private void GameOver(string reason)
    {
        if (postLockRoutine != null)
        {
            StopCoroutine(postLockRoutine);
            postLockRoutine = null;
        }

        CancelLockDelay();
        DestroyGhost();
        DestroyDropTrails();
        DestroyRotationIndicators();
        Vector3 explosionOrigin = grid != null
            ? grid.Center
            : (activePiece != null ? activePiece.transform.position : transform.position);
        List<Transform> gameOverFragments = CollectGameOverFragments();

        activeRotationState = 0;

        activeRenderers.Clear();

        if (gameMaster != null)
        {
            gameMaster.PlayGameOverExplosion(reason, explosionOrigin, orbitCamera, gameOverFragments);
        }
        else
        {
            Debug.Log($"Game Over: {reason}");

            for (int i = 0; i < gameOverFragments.Count; i++)
            {
                if (gameOverFragments[i] != null)
                {
                    Destroy(gameOverFragments[i].gameObject);
                }
            }
        }

        NotifyStateChanged();
        enabled = false;
    }

    private List<Transform> CollectGameOverFragments()
    {
        List<Transform> fragments = new List<Transform>();

        if (grid != null)
        {
            fragments.AddRange(grid.ReleaseLockedCubes());
        }

        if (activePiece != null)
        {
            while (activePiece.transform.childCount > 0)
            {
                Transform cube = activePiece.transform.GetChild(0);
                cube.SetParent(null, worldPositionStays: true);
                fragments.Add(cube);
            }

            Destroy(activePiece);
            activePiece = null;
        }

        return fragments;
    }

    private enum ViewMoveDirection
    {
        Left,
        Right,
        Forward,
        Back
    }

    private readonly struct PieceIdentity
    {
        public PieceIdentity(int prefabIndex)
        {
            PrefabIndex = prefabIndex;
        }

        public int PrefabIndex { get; }
    }

    private sealed class HeldMoveState
    {
        public HeldMoveState(KeyCode key, ViewMoveDirection direction)
        {
            Key = key;
            Direction = direction;
        }

        public KeyCode Key { get; }
        public ViewMoveDirection Direction { get; }
        public bool WaitingForAutoShift { get; set; }
        public float NextRepeatTime { get; set; }

        public void Reset()
        {
            WaitingForAutoShift = false;
            NextRepeatTime = 0f;
        }
    }

    private readonly struct IntroDemoPlacement
    {
        public IntroDemoPlacement(string id, string pieceCode, Vector2Int[] cells)
        {
            Id = id;
            PieceCode = pieceCode;
            Cells = cells;
        }

        public string Id { get; }
        public string PieceCode { get; }
        public Vector2Int[] Cells { get; }
    }
}

/// <summary>
/// Tiny tag component so the piece remembers which prefab it came from.
/// Used by the hold/swap system.
/// </summary>
public class PieceTag : MonoBehaviour
{
    public int PrefabIndex;
}
