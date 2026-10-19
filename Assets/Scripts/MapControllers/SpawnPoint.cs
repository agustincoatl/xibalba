using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    public string id;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
}