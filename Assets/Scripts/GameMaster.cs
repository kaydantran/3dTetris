using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Simple progression and game-state coordinator. It tracks cleared layers for score/speed
/// and exposes a placeholder game-over entry point for the piece controller.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class GameMaster : MonoBehaviour
{
    [SerializeField] private ActivePieceController pieceController;

    [Tooltip("How many layers must be cleared before the drop speed increases.")]
    [SerializeField] private int layersPerSpeedBump = 3;

    [Header("Audio")]
    [SerializeField] private AudioClip placeBlockSound;
    [SerializeField] private AudioClip clearSound;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField, Range(0f, 1f)] private float placeBlockVolume = 0.75f;
    [SerializeField, Range(0f, 1f)] private float clearVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float explosionVolume = 0.95f;

    [Header("Game Over Explosion")]
    [SerializeField] private float gameOverLaunchDelay = 0.12f;
    [SerializeField] private float explosionDuration = 2.4f;
    [SerializeField, Range(0.05f, 1f)] private float gameOverSlowMotionScale = 0.2f;
    [SerializeField, Range(0f, 1f)] private float gameOverFlashPeakAlpha = 0.92f;
    [SerializeField] private float gameOverFlashDuration = 0.16f;
    [SerializeField] private float minLaunchSpeed = 8f;
    [SerializeField] private float maxLaunchSpeed = 16f;
    [SerializeField] private float upwardLaunchBias = 1.15f;
    [SerializeField] private float randomSpread = 0.9f;
    [SerializeField] private float gravity = 20f;
    [SerializeField] private float minSpinSpeed = 220f;
    [SerializeField] private float maxSpinSpeed = 640f;
    [SerializeField] private float trailLifetime = 0.55f;
    [SerializeField] private float trailStartWidth = 0.18f;
    [SerializeField] private float trailEndWidth = 0.01f;

    private int totalLayersCleared;
    private int layersSinceLastBump;
    private int totalPiecesLocked;
    private int score;
    private bool isGameOver;
    private float gameplayStartTime = -1f;
    private float finalElapsedGameplayTime;
    private float finalPiecesPerSecond;
    private AudioSource effectsAudioSource;
    private Material trailMaterial;
    private Coroutine gameOverRoutine;
    private Canvas flashCanvas;
    private Image flashImage;
    private float timeScaleBeforeSlowMotion = 1f;
    private bool isSlowMotionActive;

    public int Score => score;
    public int TotalLayersCleared => totalLayersCleared;
    public int TotalPiecesLocked => totalPiecesLocked;
    public float ElapsedGameplayTime => isGameOver ? finalElapsedGameplayTime : CalculateElapsedGameplayTime();
    public float PiecesPerSecond => isGameOver ? finalPiecesPerSecond : CalculatePiecesPerSecond();
    public bool IsGameOver => isGameOver;

    public event Action StatsChanged;

    private void Awake()
    {
#if UNITY_EDITOR
        ResolveAudioClips();
#endif

        if (pieceController == null)
        {
            pieceController = FindAnyObjectByType<ActivePieceController>();
        }

        effectsAudioSource = GetComponent<AudioSource>();
        effectsAudioSource.playOnAwake = false;
        effectsAudioSource.spatialBlend = 0f;
        effectsAudioSource.loop = false;

        if (FindAnyObjectByType<GameplayHudController>() == null)
        {
            Canvas existingHudCanvas = FindGameplayHudCanvas();
            if (existingHudCanvas != null)
            {
                existingHudCanvas.gameObject.AddComponent<GameplayHudController>();
            }
            else
            {
                GameObject hudObject = new GameObject(
                    "GameplayHUD",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));

                hudObject.AddComponent<GameplayHudController>();
            }
        }
    }

    private void OnDestroy()
    {
        RestoreTimeScale();

        if (trailMaterial != null)
        {
            Destroy(trailMaterial);
        }

        if (flashCanvas != null)
        {
            Destroy(flashCanvas.gameObject);
        }
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        ResolveAudioClips();
#endif
    }

    private static Canvas FindGameplayHudCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Canvas existingCanvas in canvases)
        {
            if (string.Equals(existingCanvas.name, "HUD", StringComparison.OrdinalIgnoreCase))
            {
                return existingCanvas;
            }
        }

        return null;
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
        score += GetLineClearScore(count);

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

        finalElapsedGameplayTime = CalculateElapsedGameplayTime();
        finalPiecesPerSecond = CalculatePiecesPerSecond();
        isGameOver = true;
        NotifyStatsChanged();
        Debug.Log($"Game Over: {reason}");
    }

    public void PlayGameOverExplosion(string reason, Vector3 explosionOrigin, OrbitCamera orbitCamera, IEnumerable<Transform> cubes)
    {
        OnGameOver(reason);

        if (gameOverRoutine != null)
        {
            StopCoroutine(gameOverRoutine);
            RestoreTimeScale();
            SetFlashAlpha(0f);
        }

        List<Transform> fragments = new List<Transform>();
        if (cubes != null)
        {
            foreach (Transform cube in cubes)
            {
                if (cube != null)
                {
                    fragments.Add(cube);
                }
            }
        }

        gameOverRoutine = StartCoroutine(PlayGameOverExplosionRoutine(explosionOrigin, orbitCamera, fragments));
    }

    private float CalculateElapsedGameplayTime()
    {
        if (gameplayStartTime < 0f) return 0f;

        return Mathf.Max(0f, Time.time - gameplayStartTime);
    }

    private float CalculatePiecesPerSecond()
    {
        if (gameplayStartTime < 0f) return 0f;

        float elapsed = Mathf.Max(0.001f, CalculateElapsedGameplayTime());
        return totalPiecesLocked / elapsed;
    }

    private static int GetLineClearScore(int clearedLayers)
    {
        return clearedLayers switch
        {
            1 => 100,
            2 => 300,
            3 => 500,
            4 => 800,
            _ => 800 + Mathf.Max(0, clearedLayers - 4) * 300
        };
    }

    public void PlayPlaceBlockSound()
    {
        PlaySound(placeBlockSound, placeBlockVolume);
    }

    public void PlayClearSound()
    {
        PlaySound(clearSound, clearVolume);
    }

    public void PlayExplosionSound()
    {
        PlaySound(explosionSound, explosionVolume);
    }

    private void NotifyStatsChanged()
    {
        StatsChanged?.Invoke();
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (effectsAudioSource == null || clip == null) return;

        effectsAudioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private IEnumerator PlayGameOverExplosionRoutine(Vector3 explosionOrigin, OrbitCamera orbitCamera, List<Transform> cubes)
    {
        float launchDelay = Mathf.Max(0f, gameOverLaunchDelay);
        orbitCamera?.PlayGameOverImpact(launchDelay);

        if (launchDelay > 0f)
        {
            yield return PlayGameOverIntroBeat(launchDelay);
        }
        else
        {
            SetFlashAlpha(0f);
        }

        if (cubes == null || cubes.Count == 0)
        {
            gameOverRoutine = null;
            yield break;
        }

        PlayExplosionSound();
        StartCoroutine(PlayExplosionFlash());

        List<ExplosionCube> fragments = new List<ExplosionCube>(cubes.Count);
        float launchDuration = Mathf.Max(0.1f, explosionDuration);
        float gravityAcceleration = Mathf.Max(0f, gravity);

        for (int i = 0; i < cubes.Count; i++)
        {
            Transform cube = cubes[i];
            if (cube == null) continue;

            cube.SetParent(null, worldPositionStays: true);
            AttachTrail(cube.gameObject);
            fragments.Add(new ExplosionCube(
                cube,
                BuildLaunchVelocity(cube.position, explosionOrigin),
                UnityEngine.Random.onUnitSphere,
                UnityEngine.Random.Range(minSpinSpeed, maxSpinSpeed)));
        }

        float elapsed = 0f;
        while (elapsed < launchDuration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            for (int i = 0; i < fragments.Count; i++)
            {
                ExplosionCube fragment = fragments[i];
                if (fragment.Transform == null) continue;

                fragment.Velocity += Vector3.down * gravityAcceleration * dt;
                fragment.Transform.position += fragment.Velocity * dt;
                fragment.Transform.Rotate(fragment.SpinAxis, fragment.SpinSpeed * dt, Space.World);
                fragments[i] = fragment;
            }

            yield return null;
        }

        for (int i = 0; i < fragments.Count; i++)
        {
            ExplosionCube fragment = fragments[i];
            if (fragment.Transform == null) continue;

            Destroy(fragment.Transform.gameObject);
        }

        gameOverRoutine = null;
    }

    private IEnumerator PlayGameOverIntroBeat(float duration)
    {
        BeginSlowMotion();

        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.0001f, duration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        RestoreTimeScale();
    }

    private IEnumerator PlayExplosionFlash()
    {
        float duration = Mathf.Max(0.01f, gameOverFlashDuration);
        float elapsed = 0f;

        SetFlashAlpha(gameOverFlashPeakAlpha);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float alpha = gameOverFlashPeakAlpha * (1f - normalized) * (1f - normalized);
            SetFlashAlpha(alpha);
            yield return null;
        }

        SetFlashAlpha(0f);
    }

    private Vector3 BuildLaunchVelocity(Vector3 cubePosition, Vector3 explosionOrigin)
    {
        Vector3 awayFromCenter = cubePosition - explosionOrigin;
        Vector3 horizontalDirection = Vector3.ProjectOnPlane(awayFromCenter, Vector3.up);
        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            Vector3 randomPlanar = UnityEngine.Random.insideUnitSphere;
            horizontalDirection = Vector3.ProjectOnPlane(randomPlanar, Vector3.up);
        }

        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            horizontalDirection = Vector3.forward;
        }

        Vector3 launchDirection =
            horizontalDirection.normalized +
            Vector3.up * upwardLaunchBias +
            UnityEngine.Random.insideUnitSphere * randomSpread;

        if (launchDirection.sqrMagnitude < 0.0001f)
        {
            launchDirection = Vector3.up;
        }

        float launchSpeed = UnityEngine.Random.Range(minLaunchSpeed, maxLaunchSpeed);
        return launchDirection.normalized * launchSpeed;
    }

    private TrailRenderer AttachTrail(GameObject cubeObject)
    {
        TrailRenderer trail = cubeObject.GetComponent<TrailRenderer>();
        if (trail == null)
        {
            trail = cubeObject.AddComponent<TrailRenderer>();
        }

        trail.time = Mathf.Max(0.05f, trailLifetime);
        trail.minVertexDistance = 0.02f;
        trail.widthMultiplier = 1f;
        trail.autodestruct = false;
        trail.emitting = true;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.material = GetTrailMaterial();
        trail.startWidth = Mathf.Max(0.01f, trailStartWidth);
        trail.endWidth = Mathf.Max(0f, trailEndWidth);
        trail.colorGradient = BuildTrailGradient();
        trail.Clear();
        return trail;
    }

    private void BeginSlowMotion()
    {
        if (isSlowMotionActive) return;

        timeScaleBeforeSlowMotion = Time.timeScale;
        Time.timeScale = Mathf.Clamp(gameOverSlowMotionScale, 0.05f, 1f);
        isSlowMotionActive = true;
    }

    private void RestoreTimeScale()
    {
        if (!isSlowMotionActive) return;

        Time.timeScale = timeScaleBeforeSlowMotion;
        isSlowMotionActive = false;
    }

    private void SetFlashAlpha(float alpha)
    {
        Image overlay = EnsureFlashOverlay();
        if (overlay == null) return;

        Color color = overlay.color;
        color.a = Mathf.Clamp01(alpha);
        overlay.color = color;
        overlay.enabled = color.a > 0.001f;
    }

    private Image EnsureFlashOverlay()
    {
        if (flashImage != null) return flashImage;

        GameObject canvasObject = new GameObject("GameOverFlashCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        flashCanvas = canvasObject.GetComponent<Canvas>();
        flashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        flashCanvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject imageObject = new GameObject("GameOverFlash", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        flashImage = imageObject.GetComponent<Image>();
        flashImage.color = new Color(1f, 1f, 1f, 0f);
        flashImage.enabled = false;

        return flashImage;
    }

    private Material GetTrailMaterial()
    {
        if (trailMaterial != null) return trailMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        trailMaterial = shader != null
            ? new Material(shader) { color = Color.white }
            : new Material(Shader.Find("Standard")) { color = Color.white };

        return trailMaterial;
    }

    private Gradient BuildTrailGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.55f, 0.3f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

#if UNITY_EDITOR
    private void ResolveAudioClips()
    {
        if (placeBlockSound == null)
        {
            placeBlockSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/placeBlock.mp3");
        }

        if (clearSound == null)
        {
            clearSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/clear.mp3");
        }

        if (explosionSound == null)
        {
            explosionSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/explosion.mp3");
        }
    }
#endif

    private struct ExplosionCube
    {
        public ExplosionCube(Transform transform, Vector3 velocity, Vector3 spinAxis, float spinSpeed)
        {
            Transform = transform;
            Velocity = velocity;
            SpinAxis = spinAxis.sqrMagnitude > 0.0001f ? spinAxis.normalized : Vector3.up;
            SpinSpeed = spinSpeed;
        }

        public Transform Transform { get; }
        public Vector3 Velocity { get; set; }
        public Vector3 SpinAxis { get; }
        public float SpinSpeed { get; }
    }
}
