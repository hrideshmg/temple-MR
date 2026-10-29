using System;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A world-space button that is pressed directly by Interaction SDK poke (finger) or ray (pinch).
///
/// Unlike a uGUI Button behind a PointableCanvas, this fires on the *press* itself, so a poke
/// that slides sideways or drags a few millimetres while pressed still counts. That uGUI
/// "release over the same element without dragging" rule is what made poke clicks fail before.
/// </summary>
public class PokeButton : MonoBehaviour
{
    public enum Style
    {
        Primary,
        Secondary,
        Ghost,
        Orb,
        Danger,
    }

    /// <summary>Raised once per press (poke or ray select).</summary>
    public event Action Pressed;

    private static readonly Color PrimaryColor = new Color32(0xF2, 0xA3, 0x3A, 0xFF);
    private static readonly Color PrimaryContent = new Color32(0x1B, 0x12, 0x06, 0xFF);
    private static readonly Color SecondaryColor = new Color(1f, 1f, 1f, 0.09f);
    private static readonly Color GhostColor = new Color(1f, 1f, 1f, 0.0f);
    private static readonly Color DangerColor = new Color32(0xC8, 0x3B, 0x2F, 0xFF);
    private static readonly Color Content = new Color(1f, 1f, 1f, 0.94f);
    private static readonly Color ActiveColor = new Color32(0xF2, 0xA3, 0x3A, 0x55);

    private const float PressCooldown = 0.35f;
    private const float PressDepth = 4f;      // canvas units the face sinks when pressed
    private const float AnimSharpness = 18f;
    private const float DisabledAlpha = 0.35f;

    private Style _style;
    private RectTransform _face;
    private Image _background;
    private Image _icon;
    private TMP_Text _label;
    private PokeInteractable _poke;
    private RayInteractable _ray;
    private GameObject _interactionRoot;
    private CanvasGroup _faceGroup;
    private bool _interactable = true;
    private bool _enabled = true;

    private Color _baseColor;
    private Color _contentColor;
    private bool _toggled;
    private float _lastPressTime = -10f;
    private float _hover;
    private float _press;
    private float _flash;

    public RectTransform Rect => (RectTransform)transform;

    public bool Toggled
    {
        get => _toggled;
        set => _toggled = value;
    }

    /// <summary>
    /// Builds a button under <paramref name="parent"/> (a RectTransform inside a world-space canvas).
    /// Call while the parent hierarchy is still inactive so the Interaction SDK components
    /// receive their dependencies before their Start runs.
    /// </summary>
    public static PokeButton Create(RectTransform parent, string name, Style style,
        UISpriteFactory.Icon? icon, string label, float cornerRadius, float fontSize = 15f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        var button = go.AddComponent<PokeButton>();
        button.Build(style, icon, label, cornerRadius, fontSize);
        return button;
    }

    private void Build(Style style, UISpriteFactory.Icon? icon, string label, float cornerRadius, float fontSize)
    {
        _style = style;
        switch (style)
        {
            case Style.Primary:
                _baseColor = PrimaryColor;
                _contentColor = PrimaryContent;
                break;
            case Style.Danger:
                _baseColor = DangerColor;
                _contentColor = Content;
                break;
            case Style.Ghost:
                _baseColor = GhostColor;
                _contentColor = new Color(1f, 1f, 1f, 0.7f);
                break;
            case Style.Orb:
                // The collapsed handle: same bright accent as the primary action so it is easy to spot.
                _baseColor = PrimaryColor;
                _contentColor = PrimaryContent;
                break;
            default:
                _baseColor = SecondaryColor;
                _contentColor = Content;
                break;
        }

        // Face: everything visual lives here so it can sink / scale without moving the hit surface.
        _face = NewRect("Face", Rect);
        Stretch(_face);
        _faceGroup = _face.gameObject.AddComponent<CanvasGroup>();

        if (style == Style.Orb)
        {
            var glow = NewImage("Glow", _face, UISpriteFactory.Shadow, new Color(0.95f, 0.64f, 0.23f, 0.6f));
            Stretch(glow.rectTransform, -16f);
            glow.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / (cornerRadius + 16f);
        }

        _background = NewImage("Background", _face, UISpriteFactory.Rounded, _baseColor);
        Stretch(_background.rectTransform);
        _background.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / cornerRadius;

        bool hasLabel = !string.IsNullOrEmpty(label);
        if (icon.HasValue)
        {
            _icon = NewImage("Icon", _face, UISpriteFactory.GetIcon(icon.Value), _contentColor);
            var irt = _icon.rectTransform;
            float iconSize = fontSize * 1.6f;
            irt.anchorMin = irt.anchorMax = new Vector2(hasLabel ? 0f : 0.5f, 0.5f);
            irt.sizeDelta = new Vector2(iconSize, iconSize);
            irt.anchoredPosition = hasLabel ? new Vector2(14f + iconSize * 0.5f, 0f) : Vector2.zero;
        }

        if (hasLabel)
        {
            var lrt = NewRect("Label", _face);
            Stretch(lrt);
            if (icon.HasValue)
            {
                lrt.offsetMin = new Vector2(14f + fontSize * 1.6f + 8f, 0f);
                lrt.offsetMax = new Vector2(-10f, 0f);
            }
            _label = lrt.gameObject.AddComponent<TextMeshProUGUI>();
            _label.text = label;
            _label.fontSize = fontSize;
            _label.fontStyle = style != Style.Secondary && style != Style.Ghost ? FontStyles.Bold : FontStyles.Normal;
            _label.color = _contentColor;
            _label.alignment = icon.HasValue ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            _label.raycastTarget = false;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    /// <summary>
    /// Adds the poke + ray hit surface sized to the button. Must be called after the button's
    /// RectTransform has its final size.
    /// </summary>
    public void BuildInteraction()
    {
        Vector2 size = Rect.rect.size;

        _interactionRoot = new GameObject("Interaction");
        _interactionRoot.layer = gameObject.layer;
        _interactionRoot.SetActive(false);
        _interactionRoot.transform.SetParent(transform, false);

        // The canvas faces the user along -Z, so the pokeable side is the backward normal.
        var plane = _interactionRoot.AddComponent<PlaneSurface>();
        plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);

        var clipper = _interactionRoot.AddComponent<BoundsClipper>();
        clipper.Position = Vector3.zero;
        clipper.Size = new Vector3(size.x, size.y, 50f);

        var clipped = _interactionRoot.AddComponent<ClippedPlaneSurface>();
        clipped.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { clipper });

        _poke = _interactionRoot.AddComponent<PokeInteractable>();
        _poke.InjectAllPokeInteractable(clipped);
        // Forgiving thresholds: hover a little before contact, and don't cancel a press just
        // because the fingertip slides across the face (the default 3 cm was too strict).
        _poke.EnterHoverNormal = 0.03f;
        _poke.ExitHoverNormal = 0.05f;
        _poke.CancelSelectNormal = 0.12f;
        _poke.CancelSelectTangent = 0.08f;

        _ray = _interactionRoot.AddComponent<RayInteractable>();
        _ray.InjectAllRayInteractable(clipped);

        _poke.WhenStateChanged += OnStateChanged;
        _ray.WhenStateChanged += OnStateChanged;

        _interactionRoot.SetActive(true);
    }

