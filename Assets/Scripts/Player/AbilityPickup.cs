using UnityEngine;

public enum Ability
{
    DobleSalto,
    Dash,
    SaltoPared,
    ArmaLanzable
}

// Objeto de mejora: al tocarlo, el jugador desbloquea una habilidad (el nodo "Obtiene Arma / Mejora" de tu diagrama).
// Requiere un Collider2D con "Is Trigger" activado.
public class AbilityPickup : MonoBehaviour
{
    [SerializeField] private Ability ability;

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        switch (ability)
        {
            case Ability.DobleSalto: player.hasDoubleJump = true; break;
            case Ability.Dash: player.hasDash = true; break;
            case Ability.SaltoPared: player.hasWallJump = true; break;
            case Ability.ArmaLanzable:
                PlayerCombat combat = player.GetComponent<PlayerCombat>();
                if (combat != null) combat.hasThrowable = true;
                break;
        }

        Destroy(gameObject);
    }
}
