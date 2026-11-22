using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Define el aspecto de un ataque generado por el jefe. Deja los campos vacios
/// para usar los circulos generados en runtime; cuando tengas el arte, arrastra
/// aca el prefab / sprite / material y no hace falta tocar el codigo.
/// </summary>
[System.Serializable]
public class BossAttackVisual
{
    [Tooltip("Prefab visual. Si se asigna, se instancia y se le agregan rb/collider/logica.")]
    public GameObject prefab;

    [Tooltip("Sprite suelto (se usa si no hay prefab o para reemplazar el del prefab).")]
    public Sprite sprite;

    [Tooltip("Material del Renderer (opcional).")]
    public Material material;

    [Tooltip("Color del circulo generado cuando no hay sprite.")]
    public Color color = Color.white;

    [Tooltip("Radio del circulo generado cuando no hay sprite.")]
    public float radius = 0.2f;

    [Tooltip("Escala extra aplicada al prefab.")]
    public float scale = 1f;

    [Tooltip("Rotar el visual en la direccion del disparo (estelas, colmillos...).")]
    public bool rotateToVelocity;

    public int sortingOrder = 20;
}

/// <summary>
/// Proyectil generado en runtime para los ataques del jefe.
/// Funciona sin prefab, pero acepta uno (o solo sprite/material) desde el inspector.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class BossProjectile : MonoBehaviour
{
    private Rigidbody2D rb;
    private int damage;
    private bool impacted;
    private bool rotateToVelocity;
    private BossController owner;

    // Registro en memoria: permite limpiar los proyectiles al entrar en
    // recuperacion/vulnerabilidad o al morir, para que no haya hitboxes
    // residuales durante la ventana de castigo.
    private static readonly HashSet<BossProjectile> activeProjectiles = new HashSet<BossProjectile>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void OnDestroy()
    {
        activeProjectiles.Remove(this);
    }

    /// <summary>Destruye todos los proyectiles que pertenecen a este jefe.</summary>
    public static void ClearFrom(BossController source)
    {
        if (source == null) return;
        var snapshot = new List<BossProjectile>(activeProjectiles);
        for (int i = 0; i < snapshot.Count; i++)
            if (snapshot[i].owner == source && snapshot[i] != null)
                Object.Destroy(snapshot[i].gameObject);
    }

    /// <summary>Destruye todos los proyectiles activos (reinicio completo).</summary>
    public static void ClearAll()
    {
        var snapshot = new List<BossProjectile>(activeProjectiles);
        for (int i = 0; i < snapshot.Count; i++)
            if (snapshot[i] != null) Object.Destroy(snapshot[i].gameObject);
    }

    private void Update()
    {
        if (!rotateToVelocity || rb == null) return;
        Vector2 velocity = rb.linearVelocity;
        if (velocity.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);
    }

    /// <summary>Compatibilidad con la firma anterior (circulo generado).</summary>
    public static BossProjectile Spawn(Vector2 position, Vector2 velocity, int damage,
        float gravityScale, float lifetime, Color color, float radius)
    {
        var visual = new BossAttackVisual { color = color, radius = radius };
        return Spawn(position, velocity, damage, gravityScale, lifetime, visual);
    }

    /// <summary>
    /// Crea y lanza un proyectil. gravityScale = 0 para ondas rectas, mayor a 0 para colmillos que caen.
    /// </summary>
    public static BossProjectile Spawn(Vector2 position, Vector2 velocity, int damage,
        float gravityScale, float lifetime, BossAttackVisual visual, BossController owner = null)
    {
        if (visual == null) visual = new BossAttackVisual();

        GameObject go;
        if (visual.prefab != null)
        {
            go = Object.Instantiate(visual.prefab, position, Quaternion.identity);
            go.name = "BossProjectile";
            go.transform.localScale *= visual.scale;

            SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (visual.sprite != null) renderers[i].sprite = visual.sprite;
                if (visual.material != null) renderers[i].material = visual.material;
                renderers[i].sortingOrder = visual.sortingOrder;
            }
        }
        else
        {
            go = new GameObject("BossProjectile");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (visual.radius * 2f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = visual.sprite != null ? visual.sprite : BossFx.Circle;
            renderer.color = visual.color;
            renderer.sortingOrder = visual.sortingOrder;
            if (visual.material != null) renderer.material = visual.material;
        }

        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        if (body == null) body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = gravityScale;
        body.constraints = visual.rotateToVelocity
            ? RigidbodyConstraints2D.None
            : RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        Collider2D collider = go.GetComponent<Collider2D>();
        if (collider == null) collider = go.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;

        BossProjectile projectile = go.GetComponent<BossProjectile>();
        if (projectile == null) projectile = go.AddComponent<BossProjectile>();
        projectile.damage = damage;
        projectile.rotateToVelocity = visual.rotateToVelocity;
        projectile.rb = body;
        projectile.owner = owner;
        activeProjectiles.Add(projectile);

        body.linearVelocity = velocity;
        Object.Destroy(go, lifetime);
        return projectile;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (impacted || other.isTrigger) return; // ignora salidas, checkpoints y otros triggers

        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            if (target.DamageTeam == Team.Enemy) return;
            impacted = true;
            target.TakeDamage(damage, transform.position);
            Destroy(gameObject);
            return;
        }

        // Choque con el escenario: se destruye igual para no acumular proyectiles.
        impacted = true;
        Destroy(gameObject);
    }
}

