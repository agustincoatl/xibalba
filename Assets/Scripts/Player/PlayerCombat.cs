using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

// Ataque cuerpo a cuerpo y arma lanzable.
// Controles: J = atacar | K = lanzar | H = canalizar Ch'ulel | Q = ofrenda
[RequireComponent(typeof(PlayerController))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Ataque cuerpo a cuerpo")]
    [SerializeField] private Vector2 attackOffset = new Vector2(1f, 0f); // posiciÃ³n del golpe respecto al jugador (X se invierte segÃºn hacia dÃ³nde mira)
    [SerializeField] private float attackRadius = 0.8f;
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackCooldown = 0.35f;
    [SerializeField] private float attackHitDelay = 0.08f; // retraso hasta que "conecta" el golpe (sincroniza con la animaciÃ³n)
    [SerializeField] private LayerMask attackLayers;       // vacÃ­o = golpea cualquier capa
    [Header("Feedback de ritmo de ataque")]
    [SerializeField] private AttackRateVfx attackRateVfx;
    [SerializeField] private Color attackPulseColor = new Color(0.25f, 0.85f, 1f, 1f);

    [Header("Arma lanzable")]
    public bool hasThrowable = true; // pon en false para desbloquearla con una mejora
    [SerializeField] private ThrowableWeapon throwablePrefab;
    [SerializeField] private Vector2 throwOffset = new Vector2(0.8f, 0f);
    [SerializeField] private float throwCooldown = 0.5f;

    public bool IsAttacking { get; private set; }

    private PlayerController player;
    private PlayerHealing healing;
    private float attackTimer;
    private float throwTimer;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        healing = GetComponent<PlayerHealing>();
        attackRateVfx = GetComponent<AttackRateVfx>();
        if (attackRateVfx == null) attackRateVfx = gameObject.AddComponent<AttackRateVfx>();
        if (attackLayers.value == 0) attackLayers = ~0;
    }

    private void Update()
    {
        attackTimer -= Time.deltaTime;
        throwTimer -= Time.deltaTime;

        Keyboard kb = Keyboard.current;
        if (kb == null || !player.enabled || !player.CanAct) return;

        if (kb.jKey.wasPressedThisFrame && attackTimer <= 0f)
        {
            attackTimer = attackCooldown;
            AudioManager.PlayAtaque();
            StartCoroutine(MeleeRoutine());
        }

        if (kb.kKey.wasPressedThisFrame && hasThrowable && throwTimer <= 0f)
        {
            throwTimer = throwCooldown;
            Throw();
        }
    }

    private IEnumerator MeleeRoutine()
    {
        IsAttacking = true;
        attackRateVfx?.PlayAttackPulse(transform.position, attackPulseColor, 0.9f);
        yield return new WaitForSeconds(attackHitDelay);
        DoMeleeHit();
        attackRateVfx?.PlayAttackPulse(AttackPosition(), Color.white, 1.2f);
        yield return new WaitForSeconds(0.1f);
        IsAttacking = false;
    }

    private void DoMeleeHit()
    {
        AudioManager.PlayAtaque();

        Collider2D[] hits = Physics2D.OverlapCircleAll(AttackPosition(), attackRadius, attackLayers);
        HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>(); // un enemigo con varios colliders recibe un solo golpe

        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable target = hits[i].GetComponentInParent<IDamageable>();
            if (target == null || target.DamageTeam == Team.Player) continue; // no golpea a aliados ni a sï¿½ï¿½ï¿½ï¿½ï¿½ï¿½ mismo
            if (!alreadyHit.Add(target)) continue;

            target.TakeDamage(attackDamage, transform.position);
            // El bootstrap añade PlayerHealing justo después de los Awake de la escena,
            // así que aquí lo rebuscamos por si Awake lo vio todavía nulo.
            if (healing == null) healing = GetComponent<PlayerHealing>();
            if (healing != null) healing.AddEssence();
        }
    }

    private void Throw()
    {
        if (throwablePrefab == null)
        {
            Debug.LogWarning("PlayerCombat: falta asignar el prefab del arma lanzable.");
            return;
        }

        float dir = player.FacingRight ? 1f : -1f;
        Vector3 pos = transform.position + new Vector3(throwOffset.x * dir, throwOffset.y, 0f);
        ThrowableWeapon projectile = Instantiate(throwablePrefab, pos, Quaternion.identity);
        projectile.Launch(new Vector2(dir, 0f), Team.Player);
        attackRateVfx?.PlayAttackPulse(pos, attackPulseColor, 0.8f);
        AudioManager.PlayAtaque();
    }

    private Vector2 AttackPosition()
    {
        float dir = player != null && !player.FacingRight ? -1f : 1f;
        return (Vector2)transform.position + new Vector2(attackOffset.x * dir, attackOffset.y);
    }

    private void OnDrawGizmosSelected()
    {
        if (player == null) player = GetComponent<PlayerController>();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(AttackPosition(), attackRadius);
    }
}
