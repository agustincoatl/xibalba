using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Horizontal Movement Settings:")]
    [SerializeField] private float walkSpeed = 8f;

    [Header("Jump Settings:")]
    [SerializeField] private float jumpForce = 14f;
    [SerializeField] private float coyoteTime = 0.1f;      // margen para saltar justo después de caer de un borde
    [SerializeField] private float jumpBufferTime = 0.1f;  // margen si pulsas salto un poco antes de tocar el suelo

    [Header("Gravity & Fall Settings:")]
    [SerializeField] private float defaultGravityScale = 2f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Health & Combat Settings:")]
    public int maxHealth = 5;
    public int currentHealth;
    public float invulnerabilityDuration = 1f;
    [SerializeField] private float knockbackForceX = 6f;
    [SerializeField] private float knockbackForceY = 8f;
    [SerializeField] private float hitStunDuration = 0.2f; // tiempo sin control tras el golpe

    [Header("Ground Check Settings:")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    // Para usar luego en ataques y habilidades
    public bool FacingRight { get; private set; } = true;

    // References
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;

    // Estado
    private float xAxis;
    private bool jumpHeld;
    private bool isGrounded;
    private bool isInvulnerable;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private float hitStunTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        rb.gravityScale = defaultGravityScale;
        currentHealth = maxHealth;
    }

    void Update()
    {
        GetInputs();
        CheckGround();
        Jump();
        ModifyGravity();
        Flip();
    }

    void FixedUpdate()
    {
        Move();
    }

    void GetInputs()
    {
        var kb = Keyboard.current;
        if (kb == null) { xAxis = 0f; jumpHeld = false; return; }

        xAxis = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) xAxis = -1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) xAxis = 1f;

        jumpHeld = kb.spaceKey.isPressed;

        if (kb.spaceKey.wasPressedThisFrame) jumpBufferCounter = jumpBufferTime;
        else jumpBufferCounter -= Time.deltaTime;
    }

    void CheckGround()
    {
        if (groundCheck != null)
            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (isGrounded) coyoteCounter = coyoteTime;
        else coyoteCounter -= Time.deltaTime;
    }

    void Jump()
    {
        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
        }
    }

    void ModifyGravity()
    {
        if (rb.linearVelocity.y < 0)
            rb.gravityScale = defaultGravityScale * fallGravityMultiplier;
        else if (rb.linearVelocity.y > 0 && !jumpHeld)
            rb.gravityScale = defaultGravityScale * lowJumpMultiplier;
        else
            rb.gravityScale = defaultGravityScale;
    }

    void Flip()
    {
        if (hitStunTimer > 0f || xAxis == 0f) return;

        FacingRight = xAxis > 0f;
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (FacingRight ? 1f : -1f); // asume que el sprite original mira a la derecha
        transform.localScale = s;
    }

    void Move()
    {
        // Durante el golpe no sobrescribimos la velocidad, para que el knockback funcione
        if (hitStunTimer > 0f)
        {
            hitStunTimer -= Time.fixedDeltaTime;
            return;
        }
        rb.linearVelocity = new Vector2(walkSpeed * xAxis, rb.linearVelocity.y);
    }

    public void TakeDamage(int damageAmount, Vector2 damageSourcePosition)
    {
        if (isInvulnerable) return;

        currentHealth = Mathf.Max(currentHealth - damageAmount, 0);

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            StartCoroutine(InvulnerabilityRoutine());
            ApplyKnockback(damageSourcePosition);
        }
    }

    private IEnumerator InvulnerabilityRoutine()
    {
        isInvulnerable = true;
        float elapsed = 0f;

        while (elapsed < invulnerabilityDuration)
        {
            if (spriteRenderer != null) spriteRenderer.enabled = !spriteRenderer.enabled;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (spriteRenderer != null) spriteRenderer.enabled = true;
        isInvulnerable = false;
    }

    private void ApplyKnockback(Vector2 damageSourcePosition)
    {
        float dir = transform.position.x < damageSourcePosition.x ? -1f : 1f;
        rb.linearVelocity = new Vector2(dir * knockbackForceX, knockbackForceY);
        hitStunTimer = hitStunDuration;
    }

    void Die()
    {
/*         // Por ahora recarga la escena. Más adelante: regresar al checkpoint o a la Zona Inicial, según tu diagrama.
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        ); */
        TransitionManager.Instance.Respawn();
    }

    private void OnCollisionEnter2D(Collision2D collision) => HandleContact(collision);
    private void OnCollisionStay2D(Collision2D collision) => HandleContact(collision);

    private void HandleContact(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Hazard") || collision.gameObject.CompareTag("Enemy"))
            TakeDamage(1, collision.transform.position);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}