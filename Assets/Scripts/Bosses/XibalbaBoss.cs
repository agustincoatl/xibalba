using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Jefe 2: Xibalba.
///
/// Ciclo autoritativo:
///   Dormant -> StartBattle -> Idle -> (ataque suelto) -> Idle
///                                  \-> (ataque + pausa corta + ataque) -> Idle
///   Fase 2: cualquier estado -> PhaseTransition (Screem) -> Idle
///   Muerte: cualquier estado -> Dead (animacion Dead, trigger base "Die").
///
/// Diseno 2.5D: el root se mantiene dentro de la arena con ClampToArena() y el
/// modelo voltea con rotacion Y (logica heredada de BossController), nunca con
/// localScale negativo.
///
/// Ataques:
///   - BasicAttackA / BasicAttackB: melee alterno, con pequeno lunge.
///   - Cast: fija direccion hacia el jugador con una linea de aviso y lanza
///     orbes rectos (gravityScale = 0); una vez fijada NO persiguen.
///   - JumpAttack: salto en arco hacia un objetivo, HitAround al aterrizar y
///     dos ondas rasantes con BossProjectile.
///   - ObsidianAttack: puntas que asoman antes de activarse y golpean por area.
/// </summary>
public class XibalbaBoss : BossController
{
    // ------------------------------------------------------------ ANIMATOR
    // Los hashes base (BattleActive, Enraged, IdleReset, Die, Walking, ...)
    // se heredan de BossController. Screem se escribe con doble 'e' porque es
    // el nombre del parametro que pedimos en el Animator (el clip se llama
    // "scream," en el archivo importado).
    private static readonly int AnimStartBattle = Animator.StringToHash("StartBattle");
    private static readonly int AnimBasicAttackA = Animator.StringToHash("BasicAttackA");
    private static readonly int AnimBasicAttackB = Animator.StringToHash("BasicAttackB");
    private static readonly int AnimCast = Animator.StringToHash("Cast");
    private static readonly int AnimJumpAttack = Animator.StringToHash("JumpAttack");
    private static readonly int AnimObsidianAttack = Animator.StringToHash("ObsidianAttack");
    private static readonly int AnimScreem = Animator.StringToHash("Screem");

    // ---------------------------------------------------------------- ARENA
    [Header("Arena (ajusta en Inspector)")]
    [Tooltip("Calcula los limites desde la posicion de spawn del jefe.")]
    [SerializeField] private bool autoFitArenaOnAwake = true;
    [SerializeField] private float arenaHalfWidth = 9f;
    [Tooltip("Y del suelo relativa al spawn (spawn.y + este valor).")]
    [SerializeField] private float arenaGroundOffset = -2f;
    [SerializeField] private float arenaHeight = 11f;

    [Tooltip("Limites manuales. Se usan si desactivas Auto Fit.")]
    [SerializeField] private float arenaMinX = -9f;
    [SerializeField] private float arenaMaxX = 9f;
    [SerializeField] private float groundY = -2f;
    [SerializeField] private float ceilingY = 9f;

    [Header("Zona de combate")]
    [SerializeField] private bool createRuntimeArenaTrigger = true;
    [SerializeField] private float entranceTriggerWidth = 4f;
    [Tooltip("Capsula del jefe. Se pone en trigger mientras salta para no chocar con el escenario.")]
    [SerializeField] private CapsuleCollider2D bodyCollider;

    // ---------------------------------------------------------------- CICLO
    [Header("Idle y cadena de ataques")]
    [Tooltip("Segundos quieto en Idle antes de decidir (Fase 1).")]
    [SerializeField, Min(0.05f)] private float idleTime = 1.1f;
    [Tooltip("Idle en Fase 2. Dejalo menor que idleTime para aumentar la presion.")]
    [SerializeField, Min(0.05f)] private float phase2IdleTime = 0.7f;

    [Tooltip("Probabilidad base de encadenar 2 ataques.")]
    [SerializeField, Range(0f, 1f)] private float chainChance = 0.25f;
    [Tooltip("Probabilidad extra de cadena en Fase 2.")]
    [SerializeField, Range(0f, 1f)] private float phase2ChainChanceBonus = 0.2f;
    [Tooltip("Fuerza la cadena cada N decisiones. 0 = solo probabilidad.")]
    [SerializeField, Min(0)] private int chainEveryTurns = 3;
    [Tooltip("Pausa lectura entre el 1.er y el 2.do ataque. El jefe queda vulnerable.")]
    [SerializeField, Min(0.05f)] private float chainGap = 0.45f;

    [Tooltip("Cooldown comun entre inicios de ataque.")]
    [SerializeField, Min(0f)] private float attackCooldown = 0.5f;
    [SerializeField, Range(0.3f, 1f)] private float phase2AttackCooldownMultiplier = 0.65f;
    [Tooltip("Velocidad del Animator en Fase 2 (acompania a la cadencia C#).")]
    [SerializeField, Range(1f, 2f)] private float phase2AnimatorSpeed = 1.15f;

    // ------------------------------------------------------------ START BATTLE
    [Header("StartBattle (arrodillarse / alzarse)")]
    [Tooltip("Debe cubrir la longitud del clip Start_Battle. El jefe es invulnerable mientras entra.")]
    [SerializeField, Min(0.1f)] private float startBattleDuration = 1.6f;

    // ------------------------------------------------------------ BASIC ATTACK
    [Header("BasicAttack A / B (melee alterno)")]
    [SerializeField, Min(0.05f)] private float basicWindup = 0.22f;
    [SerializeField, Min(0.1f)] private float basicDuration = 0.58f;
    [SerializeField, Min(0.1f)] private float basicHitRadius = 1.6f;
    [SerializeField, Min(0)] private int basicDamage = 1;
    [SerializeField, Min(0f)] private float basicLungeDistance = 1.05f;
    [SerializeField, Min(0.1f)] private float basicLungeSpeed = 7f;

