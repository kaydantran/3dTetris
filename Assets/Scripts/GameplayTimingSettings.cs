using UnityEngine;

/// <summary>
/// Persists timing settings between scenes so title/menu inputs and gameplay stay in sync.
/// </summary>
public static class GameplayTimingSettings
{
    private const string DasMillisecondsKey = "GameplayTimingSettings.DAS";
    private const string ArrMillisecondsKey = "GameplayTimingSettings.ARR";
    private const int DefaultDasMilliseconds = 160;
    private const int DefaultArrMilliseconds = 50;

    public static int GetDasMilliseconds()
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(DasMillisecondsKey, DefaultDasMilliseconds));
    }

    public static int GetArrMilliseconds()
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(ArrMillisecondsKey, DefaultArrMilliseconds));
    }

    public static void SaveDasMilliseconds(float milliseconds)
    {
        PlayerPrefs.SetInt(DasMillisecondsKey, Mathf.Max(0, Mathf.RoundToInt(milliseconds)));
        PlayerPrefs.Save();
    }

    public static void SaveArrMilliseconds(float milliseconds)
    {
        PlayerPrefs.SetInt(ArrMillisecondsKey, Mathf.Max(0, Mathf.RoundToInt(milliseconds)));
        PlayerPrefs.Save();
    }

    public static void SaveTimingMilliseconds(float dasMilliseconds, float arrMilliseconds)
    {
        PlayerPrefs.SetInt(DasMillisecondsKey, Mathf.Max(0, Mathf.RoundToInt(dasMilliseconds)));
        PlayerPrefs.SetInt(ArrMillisecondsKey, Mathf.Max(0, Mathf.RoundToInt(arrMilliseconds)));
        PlayerPrefs.Save();
    }
}
