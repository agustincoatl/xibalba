using UnityEngine;

/// <summary>
/// Zona de activacion del combate contra un jefe. Pon este script en un
/// GameObject con un Collider2D en modo Trigger. Cuando el jugador entra,
/// se activa el jefe asignado (o el primer BossController de la escena).
/// No necesita referencia al jugador: detecta por el componente
/// PlayerController, sin importar la capa fisica del trigger.
/// </summary>
public class BossArenaTrigger : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Jefe a activar. Si se deja vacio se busca uno en la escena.")]
    [SerializeField] private BossController boss;

    [Header("Comportamiento")]
    [Tooltip("Desactiva el trigger despues de activar la batalla.")]
    [SerializeField] private bool oneShot = true;

    private Collider2D trigger;

    private void Awake()
    {
        trigger = GetComponent<Collider2D>();
        if (trigger != null && !trigger.isTrigger)
        {
            Debug.LogWarning("BossArenaTrigger: el Collider2D debe ser Trigger.", this);
        }
        if (boss == null) boss = FindAnyObjectByType<BossController>();
    }

    private void OnEnable()
    {
        if (boss != null) boss.BattleStarted += OnBattleStarted;
    }

    private void OnDisable()
    {
        if (boss != null) boss.BattleStarted -= OnBattleStarted;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (boss == null || boss.BattleActive || boss.IsDead) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        boss.ActivateBattle();
    }

    private void OnBattleStarted()
    {
        if (oneShot && trigger != null) trigger.enabled = false;
    }

    /// <summary>
    /// Llama a esto desde el flujo de reintento si quieres volver a usar la
    /// misma instancia del jefe (por defecto el retry recarga la escena).
    /// </summary>
    public void ResetBattle()
    {
        if (boss == null) return;
        boss.ResetForEncounter();
        if (trigger != null) trigger.enabled = true;
    }
}