    // ------------------------------------------------------------------- CAST
    [Header("Cast (orbes con direccion fijada)")]
    [Tooltip("Origen opcional. Vacio = transform.position + castOriginOffset.")]
    [SerializeField] private Transform castOriginPoint;
    [SerializeField] private Vector2 castOriginOffset = new Vector2(0f, 1.2f);
    [Tooltip("La linea sigue al jugador durante este tiempo; al terminar se fija.")]
    [SerializeField, Min(0.05f)] private float castLockTime = 0.55f;
    [Tooltip("Pausa con la linea ya fija, antes de soltar el primer orbe.")]
    [SerializeField, Min(0f)] private float castReleaseDelay = 0.18f;
    [SerializeField, Min(0.2f)] private float castDuration = 1.5f;
    [SerializeField, Min(1)] private int castOrbCount = 3;
    [SerializeField, Range(0f, 180f)] private float castSpreadAngle = 28f;
    [SerializeField, Min(0.1f)] private float castOrbSpeed = 7f;
    [SerializeField, Min(0.5f)] private float castOrbLifetime = 2.2f;
    [SerializeField, Min(1)] private int castBursts = 1;
    [SerializeField, Min(0.05f)] private float castBurstInterval = 0.28f;
    [SerializeField, Min(0)] private int castDamage = 1;
    [SerializeField] private BossAttackVisual castOrbVisual = new BossAttackVisual
    {
        color = new Color(0.45f, 0.85f, 1f, 1f), radius = 0.22f, sortingOrder = 21
    };

    // ------------------------------------------------------------- JUMP ATTACK
    [Header("JumpAttack (salto + onda sismica)")]
    [Tooltip("Zona objetivo. Desactivada = se salta hacia el jugador, limitado a la arena.")]
    [SerializeField] private BossAttackArea jumpTargetArea = new BossAttackArea
    {
        enabled = false, space = BossAreaSpace.Arena, rect = new Rect(0f, 0f, 10f, 6f),
        offset = Vector2.zero, clampToArena = true
    };
    [SerializeField, Min(0.05f)] private float jumpWarnTime = 0.5f;
    [SerializeField, Min(0.1f)] private float jumpTravelTime = 0.65f;
    [SerializeField, Min(0.5f)] private float jumpHeight = 3.2f;
    [SerializeField, Min(0.1f)] private float jumpLandingTime = 0.35f;
    [SerializeField, Min(0.1f)] private float jumpImpactRadius = 1.7f;
    [SerializeField, Min(0)] private int jumpDamage = 1;
    [SerializeField] private BossAttackVisual jumpWaveVisual = new BossAttackVisual
    {
        color = new Color(1f, 0.55f, 0.16f, 1f), radius = 0.2f, sortingOrder = 21
    };
    [SerializeField, Min(0.1f)] private float jumpWaveSpeed = 5f;
    [SerializeField, Min(0.2f)] private float jumpWaveLifetime = 1.3f;
    [SerializeField, Min(0)] private int jumpWaveDamage = 1;

    // --------------------------------------------------------- OBSIDIAN ATTACK
    [Header("ObsidianAttack (pilares con aviso)")]
    [Tooltip("Area de aparicion. Desactivada = toda la arena con margen.")]
    [SerializeField] private BossAttackArea obsidianSpawnArea = new BossAttackArea
    {
        enabled = false, space = BossAreaSpace.Arena, rect = new Rect(0f, 0f, 10f, 6f),
        offset = Vector2.zero, clampToArena = true
    };
    [Tooltip("Si es true se ignoran columnas del area y se ponen en anillo alrededor del jugador.")]
    [SerializeField] private bool obsidianAroundPlayer = false;
    [SerializeField, Min(1)] private int obsidianPillarCount = 6;
    [Tooltip("La punta asoma durante este tiempo antes de dispararse.")]
    [SerializeField, Min(0.05f)] private float obsidianWarnTime = 0.85f;
    [SerializeField, Min(0.05f)] private float obsidianActiveTime = 0.25f;
    [SerializeField, Min(0.1f)] private float obsidianDuration = 1.6f;
    [SerializeField, Min(0.1f)] private float obsidianPillarHeight = 1.6f;
    [SerializeField, Min(0.05f)] private float obsidianPillarWidth = 0.45f;
    [SerializeField, Min(0.1f)] private float obsidianHitRadius = 0.55f;
    [SerializeField, Min(0)] private int obsidianDamage = 1;
    [Tooltip("Un solo golpe por jugador y oleada, aunque toque varios pilares.")]
    [SerializeField] private bool obsidianHitOncePerWave = true;
    [SerializeField] private BossAttackVisual obsidianVisual = new BossAttackVisual
    {
        color = new Color(0.22f, 0.08f, 0.32f, 1f), radius = 0.22f, sortingOrder = 22
    };

    // --------------------------------------------------------------- PHASE 2
    [Header("Fase 2: Screem")]
    [Tooltip("Duracion del grito de cambio de fase. Invulnerable mientras dura.")]
    [SerializeField, Min(0.5f)] private float phase2ScreamDuration = 1.8f;

    // --------------------------------------------------------------- VISUALES
    [Header("Avisos y feedback")]
    [SerializeField] private Color warningColor = new Color(1f, 0.32f, 0.18f, 1f);
    [SerializeField] private Color obsidianWarningColor = new Color(0.78f, 0.38f, 1f, 1f);
    [SerializeField] private Material warningLineMaterial;

    // ----------------------------------------------------------- ESTADO INTERNO
    private BossCombatVfx combatVfx;
    private AttackRateVfx attackRateVfx;

    private LineRenderer warningLine;
    private readonly List<GameObject> obsidianVisuals = new List<GameObject>();
    private readonly List<Vector3> obsidianFullScales = new List<Vector3>();
    private readonly List<Vector2> obsidianPositions = new List<Vector2>();
    private readonly HashSet<IDamageable> obsidianHits = new HashSet<IDamageable>();

    private readonly Dictionary<BossState, float> attackReadyAt = new Dictionary<BossState, float>();

