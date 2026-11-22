using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Muestra mensajes de progreso en pantalla usando una cola.
/// Los mensajes nuevos esperan su turno: nunca pisan al que se esta mostrando.
/// Si no hay UI asignada, crea un canvas basico en runtime.
/// </summary>
public class MessageManager : MonoBehaviour
{
    public static MessageManager Instance { get; private set; }

    [Header("UI (opcional: si se deja vacio se genera en runtime)")]
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private Text messageText;

    [Header("Configuracion")]
    [SerializeField] private float defaultDuration = 2f;
    [SerializeField] private float fadeTime = 0.15f;

    private readonly Queue<MessageRequest> pending = new Queue<MessageRequest>();
    private Coroutine displayRoutine;
    private GameObject runtimeCanvas;

    private struct MessageRequest
    {
        public string text;
        public float duration;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Con "Reload Domain" desactivado, las estaticas sobreviven entre sesiones de Play.
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static MessageManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        GameObject go = new GameObject("MessageManager");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<MessageManager>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Encola un mensaje. Si ya hay uno visible, este espera su turno.
    /// </summary>
    public static void Show(string message, float duration = -1f)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        MessageManager manager = EnsureInstance();
        manager.Enqueue(message, duration);
    }

    /// <summary>
    /// Limpia la cola y oculta el mensaje actual (por ejemplo al reintentar).
    /// </summary>
    public static void Clear()
    {
        if (Instance == null) return;
        Instance.ClearInternal();
    }

    private void Enqueue(string message, float duration)
    {
        pending.Enqueue(new MessageRequest
        {
            text = message,
            duration = duration > 0f ? duration : defaultDuration
        });

        if (displayRoutine == null)
            displayRoutine = StartCoroutine(DisplayRoutine());
    }

    private IEnumerator DisplayRoutine()
    {
        EnsureUI();

        while (pending.Count > 0)
        {
            MessageRequest request = pending.Dequeue();

            panelGroup.gameObject.SetActive(true);
            messageText.text = request.text;
            panelGroup.alpha = 0f;

            yield return Fade(0f, 1f);

            if (request.duration > 0f)
                yield return new WaitForSecondsRealtime(request.duration);

            yield return Fade(1f, 0f);
        }

        panelGroup.alpha = 0f;
        panelGroup.gameObject.SetActive(false);
        displayRoutine = null;
    }

    private IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            panelGroup.alpha = Mathf.Lerp(from, to, t / fadeTime);
            yield return null;
        }

        panelGroup.alpha = to;
    }

    private void ClearInternal()
    {
        pending.Clear();

        if (displayRoutine != null)
        {
            StopCoroutine(displayRoutine);
            displayRoutine = null;
        }

        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            panelGroup.gameObject.SetActive(false);
        }
    }

    private void EnsureUI()
    {
        if (panelGroup == null)
            CreateRuntimeUI();

        if (messageText == null)
        {
            messageText = panelGroup.GetComponentInChildren<Text>(true);
            if (messageText == null)
                messageText = CreateText(panelGroup.transform);
        }
    }

    private void CreateRuntimeUI()
    {
        runtimeCanvas = new GameObject("MessageCanvas");
        runtimeCanvas.transform.SetParent(transform, false);

        Canvas canvas = runtimeCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;

        CanvasScaler scaler = runtimeCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject panel = new GameObject("MessagePanel");
        panel.transform.SetParent(runtimeCanvas.transform, false);

        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 120f);
        panelRect.sizeDelta = new Vector2(900f, 110f);

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.7f);

        panelGroup = panel.AddComponent<CanvasGroup>();
        panelGroup.alpha = 0f;
        panelGroup.interactable = false;
        panelGroup.blocksRaycasts = false;

        messageText = CreateText(panel.transform);
        panel.SetActive(false);
    }

    private static Text CreateText(Transform parent)
    {
        GameObject go = new GameObject("MessageText");
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(24f, 10f);
        rect.offsetMax = new Vector2(-24f, -10f);

        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 30;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 18;
        text.resizeTextMaxSize = 30;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        return text;
    }
}
