using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class AjustarFondo : MonoBehaviour
{
    void Start()
    {
        var sr = GetComponent<SpriteRenderer>();
        var cam = Camera.main;

        float altoCam = cam.orthographicSize * 2f;
        float anchoCam = altoCam * cam.aspect;

        Vector2 tam = sr.sprite.bounds.size;
        float escala = Mathf.Max(anchoCam / tam.x, altoCam / tam.y);

        transform.localScale = new Vector3(escala, escala, 1f);
    }
}