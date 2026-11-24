using UnityEngine;
using System.Collections.Generic;

/// <summary>Espacio de coordenadas de un area configurable de ataque.</summary>
public enum BossAreaSpace
{
    /// <summary>El Rect esta en coordenadas del mundo.</summary>
    World,
    /// <summary>El Rect es relativo a la esquina inferior izquierda de la arena.</summary>
    Arena,
    /// <summary>El Rect es relativo a la posicion del jefe.</summary>
    Boss
}

/// <summary>
/// Area de spawn configurable. Si 'enabled' es false se usa el comportamiento
/// original de la arena, para no romper las referencias ya existentes.
/// </summary>
[System.Serializable]
public struct BossAttackArea
{
    public bool enabled;
    public BossAreaSpace space;
    public Rect rect;
    public Vector2 offset;
    [Tooltip("Recorta el area resultante a los limites de la arena.")]
    public bool clampToArena;
}

/// <summary>
/// Jefe 1: Camazotz. El ciclo activo es Idle -> (Walk opcional) -> ataque -> Idle.
/// Idle y Walk son vulnerables; cada ataque registra cooldown e i-frames desde
/// su entrada hasta el final de su secuencia. La Fase 2 comienza con SuperSonic.
///
/// Suelo: BasicAttack (golpe pesado + onda rasante), ComboAttack (tres impactos
/// + onda final), Scream y Spell. Aire: Dive (picada con telemetría) y Sweep
/// (barrido a la altura del jugador), ambos usando la animación Fly.
/// </summary>
public class CamazotzBoss : BossController
{
    // ---------------------------------------------------------------- ARENA
    [Header("Arena (ajusta a Battle-01)")]
    [SerializeField] private float arenaMinX = -61f;
    [SerializeField] private float arenaMaxX = -19f;
    [SerializeField] private float groundY = -99f;
    [SerializeField] private float ceilingY = -84.5f;
    [SerializeField] private float hoverY = -92f;

    [Header("Zona de combate")]
    [Tooltip("Si no hay un BossArenaTrigger en la escena, se crea uno en runtime.")]
    [SerializeField] private bool createRuntimeArenaTrigger = true;
    [Tooltip("Ancho de cada franja de entrada de la arena.")]
    [SerializeField] private float entranceTriggerWidth = 4f;

    // ---------------------------------------------------------------- CICLO FSM
    [Header("Idle y reposicionamiento")]
    [Tooltip("Tiempo minimo de decision en Idle antes de escoger el siguiente ataque.")]
    [SerializeField] private float idleDecisionTime = 1.25f;
    [Tooltip("Cooldown comun entre inicios de ataques.")]
    [SerializeField, Min(0f)] private float attackCooldown = 0.65f;
    [Tooltip("Multiplicador de cooldown en fase 2. Menor valor = mayor frecuencia.")]
    [SerializeField, Range(0.35f, 1f)] private float phase2AttackCooldownMultiplier = 0.65f;
    [Tooltip("Multiplicador del tiempo de decision en Idle durante fase 2.")]
    [SerializeField, Range(0.35f, 1f)] private float phase2DecisionTimeMultiplier = 0.65f;

    // ---------------------------------------------------------------- MOVIMIENTO
    [Header("Movimiento en suelo / aire")]
    [SerializeField] private float returnSpeed = 7f;
    [Tooltip("Altura del barrido aereo sobre el suelo cuando el jugador esta abajo.")]
    [SerializeField] private float sweepHeightAboveGround = 1.6f;
    [SerializeField] private float diveSpeed = 16f;
    [SerializeField] private float sweepSpeed = 9f;

    [Header("Collider (suelo / vuelo)")]
    [Tooltip("En vuelo la capsula se desactiva: el dano lo dan los hitboxes y proyectiles.")]
    [SerializeField] private CapsuleCollider2D bodyCollider;
    [SerializeField] private Vector2 flyingColliderSize = new Vector2(1.9f, 1f);
    [SerializeField] private Vector2 flyingColliderOffset = new Vector2(0f, 0.15f);

    private Vector2 groundedColliderSize = new Vector2(1.2f, 1.9f);
    private Vector2 groundedColliderOffset = new Vector2(0.08f, 0.75f);

    // ---------------------------------------------------------------- ATAQUES
    [Header("BasicAttack (1 golpe)")]
    [SerializeField] private float basicWindup = 0.22f;
    [SerializeField] private float basicDuration = 0.55f;
    [SerializeField] private float basicHitRadius = 1.5f;
    [SerializeField] private int basicDamage = 1;
    [Tooltip("Distancia horizontal de la embestida durante la anticipacion del golpe basico.")]
    [SerializeField, Min(0f)] private float basicLungeDistance = 1.15f;
    [SerializeField, Min(0.1f)] private float basicLungeSpeed = 7.5f;

    [Header("Impacto de suelo (estilo False Knight)")]
    [Tooltip("Lanza dos ondas rasantes al golpear el piso. Usa sprite/material si se asigna.")]
    [SerializeField] private BossAttackVisual groundShockwaveVisual = new BossAttackVisual
    {
        color = new Color(1f, 0.52f, 0.16f, 1f), radius = 0.18f, sortingOrder = 20
    };
    [SerializeField, Min(0f)] private float groundShockwaveSpeed = 5.5f;
    [SerializeField, Min(0.1f)] private float groundShockwaveLifetime = 1.4f;
    [SerializeField, Min(0)] private int groundShockwaveDamage = 1;

    [Header("ComboAttack (3 golpes)")]
    [SerializeField] private float comboDuration = 1.25f;
    [Tooltip("Instantes de los 3 impactos dentro de la animacion (segundos).")]
    [SerializeField] private Vector3 comboHitTimes = new Vector3(0.28f, 0.62f, 0.96f);
    [SerializeField] private float comboHitRadius = 1.6f;
    [SerializeField] private int comboDamage = 1;
    [Tooltip("Distancia horizontal del desplazamiento durante la anticipacion del combo.")]
    [SerializeField, Min(0f)] private float comboLungeDistance = 0.8f;
    [SerializeField, Min(0.1f)] private float comboLungeSpeed = 6.5f;

    [Header("Scream (suelo)")]
    [SerializeField] private int sonicWaves = 5;
    [SerializeField] private float sonicSpeed = 6.5f;
    [SerializeField] private float screamWindup = 0.7f;
    [SerializeField] private float screamDuration = 1.5f;
    [Tooltip("Velocidad con la que el jefe busca el centro antes de un grito.")]
    [SerializeField, Min(0.1f)] private float screamCenterSpeed = 7f;
    [Tooltip("Zona desde la que salen las ondas. Desactivada = origen sobre el jefe.")]
    [SerializeField] private BossAttackArea screamSpawnArea = new BossAttackArea
    {
        enabled = false, space = BossAreaSpace.Arena, rect = new Rect(0f, 0f, 10f, 10f),
        offset = Vector2.zero, clampToArena = true
    };
    [Tooltip("Avance del origen hacia el jugador dentro del area.")]
    [SerializeField] private float sonicOriginForwardOffset = 0.6f;

