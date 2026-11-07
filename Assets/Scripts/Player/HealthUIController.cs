using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class HealthUIController : MonoBehaviour
{
    [Header("Referencias")]
    public PlayerController player;
    public List<Image> healthIcons;
    public Sprite fullMaskSprite;
    public Sprite emptyMaskSprite;

    [Header("Animación de rotación y apagado")]
    [Tooltip("Duración de la rotación de cada corazón, en segundos.")]
    [SerializeField] private float rotationDuration = 0.45f;
    [Tooltip("Grados que gira el corazón al perder/recuperar una vida.")]
    [SerializeField] private float rotationDegrees = 360f;
    [Tooltip("Eje de rotación. Y = giro 3D tipo moneda; Z = giro 2D sobre el plano.")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;
    [Tooltip("Pequeño aumento de escala a mitad del giro para dar más impacto.")]
    [SerializeField] private float scalePunch = 0.15f;
    [Tooltip("Retardo entre corazones cuando se pierden o recuperan varios a la vez.")]
    [SerializeField] private float iconStagger = 0.08f;

    [Header("Colores")]
    [SerializeField] private Color fullColor = Color.white;
    [SerializeField] private Color offColor = new Color(0.75f, 0.75f, 0.75f, 0.8f);

    int ultimaVida = -1;

    Coroutine[] animaciones;
    Vector3[] escalasBase;
    Quaternion[] rotacionesBase;

    void Awake()
    {
        int cantidad = healthIcons != null ? healthIcons.Count : 0;
        animaciones = new Coroutine[cantidad];
        escalasBase = new Vector3[cantidad];
        rotacionesBase = new Quaternion[cantidad];

        for (int i = 0; i < cantidad; i++)
        {
            if (healthIcons[i] == null) continue;

            RectTransform rect = healthIcons[i].rectTransform;
            escalasBase[i] = rect.localScale;
            rotacionesBase[i] = rect.localRotation;
        }
    }

    void Update()
    {
        if (player == null || healthIcons == null) return;

        int vidaActual = Mathf.Clamp(Mathf.CeilToInt(player.currentHealth), 0, healthIcons.Count);

        // Primera actualización: coloca los corazones sin animación.
        if (ultimaVida < 0)
        {
            ultimaVida = vidaActual;

            for (int i = 0; i < healthIcons.Count; i++)
                AplicarEstado(i, i < vidaActual);

            return;
        }

        if (vidaActual == ultimaVida) return; // solo actualiza cuando cambia la vida

        int vidaAnterior = ultimaVida;
        ultimaVida = vidaActual;

        for (int i = 0; i < healthIcons.Count; i++)
        {
            bool debeEstarEncendido = i < vidaActual;
            bool estabaEncendido = i < vidaAnterior;

            if (debeEstarEncendido == estabaEncendido) continue;
            if (healthIcons[i] == null) continue;

            // Si el corazón cambió otra vez mientras se animaba, reinicia su animación.
            if (animaciones[i] != null)
            {
                StopCoroutine(animaciones[i]);
                animaciones[i] = null;
                ResetTransform(i);
            }

            // Al apagarse, empieza por el corazón de la derecha; al encenderse, por la izquierda.
            float retardo = debeEstarEncendido
                ? (i - vidaAnterior) * iconStagger
                : (vidaAnterior - 1 - i) * iconStagger;

            animaciones[i] = StartCoroutine(AnimarCorazon(i, debeEstarEncendido, Mathf.Max(0f, retardo)));
        }
    }

    void AplicarEstado(int indice, bool encendido)
    {
        Image icono = healthIcons[indice];
        if (icono == null) return;

        icono.enabled = true;
        icono.sprite = encendido ? fullMaskSprite : emptyMaskSprite;
        icono.color = encendido ? fullColor : offColor;
        ResetTransform(indice);
    }

    void ResetTransform(int indice)
    {
        Image icono = healthIcons[indice];
        if (icono == null) return;

        RectTransform rect = icono.rectTransform;
        rect.localRotation = rotacionesBase[indice];
        rect.localScale = escalasBase[indice];
    }

    IEnumerator AnimarCorazon(int indice, bool encender, float retardo)
    {
        if (retardo > 0f)
            yield return new WaitForSecondsRealtime(retardo);

        Image icono = healthIcons[indice];
        if (icono == null)
        {
            animaciones[indice] = null;
            yield break;
        }

        RectTransform rect = icono.rectTransform;
        Vector3 escalaBase = escalasBase[indice];
        Quaternion rotacionBase = rotacionesBase[indice];
        Color colorInicial = icono.color;
        Color colorFinal = encender ? fullColor : offColor;
        float duracion = Mathf.Max(0.01f, rotationDuration);
        Vector3 ejeRotacion = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis : Vector3.up;

        float tiempo = 0f;
        while (tiempo < duracion)
        {
            tiempo += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(tiempo / duracion);
            float suave = t * t * (3f - 2f * t); // ease in-out

            // Giro 3D (por defecto sobre Y) mientras el corazón se apaga o enciende.
            rect.localRotation = rotacionBase * Quaternion.AngleAxis(rotationDegrees * suave, ejeRotacion);

            // Pequeño "punch" de escala a mitad de la animación.
            rect.localScale = escalaBase * (1f + Mathf.Sin(t * Mathf.PI) * scalePunch);

            // Cambia el sprite justo cuando el corazón está de canto (mitad del giro).
            if (t >= 0.5f)
                icono.sprite = encender ? fullMaskSprite : emptyMaskSprite;

            icono.color = Color.Lerp(colorInicial, colorFinal, suave);

            yield return null;
        }

        // Estado final exacto.
        rect.localRotation = rotacionBase;
        rect.localScale = escalaBase;
        icono.sprite = encender ? fullMaskSprite : emptyMaskSprite;
        icono.color = colorFinal;
        animaciones[indice] = null;
    }
}
