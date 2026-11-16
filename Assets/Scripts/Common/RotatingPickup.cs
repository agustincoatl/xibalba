using UnityEngine;

public class RotatingPickup : MonoBehaviour
{
    [Header("Rotación")]
    [SerializeField] private float rotationSpeed = 45f;

    [Header("Pulso")]
    [SerializeField] private float scaleSpeed = 3f;
    [SerializeField] private float scaleAmount = 0.15f;

    private Vector3 baseScale;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void Update()
    {
        transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f);

        if (scaleAmount <= 0f) return;

        float factor = Mathf.Sin(Time.time * scaleSpeed) * scaleAmount;
        transform.localScale = baseScale * (1f + factor);
    }
}
