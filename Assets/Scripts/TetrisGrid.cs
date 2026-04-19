using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TetrisGrid is the central authority for the 3D play area.
/// It tracks every locked cube, validates prospective positions for the active piece,
/// detects and clears completed layers, and exposes a small API that other systems
/// (piece controller, game master) use to interact with the world.
///
/// Coordinate system (IMPORTANT — match this in your prefabs):
///   - Each cell is 1 unit cubed.
///   - Cube meshes have their pivot at the CUBE CENTER (Unity's default cube primitive is perfect).
///   - A cube whose WORLD position is (x, y, z) occupies the cell with integer index (x, y, z).
///     i.e. integer positions = cell centers. No half-offsets anywhere.
///   - The piece root is positioned at integer coordinates; child cubes sit at integer
///     LOCAL offsets like (0,0,0), (1,0,0), (0,1,0), etc.
///   - The grid origin (transform.position) is the center of the (0,0,0) cell.
///   - X is width, Y is height (up), Z is depth.
///
/// Attach this to an empty GameObject. It is the source of truth for "is this cell free".
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

    [Header("Visuals (optional)")]
    [Tooltip("If true, draws the grid bounds in the Scene view.")]
    [SerializeField] private bool drawGizmos = true;

    // The occupancy grid. A non-null Transform means the cell is occupied by a locked cube.
    // Storing the Transform (rather than just a bool) lets us move/destroy cubes when
    // layers clear, without needing to maintain a parallel data structure.
    private Transform[,,] cells;

    public int Width  => width;
    public int Depth  => depth;
    public int Height => height;

    /// <summary>Center of the grid in world space. Useful for the orbit camera target.</summary>
    public Vector3 Center => transform.position + new Vector3((width - 1) * 0.5f, (height - 1) * 0.5f, (depth - 1) * 0.5f);

    private void Awake()
    {
        cells = new Transform[width, height, depth];
    }

    // ---------------------------------------------------------------------
    //  Cell coordinate helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Converts a world-space cube position (a cube center) into its integer cell coordinate.
    /// Uses RoundToInt because in our convention integer positions *are* cell centers, and
    /// rotated children may land on positions like (3.0000001, 5, 2) due to float error.
    /// </summary>
    public Vector3Int WorldToCell(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        return new Vector3Int(
            Mathf.RoundToInt(local.x),
            Mathf.RoundToInt(local.y),
            Mathf.RoundToInt(local.z)
        );
    }

    /// <summary>Converts an integer cell coordinate to that cell's world-space center.</summary>
    public Vector3 CellToWorldCenter(Vector3Int cell)
    {
        return transform.position + new Vector3(cell.x, cell.y, cell.z);
    }

    /// <summary>True if the integer cell coordinate is inside the grid bounds.</summary>
    public bool IsInsideBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < width
            && cell.y >= 0 && cell.y < height
            && cell.z >= 0 && cell.z < depth;
    }

    /// <summary>True if the cell is inside the grid AND not occupied by a locked cube.</summary>
    public bool IsCellFree(Vector3Int cell)
    {
        if (!IsInsideBounds(cell)) return false;
        return cells[cell.x, cell.y, cell.z] == null;
    }

    // ---------------------------------------------------------------------
    //  Public API — piece validation & locking
    // ---------------------------------------------------------------------

    /// <summary>
    /// Tests whether the piece at <paramref name="piecePosition"/> with the given rotation
    /// would occupy only valid cells (inside bounds, not clipping into locked cubes).
    ///
    /// The method uses the piece's child cube transforms as the shape definition: whatever
    /// cubes are childed to the piece root — at whatever local offsets — are tested. This
    /// keeps the grid agnostic to the specific tetromino shapes and supports arbitrary
    /// custom blocks.
    /// </summary>
    /// <param name="pieceRoot">The root Transform of the piece (its children are the cubes).</param>
    /// <param name="piecePosition">Prospective world position of the piece root.</param>
    /// <param name="pieceRotation">Prospective world rotation of the piece root.</param>
    public bool IsValidPosition(Transform pieceRoot, Vector3 piecePosition, Quaternion pieceRotation)
    {
        // Iterate every child cube and compute where it would land under the prospective
        // position/rotation. We reconstruct the child's would-be world position manually
        // instead of actually moving the piece, so we don't disturb the scene mid-check.
        for (int i = 0; i < pieceRoot.childCount; i++)
        {
            Transform cube = pieceRoot.GetChild(i);
            Vector3 worldCubePos = piecePosition + pieceRotation * cube.localPosition;
            Vector3Int cell = WorldToCell(worldCubePos);
            if (!IsCellFree(cell)) return false;
        }
        return true;
    }

    /// <summary>
    /// Registers all child cubes of <paramref name="pieceRoot"/> as locked in the grid.
    /// The piece root GameObject itself is destroyed; individual cubes are re-parented
    /// so they can be moved independently when layers clear.
    /// Returns the set of Y-layers that received new cubes (caller should check these for clears).
    /// </summary>
    public HashSet<int> LockPiece(Transform pieceRoot)
    {
        HashSet<int> touchedLayers = new HashSet<int>();

        // Copy children to a buffer before reparenting — modifying the child list mid-loop is unsafe.
        List<Transform> cubes = new List<Transform>(pieceRoot.childCount);
        for (int i = 0; i < pieceRoot.childCount; i++) cubes.Add(pieceRoot.GetChild(i));

        foreach (Transform cube in cubes)
        {
            Vector3Int cell = WorldToCell(cube.position);
            if (!IsInsideBounds(cell))
            {
                // Should never happen if IsValidPosition was respected, but guard against it.
                Debug.LogWarning($"Locking cube out of bounds at {cell}; destroying.");
                Destroy(cube.gameObject);
                continue;
            }

            // Snap cube to the exact center of its cell so later layer-shifting is clean.
            cube.SetParent(transform, worldPositionStays: true);
            cube.position = CellToWorldCenter(cell);

            cells[cell.x, cell.y, cell.z] = cube;
            touchedLayers.Add(cell.y);
        }

        Destroy(pieceRoot.gameObject);
        return touchedLayers;
    }

    // ---------------------------------------------------------------------
    //  Layer clearing
    // ---------------------------------------------------------------------

    /// <summary>
    /// Checks the given Y layers (and, after clearing, everything above) for completed
    /// horizontal slabs. A layer is "complete" when every (x,z) cell in it is occupied.
    /// Returns the number of layers cleared so the GameMaster can score / increase speed.
    /// </summary>
    public int CheckAndClearLayers(IEnumerable<int> candidateLayers)
    {
        // Sort descending so when we clear and shift down, upper layers don't get processed
        // before the shift resolves their new indices.
        List<int> sorted = new List<int>(candidateLayers);
        sorted.Sort((a, b) => b.CompareTo(a));

        int cleared = 0;
        foreach (int y in sorted)
        {
            if (y < 0 || y >= height) continue;
            if (IsLayerFull(y))
            {
                ClearLayer(y);
                ShiftLayersDown(y);
                cleared++;
            }
        }
        return cleared;
    }

    private bool IsLayerFull(int y)
    {
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                if (cells[x, y, z] == null) return false;
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

    /// <summary>After clearing layer <paramref name="clearedY"/>, drop every layer above it by one.</summary>
    private void ShiftLayersDown(int clearedY)
    {
        for (int y = clearedY + 1; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    Transform cube = cells[x, y, z];
                    if (cube == null) continue;

                    cells[x, y - 1, z] = cube;
                    cells[x, y, z] = null;
                    cube.position = CellToWorldCenter(new Vector3Int(x, y - 1, z));
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Gizmos
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Under the center-based convention, cell (0,0,0) center is at transform.position,
        // so the wireframe bounding box spans from -0.5 to (N-1)+0.5 on each axis.
        Vector3 size   = new Vector3(width, height, depth);
        Vector3 center = transform.position + new Vector3((width - 1) * 0.5f, (height - 1) * 0.5f, (depth - 1) * 0.5f);

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(center, size);

        // Floor grid lines
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.2f);
        Vector3 floorMin = transform.position + new Vector3(-0.5f, -0.5f, -0.5f);
        for (int x = 0; x <= width; x++)
            Gizmos.DrawLine(floorMin + new Vector3(x, 0, 0),
                            floorMin + new Vector3(x, 0, depth));
        for (int z = 0; z <= depth; z++)
            Gizmos.DrawLine(floorMin + new Vector3(0, 0, z),
                            floorMin + new Vector3(width, 0, z));
    }
}
