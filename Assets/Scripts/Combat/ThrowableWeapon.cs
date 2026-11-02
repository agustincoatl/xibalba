using UnityEngine;

// Proyectil lanzable. Requiere Rigidbody2D (Dynamic, Gravity Scale 0) y un Collider2D con "Is Trigger" activado.
[RequireComponent(typeof(Rigidbody2D))]
public class ThrowableWeapon : MonoBehaviour
{
    [SerializeField] private float speed = 12f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float lifetime = 3f;

    private Team ownerTeam = Team.Player;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
    }

    public void Launch(Vector2 direction, Team team)
    {
        ownerTeam = team;
        rb.linearVelocity = direction.normalized * speed;

        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (direction.x >= 0f ? 1f : -1f);
        transform.localScale = s;

        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.isTrigger) return; // ignora salidas de sala, checkpoints, etc.

        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            if (target.DamageTeam == ownerTeam) return; // no daña a los suyos
            target.TakeDamage(damage, transform.position);
        }

        Destroy(gameObject); // choca con enemigo o con el escenario
    }
}
