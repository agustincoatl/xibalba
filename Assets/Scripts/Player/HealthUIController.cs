using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class HealthUIController : MonoBehaviour
{
    public PlayerController player;
    public List<Image> healthIcons;
    public Sprite fullMaskSprite;
    public Sprite emptyMaskSprite;

    int ultimaVida = -1;

    void Update()
    {
        if (player == null) return;

        int vidaActual = Mathf.CeilToInt(player.currentHealth);
        if (vidaActual == ultimaVida) return; // solo actualiza cuando cambia la vida
        ultimaVida = vidaActual;

        for (int i = 0; i < healthIcons.Count; i++)
        {
            healthIcons[i].enabled = true;
            healthIcons[i].sprite = (i < vidaActual) ? fullMaskSprite : emptyMaskSprite;
        }
    }
}