    /// <summary>Enables or disables input (e.g. when the panel collapses).</summary>
    public void SetInteractable(bool interactable)
    {
        _interactable = interactable;
        ApplyInputState();
    }

    /// <summary>
    /// Greys the button out and ignores input while false (e.g. an action that makes no sense
    /// right now). Independent of <see cref="SetInteractable"/>.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        ApplyInputState();
    }

    private void ApplyInputState()
    {
        bool active = _interactable && _enabled;
        if (_interactionRoot != null && _interactionRoot.activeSelf != active)
        {
            _interactionRoot.SetActive(active);
        }
        if (!active)
        {
            _hover = 0f;
            _press = 0f;
        }
        _faceGroup.alpha = _enabled ? 1f : DisabledAlpha;
    }

    public void SetLabel(string text)
    {
        if (_label != null && _label.text != text)
        {
            _label.text = text;
        }
    }

    public void SetIcon(UISpriteFactory.Icon icon)
    {
        if (_icon != null)
        {
            _icon.sprite = UISpriteFactory.GetIcon(icon);
        }
    }

    private void OnStateChanged(InteractableStateChangeArgs args)
    {
        if (args.NewState != InteractableState.Select)
        {
            return;
        }
        if (Time.unscaledTime - _lastPressTime < PressCooldown)
        {
            return;
        }
        _lastPressTime = Time.unscaledTime;
        _flash = 1f;
        Pressed?.Invoke();
    }

    private void Update()
    {
        bool hovered = IsIn(InteractableState.Hover) || IsIn(InteractableState.Select);
        bool selected = IsIn(InteractableState.Select);

        float t = 1f - Mathf.Exp(-AnimSharpness * Time.unscaledDeltaTime);
        _hover = Mathf.Lerp(_hover, hovered ? 1f : 0f, t);
        _press = Mathf.Lerp(_press, selected ? 1f : 0f, t);
        _flash = Mathf.MoveTowards(_flash, 0f, Time.unscaledDeltaTime * 3f);

        Color c = _toggled ? ActiveColor : _baseColor;
        if (_style == Style.Primary || _style == Style.Orb || _style == Style.Danger)
        {
            c = Color.Lerp(c, Color.white, _hover * 0.18f + _flash * 0.25f);
        }
        else
        {
            float lift = _hover * 0.08f + _press * 0.08f + _flash * 0.15f;
            c = new Color(
                Mathf.Lerp(c.r, 1f, lift * 2f),
                Mathf.Lerp(c.g, 1f, lift * 2f),
                Mathf.Lerp(c.b, 1f, lift * 2f),
                Mathf.Max(c.a, 0.05f) + lift);
        }
        _background.color = c;

        float scale = 1f + _hover * 0.03f - _press * 0.05f;
        _face.localScale = new Vector3(scale, scale, 1f);
        _face.localPosition = new Vector3(0f, 0f, _press * PressDepth);
    }

    private bool IsIn(InteractableState state)
    {
        return (_poke != null && _poke.State == state) || (_ray != null && _ray.State == state);
    }

    private void OnDestroy()
    {
        if (_poke != null)
        {
            _poke.WhenStateChanged -= OnStateChanged;
        }
        if (_ray != null)
        {
            _ray.WhenStateChanged -= OnStateChanged;
        }
    }

    // ---------------------------------------------------------------- helpers

    internal static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    internal static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.preserveAspect = img.type == Image.Type.Simple;
        return img;
    }

    internal static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }
}