/// <summary>
/// Feedback visual ligero y reutilizable para Camazotz. Crea los sistemas una
/// sola vez y los reutiliza para evitar Instantiate/Destroy durante el ataque.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossCombatVfx : MonoBehaviour
{
    [SerializeField] private ParticleSystem chargeParticles;
    [SerializeField] private ParticleSystem impactParticles;
    [SerializeField] private ParticleSystem trailParticles;
    [SerializeField] private int burstCount = 10;
    [SerializeField] private float particleLifetime = 0.28f;
    [SerializeField] private float particleSize = 0.13f;

    private ParticleSystem.MainModule chargeMain;
    private ParticleSystem.MainModule impactMain;
    private ParticleSystem.MainModule trailMain;
    private ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
    private float nextTrailTime;
    private CombatRingVfx chargeRing;
    private CombatRingVfx impactRing;

    private void Awake()
    {
        chargeParticles = EnsureSystem(chargeParticles, "ChargeVfx");
        impactParticles = EnsureSystem(impactParticles, "ImpactVfx");
        trailParticles = EnsureSystem(trailParticles, "TrailVfx");

        chargeMain = chargeParticles.main;
        impactMain = impactParticles.main;
        trailMain = trailParticles.main;
        chargeRing = EnsureRing("ChargeRingVfx");
        impactRing = EnsureRing("ImpactRingVfx");
        Configure(chargeParticles);
        Configure(impactParticles);
        Configure(trailParticles);
    }

    public void PlayCharge(Vector3 position, Color color)
    {
        chargeParticles.transform.position = position;
        chargeMain.startColor = color;
        chargeParticles.Emit(emitParams, Mathf.Max(2, burstCount));
        chargeRing.Play(position, color, 0.7f, 0.32f);
    }

    public void PlayImpact(Vector3 position, Color color)
    {
        impactParticles.transform.position = position;
        impactMain.startColor = color;
        impactParticles.Emit(emitParams, Mathf.Max(3, burstCount));
        impactRing.Play(position, color, 1.35f, 0.26f);
    }

    public void PlayTrail(Vector3 position, Color color)
    {
        if (Time.time < nextTrailTime) return;
        nextTrailTime = Time.time + 0.055f;
        trailParticles.transform.position = position;
        trailMain.startColor = color;
        trailParticles.Emit(emitParams, Mathf.Max(1, burstCount / 3));
    }

    private void Configure(ParticleSystem system)
    {
        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.startLifetime = particleLifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.6f);
        main.startSize = particleSize;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.06f;

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.radial = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.6f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.18f, 1f),
            new Keyframe(1f, 0.05f));
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
    }

    private ParticleSystem EnsureSystem(ParticleSystem source, string name)
    {
        if (source != null) return source;
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var system = child.AddComponent<ParticleSystem>();
        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = 24;
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return system;
    }

    private CombatRingVfx EnsureRing(string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(transform, false);
        CombatRingVfx ring = child.AddComponent<CombatRingVfx>();
        ring.Initialize(BossFx.Circle, 25);
        return ring;
    }
}

/// <summary>
/// Anillo expansivo reutilizable para telegráficas e impactos. Es más legible
/// que un círculo estático y no instancia objetos durante el combate.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatRingVfx : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private float elapsed;
    private float duration;
    private float startScale;
    private float endScale;
    private Color baseColor;

    public void Initialize(Sprite sprite, int sortingOrder)
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sortingOrder = sortingOrder;
        spriteRenderer.enabled = false;
    }

    public void Play(Vector3 position, Color color, float scale, float lifetime)
    {
        if (spriteRenderer == null) return;
        transform.position = position;
        elapsed = 0f;
        duration = Mathf.Max(0.05f, lifetime);
        startScale = Mathf.Max(0.05f, scale * 0.25f);
        endScale = Mathf.Max(startScale, scale);
        baseColor = color;
        transform.localScale = Vector3.one * startScale;
        spriteRenderer.color = new Color(color.r, color.g, color.b, 0.9f);
        spriteRenderer.enabled = true;
    }

    private void Update()
    {
        if (spriteRenderer == null || !spriteRenderer.enabled) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float size = Mathf.Lerp(startScale, endScale, eased);
        transform.localScale = Vector3.one * size;
        Color color = baseColor;
        color.a = Mathf.Lerp(0.9f, 0f, t);
        spriteRenderer.color = color;

        if (t >= 1f) spriteRenderer.enabled = false;
    }
}

