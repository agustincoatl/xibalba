using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class TransitionManager : MonoBehaviour
{
    public static TransitionManager Instance { get; private set; }

    [SerializeField] private PlayerController player;
    [SerializeField] private CanvasGroup fadeGroup;
    [SerializeField] private float fadeTime = 0.3f;

    // Último checkpoint (para respawn cuando mueres)
    private string checkpointScene;
    private string checkpointSpawn;

    private bool busy;
    public Transform PlayerTransform => player != null ? player.transform : null;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; } // evita duplicados al volver a una sala
        Instance = this;
        DontDestroyOnLoad(gameObject);                        // debe ser un objeto raíz de la jerarquía

        SceneManager.sceneLoaded += OnSceneLoaded;
        checkpointScene = SceneManager.GetActiveScene().name;
        checkpointSpawn = "Inicio";
        RebindPlayer();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindPlayer();
    }

    private void RebindPlayer()
    {
        if (player == null)
            player = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
    }

    public void GoTo(string sceneName, string spawnId)
    {
        if (busy) return;
        AudioManager.PlayCambioEscena();
        StartCoroutine(TransitionRoutine(sceneName, spawnId, false));
    }

    public void SetCheckpoint(string spawnId)
    {
        checkpointScene = SceneManager.GetActiveScene().name;
        checkpointSpawn = spawnId;
    }

    public void Respawn()
    {
        if (busy) return;
        RebindPlayer();
        StartCoroutine(TransitionRoutine(checkpointScene, checkpointSpawn, true));
    }

    private IEnumerator TransitionRoutine(string sceneName, string spawnId, bool restoreHealth)
    {
        busy = true;
        RebindPlayer();
        if (player == null)
        {
            busy = false;
            yield break;
        }
        var rb = player.GetComponent<Rigidbody2D>();

        player.enabled = false;               // sin control durante la transición
        rb.linearVelocity = Vector2.zero;
        rb.simulated = false;

        yield return Fade(1f);

        yield return SceneManager.LoadSceneAsync(sceneName);
        RebindPlayer();
        if (player == null)
        {
            busy = false;
            yield break;
        }

        bool hasWorldCheckpoint = restoreHealth &&
            GameManager.Instance != null &&
            GameManager.Instance.HasCheckpoint &&
            GameManager.Instance.CheckpointScene == sceneName;

        if (hasWorldCheckpoint)
        {
            // El checkpoint fisico es la autoridad para un retry. No usar el
            // spawn "Inicio", que puede pertenecer a otra sala o escena.
            player.transform.position = GameManager.Instance.CheckpointPosition;
        }
        else
        {
            SpawnPoint chosen = null;
            foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude))
            {
                if (sp.id == spawnId || sp.id == sceneName)
                {
                    chosen = sp;
                    break;
                }
            }

            // Fallback: si el id guardado no existe en esta escena, usar cualquier
            // SpawnPoint para no dejar al jugador fuera del mapa.
            if (chosen == null)
            {
                foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude))
                {
                    chosen = sp;
                    break;
                }
            }

            if (chosen != null)
                player.transform.position = chosen.transform.position;
        }

        Physics2D.SyncTransforms();
        player.ResetSafePosition();

        if (restoreHealth) player.Revive(player.maxHealth, true);

        yield return null;                    // deja que la cámara se acomode
        rb.simulated = true;
        player.enabled = true;

        yield return Fade(0f);
        busy = false;
    }

    private IEnumerator Fade(float target)
    {
        float start = fadeGroup.alpha;
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(start, target, t / fadeTime);
            yield return null;
        }
        fadeGroup.alpha = target;
    }

    public void ResetSafePosition()
    {
        if (player != null) player.ResetSafePosition();
    }
}
