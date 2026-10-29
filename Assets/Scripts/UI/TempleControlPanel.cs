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
/// The bar above the panel (<see cref="PanelGrabBar"/>) can be grabbed to move it. Where it is
/// dropped becomes its new docking spot relative to the body, so it keeps following from there.
///
/// The "</>" button in the header opens a developer popup beside the panel with room tools: rescan
/// the room and show the room colliders (see <see cref="RoomColliders"/>).
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
    [Tooltip("Defaults to the TempleDemolition on the temple.")]
    private TempleDemolition _demolition;

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
    [Tooltip("Seconds the head must stay turned past the threshold before the body counts as turned.")]
    private float _turnDwell = 0.8f;

    [SerializeField]
    [Tooltip("Looking within this many degrees of the panel never counts as turning away.")]
    private float _lookAtPanelAngle = 35f;

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
    private const float Height = 384f;
    private const float Pad = 16f;
    private const float PanelRadius = 22f;
    private const float ButtonRadius = 14f;
    private static readonly Vector2 HandleSize = new Vector2(168f, 60f);
    private static readonly Vector2 GrabBarHitSize = new Vector2(150f, 34f);
    private static readonly Vector2 GrabBarPillSize = new Vector2(80f, 9f);
    private const float DevWidth = 200f;
    private const float DevHeight = 184f;
    private const float DevGap = 12f;

    // Limits on where the panel can be dragged to, as distance from the eyes.
    private const float MinDragDistance = 0.2f;
    private const float MaxDragDistance = 1.5f;

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
    private PokeButton _resetButton;
    private PokeButton _demolishButton;
    private PokeButton _devButton;
    private RectTransform _devPopup;
    private PokeButton[] _devButtons;
    private PokeButton _rescanButton;
    private PokeButton _collidersButton;
    private bool _devOpen;
    private PanelGrabBar _grabBar;
    private RectTransform _progressFill;
    private TMP_Text _timeText;
    private AudioSource _audio;
    private Coroutine _savedFeedback;

    // Placement state
    private bool _collapsed;
    private float _expand;          // 0 = handle, 1 = panel
    private float _bodyYaw;
    private bool _turning;
    private float _turnTimer;
    private float _eyeHeight;
    private Vector3 _velocity;
    private float _initTimer;
    private Vector3 _dockOffset;    // panel position in the body frame, relative to the filtered eyes
    private bool _wasGrabbed;

    private void Awake()
    {
        _dockOffset = new Vector3((int)_side * _sideOffset, -_dropBelowEyes, _forwardOffset);

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
        if (_demolition == null && _temple != null)
        {
            _demolition = _temple.GetComponent<TempleDemolition>();
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
        BuildDevPopup();
        BuildHandle();

        canvasGo.SetActive(true);
    }

    private void BuildPanel()
    {
        _panel = PokeButton.NewRect("Panel", _canvasRect);
        _panel.sizeDelta = new Vector2(Width, Height);
        _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();
        Backdrop(_panel);

        // ---- Header
        Text("Title", "Temple MR", 19f, FontStyles.Bold, TextPrimary, Pad, 18f, 120f, 24f);
        Text("Subtitle", "Control panel", 11.5f, FontStyles.Normal, TextSecondary, Pad, 41f, 120f, 16f);
        _devButton = Button("Developer", PokeButton.Style.Ghost, UISpriteFactory.Icon.Code, null,
            Width - Pad - 36f - 8f - 36f, 18f, 36f, 36f, ToggleDevPopup, 13f);
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
        _resetButton = Button("ResetTemple", PokeButton.Style.Secondary, UISpriteFactory.Icon.Home, "Reset temple",
            Pad, 208f, Width - Pad * 2f, 48f, () => _temple?.ResetTemple(), 15f);
        _setPositionButton = Button("SetTemplePosition", PokeButton.Style.Secondary, UISpriteFactory.Icon.Pin, SetPositionLabel,
            Pad, 264f, Width - Pad * 2f, 48f, SetTemplePosition, 15f);
        _demolishButton = Button("Demolish", PokeButton.Style.Danger, UISpriteFactory.Icon.Crack, "Demolish temple",
            Pad, 320f, Width - Pad * 2f, 48f, () => _demolition?.Toggle(), 15f);

        _panelButtons = new[] { _devButton, collapse, _playButton, _resetButton, _setPositionButton, _demolishButton };
        foreach (var b in _panelButtons)
        {
            b.BuildInteraction();
        }

        // ---- Grab bar floating just above the top edge: pick it up to move the panel.
        _grabBar = PanelGrabBar.Create(_panel, transform, GrabBarHitSize, GrabBarPillSize);
        Place(_grabBar.Rect, (Width - GrabBarHitSize.x) * 0.5f, -GrabBarHitSize.y - 4f, GrabBarHitSize.x, GrabBarHitSize.y);
        _grabBar.BuildInteraction();
    }

    /// <summary>Developer tools in a popup beside the panel, on the side nearer the body.</summary>
    private void BuildDevPopup()
    {
        _devPopup = PokeButton.NewRect("DeveloperPopup", _canvasRect);
        _devPopup.sizeDelta = new Vector2(DevWidth, DevHeight);
        float x = -(int)_side * (Width + DevWidth) * 0.5f - (int)_side * DevGap;
        _devPopup.anchoredPosition = new Vector2(x, (Height - DevHeight) * 0.5f);
        Backdrop(_devPopup);

        Text("Title", "Developer", 15f, FontStyles.Bold, TextPrimary, Pad, 18f, 120f, 22f, _devPopup);
        var close = Button("Close", PokeButton.Style.Ghost, UISpriteFactory.Icon.Close, null,
            DevWidth - Pad - 32f, 14f, 32f, 32f, ToggleDevPopup, 12f, _devPopup);

        Divider(56f, _devPopup);
        SectionLabel("ROOM", 68f, _devPopup);
        _rescanButton = Button("RescanRoom", PokeButton.Style.Secondary, UISpriteFactory.Icon.Scan, "Rescan room",
            Pad, 86f, DevWidth - Pad * 2f, 40f, RoomColliders.Rescan, 13.5f, _devPopup);
        _collidersButton = Button("ShowColliders", PokeButton.Style.Secondary, UISpriteFactory.Icon.Eye, "Show colliders",
            Pad, 132f, DevWidth - Pad * 2f, 40f,
            () => RoomColliders.CollidersVisible = !RoomColliders.CollidersVisible, 13.5f, _devPopup);

        _devButtons = new[] { close, _rescanButton, _collidersButton };
        foreach (var b in _devButtons)
        {
            b.BuildInteraction();
        }
    }

    /// <summary>Drop shadow, rim, background and accent strip shared by the panel and popup.</summary>
    private static void Backdrop(RectTransform parent)
    {
        var shadow = PokeButton.NewImage("Shadow", parent, UISpriteFactory.Shadow, new Color(0f, 0f, 0f, 0.45f));
        PokeButton.Stretch(shadow.rectTransform, -18f);
        shadow.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / (PanelRadius + 18f);

        var rim = PokeButton.NewImage("Rim", parent, UISpriteFactory.Rounded, new Color(1f, 1f, 1f, 0.10f));
        PokeButton.Stretch(rim.rectTransform, -1.5f);
        rim.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / (PanelRadius + 1.5f);

        var bg = PokeButton.NewImage("Background", parent, UISpriteFactory.Rounded, PanelColor);
        PokeButton.Stretch(bg.rectTransform);
        bg.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / PanelRadius;

        // Accent strip along the top edge.
        var strip = PokeButton.NewImage("AccentStrip", parent, UISpriteFactory.Rounded, Accent);
        Place(strip.rectTransform, parent.sizeDelta.x * 0.5f - 24f, 7f, 48f, 4f);
        strip.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / 2f;
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
        // Nothing sensible to save while the temple is lying in pieces.
        if (_temple == null || (_demolition != null && _demolition.IsDemolished))
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

    private void ToggleDevPopup()
    {
        _devOpen = !_devOpen;
        ApplyExpand();
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

        // Filter eye height so nodding / bobbing doesn't make the panel bounce.
        _eyeHeight = Mathf.Lerp(_eyeHeight, _head.position.y, 1f - Mathf.Exp(-2f * dt));

        if (_grabBar.IsGrabbed && !snapping)
        {
            // Held: follow the hand directly. Body yaw is frozen so turning while carrying it doesn't fight.
            transform.position = Vector3.Lerp(transform.position, ClampToReach(_grabBar.TargetPosition),
                1f - Mathf.Exp(-25f * dt));
            _velocity = Vector3.zero;
            _turning = false;
            _wasGrabbed = true;
            FaceHead(false, dt);
            return;
        }
        if (_wasGrabbed)
        {
            // Dropped: re-dock at this spot, relative to the body, so it keeps following from here.
            _wasGrabbed = false;
            _turnTimer = 0f;
            Vector3 eyes = new Vector3(_head.position.x, _eyeHeight, _head.position.z);
            _dockOffset = Quaternion.Inverse(Quaternion.Euler(0f, _bodyYaw, 0f)) * (transform.position - eyes);
        }

        // Lazy body yaw: ignore normal glancing, follow a real turn until nearly re-centred.
        // A turn only counts once it is held for a moment, and never while looking at the panel:
        // otherwise a panel placed off to the side runs away from the user as they turn to look at it.
        float delta = Mathf.DeltaAngle(_bodyYaw, headYaw);
        if (!_turning)
        {
            bool turnedAway = Mathf.Abs(delta) > _turnThreshold && !IsLookingAtPanel(headYaw);
            _turnTimer = turnedAway ? _turnTimer + dt : 0f;
            if (_turnTimer > _turnDwell)
            {
                _turning = true;
                _turnTimer = 0f;
            }
        }
        if (_turning)
        {
            _bodyYaw += delta * (1f - Mathf.Exp(-5f * dt));
            if (Mathf.Abs(delta) < 4f)
            {
                _turning = false;
            }
        }

        Quaternion body = Quaternion.Euler(0f, _bodyYaw, 0f);
        Vector3 eyePos = new Vector3(_head.position.x, _eyeHeight, _head.position.z);
        Vector3 target = eyePos + body * _dockOffset;

        transform.position = snapping
            ? target
            : Vector3.SmoothDamp(transform.position, target, ref _velocity, _smoothTime, Mathf.Infinity, dt);

        FaceHead(snapping, dt);
    }

    private void FaceHead(bool snap, float dt)
    {
        // Face the eyes. The canvas front faces -Z, so +Z points away from the viewer.
        Vector3 toPanel = transform.position - _head.position;
        if (toPanel.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
            transform.rotation = snap ? look : Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-8f * dt));
        }
    }

    /// <summary>Keeps a dragged position within a sensible distance of the eyes.</summary>
    private Vector3 ClampToReach(Vector3 position)
    {
        Vector3 fromEyes = position - _head.position;
        float d = fromEyes.magnitude;
        if (d < 0.0001f)
        {
            return _head.position + _head.forward * MinDragDistance;
        }
        return _head.position + fromEyes * (Mathf.Clamp(d, MinDragDistance, MaxDragDistance) / d);
    }

    private bool IsLookingAtPanel(float headYaw)
    {
        Vector3 toPanel = transform.position - _head.position;
        float panelYaw = Mathf.Atan2(toPanel.x, toPanel.z) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(headYaw, panelYaw)) < _lookAtPanelAngle;
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
        _grabBar.SetInteractable(!_collapsed);

        // The popup stays open across a collapse, but only shows with the panel.
        bool devVisible = _devOpen && !_collapsed;
        foreach (var b in _devButtons)
        {
            b.SetInteractable(devVisible);
        }
        _devButton.Toggled = _devOpen;
        _devPopup.localScale = _panel.localScale;
        _devPopup.gameObject.SetActive(devVisible && k > 0.001f);

        _panel.gameObject.SetActive(k > 0.001f);
        _handle.gameObject.SetActive(k < 0.999f);
    }

    private void UpdateStatus()
    {
        if (_demolition != null)
        {
            bool down = _demolition.IsDemolished;
            _demolishButton.SetLabel(!_demolition.IsReady ? "Preparing..." : (down ? "Rebuild temple" : "Demolish temple"));
            _demolishButton.SetIcon(down ? UISpriteFactory.Icon.Home : UISpriteFactory.Icon.Crack);
            // Nothing sensible to save while the temple is lying in pieces.
            _setPositionButton.SetEnabled(!down);
            _resetButton.SetEnabled(!down);
        }

        if (_devOpen)
        {
            bool busy = RoomColliders.IsBusy;
            _rescanButton.SetLabel(busy ? "Scanning..." : "Rescan room");
            _rescanButton.SetEnabled(!busy);
            bool shown = RoomColliders.CollidersVisible;
            _collidersButton.SetLabel(shown ? "Hide colliders" : "Show colliders");
            _collidersButton.Toggled = shown;
        }

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

    /// <summary>Positions a rect inside its parent (the panel or popup) using top-left coordinates.</summary>
    private static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        Vector2 parent = ((RectTransform)rt.parent).sizeDelta;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x + w * 0.5f - parent.x * 0.5f, parent.y * 0.5f - y - h * 0.5f);
    }

    private PokeButton Button(string name, PokeButton.Style style, UISpriteFactory.Icon? icon, string label,
        float x, float y, float w, float h, System.Action onPress, float fontSize, RectTransform parent = null)
    {
        var b = PokeButton.Create(parent != null ? parent : _panel, name, style, icon, label,
            Mathf.Min(ButtonRadius, h * 0.5f), fontSize);
        Place(b.Rect, x, y, w, h);
        b.Pressed += Click;
        b.Pressed += onPress;
        return b;
    }

    private TMP_Text Text(string name, string text, float size, FontStyles style, Color color,
        float x, float y, float w, float h, RectTransform parent = null)
    {
        var rt = PokeButton.NewRect(name, parent != null ? parent : _panel);
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

    private void SectionLabel(string text, float y, RectTransform parent = null)
    {
        parent = parent != null ? parent : _panel;
        var t = Text("Section_" + text, text, 10f, FontStyles.Bold, Accent, Pad, y, parent.sizeDelta.x - Pad * 2f, 14f, parent);
        t.characterSpacing = 12f;
    }

    private void Divider(float y, RectTransform parent = null)
    {
        parent = parent != null ? parent : _panel;
        var d = PokeButton.NewImage("Divider", parent, null, new Color(1f, 1f, 1f, 0.07f));
        Place(d.rectTransform, Pad, y, parent.sizeDelta.x - Pad * 2f, 1f);
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