    // BasicAttack se usa como "clave" del par A/B para compartir cooldown.
    // Dormant se usa como centinela de "no hay segundo ataque pendiente".
    private static readonly BossState[] AttackOptions =
    {
        BossState.BasicAttack, BossState.Cast, BossState.JumpAttack, BossState.ObsidianAttack
    };

    private BossState pendingSecondAttack = BossState.Dormant;
    private float chainGapRemaining;
    private int attackTurn;
    private bool nextBasicIsB;
    private bool introPlayed;
    private bool phase2ScreamPending;

    private int phase;
    private float phaseTimer;
    private bool attackHitDone;

    private bool basicLungeActive;
    private float basicLungeTargetX;

    private Vector2 castOrigin;
    private Vector2 castFixedDirection;
    private Vector2 castLockTarget;
    private int castBurstsFired;

    private Vector3 jumpStart;
    private Vector2 jumpTarget;
    private bool jumpHitDone;

    private float arenaZ;
    private float bodyExtX = 0.6f;
    private float bodyExtY = 0.95f;
    private float bodyOffsetX;
    private float bodyOffsetY;

    private float GroundRestY => groundY - bodyOffsetY + bodyExtY;
    private float CurrentIdleTime => Enraged ? phase2IdleTime : idleTime;
    private float CurrentChainChance => Mathf.Clamp01(chainChance + (Enraged ? phase2ChainChanceBonus : 0f));

    // ============================================================== CICLO DE VIDA
    protected override void Awake()
    {
        base.Awake();

        combatVfx = GetComponent<BossCombatVfx>();
        if (combatVfx == null) combatVfx = gameObject.AddComponent<BossCombatVfx>();
        attackRateVfx = GetComponent<AttackRateVfx>();
        if (attackRateVfx == null) attackRateVfx = gameObject.AddComponent<AttackRateVfx>();

        if (bodyCollider == null) bodyCollider = GetComponent<CapsuleCollider2D>();

        Vector3 spawn = transform.position;
        arenaZ = Mathf.Abs(spawn.z) < 2f ? spawn.z : 0f;
        spawn.z = arenaZ;
        transform.position = spawn;

        if (autoFitArenaOnAwake)
        {
            arenaMinX = spawn.x - arenaHalfWidth;
            arenaMaxX = spawn.x + arenaHalfWidth;
            groundY = spawn.y + arenaGroundOffset;
            ceilingY = groundY + arenaHeight;
        }

        NormalizeArena();

        RefreshBodyMetrics();

        Vector3 grounded = ClampToArena(new Vector3(transform.position.x, GroundRestY, arenaZ));
        transform.position = grounded;

        if (createRuntimeArenaTrigger && Object.FindAnyObjectByType<BossArenaTrigger>() == null)
            CreateRuntimeArenaTrigger();
    }

    protected override void Start()
    {
        base.Start();

        // Si el jugador ya esta dentro del arena al cargar la escena.
        if (!BattleActive && player != null && !player.IsDead)
        {
            Vector2 p = player.transform.position;
            if (p.x >= arenaMinX && p.x <= arenaMaxX &&
                p.y >= groundY - 2f && p.y <= ceilingY + 2f)
            {
                ActivateBattle();
            }
        }
    }

    private void OnValidate()
    {
        NormalizeArena();
        if (phase2IdleTime <= 0f) phase2IdleTime = idleTime;
    }

    public override void ActivateBattle()
    {
        if (BattleActive || IsDead) return;

        // Base: enciende BattleActive, avisa a la UI y entra en Idle.
        base.ActivateBattle();
        if (!BattleActive) return;

        // Xibalba siempre hace su entrada antes de quedar en Idle.
        if (!introPlayed) ChangeState(BossState.StartBattle);
        FaceTarget();
    }

    protected override void OnPhaseTwoTriggered()
    {
        phase2ScreamPending = true;

        // Si la entrada o el grito ya estan en curso no los cortamos: el
        // pending se consume al terminar StartBattle.
        if (BattleActive && currentState != BossState.StartBattle &&
            currentState != BossState.PhaseTransition && currentState != BossState.Dead)
        {
            ChangeState(BossState.PhaseTransition);
        }
    }

    protected override void OnCombatReset()
    {
        ClearResidualAttackState();

        attackReadyAt.Clear();
        pendingSecondAttack = BossState.Dormant;
        chainGapRemaining = 0f;
        attackTurn = 0;
        nextBasicIsB = false;
        introPlayed = false;
        phase2ScreamPending = false;

        if (bodyCollider != null) bodyCollider.isTrigger = false;
        if (animator != null) animator.speed = 1f;
    }

    /// <summary>Limpia timers/flags de un ataque sin tocar la FSM.</summary>
    private void ClearResidualAttackState()
    {
        ResetMeleeWindow();
        EndAttack();
        DestroyWarningLine();
        DestroyObsidianVisuals();
        ClearModelTint();
        BossProjectile.ClearFrom(this);

        phase = 0;
        phaseTimer = 0f;
        attackHitDone = false;
        basicLungeActive = false;
        castBurstsFired = 0;
        jumpHitDone = false;
        obsidianHits.Clear();
        obsidianPositions.Clear();
        ResetFacingLock();
        SetVisualTilt(0f);
    }

    /// <summary>
    /// Extiende la limpieza de triggers de BossController con los parametros
    /// propios del jefe. ChangeState() la llama antes de OnEnterState().
    /// </summary>
    protected override void ClearAttackTriggers()
    {
        base.ClearAttackTriggers();
        animator?.ResetTrigger(AnimStartBattle);
        animator?.ResetTrigger(AnimBasicAttackA);
        animator?.ResetTrigger(AnimBasicAttackB);
        animator?.ResetTrigger(AnimCast);
        animator?.ResetTrigger(AnimJumpAttack);
        animator?.ResetTrigger(AnimObsidianAttack);
        animator?.ResetTrigger(AnimScreem);
        // AnimDie NO se resetea: BossController lo conserva a proposito.
    }

