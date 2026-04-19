using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the currently falling piece: input, gravity, rotation, ghost preview,
/// lock delay with speed-scaled flashing, and the hold/swap mechanic.
///
/// This controller owns the "active piece" GameObject at any given moment. The piece
/// itself is a plain prefab: a root Transform with child cubes laid out in local space
/// to describe its shape. See the setup notes at the bottom of this response.
/// </summary>
public class ActivePieceController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TetrisGrid grid;

    [Header("Piece Prefabs & Colors")]
    [Tooltip("Array of piece prefab variants (e.g. L, T, I, S, Z, O, custom shapes).")]
    [SerializeField] private GameObject[] piecePrefabs;
    [Tooltip("Materials that will be randomly assigned to newly spawned pieces.")]
    [SerializeField] private Material[] pieceMaterials;

    [Header("Ghost Piece")]
    [Tooltip("Material applied to the ghost silhouette (should be transparent).")]
    [SerializeField] private Material ghostMaterial;

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
    [Tooltip("How far below the top of the grid (in cells) new pieces spawn. 2 leaves headroom for tall pieces.")]
    [SerializeField] private int spawnHeightOffset = 2;

    // --- Runtime state ---
    private GameObject activePiece;          // The piece the player controls
    private GameObject ghostPiece;           // Transparent silhouette showing landing spot
    private GameObject heldPiece;            // The piece in the hold slot (cloned, not active)
    private int heldPiecePrefabIndex = -1;   // Index into piecePrefabs for the held piece
    private bool hasSwappedThisDrop;         // Prevents holding twice in one drop

    private float currentDropInterval;       // Current gravity speed (seconds per cell)
    private float dropTimer;                 // Accumulates until >= currentDropInterval
    private bool softDropHeld;

    private bool isLocking;                  // True while the lock-delay coroutine is running
    private float lockTimer;                 // Remaining time until forced lock
    private int lockResetCount;              // Counts player-initiated resets of the lock timer
    private Coroutine flashRoutine;

    // Cached renderers of the active piece for flashing effects.
    private readonly List<Renderer> activeRenderers = new List<Renderer>();

    private void Start()
    {
        currentDropInterval = initialDropInterval;
        SpawnNewPiece();
    }

    private void Update()
    {
        if (activePiece == null) return;

        HandleInput();
        HandleGravity();
        UpdateGhost();
    }

    // ---------------------------------------------------------------------
    //  Input
    // ---------------------------------------------------------------------

    private void HandleInput()
    {
        // Horizontal movement (snaps to grid).
        if (Input.GetKeyDown(KeyCode.LeftArrow))  TryMove(new Vector3(-1, 0,  0));
        if (Input.GetKeyDown(KeyCode.RightArrow)) TryMove(new Vector3( 1, 0,  0));
        if (Input.GetKeyDown(KeyCode.UpArrow))    TryMove(new Vector3( 0, 0,  1));
        if (Input.GetKeyDown(KeyCode.DownArrow))  TryMove(new Vector3( 0, 0, -1));

        // Rotations. We rotate around the piece's LOCAL axes so rotation chains intuitively
        // after previous rotations — rotating world-axis can feel disorienting in 3D.
        if (Input.GetKeyDown(KeyCode.E)) TryRotate(Vector3.right);   // X-axis
        if (Input.GetKeyDown(KeyCode.R)) TryRotate(Vector3.forward); // Z-axis

        // Soft drop — distinct from DownArrow (which is a sideways move). Using LeftShift.
        softDropHeld = Input.GetKey(KeyCode.LeftShift);

        // Hard drop.
        if (Input.GetKeyDown(KeyCode.Space)) HardDrop();

        // Hold / swap.
        if (Input.GetKeyDown(KeyCode.C)) TryHold();
    }

    // ---------------------------------------------------------------------
    //  Movement & rotation
    // ---------------------------------------------------------------------

    private bool TryMove(Vector3 delta)
    {
        Vector3 newPos = activePiece.transform.position + delta;
        if (grid.IsValidPosition(activePiece.transform, newPos, activePiece.transform.rotation))
        {
            activePiece.transform.position = newPos;
            OnPieceMovedOrRotated();
            return true;
        }
        return false;
    }

    private bool TryRotate(Vector3 localAxis)
    {
        // Build the prospective rotation by composing with the current world rotation.
        Quaternion delta = Quaternion.AngleAxis(90f, activePiece.transform.TransformDirection(localAxis));
        Quaternion newRot = delta * activePiece.transform.rotation;

        if (grid.IsValidPosition(activePiece.transform, activePiece.transform.position, newRot))
        {
            activePiece.transform.rotation = newRot;
            OnPieceMovedOrRotated();
            return true;
        }

        // Simple wall-kick: try nudging up to 1 cell in each cardinal direction before giving up.
        // For 3D this covers common edge cases where a rotation clips a wall or tall stack.
        Vector3[] kicks = {
            Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up
        };
        foreach (Vector3 kick in kicks)
        {
            Vector3 testPos = activePiece.transform.position + kick;
            if (grid.IsValidPosition(activePiece.transform, testPos, newRot))
            {
                activePiece.transform.position = testPos;
                activePiece.transform.rotation = newRot;
                OnPieceMovedOrRotated();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Called whenever the player successfully moves or rotates. If the piece is currently
    /// in its lock-delay window, this resets the timer (up to maxLockResets times) so
    /// players can slide/spin at the last moment — a staple of modern Tetris feel.
    /// </summary>
    private void OnPieceMovedOrRotated()
    {
        if (!isLocking) return;
        if (lockResetCount >= maxLockResets) return;

        lockResetCount++;
        lockTimer = GetEffectiveLockDelay();

        // If the piece is now floating again (moved off the ground), cancel the lock entirely.
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
        if (dropTimer >= interval)
        {
            dropTimer = 0f;
            StepDown();
        }
    }

    private void StepDown()
    {
        Vector3 newPos = activePiece.transform.position + Vector3.down;
        if (grid.IsValidPosition(activePiece.transform, newPos, activePiece.transform.rotation))
        {
            activePiece.transform.position = newPos;

            // If we were locking but gravity pushed us into a valid lower cell somehow
            // (edge case after layer clears, etc.), cancel lock.
            if (isLocking && !IsTouchingGround()) CancelLockDelay();
        }
        else
        {
            // Can't move down — begin (or continue) lock delay.
            if (!isLocking) BeginLockDelay();
        }
    }

    private bool IsTouchingGround()
    {
        Vector3 below = activePiece.transform.position + Vector3.down;
        return !grid.IsValidPosition(activePiece.transform, below, activePiece.transform.rotation);
    }

    /// <summary>
    /// Lock delay scales with drop speed: faster gravity → shorter grace period.
    /// This keeps late-game feel responsive.
    /// </summary>
    private float GetEffectiveLockDelay()
    {
        float speedRatio = currentDropInterval / initialDropInterval; // 1 at start, →0 as speed grows
        return Mathf.Max(0.05f, baseLockDelay * speedRatio);
    }

    private void BeginLockDelay()
    {
        isLocking = true;
        lockTimer = GetEffectiveLockDelay();
        lockResetCount = 0;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashAndLock());
    }

    private void CancelLockDelay()
    {
        isLocking = false;
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
        RestoreRendererColors();
    }

    /// <summary>
    /// Flashes the piece while running the lock timer down. Flash frequency scales
    /// with drop speed so faster games feel visually more urgent.
    /// </summary>
    private IEnumerator FlashAndLock()
    {
        // Cache original emission/color per renderer so we can restore on cancel.
        CacheRendererColors();

        // Faster gravity → faster flash. Flashes-per-second range ~ 2 to 12.
        float speedRatio = Mathf.Clamp01(currentDropInterval / initialDropInterval);
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

            // If something cancelled the lock mid-flash (e.g. player slid off edge), exit.
            if (!isLocking) yield break;
        }

        // Timer expired — lock the piece.
        RestoreRendererColors();
        LockPieceNow();
    }

    private readonly List<Color> originalColors = new List<Color>();
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorID     = Shader.PropertyToID("_BaseColor");

    private void CacheRendererColors()
    {
        originalColors.Clear();
        foreach (Renderer r in activeRenderers)
        {
            // Use a MaterialPropertyBlock for flashing so we don't mutate the shared material asset.
            originalColors.Add(r.material.HasProperty(BaseColorID) ? r.material.GetColor(BaseColorID)
                                                                   : r.material.color);
        }
    }

    private void SetFlashState(bool on)
    {
        for (int i = 0; i < activeRenderers.Count; i++)
        {
            Renderer r = activeRenderers[i];
            Color target = on ? Color.white : originalColors[i];
            if (r.material.HasProperty(BaseColorID)) r.material.SetColor(BaseColorID, target);
            else r.material.color = target;
        }
    }

    private void RestoreRendererColors()
    {
        for (int i = 0; i < activeRenderers.Count && i < originalColors.Count; i++)
        {
            Renderer r = activeRenderers[i];
            if (r == null) continue;
            if (r.material.HasProperty(BaseColorID)) r.material.SetColor(BaseColorID, originalColors[i]);
            else r.material.color = originalColors[i];
        }
    }

    private void LockPieceNow()
    {
        isLocking = false;
        flashRoutine = null;

        HashSet<int> touched = grid.LockPiece(activePiece.transform);
        activePiece = null;
        activeRenderers.Clear();

        int cleared = grid.CheckAndClearLayers(touched);
        // A GameMaster could listen for an event here; for now, just log.
        if (cleared > 0) Debug.Log($"Cleared {cleared} layer(s).");

        SpawnNewPiece();
    }

    // ---------------------------------------------------------------------
    //  Hard drop
    // ---------------------------------------------------------------------

    private void HardDrop()
    {
        while (grid.IsValidPosition(activePiece.transform,
                                    activePiece.transform.position + Vector3.down,
                                    activePiece.transform.rotation))
        {
            activePiece.transform.position += Vector3.down;
        }

        // Hard drop bypasses lock delay entirely.
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        RestoreRendererColors();
        LockPieceNow();
    }

    // ---------------------------------------------------------------------
    //  Ghost piece
    // ---------------------------------------------------------------------

    private void UpdateGhost()
    {
        if (ghostPiece == null) return;

        ghostPiece.transform.rotation = activePiece.transform.rotation;
        Vector3 projected = activePiece.transform.position;
        while (grid.IsValidPosition(activePiece.transform,
                                    projected + Vector3.down,
                                    activePiece.transform.rotation))
        {
            projected += Vector3.down;
        }
        ghostPiece.transform.position = projected;
    }

    // ---------------------------------------------------------------------
    //  Hold / swap
    // ---------------------------------------------------------------------

    private void TryHold()
    {
        if (hasSwappedThisDrop) return;
        hasSwappedThisDrop = true;

        // Determine current piece's prefab index (stored on spawn via a lightweight tag component).
        PieceTag tag = activePiece.GetComponent<PieceTag>();
        int currentIndex = tag != null ? tag.PrefabIndex : 0;

        // Clean up the currently active piece entirely — we'll respawn from prefab.
        Destroy(activePiece);
        if (ghostPiece != null) Destroy(ghostPiece);
        CancelLockDelay();

        if (heldPiecePrefabIndex < 0)
        {
            // First hold: stash current, spawn fresh.
            heldPiecePrefabIndex = currentIndex;
            SpawnNewPiece(forcePrefabIndex: -1);
        }
        else
        {
            // Swap: spawn the held, stash the current.
            int toSpawn = heldPiecePrefabIndex;
            heldPiecePrefabIndex = currentIndex;
            SpawnNewPiece(forcePrefabIndex: toSpawn);
        }
    }

    // ---------------------------------------------------------------------
    //  Spawning
    // ---------------------------------------------------------------------

    private void SpawnNewPiece(int forcePrefabIndex = -1)
    {
        if (piecePrefabs == null || piecePrefabs.Length == 0)
        {
            Debug.LogError("No piece prefabs assigned to ActivePieceController.");
            return;
        }

        int prefabIndex = forcePrefabIndex >= 0 ? forcePrefabIndex
                                                : Random.Range(0, piecePrefabs.Length);
        GameObject prefab = piecePrefabs[prefabIndex];

        // Spawn roughly centered horizontally, near the top vertically.
        // With our integer-position convention, no half-cell offset is needed.
        Vector3 spawnPos = grid.CellToWorldCenter(new Vector3Int(
            grid.Width  / 2,
            grid.Height - spawnHeightOffset,
            grid.Depth  / 2
        ));

        activePiece = Instantiate(prefab, spawnPos, Quaternion.identity);

        // Tag with prefab index (for hold swap).
        PieceTag tag = activePiece.GetComponent<PieceTag>() ?? activePiece.AddComponent<PieceTag>();
        tag.PrefabIndex = prefabIndex;

        // Assign random material to all child renderers (except respect per-prefab override if needed).
        AssignRandomMaterial(activePiece);
        CacheActiveRenderers();

        // If the freshly spawned piece is already invalid (grid is full near spawn) — game over.
        if (!grid.IsValidPosition(activePiece.transform, activePiece.transform.position, activePiece.transform.rotation))
        {
            Debug.Log("Game Over — spawn position blocked.");
            Destroy(activePiece);
            activePiece = null;
            enabled = false;
            return;
        }

        // Reset per-drop state and (re)build the ghost.
        hasSwappedThisDrop = false;
        dropTimer = 0f;
        CancelLockDelay();
        BuildGhost();
    }

    private void AssignRandomMaterial(GameObject piece)
    {
        if (pieceMaterials == null || pieceMaterials.Length == 0) return;
        Material mat = pieceMaterials[Random.Range(0, pieceMaterials.Length)];
        foreach (Renderer r in piece.GetComponentsInChildren<Renderer>())
        {
            r.material = mat;
        }
    }

    private void CacheActiveRenderers()
    {
        activeRenderers.Clear();
        activeRenderers.AddRange(activePiece.GetComponentsInChildren<Renderer>());
    }

    private void BuildGhost()
    {
        // Clone the active piece, strip its colliders (if any), and apply the ghost material.
        ghostPiece = Instantiate(activePiece, activePiece.transform.position, activePiece.transform.rotation);
        ghostPiece.name = "Ghost";

        // Remove any scripts the active piece might carry so the ghost is purely visual.
        foreach (MonoBehaviour mb in ghostPiece.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
        foreach (Collider c in ghostPiece.GetComponentsInChildren<Collider>()) Destroy(c);

        if (ghostMaterial != null)
        {
            foreach (Renderer r in ghostPiece.GetComponentsInChildren<Renderer>())
            {
                r.material = ghostMaterial;
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Public progression API
    // ---------------------------------------------------------------------

    /// <summary>
    /// Permanently increases the base drop speed of all future (and the current) piece.
    /// Intended to be called by a GameMaster when layers are cleared, difficulty thresholds
    /// are met, etc. Each call multiplies the interval by <see cref="speedIncreaseFactor"/>.
    /// </summary>
    public void IncreaseDropSpeed()
    {
        currentDropInterval = Mathf.Max(minDropInterval, currentDropInterval * speedIncreaseFactor);
    }

    /// <summary>Direct setter if the GameMaster wants tight control.</summary>
    public void SetDropInterval(float seconds)
    {
        currentDropInterval = Mathf.Max(minDropInterval, seconds);
    }

    public float CurrentDropInterval => currentDropInterval;
}

/// <summary>
/// Tiny tag component so the piece remembers which prefab index it came from.
/// Used by the hold/swap system to spawn the right prefab when unstashing.
/// </summary>
public class PieceTag : MonoBehaviour
{
    public int PrefabIndex;
}
