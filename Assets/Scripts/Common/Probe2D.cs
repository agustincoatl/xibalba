using UnityEngine;

// Utilidad para "sentir" el entorno con rayos (paredes, bordes, suelo).
// Ignora triggers y los colliders del propio personaje.
public static class Probe2D
{
    public static bool Hit(Vector2 origin, Vector2 direction, float distance, LayerMask mask, Transform ignoreRoot)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, distance, mask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null || c.isTrigger) continue;
            if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
            return true;
        }
        return false;
    }
}