    [Header("Spell (suelo)")]
    [SerializeField] private int fangCount = 6;
    [SerializeField] private float fangGravity = 2.6f;
    [SerializeField] private float spellWindup = 0.6f;
    [SerializeField] private float spellSpawnInterval = 0.09f;
    [SerializeField] private float spellDuration = 1.9f;
    [Tooltip("Zona de caida de los colmillos. Desactivada = todo el ancho de la arena.")]
    [SerializeField] private BossAttackArea spellSpawnArea = new BossAttackArea
    {
        enabled = false, space = BossAreaSpace.Arena, rect = new Rect(0f, 0f, 10f, 10f),
        offset = Vector2.zero, clampToArena = true
    };
    [Tooltip("Margen a los bordes del area de spawn.")]
    [SerializeField] private float fangEdgeInset = 0.6f;

    [Header("Scream: origen en la cabeza")]
    [Tooltip("Punto exacto de emision. Si queda vacio, se calcula automaticamente en la parte alta del modelo.")]
    [SerializeField] private Transform sonicOriginPoint;
    [Tooltip("0 = centro de la cabeza estimada, 1 = borde superior de los renderers.")]
    [SerializeField, Range(0f, 1f)] private float automaticSonicHeadInset = 0.82f;
    [SerializeField] private float sonicHeadHeightOffset;

    [Header("Dive / Sweep (aire, animacion Fly)")]
    [SerializeField] private float diveRiseTime = 0.5f;
    [SerializeField] private float diveWarnTime = 0.55f;
    [SerializeField] private float diveStaggerTime = 1f;
    [SerializeField] private float sweepApproachSpeed = 14f;
    [SerializeField] private float sweepWarnTime = 0.28f;
    [SerializeField] private float diveHitRadius = 1.25f;
    [SerializeField] private float sweepHitRadius = 0.95f;
    [SerializeField] private int aerialDamage = 1;
    [Tooltip("Tiempo vulnerable y aturdido al terminar Sweep.")]
    [SerializeField, Min(0.1f)] private float sweepStunTime = 0.85f;
    [SerializeField] private Color sweepStunnedColor = new Color(0.4f, 0.85f, 1f, 1f);

    [Header("Dive: inclinacion visual (Fly)")]
    [Tooltip("Maxima inclinacion del modelo al alinearlo con la picada diagonal.")]
    [SerializeField, Range(0f, 85f)] private float maxDiveTilt = 60f;
    [Tooltip("Velocidad de giro hacia el angulo de la trayectoria (grados/segundo).")]
    [SerializeField] private float diveTiltSpeed = 420f;
    [Tooltip("Velocidad de vuelta al angulo neutral (grados/segundo).")]
    [SerializeField] private float tiltRecoverySpeed = 240f;

    [Header("Cooldown de ataques")]
    [Tooltip("Segundos extra antes de poder repetir Scream/Spell/Dive/Sweep. Se suman al cooldown comun.")]
    [SerializeField, Min(0f)] private float specialAttackCooldown = 0f;

    [Header("Fase 2: super grito sonico")]
    [Tooltip("Duracion de la secuencia inicial invulnerable de fase 2.")]
    [SerializeField, Range(3f, 5f)] private float phase2SuperSonicDuration = 4f;
    [Tooltip("Intervalo entre rafagas del super grito.")]
    [SerializeField, Min(0.08f)] private float phase2SuperSonicInterval = 0.32f;
    [SerializeField, Min(3)] private int phase2SuperSonicWaves = 8;
    [SerializeField, Range(90f, 360f)] private float phase2SuperSonicSpread = 220f;
    [Tooltip("Velocidad de reposicionamiento al centro antes del super grito.")]
    [SerializeField, Min(0.1f)] private float phase2CenterSpeed = 8f;

    // ---------------------------------------------------------------- VISUALES
    [Header("Visuales: ondas sonicas")]
    [SerializeField] private BossAttackVisual sonicWaveVisual = new BossAttackVisual
    {
        color = new Color(0.62f, 0.22f, 0.95f, 1f), radius = 0.24f, sortingOrder = 20
    };

    [Header("Visuales: colmillos")]
    [SerializeField] private BossAttackVisual fangVisual = new BossAttackVisual
    {
        color = new Color(0.18f, 0.12f, 0.22f, 1f), radius = 0.19f,
        rotateToVelocity = true, sortingOrder = 20
    };

    [Header("Visuales: avisos")]
    [SerializeField] private Color warningColor = new Color(1f, 0.32f, 0.2f, 1f);
    [SerializeField] private Material warningLineMaterial;

    [Header("Visuales: Sweep (telegraph + area activa)")]
    [SerializeField] private Color sweepTelegraphColor = new Color(1f, 0.55f, 0.15f, 0.9f);
    [SerializeField] private float sweepTelegraphWidth = 0.22f;
    [SerializeField] private Material sweepTelegraphMaterial;
    [Tooltip("Color del circulo que marca el area que esta haciendo daño.")]
    [SerializeField] private Color sweepActiveColor = new Color(1f, 0.15f, 0.25f, 0.55f);
    [SerializeField] private Sprite sweepActiveSprite;
    [SerializeField] private Material sweepActiveMaterial;
    [SerializeField] private int sweepActiveSortingOrder = 21;

    // ---------------------------------------------------------------- ESTADO INTERNO
    private float bodyExtX = 0.6f;
    private float bodyExtY = 0.95f;
    private float bodyOffsetX;
    private float bodyOffsetY;
    private float arenaZ;

    private float groundedExtY = 0.95f;
    private float groundedOffsetY;
    private float GroundRestY => groundY - groundedOffsetY + groundedExtY;

    private bool flying;
    private bool attackHitDone;
    private bool groundShockwaveFired;
    private float attackLungeTargetX;
    private float attackLungeSpeed;
    private bool attackLungeActive;
    private bool diveMissed;
    private int comboHitIndex;
    private int phase;
    private float phaseTimer;
    private int fangsSpawned;
    private LineRenderer warningLine;
    private LineRenderer sweepTelegraph;
    private GameObject sweepActiveVisual;
    private SpriteRenderer sweepActiveRenderer;
    private BossCombatVfx combatVfx;
    private AttackRateVfx attackRateVfx;
    private Renderer[] sonicRenderers;
    private Rect lastSpellArea;
    private Rect lastScreamArea;

    private Vector2 diveTarget;
    private Vector3 diveHighPoint;
    private Vector3 sweepStart;
    private Vector3 sweepEnd;
    private float sweepTravelTime;

    private BossState pendingAttack;
    private float phase2SuperSonicTime;
    private float nextPhase2SuperSonicTime;
    private bool phase2SuperSonicStarted;
    private readonly Dictionary<BossState, float> attackReadyAt = new Dictionary<BossState, float>();
    private static readonly BossState[] AttackOptions =
    {
        BossState.BasicAttack, BossState.ComboAttack,
        BossState.Scream, BossState.Spell, BossState.Dive, BossState.Sweep
    };

