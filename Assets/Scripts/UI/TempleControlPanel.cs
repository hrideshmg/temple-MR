using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A hand-reachable control panel that docks to the user's side, like a lectern at hip height.
///
/// Placement: the panel is anchored to a lazily-tracked "body" direction rather than the head.
/// Looking around (including turning to look at the panel itself) leaves it alone; only a sustained
/// turn of the whole body (past <see cref="_turnThreshold"/>) swings it round, and walking carries
/// it along smoothly. It always tilts to face the eyes. It can be collapsed into a bright "Menu"
/// handle that expands again on a poke.
///
/// The whole UI is built in code at startup (see <see cref="Build"/>) so there are no prefabs or
/// art assets to keep in sync. Every button is a <see cref="PokeButton"/>, which works with both
/// direct finger poke and hand/controller rays.
/// </summary>
public class TempleControlPanel : MonoBehaviour
{
    private enum Side
    {
        Left = -1,
        Right = 1,
    }

    [Header("Targets")]
    [SerializeField]
    [Tooltip("Head transform. Defaults to Camera.main (CenterEyeAnchor).")]
    private Transform _head;

    [SerializeField]
    [Tooltip("Defaults to the first TempleResetter in the scene.")]
    private TempleResetter _temple;

    [SerializeField]
    [Tooltip("Defaults to the first StoryAudioController in the scene.")]
    private StoryAudioController _story;

    [SerializeField]
    [Tooltip("Optional click sound played on every press.")]
    private AudioClip _clickSound;

    [Header("Docking (metres / degrees)")]
    [SerializeField] private Side _side = Side.Right;

    [SerializeField]
    [Tooltip("How far out to the side of the body the panel sits.")]
    private float _sideOffset = 0.24f;

    [SerializeField]
    [Tooltip("How far in front of the body the panel sits. ~0.3-0.35 m is a relaxed arm's reach.")]
    private float _forwardOffset = 0.32f;

    [SerializeField]
    [Tooltip("Panel centre height below the eyes. ~0.4 m is lower-chest height.")]
    private float _dropBelowEyes = 0.40f;

    [SerializeField]
    [Tooltip("Body yaw only follows the head once the head is turned further than this.")]
    private float _turnThreshold = 55f;

    [SerializeField]
    [Tooltip("Seconds the panel takes to settle when it moves.")]
    private float _smoothTime = 0.25f;

    [Header("Look")]
    [SerializeField]
    [Tooltip("World size of one canvas unit. 0.0008 => the 240-unit wide panel is 19 cm.")]
    private float _canvasScale = 0.0008f;

    [SerializeField]
    [Tooltip("Start collapsed into the Menu handle.")]
    private bool _startCollapsed;

    // Layout (canvas units, 1 unit = _canvasScale metres).
    private const float Width = 240f;
    private const float Height = 328f;
    private const float Pad = 16f;
    private const float PanelRadius = 22f;
    private const float ButtonRadius = 14f;
    private static readonly Vector2 HandleSize = new Vector2(168f, 60f);

    private const string SetPositionLabel = "Set temple position";

    private static readonly Color PanelColor = new Color32(0x14, 0x16, 0x1D, 0xF8);
    private static readonly Color TextPrimary = new Color(1f, 1f, 1f, 0.95f);
    private static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Accent = new Color32(0xF2, 0xA3, 0x3A, 0xFF);

    // Built UI
    private RectTransform _canvasRect;
    private RectTransform _panel;
    private CanvasGroup _panelGroup;
    private RectTransform _handle;
    private CanvasGroup _handleGroup;
    private PokeButton[] _panelButtons;
    private PokeButton _handleButton;
    private PokeButton _playButton;
    private PokeButton _setPositionButton;
    private RectTransform _progressFill;
    private TMP_Text _timeText;
    private AudioSource _audio;
    private Coroutine _savedFeedback;

    // Placement state
    private bool _collapsed;
    private float _expand;          // 0 = handle, 1 = panel
    private float _bodyYaw;
    private bool _turning;
    private float _eyeHeight;
    private Vector3 _velocity;
    private float _initTimer;

