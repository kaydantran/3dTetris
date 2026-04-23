using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Populates the end screen and wires its buttons after a run completes.
/// </summary>
public class EndScreenController : MonoBehaviour
{
    private const string EndScreenSceneName = "EndScreen";
    private const float PanelBorderThickness = 3f;
    private static readonly Color PanelBorderColor = new Color(0.95f, 0.98f, 1f, 0.95f);
    private static readonly Color PanelBevelColor = new Color(0.72f, 0.88f, 1f, 0.28f);

    [SerializeField] private string gameplaySceneName = "MainGame";
    [SerializeField] private TMP_Text statsInfoText;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private RectTransform panel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapEndScreen()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, EndScreenSceneName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (FindAnyObjectByType<EndScreenController>() != null)
        {
            return;
        }

        GameObject controllerObject = new GameObject(nameof(EndScreenController));
        controllerObject.AddComponent<EndScreenController>();
    }

    private void Awake()
    {
        ResolveReferences();
        BindListeners();
        StylePanel(panel);
        RefreshStats();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindListeners();
        StylePanel(panel);
        RefreshStats();
    }

    private void OnDisable()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartGame);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
        }
    }

    private void ResolveReferences()
    {
        if (statsInfoText == null)
        {
            Transform statsInfoTransform = FindDescendantByName(null, "StatsInfo");
            if (statsInfoTransform != null)
            {
                statsInfoText = statsInfoTransform.GetComponent<TMP_Text>();
            }
        }

        if (restartButton == null)
        {
            Transform restartTransform = FindDescendantByName(null, "RestartButton");
            if (restartTransform != null)
            {
                restartButton = restartTransform.GetComponent<Button>();
            }
        }

        if (quitButton == null)
        {
            Transform quitTransform = FindDescendantByName(null, "QuitButton");
            if (quitTransform != null)
            {
                quitButton = quitTransform.GetComponent<Button>();
            }
        }

        if (panel == null)
        {
            panel = FindDescendantByName(null, "Panel") as RectTransform;
        }
    }

    private void BindListeners()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartGame);
            restartButton.onClick.AddListener(RestartGame);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
            quitButton.onClick.AddListener(QuitGame);
        }
    }

    private void RefreshStats()
    {
        if (statsInfoText == null)
        {
            return;
        }

        if (!GameRunSummary.HasSummary)
        {
            statsInfoText.text =
                "SCORE: 0\n" +
                "LAYERS CLEARED: 0\n" +
                "TIME: 0:00\n" +
                "PIECES/SECOND: 0.00\n" +
                "TETRISES: 0\n" +
                "MAX COMBO: 0";
            return;
        }

        GameRunSummary.SummaryData summary = GameRunSummary.Current;
        statsInfoText.text =
            $"SCORE: {summary.Score}\n" +
            $"LAYERS CLEARED: {summary.TotalLayersCleared}\n" +
            $"TIME: {FormatElapsedTime(summary.ElapsedGameplayTime)}\n" +
            $"PIECES/SECOND: {summary.PiecesPerSecond:0.00}\n" +
            $"TETRISES: {summary.TotalTetrises}\n" +
            $"MAX COMBO: {summary.MaxCombo}";
    }

    private void RestartGame()
    {
        GameRunSummary.Clear();
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void QuitGame()
    {
        Application.Quit();
    }

    private static string FormatElapsedTime(float elapsedSeconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(elapsedSeconds));
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds / 60) % 60;
        int seconds = totalSeconds % 60;

        if (hours > 0)
        {
            return $"{hours}:{minutes:00}:{seconds:00}";
        }

        return $"{minutes}:{seconds:00}";
    }

    private static void StylePanel(RectTransform targetPanel)
    {
        if (targetPanel == null)
        {
            return;
        }

        Image background = targetPanel.GetComponent<Image>();
        if (background == null)
        {
            background = targetPanel.gameObject.AddComponent<Image>();
        }

        background.color = new Color(1f, 1f, 1f, 0f);
        background.raycastTarget = false;

        CreateOrUpdateBorderSegment(targetPanel, "BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, PanelBorderThickness), PanelBorderColor);
        CreateOrUpdateBorderSegment(targetPanel, "BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(0f, PanelBorderThickness), PanelBorderColor);
        CreateOrUpdateBorderSegment(targetPanel, "BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(PanelBorderThickness, 0f), PanelBorderColor);
        CreateOrUpdateBorderSegment(targetPanel, "BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(PanelBorderThickness, 0f), PanelBorderColor);
        CreateOrUpdateBorderSegment(targetPanel, "BevelTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -PanelBorderThickness), new Vector2(0f, 1f), PanelBevelColor);
        CreateOrUpdateBorderSegment(targetPanel, "BevelLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(PanelBorderThickness, 0f), new Vector2(1f, 0f), PanelBevelColor);
    }

    private static void CreateOrUpdateBorderSegment(
        RectTransform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Color color)
    {
        RectTransform segment = parent.Find(name) as RectTransform;
        if (segment == null)
        {
            GameObject segmentObject = new GameObject(name, typeof(RectTransform));
            segmentObject.transform.SetParent(parent, false);
            segment = segmentObject.GetComponent<RectTransform>();
        }

        segment.SetAsFirstSibling();
        segment.anchorMin = anchorMin;
        segment.anchorMax = anchorMax;
        segment.pivot = pivot;
        segment.anchoredPosition = anchoredPosition;
        segment.sizeDelta = sizeDelta;

        Image image = segment.GetComponent<Image>();
        if (image == null)
        {
            image = segment.gameObject.AddComponent<Image>();
        }

        image.color = color;
        image.raycastTarget = false;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return null;
        }

        if (root == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                Transform canvasMatch = FindDescendantByName(canvas.transform, targetName);
                if (canvasMatch != null)
                {
                    return canvasMatch;
                }
            }

            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform nestedMatch = FindDescendantByName(child, targetName);
            if (nestedMatch != null)
            {
                return nestedMatch;
            }
        }

        return null;
    }
}
