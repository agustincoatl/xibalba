using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barra de vida + nombre del jefe. Se crea sola en pantalla cuando el jugador
/// entra al rango de encuentro y se subscribe a los eventos del BossController.
/// Si ya tenes tu propio Canvas, asigna root / nameText / fillImage en el
/// inspector y no se crea nada en runtime.
/// </summary>
public class BossHealthUI : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private BossController boss;
    [SerializeField] private PlayerController player;

    [Header("Aparicion")]
    [Tooltip("Distancia al jefe para mostrar la barra.")]
    [SerializeField] private float encounterRange = 13f;
    [SerializeField] private bool keepVisibleAfterEncounter = true;
    [SerializeField] private float fadeSpeed = 5f;

    [Header("Nombre")]
    [SerializeField] private string bossName = "CAMAZOTZ";
    [SerializeField] private Color nameColor = new Color(1f, 0.86f, 0.65f);
    [SerializeField] private int nameFontSize = 24;
    [SerializeField] private Font customFont;

    [Header("Barra")]
    [SerializeField] private Color fillColor = new Color(0.78f, 0.12f, 0.22f, 1f);
    [SerializeField] private Color enragedFillColor = new Color(1f, 0.38f, 0.12f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.02f, 0.05f, 0.85f);
    [SerializeField] private Vector2 barSize = new Vector2(420f, 18f);
    [Tooltip("Desplazamiento desde el centro superior de la pantalla.")]
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, -70f);
    [SerializeField] private float fillLerpSpeed = 8f;

    [Header("UI ya existente (opcional)")]
    [SerializeField] private RectTransform root;
    [SerializeField] private Text nameText;
    [SerializeField] private Image fillImage;
    [SerializeField] private CanvasGroup canvasGroup;

    private float targetFill = 1f;
    private bool encountered;
    private bool builtByScript;

    public BossController Boss => boss;

    private void Awake()
    {
        if (boss == null) boss = GetComponent<BossController>();
        if (player == null) player = FindAnyObjectByType<PlayerController>();

        if (root == null || nameText == null || fillImage == null)
            BuildUI();

        if (nameText != null)
        {
            nameText.text = bossName;
            nameText.color = nameColor;
        }
        if (fillImage != null) fillImage.color = fillColor;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        if (boss == null) boss = GetComponent<BossController>();
        if (boss == null) return;

        boss.HealthChanged += OnHealthChanged;
        boss.PhaseChanged += OnPhaseChanged;
        boss.Defeated += OnDefeated;
    }

    private void Start()
    {
        if (boss == null) return;
        targetFill = boss.MaxHealth > 0 ? (float)boss.Health / boss.MaxHealth : 0f;
        if (fillImage != null) fillImage.fillAmount = targetFill;
    }

    private void OnDisable()
    {
        if (boss == null) return;
        boss.HealthChanged -= OnHealthChanged;
        boss.PhaseChanged -= OnPhaseChanged;
        boss.Defeated -= OnDefeated;
    }

    private void OnDestroy()
    {
        if (builtByScript && root != null)
            Destroy(root.gameObject);
    }

    private void Update()
    {
        if (boss == null || player == null) return;

        if (!encountered && !boss.IsDead &&
            Vector2.Distance(player.transform.position, boss.transform.position) <= encounterRange)
        {
            encountered = true;
        }

        bool visible = encountered && (!boss.IsDead || keepVisibleAfterEncounter);

        if (canvasGroup != null)
        {
            CanvasGroup group = canvasGroup;
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        if (fillImage != null && !Mathf.Approximately(fillImage.fillAmount, targetFill))
        {
            fillImage.fillAmount = Mathf.MoveTowards(
                fillImage.fillAmount, targetFill, fillLerpSpeed * Time.deltaTime);
        }
    }

    private void OnHealthChanged(int current, int max)
    {
        targetFill = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
    }

    private void OnPhaseChanged()
    {
        if (fillImage != null) fillImage.color = enragedFillColor;
        if (nameText != null) nameText.text = bossName + "  —  ENRAGED";
    }

    private void OnDefeated()
    {
        targetFill = 0f;
        if (nameText != null) nameText.text = bossName + "  —  DERROTADO";
    }

    /// <summary>Permite cablear el jefe desde otro script sin tocar el inspector.</summary>
    public void Configure(BossController target)
    {
        if (boss == target) return;
        if (boss != null)
        {
            boss.HealthChanged -= OnHealthChanged;
            boss.PhaseChanged -= OnPhaseChanged;
            boss.Defeated -= OnDefeated;
        }
        boss = target;
        OnEnable();
    }

    private void BuildUI()
    {
        builtByScript = true;

        GameObject canvasObject = new GameObject("BossHealthCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasObject.AddComponent<CanvasGroup>();

        root = canvasObject.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = screenOffset;
        root.sizeDelta = new Vector2(barSize.x, barSize.y + 40f);

        Font font = customFont != null ? customFont : GetBuiltinFont();

        GameObject nameObject = new GameObject("BossName", typeof(RectTransform));
        nameObject.transform.SetParent(root, false);
        RectTransform nameRect = nameObject.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0.5f, 1f);
        nameRect.anchorMax = new Vector2(0.5f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.anchoredPosition = Vector2.zero;
        nameRect.sizeDelta = new Vector2(barSize.x, 34f);

        nameText = nameObject.AddComponent<Text>();
        nameText.font = font;
        nameText.fontSize = nameFontSize;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        nameText.verticalOverflow = VerticalWrapMode.Overflow;
        nameText.raycastTarget = false;

        GameObject backObject = new GameObject("BarBackground", typeof(RectTransform));
        backObject.transform.SetParent(root, false);
        RectTransform backRect = backObject.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 1f);
        backRect.anchorMax = new Vector2(0.5f, 1f);
        backRect.pivot = new Vector2(0.5f, 1f);
        backRect.anchoredPosition = new Vector2(0f, -34f);
        backRect.sizeDelta = barSize;

        Image background = backObject.AddComponent<Image>();
        background.sprite = BossFx.White;
        background.color = backgroundColor;
        background.raycastTarget = false;

        GameObject fillObject = new GameObject("BarFill", typeof(RectTransform));
        fillObject.transform.SetParent(backRect, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        fillImage = fillObject.AddComponent<Image>();
        fillImage.sprite = BossFx.White;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 1f;
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;
    }

    private static Font GetBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) return font;
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
