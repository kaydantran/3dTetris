using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TetrisGrid is the central authority for the 3D play area.
/// It tracks every locked cube, validates prospective positions for the active piece,
/// detects and clears completed layers, applies post-clear gravity, and exposes a small API
/// that other systems use to interact with the world.
/// </summary>
public class TetrisGrid : MonoBehaviour
{
    [Header("Grid Dimensions")]
    [Tooltip("Width of the play area in cells (X axis).")]
    [SerializeField] private int width = 8;
    [Tooltip("Depth of the play area in cells (Z axis).")]
    [SerializeField] private int depth = 8;
    [Tooltip("Height of the play area in cells (Y axis). Pieces spawn near the top.")]
    [SerializeField] private int height = 16;

    [Header("Top Boundary")]
    [Tooltip("Highest safe occupied row. Locking any block above this row triggers GameOver. -1 defaults to height - 2.")]
    [SerializeField] private int topBoundaryY = -1;

    [Header("Runtime Boundary Visuals")]
    [Tooltip("Draws the playfield boundary and top-out line in the Game view at runtime.")]
    [SerializeField] private bool showRuntimeBoundaryVisuals = true;
    [Tooltip("Optional camera used to decide which grid side is currently facing the player.")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private OrbitCamera orbitCamera;
    [SerializeField] private Color boundaryColor = new Color(0.25f, 0.75f, 1f, 0.75f);
    [SerializeField] private Color floorGridColor = new Color(0.25f, 0.75f, 1f, 0.18f);
    [SerializeField] private Color topBoundaryColor = new Color(1f, 0.35f, 0.35f, 0.95f);
    [SerializeField] private float boundaryLineWidth = 0.05f;

    [Header("Scene Gizmos")]
    [Tooltip("If true, draws the grid bounds in the Scene view.")]
    [SerializeField] private bool drawGizmos = true;

    [Header("Layer Clear Animation")]
    [SerializeField] private float comboGravityFallSpeed = 14f;
    [SerializeField] private float clearPause = 0.05f;
    [SerializeField] private float settlePause = 0.03f;

    private Transform[,,] cells;
    private GameObject boundaryVisualRoot;
    private Material runtimeLineMaterial;
    private bool runtimeBoundaryVisualsDirty;
    private readonly Dictionary<BoundaryFace, GameObject> boundaryFaceRoots = new Dictionary<BoundaryFace, GameObject>();
    private BoundaryFaceSet activeBoundaryFaces = BoundaryFaceSet.None;

    public int Width => width;
    public int Depth => depth;
    public int Height => height;
    public int TopBoundaryY => Mathf.Clamp(topBoundaryY < 0 ? Mathf.Max(0, height - 2) : topBoundaryY, 0, height - 1);

    /// <summary>Center of the grid in world space. Useful for the orbit camera target.</summary>
    public Vector3 Center => transform.position + new Vector3((width - 1) * 0.5f, (height - 1) * 0.5f, (depth - 1) * 0.5f);

    private void Awake()
    {
        EnsureCellArray();
        ResolveGameplayCamera();
        runtimeBoundaryVisualsDirty = true;
    }

    private void Start()
    {
        RefreshRuntimeBoundaryVisualsIfNeeded();
    }

    private void LateUpdate()
    {
        RefreshRuntimeBoundaryVisualsIfNeeded();
        UpdateRuntimeBoundaryFaceVisibility();
    }

    private void OnDestroy()
    {
        if (boundaryVisualRoot != null)
        {
            Destroy(boundaryVisualRoot);
        }

        if (runtimeLineMaterial != null)
        {
            Destroy(runtimeLineMaterial);
        }
    }

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        depth = Mathf.Max(1, depth);
        height = Mathf.Max(1, height);

        if (Application.isPlaying)
        {
            EnsureCellArray();
            ResolveGameplayCamera();
            runtimeBoundaryVisualsDirty = true;
        }
    }

