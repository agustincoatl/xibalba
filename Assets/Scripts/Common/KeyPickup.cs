using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Llave recogible. Anade la llave al inventario, muestra un mensaje y recuerda
/// que ya se recogio para que no reaparezca al volver a la sala.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class KeyPickup : MonoBehaviour
{
    [Header("Llave")]
    [SerializeField] private string keyId = KeyInventory.JadeKeyId;
    [Tooltip("Identificador unico de este objeto. Si se deja vacio se usa escena/nombre.")]
    [SerializeField] private string pickupId = "";
    [SerializeField] private string pickupMessage = "Has conseguido la llave de jade";

    [Header("Puntos (opcional)")]
    [SerializeField] private int points = 0;

    [Header("Recogida")]
    [SerializeField] private float pickupDelay = 0.25f;
    [SerializeField] private bool destroyOnPickup = true;
    [SerializeField] private GameObject visualRoot;

    [Header("Eventos")]
    public UnityEvent onPickup;

    private Collider2D col;
    private float spawnTime;
    private bool collected;

    private void Reset()
    {
        Collider2D trigger = GetComponent<Collider2D>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        spawnTime = Time.time;

        if (string.IsNullOrWhiteSpace(pickupId))
            pickupId = BuildDefaultPickupId();

        if (col != null && !col.isTrigger)
            Debug.LogWarning(name + ": el Collider2D de KeyPickup deberia tener 'Is Trigger' activado.");

        if (KeyInventory.WasPickupCollected(pickupId))
        {
            Destroy(gameObject);
            return;
        }

        if (KeyInventory.HasKey(keyId))
        {
            // La llave ya se obtuvo por otra via: no duplicar el objeto.
            KeyInventory.MarkPickupCollected(pickupId);
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || Time.time - spawnTime < pickupDelay) return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        collected = true;
        if (col != null) col.enabled = false;

        KeyInventory.AddKey(keyId);
        KeyInventory.MarkPickupCollected(pickupId);

        if (points != 0)
            ScoreManager.AddPoints(points);

        if (!string.IsNullOrWhiteSpace(pickupMessage))
            MessageManager.Show(pickupMessage);

        if (onPickup != null)
            onPickup.Invoke();

        if (destroyOnPickup)
        {
            Destroy(gameObject);
        }
        else if (visualRoot != null)
        {
            visualRoot.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private string BuildDefaultPickupId()
    {
        string sceneName = gameObject.scene.name;
        if (string.IsNullOrEmpty(sceneName)) sceneName = "Unsaved";
        return sceneName + "/" + name;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(keyId))
            keyId = KeyInventory.JadeKeyId;
    }
}
