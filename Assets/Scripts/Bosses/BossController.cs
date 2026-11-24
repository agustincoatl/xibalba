using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Estados de la maquina de estados finita del jefe Camazotz.
/// El flujo autoritativo es: Dormant -> Idle -> Walk (si hace falta) ->
/// ataque -> Idle. No hay Recover/Vulnerable/Stunned: el unico momento
/// invulnerable es la ejecucion del ataque (o el super grito de fase 2).
/// </summary>
public enum BossState
{
    /// <summary>Esperando a que el jugador entre a la zona de combate.</summary>
    Dormant,

    /// <summary>Estado base: orienta, decide y recibe dano.</summary>
    Idle,

    /// <summary>Reposicionamiento previo a un ataque.</summary>
    Walk,

    /// <summary>Secuencia inicial invulnerable de la segunda fase.</summary>
    PhaseTransition,

    /// <summary>Golpe unico de corto alcance (BasicAttack).</summary>
    BasicAttack,

    /// <summary>Combo de tres golpes consecutivos (ComboAttack).</summary>
    ComboAttack,

    /// <summary>Grito sonico desde el suelo (Scream).</summary>
    Scream,

    /// <summary>Lluvia de colmillos casteada desde el suelo (Spell).</summary>
    Spell,

    /// <summary>Picada diagonal: sube con Fly y embiste al jugador.</summary>
    Dive,

    /// <summary>Barrido aereo: se coloca a la altura del jugador y barre (Fly).</summary>
    Sweep,

    /// <summary>Derrotado.</summary>
    Dead,

    // ---------------------------------------------------------------- XIBALBA
    // Se agregan al final para no cambiar el valor serializado de los estados
    // existentes de Camazotz.
    /// <summary>Entrada: arrodillado -> se alza (Start_Battle).</summary>
    StartBattle,

    /// <summary>Golpe melee alternativo A (BasicAttackA).</summary>
    BasicAttackA,

    /// <summary>Golpe melee alternativo B (BasicAttackB).</summary>
    BasicAttackB,

    /// <summary>Canalizacion de orbes con trayectoria fijada (Cast).</summary>
    Cast,

    /// <summary>Salto con onda sismica al aterrizar (JumpAttack).</summary>
    JumpAttack,

    /// <summary>Pinchos de obsidiana con aviso (ObsidianAttack).</summary>
    ObsidianAttack
}

/// <summary>
/// Base comun para jefes: vida, fases por umbral, invulnerabilidad breve,
/// aviso de dano, eventos para la UI, orientacion y la maquina de estados
/// finita (FSM) que usan todos los jefes del proyecto.
///
/// La FSM vive aca para que cualquier jefe nuevo herede el mismo ciclo:
/// el derived implementa TickAI() con su logica por estado y usa
/// ChangeState() para transicionar. El Animator se sincroniza desde ahi.
/// </summary>
public abstract class BossController : MonoBehaviour, IDamageable
{
    protected static readonly int AnimBattleActive = Animator.StringToHash("BattleActive");
    protected static readonly int AnimWalking = Animator.StringToHash("Walking");
    protected static readonly int AnimIsFlying = Animator.StringToHash("IsFlying");
    protected static readonly int AnimEnraged = Animator.StringToHash("Enraged");
    protected static readonly int AnimBasicAttack = Animator.StringToHash("BasicAttack");
    protected static readonly int AnimComboAttack = Animator.StringToHash("ComboAttack");
    protected static readonly int AnimSonic = Animator.StringToHash("Sonic");
    protected static readonly int AnimFangs = Animator.StringToHash("Fangs");
    protected static readonly int AnimDive = Animator.StringToHash("Dive");
    protected static readonly int AnimSweep = Animator.StringToHash("Sweep");
    protected static readonly int AnimDie = Animator.StringToHash("Die");
    protected static readonly int AnimIdleReset = Animator.StringToHash("IdleReset");
    protected static readonly int AnimSuperSonic = Animator.StringToHash("SuperSonic");

    [Header("Vida y fases")]
    [SerializeField] protected int maxHealth = 12;
    [Tooltip("Porcentaje de vida que activa la fase 2 y el super grito sónico.")]
    [SerializeField, Range(0.1f, 0.9f)] protected float phase2Threshold = 0.5f;

    [Header("Referencias")]
    [SerializeField] protected Animator animator;
    [SerializeField] protected Transform modelRoot;
    [Tooltip("Solo si la deteccion automatica falla: marcalo si el modelo mira a +X con su rotacion base.")]
    [SerializeField] protected bool modelFacesRightByDefault;

