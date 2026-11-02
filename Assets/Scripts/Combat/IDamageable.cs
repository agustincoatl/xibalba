using UnityEngine;

// Bando al que pertenece cada personaje. Un ataque solo hace daño al bando contrario.
public enum Team
{
    Player, // jugador y aliados
    Enemy   // enemigos
}

// Todo lo que puede recibir daño (jugador, enemigos, aliado) implementa esta interfaz.
public interface IDamageable
{
    Team DamageTeam { get; }
    void TakeDamage(int amount, Vector2 sourcePosition);
}
