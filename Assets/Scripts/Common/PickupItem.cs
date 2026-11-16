using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class PickupItem : MonoBehaviour
{
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

        if (onPickup != null) onPickup.Invoke();

        if (destroyOnPickup) Destroy(gameObject);
    }
}
