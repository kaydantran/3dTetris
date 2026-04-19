using System;
using UnityEngine;

/// <summary>
/// Simple progression and game-state coordinator. It tracks cleared layers for score/speed
/// and exposes a placeholder game-over entry point for the piece controller.
/// </summary>
public class GameMaster : MonoBehaviour
{
    [SerializeField] private ActivePieceController pieceController;

    [Tooltip("How many layers must be cleared before the drop speed increases.")]
    [SerializeField] private int layersPerSpeedBump = 3;

    private int totalLayersCleared;
    private int layersSinceLastBump;
    private int totalPiecesLocked;
    private int score;
    private bool isGameOver;
    private float gameplayStartTime = -1f;
    private float finalPiecesPerSecond;

    public int Score => score;
    public int TotalLayersCleared => totalLayersCleared;
    public int TotalPiecesLocked => totalPiecesLocked;
    public float PiecesPerSecond => isGameOver ? finalPiecesPerSecond : CalculatePiecesPerSecond();
    public bool IsGameOver => isGameOver;

    public event Action StatsChanged;

    private void Awake()
    {
        if (pieceController == null)
        {
            pieceController = FindAnyObjectByType<ActivePieceController>();
        }

        if (FindAnyObjectByType<GameplayHudController>() == null)
        {
            new GameObject("GameplayHUD").AddComponent<GameplayHudController>();
        }
    }

    public void OnGameplayStarted()
    {
        if (gameplayStartTime >= 0f) return;

        gameplayStartTime = Time.time;
        NotifyStatsChanged();
    }

    public void OnPieceLocked()
    {
        if (isGameOver) return;

        if (gameplayStartTime < 0f)
        {
            gameplayStartTime = Time.time;
        }

        totalPiecesLocked++;
        NotifyStatsChanged();
    }

    public void OnLayersCleared(int count)
    {
        if (count <= 0 || isGameOver) return;

        totalLayersCleared += count;
        layersSinceLastBump += count;
        score += count * count * 100;

        while (layersSinceLastBump >= layersPerSpeedBump)
        {
            layersSinceLastBump -= layersPerSpeedBump;
            if (pieceController != null)
            {
                pieceController.IncreaseDropSpeed();
            }
        }

        NotifyStatsChanged();
    }

    public void OnGameOver(string reason)
    {
        if (isGameOver) return;

        finalPiecesPerSecond = CalculatePiecesPerSecond();
        isGameOver = true;
        NotifyStatsChanged();
        Debug.Log($"Game Over: {reason}");
    }

    private float CalculatePiecesPerSecond()
    {
        if (gameplayStartTime < 0f) return 0f;

        float elapsed = Mathf.Max(0.001f, Time.time - gameplayStartTime);
        return totalPiecesLocked / elapsed;
    }

    private void NotifyStatsChanged()
    {
        StatsChanged?.Invoke();
    }
}
