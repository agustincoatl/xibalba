using UnityEngine;

/// <summary>
/// Zona de salida de la sala. Si hay una LockedDoor asociada, no permite
/// cambiar de escena hasta que la puerta este abierta. Solo responde al jugador.
/// Acepta triggers 2D y 3D.
/// </summary>
public class RoomExit : MonoBehaviour
{
    [SerializeField] private string targetScene;
    [SerializeField] private string targetSpawnId;

    [Header("Puerta requerida (opcional)")]
    [Tooltip("Si se deja vacio, se busca la unica LockedDoor de la escena.")]
    [SerializeField] private LockedDoor requiredDoor;
    [Tooltip("Identificador de la puerta a buscar si no se asigna requiredDoor.")]
    [SerializeField] private string requiredDoorId = "";
    [SerializeField] private string lockedMessage = "Necesitas la Llave de Jade para abrir esta puerta";
    [SerializeField] private float messageCooldown = 2f;

    private LockedDoor resolvedDoor;
    private bool doorSearched;
    private float lastMessageTime = -999f;

    private void Start()
    {
        ResolveDoor();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryExit(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Si la puerta se abre mientras el jugador ya esta dentro del trigger,
        // la transicion ocurre sin obligarle a salir y volver a entrar.
        TryExit(other);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryExit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryExit(other);
    }

    private void TryExit(Component other)
    {
        if (other == null) return;

        // Solo el PlayerController puede activar la salida.
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead) return;

        LockedDoor door = ResolveDoor();

        // Si se configuro una puerta concreta y no aparece, es mas seguro bloquear
        // la salida que dejar pasar al jugador sin haber abierto la puerta.
        if (door == null && !string.IsNullOrWhiteSpace(requiredDoorId))
        {
            Debug.LogWarning(name + ": no se encontro la puerta requerida '" + requiredDoorId + "'.");
            ShowLockedMessage();
            return;
        }

        if (door != null && !door.IsOpen)
        {
            ShowLockedMessage();
            return;
        }

        if (TransitionManager.Instance == null)
        {
            Debug.LogError("No hay TransitionManager en la escena.");
            return;
        }

        Debug.Log("Yendo a: " + targetScene + " / spawn: " + targetSpawnId);
        TransitionManager.Instance.GoTo(targetScene, targetSpawnId);
    }

    private LockedDoor ResolveDoor()
    {
        if (resolvedDoor != null) return resolvedDoor;
        if (requiredDoor != null)
        {
            resolvedDoor = requiredDoor;
            return resolvedDoor;
        }

        if (doorSearched) return null;
        doorSearched = true;

        LockedDoor[] doors = FindObjectsByType<LockedDoor>(FindObjectsInactive.Include);

        if (!string.IsNullOrWhiteSpace(requiredDoorId))
        {
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] != null &&
                    string.Equals(doors[i].DoorId, requiredDoorId, System.StringComparison.OrdinalIgnoreCase))
                {
                    resolvedDoor = doors[i];
                    break;
                }
            }
        }
        else if (doors.Length == 1)
        {
            // En Room-01 solo hay una puerta, asi que no hace falta asignarla a mano.
            resolvedDoor = doors[0];
        }

        return resolvedDoor;
    }

    private void ShowLockedMessage()
    {
        if (string.IsNullOrWhiteSpace(lockedMessage)) return;
        if (Time.unscaledTime - lastMessageTime < messageCooldown) return;

        lastMessageTime = Time.unscaledTime;
        MessageManager.Show(lockedMessage);
    }
}
