using UnityEngine;

/// <summary>
/// Carries the last finished run's results across scene loads.
/// </summary>
public static class GameRunSummary
{
    public struct SummaryData
    {
        public int Score;
        public int TotalLayersCleared;
        public int TotalPiecesLocked;
        public float ElapsedGameplayTime;
        public float PiecesPerSecond;
        public int TotalTetrises;
        public int MaxCombo;
        public string GameOverReason;
    }

    public static bool HasSummary { get; private set; }
    public static SummaryData Current { get; private set; }

    public static void Save(SummaryData summary)
    {
        Current = summary;
        HasSummary = true;
    }

    public static void Clear()
    {
        Current = default;
        HasSummary = false;
    }
}
