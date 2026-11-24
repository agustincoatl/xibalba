using UnityEngine;

/// <summary>
/// Zona de salida de la sala. Por defecto es SOLO una transicion de escena:
/// no pide llave ni bloquea el paso. Para exigir una puerta hay que asignar
/// requiredDoor / requiredDoorId, o marcar requireDoor para detectar la unica
/// LockedDoor de la escena. Solo responde al jugador. Acepta triggers 2D y 3D.
/// </summary>
public class RoomExit : MonoBehaviour
{
    [SerializeField] private string targetScene;
    [SerializeField] private string targetSpawnId;

    [Header("Puerta requerida (opcional)")]
    [Tooltip("Desactivalo para que la salida sea solo una transicion: no pedira llave aunque exista una LockedDoor en la escena. Marcalo para buscar automaticamente la unica LockedDoor.")]
    [SerializeField] private bool requireDoor;
    [Tooltip("Puerta concreta que debe estar abierta (opcional).")]
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

    private bool RequiresDoor()
    {
        // Un ID o una puerta asignada implican que esta salida esta cerrada,
        // aunque requireDoor este desmarcado (escenas antiguas). Si los tres
        // campos estan vacios, la salida es una transicion libre.
        return requireDoor || requiredDoor != null ||
               !string.IsNullOrWhiteSpace(requiredDoorId);
    }

    private LockedDoor ResolveDoor()
    {
        // Con los dos campos de puerta vacios no se busca nada: asi una
        // LockedDoor suelta en la escena nunca bloquea esta salida.
        if (!RequiresDoor()) return null;

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
        else if (requireDoor && doors.Length == 1)
        {
            // Detecion automatica opt-in: solo se busca la unica puerta cuando
            // el disenador marca Require Door explicitamente.
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