    private void RefreshRuntimeBoundaryVisualsIfNeeded()
    {
        if (!runtimeBoundaryVisualsDirty) return;

        runtimeBoundaryVisualsDirty = false;
        RebuildRuntimeBoundaryVisuals();
    }

    private void EnsureCellArray()
    {
        if (cells == null
            || cells.GetLength(0) != width
            || cells.GetLength(1) != height
            || cells.GetLength(2) != depth)
        {
            cells = new Transform[width, height, depth];
        }
    }

    // ---------------------------------------------------------------------
    //  Cell coordinate helpers
    // ---------------------------------------------------------------------

    public Vector3Int WorldToCell(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        return new Vector3Int(
            Mathf.RoundToInt(local.x),
            Mathf.RoundToInt(local.y),
            Mathf.RoundToInt(local.z)
        );
    }

    public Vector3 CellToWorldCenter(Vector3Int cell)
    {
        return transform.position + new Vector3(cell.x, cell.y, cell.z);
    }

    public bool IsInsideBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < width
            && cell.y >= 0 && cell.y < height
            && cell.z >= 0 && cell.z < depth;
    }

    public bool IsCellFree(Vector3Int cell)
    {
        if (!IsInsideBounds(cell)) return false;
        return cells[cell.x, cell.y, cell.z] == null;
    }

    public bool IsAboveTopBoundary(Vector3Int cell)
    {
        return cell.y > TopBoundaryY;
    }

    public float GetTopBoundaryWorldY()
    {
        return transform.position.y + TopBoundaryY + 0.5f;
    }

    public float GetSupportSurfaceWorldY(Vector3 worldPosition)
    {
        if (width <= 0 || depth <= 0)
        {
            return transform.position.y - 0.5f;
        }

        Vector3Int cell = WorldToCell(worldPosition);
        int x = Mathf.Clamp(cell.x, 0, width - 1);
        int z = Mathf.Clamp(cell.z, 0, depth - 1);

        for (int y = height - 1; y >= 0; y--)
        {
            if (cells[x, y, z] != null)
            {
                return transform.position.y + y + 0.5f;
            }
        }

        return transform.position.y - 0.5f;
    }

    // ---------------------------------------------------------------------
    //  Public API — piece validation & locking
    // ---------------------------------------------------------------------

    public bool IsValidPosition(Transform pieceRoot, Vector3 piecePosition, Quaternion pieceRotation)
    {
        for (int i = 0; i < pieceRoot.childCount; i++)
        {
            Transform cube = pieceRoot.GetChild(i);
            Vector3 worldCubePos = piecePosition + pieceRotation * cube.localPosition;
            Vector3Int cell = WorldToCell(worldCubePos);
            if (!IsCellFree(cell)) return false;
        }
        return true;
    }

    public GridLockResult LockPiece(Transform pieceRoot)
    {
        GridLockResult result = new GridLockResult
        {
            TouchedLayers = new HashSet<int>(),
            CrossedTopBoundary = false
        };

        List<Transform> cubes = new List<Transform>(pieceRoot.childCount);
        for (int i = 0; i < pieceRoot.childCount; i++)
        {
            cubes.Add(pieceRoot.GetChild(i));
        }

        foreach (Transform cube in cubes)
        {
            Vector3Int cell = WorldToCell(cube.position);
            if (!IsInsideBounds(cell))
            {
                Debug.LogWarning($"Locking cube out of bounds at {cell}; destroying.");
                Destroy(cube.gameObject);
                continue;
            }

            cube.SetParent(transform, worldPositionStays: true);
            cube.position = CellToWorldCenter(cell);

            cells[cell.x, cell.y, cell.z] = cube;
            result.TouchedLayers.Add(cell.y);

            if (IsAboveTopBoundary(cell))
            {
                result.CrossedTopBoundary = true;
            }
        }

        Destroy(pieceRoot.gameObject);
        return result;
    }

    public bool TryRegisterLockedCube(Transform cube, Vector3Int cell)
    {
        if (cube == null || !IsInsideBounds(cell) || !IsCellFree(cell))
        {
            return false;
        }

        EnsureCellArray();

        cube.SetParent(transform, worldPositionStays: true);
        cube.position = CellToWorldCenter(cell);
        cells[cell.x, cell.y, cell.z] = cube;
        return true;
    }

    public List<Transform> ReleaseLockedCubes()
    {
        EnsureCellArray();

        List<Transform> releasedCubes = new List<Transform>();
        HashSet<Transform> seenCubes = new HashSet<Transform>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    Transform cube = cells[x, y, z];
                    if (cube == null) continue;

                    if (seenCubes.Add(cube))
                    {
                        cube.SetParent(null, worldPositionStays: true);
                        releasedCubes.Add(cube);
                    }

                    cells[x, y, z] = null;
                }
            }
        }

        return releasedCubes;
    }

    // ---------------------------------------------------------------------
    //  Layer clearing
    // ---------------------------------------------------------------------

    /// <summary>
    /// Clears every full horizontal plane and keeps collapsing columns downward until no
    /// more full planes remain. This removes "cheese" gaps because each column is packed
    /// to the floor after a clear.
    /// </summary>
    public int CheckAndClearLayers(IEnumerable<int> candidateLayers)
    {
        int totalCleared = 0;

        while (true)
        {
            List<int> fullLayers = GetFullLayers();
            if (fullLayers.Count == 0)
            {
                return totalCleared;
            }

            foreach (int y in fullLayers)
            {
                ClearLayer(y);
            }

            List<CollapseMove> moves = CollapseColumnsToFloor();
            for (int i = 0; i < moves.Count; i++)
            {
                CollapseMove move = moves[i];
                if (move.Cube != null)
                {
                    move.Cube.position = move.EndPosition;
                }
            }

            totalCleared += fullLayers.Count;
        }
    }

    public IEnumerator CheckAndClearLayersAnimated(IEnumerable<int> candidateLayers, Action<int> onLayersCleared = null)
    {
        while (true)
        {
            List<int> fullLayers = GetFullLayers();
            if (fullLayers.Count == 0)
            {
                yield break;
            }

            foreach (int y in fullLayers)
            {
                ClearLayer(y);
            }

            onLayersCleared?.Invoke(fullLayers.Count);

            if (clearPause > 0f)
            {
                yield return new WaitForSeconds(clearPause);
            }
            else
            {
                yield return null;
            }

            yield return AnimateCollapseColumnsToFloor();

            if (settlePause > 0f)
            {
                yield return new WaitForSeconds(settlePause);
            }
        }
    }

    private List<int> GetFullLayers()
    {
        List<int> fullLayers = new List<int>();
        for (int y = 0; y < height; y++)
        {
            if (IsLayerFull(y))
            {
                fullLayers.Add(y);
            }
        }

        return fullLayers;
    }

    private bool IsLayerFull(int y)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (cells[x, y, z] == null) return false;
            }
        }

        return true;
    }

    private void ClearLayer(int y)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (cells[x, y, z] != null)
                {
                    Destroy(cells[x, y, z].gameObject);
                    cells[x, y, z] = null;
                }
            }
        }
    }

    private IEnumerator AnimateCollapseColumnsToFloor()
    {
        List<CollapseMove> moves = CollapseColumnsToFloor();
        if (moves.Count == 0) yield break;

        float safeFallSpeed = Mathf.Max(0.01f, comboGravityFallSpeed);

        while (true)
        {
            bool anyMoving = false;
            float step = safeFallSpeed * Time.deltaTime;

            for (int i = 0; i < moves.Count; i++)
            {
                CollapseMove move = moves[i];
                if (move.Cube == null) continue;

                move.Cube.position = Vector3.MoveTowards(move.Cube.position, move.EndPosition, step);
                if ((move.Cube.position - move.EndPosition).sqrMagnitude > 0.0001f)
                {
                    anyMoving = true;
                }
            }

            if (!anyMoving)
            {
                break;
            }

            yield return null;
        }

        for (int i = 0; i < moves.Count; i++)
        {
            CollapseMove move = moves[i];
            if (move.Cube != null)
            {
                move.Cube.position = move.EndPosition;
            }
        }
    }

    private List<CollapseMove> CollapseColumnsToFloor()
    {
        List<CollapseMove> moves = new List<CollapseMove>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                int writeY = 0;

                for (int readY = 0; readY < height; readY++)
                {
                    Transform cube = cells[x, readY, z];
                    if (cube == null) continue;

                    if (writeY != readY)
                    {
                        cells[x, writeY, z] = cube;
                        cells[x, readY, z] = null;
                        moves.Add(new CollapseMove(cube, CellToWorldCenter(new Vector3Int(x, writeY, z))));
                    }

                    writeY++;
                }

                for (int clearY = writeY; clearY < height; clearY++)
                {
                    cells[x, clearY, z] = null;
                }
            }
        }

        return moves;
    }

    // ---------------------------------------------------------------------
    //  Runtime visuals
    // ---------------------------------------------------------------------

    private void RebuildRuntimeBoundaryVisuals()
    {
        boundaryFaceRoots.Clear();
        activeBoundaryFaces = BoundaryFaceSet.None;

        if (boundaryVisualRoot != null)
        {
            Destroy(boundaryVisualRoot);
            boundaryVisualRoot = null;
        }

        if (!showRuntimeBoundaryVisuals) return;

        boundaryVisualRoot = new GameObject("GridBoundaryVisuals");
        boundaryVisualRoot.transform.SetParent(transform, false);

        Vector3 min = new Vector3(-0.5f, -0.5f, -0.5f);
        Vector3 max = new Vector3(width - 0.5f, height - 0.5f, depth - 0.5f);

        for (int x = 0; x <= width; x++)
        {
            CreateLine($"FloorX_{x}",
                new Vector3(x - 0.5f, -0.5f, -0.5f),
                new Vector3(x - 0.5f, -0.5f, depth - 0.5f),
                floorGridColor,
                boundaryLineWidth * 0.5f);
        }

        for (int z = 0; z <= depth; z++)
        {
            CreateLine($"FloorZ_{z}",
                new Vector3(-0.5f, -0.5f, z - 0.5f),
                new Vector3(width - 0.5f, -0.5f, z - 0.5f),
                floorGridColor,
                boundaryLineWidth * 0.5f);
        }

        float topLineY = TopBoundaryY + 0.5f;
        CreateFaceVisuals(
            BoundaryFace.Front,
            ("BottomFront", min, new Vector3(max.x, min.y, min.z), boundaryColor, boundaryLineWidth),
            ("TopFront", new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), boundaryColor, boundaryLineWidth),
            ("VerticalFL", min, new Vector3(min.x, max.y, min.z), boundaryColor, boundaryLineWidth),
            ("VerticalFR", new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), boundaryColor, boundaryLineWidth),
            ("TopBoundaryFront", new Vector3(min.x, topLineY, min.z), new Vector3(max.x, topLineY, min.z), topBoundaryColor, boundaryLineWidth * 1.2f));

        CreateFaceVisuals(
            BoundaryFace.Back,
            ("BottomBack", new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), boundaryColor, boundaryLineWidth),
            ("TopBack", new Vector3(min.x, max.y, max.z), max, boundaryColor, boundaryLineWidth),
            ("VerticalBL", new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), boundaryColor, boundaryLineWidth),
            ("VerticalBR", max, new Vector3(max.x, min.y, max.z), boundaryColor, boundaryLineWidth),
            ("TopBoundaryBack", new Vector3(min.x, topLineY, max.z), new Vector3(max.x, topLineY, max.z), topBoundaryColor, boundaryLineWidth * 1.2f));

        CreateFaceVisuals(
            BoundaryFace.Left,
            ("BottomLeft", min, new Vector3(min.x, min.y, max.z), boundaryColor, boundaryLineWidth),
            ("TopLeft", new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), boundaryColor, boundaryLineWidth),
            ("VerticalFL", min, new Vector3(min.x, max.y, min.z), boundaryColor, boundaryLineWidth),
            ("VerticalBL", new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), boundaryColor, boundaryLineWidth),
            ("TopBoundaryLeft", new Vector3(min.x, topLineY, min.z), new Vector3(min.x, topLineY, max.z), topBoundaryColor, boundaryLineWidth * 1.2f));

        CreateFaceVisuals(
            BoundaryFace.Right,
            ("BottomRight", new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), boundaryColor, boundaryLineWidth),
            ("TopRight", new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), boundaryColor, boundaryLineWidth),
            ("VerticalFR", new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), boundaryColor, boundaryLineWidth),
            ("VerticalBR", max, new Vector3(max.x, min.y, max.z), boundaryColor, boundaryLineWidth),
            ("TopBoundaryRight", new Vector3(max.x, topLineY, min.z), new Vector3(max.x, topLineY, max.z), topBoundaryColor, boundaryLineWidth * 1.2f));

        UpdateRuntimeBoundaryFaceVisibility(forceRefresh: true);
    }

    private void CreateLine(string lineName, Vector3 start, Vector3 end, Color color, float widthValue)
    {
        CreateLine(boundaryVisualRoot.transform, lineName, start, end, color, widthValue);
    }

    private void CreateLine(Transform parent, string lineName, Vector3 start, Vector3 end, Color color, float widthValue)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(parent, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = widthValue;
        line.endWidth = widthValue;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.material = GetLineMaterial();
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    private void CreateFaceVisuals(
        BoundaryFace face,
        params (string Name, Vector3 Start, Vector3 End, Color Color, float Width)[] lines)
    {
        if (boundaryVisualRoot == null)
        {
            return;
        }

        GameObject faceRoot = new GameObject(face + "Face");
        faceRoot.transform.SetParent(boundaryVisualRoot.transform, false);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            CreateLine(faceRoot.transform, line.Name, line.Start, line.End, line.Color, line.Width);
        }

        boundaryFaceRoots[face] = faceRoot;
    }

    private void UpdateRuntimeBoundaryFaceVisibility(bool forceRefresh = false)
    {
        if (boundaryVisualRoot == null || boundaryFaceRoots.Count == 0)
        {
            return;
        }

        BoundaryFaceSet viewedFaces = ResolveViewedBoundaryFaces();
        if (!forceRefresh && viewedFaces == activeBoundaryFaces)
        {
            return;
        }

        activeBoundaryFaces = viewedFaces;
        foreach (KeyValuePair<BoundaryFace, GameObject> pair in boundaryFaceRoots)
        {
            if (pair.Value == null)
            {
                continue;
            }

            pair.Value.SetActive((viewedFaces & ToFaceSet(pair.Key)) != 0);
        }
    }

    private BoundaryFaceSet ResolveViewedBoundaryFaces()
    {
        Camera viewCamera = ResolveGameplayCamera();
        if (viewCamera == null)
        {
            return BoundaryFaceSet.None;
        }

        Vector3 forward = viewCamera.transform.forward.normalized;
        if (Mathf.Abs(forward.y) >= 0.88f)
        {
            return BoundaryFaceSet.None;
        }

        Vector3 planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
        {
            return BoundaryFaceSet.None;
        }

        planarForward.Normalize();
        BoundaryFaceSet visibleFaces = BoundaryFaceSet.None;
        const float diagonalThreshold = 0.2f;

        if (Mathf.Abs(planarForward.x) >= diagonalThreshold)
        {
            // Show the face farthest from the camera along the X axis.
            visibleFaces |= planarForward.x >= 0f ? BoundaryFaceSet.Right : BoundaryFaceSet.Left;
        }

        if (Mathf.Abs(planarForward.z) >= diagonalThreshold)
        {
            // Show the face farthest from the camera along the Z axis.
            visibleFaces |= planarForward.z >= 0f ? BoundaryFaceSet.Back : BoundaryFaceSet.Front;
        }

        if (visibleFaces != BoundaryFaceSet.None)
        {
            return visibleFaces;
        }

        if (Mathf.Abs(planarForward.x) > Mathf.Abs(planarForward.z))
        {
            return planarForward.x >= 0f ? BoundaryFaceSet.Right : BoundaryFaceSet.Left;
        }

        return planarForward.z >= 0f ? BoundaryFaceSet.Back : BoundaryFaceSet.Front;
    }

    private Camera ResolveGameplayCamera()
    {
        if (gameplayCamera != null)
        {
            return gameplayCamera;
        }

        if (orbitCamera == null)
        {
            orbitCamera = FindAnyObjectByType<OrbitCamera>();
        }

        gameplayCamera = orbitCamera != null ? orbitCamera.GetComponent<Camera>() : Camera.main;
        return gameplayCamera;
    }

    private Material GetLineMaterial()
    {
        if (runtimeLineMaterial != null) return runtimeLineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        runtimeLineMaterial = shader != null ? new Material(shader) : null;
        return runtimeLineMaterial;
    }

    private enum BoundaryFace
    {
        Front,
        Back,
        Left,
        Right
    }

    [Flags]
    private enum BoundaryFaceSet
    {
        None = 0,
        Front = 1 << 0,
        Back = 1 << 1,
        Left = 1 << 2,
        Right = 1 << 3
    }

    private static BoundaryFaceSet ToFaceSet(BoundaryFace face)
    {
        return face switch
        {
            BoundaryFace.Front => BoundaryFaceSet.Front,
            BoundaryFace.Back => BoundaryFaceSet.Back,
            BoundaryFace.Left => BoundaryFaceSet.Left,
            BoundaryFace.Right => BoundaryFaceSet.Right,
            _ => BoundaryFaceSet.None
        };
    }

    // ---------------------------------------------------------------------
    //  Gizmos
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Vector3 size = new Vector3(width, height, depth);
        Vector3 center = transform.position + new Vector3((width - 1) * 0.5f, (height - 1) * 0.5f, (depth - 1) * 0.5f);

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(center, size);

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.2f);
        Vector3 floorMin = transform.position + new Vector3(-0.5f, -0.5f, -0.5f);
        for (int x = 0; x <= width; x++)
        {
            Gizmos.DrawLine(floorMin + new Vector3(x, 0f, 0f), floorMin + new Vector3(x, 0f, depth));
        }

        for (int z = 0; z <= depth; z++)
        {
            Gizmos.DrawLine(floorMin + new Vector3(0f, 0f, z), floorMin + new Vector3(width, 0f, z));
        }

        Gizmos.color = topBoundaryColor;
        float topLineY = TopBoundaryY + 0.5f;
        Vector3 topMin = transform.position + new Vector3(-0.5f, topLineY, -0.5f);
        Vector3 topMax = transform.position + new Vector3(width - 0.5f, topLineY, depth - 0.5f);
        Gizmos.DrawLine(new Vector3(topMin.x, topLineY, topMin.z), new Vector3(topMax.x, topLineY, topMin.z));
        Gizmos.DrawLine(new Vector3(topMin.x, topLineY, topMax.z), new Vector3(topMax.x, topLineY, topMax.z));
        Gizmos.DrawLine(new Vector3(topMin.x, topLineY, topMin.z), new Vector3(topMin.x, topLineY, topMax.z));
        Gizmos.DrawLine(new Vector3(topMax.x, topLineY, topMin.z), new Vector3(topMax.x, topLineY, topMax.z));
    }
}

public struct GridLockResult
{
    public HashSet<int> TouchedLayers;
    public bool CrossedTopBoundary;
}

public readonly struct CollapseMove
{
    public CollapseMove(Transform cube, Vector3 endPosition)
    {
        Cube = cube;
        EndPosition = endPosition;
    }

    public Transform Cube { get; }
    public Vector3 EndPosition { get; }
}
