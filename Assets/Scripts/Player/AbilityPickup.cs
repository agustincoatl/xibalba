using UnityEngine;

public enum Ability
{
    DobleSalto,
    Dash,
    SaltoPared,
    ArmaLanzable
}

// Objeto de mejora: al tocarlo, el jugador desbloquea una habilidad.
// Requiere un Collider2D con "Is Trigger" activado.
public class AbilityPickup : MonoBehaviour
{
    [SerializeField] private Ability ability;

    [Header("Efectos Visuales (Animación)")]
    [Tooltip("Velocidad de la animación de escalado")]
    [SerializeField] private float scaleSpeed = 3f;

    [Tooltip("Variación de tamaño (ej. 0.15 = +/- 15% del tamaño base)")]
    [SerializeField] private float scaleAmount = 0.15f;

    [Tooltip("Velocidad de rotación en el eje Z (grados por segundo)")]
    [SerializeField] private float rotationSpeed = 45f;

    private Vector3 baseScale;

    private void Start()
    {
        // Guardar la escala inicial configurada en el inspector
        baseScale = transform.localScale;
    }

    private void Update()
    {
        // 1. Escalación continua (pulsación relativa al tamaño base)
        float factor = Mathf.Sin(Time.time * scaleSpeed) * scaleAmount;
        transform.localScale = baseScale * (1f + factor);

        // 2. Rotación continua sobre el eje Z (ideal para Sprites / 2D)
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
    }

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