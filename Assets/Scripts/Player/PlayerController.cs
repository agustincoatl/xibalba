using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Horizontal Movement Settings:")]
    [SerializeField] private float walkSpeed = 8f;

    [Header("Jump Settings:")]
    [SerializeField] private float jumpForce = 14f;

    [Header("Gravity & Fall Settings:")]
    [SerializeField] private float defaultGravityScale = 2f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Health & Combat Settings:")]
    public int maxHealth = 5;         // Vidas máximas estilo Hollow Knight (máscaras)
    public int currentHealth;
    private bool isInvulnerable = false;
    public float invulnerabilityDuration = 1f; // Segundos de inmunidad tras recibir daño

    [Header("Ground Check Settings:")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    // References
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private float xAxis;
    private bool isGrounded;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb.gravityScale = defaultGravityScale;
        currentHealth = maxHealth; // Iniciar con la vida al máximo
    }

    void Update()
    {
        GetInputs();
        CheckGround();
        Jump();
        ModifyGravity();
    }

    void FixedUpdate()
    {
        Move();
    }

    void GetInputs()
    {
        xAxis = 0f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            xAxis = -1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            xAxis = 1f;
    }

    void CheckGround()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        }
    }

    void Jump()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        }
    }

    void ModifyGravity()
    {
        if (rb.linearVelocity.y < 0)
        {
            rb.gravityScale = defaultGravityScale * fallGravityMultiplier;
        }
        else if (rb.linearVelocity.y > 0 && !Keyboard.current.spaceKey.isPressed)
        {
            rb.gravityScale = defaultGravityScale * lowJumpMultiplier;
        }
        else
        {
            rb.gravityScale = defaultGravityScale;
        }
    }

    void Move()
    {
        rb.linearVelocity = new Vector2(walkSpeed * xAxis, rb.linearVelocity.y);
    }

    // Método público para recibir daño desde enemigos o trampas
    public void TakeDamage(int damageAmount, Vector2 damageSourcePosition)
    {
        if (isInvulnerable) return;

        currentHealth -= damageAmount;
        Debug.Log("¡Daño recibido! Vidas actuales: " + currentHealth);

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

        // Efecto de parpadeo visual mientras es invulnerable
        while (elapsed < invulnerabilityDuration)
        {
            if (spriteRenderer != null)
                spriteRenderer.enabled = !spriteRenderer.enabled;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        isInvulnerable = false;
    }

    private void ApplyKnockback(Vector2 damageSourcePosition)
    {
        // Pequeño impulso de retroceso al recibir daño
        float knockbackDir = transform.position.x < damageSourcePosition.x ? -1f : 1f;
        rb.linearVelocity = new Vector2(knockbackDir * 6f, 8f);
    }

    void Die()
    {
        Debug.Log("El jugador ha muerto. Reiniciando nivel...");
        // Aquí puedes recargar la escena actual:
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Detectar si choca con un objeto marcado como Hazard (Daño)
        if (collision.gameObject.CompareTag("Hazard") || collision.gameObject.CompareTag("Enemy"))
        {
            TakeDamage(1, collision.transform.position);
        }
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