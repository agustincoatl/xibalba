using UnityEngine;

/// <summary>
/// Punto de guardado fisico en el mapa (estatua, altar, punto de descanso).
/// Independiente del sistema de transiciones por SpawnPoint.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Checkpoint : MonoBehaviour
{
    [Header("Activacion")]
    [Tooltip("Si es true, se activa por colision/trigger. Si es false, requiere pulsar la tecla de interaccion dentro del area.")]
    [SerializeField] private bool activateOnContact = true;
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private bool saveOnlyOnce = true;

    [Header("Feedback (opcional)")]
    [SerializeField] private GameObject activeVisual;

    private bool activated;
    private bool playerInRange;
    private PlayerController playerRef;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        if (activeVisual != null) activeVisual.SetActive(false);
    }

    private void Update()
    {
        if (activateOnContact || !playerInRange) return;
        if (saveOnlyOnce && activated) return;

        if (Input.GetKeyDown(interactKey) && playerRef != null)
            Activate(playerRef);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHandle(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Por si el jugador ya estaba dentro al habilitarse
        if (!playerInRange) TryHandle(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc != null && pc == playerRef)
        {
            playerInRange = false;
            playerRef = null;
        }
    }

    private void TryHandle(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        playerInRange = true;
        playerRef = player;

        PlayerHealing healing = player.GetComponent<PlayerHealing>();
        if (healing != null) healing.OnRest();

        if (activateOnContact && (!saveOnlyOnce || !activated))
            Activate(player);
    }

    private void Activate(PlayerController player)
    {
        bool firstActivation = !activated;
        activated = true;

        // Guardar posicion exacta del checkpoint (o del jugador, segun prefieras). Aqui usamos la del checkpoint.
        Vector3 savePosition = transform.position;

        // Asegurar que el GameManager exista
        if (GameManager.Instance == null)
        {
            var gm = FindAnyObjectByType<GameManager>();
            if (gm == null)
            {
                GameObject go = new GameObject("GameManager");
                go.AddComponent<GameManager>();
            }
        }

        if (GameManager.Instance != null)
            GameManager.Instance.SaveCheckpoint(savePosition, player);

        if (activeVisual != null) activeVisual.SetActive(true);

        if (firstActivation)
            MessageManager.Show("Checkpoint guardado");

        Debug.Log($"[Checkpoint] Guardado en escena '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' pos={savePosition}");
    }
}