    // ---------------------------------------------------------------- CICLO DE VIDA
    protected override void Awake()
    {
        base.Awake();
        combatVfx = GetComponent<BossCombatVfx>();
        if (combatVfx == null) combatVfx = gameObject.AddComponent<BossCombatVfx>();
        attackRateVfx = GetComponent<AttackRateVfx>();
        if (attackRateVfx == null) attackRateVfx = gameObject.AddComponent<AttackRateVfx>();
        sonicRenderers = modelRoot != null
            ? modelRoot.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];

        arenaZ = Mathf.Abs(transform.position.z) < 2f ? transform.position.z : 0f;
        Vector3 spawn = transform.position;
        spawn.z = arenaZ;
        transform.position = spawn;

        if (bodyCollider == null) bodyCollider = GetComponent<CapsuleCollider2D>();
        if (bodyCollider != null)
        {
            groundedColliderSize = bodyCollider.size;
            groundedColliderOffset = bodyCollider.offset;
            // En vuelo el collider se vuelve trigger para no chocar con el
            // escenario, pero sigue detectando los golpes del jugador. Ignora
            // los pinchos para que eso no le inflija daño accidental.
            IgnoreHazardCollisions();
        }

        Vector3 scale = transform.lossyScale;
        groundedExtY = Mathf.Abs(groundedColliderSize.y * scale.y) * 0.5f;
        groundedOffsetY = groundedColliderOffset.y * scale.y;
        if (groundedExtY < 0.01f) groundedExtY = 0.95f;

        RefreshBodyMetrics();
        NormalizeArena();

        Vector3 grounded = ClampToArena(new Vector3(transform.position.x, GroundRestY, arenaZ));
        transform.position = grounded;