    // --------------------------------------------------------------- TRANSICIONES
    protected override void OnEnterState(BossState state)
    {
        phase = 0;
        phaseTimer = 0f;
        attackHitDone = false;
        basicLungeActive = false;
        castBurstsFired = 0;
        jumpHitDone = false;
        obsidianHits.Clear();
        obsidianPositions.Clear();

        switch (state)
        {
            case BossState.Idle:
                BreakInvulnerability();
                SetFlying(false);
                SetBool(AnimWalking, false);
                ResetFacingLock();
                FaceTarget();
                // ChangeState ya limpio los triggers; este devuelve la pose base.
                Trigger(AnimIdleReset);
                break;

            case BossState.Walk:
                // Compatibilidad: no se usa. TickAI() lo devuelve a Idle.
                BreakInvulnerability();
                SetFlying(false);
                break;

            case BossState.StartBattle:
                introPlayed = true;
                BeginAttack();
                SetFlying(false);
                SetBool(AnimWalking, false);
                Trigger(AnimStartBattle);
                combatVfx?.PlayCharge(transform.position, warningColor);
                SetModelTint(warningColor);
                break;

            case BossState.PhaseTransition:
                ClearResidualAttackState();
                BeginAttack();
                SetSpeed(phase2AnimatorSpeed);
                SetFlying(false);
                phase2ScreamPending = false;
                pendingSecondAttack = BossState.Dormant;
                chainGapRemaining = 0f;
                attackReadyAt.Clear();
                Trigger(AnimScreem);
                combatVfx?.PlayCharge(transform.position, new Color(0.75f, 0.3f, 1f));
                SetModelTint(obsidianWarningColor);
                break;

            case BossState.BasicAttackA:
            case BossState.BasicAttackB:
                BeginAttack();
                RegisterCooldown(state);
                BeginBasicLunge();
                Trigger(state == BossState.BasicAttackA ? AnimBasicAttackA : AnimBasicAttackB);
                // Alterna A/B en cada ejecucion, no por random.
                nextBasicIsB = state == BossState.BasicAttackA;
                attackRateVfx?.PlayAttackPulse(transform.position, new Color(1f, 0.45f, 0.15f), 1.1f);
                combatVfx?.PlayCharge(transform.position, new Color(1f, 0.5f, 0.18f));
                SetModelTint(warningColor);
                break;

            case BossState.Cast:
                BeginAttack();
                RegisterCooldown(state);
                BeginCast();
                Trigger(AnimCast);
                combatVfx?.PlayCharge(castOrigin, new Color(0.4f, 0.8f, 1f));
                SetModelTint(new Color(0.35f, 0.7f, 1f, 1f));
                break;

            case BossState.JumpAttack:
                BeginAttack();
                RegisterCooldown(state);
                BeginJump();
                Trigger(AnimJumpAttack);
                break;

            case BossState.ObsidianAttack:
                BeginAttack();
                RegisterCooldown(state);
                BeginObsidian();
                Trigger(AnimObsidianAttack);
                break;
        }
    }

    protected override void OnExitState(BossState state)
    {
        SetBool(AnimWalking, false);
        ClearModelTint();
        DestroyWarningLine();

        if (state == BossState.JumpAttack) SetFlying(false);
        if (state == BossState.ObsidianAttack) DestroyObsidianVisuals();

        // Idle/Walk nunca heredan i-frames del ataque anterior.
        if (IsAttackState(state) || state == BossState.PhaseTransition)
            EndAttack();
    }

    protected override bool IsAttackState(BossState state)
    {
        return state == BossState.StartBattle ||
               state == BossState.BasicAttackA || state == BossState.BasicAttackB ||
               state == BossState.Cast || state == BossState.JumpAttack ||
               state == BossState.ObsidianAttack;
    }

    // ------------------------------------------------------------------- TICK FSM
    protected override void TickAI()
    {
        switch (currentState)
        {
            case BossState.Idle: TickIdle(); break;
            case BossState.Walk: ChangeState(BossState.Idle); break;
            case BossState.StartBattle: TickStartBattle(); break;
            case BossState.PhaseTransition: TickPhaseTransition(); break;
            case BossState.BasicAttackA:
            case BossState.BasicAttackB: TickBasicAttack(); break;
            case BossState.Cast: TickCast(); break;
            case BossState.JumpAttack: TickJumpAttack(); break;
            case BossState.ObsidianAttack: TickObsidianAttack(); break;
        }
    }

    private void TickIdle()
    {
        if (player == null || player.IsDead) return;
        StayOnGround();
        FaceTarget();

        // Pausa de lectura entre los dos ataques de una cadena.
        if (chainGapRemaining > 0f)
        {
            chainGapRemaining -= Time.deltaTime;
            if (chainGapRemaining <= 0f)
            {
                BossState next = pendingSecondAttack;
                pendingSecondAttack = BossState.Dormant;
                chainGapRemaining = 0f;

                if (next != BossState.Dormant && IsAttackReady(next))
                {
                    ChangeState(next);
                    return;
                }
            }
            return;
        }

        if (stateTime < CurrentIdleTime) return;

        if (!TryPickNextAttack(out BossState selected)) return;

        attackTurn++;
        pendingSecondAttack = ShouldChain() ? PickSecondAttack(selected) : BossState.Dormant;
        ChangeState(selected);
    }

    private bool ShouldChain()
    {
        bool byTurn = chainEveryTurns > 0 && attackTurn % chainEveryTurns == 0;
        return byTurn || Random.value < CurrentChainChance;
    }

    private BossState PickSecondAttack(BossState first)
    {
        // Se excluye la misma "clave" de cooldown: no A seguido de B, ni dos Cast.
        return TryPickNextAttack(out BossState second, CooldownKey(first)) ? second : BossState.Dormant;
    }

    /// <summary>Elige entre las opciones listas. 'exclude' usa claves de cooldown.</summary>
    private bool TryPickNextAttack(out BossState selected, BossState exclude = BossState.Dormant)
    {
        int readyCount = 0;
        for (int i = 0; i < AttackOptions.Length; i++)
        {
            BossState key = AttackOptions[i];
            if (key != exclude && IsAttackReady(key)) readyCount++;
        }

        if (readyCount == 0)
        {
            selected = BossState.Dormant;
            return false;
        }

        int pick = Random.Range(0, readyCount);
        for (int i = 0; i < AttackOptions.Length; i++)
        {
            BossState key = AttackOptions[i];
            if (key == exclude || !IsAttackReady(key)) continue;
            if (pick-- != 0) continue;

            selected = ResolveAttack(key);
            return true;
        }

        selected = BossState.Dormant;
        return false;
    }

