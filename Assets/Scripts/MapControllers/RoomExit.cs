using UnityEngine;

public class RoomExit : MonoBehaviour
{
    [SerializeField] private string targetScene;
    [SerializeField] private string targetSpawnId;

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log("RoomExit tocado por: " + other.name);

        // Busca el PlayerController en el objeto o en su padre (no depende de la tag)
        if (other.GetComponentInParent<PlayerController>() == null) return;

        if (TransitionManager.Instance == null)
        {
            Debug.LogError("No hay TransitionManager en la escena.");
            return;
        }

        Debug.Log("Yendo a: " + targetScene + " / spawn: " + targetSpawnId);
        TransitionManager.Instance.GoTo(targetScene, targetSpawnId);
    }
}