        if (createRuntimeArenaTrigger && Object.FindAnyObjectByType<BossArenaTrigger>() == null)
            CreateRuntimeArenaTrigger();
    }

    protected override void Start()
    {
        base.Start();

        // Si el jugador ya empieza dentro del arena, activamos por distancia.
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
        if (comboHitTimes.x < 0f) comboHitTimes.x = 0.2f;
    }

    public override void ActivateBattle()
    {
        if (BattleActive) return;
        base.ActivateBattle();
        if (!BattleActive) return;
        FaceTarget();
    }

    /// <summary>
    /// Golpes del jugador nunca cambian de estado: no aturden ni cortan clips.
    /// La invulnerabilidad solo la gestiona la ejecución de un ataque.
    /// </summary>
    protected override void OnDamaged() { }

    protected override void OnPhaseTwoTriggered()
    {
        if (!isDead) ChangeState(BossState.PhaseTransition);
    }

    /// <summary>Limpia todo rastro del encuentro anterior, incluidos cooldowns.</summary>
    protected override void OnCombatReset()
    {
        BossProjectile.ClearFrom(this);
        pendingAttack = default;
        phase2SuperSonicTime = 0f;
        nextPhase2SuperSonicTime = 0f;
        phase2SuperSonicStarted = false;
        attackReadyAt.Clear();
        diveMissed = false;
        attackHitDone = false;
        groundShockwaveFired = false;
        attackLungeActive = false;
        comboHitIndex = 0;
        fangsSpawned = 0;
        lastSpellArea = default;
        lastScreamArea = default;
        SetVisualTilt(0f);
        ResetFacingLock();
        DestroyWarningLine();
        DestroySweepVisuals();
    }

    // ---------------------------------------------------------------- ZONA RUNTIME
    private void CreateRuntimeArenaTrigger()
    {
        CreateTriggerStrip("BossArenaTrigger_Left", arenaMinX + entranceTriggerWidth * 0.5f);
        CreateTriggerStrip("BossArenaTrigger_Right", arenaMaxX - entranceTriggerWidth * 0.5f);
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

    // ---------------------------------------------------------------- TRANSICIONES
    protected override void OnEnterState(BossState state)
    {
        phase = 0;
        phaseTimer = 0f;
        attackHitDone = false;
        groundShockwaveFired = false;
        diveMissed = false;
        comboHitIndex = 0;
        fangsSpawned = 0;

        switch (state)
        {
            case BossState.Idle:
                // Red de seguridad: Idle y Walk nunca pueden heredar i-frames
                // de un ataque anterior ni del super grito de fase 2.
                BreakInvulnerability();
                SetFlying(false);
                SetBool(AnimWalking, false);
                ResetFacingLock();
                FaceTarget();
                // Libera cualquier estado visual anterior, incluido
                // SuperSonic, despues de que ChangeState limpie triggers.
                Trigger(AnimIdleReset);
                break;

            case BossState.Walk:
                BreakInvulnerability();
                SetFlying(false);
                SetBool(AnimWalking, true);
                ResetFacingLock();
                break;

            case BossState.PhaseTransition:
                // Secuencia inicial de fase 2: ataque completo e invulnerable.
                ClearResidualAttacks();
                BeginAttack();
                attackReadyAt.Clear(); // la fase 2 empieza un ciclo nuevo
                SetFlying(false);
                SetBool(AnimWalking, false);
                phase2SuperSonicTime = 0f;
                nextPhase2SuperSonicTime = 0f;
                phase2SuperSonicStarted = false;
                SetModelTint(warningColor);
                break;

            case BossState.BasicAttack:
                BeginAttack();
                RegisterAttackCooldown(state);
                BeginAttackLunge(basicLungeDistance, basicLungeSpeed);
                Trigger(AnimBasicAttack);
                attackRateVfx?.PlayAttackPulse(transform.position, new Color(1f, 0.36f, 0.08f), 1f);
                combatVfx?.PlayCharge(transform.position, new Color(1f, 0.48f, 0.12f));
                SetModelTint(warningColor);
                break;

            case BossState.ComboAttack:
                BeginAttack();
                RegisterAttackCooldown(state);
                BeginAttackLunge(comboLungeDistance, comboLungeSpeed);
                Trigger(AnimComboAttack);
                attackRateVfx?.PlayAttackPulse(transform.position, new Color(1f, 0.36f, 0.08f), 1.15f);
                combatVfx?.PlayCharge(transform.position, new Color(1f, 0.48f, 0.12f));
                SetModelTint(warningColor);
                break;

            case BossState.Scream:
                BeginAttack();
                RegisterAttackCooldown(state);
                Trigger(AnimSonic);
                combatVfx?.PlayCharge(transform.position, new Color(0.7f, 0.3f, 1f));
                SetModelTint(warningColor);
                break;

            case BossState.Spell:
                BeginAttack();
                RegisterAttackCooldown(state);
                Trigger(AnimFangs);
                combatVfx?.PlayCharge(transform.position, new Color(1f, 0.35f, 0.12f));
                SetModelTint(warningColor);
                // Se congela al entrar: todos los colmillos respetan el mismo rect.
                SpellArea();
                break;

            case BossState.Dive:
                BeginAttack();
                RegisterAttackCooldown(state);
                Trigger(AnimDive);
                BeginDive();
                break;

            case BossState.Sweep:
                BeginAttack();
                RegisterAttackCooldown(state);
                Trigger(AnimSweep);
                BeginSweep();
                break;
        }
    }

    protected override void OnExitState(BossState state)
    {
        SetBool(AnimWalking, false);
        ClearModelTint();
        DestroyWarningLine();
        DestroySweepVisuals();

        // El ataque termina aqui: se apagan los i-frames antes de entrar en
        // Idle, donde el jugador ya puede castigar.
        if (IsAttackState(state) || state == BossState.PhaseTransition)
            EndAttack();
    }

    protected override bool IsAttackState(BossState state)
    {
        return state == BossState.BasicAttack || state == BossState.ComboAttack ||
               state == BossState.Scream || state == BossState.Spell ||
               state == BossState.Dive || state == BossState.Sweep;
    }

    /// <summary>Patrones con cooldown extra además del cooldown comun.</summary>
    private static bool IsSpecialAttack(BossState state)
    {
        return state == BossState.Scream || state == BossState.Spell ||
               state == BossState.Dive || state == BossState.Sweep;
    }

    /// <summary>Corta avisos, golpes y proyectiles antes de una transición de fase.</summary>
    private void ClearResidualAttacks()
    {
        ResetMeleeWindow();
        EndAttack();
        DestroyWarningLine();
        DestroySweepVisuals();
        ClearModelTint();
        BossProjectile.ClearFrom(this);
    }

    private void DestroyWarningLine()
    {
        BossFx.DestroyLine(warningLine);
        warningLine = null;
    }

    /// <summary>Telegraph del recorrido completo; se crea al empezar el Sweep.</summary>
    private void CreateSweepTelegraph()
    {
        DestroySweepVisuals();
        sweepTelegraph = BossFx.CreateLine(sweepStart, sweepEnd, sweepTelegraphColor, sweepTelegraphMaterial);
        if (sweepTelegraph == null) return;

        sweepTelegraph.name = "AvisoSweep";
        sweepTelegraph.startWidth = Mathf.Max(0.01f, sweepTelegraphWidth);
        sweepTelegraph.endWidth = Mathf.Max(0.01f, sweepTelegraphWidth);
    }

    /// <summary>Circulo que acompana al hitbox real mientras el Sweep hace daño.</summary>
    private void UpdateSweepActiveVisual(bool active)
    {
        if (!active)
        {
            if (sweepActiveVisual != null) sweepActiveVisual.SetActive(false);
            return;
        }

        if (sweepActiveVisual == null)
        {
            sweepActiveVisual = new GameObject("SweepAreaActiva");
            sweepActiveRenderer = sweepActiveVisual.AddComponent<SpriteRenderer>();
            sweepActiveRenderer.sprite = sweepActiveSprite != null ? sweepActiveSprite : BossFx.Circle;
            sweepActiveRenderer.color = sweepActiveColor;
            sweepActiveRenderer.sortingOrder = sweepActiveSortingOrder;
            if (sweepActiveMaterial != null) sweepActiveRenderer.sharedMaterial = sweepActiveMaterial;
        }

        sweepActiveVisual.SetActive(true);
        Vector3 p = transform.position;
        sweepActiveVisual.transform.position = new Vector3(p.x, p.y, arenaZ);

        // BossFx.Circle mide 1x1; con un sprite asignado se respeta su tamano real.
        Sprite sprite = sweepActiveRenderer.sprite;
        float baseSize = sprite != null
            ? Mathf.Max(0.001f, Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y))
            : 1f;
        float scale = Mathf.Max(0.01f, sweepHitRadius * 2f) / baseSize;
        sweepActiveVisual.transform.localScale = new Vector3(scale, scale, 1f);
        sweepActiveRenderer.color = sweepActiveColor;
    }

    private void DestroySweepVisuals()
    {
        BossFx.DestroyLine(sweepTelegraph);
        sweepTelegraph = null;
        if (sweepActiveVisual != null) Destroy(sweepActiveVisual);
        sweepActiveVisual = null;
        sweepActiveRenderer = null;
    }

    // ---------------------------------------------------------------- TICK DE LA FSM
    protected override void TickAI()
    {
        if (Enraged && !speedBoosted)
        {
            speedBoosted = true;
            SetSpeed(1.2f);
        }

        switch (currentState)
        {
            case BossState.Idle:
                TickIdle();
                break;
            case BossState.Walk:
                TickWalk();
                break;
            case BossState.PhaseTransition:
                TickPhaseTransition();
                break;
            case BossState.BasicAttack:
                TickBasicAttack();
                break;
            case BossState.ComboAttack:
                TickComboAttack();
                break;
            case BossState.Scream:
                TickScream();
                break;
            case BossState.Spell:
                TickSpell();
                break;
            case BossState.Dive:
                TickDive();
                break;
            case BossState.Sweep:
                TickSweep();
                break;
        }
    }

    private void TickIdle()
    {
        if (player == null || player.IsDead) return;
        StayOnGround();
        FaceTarget();

        float decisionTime = idleDecisionTime * (Enraged ? phase2DecisionTimeMultiplier : 1f);
        if (stateTime < decisionTime) return;

        if (!TryPickNextAttack(out pendingAttack))
            return;

        ChangeState(pendingAttack);
    }

    private void TickWalk()
    {
        // Compatibilidad con escenas antiguas: el jefe ya no acecha ni recorre
        // la arena. Los ataques cuerpo a cuerpo hacen su propio desplazamiento.
        ChangeState(BossState.Idle);
    }

    private void BeginAttackLunge(float distance, float speed)
    {
        attackLungeActive = distance > 0.01f;
        attackLungeSpeed = speed;
        float direction = Mathf.Sign(PlayerPosition.x - transform.position.x);
        if (Mathf.Abs(direction) < 0.01f) direction = FacingSign;
        attackLungeTargetX = ClampToArena(new Vector3(
            transform.position.x + direction * distance, GroundRestY, arenaZ)).x;
    }

    private void TickAttackLunge(float windup)
    {
        if (!attackLungeActive || stateTime >= AnimatorTime(windup)) return;
        float nextX = Mathf.MoveTowards(transform.position.x, attackLungeTargetX,
            attackLungeSpeed * Time.deltaTime);
        transform.position = ClampToArena(new Vector3(nextX, GroundRestY, arenaZ));
        if (Mathf.Abs(nextX - attackLungeTargetX) <= 0.01f)
            attackLungeActive = false;
    }

    private void TickPhaseTransition()
    {
        StayOnGround();

        if (!MoveToArenaCenter(phase2CenterSpeed))
            return;

        if (!phase2SuperSonicStarted)
        {
            phase2SuperSonicStarted = true;
            phase2SuperSonicTime = 0f;
            nextPhase2SuperSonicTime = 0f;
            Trigger(AnimSuperSonic);
            RestartAttackAnimation(AnimSuperSonic);
            combatVfx?.PlayCharge(transform.position, new Color(0.7f, 0.3f, 1f));
        }

        phase2SuperSonicTime += Time.deltaTime;

        if (phase2SuperSonicTime < phase2SuperSonicDuration &&
            phase2SuperSonicTime >= nextPhase2SuperSonicTime)
        {
            // Cada rafaga reinicia el clip: la animacion acompaña a las ondas
            // durante los 3-5 s completos en vez de congelarse en el ultimo frame.
            RestartAttackAnimation(AnimSuperSonic);
            FireSonicWaves(phase2SuperSonicWaves, phase2SuperSonicSpread, false);
            nextPhase2SuperSonicTime = phase2SuperSonicTime + phase2SuperSonicInterval;
        }

        if (phase2SuperSonicTime >= phase2SuperSonicDuration)
            ChangeState(BossState.Idle);
    }

    private bool TryPickNextAttack(out BossState selectedAttack)
    {
        int readyCount = 0;
        for (int i = 0; i < AttackOptions.Length; i++)
            if (IsAttackReady(AttackOptions[i])) readyCount++;

        if (readyCount == 0)
        {
            // Si todo esta en cooldown, el jefe espera en Idle y vuelve a
            // decidir en el siguiente tick de decision; nunca fuerza un ataque.
            selectedAttack = default;
            return false;
        }

        int selected = Random.Range(0, readyCount);
        for (int i = 0; i < AttackOptions.Length; i++)
        {
            if (!IsAttackReady(AttackOptions[i])) continue;
            if (selected-- == 0)
            {
                selectedAttack = AttackOptions[i];
                return true;
            }
        }

        selectedAttack = default;
        return false;
    }

    private bool IsAttackReady(BossState attack)
    {
        return !attackReadyAt.TryGetValue(attack, out float readyAt) || Time.time >= readyAt;
    }

    private void RegisterAttackCooldown(BossState attack)
    {
        float multiplier = Enraged ? phase2AttackCooldownMultiplier : 1f;
        float cooldown = attackCooldown * multiplier;
        if (IsSpecialAttack(attack)) cooldown += specialAttackCooldown * multiplier;
        attackReadyAt[attack] = Time.time + cooldown;
    }

    private static bool RequiresMeleePosition(BossState attack)
    {
        return attack == BossState.BasicAttack || attack == BossState.ComboAttack;
    }

    // ------------------------------------------------------------ BASIC ATTACK
    private void TickBasicAttack()
    {
        TickAttackLunge(basicWindup);
        if (!attackHitDone && stateTime >= AnimatorTime(basicWindup))
        {
            attackHitDone = true;
            MeleeLockFacingOnHit();
            DoMeleeHit(basicHitRadius, basicDamage);
            FireGroundShockwave();
            attackRateVfx?.PlayAttackPulse(transform.position, Color.white, 1.35f);
            combatVfx?.PlayImpact(transform.position, new Color(1f, 0.72f, 0.32f));
        }

        if (stateTime >= AnimatorTime(basicDuration)) ChangeState(BossState.Idle);
    }

    // ------------------------------------------------------------ COMBO ATTACK
    private void TickComboAttack()
    {
        TickAttackLunge(comboHitTimes.x);
        float[] times = {
            AnimatorTime(comboHitTimes.x),
            AnimatorTime(comboHitTimes.y),
            AnimatorTime(comboHitTimes.z)
        };
        if (comboHitIndex < times.Length && stateTime >= times[comboHitIndex])
        {
            // Cada golpe tiene su propia ventana: el mismo objetivo puede
            // recibir los 3, pero no dos veces dentro de un mismo golpe.
            ResetMeleeWindow();
            FaceTarget();
            MeleeLockFacingOnHit();
            DoMeleeHit(comboHitRadius, comboDamage);
            if (comboHitIndex == times.Length - 1) FireGroundShockwave();
            attackRateVfx?.PlayAttackPulse(transform.position, Color.white, 1.2f);
            combatVfx?.PlayImpact(transform.position, new Color(1f, 0.72f, 0.32f));
            comboHitIndex++;
        }

        if (stateTime >= AnimatorTime(comboDuration)) ChangeState(BossState.Idle);
    }

    /// <summary>
    /// Impacto pesado inspirado en False Knight: además del melee, crea dos
    /// amenazas bajas que recorren el suelo y se limpian con el owner del jefe.
    /// </summary>
    private void FireGroundShockwave()
    {
        if (groundShockwaveFired || groundShockwaveSpeed <= 0f || groundShockwaveDamage <= 0)
            return;

        groundShockwaveFired = true;
        Vector2 origin = new Vector2(transform.position.x, GroundRestY - 0.2f);
        BossProjectile.Spawn(origin, Vector2.left * groundShockwaveSpeed,
            groundShockwaveDamage, 0f, groundShockwaveLifetime, groundShockwaveVisual, this);
        BossProjectile.Spawn(origin, Vector2.right * groundShockwaveSpeed,
            groundShockwaveDamage, 0f, groundShockwaveLifetime, groundShockwaveVisual, this);
    }

    // ------------------------------------------------------------ SCREAM
    private void TickScream()
    {
        if (!MoveToArenaCenter(screamCenterSpeed))
            return;

        if (!attackHitDone && stateTime >= AnimatorTime(screamWindup))
        {
            attackHitDone = true;
            ClearModelTint();
            FireSonicWaves(Enraged ? sonicWaves + 2 : sonicWaves,
                           Enraged ? 165f : 125f, Enraged);
            attackRateVfx?.PlayAttackPulse(GetSonicOrigin(), Color.white, 1.25f);
            combatVfx?.PlayImpact(transform.position, new Color(0.72f, 0.3f, 1f));
        }

        if (stateTime >= AnimatorTime(screamDuration)) ChangeState(BossState.Idle);
    }

    // ------------------------------------------------------------ AREAS CONFIGURABLES
    /// <summary>Convierte un area del inspector a coordenadas del mundo.</summary>
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
            float pad = Mathf.Abs(inset);
            float minX = Mathf.Min(arenaMinX, arenaMaxX) + pad;
            float maxX = Mathf.Max(arenaMinX, arenaMaxX) - pad;
            float minY = Mathf.Min(groundY, ceilingY) + pad;
            float maxY = Mathf.Max(groundY, ceilingY) - pad;
            if (minX > maxX) minX = maxX = (arenaMinX + arenaMaxX) * 0.5f;
            if (minY > maxY) minY = maxY = (groundY + ceilingY) * 0.5f;

            result.xMin = Mathf.Clamp(result.xMin, minX, maxX);
            result.xMax = Mathf.Clamp(result.xMax, minX, maxX);
            result.yMin = Mathf.Clamp(result.yMin, minY, maxY);
            result.yMax = Mathf.Clamp(result.yMax, minY, maxY);
        }

        // Un rect degenerado (mal calibrado en el Inspector) no debe dejar el
        // ataque sin area: se cae al comportamiento original.
        if (result.width <= 0.02f || result.height <= 0.02f) return fallback;
        return result;
    }

    /// <summary>Area por defecto de los colmillos: toda la arena con margen.</summary>
    private Rect SpellFallbackArea()
    {
        return Rect.MinMaxRect(
            Mathf.Min(arenaMinX, arenaMaxX) + fangEdgeInset,
            Mathf.Min(groundY, ceilingY) + fangEdgeInset,
            Mathf.Max(arenaMinX, arenaMaxX) - fangEdgeInset,
            Mathf.Max(groundY, ceilingY) - fangEdgeInset);
    }

    /// <summary>Area por defecto del grito: un punto en la cabeza del jefe.</summary>
    private Rect ScreamFallbackArea()
    {
        Vector2 p = GetSonicOrigin();
        return Rect.MinMaxRect(p.x - 0.05f, p.y - 0.05f, p.x + 0.05f, p.y + 0.05f);
    }

    /// <summary>
    /// Usa un socket asignable cuando existe. Sin socket, estima la cabeza a
    /// partir de los bounds visuales para que el prefab actual ya emita desde
    /// arriba sin exigir editar la jerarquia del modelo importado.
    /// </summary>
    private Vector2 GetSonicOrigin()
    {
        if (sonicOriginPoint != null) return sonicOriginPoint.position;

        // OnDrawGizmosSelected puede llamar este metodo en modo edicion,
        // antes de Awake. Inicializa la cache de forma segura en ese caso.
        if (sonicRenderers == null)
        {
            sonicRenderers = modelRoot != null
                ? modelRoot.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
        }

        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < sonicRenderers.Length; i++)
        {
            Renderer renderer = sonicRenderers[i];
            if (renderer == null || !renderer.enabled) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
            return transform.position + Vector3.up * Mathf.Max(1f, bodyExtY * 1.5f);

        float headY = Mathf.Lerp(bounds.center.y, bounds.max.y, automaticSonicHeadInset);
        return new Vector2(bounds.center.x, headY + sonicHeadHeightOffset);
    }

    /// <summary>Area de caida de colmillos (configurable o la arena completa).</summary>
    private Rect SpellArea()
    {
        lastSpellArea = ResolveArea(spellSpawnArea, SpellFallbackArea(), fangEdgeInset);
        return lastSpellArea;
    }

    /// <summary>Area de emision del grito: por defecto un punto sobre el jefe.</summary>
    private Rect ScreamArea()
    {
        lastScreamArea = ResolveArea(screamSpawnArea, ScreamFallbackArea(), 0f);
        return lastScreamArea;
    }

    private void FireSonicWaves(int count, float spread, bool addEnragedRing)
    {
        StayOnGround();

        // El origen se toma del area configurada: se acerca al jugador dentro
        // del rectangulo y se avanza un poco hacia el para no spawnear "dentro"
        // del area. Con el area por defecto esto equivale al comportamiento anterior.
        Rect area = ScreamArea();
        Vector2 origin = new Vector2(Mathf.Clamp(PlayerPosition.x, area.xMin, area.xMax),
                                     Mathf.Clamp(PlayerPosition.y, area.yMin, area.yMax));
        Vector2 towardPlayer = PlayerPosition - origin;
        if (towardPlayer.sqrMagnitude < 0.01f) towardPlayer = new Vector2(FacingSign, 0f);
        towardPlayer.Normalize();
        origin += towardPlayer * sonicOriginForwardOffset;

        // Las ondas se abren en abanico alrededor de la direccion real jugador.
        Vector2 baseDirection = towardPlayer;
        float baseAngle = Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg;

        count = Mathf.Max(2, count);
        int gap = Random.Range(0, count);

        for (int i = 0; i < count; i++)
        {
            if (i == gap) continue;
            float angle = (baseAngle - spread * 0.5f + spread * i / (count - 1f)) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
            BossProjectile.Spawn(origin, direction * sonicSpeed, 1, 0f, 2.6f, sonicWaveVisual, this);
        }

        if (!addEnragedRing) return;

        // Segundo anillo con el hueco girado.
        int ringCount = 8;
        int ringGap = Random.Range(0, ringCount);
        for (int i = 0; i < ringCount; i++)
        {
            if (i == ringGap) continue;
            float angle = (i * 360f / ringCount) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
            BossProjectile.Spawn(origin, direction * sonicSpeed * 0.85f, 1, 0f, 2.4f, sonicWaveVisual, this);
        }
    }

    // ------------------------------------------------------------ SPELL (SUELO)
    private void TickSpell()
    {
        // Casteado desde el suelo, sin subir al techo.
        StayOnGround();

        if (stateTime >= AnimatorTime(spellWindup) && fangsSpawned < fangCount)
        {
            phaseTimer += Time.deltaTime;
            if (phaseTimer >= AnimatorTime(spellSpawnInterval) || fangsSpawned == 0)
            {
                phaseTimer = 0f;
                SpawnFang(fangsSpawned, fangCount);
                fangsSpawned++;
                if (fangsSpawned == 1) ClearModelTint();
            }
        }

        if (stateTime >= AnimatorTime(spellDuration)) ChangeState(BossState.Idle);
    }

    private void SpawnFang(int index, int count)
    {
        int gap = Mathf.Max(0, count / 3);
        if (index == gap || index == gap + 1) return; // hueco para esquivar

        // 'lastSpellArea' se calculo al entrar en Spell: ni el movimiento del
        // jefe ni un cambio de camara mueven la lluvia a mitad de cast.
        Rect area = lastSpellArea;
        if (area.width <= 0.02f || area.height <= 0.02f) area = SpellArea();

        float inset = Mathf.Min(Mathf.Abs(fangEdgeInset), area.width * 0.25f);
        float minX = area.xMin + inset;
        float maxX = area.xMax - inset;
        float spawnX = count <= 1 ? area.center.x
                                  : Mathf.Lerp(minX, maxX, (index + 0.5f) / count);
        float spawnY = Mathf.Clamp(area.yMax - 0.3f, groundY + 0.5f, ceilingY + 0.5f);

        Vector2 velocity = new Vector2(Random.Range(-0.5f, 0.5f), -Random.Range(4.5f, 6.5f));
        BossProjectile.Spawn(new Vector2(spawnX, spawnY), velocity, 1,
                             fangGravity, 3f, fangVisual, this);
        combatVfx?.PlayImpact(new Vector3(spawnX, spawnY, arenaZ), new Color(1f, 0.35f, 0.12f));
    }

    // ------------------------------------------------------------ DIVE (AIRE)
    private void BeginDive()
    {
        SetFlying(true);
        diveTarget = (Vector2)ClampToArena(new Vector3(PlayerPosition.x, PlayerPosition.y, arenaZ));
        diveHighPoint = ClampToArena(new Vector3(diveTarget.x, ceilingY - 0.5f, arenaZ));
        phase = 0;
        phaseTimer = 0f;

        // Durante todo el Dive el giro lo decide la trayectoria, no el base Update.
        ResetFacingLock();
        LockFacing(diveRiseTime + diveWarnTime + 2f);
        SetVisualTilt(0f);
    }

    /// <summary>
    /// Alinea el modelo con el vector real de la picada. 'sign' elige izquierda
    /// o derecha y el angulo se compone con el facing para que la malla apunte
    /// por la misma linea que el telegraph.
    /// </summary>
    private void AlignDiveTilt(Vector2 direction, float speed)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        int sign = direction.x >= 0f ? 1 : -1;
        SetFacing(sign);

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float targetTilt = sign > 0f ? angle : Mathf.DeltaAngle(180f, angle);
        targetTilt = Mathf.Clamp(targetTilt, -maxDiveTilt, maxDiveTilt);
        SetVisualTilt(Mathf.MoveTowards(VisualTilt, targetTilt, speed * Time.deltaTime));
    }

    /// <summary>Vuelta suave al angulo neutral al aterrizar/acechar.</summary>
    private void RecoverVisualTilt()
    {
        if (Mathf.Abs(VisualTilt) < 0.01f) return;
        SetVisualTilt(Mathf.MoveTowards(VisualTilt, 0f, tiltRecoverySpeed * Time.deltaTime));
    }

    private void TickDive()
    {
        phaseTimer += Time.deltaTime;

        switch (phase)
        {
            case 0: // subir
                MoveTowards(diveHighPoint, 11f);
                AlignDiveTilt((Vector2)diveHighPoint - (Vector2)transform.position, diveTiltSpeed);
                if (phaseTimer >= AnimatorTime(diveRiseTime) || Vector2.Distance(transform.position, diveHighPoint) < 0.3f)
                {
                    phase = 1;
                    phaseTimer = 0f;
                    warningLine = BossFx.CreateLine(transform.position, diveTarget, warningColor, warningLineMaterial);
                    SetModelTint(warningColor);
                }
                break;

            case 1: // aviso: la linea ya muestra la ruta completa de impacto
                AlignDiveTilt((Vector2)diveTarget - (Vector2)diveHighPoint, diveTiltSpeed);
                if (phaseTimer >= AnimatorTime(diveWarnTime))
                {
                    DestroyWarningLine();
                    ClearModelTint();
                    phase = 2;
                    phaseTimer = 0f;
                }
                break;

            case 2: // embestida: se corrige con el vector real hacia el objetivo
                Vector2 toTarget = (Vector2)diveTarget - (Vector2)transform.position;
                AlignDiveTilt(toTarget, diveTiltSpeed);
                MoveTowards(diveTarget, diveSpeed);
                combatVfx?.PlayTrail(transform.position, new Color(1f, 0.32f, 0.16f));
                if (phaseTimer >= 0.6f || toTarget.magnitude < 0.25f)
                {
                    HitAround(diveHitRadius, aerialDamage);
                    combatVfx?.PlayImpact(transform.position, new Color(1f, 0.25f, 0.12f));
                    diveMissed = Vector2.Distance(PlayerPosition, diveTarget) > 1.3f;
                    phase = 3;
                    phaseTimer = 0f;
                }
                break;

            case 3: // clavado: mantiene la pose y baja antes de volver a Idle
                if (LandFromAir(diveMissed ? diveStaggerTime : 0.35f))
                {
                    FireGroundShockwave();
                    ChangeState(BossState.Idle);
                }
                break;
        }
    }

    // ------------------------------------------------------------ SWEEP (AIRE)
    private void BeginSweep()
    {
        SetFlying(true);

        bool playerToRight = PlayerPosition.x >= transform.position.x;
        float toX = playerToRight ? arenaMaxX - 0.5f : arenaMinX + 0.5f;

        // El barrido conserva la altura actual; solo corrige si esta demasiado
        // cerca del suelo para que el collider no atraviese la arena.
        float sweepY = ClampToArena(new Vector3(transform.position.x, transform.position.y, arenaZ)).y;
        if (sweepY < groundY + sweepHeightAboveGround)
            sweepY = ClampToArena(new Vector3(transform.position.x, groundY + sweepHeightAboveGround, arenaZ)).y;

        float fromX = transform.position.x;
        sweepStart = ClampToArena(new Vector3(fromX, sweepY, arenaZ));
        sweepEnd = ClampToArena(new Vector3(toX, sweepY, arenaZ));
        sweepTravelTime = Mathf.Max(0.2f, Vector2.Distance(sweepStart, sweepEnd) / sweepSpeed);
        phase = 1; // empieza desde la posicion actual; solo hace telemetria previa
        phaseTimer = 0f;

        // El barrido se lee mejor si el modelo mira hacia donde se desplaza.
        ResetFacingLock();
        SetFacing(toX >= fromX ? 1 : -1);
        LockFacing(sweepWarnTime + sweepTravelTime + sweepStunTime + 2f);
        SetVisualTilt(0f);
        CreateSweepTelegraph();
    }

    private void TickSweep()
    {
        phaseTimer += Time.deltaTime;

        switch (phase)
        {
            case 0: // ir al punto de partida
                MoveTowards(sweepStart, sweepApproachSpeed);
                if (phaseTimer >= 0.5f || Vector2.Distance(transform.position, sweepStart) < 0.3f)
                {
                    phase = 1;
                    phaseTimer = 0f;
                    SetModelTint(warningColor);
                }
                break;

            case 1: // aviso
                if (phaseTimer >= sweepWarnTime)
                {
                    ClearModelTint();
                    phase = 2;
                    phaseTimer = 0f;
                }
                break;

            case 2: // barrido
                float t = Mathf.Clamp01(phaseTimer / sweepTravelTime);
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                transform.position = ClampToArena(Vector3.Lerp(sweepStart, sweepEnd, easedT));
                combatVfx?.PlayTrail(transform.position, new Color(1f, 0.55f, 0.16f));

                // El circulo visible usa el mismo centro y radio que HitAround
                // y se apaga en cuanto la ventana de daño termina o ya golpeo.
                bool dangerWindow = !attackHitDone && t > 0.2f && t < 0.8f;
                UpdateSweepActiveVisual(dangerWindow);

                // Un solo golpe por barrido: HitAround usa un HashSet nuevo en
                // cada llamada, sin esta flag el jugador recibiria daño por frame.
                if (dangerWindow &&
                    Vector2.Distance(PlayerPosition, transform.position) <= sweepHitRadius)
                {
                    attackHitDone = true;
                    HitAround(sweepHitRadius, aerialDamage);
                    combatVfx?.PlayImpact(transform.position, new Color(1f, 0.2f, 0.12f));
                }
                if (t >= 1f)
                {
                    UpdateSweepActiveVisual(false);
                    phase = 3;
                    phaseTimer = 0f;
                }
                break;

            case 3: // aterrizaje del barrido
                if (LandFromAir(0.2f))
                {
                    FireGroundShockwave();
                    BreakInvulnerability();
                    SetModelTint(sweepStunnedColor);
                    phase = 4;
                    phaseTimer = 0f;
                }
                break;

            case 4: // pausa vulnerable: el jugador puede castigar el final
                StayOnGround();
                if (phaseTimer >= sweepStunTime)
                    ChangeState(BossState.Idle);
                break;
        }
    }

    private bool MoveToArenaCenter(float speed)
    {
        float centerX = (arenaMinX + arenaMaxX) * 0.5f;
        Vector3 target = ClampToArena(new Vector3(centerX, GroundRestY, arenaZ));
        MoveTowards(target, speed);
        FaceTarget();
        return Mathf.Abs(transform.position.x - target.x) <= 0.08f;
    }

    private void MoveTowards(Vector3 target, float speed)
    {
        Vector3 next = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
        transform.position = ClampToArena(next);
    }

    /// <summary>
    /// Mantiene al jefe en el aire el tiempo minimo de pose, lo baja al suelo
    /// y solo devuelve true cuando ya aterrizo. La invulnerabilidad sigue activa
    /// porque el ataque no termina hasta que esta funcion devuelve true.
    /// </summary>
    private bool LandFromAir(float minPoseTime)
    {
        if (phaseTimer < minPoseTime) return false;

        Vector3 ground = ClampToArena(new Vector3(transform.position.x, GroundRestY, arenaZ));
        MoveTowards(ground, returnSpeed);
        if (Vector2.Distance(transform.position, ground) > 0.15f) return false;

        SetFlying(false);
        StayOnGround();
        return true;
    }

    /// <summary>Mantiene el root apoyado en el suelo con la capsula vertical.</summary>
    private void StayOnGround()
    {
        SetFlying(false);
        RecoverVisualTilt();
        Vector3 p = transform.position;
        float newY = Mathf.MoveTowards(p.y, GroundRestY, returnSpeed * Time.deltaTime);
        transform.position = ClampToArena(new Vector3(p.x, newY, arenaZ));
    }

    // ---------------------------------------------------------------- COLLIDER / ARENA
    /// <summary>
    /// El jefe no debe recibir daño por contacto con trampas, pero necesita
    /// un collider activo siempre para que PlayerCombat pueda golpearlo.
    /// </summary>
    private void IgnoreHazardCollisions()
    {
        DamageOnContact[] hazards = Object.FindObjectsByType<DamageOnContact>();
        for (int i = 0; i < hazards.Length; i++)
        {
            Collider2D hazardCollider = hazards[i].GetComponent<Collider2D>();
            if (hazardCollider != null) Physics2D.IgnoreCollision(bodyCollider, hazardCollider, true);
        }
    }

    protected override void SetFlying(bool value)
    {
        if (flying == value)
        {
            contactDamageEnabled = !value;
            return;
        }

        flying = value;
        if (bodyCollider != null)
        {
            bodyCollider.direction = value ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
            bodyCollider.size = value ? flyingColliderSize : groundedColliderSize;
            bodyCollider.offset = value ? flyingColliderOffset : groundedColliderOffset;
            // Antes se desactivaba: OverlapCircle del jugador no lo veia y el
            // jefe era intocable en Dive/Sweep y mientras bajaba. Trigger
            // mantiene el hurtbox sin bloquear fisicamente ni dañar al jugador.
            bodyCollider.isTrigger = value;
            bodyCollider.enabled = true;
        }

        base.SetFlying(value);
        RefreshBodyMetrics();
    }

    private void RefreshBodyMetrics()
    {
        if (bodyCollider == null) return;
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
            float temp = arenaMinX; arenaMinX = arenaMaxX; arenaMaxX = temp;
        }
        if (groundY > ceilingY)
        {
            float temp = groundY; groundY = ceilingY; ceilingY = temp;
        }

        float minHover = groundY - bodyOffsetY + bodyExtY + 0.1f;
        float maxHover = ceilingY - bodyOffsetY - bodyExtY - 0.1f;
        if (minHover > maxHover)
        {
            float mid = (groundY + ceilingY) * 0.5f - bodyOffsetY;
            minHover = mid;
            maxHover = mid;
        }
        hoverY = Mathf.Clamp(hoverY, minHover, maxHover);
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
                           Mathf.Clamp(position.y, minY, maxY), arenaZ);
    }

    private void DrawAttackArea(BossAttackArea area, Rect fallback, float inset,
                                Color color)
    {
        Rect r = ResolveArea(area, fallback, inset);
        Gizmos.color = color;
        Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, arenaZ),
                            new Vector3(r.width, r.height, 0.05f));
        // Antes se dibujaba una etiqueta con Handles.Label aqui. Eso no es
        // valido desde OnDrawGizmosSelected: Handles pertenece al GUIView de
        // la Scene view (OnSceneGUI de un Editor) y Unity registraba
        // "Screen position out of view frustum" al proyectar la esquina.
        // El color del area ya identifica el ataque; este marcador senala la
        // esquina superior izquierda y se atenua cuando el area esta off.
        Gizmos.color = area.enabled ? color : new Color(color.r, color.g, color.b, 0.2f);
        Vector3 corner = new Vector3(r.xMin, r.yMax, arenaZ);
        Gizmos.DrawWireCube(corner, Vector3.one * 0.16f);
    }

    private void OnDrawGizmosSelected()
    {
        float minX = Mathf.Min(arenaMinX, arenaMaxX);
        float maxX = Mathf.Max(arenaMinX, arenaMaxX);
        float minY = Mathf.Min(groundY, ceilingY);
        float maxY = Mathf.Max(groundY, ceilingY);

        Gizmos.color = new Color(1f, 0.2f, 0.35f, 0.35f);
        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, arenaZ);
        Gizmos.DrawWireCube(center, new Vector3(maxX - minX, maxY - minY, 0.1f));

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        Gizmos.DrawLine(new Vector3(minX, hoverY, arenaZ), new Vector3(maxX, hoverY, arenaZ));

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
        Gizmos.DrawLine(new Vector3(minX, GroundRestY, arenaZ), new Vector3(maxX, GroundRestY, arenaZ));

        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(new Vector3(arenaMinX + entranceTriggerWidth * 0.5f, (minY + maxY) * 0.5f, arenaZ),
                            new Vector3(entranceTriggerWidth, maxY - minY, 0.1f));
        Gizmos.DrawWireCube(new Vector3(arenaMaxX - entranceTriggerWidth * 0.5f, (minY + maxY) * 0.5f, arenaZ),
                            new Vector3(entranceTriggerWidth, maxY - minY, 0.1f));

        // Areas configurables de spawn (se ven aunque el ataque no este activo).
        DrawAttackArea(screamSpawnArea, ScreamFallbackArea(), 0f,
                       new Color(0.65f, 0.35f, 1f, 0.9f));
        DrawAttackArea(spellSpawnArea, SpellFallbackArea(), fangEdgeInset,
                       new Color(0.95f, 0.55f, 0.1f, 0.9f));

        // Ruta y area activa del Sweep mientras se esta ejecutando en Play Mode.
        if (Application.isPlaying && currentState == BossState.Sweep)
        {
            Gizmos.color = sweepTelegraphColor;
            Gizmos.DrawLine(sweepStart, sweepEnd);
            Gizmos.color = sweepActiveColor;
            Gizmos.DrawWireSphere(new Vector3(transform.position.x, transform.position.y, arenaZ),
                                  sweepHitRadius);
        }
    }
}
