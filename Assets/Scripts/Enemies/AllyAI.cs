using UnityEngine;

// Aliado con IA simple: sigue al jugador, salta obstáculos bajos y ataca a los enemigos cercanos.
// Requiere Rigidbody2D (Dynamic, con Freeze Rotation Z) y un Collider2D.
[RequireComponent(typeof(Rigidbody2D))]
public class AllyAI : MonoBehaviour, IDamageable
{
    [Header("Vida")]
    [SerializeField] private int maxHealth = 5;
    [SerializeField] private float invulnerabilityTime = 0.5f;
    [SerializeField] private float knockbackX = 4f;
    [SerializeField] private float knockbackY = 3f;
    [SerializeField] private float stunTime = 0.2f;

    [Header("Seguir al jugador")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float followDistance = 2.5f;   // se detiene a esta distancia
    [SerializeField] private float teleportDistance = 14f;  // si se queda muy lejos, reaparece junto al jugador

    [Header("Combate")]
    [SerializeField] private float detectRange = 6f;
    [SerializeField] private float attackRange = 1.1f;
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackCooldown = 0.8f;
    [SerializeField] private LayerMask targetLayers;        // vacío = detecta cualquier capa

    [Header("Terreno")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float jumpForce = 11f;
    [SerializeField] private float wallCheckDistance = 0.15f;

    public Team DamageTeam => Team.Player;

    private Rigidbody2D rb;
    private Collider2D col;
    private Transform playerT;
    private Transform targetT;
    private IDamageable targetDamageable;

    private int health;
    private float attackTimer;
    private float retargetTimer;
    private float stunTimer;
    private float invulnerableTimer;
    private bool isDead;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        health = maxHealth;

        if (targetLayers.value == 0) targetLayers = ~0;
        if (groundLayer.value == 0)
        {
            groundLayer = LayerMask.GetMask("Default", "Ground");
            Debug.LogWarning(name + ": no asignaste 'Ground Layer'. Usando Default y Ground.");
        }
    }

    private void FixedUpdate()
    {
        if (isDead) return;

        float dt = Time.fixedDeltaTime;
        attackTimer -= dt;
        retargetTimer -= dt;
        invulnerableTimer -= dt;

        if (stunTimer > 0f)
        {
            stunTimer -= dt;
            return;
        }

        if (playerT == null) playerT = FindPlayer();

        // Buscar enemigo cercano (no cada frame, para ahorrar trabajo)
        if (retargetTimer <= 0f)
        {
            retargetTimer = 0.25f;
            FindTarget();
        }

        float moveDir = 0f;

        if (targetT != null)
        {
            float dx = targetT.position.x - transform.position.x;
            Face(dx);

            if (Mathf.Abs(dx) > attackRange)
            {
                moveDir = Mathf.Sign(dx); // acercarse al enemigo
            }
            else if (attackTimer <= 0f)
            {
                attackTimer = attackCooldown;
                targetDamageable.TakeDamage(attackDamage, transform.position);
            }
        }
        else if (playerT != null)
        {
            float dx = playerT.position.x - transform.position.x;
            if (Mathf.Abs(dx) > followDistance) moveDir = Mathf.Sign(dx);

            bool tooFar = Vector2.Distance(playerT.position, transform.position) > teleportDistance;
            bool fellDown = transform.position.y < playerT.position.y - 15f;
            if (tooFar || fellDown) TeleportToPlayer();
        }

        if (!Mathf.Approximately(moveDir, 0f)) Face(moveDir);

        // Saltar si hay un obstáculo delante y está en el suelo
        if (!Mathf.Approximately(moveDir, 0f) && IsGrounded() && IsBlocked(moveDir))
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);

        rb.linearVelocity = new Vector2(moveDir * moveSpeed, rb.linearVelocity.y);
    }

    // ---------------------------------------------------------------- IA

    private Transform FindPlayer()
    {
        PlayerController p = FindAnyObjectByType<PlayerController>();
        return p != null ? p.transform : null;
    }

    private void FindTarget()
    {
        targetT = null;
        targetDamageable = null;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectRange, targetLayers);
        float best = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable d = hits[i].GetComponentInParent<IDamageable>();
            if (d == null || d.DamageTeam == Team.Player) continue; // solo ataca enemigos

            float dist = ((Vector2)hits[i].transform.position - (Vector2)transform.position).sqrMagnitude;
            if (dist < best)
            {
                best = dist;
                targetT = hits[i].transform;
                targetDamageable = d;
            }
        }
    }

    private void TeleportToPlayer()
    {
        transform.position = playerT.position + Vector3.left * followDistance;
        rb.linearVelocity = Vector2.zero;
    }

    private bool IsGrounded()
    {
        Bounds b = col.bounds;
        return Probe2D.Hit(new Vector2(b.center.x, b.min.y + 0.05f), Vector2.down, 0.15f, groundLayer, transform);
    }

    private bool IsBlocked(float dir)
    {
        Bounds b = col.bounds;
        return Probe2D.Hit(b.center, new Vector2(dir, 0f), b.extents.x + wallCheckDistance, groundLayer, transform);
    }

    private void Face(float dx)
    {
        if (Mathf.Approximately(dx, 0f)) return;
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (dx > 0f ? 1f : -1f); // asume que el sprite original mira a la derecha
        transform.localScale = s;
    }

    // ---------------------------------------------------------------- DAÑO

    public void TakeDamage(int amount, Vector2 sourcePosition)
    {
        if (isDead || invulnerableTimer > 0f) return;

        health -= amount;
        invulnerableTimer = invulnerabilityTime;

        if (health <= 0)
        {
            isDead = true;
            rb.linearVelocity = Vector2.zero;
            Destroy(gameObject, 0.2f); // aquí puedes esperar a una animación de muerte
            return;
        }

        float dir = transform.position.x < sourcePosition.x ? -1f : 1f;
        rb.linearVelocity = new Vector2(dir * knockbackX, knockbackY);
        stunTimer = stunTime;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
