using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;

// Enemigo básico que persigue: espera quieto, detecta al jugador, lo persigue y lo ataca.
// También le hace daño si lo toca. Requiere Rigidbody2D (Dynamic, con Freeze Rotation Z) y un Collider2D.
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyChaser : MonoBehaviour, IDamageable
{
    [Header("Vida")]
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private float knockbackX = 5f;
    [SerializeField] private float knockbackY = 4f;
    [SerializeField] private float stunTime = 0.25f;

    [Header("Detección")]
    [SerializeField] private float detectRange = 7f; // empieza a perseguir dentro de esta distancia
    [SerializeField] private float loseRange = 10f;  // deja de perseguir fuera de esta (mayor que detectRange)

    [Header("Persecución")]
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float stopDistance = 0.8f;       // se detiene a esta distancia horizontal
    [SerializeField] private bool stopAtEdges = true;         // no se lanza al vacío persiguiendo
    [SerializeField] private LayerMask groundLayer;           // capa de tu Tilemap
    [SerializeField] private float edgeCheckDistance = 0.6f;
    [SerializeField] private float wallCheckDistance = 0.15f;

    [Header("Ataque")]
    [SerializeField] private float attackRange = 1.3f;        // distancia horizontal para iniciar el ataque
    [SerializeField] private float attackVerticalTolerance = 1f;
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackWindup = 0.35f;      // aviso antes del golpe (el jugador puede esquivar)
    [SerializeField] private float attackRecovery = 0.3f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private Vector2 attackOffset = new Vector2(0.9f, 0f);
    [SerializeField] private float attackRadius = 0.7f;

    [Header("Daño por contacto")]
    [SerializeField] private int contactDamage = 1;           // pon 0 si solo quieres que dañe con el ataque

    [Header("Al morir")]
    [Tooltip("Llave de Jade que se instancia al morir. Cae por gravedad hasta el suelo.")]
    [FormerlySerializedAs("dropPrefab")]
    [SerializeField] private GameObject jadeKeyPrefab;
    [SerializeField] private Vector2 dropOffset = new Vector2(0f, 0.5f);
    [SerializeField] private UnityEvent onDeath;

    public Team DamageTeam => Team.Enemy;
    public bool IsChasing => chasing;
    public bool IsAttacking => isAttacking;
    public event System.Action Died;

    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;
    private Color originalColor = Color.white;

    private PlayerController player;
    private Coroutine attackRoutine;

    private int health;
    private int direction = 1;
    private bool chasing;
    private bool isAttacking;
    private bool isDead;
    private float attackTimer;
    private float stunTimer;
    private float findPlayerTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) originalColor = sr.color;

        health = maxHealth;

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
        findPlayerTimer -= dt;

        if (stunTimer > 0f)
        {
            stunTimer -= dt;
            return;
        }

        // Durante el ataque se queda plantado
        if (isAttacking)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        // Buscar al jugador (el jugador es persistente, así que se busca de vez en cuando)
        if (player == null && findPlayerTimer <= 0f)
        {
            findPlayerTimer = 1f;
            player = FindAnyObjectByType<PlayerController>();
        }

        if (player == null || player.IsDead)
        {
            chasing = false;
            Stop();
            return;
        }

        // Detección con histéresis: empieza a menos de detectRange y suelta a más de loseRange
        float distance = Vector2.Distance(player.transform.position, transform.position);
        if (!chasing && distance <= detectRange) chasing = true;
        else if (chasing && distance > loseRange) chasing = false;

        if (!chasing)
        {
            Stop();
            return;
        }

        float dx = player.transform.position.x - transform.position.x;
        float dy = player.transform.position.y - transform.position.y;
        Face(dx);

        // ¿Está lo bastante cerca para atacar?
        if (attackTimer <= 0f && Mathf.Abs(dx) <= attackRange && Mathf.Abs(dy) <= attackVerticalTolerance)
        {
            attackRoutine = StartCoroutine(AttackRoutine());
            return;
        }

        // Perseguir
        if (Mathf.Abs(dx) > stopDistance && !IsBlocked() && !(stopAtEdges && IsGrounded() && !HasGroundAhead()))
            rb.linearVelocity = new Vector2(direction * chaseSpeed, rb.linearVelocity.y);
        else
            Stop();
    }

    // ---------------------------------------------------------------- ATAQUE

    private IEnumerator AttackRoutine()
    {
        isAttacking = true;
        attackTimer = attackCooldown;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        // Aviso visual: se tiñe de amarillo antes de golpear
        if (sr != null) sr.color = Color.yellow;
        yield return new WaitForSeconds(attackWindup);
        if (sr != null) sr.color = originalColor;

        DoAttackHit();

        yield return new WaitForSeconds(attackRecovery);
        isAttacking = false;
    }

    private void DoAttackHit()
    {
        Vector2 pos = (Vector2)transform.position + new Vector2(attackOffset.x * direction, attackOffset.y);
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, attackRadius);
        HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();

        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable target = hits[i].GetComponentInParent<IDamageable>();
            if (target == null || target.DamageTeam == Team.Enemy) continue; // no se daña a sí mismo ni a otros enemigos
            if (!alreadyHit.Add(target)) continue;

            target.TakeDamage(attackDamage, transform.position);
        }
    }

    // ---------------------------------------------------------------- SENSORES

    private bool IsGrounded()
    {
        Bounds b = col.bounds;
        return Probe2D.Hit(new Vector2(b.center.x, b.min.y + 0.05f), Vector2.down, 0.15f, groundLayer, transform);
    }

    private bool IsBlocked()
    {
        Bounds b = col.bounds;
        return Probe2D.Hit(b.center, new Vector2(direction, 0f), b.extents.x + wallCheckDistance, groundLayer, transform);
    }

    private bool HasGroundAhead()
    {
        Bounds b = col.bounds;
        Vector2 origin = new Vector2(b.center.x + direction * (b.extents.x + 0.05f), b.min.y + 0.1f);
        return Probe2D.Hit(origin, Vector2.down, edgeCheckDistance, groundLayer, transform);
    }

    private void Stop()
    {
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private void Face(float dx)
    {
        if (Mathf.Approximately(dx, 0f)) return;
        direction = dx > 0f ? 1 : -1;

        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * direction; // asume que el sprite original mira a la derecha
        transform.localScale = s;
    }

    // ---------------------------------------------------------------- DAÑO

    // Daño por tocar al jugador (o a un aliado)
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (isDead || contactDamage <= 0) return;

        IDamageable target = collision.collider.GetComponentInParent<IDamageable>();
        if (target != null && target.DamageTeam != Team.Enemy)
            target.TakeDamage(contactDamage, transform.position);
    }

    public void TakeDamage(int amount, Vector2 sourcePosition)
    {
        if (isDead) return;

        health -= amount;
        if (health <= 0)
        {
            Die();
            return;
        }

        // Un golpe interrumpe el ataque que estaba preparando
        if (attackRoutine != null) StopCoroutine(attackRoutine);
        isAttacking = false;
        if (sr != null) sr.color = originalColor;

        chasing = true; // si lo golpean desde lejos, se entera y persigue

        float dir = transform.position.x < sourcePosition.x ? -1f : 1f;
        rb.linearVelocity = new Vector2(dir * knockbackX, knockbackY);
        stunTimer = stunTime;
        StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        if (sr == null) yield break;
        sr.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        sr.color = originalColor;
    }

    private void Die()
    {
        isDead = true;
        StopAllCoroutines();
        rb.linearVelocity = Vector2.zero;
        col.enabled = false;
        rb.simulated = false;

        if (jadeKeyPrefab != null)
        {
            GameObject key = Instantiate(
                jadeKeyPrefab,
                transform.position + (Vector3)dropOffset,
                Quaternion.identity);

            // Nombre claro para depurar la jerarquia mientras se prueba el flujo.
            key.name = "LlaveDeJade";
        }
        else
        {
            Debug.LogWarning(name + ": no hay prefab de Llave de Jade asignado.");
        }

        onDeath.Invoke();
        Died?.Invoke();

        Destroy(gameObject, 0.2f); // aquí puedes esperar a una animación de muerte
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow; // detección
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = Color.cyan;   // se rinde
        Gizmos.DrawWireSphere(transform.position, loseRange);

        float dir = Application.isPlaying ? direction : 1f;
        Gizmos.color = Color.red;    // zona del golpe
        Gizmos.DrawWireSphere(transform.position + new Vector3(attackOffset.x * dir, attackOffset.y, 0f), attackRadius);
    }
}