    /// <summary>Resuelve el par melee alterno segun el ultimo uso.</summary>
    private BossState ResolveAttack(BossState option)
    {
        if (option == BossState.BasicAttack)
            return nextBasicIsB ? BossState.BasicAttackB : BossState.BasicAttackA;
        return option;
    }

    /// <summary>A y B comparten cooldown para que la alternancia no duplique ritmo.</summary>
    private static BossState CooldownKey(BossState state)
    {
        return state == BossState.BasicAttackA || state == BossState.BasicAttackB
            ? BossState.BasicAttack
            : state;
    }

    private bool IsAttackReady(BossState state)
    {
        BossState key = CooldownKey(state);
        return !attackReadyAt.TryGetValue(key, out float readyAt) || Time.time >= readyAt;
    }

    private void RegisterCooldown(BossState state)
    {
        float multiplier = Enraged ? phase2AttackCooldownMultiplier : 1f;
        attackReadyAt[CooldownKey(state)] = Time.time + attackCooldown * multiplier;
    }

    private void FinishAttack()
    {
        if (pendingSecondAttack != BossState.Dormant)
            chainGapRemaining = chainGap;

        if (phase2ScreamPending) ChangeState(BossState.PhaseTransition);
        else ChangeState(BossState.Idle);
    }

    private void FinishIntro()
    {
        pendingSecondAttack = BossState.Dormant;
        chainGapRemaining = 0f;
        if (phase2ScreamPending) ChangeState(BossState.PhaseTransition);
        else ChangeState(BossState.Idle);
    }

    // ------------------------------------------------------------- START BATTLE
    private void TickStartBattle()
    {
        StayOnGround();
        if (stateTime >= AnimatorTime(startBattleDuration))
            FinishIntro();
    }

    // --------------------------------------------------------------- PHASE 2
    private void TickPhaseTransition()
    {
        StayOnGround();

        if (stateTime >= AnimatorTime(phase2ScreamDuration))
        {
            combatVfx?.PlayImpact(transform.position, new Color(0.8f, 0.35f, 1f));
            BreakInvulnerability();
            ChangeState(BossState.Idle);
        }
    }

    // ---------------------------------------------------------- BASIC ATTACK
    private void BeginBasicLunge()
    {
        basicLungeActive = basicLungeDistance > 0.01f;
        float direction = Mathf.Sign(PlayerPosition.x - transform.position.x);
        if (Mathf.Abs(direction) < 0.01f) direction = FacingSign;
        basicLungeTargetX = ClampToArena(new Vector3(
            transform.position.x + direction * basicLungeDistance, GroundRestY, arenaZ)).x;
    }

    private void TickBasicLunge()
    {
        if (!basicLungeActive || stateTime >= AnimatorTime(basicWindup)) return;

        float nextX = Mathf.MoveTowards(transform.position.x, basicLungeTargetX,
            basicLungeSpeed * Time.deltaTime);
        transform.position = ClampToArena(new Vector3(nextX, GroundRestY, arenaZ));
        if (Mathf.Abs(nextX - basicLungeTargetX) <= 0.01f) basicLungeActive = false;
    }

    private void TickBasicAttack()
    {
        TickBasicLunge();

        if (!attackHitDone && stateTime >= AnimatorTime(basicWindup))
        {
            attackHitDone = true;
            MeleeLockFacingOnHit();
            DoMeleeHit(basicHitRadius, basicDamage);
            attackRateVfx?.PlayAttackPulse(transform.position, Color.white, 1.3f);
            combatVfx?.PlayImpact(transform.position, new Color(1f, 0.7f, 0.35f));
        }

        if (stateTime >= AnimatorTime(basicDuration))
            FinishAttack();
    }

    // -------------------------------------------------------------------- CAST
    private Vector2 GetCastOrigin()
    {
        Vector3 p = castOriginPoint != null
            ? castOriginPoint.position
            : transform.position + (Vector3)castOriginOffset;
        return (Vector2)ClampToArena(new Vector3(p.x, p.y, arenaZ));
    }

    private void BeginCast()
    {
        castOrigin = GetCastOrigin();
        castLockTarget = PlayerPosition;
        castFixedDirection = new Vector2(FacingSign, 0f);
        castBurstsFired = 0;
        DestroyWarningLine();
    }

    private void TickCast()
    {
        float lockTime = AnimatorTime(castLockTime);
        float releaseDelay = AnimatorTime(castReleaseDelay);

        switch (phase)
        {
            case 0: // canalizacion: la linea todavia sigue al jugador
                FaceTarget();
                castLockTarget = (Vector2)ClampToArena(new Vector3(
                    PlayerPosition.x, PlayerPosition.y, arenaZ));
                UpdateWarningLine(castOrigin, castLockTarget);

                if (phaseTimer >= lockTime)
                {
                    Vector2 delta = castLockTarget - castOrigin;
                    castFixedDirection = delta.sqrMagnitude > 0.01f
                        ? delta.normalized
                        : new Vector2(FacingSign, 0f);
                    SetFacing(castFixedDirection.x >= 0f ? 1 : -1);
                    // Desde aqui el jefe y los orbes conservan esta direccion.
                    LockFacing(castDuration + 0.5f);
                    phase = 1;
                    phaseTimer = 0f;
                }
                break;

            case 1: // aviso fijado: no vuelve a mirar al jugador
                UpdateWarningLine(castOrigin, castOrigin + castFixedDirection * 9f);
                if (phaseTimer >= releaseDelay)
                {
                    FireCastBurst();
                    phase = 2;
                    phaseTimer = 0f;
                }
                break;

            case 2: // rafagas restantes
                if (castBurstsFired < castBursts)
                {
                    float nextBurstAt = AnimatorTime(castBurstInterval * castBurstsFired);
                    if (phaseTimer >= nextBurstAt) FireCastBurst();
                }
                break;
        }

        phaseTimer += Time.deltaTime;
        if (stateTime >= AnimatorTime(castDuration))
            FinishAttack();
    }

