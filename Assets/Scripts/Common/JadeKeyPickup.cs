using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Llave de Jade recogible. Cae con gravedad hasta el suelo, flota y pulsa
/// como los objetos de mejora, y al tocarla el jugador la guarda en KeyInventory.
/// Acepta Collider2D/Rigidbody2D y tambien Collider/Rigidbody para el futuro 3D.
/// </summary>
[DisallowMultipleComponent]
public class JadeKeyPickup : MonoBehaviour
{
    [Header("Llave")]
    [SerializeField] private string keyId = KeyInventory.JadeKeyId;
    [Tooltip("Opcional. Si se deja vacio, la llave no se recuerda entre visitas a la sala.")]
    [SerializeField] private string pickupId = "";
    [SerializeField] private string pickupMessage = "Has conseguido la Llave de Jade";
    [SerializeField] private float pickupDelay = 0.35f;
    [SerializeField] private bool destroyOnPickup = true;

    [Header("Caida")]
    [Tooltip("Capas del suelo. Si se deja vacio se usan Default y Ground.")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float fallGravityScale = 2.5f;
    [Tooltip("Altura a la que queda el centro de la llave sobre el punto de suelo.")]
    [SerializeField] private float landingOffset = 0.3f;
    [Tooltip("Si cae mas de esta distancia sin encontrar suelo, se destruye.")]
    [SerializeField] private float maxFallDistance = 60f;

    [Header("Flotacion y pulso")]
    [Tooltip("Hijo que gira y pulsa. Si se deja vacio se usa el primer hijo.")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private float bobHeight = 0.18f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float rotationSpeed = 90f;
    [SerializeField] private float scaleSpeed = 3f;
    [SerializeField] private float scaleAmount = 0.12f;

    [Header("Eventos")]
    public UnityEvent onPickup;

    private Rigidbody2D rb2D;
    private Rigidbody rb3D;
    private Collider2D col2D;
    private Collider col3D;
    private Vector3 spawnPosition;
    private Vector3 landingPosition;
    private Vector3 baseVisualScale = Vector3.one;
    private float spawnTime;
    private bool landed;
    private bool collected;

    private void Reset()
    {
        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Default", "Ground");
    }

    private void Awake()
    {
        rb2D = GetComponent<Rigidbody2D>();
        rb3D = GetComponent<Rigidbody>();
        col2D = GetComponent<Collider2D>();
        col3D = GetComponent<Collider>();

        spawnTime = Time.time;
        spawnPosition = transform.position;

        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Default", "Ground");

        if (visualRoot == null && transform.childCount > 0)
            visualRoot = transform.GetChild(0);
        if (visualRoot != null)
            baseVisualScale = visualRoot.localScale;

        if (string.IsNullOrWhiteSpace(keyId))
            keyId = KeyInventory.JadeKeyId;

        if (!string.IsNullOrWhiteSpace(pickupId) && KeyInventory.WasPickupCollected(pickupId))
        {
            Destroy(gameObject);
            return;
        }

        if (KeyInventory.HasKey(keyId))
        {
            // Ya hay una llave en el inventario: evita duplicados.
            if (!string.IsNullOrWhiteSpace(pickupId))
                KeyInventory.MarkPickupCollected(pickupId);
            Destroy(gameObject);
            return;
        }

        if (col2D == null && col3D == null)
            Debug.LogWarning(name + ": JadeKeyPickup necesita un Collider (2D o 3D) con Is Trigger.");
        else if ((col2D == null || !col2D.isTrigger) && (col3D == null || !col3D.isTrigger))
            Debug.LogWarning(name + ": el Collider de JadeKeyPickup deberia tener Is Trigger activado.");

        if (rb2D != null)
        {
            rb2D.gravityScale = fallGravityScale;
            rb2D.linearVelocity = Vector2.zero;
            rb2D.angularVelocity = 0f;
        }
        if (rb3D != null)
        {
            rb3D.useGravity = true;
            rb3D.linearVelocity = Vector3.zero;
            rb3D.angularVelocity = Vector3.zero;
        }
    }

    private void FixedUpdate()
    {
        if (collected) return;

        if (!landed)
        {
            TryLand();
            return;
        }

        // Flotacion suave alrededor del punto donde aterrizo.
        float offset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        Vector3 target = landingPosition + Vector3.up * offset;

        if (rb2D != null) rb2D.position = target;
        else if (rb3D != null) rb3D.position = target;
        else transform.position = target;
    }

    private void Update()
    {
        if (collected || visualRoot == null) return;

        visualRoot.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);

        if (scaleAmount <= 0f || scaleSpeed <= 0f) return;

        float factor = Mathf.Sin(Time.time * scaleSpeed) * scaleAmount;
        visualRoot.localScale = baseVisualScale * (1f + factor);
    }

    private void TryLand()
    {
        if (!TryGetGround(out Vector3 groundPoint))
        {
            if (spawnPosition.y - transform.position.y > maxFallDistance)
                Destroy(gameObject);
            return;
        }

        float targetY = groundPoint.y + landingOffset;
        if (transform.position.y <= targetY + 0.01f)
            Land(new Vector3(transform.position.x, targetY, transform.position.z));
    }

    private bool TryGetGround(out Vector3 groundPoint)
    {
        groundPoint = transform.position;
        float bestDistance = float.PositiveInfinity;
        bool found = false;

        // Se lanza desde arriba para que la llave no se cuele si cae rapido.
        // Los impactos muy por encima de la llave (techos) se descartan.
        Vector3 origin = transform.position + Vector3.up * 2f;

        RaycastHit2D[] hits2D = Physics2D.RaycastAll(origin, Vector2.down, maxFallDistance + 10f, groundLayers);
        for (int i = 0; i < hits2D.Length; i++)
        {
            Collider2D hit = hits2D[i].collider;
            if (!IsValidGround(hit)) continue;
            if (hits2D[i].point.y > transform.position.y + 0.5f) continue;
            if (hits2D[i].distance >= bestDistance) continue;

            bestDistance = hits2D[i].distance;
            groundPoint = hits2D[i].point;
            found = true;
        }

        RaycastHit[] hits3D = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxFallDistance + 10f,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits3D.Length; i++)
        {
            Collider hit = hits3D[i].collider;
            if (!IsValidGround(hit)) continue;
            if (hits3D[i].point.y > transform.position.y + 0.5f) continue;
            if (hits3D[i].distance >= bestDistance) continue;

            bestDistance = hits3D[i].distance;
            groundPoint = hits3D[i].point;
            found = true;
        }

        return found;
    }

