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
    public Transform PlayerTransform => player.transform;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; } // evita duplicados al volver a una sala
        Instance = this;
        DontDestroyOnLoad(gameObject);                        // debe ser un objeto raíz de la jerarquía

        checkpointScene = SceneManager.GetActiveScene().name;
        checkpointSpawn = "Inicio";
    }

    public void GoTo(string sceneName, string spawnId)
    {
        if (busy) return;
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
        StartCoroutine(TransitionRoutine(checkpointScene, checkpointSpawn, true));
    }

    private IEnumerator TransitionRoutine(string sceneName, string spawnId, bool restoreHealth)
    {
        busy = true;
        var rb = player.GetComponent<Rigidbody2D>();

        player.enabled = false;               // sin control durante la transición
        rb.linearVelocity = Vector2.zero;
        rb.simulated = false;

        yield return Fade(1f);

        yield return SceneManager.LoadSceneAsync(sceneName);

        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude))
        {
            if (sp.id == spawnId)
            {
                player.transform.position = sp.transform.position;
                break;
            }
        }

        if (restoreHealth) player.currentHealth = player.maxHealth;

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
}