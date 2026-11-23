using UnityEngine;
using UnityEngine.InputSystem;

// Sistema de curación de Sombras de Xibalbá. Combina dos piezas:
//  1) Esencia vital (Ch'ulel): se gana golpeando enemigos cuerpo a cuerpo y se gasta canalizando
//     (mantener H) para recuperar vida. Mientras canaliza, el jugador queda inmóvil y vulnerable.
//  2) Ofrendas: reserva de seguridad que se recarga en los checkpoints y se usa con Q casi al instante,
//     sin depender de haber golpeado enemigos.
// Va en el objeto Player, junto a PlayerController y PlayerCombat.
[RequireComponent(typeof(PlayerController))]
public class PlayerHealing : MonoBehaviour
{
    public static PlayerHealing Instance { get; private set; }

    [Header("Esencia vital (Ch'ulel)")]
    [SerializeField] private int maxEssence = 99;
    [SerializeField] private int essencePerHit = 11;       // esencia que da cada golpe a un enemigo
    [SerializeField] private int channelCost = 33;         // esencia que cuesta una curación (3 golpes)
    [SerializeField] private float channelDuration = 1.0f; // segundos inmóvil y vulnerable
    [SerializeField] private int channelHeal = 1;
    [SerializeField] private bool loseEssenceOnDeath = true;

    [Header("Ofrendas (reserva de seguridad)")]
    [SerializeField] private int maxOfferings = 1;         // se puede ampliar con IncreaseMaxOfferings
    [SerializeField] private int offeringHeal = 1;
    [SerializeField] private float offeringLockTime = 0.25f;

    [Header("Interfaz")]
    [SerializeField] private bool buildHud = true;         // crea sola la barra de esencia y las ofrendas

    [Header("Animación (opcional)")]
    [SerializeField] private string healingBool = "Healing"; // parámetro Bool del Animator; si no existe, se ignora

    [Header("Sonidos (opcionales)")]
    [SerializeField] private AudioClip essenceGainClip;
    [SerializeField] private AudioClip channelStartClip;
    [SerializeField] private AudioClip healClip;
    [SerializeField] private AudioClip offeringClip;
    [SerializeField] private AudioClip rechargeClip;

    // --- Estado para la interfaz y otros scripts ---
    public float Essence => essence;
    public int MaxEssence => maxEssence;
    public int ChannelCost => channelCost;
    public int Offerings => offerings;
    public int MaxOfferings => maxOfferings;
    public bool IsChanneling => isChanneling;
    public float ChannelProgress => isChanneling ? Mathf.Clamp01(channelTimer / Mathf.Max(0.05f, channelDuration)) : 0f;

    private const int MaxOfferingsCap = 5;

    private PlayerController player;
    private PlayerCombat combat;
    private Animator animator;
    private int healingHash;
    private bool hasHealingParam;
    private bool animCached;

    private float essence;
    private int offerings;
    private bool isChanneling;
    private float channelTimer;
    private float offeringLockTimer;
    private bool waitRelease;      // tras curarse hay que soltar H antes de volver a canalizar
    private bool lockedByMe;
    private int lastHealth;
    private bool isDuplicate;

