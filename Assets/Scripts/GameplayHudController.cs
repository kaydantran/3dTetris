using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
    private static readonly Color NextPanelBackgroundColor = new Color(1f, 1f, 1f, 0f);
    private static readonly Color NextPanelInnerColor = new Color(1f, 1f, 1f, 0f);
    private static readonly Color NextPanelOutlineColor = new Color(0.95f, 0.98f, 1f, 0.95f);
    private static readonly Color NextPanelBevelColor = new Color(1f, 1f, 1f, 0f);
    private static readonly Color NextPanelInnerOutlineColor = new Color(1f, 1f, 1f, 0f);
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
    [SerializeField] private Texture2D pieceITexture;
    [SerializeField] private Texture2D pieceJTexture;
    [SerializeField] private Texture2D pieceLTexture;
    [SerializeField] private Texture2D pieceOTexture;
    [SerializeField] private Texture2D pieceSTexture;
    [SerializeField] private Texture2D pieceTTexture;
    [SerializeField] private Texture2D pieceZTexture;
    [SerializeField] private RenderTexture pieceIRenderTexture;
    [SerializeField] private RenderTexture pieceJRenderTexture;
    [SerializeField] private RenderTexture pieceLRenderTexture;
    [SerializeField] private RenderTexture pieceORenderTexture;
    [SerializeField] private RenderTexture pieceSRenderTexture;
    [SerializeField] private RenderTexture pieceTRenderTexture;
    [SerializeField] private RenderTexture pieceZRenderTexture;
    [SerializeField] private Canvas targetHudCanvas;
    [SerializeField] private TMP_Text scoreNumberTmpText;
    [SerializeField] private TMP_Text timeNumberTmpText;
    [SerializeField] private TMP_Text nextTextTmpText;
    [SerializeField] private Text scoreNumberText;
    [SerializeField] private Text timeNumberText;

    private Canvas canvas;
    private RectTransform hudRoot;
    private RectTransform nextPiecesRoot;
    private RectTransform sceneNextPreviewPanel;
    private Font uiFont;
    private Text scoreValueText;
    private Text timeValueText;
    private Text linesValueText;
    private Text piecesPerSecondValueText;
    private InputField dasInputField;
    private InputField arrInputField;
    private PiecePreviewWidget holdPreviewWidget;
    private readonly List<PiecePreviewWidget> nextPreviewWidgets = new List<PiecePreviewWidget>();
    private readonly List<RawImage> sceneNextPreviewSlots = new List<RawImage>();
    private bool isBound;
    private const string PreferredBuiltinFontPath = "LegacyRuntime.ttf";
    private const string FallbackBuiltinFontPath = "Arial.ttf";

    private void Awake()
    {
        ResolveReferences();
        ResolveHudBindings();
        ResolvePreviewRenderTextures();
        ResolvePreviewSprites();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        EnsureEventSystem();
        EnsureUi();
        BindEvents();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveHudBindings();
        ResolvePreviewRenderTextures();
        ResolvePreviewSprites();
        EnsureEventSystem();
        EnsureUi();
        BindEvents();
        RefreshAll();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveHudBindings();
        ResolvePreviewRenderTextures();
        ResolvePreviewSprites();
    }
