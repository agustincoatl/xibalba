using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class PickupItem : MonoBehaviour
{
    [Header("Puntos")]
    [SerializeField] private int points = 1;

    [Header("Mensaje")]
    [SerializeField] private bool showMessage = true;
    [Tooltip("Usa {0} para insertar el nombre visible del objeto.")]
    [SerializeField] private string pickupMessage = "Has recogido {0}";
    [Tooltip("Nombre visible. Si se deja vacio se usa el nombre del objeto.")]
    [SerializeField] private string displayName = "";

    [Header("Recogida")]
    [SerializeField] private float pickupDelay = 0.25f;
    [SerializeField] private bool destroyOnPickup = true;

    [Header("Eventos")]
    public UnityEvent onPickup;

    private Collider2D col;
    private float spawnTime;
    private bool pickedUp;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        spawnTime = Time.time;

        if (!col.isTrigger)
            Debug.LogWarning(name + ": el Collider2D debe tener 'Is Trigger' activado para poder recogerse.");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (pickedUp || Time.time - spawnTime < pickupDelay) return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        pickedUp = true;
        if (col != null) col.enabled = false;

        if (points != 0)
            ScoreManager.AddPoints(points);

        if (showMessage)
            MessageManager.Show(BuildPickupMessage());

        if (onPickup != null) onPickup.Invoke();

        if (destroyOnPickup) Destroy(gameObject);
    }

    private string BuildPickupMessage()
    {
        string targetName = string.IsNullOrWhiteSpace(displayName) ? CleanName(name) : displayName;

        if (string.IsNullOrWhiteSpace(pickupMessage))
            return "Has recogido " + targetName;

        return pickupMessage.Contains("{0}")
            ? string.Format(pickupMessage, targetName)
            : pickupMessage;
    }

    private static string CleanName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "objeto";
        return raw.Replace("(Clone)", "").Replace('_', ' ').Trim();
    }
}