    public Team DamageTeam => Team.Enemy;
    public int Health => health;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool Enraged => enraged;

    /// <summary>
    /// True solo mientras se ejecuta un ataque (o el super grito de fase 2).
    /// Idle y Walk siempre son vulnerables.
    /// </summary>
    public bool IsAttackInvulnerable => attackInvulnerable;

    /// <summary>Sentido horizontal real del modelo (+1 derecha, -1 izquierda).</summary>
    public int FacingSign => facingSign;

    /// <summary>Inclinación visual extra del modelo en grados (Dive).</summary>
    public float VisualTilt => visualTilt;

    /// <summary>Falso mientras vuela: evita que el collider haga dano "por el aire".</summary>
    public bool ContactDamageEnabled => contactDamageEnabled && !isDead;

    /// <summary>True solo despues de que el jugador entro a la zona de combate.</summary>
    public bool BattleActive => battleActive;

    // ---------------------------------------------------------------- FSM
    /// <summary>Estado actual de la maquina (visible en el Inspector al debuggear).</summary>
    public BossState CurrentState => currentState;

    /// <summary>Segundos transcurridos desde que se entro al estado actual.</summary>
    public float StateTime => stateTime;

    [SerializeField] private BossState initialState = BossState.Dormant;
    protected BossState currentState = BossState.Dormant;
    protected float stateTime;

    public event Action<int, int> HealthChanged;
    public event Action BattleStarted;
    public event Action PhaseChanged;
    public event Action Defeated;
    public event Action<BossState> StateChanged;

    protected PlayerController player;
    protected int health;
    protected bool enraged;
    protected bool isDead;
    protected bool busy;
    protected bool contactDamageEnabled = true;
    private bool attackInvulnerable;

    [Header("Encuentro")]
    [Tooltip("Si está en false el jefe espera en su sitio: no persigue ni ataca.")]
    [SerializeField] protected bool battleActive = true;

    /// <summary>
    /// golpes ya aplicados en la ventana de melee actual. Se resetea al
    /// entrar en cada estado de ataque y entre los golpes de un combo.
    /// </summary>
    private readonly HashSet<IDamageable> meleeHits = new HashSet<IDamageable>();

    private float findPlayerTimer;

    // Facing: se voltea el modelo con una rotacion Y de 180 grados, nunca con
    // localScale.x. En una instancia 2.5D con el root rotado ~-96 grados, el
    // eje local X apunta al fondo de la pantalla: escalarlo en negativo no
    // cambia hacia donde mira el modelo en pantalla y por eso se quedaba
    // "atascado a la izquierda".
    private Quaternion modelBaseLocalRotation;
    private Vector3 modelBaseLocalScale;
    private Renderer[] modelRenderers;
    private bool baseFacesRight;
    private int facingSign = -1;
    private float visualTilt;

    protected virtual void Awake()
    {
        health = maxHealth;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (modelRoot == null && animator != null) modelRoot = animator.transform;

        if (modelRoot != null)
        {
            modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            modelBaseLocalRotation = modelRoot.localRotation;

            // Sanitiza flips por escala heredados de prefab o de la escena.
            Vector3 scale = modelRoot.localScale;
            modelRoot.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            modelBaseLocalScale = modelRoot.localScale;

            float forwardX = modelRoot.forward.x;
            baseFacesRight = Mathf.Abs(forwardX) > 0.1f ? forwardX > 0f : modelFacesRightByDefault;
            facingSign = baseFacesRight ? 1 : -1;
            visualTilt = 0f;
            ApplyFacing(facingSign);
        }

        currentState = initialState;
        stateTime = 0f;
    }

    protected virtual void Start()
    {
        player = FindAnyObjectByType<PlayerController>();
        HealthChanged?.Invoke(health, maxHealth);
    }

    /// <summary>Ultima linea de defensa: ningun jefe puede salir de su arena.</summary>
    protected virtual void LateUpdate()
    {
        transform.position = ClampToArena(transform.position);
    }

    /// <summary>Por defecto no hay limites; Camazotz lo override con su arena.</summary>
    protected virtual Vector3 ClampToArena(Vector3 position) => position;