#endif

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

        RefreshTimeDisplay();
        RefreshPiecesPerSecond();
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    private void EnsureUi()
    {
        ResolveHudBindings();
        BuildUi();
        EnsureSceneNextPreviewPanel();

        if (canvas != null)
        {
            canvas.enabled = true;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
        }

        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
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

    private void ResolveHudBindings()
    {
        if (targetHudCanvas == null)
        {
            targetHudCanvas = GetComponent<Canvas>();
        }

        if (targetHudCanvas == null)
        {
            targetHudCanvas = FindCanvasByName("HUD");
        }

        if (targetHudCanvas == null)
        {
            targetHudCanvas = FindAnyObjectByType<Canvas>();
        }

        canvas = targetHudCanvas;

        if (canvas == null)
        {
            return;
        }

        if (scoreNumberTmpText == null)
        {
            Transform scoreTransform = FindDescendantByName(canvas.transform, "ScoreNumber");
            if (scoreTransform != null)
            {
                scoreNumberTmpText = scoreTransform.GetComponent<TMP_Text>();
                if (scoreNumberTmpText == null)
                {
                    scoreNumberText = scoreTransform.GetComponent<Text>();
                }
            }
        }

        if (timeNumberTmpText == null)
        {
            Transform timeTransform = FindDescendantByName(canvas.transform, "TimeNumber");
            if (timeTransform != null)
            {
                timeNumberTmpText = timeTransform.GetComponent<TMP_Text>();
                if (timeNumberTmpText == null)
                {
                    timeNumberText = timeTransform.GetComponent<Text>();
                }
            }
        }

        if (nextPiecesRoot == null)
        {
            Transform nextPiecesTransform = FindDescendantByName(canvas.transform, "NextPieces");
            nextPiecesRoot = nextPiecesTransform as RectTransform;
        }

        if (nextTextTmpText == null)
        {
            Transform nextTextTransform = FindDescendantByName(canvas.transform, "NextText");
            if (nextTextTransform != null)
            {
                nextTextTmpText = nextTextTransform.GetComponent<TMP_Text>();
            }
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

        if (canvas == null)
        {
            return;
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (canvas.gameObject.GetComponent<GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        if (hudRoot != null)
        {
            return;
        }

        Transform existingGeneratedRoot = canvas.transform.Find("GameplayHudGenerated");
        hudRoot = existingGeneratedRoot as RectTransform;
        if (hudRoot == null)
        {
            hudRoot = CreateUiObject("GameplayHudGenerated", canvas.transform);
        }

        Stretch(hudRoot);

        RectTransform holdPanel = CreatePanel("HoldPanel", hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(220f, 250f));
        RectTransform statsPanel = CreatePanel("StatsPanel", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(360f, 250f));
        RectTransform nextPanel = nextPiecesRoot == null
            ? CreatePanel("NextPanel", hudRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(220f, 640f))
            : null;

        BuildHoldPanel(holdPanel);
        BuildStatsPanel(statsPanel);
        if (nextPanel != null)
        {
            BuildNextPanel(nextPanel);
        }
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
        if (scoreNumberTmpText == null && scoreNumberText == null)
        {
            CreateStatRow(panel, "Score", out scoreValueText);
        }
        if (timeNumberTmpText == null && timeNumberText == null)
        {
            CreateStatRow(panel, "Time", out timeValueText);
        }
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
            nextPreviewWidgets.Add(new PiecePreviewWidget(panel, uiFont, 20f, 82f, true));
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
        for (int i = 0; i < sceneNextPreviewSlots.Count; i++)
        {
            string pieceCode = i < upcomingCodes.Length ? upcomingCodes[i] : string.Empty;
            sceneNextPreviewSlots[i].texture = GetPreviewTexture(pieceCode);
            sceneNextPreviewSlots[i].color = string.IsNullOrWhiteSpace(pieceCode)
                ? new Color(1f, 1f, 1f, 0f)
                : Color.white;
        }

        for (int i = 0; i < nextPreviewWidgets.Count; i++)
        {
            string pieceCode = i < upcomingCodes.Length ? upcomingCodes[i] : string.Empty;
            nextPreviewWidgets[i].SetPiece(pieceCode, GetPreviewTexture(pieceCode));
        }
    }

    private void RefreshStats()
    {
        if (gameMaster == null) return;

        if (scoreValueText != null)
        {
            scoreValueText.text = gameMaster.Score.ToString();
        }

        if (scoreNumberTmpText != null)
        {
            scoreNumberTmpText.text = gameMaster.Score.ToString();
        }

        if (scoreNumberText != null)
        {
            scoreNumberText.text = gameMaster.Score.ToString();
        }

        RefreshTimeDisplay();

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

    private void RefreshTimeDisplay()
    {
        if (gameMaster == null) return;

        string formattedTime = FormatElapsedTime(gameMaster.ElapsedGameplayTime);

        if (timeValueText != null)
        {
            timeValueText.text = formattedTime;
        }

        if (timeNumberTmpText != null)
        {
            timeNumberTmpText.text = formattedTime;
        }

        if (timeNumberText != null)
        {
            timeNumberText.text = formattedTime;
        }
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

    private Texture GetPreviewTexture(string pieceCode)
    {
        if (string.IsNullOrWhiteSpace(pieceCode))
        {
            return null;
        }

        switch (pieceCode.Trim().ToUpperInvariant())
        {
            case "I":
                return pieceIRenderTexture != null ? pieceIRenderTexture : pieceITexture;
            case "J":
                return pieceJRenderTexture != null ? pieceJRenderTexture : pieceJTexture;
            case "L":
                return pieceLRenderTexture != null ? pieceLRenderTexture : pieceLTexture;
            case "O":
                return pieceORenderTexture != null ? pieceORenderTexture : pieceOTexture;
            case "S":
                return pieceSRenderTexture != null ? pieceSRenderTexture : pieceSTexture;
            case "T":
                return pieceTRenderTexture != null ? pieceTRenderTexture : pieceTTexture;
            case "Z":
                return pieceZRenderTexture != null ? pieceZRenderTexture : pieceZTexture;
            default:
                return null;
        }
    }

    private void EnsureSceneNextPreviewPanel()
    {
        if (nextPiecesRoot == null)
        {
            return;
        }

        sceneNextPreviewPanel = nextPiecesRoot.Find("NextPreviewPanel") as RectTransform;
        if (sceneNextPreviewPanel == null)
        {
            sceneNextPreviewPanel = CreateUiObject("NextPreviewPanel", nextPiecesRoot);
        }

        sceneNextPreviewPanel.anchorMin = new Vector2(1f, 1f);
        sceneNextPreviewPanel.anchorMax = new Vector2(1f, 1f);
        sceneNextPreviewPanel.pivot = new Vector2(1f, 1f);
        sceneNextPreviewPanel.anchoredPosition = new Vector2(0f, -66f);
        sceneNextPreviewPanel.sizeDelta = new Vector2(225f, 692f);

        Image background = sceneNextPreviewPanel.GetComponent<Image>();
        if (background == null)
        {
            background = sceneNextPreviewPanel.gameObject.AddComponent<Image>();
        }

        background.color = NextPanelBackgroundColor;
        background.raycastTarget = false;

        Outline outline = sceneNextPreviewPanel.GetComponent<Outline>();
        if (outline == null)
        {
            outline = sceneNextPreviewPanel.gameObject.AddComponent<Outline>();
        }

        outline.effectColor = NextPanelOutlineColor;
        outline.effectDistance = new Vector2(1f, -1f);

        Shadow bevelShadow = sceneNextPreviewPanel.GetComponent<Shadow>();
        if (bevelShadow == null)
        {
            bevelShadow = sceneNextPreviewPanel.gameObject.AddComponent<Shadow>();
        }

        bevelShadow.effectColor = NextPanelBevelColor;
        bevelShadow.effectDistance = Vector2.zero;

        RectTransform innerPanel = sceneNextPreviewPanel.Find("InnerPanel") as RectTransform;
        if (innerPanel == null)
        {
            innerPanel = CreateUiObject("InnerPanel", sceneNextPreviewPanel);
        }

        Stretch(innerPanel, 2f, 2f, 2f, 2f);

        Image innerPanelImage = innerPanel.GetComponent<Image>();
        if (innerPanelImage == null)
        {
            innerPanelImage = innerPanel.gameObject.AddComponent<Image>();
        }

        innerPanelImage.color = NextPanelInnerColor;
        innerPanelImage.raycastTarget = false;

        Outline innerPanelOutline = innerPanel.GetComponent<Outline>();
        if (innerPanelOutline == null)
        {
            innerPanelOutline = innerPanel.gameObject.AddComponent<Outline>();
        }

        innerPanelOutline.effectColor = NextPanelInnerOutlineColor;
        innerPanelOutline.effectDistance = Vector2.zero;

        Shadow innerPanelBevelShadow = innerPanel.GetComponent<Shadow>();
        if (innerPanelBevelShadow == null)
        {
            innerPanelBevelShadow = innerPanel.gameObject.AddComponent<Shadow>();
        }

        innerPanelBevelShadow.effectColor = new Color(1f, 1f, 1f, 0f);
        innerPanelBevelShadow.effectDistance = Vector2.zero;

        RectTransform bevelHighlight = innerPanel.Find("BevelHighlight") as RectTransform;
        if (bevelHighlight == null)
        {
            bevelHighlight = CreateUiObject("BevelHighlight", innerPanel);
        }

        bevelHighlight.SetAsFirstSibling();
        Stretch(bevelHighlight, 0f, 0f, 0f, 0f);

        Image bevelHighlightImage = bevelHighlight.GetComponent<Image>();
        if (bevelHighlightImage == null)
        {
            bevelHighlightImage = bevelHighlight.gameObject.AddComponent<Image>();
        }

        bevelHighlightImage.color = new Color(1f, 1f, 1f, 0f);
        bevelHighlightImage.raycastTarget = false;

        Outline bevelHighlightOutline = bevelHighlight.GetComponent<Outline>();
        if (bevelHighlightOutline == null)
        {
            bevelHighlightOutline = bevelHighlight.gameObject.AddComponent<Outline>();
        }

        bevelHighlightOutline.effectColor = new Color(1f, 1f, 1f, 0f);
        bevelHighlightOutline.effectDistance = Vector2.zero;

        RectTransform stackRoot = innerPanel.Find("PreviewStack") as RectTransform;
        if (stackRoot == null)
        {
            stackRoot = CreateUiObject("PreviewStack", innerPanel);
        }

        Stretch(stackRoot, 16f, 16f, 18f, 18f);

        VerticalLayoutGroup layout = stackRoot.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = stackRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        sceneNextPreviewSlots.Clear();
        for (int i = 0; i < 5; i++)
        {
            string slotName = "PreviewSlot" + i;
            RectTransform slotRoot = stackRoot.Find(slotName) as RectTransform;
            if (slotRoot == null)
            {
                slotRoot = CreateUiObject(slotName, stackRoot);
            }

            AddLayoutElement(slotRoot.gameObject, 0f, 120f);

            Image slotBackground = slotRoot.GetComponent<Image>();
            if (slotBackground == null)
            {
                slotBackground = slotRoot.gameObject.AddComponent<Image>();
            }

            slotBackground.color = new Color(1f, 1f, 1f, 0f);
            slotBackground.raycastTarget = false;

            RectTransform previewImageRect = slotRoot.Find("PreviewImage") as RectTransform;
            if (previewImageRect == null)
            {
                previewImageRect = CreateUiObject("PreviewImage", slotRoot);
            }

            previewImageRect.anchorMin = new Vector2(0.5f, 0.5f);
            previewImageRect.anchorMax = new Vector2(0.5f, 0.5f);
            previewImageRect.pivot = new Vector2(0.5f, 0.5f);
            previewImageRect.anchoredPosition = Vector2.zero;
            previewImageRect.sizeDelta = new Vector2(104f, 104f);

            AspectRatioFitter aspectRatioFitter = previewImageRect.GetComponent<AspectRatioFitter>();
            if (aspectRatioFitter == null)
            {
                aspectRatioFitter = previewImageRect.gameObject.AddComponent<AspectRatioFitter>();
            }

            aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspectRatioFitter.aspectRatio = 1f;

            RawImage previewImage = previewImageRect.GetComponent<RawImage>();
            if (previewImage == null)
            {
                previewImage = previewImageRect.gameObject.AddComponent<RawImage>();
            }

            previewImage.color = new Color(1f, 1f, 1f, 0f);
            previewImage.raycastTarget = false;
            sceneNextPreviewSlots.Add(previewImage);
        }
    }

    private void ResolvePreviewSprites()
    {
#if UNITY_EDITOR
        pieceITexture = ResolvePreviewTexture(pieceITexture, "Assets/GUI/PieceI.png");
        pieceJTexture = ResolvePreviewTexture(pieceJTexture, "Assets/GUI/PieceJ.png");
        pieceLTexture = ResolvePreviewTexture(pieceLTexture, "Assets/GUI/PieceL.png");
        pieceOTexture = ResolvePreviewTexture(pieceOTexture, "Assets/GUI/PieceO.png");
        pieceSTexture = ResolvePreviewTexture(pieceSTexture, "Assets/GUI/PieceS.png");
        pieceTTexture = ResolvePreviewTexture(pieceTTexture, "Assets/GUI/PieceT.png");
        pieceZTexture = ResolvePreviewTexture(pieceZTexture, "Assets/GUI/PieceZ.png");
#endif
    }

    private void ResolvePreviewRenderTextures()
    {
#if UNITY_EDITOR
        pieceIRenderTexture = ResolvePreviewRenderTexture(pieceIRenderTexture, "Assets/RenderTextures/RTPieceI.renderTexture");
        pieceJRenderTexture = ResolvePreviewRenderTexture(pieceJRenderTexture, "Assets/RenderTextures/RTPieceJ.renderTexture");
        pieceLRenderTexture = ResolvePreviewRenderTexture(pieceLRenderTexture, "Assets/RenderTextures/RTPieceL.renderTexture");
        pieceORenderTexture = ResolvePreviewRenderTexture(pieceORenderTexture, "Assets/RenderTextures/RTPieceO.renderTexture");
        pieceSRenderTexture = ResolvePreviewRenderTexture(pieceSRenderTexture, "Assets/RenderTextures/RTPieceS.renderTexture");
        pieceTRenderTexture = ResolvePreviewRenderTexture(pieceTRenderTexture, "Assets/RenderTextures/RTPieceT.renderTexture");
        pieceZRenderTexture = ResolvePreviewRenderTexture(pieceZRenderTexture, "Assets/RenderTextures/RTPieceZ.renderTexture");
#endif
    }

#if UNITY_EDITOR
    private static Texture2D ResolvePreviewTexture(Texture2D existingTexture, string assetPath)
    {
        if (existingTexture != null)
        {
            return existingTexture;
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static RenderTexture ResolvePreviewRenderTexture(RenderTexture existingRenderTexture, string assetPath)
    {
        if (existingRenderTexture != null)
        {
            return existingRenderTexture;
        }

        return AssetDatabase.LoadAssetAtPath<RenderTexture>(assetPath);
    }
#endif

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

    private static Canvas FindCanvasByName(string canvasName)
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Canvas foundCanvas in canvases)
        {
            if (string.Equals(foundCanvas.name, canvasName, StringComparison.OrdinalIgnoreCase))
            {
                return foundCanvas;
            }
        }

        return null;
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
        private readonly RectTransform gridRoot;
        private readonly AspectRatioFitter textureAspectRatioFitter;
        private readonly RawImage pieceTextureImage;
        private readonly Image[] cells;
        private readonly Text pieceCodeLabel;
        private readonly bool preferSpritePreview;

        public PiecePreviewWidget(Transform parent, Font font, float cellSize, float preferredHeight, bool preferSpritePreview = false)
        {
            this.preferSpritePreview = preferSpritePreview;

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

            RectTransform spriteRoot = CreateUiObject("Sprite", root);
            spriteRoot.anchorMin = new Vector2(0.5f, 0.5f);
            spriteRoot.anchorMax = new Vector2(0.5f, 0.5f);
            spriteRoot.pivot = new Vector2(0.5f, 0.5f);
            spriteRoot.sizeDelta = new Vector2(156f, preferredHeight - 34f);
            spriteRoot.anchoredPosition = new Vector2(0f, -5f);

            textureAspectRatioFitter = spriteRoot.gameObject.AddComponent<AspectRatioFitter>();
            textureAspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            textureAspectRatioFitter.aspectRatio = 1f;

            pieceTextureImage = spriteRoot.gameObject.AddComponent<RawImage>();
            pieceTextureImage.raycastTarget = false;
            pieceTextureImage.enabled = false;

            gridRoot = CreateUiObject("Grid", root);
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

        public void SetPiece(string pieceCode, Texture previewTexture = null)
        {
            string normalizedCode = string.IsNullOrWhiteSpace(pieceCode) ? string.Empty : pieceCode.Trim().ToUpperInvariant();
            PreviewDefinition definition = default;
            bool hasDefinition = normalizedCode.Length > 0 && PreviewDefinitions.TryGetValue(normalizedCode, out definition);
            string label = hasDefinition ? normalizedCode : "EMPTY";
            pieceCodeLabel.text = label;

            backgroundImage.color = hasDefinition
                ? Color.Lerp(PreviewBackgroundColor, definition.Color, 0.14f)
                : PreviewBackgroundColor;

            bool showTexture = preferSpritePreview && previewTexture != null && hasDefinition;
            if (showTexture)
            {
                textureAspectRatioFitter.aspectRatio = Mathf.Max(0.01f, (float)previewTexture.width / previewTexture.height);
            }

            pieceTextureImage.texture = previewTexture;
            pieceTextureImage.enabled = showTexture;
            gridRoot.gameObject.SetActive(!showTexture);

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].color = PreviewCellOffColor;
            }

            if (!hasDefinition || showTexture)
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
