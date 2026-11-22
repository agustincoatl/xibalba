using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inventario de llaves del jugador. El estado vive en memoria estatica para
/// conservarse entre salas y se reinicia al reintentar o empezar una partida.
/// </summary>
public static class KeyInventory
{
    public const string JadeKeyId = "jade";

    private static readonly HashSet<string> keys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> collectedPickups =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static event Action<string> KeyAdded;
    public static event Action<string> KeyRemoved;

    public static IReadOnlyCollection<string> Keys => keys;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // Con "Reload Domain" desactivado, las estaticas sobreviven entre sesiones de Play.
        ResetAll();
    }

    public static bool HasKey(string keyId)
    {
        return !string.IsNullOrWhiteSpace(keyId) && keys.Contains(keyId.Trim());
    }

    public static void AddKey(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)) return;

        string id = keyId.Trim();
        if (!keys.Add(id)) return;

        KeyAdded?.Invoke(id);
    }

    public static bool RemoveKey(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)) return false;

        string id = keyId.Trim();
        if (!keys.Remove(id)) return false;

        KeyRemoved?.Invoke(id);
        return true;
    }

    public static bool WasPickupCollected(string pickupId)
    {
        return !string.IsNullOrWhiteSpace(pickupId) && collectedPickups.Contains(pickupId.Trim());
    }

    public static void MarkPickupCollected(string pickupId)
    {
        if (string.IsNullOrWhiteSpace(pickupId)) return;
        collectedPickups.Add(pickupId.Trim());
    }

    /// <summary>
    /// Borra llaves y objetos ya recogidos. Se llama al reintentar y al volver al menu.
    /// </summary>
    public static void ResetAll()
    {
        keys.Clear();
        collectedPickups.Clear();
    }
}
