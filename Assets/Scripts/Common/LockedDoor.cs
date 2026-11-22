using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Puerta cerrada con llave. Sin la llave muestra el mensaje configurado;
/// con la llave se abre y el estado se recuerda entre salas.
/// Acepta colliders 2D y 3D para poder migrar la puerta a 3D mas adelante.
/// </summary>
public class LockedDoor : MonoBehaviour
{
    private static readonly HashSet<string> openedDoors =
        new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    [Header("Identidad")]
    [Tooltip("Identificador unico de la puerta. Si se deja vacio se usa escena/nombre.")]
    [SerializeField] private string doorId = "";
    [SerializeField] private string requiredKeyId = KeyInventory.JadeKeyId;
    [Tooltip("Mensaje que se muestra si el jugador no tiene la llave.")]
    [SerializeField] private string lockedMessage = "Necesitas la Llave de Jade para abrir esta puerta";
    [SerializeField] private string openMessage = "La puerta se ha abierto";

    [Header("Apertura")]
    [Tooltip("Si esta activo, la llave se consume al abrir la puerta.")]
    [SerializeField] private bool consumeKey = true;
    [SerializeField] private float messageCooldown = 2f;

    [Header("Visual (opcional)")]
    [SerializeField] private GameObject closedVisual;
    [SerializeField] private GameObject openVisual;
    [SerializeField] private Animator animator;
    [SerializeField] private string openTrigger = "Open";

    [Header("Colisiones")]
    [Tooltip("Colliders 2D que bloquean el paso. Si se deja vacio se detectan automaticamente.")]
    [SerializeField] private Collider2D[] blockingColliders;
    [Tooltip("Colliders 3D que bloquean el paso. Si se deja vacio se detectan automaticamente.")]
    [SerializeField] private Collider[] blockingColliders3D;

    [Header("Eventos")]
    public UnityEvent onOpened;
    public UnityEvent onLocked;

    private bool isOpen;
    private float lastMessageTime = -999f;

    public bool IsOpen => isOpen;
    public string DoorId => doorId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Con "Reload Domain" desactivado, las estaticas sobreviven entre sesiones de Play.
        openedDoors.Clear();
    }

    public static bool IsMarkedOpen(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && openedDoors.Contains(id.Trim());
    }

    public static void MarkOpen(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            openedDoors.Add(id.Trim());
    }

    /// <summary>
    /// Borra el estado de todas las puertas. Se llama al reintentar y al volver al menu.
    /// </summary>
    public static void ResetAll()
    {
        openedDoors.Clear();
    }

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(doorId))
            doorId = BuildDefaultDoorId();

        if (blockingColliders == null || blockingColliders.Length == 0)
            blockingColliders = FindBlockingColliders();

        if (blockingColliders3D == null || blockingColliders3D.Length == 0)
            blockingColliders3D = FindBlockingColliders3D();

        if ((blockingColliders == null || blockingColliders.Length == 0) &&
            (blockingColliders3D == null || blockingColliders3D.Length == 0))
        {
            Debug.LogWarning(name + ": LockedDoor no encontro un Collider solido. La puerta no bloqueara el paso.");
        }

        if (openVisual != null)
            openVisual.SetActive(false);

        if (IsMarkedOpen(doorId))
            ApplyOpen(false);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryOpen(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryOpen(other);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        // Si el jugador consigue la llave mientras sigue tocando la puerta,
        // se abre sin necesidad de separarse y volver a chocar.
        TryOpen(collision.collider);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryOpen(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryOpen(collision.collider);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryOpen(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryOpen(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryOpen(other);
    }

    private void TryOpen(Component other)
    {
        if (isOpen || other == null) return;

        // Solo el jugador puede abrir la puerta; el arma lanzada u otros objetos se ignoran.
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead) return;

        if (KeyInventory.HasKey(requiredKeyId))
            Open(player);
        else
            ShowLockedMessage();
    }

    private void ShowLockedMessage()
    {
        if (string.IsNullOrWhiteSpace(lockedMessage)) return;
        if (Time.unscaledTime - lastMessageTime < messageCooldown) return;

        lastMessageTime = Time.unscaledTime;
        MessageManager.Show(lockedMessage);

        if (onLocked != null)
            onLocked.Invoke();
    }

    private void Open(PlayerController player)
    {
        if (isOpen) return;

        isOpen = true;
        MarkOpen(doorId);

        if (consumeKey)
        {
            if (player != null)
                player.ConsumeJadeKey(requiredKeyId);
            else
                KeyInventory.RemoveKey(requiredKeyId);
        }

        ApplyOpen(true);

        if (!string.IsNullOrWhiteSpace(openMessage))
            MessageManager.Show(openMessage);

        if (onOpened != null)
            onOpened.Invoke();
    }

    private void ApplyOpen(bool animate)
    {
        isOpen = true;

        if (blockingColliders != null)
        {
            for (int i = 0; i < blockingColliders.Length; i++)
            {
                if (blockingColliders[i] != null)
                    blockingColliders[i].enabled = false;
            }
        }

        if (blockingColliders3D != null)
        {
            for (int i = 0; i < blockingColliders3D.Length; i++)
            {
                if (blockingColliders3D[i] != null)
                    blockingColliders3D[i].enabled = false;
            }
        }

        if (closedVisual != null) closedVisual.SetActive(false);
        if (openVisual != null) openVisual.SetActive(true);

        if (animate && animator != null && !string.IsNullOrEmpty(openTrigger))
            animator.SetTrigger(openTrigger);
    }

    private Collider2D[] FindBlockingColliders()
    {
        Collider2D[] all = GetComponentsInChildren<Collider2D>(true);
        List<Collider2D> solid = new List<Collider2D>();

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !all[i].isTrigger)
                solid.Add(all[i]);
        }

        return solid.ToArray();
    }

    private Collider[] FindBlockingColliders3D()
    {
        Collider[] all = GetComponentsInChildren<Collider>(true);
        List<Collider> solid = new List<Collider>();

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !all[i].isTrigger)
                solid.Add(all[i]);
        }

        return solid.ToArray();
    }

    private string BuildDefaultDoorId()
    {
        string sceneName = gameObject.scene.name;
        if (string.IsNullOrEmpty(sceneName)) sceneName = "Unsaved";
        return sceneName + "/" + name;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(requiredKeyId))
            requiredKeyId = KeyInventory.JadeKeyId;

        if (string.IsNullOrWhiteSpace(lockedMessage))
            lockedMessage = "Necesitas la Llave de Jade para abrir esta puerta";
    }
}
