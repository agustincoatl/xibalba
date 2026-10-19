using UnityEngine;
using Unity.Cinemachine;

public class CameraFollowPlayer : MonoBehaviour
{
    void Start()
    {
        var cam = GetComponent<CinemachineCamera>();
        if (cam != null && TransitionManager.Instance != null)
            cam.Follow = TransitionManager.Instance.PlayerTransform;
    }
}