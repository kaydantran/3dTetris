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

    [Header("Movement Feel")]
    [Tooltip("How long a held move key waits before it starts repeating.")]
    public float delayedAutoShift = 0.16f;
    [Tooltip("How quickly a held move key repeats after DAS. Set to 0 for instant wall slide.")]
    public float autoRepeatRate = 0.05f;

    [Header("Drop Speed")]
    [Tooltip("Seconds between automatic downward steps at the start of the game.")]
    [SerializeField] private float initialDropInterval = 1.0f;
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

    [Header("Control Keys")]
    [SerializeField] private KeyCode moveLeftKey = KeyCode.A;
    [SerializeField] private KeyCode moveRightKey = KeyCode.D;
    [SerializeField] private KeyCode moveInwardKey = KeyCode.S;
    [SerializeField] private KeyCode moveOutwardKey = KeyCode.W;
    [SerializeField] private KeyCode holdKey = KeyCode.Q;
    [SerializeField] private KeyCode rotateCounterClockwiseKey = KeyCode.J;
    [SerializeField] private KeyCode rotateClockwiseKey = KeyCode.K;
    [SerializeField] private KeyCode softDropKey = KeyCode.RightShift;
    [SerializeField] private KeyCode elevateViewKey = KeyCode.U;
    [SerializeField] private KeyCode flattenViewKey = KeyCode.N;

    [Header("Rotation Indicators")]
    [SerializeField] private bool showRotationIndicators = true;
    [SerializeField] private Color counterClockwiseIndicatorColor = new Color(1f, 0.55f, 0.35f, 0.95f);
    [SerializeField] private Color clockwiseIndicatorColor = new Color(0.35f, 0.9f, 1f, 0.95f);
    [SerializeField] private float rotationIndicatorHeightOffset = 0.6f;
    [SerializeField] private float rotationIndicatorPadding = 0.45f;

    private GameObject activePiece;
    private GameObject ghostPiece;
    private GameObject rotationIndicatorRoot;

    private bool hasHeldPiece;
    private PieceIdentity heldPieceIdentity;
    private bool hasSwappedThisDrop;

    private readonly Queue<int> pieceBag = new Queue<int>();
    private readonly List<int> bagBuffer = new List<int>(7);
    private bool warnedAboutBagSize;

    private float currentDropInterval;
    private float dropTimer;
    private bool softDropHeld;

    private bool isLocking;
    private float lockTimer;
    private int lockResetCount;
    private Coroutine flashRoutine;

    private readonly List<Renderer> activeRenderers = new List<Renderer>();
    private readonly List<TextMesh> indicatorLabels = new List<TextMesh>();
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
        UpdateRotationIndicators();
    }

    private void OnDisable()
    {
        CancelLockDelay();
        DestroyGhost();
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

        if (Input.GetKeyDown(KeyCode.Space))
        {
            HardDrop();
            return;
        }

        HandleRepeatingMovement();
    }

    private bool HandleCameraModifierInput()
    {
        if (!IsSoftDropHeld()) return false;
        if (orbitCamera == null) return false;

        if (Input.GetKeyDown(rotateCounterClockwiseKey))
        {
            orbitCamera.RotateToLeftFace();
            return true;
        }

        if (Input.GetKeyDown(rotateClockwiseKey))
        {
            orbitCamera.RotateToRightFace();
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

    private void MoveUntilBlocked(Vector3Int delta)
    {
        while (TryMove(delta))
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

        Quaternion delta = Quaternion.AngleAxis(angle, worldAxis);
        Quaternion newRotation = delta * activePiece.transform.rotation;

        if (grid.IsValidPosition(activePiece.transform, activePiece.transform.position, newRotation))
        {
            activePiece.transform.rotation = newRotation;
            OnPieceMovedOrRotated();
            return true;
        }

        Vector3Int[] kicks =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.forward,
            Vector3Int.back,
            Vector3Int.up
        };

        foreach (Vector3Int kick in kicks)
        {
            Vector3 testPosition = activePiece.transform.position + new Vector3(kick.x, kick.y, kick.z);
            if (!grid.IsValidPosition(activePiece.transform, testPosition, newRotation)) continue;

            activePiece.transform.position = testPosition;
            activePiece.transform.rotation = newRotation;
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
        activePiece = null;
        activeRenderers.Clear();
        DestroyGhost();
        DestroyRotationIndicators();

        if (gameMaster != null)
        {
            gameMaster.OnPieceLocked();
        }

        int cleared = grid.CheckAndClearLayers(lockResult.TouchedLayers);
        if (cleared > 0 && gameMaster != null)
        {
            gameMaster.OnLayersCleared(cleared);
        }

        if (lockResult.CrossedTopBoundary)
        {
            GameOver();
            return;
        }

        SpawnNextBagPiece();
    }

    // ---------------------------------------------------------------------
    //  Hard drop
    // ---------------------------------------------------------------------

    private void HardDrop()
    {
        if (activePiece == null) return;

        while (grid.IsValidPosition(activePiece.transform, activePiece.transform.position + Vector3.down, activePiece.transform.rotation))
        {
            activePiece.transform.position += Vector3.down;
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

        if (ghostMaterial != null)
        {
            foreach (Renderer renderer in ghostPiece.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = ghostMaterial;
                renderer.SetPropertyBlock(null);
            }
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
                int swapIndex = Random.Range(0, i + 1);
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

        PieceTag tag = activePiece.GetComponent<PieceTag>() ?? activePiece.AddComponent<PieceTag>();
        tag.PrefabIndex = prefabIndex;
        CacheActiveRenderers();
        EnsureUpcomingPieces(ModernPreviewCount);

        if (!grid.IsValidPosition(activePiece.transform, activePiece.transform.position, activePiece.transform.rotation))
        {
            GameOver("Spawn position blocked.");
            return;
        }

        hasSwappedThisDrop = false;
        softDropHeld = false;
        dropTimer = 0f;
        CancelLockDelay();
        BuildGhost();
        BuildRotationIndicators();
        UpdateGhost();
        UpdateRotationIndicators();
        NotifyStateChanged();
    }

    private void CacheActiveRenderers()
    {
        activeRenderers.Clear();
        if (activePiece == null) return;

        activeRenderers.AddRange(activePiece.GetComponentsInChildren<Renderer>());
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

        CreateArcIndicator("RotateCCW", counterClockwiseIndicatorColor, indicatorHeight, radius, startDegrees: 210f, endDegrees: 35f);
        CreateArcIndicator("RotateCW", clockwiseIndicatorColor, indicatorHeight, radius, startDegrees: -30f, endDegrees: -205f);

        CreateKeyLabel(rotateCounterClockwiseKey.ToString(), new Vector3(-radius - 0.25f, indicatorHeight, 0f), counterClockwiseIndicatorColor);
        CreateKeyLabel(rotateClockwiseKey.ToString(), new Vector3(radius + 0.25f, indicatorHeight, 0f), clockwiseIndicatorColor);
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
        CancelLockDelay();
        DestroyGhost();
        DestroyRotationIndicators();

        if (activePiece != null)
        {
            Destroy(activePiece);
            activePiece = null;
        }

        activeRenderers.Clear();

        if (gameMaster != null)
        {
            gameMaster.OnGameOver(reason);
        }
        else
        {
            Debug.Log($"Game Over: {reason}");
        }

        NotifyStateChanged();
        enabled = false;
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
}

/// <summary>
/// Tiny tag component so the piece remembers which prefab it came from.
/// Used by the hold/swap system.
/// </summary>
public class PieceTag : MonoBehaviour
{
    public int PrefabIndex;
}
