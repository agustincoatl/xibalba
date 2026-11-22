using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gestor global persistente. Almacena el ultimo checkpoint fisico (independiente de transiciones de sala)
/// y los datos relevantes del jugador para restaurarlos al reintentar.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Escena de menu")]
    [SerializeField] private string mainMenuSceneName = "Menu";

    // Datos del ultimo checkpoint fisico registrado
    public bool HasCheckpoint { get; private set; }
    public string CheckpointScene { get; private set; }
    public Vector3 CheckpointPosition { get; private set; }

    // Estado del jugador guardado en el checkpoint
    public int SavedHealth { get; private set; }
    public int SavedMaxHealth { get; private set; }

    // UI de victoria generada en runtime
    private bool victoryShown;
    private GameObject victoryCanvas;
    private Text victoryScoreLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("GameManager");
        Instance = go.AddComponent<GameManager>();
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
        HideVictory();

        // Al volver al menu se empieza una partida limpia.
        if (scene.name == mainMenuSceneName)
            ResetRunProgress(true);
    }

    /// <summary>
    /// Guarda la posicion exacta, la escena actual y los datos relevantes del jugador.
    /// Llamado por Checkpoint al activarse.
    /// </summary>
    public void SaveCheckpoint(Vector3 position, PlayerController player)
    {
        HasCheckpoint = true;
        CheckpointScene = SceneManager.GetActiveScene().name;
        CheckpointPosition = position;

        if (player != null)
        {
            SavedMaxHealth = player.maxHealth;
            SavedHealth = player.currentHealth;
        }
    }

    /// <summary>
    /// Reintenta desde el ultimo checkpoint fisico. Si no hay checkpoint, recarga la escena actual desde el inicio.
    /// </summary>
    public void Retry()
    {
        Time.timeScale = 1f;
        StartCoroutine(RetryRoutine());
    }

    /// <summary>
    /// Muestra la pantalla de victoria y congela el juego.
    /// La llama VictoryOnDeath cuando muere el jefe 2, o cualquier script que termine la partida.
    /// </summary>
    public void Victory()
    {
        if (victoryShown) return;

        victoryShown = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureVictoryUI();
        if (victoryCanvas != null)
            victoryCanvas.SetActive(true);

        if (victoryScoreLabel != null)
            victoryScoreLabel.text = string.Format("Puntuacion final: {0}", ScoreManager.CurrentScore);
    }

    /// <summary>
    /// Permite llamar a Victory() sin tener una referencia directa al GameManager.
    /// </summary>
    public static void TriggerVictory()
    {
        if (Instance == null) Bootstrap();
        if (Instance != null) Instance.Victory();
    }

    /// <summary>
    /// Vuelve al menu principal.
    /// </summary>
    public void ExitToMainMenu()
    {
        Time.timeScale = 1f;
        StartCoroutine(ExitRoutine());
    }

    private IEnumerator RetryRoutine()
    {
        ResetRunProgress(false);
        HideVictory();

        // La muerte puede dejar el juego congelado; el reintento siempre empieza a tiempo normal.
        Time.timeScale = 1f;

        string currentScene = SceneManager.GetActiveScene().name;
        string targetScene = HasCheckpoint ? CheckpointScene : currentScene;

        // Cargar escena destino (o recargar si es la misma)
        AsyncOperation op = SceneManager.LoadSceneAsync(targetScene);
        while (!op.isDone) yield return null;

        // Esperar un frame para que la escena inicialice sus objetos
        yield return null;

        PlayerController player = FindPlayer();
        if (player == null) yield break;

        if (HasCheckpoint)
        {
            // Restaurar en la posicion exacta del checkpoint y atributos guardados
            TeleportPlayer(player, CheckpointPosition);
            RestorePlayerState(player, SavedHealth, SavedMaxHealth, true);
        }
        else
        {
            // Sin checkpoint fisico: el SpawnPoint de la escena es el punto de
            // guardado efectivo. Sin esto el jugador (DontDestroyOnLoad) se
            // quedaba donde murio, a menudo fuera del mapa.
            Vector3? spawn = FindSpawnPosition(targetScene);
            if (spawn.HasValue)
                TeleportPlayer(player, spawn.Value);

            RestorePlayerState(player, player.maxHealth, player.maxHealth, false);
        }

        // Que TransitionManager use el mismo destino si luego se llama a Respawn().
        if (TransitionManager.Instance != null)
            TransitionManager.Instance.SetCheckpoint(targetScene);

        player.enabled = true;
        UnfreezePlayer(player);

        player.ResetSafePosition();
    }

    /// <summary>
    /// Busca el SpawnPoint de la escena (id = nombre de escena, o el primero).
    /// </summary>
    private static Vector3? FindSpawnPosition(string sceneName)
    {
        SpawnPoint[] points = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude);
        SpawnPoint chosen = null;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null && points[i].id == sceneName)
            {
                chosen = points[i];
                break;
            }
        }

        if (chosen == null && points.Length > 0)
            chosen = points[0];

        return chosen != null ? chosen.transform.position : (Vector3?)null;
    }

    private IEnumerator ExitRoutine()
    {
        HideVictory();

        // Limpiar referencias persistentes de gameplay si existen para evitar duplicados al volver
        if (TransitionManager.Instance != null)
            Destroy(TransitionManager.Instance.gameObject);

        AsyncOperation op = SceneManager.LoadSceneAsync(mainMenuSceneName);
        while (!op.isDone) yield return null;
        yield return null;
        Time.timeScale = 1f;
    }

    private static PlayerController FindPlayer()
    {
        // Preferir el jugador gestionado por TransitionManager si existe
        if (TransitionManager.Instance != null && TransitionManager.Instance.PlayerTransform != null)
        {
            var pc = TransitionManager.Instance.PlayerTransform.GetComponent<PlayerController>();
            if (pc != null) return pc;
        }
        return UnityEngine.Object.FindAnyObjectByType<PlayerController>();
    }

    private static void TeleportPlayer(PlayerController player, Vector3 position)
    {
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.simulated = false;
            rb.linearVelocity = Vector2.zero;
        }

        player.transform.position = position;
        player.ResetSafePosition();
        Physics2D.SyncTransforms();

        if (rb != null)
        {
            rb.simulated = true;
        }
    }

    private static void RestorePlayerState(PlayerController player, int health, int maxHealth, bool useSavedMax)
    {
        if (useSavedMax && maxHealth > 0)
            player.maxHealth = maxHealth;

        int target = health > 0 ? health : player.maxHealth;
        player.Revive(target, false);
    }

    private static void UnfreezePlayer(PlayerController player)
    {
        // Revive se encarga de reactivar fisicas/collider/estado; aqui solo garantizamos visibilidad minima.
        if (player != null && player.IsDead)
            player.Revive(player.maxHealth, true);
    }

    /// <summary>
    /// Reinicia llaves, puertas y puntos. clearCheckpoint se usa al volver al menu
    /// para que una partida nueva empiece desde cero.
    /// </summary>
    private void ResetRunProgress(bool clearCheckpoint)
    {
        KeyInventory.ResetAll();
        LockedDoor.ResetAll();
        ScoreManager.ResetAll();
        MessageManager.Clear();

        if (clearCheckpoint)
            ClearCheckpoint();
    }

    /// <summary>
    /// Limpia el checkpoint guardado (opcional, por ejemplo al iniciar nueva partida).
    /// </summary>
    public void ClearCheckpoint()
    {
        HasCheckpoint = false;
        CheckpointScene = null;
        CheckpointPosition = default;
        SavedHealth = 0;
        SavedMaxHealth = 0;
    }

    private void HideVictory()
    {
        if (victoryCanvas != null)
            victoryCanvas.SetActive(false);

        victoryShown = false;
    }

    private void EnsureVictoryUI()
    {
        if (victoryCanvas != null) return;

        victoryCanvas = new GameObject("VictoryCanvas");
        victoryCanvas.transform.SetParent(transform, false);

        Canvas canvas = victoryCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;

        CanvasScaler scaler = victoryCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        victoryCanvas.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();

        GameObject panel = new GameObject("VictoryPanel");
        panel.transform.SetParent(victoryCanvas.transform, false);

        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.82f);

        CreateVictoryText(panel.transform, "VictoryTitle", "VICTORIA", 72,
            new Vector2(0f, 170f), new Color(0.95f, 0.82f, 0.35f));

        victoryScoreLabel = CreateVictoryText(panel.transform, "VictoryScore", "Puntuacion final: 0", 40,
            new Vector2(0f, 60f), Color.white);

        CreateVictoryButton(panel.transform, "VictoryMenuButton", "Volver al menu",
            new Vector2(0f, -80f), OnVictoryMenuPressed);

        victoryCanvas.SetActive(false);
    }

    private void OnVictoryMenuPressed()
    {
        HideVictory();
        ExitToMainMenu();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        // Objeto raiz: se destruye al cambiar de escena y no deja un EventSystem extra persistente.
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    private static Text CreateVictoryText(Transform parent, string name, string content, int fontSize,
        Vector2 anchoredPos, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(900f, 120f);

        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        return text;
    }

    private static Button CreateVictoryButton(Transform parent, string name, string label,
        Vector2 anchoredPos, UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(360f, 64f);

        Image image = go.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);

        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textGO.AddComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 30;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        return button;
    }
}