    private void Awake()
    {
        if (_head == null && Camera.main != null)
        {
            _head = Camera.main.transform;
        }
        if (_temple == null)
        {
            _temple = FindAnyObjectByType<TempleResetter>();
        }
        if (_story == null)
        {
            _story = FindAnyObjectByType<StoryAudioController>();
        }

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.volume = 0.6f;

        Build();
        _collapsed = _startCollapsed;
        _expand = _collapsed ? 0f : 1f;
        ApplyExpand();
    }

    // =====================================================================  build

    private void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.SetActive(false);
        canvasGo.layer = 5; // UI
        _canvasRect = (RectTransform)canvasGo.transform;
        _canvasRect.SetParent(transform, false);
        _canvasRect.sizeDelta = new Vector2(Width, Height);
        _canvasRect.localScale = Vector3.one * _canvasScale;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                          AdditionalCanvasShaderChannels.Normal |
                                          AdditionalCanvasShaderChannels.Tangent;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 4f;
        scaler.referencePixelsPerUnit = 100f;

        BuildPanel();
        BuildHandle();

        canvasGo.SetActive(true);
    }

    private void BuildPanel()
    {
        _panel = PokeButton.NewRect("Panel", _canvasRect);
        _panel.sizeDelta = new Vector2(Width, Height);
        _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();

        var shadow = PokeButton.NewImage("Shadow", _panel, UISpriteFactory.Shadow, new Color(0f, 0f, 0f, 0.45f));
        PokeButton.Stretch(shadow.rectTransform, -18f);
        shadow.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / (PanelRadius + 18f);

        var rim = PokeButton.NewImage("Rim", _panel, UISpriteFactory.Rounded, new Color(1f, 1f, 1f, 0.10f));
        PokeButton.Stretch(rim.rectTransform, -1.5f);
        rim.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / (PanelRadius + 1.5f);

        var bg = PokeButton.NewImage("Background", _panel, UISpriteFactory.Rounded, PanelColor);
        PokeButton.Stretch(bg.rectTransform);
        bg.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / PanelRadius;

        // Accent strip along the top edge.
        var strip = PokeButton.NewImage("AccentStrip", _panel, UISpriteFactory.Rounded, Accent);
        Place(strip.rectTransform, Width * 0.5f - 24f, 7f, 48f, 4f);
        strip.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / 2f;

        // ---- Header
        Text("Title", "Temple MR", 19f, FontStyles.Bold, TextPrimary, Pad, 18f, 160f, 24f);
        Text("Subtitle", "Control panel", 11.5f, FontStyles.Normal, TextSecondary, Pad, 41f, 160f, 16f);
        var collapse = Button("Collapse", PokeButton.Style.Secondary, UISpriteFactory.Icon.Collapse, null,
            Width - Pad - 36f, 18f, 36f, 36f, Collapse, 13f);

        Divider(68f);

        // ---- Story
        _playButton = Button("PlayStory", PokeButton.Style.Primary, UISpriteFactory.Icon.Play, "Play story",
            Pad, 82f, Width - Pad * 2f, 52f, () => _story?.TogglePlayPause(), 16f);

        var track = PokeButton.NewImage("ProgressTrack", _panel, UISpriteFactory.Rounded, new Color(1f, 1f, 1f, 0.10f));
        Place(track.rectTransform, Pad, 144f, Width - Pad * 2f, 4f);
        track.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / 2f;
        var fill = PokeButton.NewImage("ProgressFill", track.rectTransform, UISpriteFactory.Rounded, Accent);
        _progressFill = fill.rectTransform;
        _progressFill.anchorMin = Vector2.zero;
        _progressFill.anchorMax = new Vector2(0f, 1f);
        _progressFill.pivot = new Vector2(0f, 0.5f);
        _progressFill.offsetMin = _progressFill.offsetMax = Vector2.zero;
        fill.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / 2f;
        _timeText = Text("Time", "0:00 / 0:00", 10.5f, FontStyles.Normal, TextSecondary, Pad, 152f, Width - Pad * 2f, 14f);
        _timeText.alignment = TextAlignmentOptions.Right;

        Divider(178f);

        // ---- Temple
        SectionLabel("TEMPLE", 190f);
        var reset = Button("ResetTemple", PokeButton.Style.Secondary, UISpriteFactory.Icon.Home, "Reset temple",
            Pad, 208f, Width - Pad * 2f, 48f, () => _temple?.ResetTemple(), 15f);
        _setPositionButton = Button("SetTemplePosition", PokeButton.Style.Secondary, UISpriteFactory.Icon.Pin, SetPositionLabel,
            Pad, 264f, Width - Pad * 2f, 48f, SetTemplePosition, 15f);

        _panelButtons = new[] { collapse, _playButton, reset, _setPositionButton };
        foreach (var b in _panelButtons)
        {
            b.BuildInteraction();
        }
    }

    private void BuildHandle()
    {
        _handle = PokeButton.NewRect("Handle", _canvasRect);
        _handle.sizeDelta = HandleSize;
        _handleGroup = _handle.gameObject.AddComponent<CanvasGroup>();

        _handleButton = PokeButton.Create(_handle, "HandleButton", PokeButton.Style.Orb, UISpriteFactory.Icon.Menu, "Menu",
            HandleSize.y * 0.5f, 21f);
        PokeButton.Stretch(_handleButton.Rect);
        _handleButton.Pressed += Expand;
        _handleButton.Pressed += Click;
        _handleButton.BuildInteraction();
    }

    // =====================================================================  actions

    private void SetTemplePosition()
    {
        if (_temple == null)
        {
            return;
        }
        _temple.SetHomeToCurrent();

        // Brief confirmation on the button itself.
        if (_savedFeedback != null)
        {
            StopCoroutine(_savedFeedback);
        }
        _savedFeedback = StartCoroutine(SavedFeedback());
    }

    private IEnumerator SavedFeedback()
    {
        _setPositionButton.SetLabel("Position saved");
        _setPositionButton.Toggled = true;
        yield return new WaitForSeconds(1.5f);
        _setPositionButton.SetLabel(SetPositionLabel);
        _setPositionButton.Toggled = false;
        _savedFeedback = null;
    }

    public void Collapse()
    {
        _collapsed = true;
        ApplyExpand();
    }

    public void Expand()
    {
        _collapsed = false;
        ApplyExpand();
    }

    private void Click()
    {
        if (_clickSound != null)
        {
            _audio.PlayOneShot(_clickSound);
        }
    }

    // =====================================================================  update

    private void LateUpdate()
    {
        UpdatePlacement();
        UpdateExpandAnimation();
        UpdateStatus();
    }

    private void UpdatePlacement()
    {
        if (_head == null)
        {
            return;
        }

        float dt = Time.deltaTime;
        float headYaw = HeadYaw();

        // Snap for the first moments after tracking starts (the head sits at the exact origin until then).
        if (_head.localPosition == Vector3.zero)
        {
            _initTimer = 0f;
        }
        bool snapping = _initTimer < 1f;
        _initTimer += dt;
        if (snapping)
        {
            _bodyYaw = headYaw;
            _eyeHeight = _head.position.y;
            _turning = false;
        }

        // Lazy body yaw: ignore normal glancing, follow a real turn until nearly re-centred.
        float delta = Mathf.DeltaAngle(_bodyYaw, headYaw);
        if (!_turning && Mathf.Abs(delta) > _turnThreshold)
        {
            _turning = true;
        }
        if (_turning)
        {
            _bodyYaw += delta * (1f - Mathf.Exp(-5f * dt));
            if (Mathf.Abs(delta) < 4f)
            {
                _turning = false;
            }
        }

        // Filter eye height so nodding / bobbing doesn't make the panel bounce.
        _eyeHeight = Mathf.Lerp(_eyeHeight, _head.position.y, 1f - Mathf.Exp(-2f * dt));

        Quaternion body = Quaternion.Euler(0f, _bodyYaw, 0f);
        Vector3 basePos = new Vector3(_head.position.x, _eyeHeight - _dropBelowEyes, _head.position.z);
        Vector3 target = basePos + body * new Vector3((int)_side * _sideOffset, 0f, _forwardOffset);

        transform.position = snapping
            ? target
            : Vector3.SmoothDamp(transform.position, target, ref _velocity, _smoothTime, Mathf.Infinity, dt);

        // Face the eyes. The canvas front faces -Z, so +Z points away from the viewer.
        Vector3 toPanel = transform.position - _head.position;
        if (toPanel.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
            transform.rotation = snapping ? look : Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-8f * dt));
        }
    }

    private float HeadYaw()
    {
        Vector3 f = _head.forward;
        // Looking steeply down: use the head's up vector, which still points where the body faces.
        Vector3 flat = new Vector3(f.x, 0f, f.z);
        if (flat.sqrMagnitude < 0.09f)
        {
            Vector3 u = _head.up;
            flat = new Vector3(u.x, 0f, u.z);
        }
        return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
    }

    private void UpdateExpandAnimation()
    {
        float target = _collapsed ? 0f : 1f;
        if (Mathf.Approximately(_expand, target))
        {
            return;
        }
        _expand = Mathf.MoveTowards(_expand, target, Time.deltaTime / 0.22f);
        ApplyExpand();
    }

    private void ApplyExpand()
    {
        float k = EaseOut(_expand);

        _panelGroup.alpha = k;
        _panel.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k);
        _handleGroup.alpha = 1f - k;
        _handle.localScale = Vector3.one * Mathf.Lerp(1f, 0.5f, k);

        // Only the visible state takes input; flip as soon as the toggle is requested.
        foreach (var b in _panelButtons)
        {
            b.SetInteractable(!_collapsed);
        }
        _handleButton.SetInteractable(_collapsed);
        _panel.gameObject.SetActive(k > 0.001f);
        _handle.gameObject.SetActive(k < 0.999f);
    }

    private void UpdateStatus()
    {
        if (_story == null)
        {
            return;
        }
        bool playing = _story.IsPlaying;
        _playButton.SetLabel(playing ? "Pause story" : (_story.PlaybackTime > 0.01f ? "Resume story" : "Play story"));
        _playButton.SetIcon(playing ? UISpriteFactory.Icon.Pause : UISpriteFactory.Icon.Play);

        float len = _story.Length;
        float t = _story.PlaybackTime;
        _progressFill.anchorMax = new Vector2(len > 0f ? Mathf.Clamp01(t / len) : 0f, 1f);
        _timeText.text = $"{FormatTime(t)} / {FormatTime(len)}";
    }

    // =====================================================================  layout helpers

    /// <summary>Positions a rect inside the panel using top-left coordinates.</summary>
    private static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x + w * 0.5f - Width * 0.5f, Height * 0.5f - y - h * 0.5f);
    }

    private PokeButton Button(string name, PokeButton.Style style, UISpriteFactory.Icon? icon, string label,
        float x, float y, float w, float h, System.Action onPress, float fontSize)
    {
        var b = PokeButton.Create(_panel, name, style, icon, label, Mathf.Min(ButtonRadius, h * 0.5f), fontSize);
        Place(b.Rect, x, y, w, h);
        b.Pressed += Click;
        b.Pressed += onPress;
        return b;
    }

    private TMP_Text Text(string name, string text, float size, FontStyles style, Color color,
        float x, float y, float w, float h)
    {
        var rt = PokeButton.NewRect(name, _panel);
        Place(rt, x, y, w, h);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    private void SectionLabel(string text, float y)
    {
        var t = Text("Section_" + text, text, 10f, FontStyles.Bold, Accent, Pad, y, Width - Pad * 2f, 14f);
        t.characterSpacing = 12f;
    }

    private void Divider(float y)
    {
        var d = PokeButton.NewImage("Divider", _panel, null, new Color(1f, 1f, 1f, 0.07f));
        Place(d.rectTransform, Pad, y, Width - Pad * 2f, 1f);
    }

    private static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    private static float EaseOut(float t)
    {
        return 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
    }
}
