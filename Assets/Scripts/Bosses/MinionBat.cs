using UnityEngine;

/// <summary>
/// Murcielago menor invocado por Camazotz. Simple: persigue, hace dano por contacto
/// y otorga puntos al derrotarlo (una forma de recuperar alma durante la pelea).
/// </summary>
public class MinionBat : MonoBehaviour, IDamageable
{
    [SerializeField] private float speed = 3.2f;
    [SerializeField] private int contactDamage = 1;
    [SerializeField] private int health = 1;
    [SerializeField] private float lifetime = 9f;

    public Team DamageTeam => Team.Enemy;

    private PlayerController player;

    /// <summary>Sin visual usa el circulo generado; con visual instancia el prefab asignado.</summary>
    public static MinionBat Spawn(Vector3 position, BossAttackVisual visual = null)
    {
        if (visual == null)
        {
            visual = new BossAttackVisual
            {
                color = new Color(0.7f, 0.25f, 0.95f, 0.95f),
                radius = 0.225f,
                sortingOrder = 18
            };
        }

        GameObject go;
        if (visual.prefab != null)
        {
            go = Object.Instantiate(visual.prefab, position, Quaternion.identity);
            go.name = "MiniMurcielago";
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
            go = new GameObject("MiniMurcielago");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (visual.radius * 2f);

            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = visual.sprite != null ? visual.sprite : BossFx.Circle;
            sprite.color = visual.color;
            sprite.sortingOrder = visual.sortingOrder;
            if (visual.material != null) sprite.material = visual.material;
        }

        var rb = go.GetComponent<Rigidbody2D>();
        if (rb == null) rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        var col = go.GetComponent<Collider2D>();
        if (col == null) col = go.AddComponent<CircleCollider2D>();
        CircleCollider2D circle = col as CircleCollider2D;
        if (circle != null) circle.radius = 0.5f;
        col.isTrigger = true;

        var bat = go.GetComponent<MinionBat>();
        if (bat == null) bat = go.AddComponent<MinionBat>();
        Destroy(go, bat.lifetime);
        return bat;
    }

    private void Start()
    {
        player = FindAnyObjectByType<PlayerController>();
    }

    private void Update()
    {
        if (player == null || player.IsDead) return;
        transform.position = Vector2.MoveTowards(
            transform.position,
            player.transform.position,
            speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (contactDamage <= 0) return;
        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target == null || target.DamageTeam != Team.Player) return;
        target.TakeDamage(contactDamage, transform.position);
    }

    public void TakeDamage(int amount, Vector2 sourcePosition)
    {
        health -= amount;
        if (health > 0) return;

        ScoreManager.AddPoints(50);
        Destroy(gameObject);
    }
}
