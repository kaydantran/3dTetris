using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MenuInteractions : MonoBehaviour
{
    private static readonly Color PanelBorderColor = new Color(0.95f, 0.98f, 1f, 0.95f);
    private static readonly Color PanelBevelColor = new Color(0.72f, 0.88f, 1f, 0.28f);
    private const float PanelBorderThickness = 3f;
    private const string TitleScreenSceneName = "TitleScreen";

    [SerializeField] private string gameplaySceneName = "MainGame";
    [SerializeField] private Canvas titleCanvas;
    [SerializeField] private Canvas optionsCanvas;
    [SerializeField] private TMP_InputField dasInputField;
    [SerializeField] private TMP_InputField arrInputField;
    [SerializeField] private Button playButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private RectTransform titlePanel;
    [SerializeField] private RectTransform optionsPanel;

    private void Awake()
    {
        ResolveReferences();
        ConfigureTitleScreenUi();
        BindListeners();
        RefreshTimingFields();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ConfigureTitleScreenUi();
        BindListeners();
        RefreshTimingFields();
    }

    private void OnDisable()
    {
        if (dasInputField != null)
        {
            dasInputField.onEndEdit.RemoveListener(HandleDasEdited);
        }

        if (arrInputField != null)
        {
            arrInputField.onEndEdit.RemoveListener(HandleArrEdited);
        }

        if (playButton != null)
        {
            playButton.onClick.RemoveListener(PlayGame);
        }

        if (optionsButton != null)
        {
            optionsButton.onClick.RemoveListener(ShowOptionsMenu);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(ShowTitleMenu);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
        }
    }

    public void PlayGame()
    {
        SaveTimingSettingsFromInputs();

        if (Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            SceneManager.LoadScene(gameplaySceneName);
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex + 1);
    }

    public void RestartGame()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.name);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapTitleScreen()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, TitleScreenSceneName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (FindAnyObjectByType<MenuInteractions>() != null)
        {
            return;
        }

        GameObject menuInteractionsObject = new GameObject(nameof(MenuInteractions));
        menuInteractionsObject.AddComponent<MenuInteractions>();
    }

    private void ResolveReferences()
    {
        if (titleCanvas == null)
        {
            titleCanvas = FindCanvasByName("HUD");
        }

        if (optionsCanvas == null)
        {
            optionsCanvas = FindCanvasByName("Options");
        }

        if (titlePanel == null && titleCanvas != null)
        {
            titlePanel = FindDescendantByName(titleCanvas.transform, "Panel") as RectTransform;
        }

        if (optionsPanel == null && optionsCanvas != null)
        {
            optionsPanel = FindDescendantByName(optionsCanvas.transform, "Panel") as RectTransform;
        }

        if (dasInputField == null)
        {
            dasInputField = FindInputFieldByName("DAS");
        }

        if (arrInputField == null)
        {
            arrInputField = FindInputFieldByName("ARR");
        }

        if (playButton == null)
        {
            Transform playButtonTransform = FindDescendantByName(titleCanvas != null ? titleCanvas.transform : null, "PlayButton");
            if (playButtonTransform != null)
            {
                playButton = playButtonTransform.GetComponent<Button>();
            }
        }

        if (optionsButton == null)
        {
            Transform optionsButtonTransform = FindDescendantByName(titleCanvas != null ? titleCanvas.transform : null, "OptionsButton");
            if (optionsButtonTransform != null)
            {
                optionsButton = optionsButtonTransform.GetComponent<Button>();
            }
        }

        if (backButton == null)
        {
            Transform backButtonTransform = FindDescendantByName(optionsCanvas != null ? optionsCanvas.transform : null, "BackButton");
            if (backButtonTransform != null)
            {
                backButton = backButtonTransform.GetComponent<Button>();
            }
        }

        if (quitButton == null)
        {
            Transform quitButtonTransform = FindDescendantByName(titleCanvas != null ? titleCanvas.transform : null, "QuitButton");
            if (quitButtonTransform != null)
            {
                quitButton = quitButtonTransform.GetComponent<Button>();
            }
        }
    }

    private void ConfigureTitleScreenUi()
    {
        ConfigureCanvas(titleCanvas);
        ConfigureCanvas(optionsCanvas);
        ConfigureTimingInputField(dasInputField, HandleDasEdited);
        ConfigureTimingInputField(arrInputField, HandleArrEdited);
        StylePanel(titlePanel);
        StylePanel(optionsPanel);
        ShowTitleMenu();
    }

    private void BindListeners()
    {
        if (dasInputField != null)
        {
            dasInputField.onEndEdit.RemoveListener(HandleDasEdited);
            dasInputField.onEndEdit.AddListener(HandleDasEdited);
        }

        if (arrInputField != null)
        {
            arrInputField.onEndEdit.RemoveListener(HandleArrEdited);
            arrInputField.onEndEdit.AddListener(HandleArrEdited);
        }

        if (playButton != null)
        {
            playButton.onClick.RemoveListener(PlayGame);
            playButton.onClick.AddListener(PlayGame);
        }

        if (optionsButton != null)
        {
            optionsButton.onClick.RemoveListener(ShowOptionsMenu);
            optionsButton.onClick.AddListener(ShowOptionsMenu);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(ShowTitleMenu);
            backButton.onClick.AddListener(ShowTitleMenu);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
            quitButton.onClick.AddListener(QuitGame);
        }
    }

    private void RefreshTimingFields()
    {
        if (dasInputField != null)
        {
            dasInputField.SetTextWithoutNotify(GameplayTimingSettings.GetDasMilliseconds().ToString());
        }

        if (arrInputField != null)
        {
            arrInputField.SetTextWithoutNotify(GameplayTimingSettings.GetArrMilliseconds().ToString());
        }
    }

    private void HandleDasEdited(string value)
    {
        if (!TryParseTimingValue(value, GameplayTimingSettings.GetDasMilliseconds(), out int parsedMilliseconds))
        {
            RefreshTimingFields();
            return;
        }

        GameplayTimingSettings.SaveDasMilliseconds(parsedMilliseconds);
        RefreshTimingFields();
    }

    private void HandleArrEdited(string value)
    {
        if (!TryParseTimingValue(value, GameplayTimingSettings.GetArrMilliseconds(), out int parsedMilliseconds))
        {
            RefreshTimingFields();
            return;
        }

        GameplayTimingSettings.SaveArrMilliseconds(parsedMilliseconds);
        RefreshTimingFields();
    }

    private void SaveTimingSettingsFromInputs()
    {
        int dasMilliseconds = GetTimingValueFromInput(dasInputField, GameplayTimingSettings.GetDasMilliseconds());
        int arrMilliseconds = GetTimingValueFromInput(arrInputField, GameplayTimingSettings.GetArrMilliseconds());
        GameplayTimingSettings.SaveTimingMilliseconds(dasMilliseconds, arrMilliseconds);
        RefreshTimingFields();
    }

    private static int GetTimingValueFromInput(TMP_InputField inputField, int fallbackValue)
    {
        if (inputField == null)
        {
            return Mathf.Max(0, fallbackValue);
        }

        return TryParseTimingValue(inputField.text, fallbackValue, out int parsedMilliseconds)
            ? parsedMilliseconds
            : Mathf.Max(0, fallbackValue);
    }

    private static bool TryParseTimingValue(string value, int fallbackValue, out int parsedMilliseconds)
    {
        if (!int.TryParse(value, out parsedMilliseconds))
        {
            parsedMilliseconds = Mathf.Max(0, fallbackValue);
            return false;
        }

        parsedMilliseconds = Mathf.Max(0, parsedMilliseconds);
        return true;
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect != null)
        {
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.localScale = Vector3.one;
        }
    }

    private void ShowTitleMenu()
    {
        if (titleCanvas != null)
        {
            titleCanvas.enabled = true;
        }

        if (optionsCanvas != null)
        {
            optionsCanvas.enabled = false;
        }
    }

    private void ShowOptionsMenu()
    {
        if (titleCanvas != null)
        {
            titleCanvas.enabled = false;
        }

        if (optionsCanvas != null)
        {
            optionsCanvas.enabled = true;
        }
    }

    private static void ConfigureTimingInputField(TMP_InputField inputField, UnityEngine.Events.UnityAction<string> onEndEdit)
    {
        if (inputField == null)
        {
            return;
        }

        inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.onEndEdit.RemoveListener(onEndEdit);
        inputField.onEndEdit.AddListener(onEndEdit);
    }

    private static void StylePanel(RectTransform panel)
    {
        if (panel == null)
        {
            return;
        }

        Image background = panel.GetComponent<Image>();
        if (background == null)
        {
            background = panel.gameObject.AddComponent<Image>();
        }

        background.color = new Color(1f, 1f, 1f, 0f);
        background.raycastTarget = false;

        CreateOrUpdateBorderSegment(panel, "BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, PanelBorderThickness), PanelBorderColor);
        CreateOrUpdateBorderSegment(panel, "BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(0f, PanelBorderThickness), PanelBorderColor);
        CreateOrUpdateBorderSegment(panel, "BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(PanelBorderThickness, 0f), PanelBorderColor);
        CreateOrUpdateBorderSegment(panel, "BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(PanelBorderThickness, 0f), PanelBorderColor);
        CreateOrUpdateBorderSegment(panel, "BevelTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -PanelBorderThickness), new Vector2(0f, 1f), PanelBevelColor);
        CreateOrUpdateBorderSegment(panel, "BevelLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(PanelBorderThickness, 0f), new Vector2(1f, 0f), PanelBevelColor);
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

    private static Canvas FindCanvasByName(string canvasName)
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Canvas canvas in canvases)
        {
            if (string.Equals(canvas.name, canvasName, StringComparison.OrdinalIgnoreCase))
            {
                return canvas;
            }
        }

        return null;
    }

    private TMP_InputField FindInputFieldByName(string fieldName)
    {
        Transform root = optionsCanvas != null ? optionsCanvas.transform : null;
        Transform fieldTransform = FindDescendantByName(root, fieldName);
        return fieldTransform != null ? fieldTransform.GetComponent<TMP_InputField>() : null;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
        {
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