/// <summary>
/// Feedback ligero del ritmo de ataque. Se reutiliza un unico ParticleSystem
/// por combatiente: no crea objetos durante cada ataque.
/// </summary>
[DisallowMultipleComponent]
public sealed class AttackRateVfx : MonoBehaviour
{
    [SerializeField] private ParticleSystem pulseParticles;
    [SerializeField, Min(2)] private int burstCount = 7;
    [SerializeField, Min(0.04f)] private float particleLifetime = 0.2f;
    [SerializeField, Min(0.01f)] private float particleSize = 0.12f;

    private ParticleSystem.MainModule pulseMain;
    private ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
    private CombatRingVfx pulseRing;

    private void Awake()
    {
        pulseParticles = EnsureSystem(pulseParticles);
        pulseMain = pulseParticles.main;
        GameObject ringObject = new GameObject("AttackRateRingVfx");
        ringObject.transform.SetParent(transform, false);
        pulseRing = ringObject.AddComponent<CombatRingVfx>();
        pulseRing.Initialize(BossFx.Circle, 27);
        pulseMain.loop = false;
        pulseMain.startLifetime = particleLifetime;
        pulseMain.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
        pulseMain.startSize = particleSize;
        pulseMain.simulationSpace = ParticleSystemSimulationSpace.World;
        pulseMain.maxParticles = 48;

        ParticleSystem.EmissionModule emission = pulseParticles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = pulseParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.04f;
        ParticleSystem.VelocityOverLifetimeModule velocity = pulseParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.radial = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        ParticleSystem.ColorOverLifetimeModule color = pulseParticles.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(fade);
    }

    public void PlayAttackPulse(Vector3 position, Color color, float intensity = 1f)
    {
        if (pulseParticles == null) return;
        pulseParticles.transform.position = position;
        pulseMain.startColor = color;
        pulseMain.startSize = particleSize * Mathf.Clamp(intensity, 0.5f, 2f);
        pulseParticles.Emit(emitParams, Mathf.Max(2, Mathf.RoundToInt(burstCount * intensity)));
        pulseRing.Play(position, color, 0.55f * Mathf.Clamp(intensity, 0.5f, 2f), 0.2f);
    }

    private ParticleSystem EnsureSystem(ParticleSystem source)
    {
        if (source != null) return source;

        GameObject child = new GameObject("AttackRatePulseVfx");
        child.transform.SetParent(transform, false);
        ParticleSystem system = child.AddComponent<ParticleSystem>();
        ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = 26;
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return system;
    }
}

/// <summary>Utilidades visuales compartidas (sprite circular y linea de aviso).</summary>
public static class BossFx
{
    private static Sprite circle;
    private static Sprite white;
    private static Material lineMaterial;

    /// <summary>Sprite blanco 1x1 para barras de UI y efectos rectangulares.</summary>
    public static Sprite White
    {
        get
        {
            if (white != null) return white;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            white = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return white;
        }
    }

    public static Sprite Circle
    {
        get
        {
            if (circle != null) return circle;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float edge = Mathf.Clamp01(center.x - distance + 1f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, edge);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return circle;
        }
    }

    /// <summary>Linea temporal que marca la trayectoria antes de la picada.</summary>
    public static LineRenderer CreateLine(Vector3 from, Vector3 to, Color color, Material material = null)
    {
        GameObject go = new GameObject("AvisoPicada");
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startWidth = 0.16f;
        line.endWidth = 0.06f;
        line.useWorldSpace = true;
        line.numCapVertices = 4;

        if (material != null)
        {
            line.material = material;
        }
        else
        {
            if (lineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) lineMaterial = new Material(shader);
            }
            line.material = lineMaterial;
        }

        line.startColor = color;
        line.endColor = color;
        line.sortingOrder = 19;
        return line;
    }

    public static void DestroyLine(LineRenderer line)
    {
        if (line != null) Object.Destroy(line.gameObject);
    }
}
