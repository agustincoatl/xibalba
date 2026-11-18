using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    /// Vuelve al menu principal.
    /// </summary>
    public void ExitToMainMenu()
    {
        Time.timeScale = 1f;
        StartCoroutine(ExitRoutine());
    }

    private IEnumerator RetryRoutine()
    {
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
            // Sin checkpoint: reinicio limpio desde el inicio de la escena
            RestorePlayerState(player, player.maxHealth, player.maxHealth, false);
        }

        player.enabled = true;
        UnfreezePlayer(player);

        player.ResetSafePosition();
    }

    private IEnumerator ExitRoutine()
    {
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
}
