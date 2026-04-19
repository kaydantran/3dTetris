using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds a cursor-friendly runtime HUD for queue/hold previews, run stats, and timing controls.
/// </summary>
public class GameplayHudController : MonoBehaviour
{
    private static readonly Color FrameColor = new Color(0.06f, 0.11f, 0.17f, 0.82f);
    private static readonly Color PanelColor = new Color(0.1f, 0.16f, 0.24f, 0.92f);
    private static readonly Color PreviewBackgroundColor = new Color(0.14f, 0.21f, 0.31f, 0.96f);
    private static readonly Color PreviewCellOffColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color TextColor = new Color(0.93f, 0.97f, 1f, 1f);
    private static readonly Color AccentColor = new Color(0.43f, 0.82f, 1f, 1f);
    private static readonly Vector2Int[] EmptyCells = Array.Empty<Vector2Int>();
    private static readonly Dictionary<string, PreviewDefinition> PreviewDefinitions = new Dictionary<string, PreviewDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        { "I", new PreviewDefinition(new Color(0.31f, 0.91f, 0.95f, 1f), new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(3, 1) }) },
        { "O", new PreviewDefinition(new Color(0.98f, 0.85f, 0.25f, 1f), new[] { new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(1, 2), new Vector2Int(2, 2) }) },
        { "T", new PreviewDefinition(new Color(0.72f, 0.45f, 0.94f, 1f), new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(1, 2) }) },
        { "S", new PreviewDefinition(new Color(0.46f, 0.86f, 0.38f, 1f), new[] { new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(0, 2), new Vector2Int(1, 2) }) },
        { "Z", new PreviewDefinition(new Color(0.95f, 0.39f, 0.4f, 1f), new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, 2), new Vector2Int(2, 2) }) },
        { "J", new PreviewDefinition(new Color(0.39f, 0.56f, 0.98f, 1f), new[] { new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(2, 2) }) },
        { "L", new PreviewDefinition(new Color(1f, 0.63f, 0.25f, 1f), new[] { new Vector2Int(2, 1), new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(2, 2) }) }
    };

    [SerializeField] private ActivePieceController pieceController;
    [SerializeField] private GameMaster gameMaster;

    private Canvas canvas;
    private RectTransform hudRoot;
    private Font uiFont;
    private Text linesValueText;
    private Text piecesPerSecondValueText;
    private InputField dasInputField;
    private InputField arrInputField;
    private PiecePreviewWidget holdPreviewWidget;
    private readonly List<PiecePreviewWidget> nextPreviewWidgets = new List<PiecePreviewWidget>();
    private bool isBound;
    private const string PreferredBuiltinFontPath = "LegacyRuntime.ttf";
    private const string FallbackBuiltinFontPath = "Arial.ttf";

    private void Awake()
    {
        ResolveReferences();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        EnsureEventSystem();
        EnsureUi();
        BindEvents();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureEventSystem();
        EnsureUi();
        BindEvents();
        RefreshAll();
    }

    private void Start()
    {
        RefreshAll();
    }

    private void Update()
    {
        if (!isBound)
        {
            ResolveReferences();
            BindEvents();
        }

        RefreshPiecesPerSecond();
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    private void EnsureUi()
    {
        BuildUi();

        if (canvas != null)
        {
            canvas.enabled = true;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
        }

        RectTransform canvasRect = transform as RectTransform;
        if (canvasRect != null)
        {
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.localScale = Vector3.one;
        }

        if (hudRoot != null)
        {
            Stretch(hudRoot);
            hudRoot.SetAsLastSibling();
        }
    }

    private void ResolveReferences()
    {
        if (pieceController == null)
        {
            pieceController = FindAnyObjectByType<ActivePieceController>();
        }

        if (gameMaster == null)
        {
            gameMaster = FindAnyObjectByType<GameMaster>();
        }
    }

    private void BindEvents()
    {
        if (isBound || pieceController == null || gameMaster == null) return;

        pieceController.StateChanged += RefreshAll;
        pieceController.TimingSettingsChanged += RefreshTimingFields;
        gameMaster.StatsChanged += RefreshStats;
        isBound = true;
    }

    private void UnbindEvents()
    {
        if (!isBound) return;

        if (pieceController != null)
        {
            pieceController.StateChanged -= RefreshAll;
            pieceController.TimingSettingsChanged -= RefreshTimingFields;
        }

        if (gameMaster != null)
        {
            gameMaster.StatsChanged -= RefreshStats;
        }

        isBound = false;
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private void BuildUi()
    {
        if (canvas != null && hudRoot != null) return;

        uiFont = LoadBuiltinFont();

        canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        if (hudRoot != null)
        {
            return;
        }

        hudRoot = CreateUiObject("HudRoot", transform);
        Stretch(hudRoot);

        RectTransform holdPanel = CreatePanel("HoldPanel", hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(220f, 250f));
        RectTransform statsPanel = CreatePanel("StatsPanel", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(360f, 250f));
        RectTransform nextPanel = CreatePanel("NextPanel", hudRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(220f, 640f));

        BuildHoldPanel(holdPanel);
        BuildStatsPanel(statsPanel);
        BuildNextPanel(nextPanel);
    }

    private void BuildHoldPanel(RectTransform panel)
    {
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 18);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateSectionTitle(panel, "HOLD");
        holdPreviewWidget = new PiecePreviewWidget(panel, uiFont, 28f, 140f);
    }

    private void BuildStatsPanel(RectTransform panel)
    {
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 18);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateSectionTitle(panel, "RUN DATA");
        CreateStatRow(panel, "Lines Cleared", out linesValueText);
        CreateStatRow(panel, "Pieces / Sec", out piecesPerSecondValueText);
        CreateDivider(panel);

        Text timingHeader = CreateText(panel, "TimingHeader", "TIMING", 14, FontStyle.Bold, AccentColor, TextAnchor.MiddleLeft);
        AddLayoutElement(timingHeader.gameObject, 0f, 22f);

        dasInputField = CreateInputRow(panel, "DAS (ms)", "160", HandleDasEdited);
        arrInputField = CreateInputRow(panel, "ARR (ms)", "50", HandleArrEdited);

        Text noteText = CreateText(panel, "TimingNote", "Type a value in milliseconds, then press Enter or click away.", 12, FontStyle.Italic, new Color(TextColor.r, TextColor.g, TextColor.b, 0.72f), TextAnchor.UpperLeft);
        noteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        noteText.verticalOverflow = VerticalWrapMode.Truncate;
        AddLayoutElement(noteText.gameObject, 0f, 34f);
    }

    private void BuildNextPanel(RectTransform panel)
    {
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 18);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateSectionTitle(panel, "NEXT (5)");

        nextPreviewWidgets.Clear();
        for (int i = 0; i < 5; i++)
        {
            nextPreviewWidgets.Add(new PiecePreviewWidget(panel, uiFont, 20f, 82f));
        }
    }

    private void RefreshAll()
    {
        RefreshPreviewPanels();
        RefreshStats();
        RefreshTimingFields();
    }

    private void RefreshPreviewPanels()
    {
        if (pieceController == null) return;

        if (holdPreviewWidget != null)
        {
            holdPreviewWidget.SetPiece(pieceController.GetHeldPieceCode());
        }

        string[] upcomingCodes = pieceController.GetUpcomingPieceCodes(pieceController.PreviewPieceCount);
        for (int i = 0; i < nextPreviewWidgets.Count; i++)
        {
            string pieceCode = i < upcomingCodes.Length ? upcomingCodes[i] : string.Empty;
            nextPreviewWidgets[i].SetPiece(pieceCode);
        }
    }

    private void RefreshStats()
    {
        if (gameMaster == null) return;

        if (linesValueText != null)
        {
            linesValueText.text = gameMaster.TotalLayersCleared.ToString();
        }

        RefreshPiecesPerSecond();
    }

    private void RefreshPiecesPerSecond()
    {
        if (piecesPerSecondValueText == null || gameMaster == null) return;

        piecesPerSecondValueText.text = gameMaster.PiecesPerSecond.ToString("0.00");
    }

    private void RefreshTimingFields()
    {
        if (pieceController == null) return;

        if (dasInputField != null && !dasInputField.isFocused)
        {
            dasInputField.SetTextWithoutNotify(Mathf.RoundToInt(pieceController.DelayedAutoShiftMilliseconds).ToString());
        }

        if (arrInputField != null && !arrInputField.isFocused)
        {
            arrInputField.SetTextWithoutNotify(Mathf.RoundToInt(pieceController.AutoRepeatRateMilliseconds).ToString());
        }
    }

    private void HandleDasEdited(string value)
    {
        ApplyTimingInput(value, ms => pieceController.DelayedAutoShiftMilliseconds = ms);
    }

    private void HandleArrEdited(string value)
    {
        ApplyTimingInput(value, ms => pieceController.AutoRepeatRateMilliseconds = ms);
    }

    private void ApplyTimingInput(string value, Action<float> applyAction)
    {
        if (pieceController == null)
        {
            return;
        }

        int parsedMilliseconds;
        if (!int.TryParse(value, out parsedMilliseconds))
        {
            RefreshTimingFields();
            return;
        }

        applyAction(Mathf.Max(0, parsedMilliseconds));
        RefreshTimingFields();
    }

    private RectTransform CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        RectTransform panel = CreateUiObject(name, parent);
        panel.anchorMin = anchorMin;
        panel.anchorMax = anchorMax;
        panel.pivot = anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = anchoredPosition;
        panel.sizeDelta = sizeDelta;

        Image background = panel.gameObject.AddComponent<Image>();
        background.color = FrameColor;

        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.18f);
        outline.effectDistance = new Vector2(1f, -1f);
        return panel;
    }

    private void CreateStatRow(Transform parent, string label, out Text valueText)
    {
        RectTransform row = CreateUiObject(label.Replace(" ", string.Empty) + "Row", parent);
        AddLayoutElement(row.gameObject, 0f, 30f);

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Text labelText = CreateText(row, label + "Label", label, 15, FontStyle.Normal, new Color(TextColor.r, TextColor.g, TextColor.b, 0.8f), TextAnchor.MiddleLeft);
        AddLayoutElement(labelText.gameObject, 170f, 30f);

        valueText = CreateText(row, label + "Value", "0", 22, FontStyle.Bold, TextColor, TextAnchor.MiddleRight);
    }

    private InputField CreateInputRow(Transform parent, string label, string placeholder, UnityEngine.Events.UnityAction<string> onEndEdit)
    {
        RectTransform row = CreateUiObject(label.Replace(" ", string.Empty) + "Row", parent);
        AddLayoutElement(row.gameObject, 0f, 34f);

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        Text labelText = CreateText(row, label.Replace(" ", string.Empty) + "Label", label, 15, FontStyle.Normal, new Color(TextColor.r, TextColor.g, TextColor.b, 0.8f), TextAnchor.MiddleLeft);
        AddLayoutElement(labelText.gameObject, 118f, 34f);

        RectTransform inputRoot = CreateUiObject(label.Replace(" ", string.Empty) + "Input", row);
        AddLayoutElement(inputRoot.gameObject, 96f, 34f);

        Image inputBackground = inputRoot.gameObject.AddComponent<Image>();
        inputBackground.color = PanelColor;

        InputField inputField = inputRoot.gameObject.AddComponent<InputField>();
        inputField.contentType = InputField.ContentType.IntegerNumber;
        inputField.lineType = InputField.LineType.SingleLine;
        inputField.characterValidation = InputField.CharacterValidation.Integer;
        inputField.targetGraphic = inputBackground;

        RectTransform textViewport = CreateUiObject("Text", inputRoot);
        Stretch(textViewport, 8f, 8f, 5f, 5f);

        Text textComponent = textViewport.gameObject.AddComponent<Text>();
        textComponent.font = uiFont;
        textComponent.fontSize = 18;
        textComponent.fontStyle = FontStyle.Bold;
        textComponent.color = TextColor;
        textComponent.alignment = TextAnchor.MiddleCenter;
        textComponent.raycastTarget = false;
        textComponent.supportRichText = false;

        RectTransform placeholderRect = CreateUiObject("Placeholder", inputRoot);
        Stretch(placeholderRect, 8f, 8f, 5f, 5f);

        Text placeholderText = placeholderRect.gameObject.AddComponent<Text>();
        placeholderText.font = uiFont;
        placeholderText.fontSize = 16;
        placeholderText.fontStyle = FontStyle.Italic;
        placeholderText.color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.35f);
        placeholderText.alignment = TextAnchor.MiddleCenter;
        placeholderText.text = placeholder;
        placeholderText.raycastTarget = false;
        placeholderText.supportRichText = false;

        inputField.textComponent = textComponent;
        inputField.placeholder = placeholderText;
        inputField.onEndEdit.AddListener(onEndEdit);

        Text suffixText = CreateText(row, label.Replace(" ", string.Empty) + "Suffix", "ms", 14, FontStyle.Bold, AccentColor, TextAnchor.MiddleLeft);
        AddLayoutElement(suffixText.gameObject, 26f, 34f);

        return inputField;
    }

    private void CreateDivider(Transform parent)
    {
        RectTransform divider = CreateUiObject("Divider", parent);
        AddLayoutElement(divider.gameObject, 0f, 1f);

        Image image = divider.gameObject.AddComponent<Image>();
        image.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.25f);
    }

    private void CreateSectionTitle(Transform parent, string title)
    {
        Text titleText = CreateText(parent, title.Replace(" ", string.Empty) + "Title", title, 18, FontStyle.Bold, AccentColor, TextAnchor.MiddleCenter);
        AddLayoutElement(titleText.gameObject, 0f, 24f);
    }

    private Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle fontStyle, Color color, TextAnchor alignment)
    {
        RectTransform rectTransform = CreateUiObject(name, parent);
        Text text = rectTransform.gameObject.AddComponent<Text>();
        text.font = uiFont;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.text = content;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    private static RectTransform CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<RectTransform>();
    }

    private static void AddLayoutElement(GameObject gameObject, float preferredWidth, float preferredHeight)
    {
        LayoutElement layoutElement = gameObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = gameObject.AddComponent<LayoutElement>();
        }

        if (preferredWidth > 0f)
        {
            layoutElement.preferredWidth = preferredWidth;
        }

        if (preferredHeight > 0f)
        {
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.minHeight = preferredHeight;
        }
    }

    private static void Stretch(RectTransform rectTransform, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(left, bottom);
        rectTransform.offsetMax = new Vector2(-right, -top);
    }

    private static Font LoadBuiltinFont()
    {
        try
        {
            return Resources.GetBuiltinResource<Font>(PreferredBuiltinFontPath);
        }
        catch (ArgumentException)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(FallbackBuiltinFontPath);
            }
            catch (ArgumentException)
            {
                return Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Verdana" }, 16);
            }
        }
    }

    private readonly struct PreviewDefinition
    {
        public PreviewDefinition(Color color, Vector2Int[] occupiedCells)
        {
            Color = color;
            OccupiedCells = occupiedCells ?? EmptyCells;
        }

        public Color Color { get; }
        public Vector2Int[] OccupiedCells { get; }
    }

    private sealed class PiecePreviewWidget
    {
        private readonly Image backgroundImage;
        private readonly Image[] cells;
        private readonly Text pieceCodeLabel;

        public PiecePreviewWidget(Transform parent, Font font, float cellSize, float preferredHeight)
        {
            RectTransform root = CreateUiObject("PiecePreviewWidget", parent);
            AddLayoutElement(root.gameObject, 0f, preferredHeight);

            backgroundImage = root.gameObject.AddComponent<Image>();
            backgroundImage.color = PreviewBackgroundColor;

            Outline outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.05f);
            outline.effectDistance = new Vector2(1f, -1f);

            RectTransform labelRect = CreateUiObject("PieceLabel", root);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -8f);
            labelRect.sizeDelta = new Vector2(0f, 20f);

            pieceCodeLabel = labelRect.gameObject.AddComponent<Text>();
            pieceCodeLabel.font = font;
            pieceCodeLabel.fontSize = 14;
            pieceCodeLabel.fontStyle = FontStyle.Bold;
            pieceCodeLabel.color = AccentColor;
            pieceCodeLabel.alignment = TextAnchor.MiddleCenter;
            pieceCodeLabel.raycastTarget = false;
            pieceCodeLabel.supportRichText = false;

            RectTransform gridRoot = CreateUiObject("Grid", root);
            gridRoot.anchorMin = new Vector2(0.5f, 0.5f);
            gridRoot.anchorMax = new Vector2(0.5f, 0.5f);
            gridRoot.pivot = new Vector2(0.5f, 0.5f);
            float gridSize = cellSize * 4f + 9f;
            gridRoot.sizeDelta = new Vector2(gridSize, gridSize);
            gridRoot.anchoredPosition = new Vector2(0f, -4f);

            GridLayoutGroup gridLayout = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 4;
            gridLayout.cellSize = new Vector2(cellSize, cellSize);
            gridLayout.spacing = new Vector2(3f, 3f);
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.UpperLeft;

            cells = new Image[16];
            for (int i = 0; i < cells.Length; i++)
            {
                RectTransform cell = CreateUiObject("Cell" + i, gridRoot);
                Image image = cell.gameObject.AddComponent<Image>();
                image.color = PreviewCellOffColor;
                cells[i] = image;
            }

            SetPiece(string.Empty);
        }

        public void SetPiece(string pieceCode)
        {
            string normalizedCode = string.IsNullOrWhiteSpace(pieceCode) ? string.Empty : pieceCode.Trim().ToUpperInvariant();
            PreviewDefinition definition = default;
            bool hasDefinition = normalizedCode.Length > 0 && PreviewDefinitions.TryGetValue(normalizedCode, out definition);
            string label = hasDefinition ? normalizedCode : "EMPTY";
            pieceCodeLabel.text = label;

            backgroundImage.color = hasDefinition
                ? Color.Lerp(PreviewBackgroundColor, definition.Color, 0.14f)
                : PreviewBackgroundColor;

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].color = PreviewCellOffColor;
            }

            if (!hasDefinition)
            {
                return;
            }

            foreach (Vector2Int occupiedCell in definition.OccupiedCells)
            {
                int index = occupiedCell.y * 4 + occupiedCell.x;
                if (index >= 0 && index < cells.Length)
                {
                    cells[index].color = definition.Color;
                }
            }
        }
    }
}