    private void FireCastBurst()
    {
        castBurstsFired++;
        int count = Mathf.Max(1, castOrbCount);
        float baseAngle = Mathf.Atan2(castFixedDirection.y, castFixedDirection.x) * Mathf.Rad2Deg;
        float step = castSpreadAngle / Mathf.Max(1, count - 1);

        for (int i = 0; i < count; i++)
        {
            float angle = (baseAngle - castSpreadAngle * 0.5f + step * i) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
            BossProjectile.Spawn(castOrigin, dir * castOrbSpeed, castDamage,
                0f, castOrbLifetime, castOrbVisual, this);
        }

        attackRateVfx?.PlayAttackPulse(castOrigin, new Color(0.45f, 0.9f, 1f), 1.15f);
        combatVfx?.PlayImpact(castOrigin, new Color(0.4f, 0.8f, 1f));
    }

    // ------------------------------------------------------------- JUMP ATTACK
    private Rect ArenaRect(float inset)
    {
        float minX = Mathf.Min(arenaMinX, arenaMaxX) + Mathf.Abs(inset);
        float maxX = Mathf.Max(arenaMinX, arenaMaxX) - Mathf.Abs(inset);
        float minY = Mathf.Min(groundY, ceilingY) + Mathf.Abs(inset);
        float maxY = Mathf.Max(groundY, ceilingY) - Mathf.Abs(inset);
        if (minX > maxX) minX = maxX = (arenaMinX + arenaMaxX) * 0.5f;
        if (minY > maxY) minY = maxY = (groundY + ceilingY) * 0.5f;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private Rect ResolveArea(BossAttackArea area, Rect fallback, float inset)
    {
        if (!area.enabled) return fallback;

        Rect result = area.rect;
        Vector2 origin;
        switch (area.space)
        {
            case BossAreaSpace.Arena:
                origin = new Vector2(Mathf.Min(arenaMinX, arenaMaxX), Mathf.Min(groundY, ceilingY));
                break;
            case BossAreaSpace.Boss:
                origin = (Vector2)transform.position;
                break;
            default:
                origin = Vector2.zero;
                break;
        }
        result.position += origin + area.offset;

        if (area.clampToArena)
        {
            Rect arena = ArenaRect(inset);
            result.xMin = Mathf.Clamp(result.xMin, arena.xMin, arena.xMax);
            result.xMax = Mathf.Clamp(result.xMax, arena.xMin, arena.xMax);
            result.yMin = Mathf.Clamp(result.yMin, arena.yMin, arena.yMax);
            result.yMax = Mathf.Clamp(result.yMax, arena.yMin, arena.yMax);
        }

        if (result.width <= 0.02f || result.height <= 0.02f) return fallback;
        return result;
    }

    private Vector2 ResolveJumpTarget()
    {
        Vector2 desired = PlayerPosition;
        if (jumpTargetArea.enabled)
        {
            Rect r = ResolveArea(jumpTargetArea, ArenaRect(0.4f), 0.4f);
            desired = new Vector2(Mathf.Clamp(desired.x, r.xMin, r.xMax),
                                  Mathf.Clamp(desired.y, r.yMin, r.yMax));
        }
        return (Vector2)ClampToArena(new Vector3(desired.x, GroundRestY, arenaZ));
    }

    private void BeginJump()
    {
        phase = 0;
        phaseTimer = 0f;
        jumpHitDone = false;

        jumpStart = ClampToArena(transform.position);
        jumpTarget = ResolveJumpTarget();

        SetFacing(jumpTarget.x >= jumpStart.x ? 1 : -1);
        LockFacing(jumpWarnTime + jumpTravelTime + jumpLandingTime + 0.5f);
        SetFlying(true);
        SetModelTint(warningColor);
        UpdateWarningLine(jumpStart, jumpTarget);
    }

    private void TickJumpAttack()
    {
        phaseTimer += Time.deltaTime;

        switch (phase)
        {
            case 0: // aviso en suelo
                UpdateWarningLine(jumpStart, jumpTarget);
                if (phaseTimer >= AnimatorTime(jumpWarnTime))
                {
                    DestroyWarningLine();
                    ClearModelTint();
                    phase = 1;
                    phaseTimer = 0f;
                }
                break;

            case 1: // salto en arco
                float travel = AnimatorTime(jumpTravelTime);
                float t = Mathf.Clamp01(phaseTimer / travel);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                Vector3 p = Vector3.Lerp(jumpStart, jumpTarget, eased);
                p.y += Mathf.Sin(Mathf.PI * t) * jumpHeight;
                transform.position = ClampToArena(p);
                combatVfx?.PlayTrail(transform.position, new Color(1f, 0.5f, 0.18f));

                if (t >= 1f)
                {
                    transform.position = ClampToArena(new Vector3(jumpTarget.x, GroundRestY, arenaZ));
                    SetFlying(false);
                    phase = 2;
                    phaseTimer = 0f;
                }
                break;

            case 2: // impacto + onda
                if (!jumpHitDone)
                {
                    jumpHitDone = true;
                    HitAround(jumpImpactRadius, jumpDamage);
                    FireJumpWaves();
                    attackRateVfx?.PlayAttackPulse(transform.position, Color.white, 1.4f);
                    combatVfx?.PlayImpact(transform.position, new Color(1f, 0.5f, 0.16f));
                }

                if (phaseTimer >= AnimatorTime(jumpLandingTime))
                    FinishAttack();
                break;
        }
    }

    private void FireJumpWaves()
    {
        if (jumpWaveSpeed <= 0f || jumpWaveDamage <= 0) return;

        Vector2 origin = new Vector2(transform.position.x, GroundRestY - 0.2f);
        BossProjectile.Spawn(origin, Vector2.left * jumpWaveSpeed, jumpWaveDamage,
            0f, jumpWaveLifetime, jumpWaveVisual, this);
        BossProjectile.Spawn(origin, Vector2.right * jumpWaveSpeed, jumpWaveDamage,
            0f, jumpWaveLifetime, jumpWaveVisual, this);
    }

    // --------------------------------------------------------- OBSIDIAN ATTACK
    private void BeginObsidian()
    {
        phase = 0;
        phaseTimer = 0f;
        obsidianHits.Clear();
        obsidianPositions.Clear();
        DestroyObsidianVisuals();

        Rect area = ResolveArea(obsidianSpawnArea, ArenaRect(0.6f), 0.6f);
        Vector2 player = (Vector2)ClampToArena(new Vector3(
            PlayerPosition.x, PlayerPosition.y, arenaZ));

        int count = Mathf.Max(1, obsidianPillarCount);
        for (int i = 0; i < count; i++)
        {
            Vector2 pos;
            if (obsidianAroundPlayer)
            {
                float angle = (i * 360f / count) * Mathf.Deg2Rad;
                pos = player + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.35f) * 1.6f;
                pos = (Vector2)ClampToArena(new Vector3(pos.x, GroundRestY, arenaZ));
            }
            else
            {
                // Hueco de escape como en Camazotz: dos posiciones consecutivas
                // se dejan libres para que la oleada sea legible.
                int gap = Mathf.Max(0, count / 3);
                if (i == gap || i == gap + 1) continue;
                float x = count <= 1 ? area.center.x
                                     : Mathf.Lerp(area.xMin, area.xMax, (i + 0.5f) / count);
                pos = new Vector2(x, GroundRestY);
            }

            CreateObsidianPillar(pos);
        }
    }

