using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class HealthUI3DController : MonoBehaviour
{
    [Header("Disposición")]
    [SerializeField] private float heartHeight = 1f;
    [SerializeField] private float heartSpacing = 1.28f;
    [SerializeField] private float rowPadding = 0.3f;
    [SerializeField] private float rowHeight = 1.5f;
    [SerializeField] private float displayHeight = 96f;
    [SerializeField] private Vector2 screenMargin = new Vector2(16f, -8f);
    [SerializeField] private int renderTextureHeight = 360;

    [Header("Animación")]
    [SerializeField] private float turnDuration = 0.45f;
    [SerializeField] private float turnDegrees = 360f;
    [SerializeField] private float scalePunch = 0.18f;
    [SerializeField] private float iconStagger = 0.08f;
    [SerializeField] private float idleAngle = 8f;
    [SerializeField] private float idleSpeed = 1.2f;
    [SerializeField] private float idlePhase = 0.9f;
    [SerializeField] private float idlePulse = 0.02f;
    [SerializeField] private float pulseSpeed = 1.8f;

    private const int HudLayerFallback = 9;
    private const string HeartResourcePath = "Heart3D";
    private const string FullMaterialPath = "HeartFull";
    private const string OffMaterialPath = "HeartOff";

    private PlayerController player;
    private int hudLayer;
    private bool scaffoldReady;
    private bool hudReady;
    private int builtMaxHealth = -1;
    private int lastHealth = -1;
    private float nextPlayerSearch;

    private GameObject scaffoldRoot;
    private Canvas canvas;
    private RawImage rawImage;
    private Camera hudCamera;
    private RenderTexture renderTexture;
    private Transform stage;
    private GameObject heartPrefab;
    private Material fullMaterial;
    private Material offMaterial;
    private HeartVisual[] hearts;
    private Coroutine[] animations;
    private Coroutine bindRoutine;
    private HealthUIController legacyHud;

    private class HeartVisual
    {
        public Transform root;
        public Renderer[] renderers;
        public Material[][] fullSets;
        public Material[][] offSets;
        public bool isOn;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<HealthUI3DController>() != null) return;

        GameObject go = new GameObject("HealthUI3D");
        DontDestroyOnLoad(go);
        go.AddComponent<HealthUI3DController>();
    }

    private void Awake()
    {
        hudLayer = LayerMask.NameToLayer("HeartHUD");
        if (hudLayer < 0) hudLayer = HudLayerFallback;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        StartBindRoutine();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ReleaseRenderTexture();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartBindRoutine();
    }

    private void StartBindRoutine()
    {
        if (bindRoutine != null) StopCoroutine(bindRoutine);
        bindRoutine = StartCoroutine(BindNextFrame());
    }

    private IEnumerator BindNextFrame()
    {
        // Espera a que los Start() de la escena hayan inicializado la vida del jugador.
        yield return null;
        bindRoutine = null;
        BindToScene();
    }

    private void BindToScene()
    {
        PlayerController found = FindAnyObjectByType<PlayerController>();
        if (found == null)
        {
            player = null;
            SetHudVisible(false);
            return;
        }

        BindPlayer(found);
    }

    private void BindPlayer(PlayerController newPlayer)
    {
        player = newPlayer;

        if (!scaffoldReady && !BuildScaffold())
        {
            SetHudVisible(false);
            return;
        }

        int maxHealth = Mathf.Max(1, player.maxHealth);
        if (!hudReady || builtMaxHealth != maxHealth)
        {
            if (!BuildHearts(maxHealth))
            {
                SetHudVisible(false);
                return;
            }
        }

        DisableLegacyHud();
        ApplyLayerCulling();
        SetHudVisible(true);
        SetHealthInstant(player.currentHealth);
    }

    private void Update()
    {
        if (player == null)
        {
            if (!hudReady) return;

            if (Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + 0.5f;
                PlayerController found = FindAnyObjectByType<PlayerController>();
                if (found != null) BindPlayer(found);
            }
            return;
        }

        if (!hudReady || hearts == null || hearts.Length == 0) return;

        int health = Mathf.Clamp(player.currentHealth, 0, hearts.Length);

        if (lastHealth < 0)
        {
            SetHealthInstant(health);
            return;
        }

        if (health != lastHealth)
        {
            int previous = lastHealth;
            lastHealth = health;

            if (health < previous)
            {
                // Al apagarse, empieza por el corazón de la derecha.
                for (int i = previous - 1; i >= health; i--)
                {
                    float delay = (previous - 1 - i) * iconStagger;
                    StartHeartAnimation(i, false, delay);
                }
            }
            else
            {
                // Al encenderse, empieza por la izquierda.
                for (int i = previous; i < health; i++)
                {
                    float delay = (i - previous) * iconStagger;
                    StartHeartAnimation(i, true, delay);
                }
            }
        }

        for (int i = 0; i < hearts.Length; i++)
        {
            if (animations[i] != null) continue;

            Transform root = hearts[i].root;
            if (root == null) continue;

            root.localRotation = Quaternion.Euler(0f, GetIdleAngle(i), 0f);
            root.localScale = Vector3.one * GetIdlePulse(i);
        }
    }

    private bool BuildScaffold()
    {
        heartPrefab = Resources.Load<GameObject>(HeartResourcePath);
        fullMaterial = Resources.Load<Material>(FullMaterialPath);
        offMaterial = Resources.Load<Material>(OffMaterialPath);

        if (heartPrefab == null)
        {
            Debug.LogError("[HealthUI3D] No se encontró Resources/Heart3D.fbx.");
            return false;
        }

        if (fullMaterial == null || offMaterial == null)
        {
            Debug.LogError("[HealthUI3D] Faltan los materiales HeartFull/HeartOff en Resources.");
            return false;
        }

        scaffoldRoot = new GameObject("HeartHUD");
        scaffoldRoot.transform.SetParent(transform, false);
        scaffoldRoot.transform.localPosition = Vector3.zero;
        scaffoldRoot.transform.localRotation = Quaternion.identity;
        scaffoldRoot.transform.localScale = Vector3.one;

        GameObject canvasGO = new GameObject("HeartHUDCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(scaffoldRoot.transform, false);
        canvasGO.layer = 5;

        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject rawGO = new GameObject("HeartHUDRawImage", typeof(RectTransform));
        rawGO.transform.SetParent(canvasGO.transform, false);
        rawGO.layer = 5;

        rawImage = rawGO.AddComponent<RawImage>();
        rawImage.raycastTarget = false;
        rawImage.color = Color.white;

        RectTransform rawRect = rawImage.rectTransform;
        rawRect.anchorMin = new Vector2(0f, 1f);
        rawRect.anchorMax = new Vector2(0f, 1f);
        rawRect.pivot = new Vector2(0f, 1f);
        rawRect.anchoredPosition = screenMargin;
        rawRect.sizeDelta = new Vector2(displayHeight, displayHeight);

        GameObject stageGO = new GameObject("HeartHUDStage");
        stageGO.transform.SetParent(scaffoldRoot.transform, false);
        stageGO.transform.localPosition = Vector3.zero;
        stageGO.transform.localRotation = Quaternion.identity;
        stageGO.transform.localScale = Vector3.one;
        stage = stageGO.transform;

        GameObject cameraGO = new GameObject("HeartHUDCamera");
        cameraGO.transform.SetParent(scaffoldRoot.transform, false);
        cameraGO.transform.localPosition = new Vector3(0f, 0f, -10f);
        cameraGO.transform.localRotation = Quaternion.identity;
        cameraGO.transform.localScale = Vector3.one;

        hudCamera = cameraGO.AddComponent<Camera>();
        hudCamera.clearFlags = CameraClearFlags.SolidColor;
        hudCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        hudCamera.orthographic = true;
        hudCamera.nearClipPlane = 0.1f;
        hudCamera.farClipPlane = 100f;
        hudCamera.cullingMask = 1 << hudLayer;
        hudCamera.depth = -10f;
        hudCamera.allowHDR = false;
        hudCamera.allowMSAA = false;
        hudCamera.useOcclusionCulling = false;
        hudCamera.enabled = false;

        UniversalAdditionalCameraData cameraData = cameraGO.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.antialiasing = AntialiasingMode.None;
        cameraData.requiresDepthTexture = false;
        cameraData.requiresColorTexture = false;

        scaffoldReady = true;
        return true;
    }

    private bool BuildHearts(int count)
    {
        hudReady = false;
        StopAllHeartAnimations();

        if (hearts != null)
        {
            for (int i = 0; i < hearts.Length; i++)
            {
                if (hearts[i] == null || hearts[i].root == null) continue;
                hearts[i].root.gameObject.SetActive(false);
                Destroy(hearts[i].root.gameObject);
            }
        }

        hearts = new HeartVisual[count];
        animations = new Coroutine[count];

        float rowWidthUnits = count * heartSpacing + rowPadding;

        for (int i = 0; i < count; i++)
        {
            GameObject rootGO = new GameObject("Heart_" + (i + 1));
            rootGO.transform.SetParent(stage, false);
            rootGO.transform.localPosition = new Vector3(
                (i - (count - 1) * 0.5f) * heartSpacing,
                0f,
                0f);
            rootGO.transform.localRotation = Quaternion.identity;
            rootGO.transform.localScale = Vector3.one;

            GameObject model = Instantiate(heartPrefab, rootGO.transform, false);
            model.name = "HeartModel";
            model.transform.localPosition = Vector3.zero;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError("[HealthUI3D] El modelo Heart3D no contiene ningún Renderer.");
                rootGO.SetActive(false);
                Destroy(rootGO);
                return false;
            }

            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
                bounds.Encapsulate(renderers[r].bounds);

            if (bounds.size.y <= 0.0001f)
            {
                Debug.LogError("[HealthUI3D] El modelo Heart3D tiene altura cero.");
                rootGO.SetActive(false);
                Destroy(rootGO);
                return false;
            }

            float scale = heartHeight / bounds.size.y;
            Vector3 centerLocal = rootGO.transform.InverseTransformPoint(bounds.center);
            model.transform.localScale = model.transform.localScale * scale;
            model.transform.localPosition = -centerLocal * scale;

            SetLayerRecursively(rootGO, hudLayer);

            HeartVisual visual = new HeartVisual
            {
                root = rootGO.transform,
                renderers = renderers,
                fullSets = new Material[renderers.Length][],
                offSets = new Material[renderers.Length][]
            };

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                int materialCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                visual.fullSets[r] = new Material[materialCount];
                visual.offSets[r] = new Material[materialCount];
                for (int m = 0; m < materialCount; m++)
                {
                    visual.fullSets[r][m] = fullMaterial;
                    visual.offSets[r][m] = offMaterial;
                }

                renderer.sharedMaterials = visual.fullSets[r];
            }

            hearts[i] = visual;
        }

        UpdateFraming(rowWidthUnits);
        builtMaxHealth = count;
        lastHealth = -1;
        hudReady = true;
        return true;
    }

    private void UpdateFraming(float rowWidthUnits)
    {
        float aspect = rowWidthUnits / rowHeight;
        int rtHeight = Mathf.Max(32, renderTextureHeight);
        int rtWidth = Mathf.Max(64, Mathf.RoundToInt(rtHeight * aspect));

        ReleaseRenderTexture();

        renderTexture = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32)
        {
            name = "HeartHUD_RenderTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false
        };
        renderTexture.Create();

        hudCamera.targetTexture = renderTexture;
        hudCamera.orthographicSize = rowHeight * 0.5f;

        rawImage.texture = renderTexture;
        rawImage.rectTransform.sizeDelta = new Vector2(displayHeight * aspect, displayHeight);
    }

    private void SetHealthInstant(int health)
    {
        if (hearts == null) return;

        int clamped = Mathf.Clamp(health, 0, hearts.Length);
        for (int i = 0; i < hearts.Length; i++)
        {
            if (animations != null && animations[i] != null)
            {
                StopCoroutine(animations[i]);
                animations[i] = null;
            }

            HeartVisual heart = hearts[i];
            if (heart == null || heart.root == null) continue;

            SetHeartMaterial(heart, i < clamped);
            heart.root.localRotation = Quaternion.Euler(0f, GetIdleAngle(i), 0f);
            heart.root.localScale = Vector3.one * GetIdlePulse(i);
        }

        lastHealth = clamped;
    }

    private void StartHeartAnimation(int index, bool turnOn, float delay)
    {
        if (animations == null || index < 0 || index >= animations.Length) return;
        if (animations[index] != null) StopCoroutine(animations[index]);
        animations[index] = StartCoroutine(AnimateHeart(index, turnOn, delay));
    }

    private IEnumerator AnimateHeart(int index, bool turnOn, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        HeartVisual heart = hearts[index];
        if (heart == null || heart.root == null)
        {
            animations[index] = null;
            yield break;
        }

        float duration = Mathf.Max(0.01f, turnDuration);
        float elapsed = 0f;
        bool swapped = false;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = t * t * (3f - 2f * t);
            float idle = GetIdleAngle(index);
            float pulse = GetIdlePulse(index);

            heart.root.localRotation = Quaternion.Euler(0f, idle + turnDegrees * ease, 0f);
            heart.root.localScale = Vector3.one * (pulse * (1f + Mathf.Sin(t * Mathf.PI) * scalePunch));

            if (!swapped && t >= 0.5f)
            {
                SetHeartMaterial(heart, turnOn);
                swapped = true;
            }

            yield return null;
        }

        SetHeartMaterial(heart, turnOn);
        heart.root.localRotation = Quaternion.Euler(0f, GetIdleAngle(index), 0f);
        heart.root.localScale = Vector3.one * GetIdlePulse(index);
        animations[index] = null;
    }

    private void SetHeartMaterial(HeartVisual heart, bool on)
    {
        if (heart == null || heart.renderers == null) return;

        Material[][] sets = on ? heart.fullSets : heart.offSets;
        if (sets == null) return;

        for (int r = 0; r < heart.renderers.Length && r < sets.Length; r++)
        {
            Renderer renderer = heart.renderers[r];
            if (renderer == null || sets[r] == null) continue;
            renderer.sharedMaterials = sets[r];
        }

        heart.isOn = on;
    }

    private float GetIdleAngle(int index)
    {
        return Mathf.Sin(Time.unscaledTime * idleSpeed + index * idlePhase) * idleAngle;
    }

    private float GetIdlePulse(int index)
    {
        return 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed + index * idlePhase) * idlePulse;
    }

    private void SetHudVisible(bool visible)
    {
        if (canvas != null) canvas.enabled = visible;
        if (hudCamera != null) hudCamera.enabled = visible;
    }

    private void DisableLegacyHud()
    {
        if (legacyHud == null)
            legacyHud = FindAnyObjectByType<HealthUIController>(FindObjectsInactive.Include);

        if (legacyHud == null) return;

        legacyHud.enabled = false;

        if (legacyHud.healthIcons != null && legacyHud.healthIcons.Count > 0)
        {
            Transform parent = null;
            for (int i = 0; i < legacyHud.healthIcons.Count; i++)
            {
                if (legacyHud.healthIcons[i] == null) continue;
                parent = legacyHud.healthIcons[i].transform.parent;
                break;
            }

            if (parent != null && parent == legacyHud.transform)
            {
                parent.gameObject.SetActive(false);
            }
            else
            {
                for (int i = 0; i < legacyHud.healthIcons.Count; i++)
                {
                    if (legacyHud.healthIcons[i] == null) continue;
                    legacyHud.healthIcons[i].gameObject.SetActive(false);
                }
            }
        }
        else
        {
            legacyHud.gameObject.SetActive(false);
        }
    }

    private void ApplyLayerCulling()
    {
        if (hudCamera == null) return;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
        int mask = ~(1 << hudLayer);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || cam == hudCamera) continue;
            cam.cullingMask &= mask;
        }
    }

    private void StopAllHeartAnimations()
    {
        if (animations == null) return;

        for (int i = 0; i < animations.Length; i++)
        {
            if (animations[i] == null) continue;
            StopCoroutine(animations[i]);
            animations[i] = null;
        }
    }

    private void ReleaseRenderTexture()
    {
        if (hudCamera != null) hudCamera.targetTexture = null;

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }

        if (rawImage != null) rawImage.texture = null;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;

        go.layer = layer;
        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }
}
