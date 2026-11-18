using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gestiona la pantalla de derrota: aparece al morir el jugador y ofrece Reintentar/Salir.
/// Si no se asigna un panel UI, genera un Canvas basico en runtime.
/// </summary>
public class GameOverManager : MonoBehaviour
{
    public static GameOverManager Instance { get; private set; }

    [Header("UI (opcional: si se deja vacio se genera en runtime)")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button exitButton;

    [Header("Configuracion")]
    [SerializeField] private string mainMenuSceneName = "Menu";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("GameOverManager");
        Instance = go.AddComponent<GameOverManager>();
        DontDestroyOnLoad(go);
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
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Hide();
    }

    /// <summary>
    /// Muestra la pantalla de derrota y congela el tiempo de juego.
    /// </summary>
    public void ShowGameOver()
    {
        EnsureUI();
        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        Time.timeScale = 0f;
        UnlockCursor();
    }

    public void Hide()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    public void OnRetryPressed()
    {
        Hide();
        EnsureGameManager();
        if (GameManager.Instance != null)
            GameManager.Instance.Retry();
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnExitPressed()
    {
        Hide();
        EnsureGameManager();
        if (GameManager.Instance != null)
            GameManager.Instance.ExitToMainMenu();
        else
            SceneManager.LoadScene(mainMenuSceneName);
    }

    private void EnsureGameManager()
    {
        if (GameManager.Instance != null) return;
        var gm = FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            GameObject go = new GameObject("GameManager");
            go.AddComponent<GameManager>();
        }
    }

    private void EnsureUI()
    {
        if (gameOverPanel != null) return;

        // Canvas
        GameObject canvasGO = new GameObject("GameOverCanvas");
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // Panel fondo
        gameOverPanel = new GameObject("GameOverPanel");
        gameOverPanel.transform.SetParent(canvasGO.transform, false);
        var panelRect = gameOverPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        var panelImg = gameOverPanel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);

        // Titulo
        CreateText(gameOverPanel.transform, "DerrotaText", "DERROTA", 64, new Vector2(0, 140));

        // Botones
        retryButton = CreateButton(gameOverPanel.transform, "RetryButton", "Reintentar", new Vector2(0, 10), OnRetryPressed);
        exitButton = CreateButton(gameOverPanel.transform, "ExitButton", "Salir", new Vector2(0, -80), OnExitPressed);

        gameOverPanel.SetActive(false);
    }

    private static void CreateText(Transform parent, string name, string text, int fontSize, Vector2 anchoredPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(600, 100);
        var txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(320, 60);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        GameObject txtGO = new GameObject("Text");
        txtGO.transform.SetParent(go.transform, false);
        var trect = txtGO.AddComponent<RectTransform>();
        trect.anchorMin = Vector2.zero;
        trect.anchorMax = Vector2.one;
        trect.offsetMin = Vector2.zero;
        trect.offsetMax = Vector2.zero;
        var txt = txtGO.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 32;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;

        return btn;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
