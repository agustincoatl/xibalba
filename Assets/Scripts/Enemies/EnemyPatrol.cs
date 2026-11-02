using UnityEngine;
using UnityEngine.Events;
using System.Collections;

// Enemigo que patrulla: avanza, y da la vuelta al llegar a una pared o a un borde.
// Requiere Rigidbody2D (Dynamic, con Freeze Rotation Z) y un Collider2D.
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyPatrol : MonoBehaviour, IDamageable
{
    [Header("Vida y daño")]
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private int contactDamage = 1;
    [SerializeField] private float knockbackX = 5f;
    [SerializeField] private float knockbackY = 4f;
    [SerializeField] private float stunTime = 0.25f;

    [Header("Al morir")]
    [SerializeField] private GameObject dropPrefab; // objeto clave / llave / recompensa (opcional)
    [SerializeField] private UnityEvent onDeath;

    [Header("Patrulla")]
    [SerializeField] private float speed = 2f;
    [SerializeField] private LayerMask groundLayer;        // capa del suelo y las paredes (la de tu Tilemap)
    [SerializeField] private float edgeCheckDistance = 0.6f;
    [SerializeField] private float wallCheckDistance = 0.15f;
    [SerializeField] private bool startFacingRight = true;
    [SerializeField] private float turnCooldown = 0.3f;    // evita girar varias veces seguidas

    public Team DamageTeam => Team.Enemy;

    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;
    private Color originalColor = Color.white;

    private int health;
    private int direction;
    private float stunTimer;
    private float turnTimer;
    private bool isDead;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) originalColor = sr.color;

        health = maxHealth;
        direction = startFacingRight ? 1 : -1;
        ApplyFlip();

        if (groundLayer.value == 0)
        {
            groundLayer = LayerMask.GetMask("Default", "Ground");
            Debug.LogWarning(name + ": no asignaste 'Ground Layer'. Usando Default y Ground.");
        }
    }

    private void FixedUpdate()
    {
        if (isDead) return;

        turnTimer -= Time.fixedDeltaTime;

        if (stunTimer > 0f)
        {
            stunTimer -= Time.fixedDeltaTime;
            return;
        }

        if (turnTimer <= 0f && (HitsWall() || (IsGrounded() && !HasGroundAhead())))
            Turn();

        rb.linearVelocity = new Vector2(direction * speed, rb.linearVelocity.y);
    }

    // ---------------------------------------------------------------- SENSORES

    private bool IsGrounded()
    {
        Bounds b = col.bounds;
        return Probe2D.Hit(new Vector2(b.center.x, b.min.y + 0.05f), Vector2.down, 0.15f, groundLayer, transform);
    }

    private bool HitsWall()
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

    private void Turn()
    {
        direction = -direction;
        turnTimer = turnCooldown;
        ApplyFlip();
    }

    private void ApplyFlip()
    {
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * direction; // asume que el sprite original mira a la derecha
        transform.localScale = s;
    }

    // ---------------------------------------------------------------- DAÑO

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (isDead) return;

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
        rb.linearVelocity = Vector2.zero;
        col.enabled = false;
        rb.simulated = false;

        if (dropPrefab != null) Instantiate(dropPrefab, transform.position, Quaternion.identity);
        onDeath.Invoke();

        Destroy(gameObject, 0.2f); // aquí puedes esperar a una animación de muerte
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D c = GetComponent<Collider2D>();
        if (c == null) return;

        int dir = Application.isPlaying ? direction : (startFacingRight ? 1 : -1);
        Bounds b = c.bounds;

        Gizmos.color = Color.red; // detector de pared
        Gizmos.DrawLine(b.center, b.center + new Vector3(dir * (b.extents.x + wallCheckDistance), 0f, 0f));

        Gizmos.color = Color.yellow; // detector de borde
        Vector3 o = new Vector3(b.center.x + dir * (b.extents.x + 0.05f), b.min.y + 0.1f, 0f);
        Gizmos.DrawLine(o, o + Vector3.down * edgeCheckDistance);
    }
}