    // Se añade al Player en runtime para no tener que editar prefabs/escenas.
    // sceneLoaded ocurre tras los Awake, pero antes de los Start; PlayerCombat
    // vuelve a buscar el componente al primer golpe por si lo cacheó nulo.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        EnsureOnPlayer();
    }

    private static void EnsureOnPlayer()
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player != null && player.GetComponent<PlayerHealing>() == null)
                player.gameObject.AddComponent<PlayerHealing>();
        }
    }

    private void Awake()
    {
        // Solo puede haber un sistema activo. Las copias de escenas anteriores se
        // destruyen al cargar la siguiente; un duplicado simultáneo se desactiva.
        if (Instance != null && Instance != this)
        {
            isDuplicate = true;
            enabled = false;
            return;
        }

        Instance = this;
        player = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombat>();
        offerings = maxOfferings;
    }

    private void Start()
    {
        if (isDuplicate) return;
        lastHealth = player.currentHealth;
        if (buildHud) HealingHUD.Create(this);
    }

    private void OnDisable()
    {
        if (isDuplicate) return;
        StopChannel();
        ApplyLock(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (isDuplicate) return;

        float dt = Time.deltaTime;
        if (offeringLockTimer > 0f) offeringLockTimer -= dt;

        // Durante transiciones o al morir el sistema se detiene
        if (!player.enabled || player.IsDead)
        {
            StopChannel();
            ApplyLock(false);
            lastHealth = player.currentHealth;
            return;
        }

        Keyboard kb = Keyboard.current;
        bool holdHeal = kb != null && kb.hKey.isPressed;
        if (!holdHeal) waitRelease = false;

        if (isChanneling)
        {
            UpdateChannel(holdHeal, dt);
        }
        else
        {
            if (holdHeal && !waitRelease && CanStartChannel())
                StartChannel();
            else if (kb != null && kb.qKey.wasPressedThisFrame && CanUseOffering())
                UseOffering();
        }

        lastHealth = player.currentHealth;
        ApplyLock(isChanneling || offeringLockTimer > 0f);
    }

    // ---------------------------------------------------------------- ESENCIA

    // PlayerCombat lo llama por cada enemigo golpeado
    public void AddEssence(int amount = -1)
    {
        if (isDuplicate) return;
        if (amount < 0) amount = essencePerHit;

        essence = Mathf.Min(maxEssence, essence + amount);
        Play(essenceGainClip, 0.6f);
    }

    private bool CanStartChannel()
    {
        return essence >= channelCost
            && player.currentHealth < player.maxHealth
            && player.IsGrounded
            && player.CanAct
            && offeringLockTimer <= 0f
            && (combat == null || !combat.IsAttacking);
    }

    private void StartChannel()
    {
        isChanneling = true;
        channelTimer = 0f;
        Play(channelStartClip);
        SetHealingAnim(true);
    }

    private void UpdateChannel(bool holdHeal, float dt)
    {
        // Se interrumpe al soltar, al caer o al recibir un golpe: la esencia ya gastada se pierde
        bool tookDamage = player.IsHurt || player.currentHealth < lastHealth;
        if (!holdHeal || !player.IsGrounded || tookDamage)
        {
            StopChannel();
            return;
        }

        float duration = Mathf.Max(0.05f, channelDuration);
        essence = Mathf.Max(0f, essence - channelCost * (dt / duration));
        channelTimer += dt;

        if (channelTimer >= duration)
        {
            Heal(channelHeal);
            Play(healClip);
            waitRelease = true;
            StopChannel();
        }
    }

    private void StopChannel()
    {
        if (!isChanneling) return;

        isChanneling = false;
        channelTimer = 0f;
        SetHealingAnim(false);
    }

    // ---------------------------------------------------------------- OFRENDAS

    private bool CanUseOffering()
    {
        return offerings > 0
            && player.currentHealth < player.maxHealth
            && !isChanneling
            && player.CanAct
            && offeringLockTimer <= 0f;
    }

    private void UseOffering()
    {
        offerings--;
        Heal(offeringHeal);
        offeringLockTimer = offeringLockTime;
        Play(offeringClip);
    }

    // Lo llama el Checkpoint cada vez que el jugador "descansa" ahí
    public void OnRest()
    {
        if (isDuplicate) return;
        if (offerings >= maxOfferings) return;

        offerings = maxOfferings;
        Play(rechargeClip);
    }

    // Lo llama PlayerController.Revive al reaparecer tras morir
    public void OnRespawn()
    {
        if (isDuplicate) return;

        StopChannel();
        ApplyLock(false);
        offeringLockTimer = 0f;
        waitRelease = false;
        offerings = maxOfferings;
        if (loseEssenceOnDeath) essence = 0f;
        lastHealth = player.currentHealth;
    }

    // Recompensa secreta de exploración: más ofrendas
    public void IncreaseMaxOfferings(int amount = 1)
    {
        maxOfferings = Mathf.Clamp(maxOfferings + amount, 1, MaxOfferingsCap);
        offerings = maxOfferings;
    }

    // ---------------------------------------------------------------- AUXILIARES

    private void Heal(int amount)
    {
        player.currentHealth = Mathf.Min(player.maxHealth, player.currentHealth + amount);
    }

    private void ApplyLock(bool locked)
    {
        if (lockedByMe == locked) return;

        lockedByMe = locked;
        player.ControlLocked = locked;
    }

    private void Play(AudioClip clip, float volume = 1f)
    {
        if (clip != null && AudioManager.Instance != null)
            AudioManager.Instance.Play(clip, volume);
    }

    private void SetHealingAnim(bool value)
    {
        if (!animCached) CacheAnimator();
        if (hasHealingParam) animator.SetBool(healingHash, value);
    }

    private void CacheAnimator()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator == null || animator.runtimeAnimatorController == null) return;

        healingHash = Animator.StringToHash(healingBool);
        hasHealingParam = false;
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.nameHash == healingHash && p.type == AnimatorControllerParameterType.Bool)
                hasHealingParam = true;
        }
        animCached = true;
    }
}