    private void CreateObsidianPillar(Vector2 position)
    {
        GameObject go;
        Vector3 fullScale;

        if (obsidianVisual.prefab != null)
        {
            go = Object.Instantiate(obsidianVisual.prefab,
                new Vector3(position.x, groundY, arenaZ), Quaternion.identity);
            go.name = "PilarObsidiana";
            fullScale = go.transform.localScale * Mathf.Max(0.01f, obsidianVisual.scale);
        }
        else
        {
            go = new GameObject("PilarObsidiana");
            go.transform.position = new Vector3(position.x, groundY, arenaZ);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = obsidianVisual.sprite != null ? obsidianVisual.sprite : BossFx.White;
            renderer.sortingOrder = obsidianVisual.sortingOrder;
            if (obsidianVisual.material != null) renderer.sharedMaterial = obsidianVisual.material;
            fullScale = Vector3.one * Mathf.Max(0.01f, obsidianVisual.scale);
        }

        float width = Mathf.Max(0.01f, obsidianPillarWidth);
        float tipHeight = Mathf.Max(0.02f, obsidianPillarHeight * 0.12f);
        go.transform.localScale = new Vector3(fullScale.x * width, fullScale.y * tipHeight, fullScale.z);
        go.transform.position = new Vector3(position.x, groundY + tipHeight * 0.5f, arenaZ);
        SetObsidianColor(go, obsidianWarningColor);

        obsidianVisuals.Add(go);
        obsidianFullScales.Add(fullScale);
        obsidianPositions.Add(position);
    }

