using UnityEngine;

// Conecta el estado del PlayerController/PlayerCombat con el Animator.
// Con clips nuevos basta con crear estados/transiciones que usen estos parametros.
[RequireComponent(typeof(PlayerController))]
public class PlayerAnimation : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string groundedParameter = "Grounded";
    [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
    [SerializeField] private string dashTrigger = "Dash";
    [SerializeField] private string attackTrigger = "Attack";
    [SerializeField] private string hurtTrigger = "Hurt";
    [SerializeField] private string deadParameter = "Dead";

    [Header("Velocidad de reproduccion")]
    [SerializeField] private float idlePlayback = 1f;
    [SerializeField] private float movePlayback = 1.5f;
    [SerializeField] private float airPlayback = 0.75f;
    [SerializeField] private float dashPlayback = 2f;
    [SerializeField] private float playbackDamping = 0.12f;

    private PlayerController player;
    private PlayerCombat combat;
    private Rigidbody2D rb;

    private int speedHash;
    private int groundedHash;
    private int verticalSpeedHash;
    private int dashHash;
    private int attackHash;
    private int hurtHash;
    private int deadHash;

    private bool hasSpeed;
    private bool hasGrounded;
    private bool hasVerticalSpeed;
    private bool hasDash;
    private bool hasAttack;
    private bool hasHurt;
    private bool hasDead;

    private bool wasDashing;
    private bool wasAttacking;
    private bool wasDead;
    private int lastHealth;

    public Animator Animator => animator;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombat>();
        rb = GetComponent<Rigidbody2D>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        CacheParameters();
    }

    private void OnEnable()
    {
        wasDashing = false;
        wasAttacking = false;
        wasDead = false;

        if (player != null)
            lastHealth = player.currentHealth;

        if (animator != null && hasSpeed)
            animator.SetFloat(speedHash, idlePlayback);
    }

    private void CacheParameters()
    {
        hasSpeed = hasGrounded = hasVerticalSpeed = false;
        hasDash = hasAttack = hasHurt = hasDead = false;

        if (animator == null)
            return;

        speedHash = Animator.StringToHash(speedParameter);
        groundedHash = Animator.StringToHash(groundedParameter);
        verticalSpeedHash = Animator.StringToHash(verticalSpeedParameter);
        dashHash = Animator.StringToHash(dashTrigger);
        attackHash = Animator.StringToHash(attackTrigger);
        hurtHash = Animator.StringToHash(hurtTrigger);
        deadHash = Animator.StringToHash(deadParameter);

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            int hash = parameters[i].nameHash;
            if (hash == speedHash) hasSpeed = true;
            else if (hash == groundedHash) hasGrounded = true;
            else if (hash == verticalSpeedHash) hasVerticalSpeed = true;
            else if (hash == dashHash) hasDash = true;
            else if (hash == attackHash) hasAttack = true;
            else if (hash == hurtHash) hasHurt = true;
            else if (hash == deadHash) hasDead = true;
        }
    }

    private void Update()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        UpdateTriggers();

        if (hasGrounded) animator.SetBool(groundedHash, player.IsGrounded);
        if (hasVerticalSpeed && rb != null) animator.SetFloat(verticalSpeedHash, rb.linearVelocity.y);
        if (hasDead) animator.SetBool(deadHash, player.IsDead);

        UpdatePlaybackSpeed();
    }

    private void UpdateTriggers()
    {
        if (hasDash)
        {
            if (player.IsDashing && !wasDashing)
                animator.SetTrigger(dashHash);
            wasDashing = player.IsDashing;
        }

        if (hasAttack)
        {
            bool attacking = combat != null && combat.IsAttacking;
            if (attacking && !wasAttacking)
                animator.SetTrigger(attackHash);
            wasAttacking = attacking;
        }

        if (hasHurt && player.currentHealth < lastHealth && !player.IsDead)
            animator.SetTrigger(hurtHash);

        lastHealth = player.currentHealth;

        if (hasDead)
        {
            if (player.IsDead && !wasDead)
                animator.SetBool(deadHash, true);
            wasDead = player.IsDead;
        }
    }

    private void UpdatePlaybackSpeed()
    {
        if (!hasSpeed)
            return;

        float target = idlePlayback;

        if (player.IsDead)
            target = 0f;
        else if (player.IsDashing)
            target = dashPlayback;
        else if (!player.IsGrounded)
            target = airPlayback;
        else if (rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.1f)
            target = movePlayback;

        animator.SetFloat(speedHash, target, playbackDamping, Time.deltaTime);
    }
}
