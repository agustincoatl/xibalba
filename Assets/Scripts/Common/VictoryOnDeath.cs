using UnityEngine;

/// <summary>
/// Coloca este componente en el jefe final (Jefe 2). Cuando el enemigo muere,
/// llama a GameManager.Victory(). Tambien se puede invocar desde un UnityEvent.
/// </summary>
[DisallowMultipleComponent]
public class VictoryOnDeath : MonoBehaviour
{
    [Header("Puntos (opcional)")]
    [SerializeField] private int pointsOnVictory = 0;

    private EnemyChaser chaser;
    private EnemyPatrol patrol;
    private BasicEnemy basicEnemy;
    private BossController boss;
    private bool triggered;

    private void Awake()
    {
        chaser = GetComponent<EnemyChaser>();
        patrol = GetComponent<EnemyPatrol>();
        basicEnemy = GetComponent<BasicEnemy>();
        boss = GetComponent<BossController>();

        if (chaser == null && patrol == null && basicEnemy == null && boss == null)
        {
            Debug.LogWarning(name + ": VictoryOnDeath necesita un BossController, EnemyChaser, EnemyPatrol o BasicEnemy en el mismo objeto.");
            return;
        }

        if (chaser != null) chaser.Died += HandleDeath;
        if (patrol != null) patrol.Died += HandleDeath;
        if (basicEnemy != null) basicEnemy.Died += HandleDeath;
        if (boss != null) boss.Defeated += HandleDeath;
    }

    private void OnDestroy()
    {
        if (chaser != null) chaser.Died -= HandleDeath;
        if (patrol != null) patrol.Died -= HandleDeath;
        if (basicEnemy != null) basicEnemy.Died -= HandleDeath;
        if (boss != null) boss.Defeated -= HandleDeath;
    }

    /// <summary>
    /// Permite disparar la victoria manualmente desde un UnityEvent o desde otro script.
    /// </summary>
    public void TriggerVictory()
    {
        HandleDeath();
    }

    private void HandleDeath()
    {
        if (triggered) return;
        triggered = true;

        if (pointsOnVictory != 0)
            ScoreManager.AddPoints(pointsOnVictory);

        GameManager.TriggerVictory();
    }
}