    private static void SetObsidianColor(GameObject go, Color color)
    {
        if (go == null) return;
        SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++) renderers[i].color = color;
    }

    private void TickObsidianAttack()
    {
        float warn = AnimatorTime(obsidianWarnTime);
        float active = AnimatorTime(obsidianActiveTime);
        float total = Mathf.Max(AnimatorTime(obsidianDuration), warn + active + 0.1f);

        if (phase == 0 && stateTime >= warn)
        {
            ActivateObsidianPillars();
            phase = 1;
        }

        if (phase == 1 && stateTime >= warn + active)
        {
            for (int i = 0; i < obsidianVisuals.Count; i++)
                if (obsidianVisuals[i] != null) obsidianVisuals[i].SetActive(false);
            phase = 2;
        }

        if (stateTime >= total)
            FinishAttack();
    }

    private void ActivateObsidianPillars()
    {
        for (int i = 0; i < obsidianVisuals.Count; i++)
        {
            GameObject go = obsidianVisuals[i];
            if (go == null) continue;

            Vector3 full = obsidianFullScales[i];
            float width = Mathf.Max(0.01f, obsidianPillarWidth);
            float height = Mathf.Max(0.1f, obsidianPillarHeight);
            go.transform.localScale = new Vector3(full.x * width, full.y * height, full.z);
            go.transform.position = new Vector3(obsidianPositions[i].x, groundY + height * 0.5f, arenaZ);
            SetObsidianColor(go, obsidianVisual.color);

            DamageAt(obsidianPositions[i], obsidianHitRadius, obsidianDamage);
            combatVfx?.PlayImpact(new Vector3(obsidianPositions[i].x, groundY + 0.15f, arenaZ),
                obsidianVisual.color);
        }

        attackRateVfx?.PlayAttackPulse(transform.position, obsidianWarningColor, 1.2f);
    }

    private void DamageAt(Vector2 center, float radius, int damage)
    {
        if (isDead || !battleActive) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, Mathf.Abs(radius));
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable target = hits[i].GetComponentInParent<IDamageable>();
            if (target == null || target.DamageTeam != Team.Player) continue;
            if (obsidianHitOncePerWave && !obsidianHits.Add(target)) continue;
            target.TakeDamage(damage, center);
        }
    }

    private void DestroyObsidianVisuals()
    {
        for (int i = 0; i < obsidianVisuals.Count; i++)
            if (obsidianVisuals[i] != null) Destroy(obsidianVisuals[i]);
        obsidianVisuals.Clear();
        obsidianFullScales.Clear();
        obsidianPositions.Clear();
        obsidianHits.Clear();
    }

    // ------------------------------------------------------------------- AVISOS
    private void UpdateWarningLine(Vector3 from, Vector3 to)
    {
        if (warningLine == null)
        {
            warningLine = BossFx.CreateLine(from, to, warningColor, warningLineMaterial);
            warningLine.name = "AvisoXibalba";
            return;
        }

        warningLine.SetPosition(0, from);
        warningLine.SetPosition(1, to);
        warningLine.startColor = warningColor;
        warningLine.endColor = warningColor;
    }

    private void DestroyWarningLine()
    {
        BossFx.DestroyLine(warningLine);
        warningLine = null;
    }

    // ------------------------------------------------------------------ MOVIMIENTO
    private void StayOnGround()
    {
        SetFlying(false);
        Vector3 p = transform.position;
        float newY = Mathf.MoveTowards(p.y, GroundRestY, 12f * Time.deltaTime);
        transform.position = ClampToArena(new Vector3(p.x, newY, arenaZ));
    }

    protected override void SetFlying(bool value)
    {
        if (bodyCollider != null && bodyCollider.isTrigger != value)
            bodyCollider.isTrigger = value;
        base.SetFlying(value);
    }

    private void RefreshBodyMetrics()
    {
        if (bodyCollider == null)
        {
            bodyExtX = 0.6f;
            bodyExtY = 0.95f;
            bodyOffsetX = 0f;
            bodyOffsetY = 0.75f;
            return;
        }

        Vector3 scale = transform.lossyScale;
        bodyExtX = Mathf.Abs(bodyCollider.size.x * scale.x) * 0.5f;
        bodyExtY = Mathf.Abs(bodyCollider.size.y * scale.y) * 0.5f;
        bodyOffsetX = bodyCollider.offset.x * scale.x;
        bodyOffsetY = bodyCollider.offset.y * scale.y;
        if (bodyExtX < 0.01f) bodyExtX = 0.6f;
        if (bodyExtY < 0.01f) bodyExtY = 0.95f;
    }

    private void NormalizeArena()
    {
        if (arenaMinX > arenaMaxX)
        {
            float t = arenaMinX; arenaMinX = arenaMaxX; arenaMaxX = t;
        }
        if (groundY > ceilingY)
        {
            float t = groundY; groundY = ceilingY; ceilingY = t;
        }
        if (arenaHalfWidth <= 0f) arenaHalfWidth = 1f;
        if (arenaHeight <= 1f) arenaHeight = 1f;
        if (ceilingY - groundY < 2f) ceilingY = groundY + 2f;
    }

    protected override Vector3 ClampToArena(Vector3 position)
    {
        float minX = arenaMinX - bodyOffsetX + bodyExtX;
        float maxX = arenaMaxX - bodyOffsetX - bodyExtX;
        float minY = groundY - bodyOffsetY + bodyExtY;
        float maxY = ceilingY - bodyOffsetY - bodyExtY;

        if (minX > maxX)
        {
            float mid = (arenaMinX + arenaMaxX) * 0.5f;
            minX = mid; maxX = mid;
        }
        if (minY > maxY)
        {
            float mid = (groundY + ceilingY) * 0.5f;
            minY = mid; maxY = mid;
        }

        return new Vector3(Mathf.Clamp(position.x, minX, maxX),
                           Mathf.Clamp(position.y, minY, maxY),
                           arenaZ);
    }

    // ------------------------------------------------------------- ARENA RUNTIME
    private void CreateRuntimeArenaTrigger()
    {
        CreateTriggerStrip("BossArenaTrigger_Xibalba_Left", arenaMinX + entranceTriggerWidth * 0.5f);
        CreateTriggerStrip("BossArenaTrigger_Xibalba_Right", arenaMaxX - entranceTriggerWidth * 0.5f);
    }

    private void CreateTriggerStrip(string name, float x)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(x, (groundY + ceilingY) * 0.5f, arenaZ);

        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(entranceTriggerWidth, Mathf.Max(2f, ceilingY - groundY));

        var trigger = go.AddComponent<BossArenaTrigger>();
        var field = typeof(BossArenaTrigger).GetField("boss",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(trigger, this);
    }

    // ------------------------------------------------------------------- GIZMOS
    private void OnDrawGizmosSelected()
    {
        float minX = Mathf.Min(arenaMinX, arenaMaxX);
        float maxX = Mathf.Max(arenaMinX, arenaMaxX);
        float minY = Mathf.Min(groundY, ceilingY);
        float maxY = Mathf.Max(groundY, ceilingY);

        Gizmos.color = new Color(1f, 0.2f, 0.35f, 0.35f);
        Gizmos.DrawWireCube(new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, arenaZ),
                            new Vector3(maxX - minX, maxY - minY, 0.1f));

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
        Gizmos.DrawLine(new Vector3(minX, GroundRestY, arenaZ),
                        new Vector3(maxX, GroundRestY, arenaZ));

        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(new Vector3(arenaMinX + entranceTriggerWidth * 0.5f, (minY + maxY) * 0.5f, arenaZ),
                            new Vector3(entranceTriggerWidth, maxY - minY, 0.1f));
        Gizmos.DrawWireCube(new Vector3(arenaMaxX - entranceTriggerWidth * 0.5f, (minY + maxY) * 0.5f, arenaZ),
                            new Vector3(entranceTriggerWidth, maxY - minY, 0.1f));

        Rect jump = ResolveArea(jumpTargetArea, ArenaRect(0.4f), 0.4f);
        Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.9f);
        Gizmos.DrawWireCube(new Vector3(jump.center.x, jump.center.y, arenaZ),
                            new Vector3(jump.width, jump.height, 0.05f));

        Rect obsidian = ResolveArea(obsidianSpawnArea, ArenaRect(0.6f), 0.6f);
        Gizmos.color = new Color(0.75f, 0.35f, 1f, 0.9f);
        Gizmos.DrawWireCube(new Vector3(obsidian.center.x, obsidian.center.y, arenaZ),
                            new Vector3(obsidian.width, obsidian.height, 0.05f));

        if (Application.isPlaying && warningLine != null)
        {
            Gizmos.color = warningColor;
            Gizmos.DrawLine(warningLine.GetPosition(0), warningLine.GetPosition(1));
        }
    }
}