    protected virtual void Update()
    {
        if (isDead) return;

        if (player == null)
        {
            findPlayerTimer -= Time.deltaTime;
            if (findPlayerTimer <= 0f)
            {
                findPlayerTimer = 1f;
                player = FindAnyObjectByType<PlayerController>();
            }
        }

        if (!battleActive || player == null || player.IsDead) return;

        // Orientacion constante mientras el combate esta activo. Los estados
        // de ataque pueden bloquear el giro con LockFacing() en el impacto.
        UpdateFacing();

        stateTime += Time.deltaTime;
        TickAI();
    }

    /// <summary>
    /// Logica de la FSM. El derived aumenta 'stateTime' automaticamente
    /// desde Update() y decide transiciones con ChangeState().
    /// </summary>
    protected virtual void TickAI() { }

    /// <summary>
    /// Transicion centralizada: sale del estado anterior, entra al nuevo y
    /// notifica a los listeners. Unico punto por el que cambia 'currentState'.
    /// </summary>
    protected void ChangeState(BossState next)
    {
        if (currentState == next) return;

        OnExitState(currentState);
        currentState = next;
        stateTime = 0f;
        meleeHits.Clear();
        ClearAttackTriggers(); // evita disparos diferidos al volver a un estado
        OnEnterState(next);
        StateChanged?.Invoke(next);
    }

    /// <summary>Se llama al entrar en un estado. El derived sincroniza animator/collider.</summary>
    protected virtual void OnEnterState(BossState state) { }

    /// <summary>Se llama al salir de un estado. El derived limpia avisos/flags.</summary>
    protected virtual void OnExitState(BossState state) { }

    /// <summary>
    /// Inicia el encuentro. Idempotente: la zona puede llamarlo varias veces.
    /// </summary>
    public virtual void ActivateBattle()
    {
        if (battleActive || isDead) return;
        battleActive = true;
        animator?.SetBool(AnimBattleActive, true);
        ChangeState(BossState.Idle);
        BattleStarted?.Invoke();
    }

    /// <summary>
    /// Devuelve al jefe a su estado inicial. Lo usa el trigger de arena en
    /// reintentos sin recarga de escena; en un retry normal la escena se
    /// recarga y este es el estado inicial de todas formas.
    /// </summary>
    public virtual void ResetForEncounter()
    {
        battleActive = false;
        busy = false;
        facingLockTimer = 0f;
        meleeHits.Clear();
        StopAllCoroutines();
        attackInvulnerable = false;
        ClearModelTint();
        // El derived limpia proyectiles, cooldowns, tilt y triggers de ataque.
        OnCombatReset();

        contactDamageEnabled = true;
        SetFlying(false);
        if (modelRoot != null)
        {
            modelRoot.localRotation = modelBaseLocalRotation;
            modelRoot.localScale = modelBaseLocalScale;
        }
        facingSign = baseFacesRight ? 1 : -1;
        visualTilt = 0f;

        if (animator != null) animator.speed = 1f;
        animator?.SetBool(AnimBattleActive, false);
        animator?.SetBool(AnimEnraged, false);
        animator?.SetBool(AnimIsFlying, false);
        animator?.SetBool(AnimWalking, false);
        ClearAttackTriggers();
        animator?.ResetTrigger(AnimDie);
        animator?.SetTrigger(AnimIdleReset);

        health = maxHealth;
        enraged = false;
        isDead = false;
        speedBoosted = false;
        ChangeState(initialState);
        HealthChanged?.Invoke(health, maxHealth);
    }

    /// <summary>Guarda si la fase 2 ya acelero la animacion, para el reset.</summary>
    protected bool speedBoosted;

    public void TakeDamage(int amount, Vector2 sourcePosition)
    {
        // Unico escudo permitido: el de la ejecución de un ataque. No hay
        // i-frames por golpe recibido, así que Idle/Walk siempre son dañables.
        if (isDead || attackInvulnerable) return;

        health -= amount;
        HealthChanged?.Invoke(Mathf.Max(0, health), maxHealth);

        if (health <= 0)
        {
            Die();
            return;
        }

        if (!enraged && health <= maxHealth * phase2Threshold)
        {
            enraged = true;
            animator?.SetBool(AnimEnraged, true);
            PhaseChanged?.Invoke();
            OnPhaseTwoTriggered();
            return;
        }

        OnDamaged();
    }

    /// <summary>Hook opcional al recibir un golpe. Nunca debe cambiar de estado.</summary>
    protected virtual void OnDamaged() { }

    /// <summary>Hook para que el jefe arranque su secuencia de fase 2.</summary>
    protected virtual void OnPhaseTwoTriggered() { }

    /// <summary>Limpieza extra al morir o al reiniciar el encuentro (proyectiles, etc.).</summary>
    protected virtual void OnCombatReset() { }

