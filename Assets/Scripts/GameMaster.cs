using UnityEngine;

/// <summary>
/// Minimal example of a GameMaster that raises difficulty as the player clears layers.
/// You can replace or expand this freely — it exists to show how to call the
/// ActivePieceController.IncreaseDropSpeed() progression API.
///
/// Hook this up by subscribing to your own "layers cleared" event from TetrisGrid,
/// or by polling, or by raising an event from ActivePieceController when it calls
/// CheckAndClearLayers. The sample below uses a simple public method you can call
/// from anywhere.
/// </summary>
public class GameMaster : MonoBehaviour
{
    [SerializeField] private ActivePieceController pieceController;

    [Tooltip("How many layers must be cleared before the drop speed increases.")]
    [SerializeField] private int layersPerSpeedBump = 3;

    private int totalLayersCleared;
    private int layersSinceLastBump;
    private int score;

    public int Score => score;
    public int TotalLayersCleared => totalLayersCleared;

    /// <summary>Call this whenever the grid reports cleared layers.</summary>
    public void OnLayersCleared(int count)
    {
        if (count <= 0) return;

        totalLayersCleared += count;
        layersSinceLastBump += count;

        // Classic-style quadratic scoring: clearing several layers at once pays bonus points.
        score += count * count * 100;

        while (layersSinceLastBump >= layersPerSpeedBump)
        {
            layersSinceLastBump -= layersPerSpeedBump;
            if (pieceController != null) pieceController.IncreaseDropSpeed();
        }
    }
}
