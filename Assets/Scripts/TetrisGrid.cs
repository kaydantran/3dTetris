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
    [SerializeField] private int width = 7;
    [Tooltip("Depth of the play area in cells (Z axis).")]
    [SerializeField] private int depth = 7;
    [Tooltip("Height of the play area in cells (Y axis). Pieces spawn near the top.")]
    [SerializeField] private int height = 10;

    [Header("Top Boundary")]
    [Tooltip("Highest safe occupied row. Locking any block above this row triggers GameOver. -1 defaults to height - 2.")]
    [SerializeField] private int topBoundaryY = -1;

    [Header("Runtime Boundary Visuals")]
    [Tooltip("Draws the playfield boundary and top-out line in the Game view at runtime.")]
    [SerializeField] private bool showRuntimeBoundaryVisuals = true;
    [SerializeField] private Color boundaryColor = new Color(0.25f, 0.75f, 1f, 0.75f);
    [SerializeField] private Color floorGridColor = new Color(0.25f, 0.75f, 1f, 0.18f);
    [SerializeField] private Color topBoundaryColor = new Color(1f, 0.35f, 0.35f, 0.95f);
    [SerializeField] private float boundaryLineWidth = 0.05f;

    [Header("Scene Gizmos")]
    [Tooltip("If true, draws the grid bounds in the Scene view.")]
    [SerializeField] private bool drawGizmos = true;

    private Transform[,,] cells;
    private GameObject boundaryVisualRoot;
    private Material runtimeLineMaterial;

    public int Width => width;
    public int Depth => depth;
    public int Height => height;
    public int TopBoundaryY => Mathf.Clamp(topBoundaryY < 0 ? Mathf.Max(0, height - 2) : topBoundaryY, 0, height - 1);

    /// <summary>Center of the grid in world space. Useful for the orbit camera target.</summary>
    public Vector3 Center => transform.position + new Vector3((width - 1) * 0.5f, (height - 1) * 0.5f, (depth - 1) * 0.5f);

    private void Awake()
    {
        EnsureCellArray();
        RebuildRuntimeBoundaryVisuals();
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
            RebuildRuntimeBoundaryVisuals();
        }
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

            CollapseColumnsToFloor();
            totalCleared += fullLayers.Count;
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

    private void CollapseColumnsToFloor()
    {
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
                        cube.position = CellToWorldCenter(new Vector3Int(x, writeY, z));
                    }

                    writeY++;
                }

                for (int clearY = writeY; clearY < height; clearY++)
                {
                    cells[x, clearY, z] = null;
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Runtime visuals
    // ---------------------------------------------------------------------

    private void RebuildRuntimeBoundaryVisuals()
    {
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

        CreateLine("BottomFront", min, new Vector3(max.x, min.y, min.z), boundaryColor, boundaryLineWidth);
        CreateLine("BottomBack", new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), boundaryColor, boundaryLineWidth);
        CreateLine("BottomLeft", min, new Vector3(min.x, min.y, max.z), boundaryColor, boundaryLineWidth);
        CreateLine("BottomRight", new Vector3(max.x, min.y, min.z), max, boundaryColor, boundaryLineWidth);

        CreateLine("TopFront", new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), boundaryColor, boundaryLineWidth);
        CreateLine("TopBack", new Vector3(min.x, max.y, max.z), max, boundaryColor, boundaryLineWidth);
        CreateLine("TopLeft", new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), boundaryColor, boundaryLineWidth);
        CreateLine("TopRight", new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), boundaryColor, boundaryLineWidth);

        CreateLine("VerticalFL", min, new Vector3(min.x, max.y, min.z), boundaryColor, boundaryLineWidth);
        CreateLine("VerticalFR", new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), boundaryColor, boundaryLineWidth);
        CreateLine("VerticalBL", new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), boundaryColor, boundaryLineWidth);
        CreateLine("VerticalBR", max, new Vector3(max.x, min.y, max.z), boundaryColor, boundaryLineWidth);

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
        CreateLine("TopBoundaryFront", new Vector3(-0.5f, topLineY, -0.5f), new Vector3(width - 0.5f, topLineY, -0.5f), topBoundaryColor, boundaryLineWidth * 1.2f);
        CreateLine("TopBoundaryBack", new Vector3(-0.5f, topLineY, depth - 0.5f), new Vector3(width - 0.5f, topLineY, depth - 0.5f), topBoundaryColor, boundaryLineWidth * 1.2f);
        CreateLine("TopBoundaryLeft", new Vector3(-0.5f, topLineY, -0.5f), new Vector3(-0.5f, topLineY, depth - 0.5f), topBoundaryColor, boundaryLineWidth * 1.2f);
        CreateLine("TopBoundaryRight", new Vector3(width - 0.5f, topLineY, -0.5f), new Vector3(width - 0.5f, topLineY, depth - 0.5f), topBoundaryColor, boundaryLineWidth * 1.2f);
    }

    private void CreateLine(string lineName, Vector3 start, Vector3 end, Color color, float widthValue)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(boundaryVisualRoot.transform, false);

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
