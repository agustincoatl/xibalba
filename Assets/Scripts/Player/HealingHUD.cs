using UnityEngine;
using UnityEngine.UI;

// Interfaz de la curación: barra de esencia (Ch'ulel) y vasijas de ofrenda.
// La crea PlayerHealing automáticamente; se destruye sola cuando el jugador desaparece (por ejemplo, al volver al menú).
public class HealingHUD : MonoBehaviour
{
    private const int MaxIcons = 5;

    private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color EssenceNormal = new Color(0.16f, 0.55f, 0.40f, 1f);   // jade
    private static readonly Color EssenceReady = new Color(0.95f, 0.78f, 0.30f, 1f);    // dorado: ya alcanza para curarse
    private static readonly Color OfferingFull = new Color(0.30f, 0.80f, 0.60f, 1f);
    private static readonly Color OfferingEmpty = new Color(1f, 1f, 1f, 0.18f);

    private static Sprite whiteSprite;

    private PlayerHealing healing;
    private Image essenceFill;
    private readonly Image[] offeringIcons = new Image[MaxIcons];

    public static HealingHUD Create(PlayerHealing source)
    {
        GameObject go = new GameObject("HealingHUD");
        DontDestroyOnLoad(go);

        HealingHUD hud = go.AddComponent<HealingHUD>();
        hud.Build(source);
        return hud;
    }

    private static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
        {
            Texture2D tex = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        return whiteSprite;
    }

    private void Build(PlayerHealing source)
    {
        healing = source;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Panel anclado abajo a la izquierda
        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.zero;
        panelRect.pivot = Vector2.zero;
        panelRect.anchoredPosition = new Vector2(40f, 40f);
        panelRect.sizeDelta = new Vector2(320f, 80f);

        // Barra de esencia
        MakeImage("EssenceBackground", panel.transform, new Vector2(0f, 0f), new Vector2(280f, 18f), BarBackground);
        essenceFill = MakeImage("EssenceFill", panel.transform, new Vector2(2f, 2f), new Vector2(276f, 14f), EssenceNormal);
        essenceFill.type = Image.Type.Filled;
        essenceFill.fillMethod = Image.FillMethod.Horizontal;
        essenceFill.fillOrigin = 0;
        essenceFill.fillAmount = 0f;

        // Vasijas de ofrenda
        for (int i = 0; i < MaxIcons; i++)
            offeringIcons[i] = MakeImage("Offering" + i, panel.transform, new Vector2(i * 32f, 26f), new Vector2(24f, 24f), OfferingEmpty);

        // Texto de ayuda
        GameObject textObject = new GameObject("Hint", typeof(RectTransform));
        textObject.transform.SetParent(panel.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.zero;
        textRect.pivot = Vector2.zero;
        textRect.anchoredPosition = new Vector2(0f, 54f);
        textRect.sizeDelta = new Vector2(320f, 24f);

        Text hint = textObject.AddComponent<Text>();
        hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hint.fontSize = 18;
        hint.color = new Color(1f, 1f, 1f, 0.85f);
        hint.text = "Ch’ulel   ·   H: canalizar   ·   Q: ofrenda";
        hint.horizontalOverflow = HorizontalWrapMode.Overflow;
        hint.raycastTarget = false;
    }

    private Image MakeImage(string objectName, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = go.AddComponent<Image>();
        image.sprite = WhiteSprite();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void Update()
    {
        if (healing == null)
        {
            Destroy(gameObject);
            return;
        }

        essenceFill.fillAmount = healing.MaxEssence > 0 ? healing.Essence / healing.MaxEssence : 0f;

        Color barColor = healing.Essence >= healing.ChannelCost ? EssenceReady : EssenceNormal;
        if (healing.IsChanneling)
            barColor = Color.Lerp(barColor, Color.white, 0.4f + 0.4f * Mathf.PingPong(Time.unscaledTime * 6f, 1f));
        essenceFill.color = barColor;

        for (int i = 0; i < MaxIcons; i++)
        {
            offeringIcons[i].enabled = i < healing.MaxOfferings;
            offeringIcons[i].color = i < healing.Offerings ? OfferingFull : OfferingEmpty;
        }
    }
}
