using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A floating world-space card for the story: part labels, captions and the title. Built in code in
/// the control panel's style (dark rounded card, gold accent), with its height fitted to the text.
/// Fade it with <see cref="Alpha"/>; place it with <see cref="Place"/>, which turns it to face the eyes.
/// </summary>
public class StoryCard
{
    public static readonly Color CardColor = new Color32(0x14, 0x16, 0x1D, 0xE6);
    public static readonly Color Gold = new Color32(0xF2, 0xA3, 0x3A, 0xFF);
    public static readonly Color TextPrimary = new Color(1f, 1f, 1f, 0.95f);
    public static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.6f);

    private const float Radius = 14f;

    public Transform Transform { get; }

    /// <summary>Card width in metres.</summary>
    public float WorldWidth { get; }

    private readonly RectTransform _rect;
    private readonly RectTransform _card;
    private readonly CanvasGroup _group;
    private readonly float _scale;
    private float _alpha = -1f;

    /// <param name="width">Width in canvas units.</param>
    /// <param name="scale">Metres per canvas unit.</param>
    /// <param name="background">Draw the dark card behind the text.</param>
    /// <param name="accentBar">Draw a gold bar down the card's left edge.</param>
    public StoryCard(string name, Transform parent, float width, float scale, bool background, bool accentBar,
        float padX = 14f, float padY = 10f, TextAnchor align = TextAnchor.UpperLeft)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        _rect = (RectTransform)go.transform;
        _rect.SetParent(parent, false);
        _rect.localScale = Vector3.one * scale;
        _scale = scale;
        Transform = _rect;
        WorldWidth = width * scale;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 4f;
        scaler.referencePixelsPerUnit = 100f;
        _group = go.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // The card inside the canvas grows to fit its text, about the canvas centre.
        _rect.sizeDelta = new Vector2(width, 10f);
        _card = PokeButton.NewRect("Card", _rect);
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(width, 10f);
        var layout = _card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)(padX + (accentBar ? 8f : 0f)), (int)padX, (int)padY, (int)padY);
        layout.spacing = 2f;
        layout.childAlignment = align;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        _card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (background)
        {
            var bg = Decoration("Background", UISpriteFactory.Rounded, CardColor, 0f);
            bg.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / Radius;
            var rim = Decoration("Rim", UISpriteFactory.Rounded, new Color(1f, 1f, 1f, 0.10f), -1.5f);
            rim.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / (Radius + 1.5f);
            rim.transform.SetAsFirstSibling();
        }
        if (accentBar)
        {
            var bar = PokeButton.NewImage("Accent", _card, UISpriteFactory.Rounded, Gold);
            bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(7f, 9f);
            rt.offsetMax = new Vector2(11f, -9f);
            bar.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / 2f;
        }

        Alpha = 0f;
    }

    private Image Decoration(string name, Sprite sprite, Color color, float inset)
    {
        var img = PokeButton.NewImage(name, _card, sprite, color);
        img.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        PokeButton.Stretch(img.rectTransform, inset);
        return img;
    }

    public TMP_Text AddText(string text, float size, FontStyles style, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, float characterSpacing = 0f)
    {
        var rt = PokeButton.NewRect("Text", _card);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.characterSpacing = characterSpacing;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>0 hides the card (and stops it rendering), 1 shows it fully.</summary>
    public float Alpha
    {
        get => _alpha;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, _alpha))
            {
                return;
            }
            _alpha = value;
            _group.alpha = value;
            bool visible = value > 0.001f;
            if (_rect.gameObject.activeSelf != visible)
            {
                _rect.gameObject.SetActive(visible);
            }
        }
    }

    /// <summary>World height of the card in metres (after the layout has fitted it).</summary>
    public float WorldHeight => _card.rect.height * _scale;

    /// <summary>Moves the card to <paramref name="position"/>, facing the eyes, uniformly scaled.</summary>
    public void Place(Vector3 position, Vector3 eyes, float sizeMultiplier = 1f)
    {
        _rect.position = position;
        Vector3 away = position - eyes;
        if (away.sqrMagnitude > 1e-6f)
        {
            // The canvas front faces -Z, so +Z points away from the viewer.
            _rect.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        Vector3 parentScale = _rect.parent != null ? _rect.parent.lossyScale : Vector3.one;
        _rect.localScale = Vector3.one * (_scale * sizeMultiplier / Mathf.Max(parentScale.x, 1e-6f));
    }
}