    private bool IsValidGround(Collider2D hit)
    {
        return hit != null &&
               !hit.isTrigger &&
               hit.transform != transform &&
               !hit.transform.IsChildOf(transform) &&
               hit.GetComponentInParent<PlayerController>() == null &&
               hit.GetComponentInParent<EnemyChaser>() == null &&
               hit.GetComponentInParent<BasicEnemy>() == null &&
               hit.GetComponentInParent<EnemyPatrol>() == null;
    }

    private bool IsValidGround(Collider hit)
    {
        return hit != null &&
               !hit.isTrigger &&
               hit.transform != transform &&
               !hit.transform.IsChildOf(transform) &&
               hit.GetComponentInParent<PlayerController>() == null &&
               hit.GetComponentInParent<EnemyChaser>() == null &&
               hit.GetComponentInParent<BasicEnemy>() == null &&
               hit.GetComponentInParent<EnemyPatrol>() == null;
    }

    private void Land(Vector3 position)
    {
        landed = true;
        landingPosition = position;

        if (rb2D != null)
        {
            rb2D.linearVelocity = Vector2.zero;
            rb2D.angularVelocity = 0f;
            rb2D.gravityScale = 0f;
            rb2D.bodyType = RigidbodyType2D.Kinematic;
            rb2D.position = position;
        }
        else if (rb3D != null)
        {
            rb3D.linearVelocity = Vector3.zero;
            rb3D.angularVelocity = Vector3.zero;
            rb3D.useGravity = false;
            rb3D.isKinematic = true;
            rb3D.position = position;
        }
        else
        {
            transform.position = position;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Cubre el caso de que la llave caiga encima del jugador y el trigger
        // ya estuviera solapado cuando termino el tiempo de espera.
        TryCollect(other);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryCollect(other);
    }

    private void TryCollect(Component other)
    {
        if (collected || other == null) return;
        if (Time.time - spawnTime < pickupDelay) return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead) return;

        collected = true;
        if (col2D != null) col2D.enabled = false;
        if (col3D != null) col3D.enabled = false;

        player.CollectJadeKey(keyId);

        if (!string.IsNullOrWhiteSpace(pickupId))
            KeyInventory.MarkPickupCollected(pickupId);

        if (!string.IsNullOrWhiteSpace(pickupMessage))
            MessageManager.Show(pickupMessage);

        if (onPickup != null)
            onPickup.Invoke();

        if (destroyOnPickup)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(keyId))
            keyId = KeyInventory.JadeKeyId;

        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Default", "Ground");
    }
}