    /// <summary>Permite a los ataques con Animation Events validar el estado actual.</summary>
    protected virtual bool IsAttackState(BossState state) => false;

    /// <summary>
    /// Garantiza que al volver a Idle/Walk no quede ningún resto de i-frames.
    /// Se llama al entrar en esos estados y al reiniciar el encuentro.
    /// </summary>
    protected void BreakInvulnerability()
    {
        attackInvulnerable = false;
        busy = false;
    }

    /// <summary>Reinicia el clip del super grito en cada rafaga de la fase 2.</summary>
    protected void RestartAttackAnimation(int stateHash) => animator?.Play(stateHash, 0, 0f);

    protected void BeginAttack()
    {
        busy = true;
        attackInvulnerable = true;
        meleeHits.Clear();
    }

    protected void EndAttack()
    {
        busy = false;
        attackInvulnerable = false;
        meleeHits.Clear();
    }

    /// <summary>Limpia la ventana de golpeo. Util entre los golpes de un combo.</summary>
    protected void ResetMeleeWindow() => meleeHits.Clear();

    private MaterialPropertyBlock tintBlock;

    /// <summary>Tinta el modelo para avisar de un ataque inminente.</summary>
    protected void SetModelTint(Color color)
    {
        if (modelRoot == null) return;
        if (tintBlock == null) tintBlock = new MaterialPropertyBlock();

        if (modelRenderers == null) modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            if (modelRenderers[i] == null) continue;
            modelRenderers[i].GetPropertyBlock(tintBlock);
            tintBlock.SetColor("_BaseColor", color);
            tintBlock.SetColor("_Color", color);
            modelRenderers[i].SetPropertyBlock(tintBlock);
        }
    }

    protected void ClearModelTint()
    {
        if (modelRoot == null) return;
        if (modelRenderers == null) modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < modelRenderers.Length; i++)
            if (modelRenderers[i] != null) modelRenderers[i].SetPropertyBlock(null);
    }

    protected void Trigger(int parameter) => animator?.SetTrigger(parameter);
    protected void SetBool(int parameter, bool value) => animator?.SetBool(parameter, value);

    /// <summary>
    /// Los triggers no se consumen si el Animator no tiene una transicion
    /// activa: se quedan colgados y pueden disparar un ataque al volver a
    /// Idle. Se limpian en cada cambio de estado (Die se conserva).
    /// </summary>
    protected virtual void ClearAttackTriggers()
    {
        if (animator == null) return;
        animator.ResetTrigger(AnimBasicAttack);
        animator.ResetTrigger(AnimComboAttack);
        animator.ResetTrigger(AnimSonic);
        animator.ResetTrigger(AnimFangs);
        animator.ResetTrigger(AnimDive);
        animator.ResetTrigger(AnimSweep);
        animator.ResetTrigger(AnimIdleReset);
        animator.ResetTrigger(AnimSuperSonic);
        // Hurt/Vulnerable ya no existen: ningun golpe puede cortar una animacion.
    }

    /// <summary>Cambia el estado aereo. Los jefes pueden override para ajustar su collider.</summary>
    protected virtual void SetFlying(bool value)
    {
        animator?.SetBool(AnimIsFlying, value);
        contactDamageEnabled = !value;
    }

    protected void SetSpeed(float value)
    {
        if (animator != null) animator.speed = value;
    }

    /// <summary>
    /// Convierte tiempos authored en tiempos de gameplay cuando el Animator
    /// corre acelerado. Los impactos los gobierna C#, pero deben conservar la
    /// misma fraccion del clip durante Enraged.
    /// </summary>
    protected float AnimatorTime(float authoredSeconds)
    {
        float speed = animator != null ? Mathf.Max(0.01f, animator.speed) : 1f;
        return authoredSeconds / speed;
    }

    protected Vector2 PlayerPosition => player != null ? (Vector2)player.transform.position : (Vector2)transform.position;

    private float facingLockTimer;

    /// <summary>Impide voltear al jefe durante 'duration' segundos.</summary>
    protected void LockFacing(float duration)
    {
        facingLockTimer = Mathf.Max(facingLockTimer, duration);
    }

    /// <summary>Libera el giro al entrar en fases donde debe mirar al jugador.</summary>
    protected void ResetFacingLock() => facingLockTimer = 0f;

    protected virtual void UpdateFacing()
    {
        if (facingLockTimer > 0f)
        {
            facingLockTimer -= Time.deltaTime;
            return;
        }
        FaceTarget();
    }

    /// <summary>Voltea el modelo hacia el jugador. No escribe si ya esta mirando.</summary>
    protected void FaceTarget()
    {
        if (player == null || modelRoot == null) return;
        float dx = player.transform.position.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.05f) return;

        SetFacing(dx > 0f ? 1 : -1);
    }

    /// <summary>Fuerza el sentido horizontal (util para la trayectoria del Dive).</summary>
    protected void SetFacing(int sign)
    {
        if (sign == facingSign) return;
        facingSign = sign;
        ApplyFacing(sign);
    }

    /// <summary>
    /// Inclina el modelo en el plano de pantalla (eje Z del mundo). Se compone
    /// con el facing para que la picada diagonal siga la ruta real del golpe.
    /// </summary>
    protected void SetVisualTilt(float degrees)
    {
        if (Mathf.Approximately(visualTilt, degrees)) return;
        visualTilt = degrees;
        ApplyFacing(facingSign);
    }

    /// <summary>
    /// Aplica el giro de 180 grados sobre la rotacion base del modelo y la
    /// inclinacion opcional. No toca localScale: en este prefab el root de la
    /// escena ya tiene una rotacion Y, asi que invertir scaling X no cambiaba
    /// la direccion en pantalla (y ademas podia dejar la malla invertida).
    /// </summary>
    private void ApplyFacing(int sign)
    {
        if (modelRoot == null) return;
        bool wantRight = sign > 0;
        Quaternion facingLocal = wantRight != baseFacesRight
            ? modelBaseLocalRotation * Quaternion.Euler(0f, 180f, 0f)
            : modelBaseLocalRotation;

        Quaternion world = Quaternion.Euler(0f, 0f, visualTilt) * (transform.rotation * facingLocal);
        modelRoot.localRotation = Quaternion.Inverse(transform.rotation) * world;
    }

    /// <summary>
    /// Golpe de cuerpo a cuerpo en frente del jefe. Cada objetivo recibe
    /// como mucho un golpe por ventana gracias a 'meleeHits'.
    /// </summary>
    protected void DoMeleeHit(float radius, int damage)
    {
        if (isDead || !battleActive) return;
        radius = Mathf.Abs(radius);

        Vector2 center = (Vector2)transform.position + new Vector2(facingSign * radius * 0.5f, 0f);

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable target = hits[i].GetComponentInParent<IDamageable>();
            if (target == null || target.DamageTeam != Team.Player) continue;
            if (!meleeHits.Add(target)) continue;
            target.TakeDamage(damage, transform.position);
        }
    }

    /// <summary>
    /// Para llamar desde un Animation Event al impacto del melee. Ignora
    /// eventos tardios si el FSM ya salio del ataque y volvio a Idle.
    /// </summary>
    public void MeleeHitEvent(float radius, int damage)
    {
        if (isDead || !IsAttackState(currentState)) return;
        DoMeleeHit(radius, damage);
    }

    /// <summary>Bloquea el giro brevemente en el impacto para dar claridad al golpe.</summary>
    protected void MeleeLockFacingOnHit() => LockFacing(0.12f);

    /// <summary>
    /// Mueve el root hacia un punto con velocidad constante y limite de tiempo.
    /// El destino y cada paso pasan por ClampToArena.
    /// </summary>
    protected IEnumerator MoveToRoutine(Vector3 target, float speed, float maxTime)
    {
        target = ClampToArena(target);
        float elapsed = 0f;
        while (elapsed < maxTime && Vector2.Distance(transform.position, target) > 0.12f)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            transform.position = ClampToArena(next);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>Golpe de area contra el bando del jugador.</summary>
    protected void HitAround(float radius, int damage)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius);
        var alreadyHit = new HashSet<IDamageable>();

        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable target = hits[i].GetComponentInParent<IDamageable>();
            if (target == null || target.DamageTeam != Team.Player) continue;
            if (!alreadyHit.Add(target)) continue;
            target.TakeDamage(damage, transform.position);
        }
    }

    private void Die()
    {
        isDead = true;
        busy = true;
        StopAllCoroutines();
        attackInvulnerable = false;
        ClearModelTint();
        OnCombatReset();
        animator?.SetBool(AnimWalking, false);
        animator?.SetTrigger(AnimDie);
        ChangeState(BossState.Dead);
        ScoreManager.AddPoints(1000);
        Defeated?.Invoke();
        Destroy(gameObject, 4.5f);
    }
}
