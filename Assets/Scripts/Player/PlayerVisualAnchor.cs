using UnityEngine;

/// <summary>
/// Red de seguridad del modelo 3D del jugador. Vive en el root del Player y
/// garantiza que ningun clip (incluidos los nuevos de Mixamo) pueda mover,
/// girar o escalar el objeto del modelo: el Animator se queda sin root motion
/// y la pose local del base se fija en LateUpdate.
/// El giro del personaje lo hace PlayerController sobre el pivote "Visual",
/// así que este script no interfiere con el facing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerVisualAnchor : MonoBehaviour
{
    private Animator animator;
    private Transform modelRoot;

    private Vector3 lockedPosition;
    private Quaternion lockedRotation;
    private Vector3 lockedScale;

    // Se añade solo al Player persistente para no tener que editar las escenas.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAnchor()
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player != null && player.GetComponent<PlayerVisualAnchor>() == null)
                player.gameObject.AddComponent<PlayerVisualAnchor>();
        }
    }

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            modelRoot = animator.transform;
        }
    }

    // Start se ejecuta despues de los Awake de PlayerController, cuando el
    // pivote Visual ya esta creado. Ahi es cuando la pose es definitiva.
    private void Start()
    {
        LockPose();
    }

    /// <summary>Fija la pose actual del modelo como la unica permitida.</summary>
    public void LockPose()
    {
        if (animator != null) animator.applyRootMotion = false;
        if (modelRoot == null) return;

        lockedPosition = modelRoot.localPosition;
        lockedRotation = modelRoot.localRotation;
        lockedScale = modelRoot.localScale;
    }

    private void LateUpdate()
    {
        if (animator != null && animator.applyRootMotion)
            animator.applyRootMotion = false;

        if (modelRoot == null) return;

        modelRoot.localPosition = lockedPosition;
        modelRoot.localRotation = lockedRotation;
        modelRoot.localScale = lockedScale;
    }
}
