using UnityEngine;

// Para trampas y pinchos: daña a cualquiera que los toque (collider normal o trigger).
public class DamageOnContact : MonoBehaviour
{
    [SerializeField] private int damage = 1;

    private void OnCollisionEnter2D(Collision2D collision) => Hit(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => Hit(collision.collider);
    private void OnTriggerEnter2D(Collider2D other) => Hit(other);
    private void OnTriggerStay2D(Collider2D other) => Hit(other);

    private void Hit(Collider2D other)
    {
        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target == null) return;

        // El jugador vuelve a su ultimo punto seguro cuando lo danan estos tiles.
        PlayerController player = target as PlayerController;
        if (player != null)
            player.TakeTileDamage();
        else
            target.TakeDamage(damage, transform.position);
    }
}
