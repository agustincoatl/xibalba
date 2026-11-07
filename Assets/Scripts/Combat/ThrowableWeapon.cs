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
    private bool hasImpacted;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
    }

    public void Launch(Vector2 direction, Team team)
    {
        ownerTeam = team;
        hasImpacted = false;
        rb.linearVelocity = direction.normalized * speed;

        // La punta de obsidiana debe ir delante. El visual tiene un giro local
        // de 180° en Z, así que el proyectil se orienta con la dirección real
        // del lanzamiento (también funciona en diagonales).
        rb.rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasImpacted) return;
        if (other.isTrigger) return; // ignora salidas de sala, checkpoints, etc.

        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            if (target.DamageTeam == ownerTeam) return; // no daña a los suyos
            hasImpacted = true;
            target.TakeDamage(damage, transform.position);
            Destroy(gameObject);
            return;
        }

        // Impacto contra escenario: la daga se rompe en fragmentos.
        hasImpacted = true;
        SpawnShatter();
        Destroy(gameObject);
    }

    private void SpawnShatter()
    {
        Dagger3DVisual visual = GetComponent<Dagger3DVisual>();
        Vector3 modelEuler = visual != null ? visual.ModelEuler : Vector3.zero;
        string layerName = visual != null ? visual.SortingLayerName : "Default";
        int sortingOrder = visual != null ? visual.SortingOrder + 1 : 11;

        DaggerShatter.Play(transform.position, rb.rotation, modelEuler, layerName, sortingOrder);
    }
}
