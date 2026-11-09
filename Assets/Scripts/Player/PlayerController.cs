using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

// Movimiento + vida del jugador.
// Controles: A/D o flechas = mover | Espacio = saltar | Shift izquierdo = dash
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour, IDamageable
{
    [Header("Habilidades (se desbloquean con mejoras)")]
    public bool hasDoubleJump = true; // pon en false cuando ya tengas las recompensas del juego
    public bool hasDash = true;
    public bool hasWallJump = true;   // incluye deslizarse por la pared

    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 8f;

    [Header("Salto")]
    [SerializeField] private float jumpForce = 14f;
    [SerializeField] private float doubleJumpForce = 12f;
    [SerializeField] private float coyoteTime = 0.1f;     // margen para saltar justo al caer de un borde
    [SerializeField] private float jumpBufferTime = 0.1f; // margen si pulsas salto un poco antes de tocar suelo

    [Header("Gravedad")]
    [SerializeField] private float defaultGravityScale = 2f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.5f;
    [SerializeField] private bool invulnerableWhileDashing = true;

    [Header("Pared")]
    [SerializeField] private float wallSlideSpeed = 2f;                           // velocidad máxima de caída pegado a la pared
    [SerializeField] private Vector2 wallJumpForce = new Vector2(10f, 14f);       // empuje horizontal y vertical
    [SerializeField] private float wallJumpLockTime = 0.2f;                       // tiempo sin control horizontal tras el salto
    [SerializeField] private float wallCheckDistance = 0.1f;

    [Header("Vida y daño")]
    public int maxHealth = 5;
    public int currentHealth;
    public float invulnerabilityDuration = 1f;
    [SerializeField] private float knockbackForceX = 6f;
    [SerializeField] private float knockbackForceY = 8f;
    [SerializeField] private float hitStunDuration = 0.2f;
    [SerializeField] private float deathDelay = 0.8f; // pausa antes de reaparecer (aquí irá la animación de muerte)

    [Header("Suelo")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Caída al vacío")]
    [SerializeField] private float fallLimitY = -30f; // ajusta según la sala

    // --- Lo que otros scripts pueden consultar (animaciones, combate, etc.) ---
    public Team DamageTeam => Team.Player;
    public bool FacingRight { get; private set; } = true;
    public bool IsGrounded => isGrounded;
    public bool IsDashing => isDashing;
    public bool IsWallSliding => isWallSliding;
    public bool IsDead => isDead;
    public bool CanAct => !isDead && !isDashing && hitStunTimer <= 0f;

    // Referencias
    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer spriteRenderer;

    // Entrada
    private float xAxis;
    private bool jumpHeld;
    private bool dashPressed;

    // Estado
    private bool isGrounded;
    private bool touchingWall;
    private bool isWallSliding;
    private bool isDashing;
    private bool isInvulnerable;
    private bool isDead;
    private bool doubleJumpAvailable = true;
    private bool airDashAvailable = true;
    private float dashDirection = 1f;

    // Temporizadores
    private float coyoteCounter;
    private float jumpBufferCounter;
    private float hitStunTimer;
    private float wallJumpLockTimer;
    private float dashTimer;
    private float dashCooldownTimer;

    private Vector3 lastSafePosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        // TransitionManager desactiva y reactiva este script al cambiar de sala o reaparecer.
        // Al volver a activarse, limpiamos cualquier estado que haya quedado a medias.
        isDead = false;
        isDashing = false;
        isInvulnerable = false;
        hitStunTimer = 0f;
        wallJumpLockTimer = 0f;
        if (spriteRenderer != null) spriteRenderer.enabled = true;
    }

    private void Start()
    {
        rb.gravityScale = defaultGravityScale;
        currentHealth = maxHealth;
        lastSafePosition = transform.position;
    }

    private void Update()
    {
        if (isDead) return;

        ReadInput();
        TickTimers();
        CheckGround();
        CheckWall();
        HandleDash();
        HandleJump();
        HandleGravity();
        HandleFacing();
        CheckFallLimit();
    }

    private void FixedUpdate()
    {
        if (isDead) return;

        // Durante el dash la velocidad la manda el dash
        if (isDashing)
        {
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
            return;
        }

        // Durante el golpe no sobrescribimos la velocidad, para que se vea el knockback
        if (hitStunTimer > 0f)
        {
            hitStunTimer -= Time.fixedDeltaTime;
            return;
        }

        // Tras un salto de pared no tomamos el control horizontal por un instante
        if (wallJumpLockTimer <= 0f)
            rb.linearVelocity = new Vector2(walkSpeed * xAxis, rb.linearVelocity.y);

        // Deslizamiento: limita la velocidad de caída pegado a la pared
        if (isWallSliding && rb.linearVelocity.y < -wallSlideSpeed)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -wallSlideSpeed);
    }

    // ---------------------------------------------------------------- ENTRADA

    private void ReadInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            xAxis = 0f;
            jumpHeld = false;
            dashPressed = false;
            return;
        }

        xAxis = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) xAxis = -1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) xAxis = 1f;

        jumpHeld = kb.spaceKey.isPressed;
        dashPressed = kb.leftShiftKey.wasPressedThisFrame;

        if (kb.spaceKey.wasPressedThisFrame) jumpBufferCounter = jumpBufferTime;
        else jumpBufferCounter -= Time.deltaTime;
    }

    private void TickTimers()
    {
        wallJumpLockTimer -= Time.deltaTime;
        dashCooldownTimer -= Time.deltaTime;
    }

    // ---------------------------------------------------------------- SENSORES

    private void CheckGround()
    {
        bool touching = groundCheck != null &&
                        Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // "vy <= 0.1": justo después de saltar todavía estamos cerca del suelo, pero ya no cuenta como tocarlo
        isGrounded = touching && rb.linearVelocity.y <= 0.1f;

        if (isGrounded)
        {
            coyoteCounter = coyoteTime;
            doubleJumpAvailable = true;
            airDashAvailable = true;
            lastSafePosition = transform.position;
        }
        else
        {
            coyoteCounter -= Time.deltaTime;
        }
    }

    private void CheckWall()
    {
        touchingWall = false;
        isWallSliding = false;
        if (col == null || !hasWallJump) return;

        float dir = FacingRight ? 1f : -1f;
        Bounds b = col.bounds;

        // Caja fina pegada al costado hacia el que miramos
        Vector2 center = new Vector2(b.center.x + dir * (b.extents.x + wallCheckDistance * 0.5f), b.center.y);
        Vector2 size = new Vector2(wallCheckDistance, b.size.y * 0.8f);
        touchingWall = Physics2D.OverlapBox(center, size, 0f, groundLayer);

        bool pressingIntoWall = Mathf.Approximately(xAxis, dir);
        isWallSliding = touchingWall && !isGrounded && pressingIntoWall &&
                        rb.linearVelocity.y < 0f && !isDashing && wallJumpLockTimer <= 0f;
    }

    // ---------------------------------------------------------------- DASH

    private void HandleDash()
    {
        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f) isDashing = false;
            return;
        }

        if (!dashPressed || !hasDash) return;
        if (dashCooldownTimer > 0f || hitStunTimer > 0f) return;
        if (!isGrounded && !airDashAvailable) return; // un dash en el aire hasta volver a tocar suelo

        // Si estamos deslizándonos por una pared, el dash nos aleja de ella
        if (isWallSliding) SetFacing(!FacingRight);

        dashDirection = FacingRight ? 1f : -1f;
        isDashing = true;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        if (!isGrounded) airDashAvailable = false;
    }

    // ---------------------------------------------------------------- SALTO

    private void HandleJump()
    {
        if (isDashing || hitStunTimer > 0f) return;
        if (jumpBufferCounter <= 0f) return;

        if (isWallSliding)
        {
            WallJump();
        }
        else if (coyoteCounter > 0f)
        {
            Jump(jumpForce);
        }
        else if (hasDoubleJump && doubleJumpAvailable)
        {
            doubleJumpAvailable = false;
            Jump(doubleJumpForce);
        }
    }

    private void Jump(float force)
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
        AudioManager.PlaySalto();
    }

    private void WallJump()
    {
        float awayDir = FacingRight ? -1f : 1f; // salimos en sentido contrario a la pared
        rb.linearVelocity = new Vector2(awayDir * wallJumpForce.x, wallJumpForce.y);
        SetFacing(awayDir > 0f);

        wallJumpLockTimer = wallJumpLockTime;
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
        doubleJumpAvailable = true; // al saltar de una pared se recuperan el doble salto y el dash aéreo
        airDashAvailable = true;
        isWallSliding = false;
        AudioManager.PlaySalto();
    }

    private void HandleGravity()
    {
        if (isDashing)
        {
            rb.gravityScale = 0f; // el dash es recto, sin caer
            return;
        }

        if (rb.linearVelocity.y < 0f)
            rb.gravityScale = defaultGravityScale * fallGravityMultiplier;
        else if (rb.linearVelocity.y > 0f && !jumpHeld)
            rb.gravityScale = defaultGravityScale * lowJumpMultiplier;
        else
            rb.gravityScale = defaultGravityScale;
    }

    // ---------------------------------------------------------------- ORIENTACIÓN

    private void HandleFacing()
    {
        if (isDashing || hitStunTimer > 0f || wallJumpLockTimer > 0f || xAxis == 0f) return;
        SetFacing(xAxis > 0f);
    }

    private void SetFacing(bool right)
    {
        FacingRight = right;
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (right ? 1f : -1f); // asume que el sprite original mira a la derecha
        transform.localScale = s;
    }

    // ---------------------------------------------------------------- DAÑO Y MUERTE

    public void TakeDamage(int amount, Vector2 sourcePosition)
    {
        if (isDead || isInvulnerable) return;
        if (isDashing && invulnerableWhileDashing) return;

        currentHealth = Mathf.Max(currentHealth - amount, 0);

        if (currentHealth <= 0)
        {
            AudioManager.PlayMuerte();
            StopAllCoroutines();
            StartCoroutine(DieRoutine());
            return;
        }

        AudioManager.PlayDanio();
        StartCoroutine(InvulnerabilityRoutine());
        ApplyKnockback(sourcePosition);
    }

    private IEnumerator InvulnerabilityRoutine()
    {
        isInvulnerable = true;
        float elapsed = 0f;

        // Parpadeo mientras dura la invulnerabilidad
        while (elapsed < invulnerabilityDuration)
        {
            if (spriteRenderer != null) spriteRenderer.enabled = !spriteRenderer.enabled;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (spriteRenderer != null) spriteRenderer.enabled = true;
        isInvulnerable = false;
    }

    private void ApplyKnockback(Vector2 sourcePosition)
    {
        float dir = transform.position.x < sourcePosition.x ? -1f : 1f;
        rb.linearVelocity = new Vector2(dir * knockbackForceX, knockbackForceY);
        hitStunTimer = hitStunDuration;
    }

    private IEnumerator DieRoutine()
    {
        isDead = true;
        isDashing = false;
        rb.linearVelocity = Vector2.zero;
        if (spriteRenderer != null) spriteRenderer.enabled = true;

        yield return new WaitForSeconds(deathDelay);

        if (TransitionManager.Instance != null)
            TransitionManager.Instance.Respawn(); // vuelve al checkpoint con la vida llena
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    // ---------------------------------------------------------------- CAÍDA AL VACÍO

    private void CheckFallLimit()
    {
        if (transform.position.y >= fallLimitY) return;
        transform.position = lastSafePosition;
        rb.linearVelocity = Vector2.zero;
    }

    // TransitionManager lo llama al colocar al jugador en un spawn
    public void ResetSafePosition()
    {
        lastSafePosition = transform.position;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        Collider2D c = GetComponent<Collider2D>();
        if (c != null)
        {
            float dir = FacingRight ? 1f : -1f;
            Bounds b = c.bounds;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                new Vector3(b.center.x + dir * (b.extents.x + wallCheckDistance * 0.5f), b.center.y, 0f),
                new Vector3(wallCheckDistance, b.size.y * 0.8f, 0f));
        }
    }
}