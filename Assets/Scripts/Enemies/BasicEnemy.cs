using UnityEngine;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// Enemigo basico con modelo 3D (Mixamo) y maquina de estados:
/// Patrulla (Walk) -> el jugador entra en rango -> Carrera (Run + avance)
/// -> rango de ataque -> Ataque (Attack) con golpe y recuperacion.
/// El giro del modelo se hace por yaw (no con localScale negativo) para que
/// el esqueleto 3D no se invierta al mirar a la izquierda.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class BasicEnemy : MonoBehaviour, IDamageable
{
    public enum BasicEnemyState { Patrol, Chase, Attack }

    [Header("Modelo 3D")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Animator animator;
    [Tooltip("Yaw que hace mirar al modelo hacia la derecha de la pantalla.")]
    [SerializeField] private float facingYaw = 90f;
    [SerializeField] private bool modelFacesRightByDefault = true;

    [Header("Vida y danio recibido")]
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private float knockbackX = 5f;
    [SerializeField] private float knockbackY = 4f;
    [SerializeField] private float stunTime = 0.25f;
    [SerializeField] private Color flashColor = Color.red;

    [Header("Patrulla")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float edgeCheckDistance = 0.6f;
    [SerializeField] private float wallCheckDistance = 0.15f;
    [SerializeField] private bool startFacingRight = true;
    [SerializeField] private float turnCooldown = 0.3f;

    [Header("Deteccion y carrera")]
    [SerializeField] private float detectRange = 7f;
    [SerializeField] private float loseRange = 10f;
    [SerializeField] private float chaseSpeed = 4.2f;
    [SerializeField] private float lungeMultiplier = 1.6f;
    [SerializeField] private float lungeDuration = 0.45f;
    [SerializeField] private float stopDistance = 0.8f;
    [SerializeField] private bool stopAtEdges = true;

    [Header("Ataque")]
    [SerializeField] private float attackRange = 1.4f;
    [SerializeField] private float attackVerticalTolerance = 1f;
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackWindup = 0.25f;
    [SerializeField] private float attackRecovery = 0.25f;
    [SerializeField] private float attackCooldown = 1.1f;
    [SerializeField] private Vector2 attackOffset = new Vector2(0.9f, 1f);
    [SerializeField] private float attackRadius = 0.8f;

    [Header("Danio por contacto")]
    [SerializeField] private int contactDamage = 1;

    [Header("Al morir")]
    [Tooltip("Llave/recompensa de historia que cae SIEMPRE al morir (opcional).")]
    [SerializeField] private GameObject jadeKeyPrefab;
    [Tooltip("Monedas u objetos que suelta al morir. Arrastra aqui Coin_Up, etc.")]
    [SerializeField] private GameObject[] lootPrefabs;
    [SerializeField] private int minLootCount = 2;
    [SerializeField] private int maxLootCount = 4;
    [SerializeField] private Vector2 dropOffset = new Vector2(0f, 0.5f);
    [SerializeField] private Vector2 dropSpread = new Vector2(0.8f, 0.25f);
    [SerializeField] private Vector2 dropImpulse = new Vector2(2f, 4f);
    [SerializeField] private UnityEvent onDeath;

    public Team DamageTeam => Team.Enemy;
    public BasicEnemyState State => state;
    public bool IsChasing => state == BasicEnemyState.Chase;
    public bool IsAttacking => state == BasicEnemyState.Attack;
    public event System.Action Died;

    private static readonly int WalkingHash = Animator.StringToHash("Walking");
    private static readonly int RunningHash = Animator.StringToHash("Running");
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    private Rigidbody2D rb;
    private Collider2D col;
    private Renderer[] renderers;
    private PlayerController player;

    private int health;
    private int direction = 1;
    private BasicEnemyState state = BasicEnemyState.Patrol;
    private bool isDead;
    private bool attackHitDone;
    private float attackElapsed;
    private float attackTimer;
    private float stunTimer;
    private float turnTimer;
    private float lungeTimer;
    private float findPlayerTimer;
    private Coroutine flashRoutine;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        if (modelRoot == null)
        {
            Transform child = transform.Find("Model");
            modelRoot = child != null ? child : transform.childCount > 0 ? transform.GetChild(0) : null;
        }
        if (animator == null && modelRoot != null)
            animator = modelRoot.GetComponentInChildren<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (modelRoot != null)
            renderers = modelRoot.GetComponentsInChildren<Renderer>(true);

        health = maxHealth;
        direction = startFacingRight ? 1 : -1;
        ApplyFacing();

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
        lungeTimer -= dt;
        turnTimer -= dt;

        if (stunTimer > 0f)
        {
            stunTimer -= dt;
            SetAnimation(false, false);
            return;
        }

        // El jugador es persistente entre salas: lo buscamos de vez en cuando.
        if (player == null && findPlayerTimer <= 0f)
        {
            findPlayerTimer = 1f;
            player = FindAnyObjectByType<PlayerController>();
        }

        bool hasPlayer = player != null && !player.IsDead;
        float distance = hasPlayer
            ? Vector2.Distance(player.transform.position, transform.position)
            : float.PositiveInfinity;

        if (!hasPlayer)
        {
            state = BasicEnemyState.Patrol;
        }
        else if (state == BasicEnemyState.Patrol && distance <= detectRange)
        {
            EnterChase();
        }
        else if (state == BasicEnemyState.Chase && distance > loseRange)
        {
            state = BasicEnemyState.Patrol; // histéresis: no suelta al jugador a la primera
        }

        switch (state)
        {
            case BasicEnemyState.Patrol:
                PatrolStep();
                break;
            case BasicEnemyState.Chase:
                ChaseStep();
                break;
            case BasicEnemyState.Attack:
                AttackStep(dt);
                break;
        }
    }

    // ---------------------------------------------------------------- FSM

    private void PatrolStep()
    {
        if (turnTimer <= 0f && (IsBlocked() || (IsGrounded() && !HasGroundAhead())))
            Turn();

        rb.linearVelocity = new Vector2(direction * patrolSpeed, rb.linearVelocity.y);
        SetAnimation(true, false);
    }

    private void EnterChase()
    {
        state = BasicEnemyState.Chase;
        lungeTimer = lungeDuration; // primer impulso: "se abalanza"
    }

    private void ChaseStep()
    {
        if (player == null) return;

        float dx = player.transform.position.x - transform.position.x;
        float dy = player.transform.position.y - transform.position.y;
        if (Mathf.Abs(dx) > 0.05f) Face(dx);

        if (attackTimer <= 0f &&
            Mathf.Abs(dx) <= attackRange && Mathf.Abs(dy) <= attackVerticalTolerance)
        {
            StartAttack();
            return;
        }

        float speed = chaseSpeed * (lungeTimer > 0f ? lungeMultiplier : 1f);
        bool canAdvance = Mathf.Abs(dx) > stopDistance &&
                          !(stopAtEdges && IsGrounded() && !HasGroundAhead());

        rb.linearVelocity = new Vector2(canAdvance ? direction * speed : 0f, rb.linearVelocity.y);
        SetAnimation(false, true);
    }

    private void StartAttack()
    {
        state = BasicEnemyState.Attack;
        attackTimer = attackCooldown;
        attackElapsed = 0f;
        attackHitDone = false;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        if (animator != null)
        {
            animator.ResetTrigger(AttackHash);
            animator.SetTrigger(AttackHash);
        }
        SetAnimation(false, false);
    }

    private void AttackStep(float dt)
    {
        attackElapsed += dt;

        if (!attackHitDone && attackElapsed >= attackWindup)
        {
            attackHitDone = true;
            DoAttackHit();
        }

        if (attackElapsed >= attackWindup + attackRecovery)
            state = BasicEnemyState.Chase;
    }

    private void DoAttackHit()
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(attackOffset.x * direction, attackOffset.y);
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, attackRadius);
        System.Collections.Generic.HashSet<IDamageable> alreadyHit =
            new System.Collections.Generic.HashSet<IDamageable>();

        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable target = hits[i].GetComponentInParent<IDamageable>();
            if (target == null || target.DamageTeam == Team.Enemy) continue;
            if (!alreadyHit.Add(target)) continue;

            target.TakeDamage(attackDamage, transform.position);
        }
    }

    // ---------------------------------------------------------------- SENSORES Y GIRO

    private bool IsGrounded()
    {
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
        return Probe2D.Hit(new Vector2(b.center.x, b.min.y + 0.05f), Vector2.down, 0.15f, groundLayer, transform);
    }

    private bool IsBlocked()
    {
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
        return Probe2D.Hit(b.center, new Vector2(direction, 0f), b.extents.x + wallCheckDistance, groundLayer, transform);
    }

    private bool HasGroundAhead()
    {
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
        Vector2 origin = new Vector2(b.center.x + direction * (b.extents.x + 0.05f), b.min.y + 0.1f);
        return Probe2D.Hit(origin, Vector2.down, edgeCheckDistance, groundLayer, transform);
    }

    private void Turn()
    {
        direction = -direction;
        turnTimer = turnCooldown;
        ApplyFacing();
    }

    private void Face(float dx)
    {
        if (Mathf.Approximately(dx, 0f)) return;
        int newDirection = dx > 0f ? 1 : -1;
        if (newDirection == direction) return;
        direction = newDirection;
        ApplyFacing();
    }

    private void ApplyFacing()
    {
        if (modelRoot == null) return;
        float sign = modelFacesRightByDefault ? 1f : -1f;
        float yaw = sign * direction * facingYaw;
        modelRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void SetAnimation(bool walking, bool running)
    {
        if (animator == null) return;
        animator.SetBool(WalkingHash, walking);
        animator.SetBool(RunningHash, running);
    }

    // ---------------------------------------------------------------- DANO Y MUERTE

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

        // Un golpe interrumpe el ataque que estaba preparando.
        if (state == BasicEnemyState.Attack && animator != null)
            animator.ResetTrigger(AttackHash);
        state = BasicEnemyState.Chase;
        lungeTimer = 0f;

        float dir = transform.position.x < sourcePosition.x ? -1f : 1f;
        rb.linearVelocity = new Vector2(dir * knockbackX, knockbackY);
        stunTimer = stunTime;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        if (renderers == null || renderers.Length == 0) yield break;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", flashColor);
        block.SetColor("_Color", flashColor);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].SetPropertyBlock(block);

        yield return new WaitForSeconds(0.1f);

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].SetPropertyBlock(null);
        flashRoutine = null;
    }

    private void Die()
    {
        isDead = true;
        StopAllCoroutines();
        rb.linearVelocity = Vector2.zero;
        if (col != null) col.enabled = false;
        rb.simulated = false;

        DropKey();
        DropLoot();

        onDeath.Invoke();
        Died?.Invoke();

        Destroy(gameObject, 0.2f);
    }

    private void DropKey()
    {
        if (jadeKeyPrefab == null) return;

        GameObject key = Instantiate(
            jadeKeyPrefab,
            transform.position + (Vector3)dropOffset,
            Quaternion.identity);
        key.name = "LlaveDeJade";
    }

    /// <summary>
    /// Suelta una cantidad aleatoria de monedas/objetos al morir. Misma idea
    /// que el dropPrefab de EnemyPatrol y el jadeKeyPrefab de EnemyChaser,
    /// pero con lista y reparto para que cualquier enemigo pueda usarlo.
    /// </summary>
    private void DropLoot()
    {
        if (lootPrefabs == null || lootPrefabs.Length == 0) return;

        int lowest = Mathf.Max(0, Mathf.Min(minLootCount, maxLootCount));
        int highest = Mathf.Max(0, Mathf.Max(minLootCount, maxLootCount));
        int count = Random.Range(lowest, highest + 1);

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = lootPrefabs[Random.Range(0, lootPrefabs.Length)];
            if (prefab == null) continue;

            Vector3 position = transform.position + (Vector3)dropOffset + new Vector3(
                Random.Range(-dropSpread.x, dropSpread.x),
                Random.Range(-dropSpread.y, dropSpread.y),
                0f);

            GameObject drop = Instantiate(prefab, position, Quaternion.identity);
            drop.name = prefab.name.Replace("(Clone)", string.Empty);

            Rigidbody2D dropBody = drop.GetComponent<Rigidbody2D>();
            if (dropBody == null) dropBody = drop.GetComponentInChildren<Rigidbody2D>();
            if (dropBody == null) continue; // Coin_Up ya tiene PickupItem + RotatingPickup, no cae por gravedad.

            dropBody.linearVelocity = new Vector2(
                Random.Range(-dropImpulse.x, dropImpulse.x),
                Random.Range(dropImpulse.y * 0.5f, dropImpulse.y));
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, loseRange);

        int dir = Application.isPlaying ? direction : (startFacingRight ? 1 : -1);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + new Vector3(attackOffset.x * dir, attackOffset.y, 0f), attackRadius);
    }
}
