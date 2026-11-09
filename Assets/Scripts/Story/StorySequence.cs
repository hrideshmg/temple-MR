using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Turns the narration into a staged, educational sequence around the temple.
///
/// Everything is keyed to the narration's playback position (<see cref="StoryAudioController.PlaybackTime"/>),
/// not to timers, so pausing freezes the sequence, resuming carries on, and in multiplayer every
/// headset shows the same moment because <see cref="NetworkedStory"/> already keeps the audio in step.
///
/// The sequence (default timings, matched to MuvarStory.mp3):
/// - 0-8 s: the room darkens and the temple goes dark, then a warm light rises on it while the title
///   card shows the site's name.
/// - 25.8 s ("neglect, invasion, disasters"): a rumble, and the temple collapses.
/// - 36.3 s (the pause before the architecture): the rubble flies back and the temple stands again.
/// - 42-58 s: as the narrator names each part (adhisthana, bhitti, devakoshthas, vimana) it lights up
///   with a glowing outline, a scan line and a label explaining it. The labels stay until 68.5 s.
/// - 74.2 s ("revive and restore"): a golden ring rises up the whole temple.
/// - 85-89 s: the room brightens again.
/// Captions of the narration run throughout.
///
/// Visual and sound cues run on every headset. Collapse and rebuild change shared state, so only
/// the host (or a single player without a session) triggers them; guests follow through
/// <see cref="NetworkedDemolition"/>.
///
/// Caption and cue times were estimated from the pauses in the narration audio; adjust them in the
/// inspector if a line or highlight lands early or late. Attach next to the <see cref="StoryAudioController"/>.
/// </summary>
[RequireComponent(typeof(StoryAudioController))]
public class StorySequence : MonoBehaviour
{
    [Serializable]
    public class Caption
    {
        public float Start;
        public float End;
        [TextArea(1, 3)] public string Text;

        public Caption(float start, float end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }
    }

    [Serializable]
    public class PartCue
    {
        [Tooltip("Playback time (s) when the part is named and lights up.")]
        public float Time;

        [Tooltip("Seconds the scan line takes to sweep up through the part.")]
        public float ScanTime = 1.2f;

        [Tooltip("Seconds the part stays brightly lit before dimming to a faint outline.")]
        public float Hold = 5f;

        public TempleAnatomy.Part Part;
    }

    [Header("References (found automatically when empty)")]
    [SerializeField] private TempleDemolition _demolition;
    [SerializeField] private OVRPassthroughLayer _passthrough;

    [Header("Room and light")]
    [SerializeField]
    [Tooltip("How far the real room (passthrough) dims during the story, 0-1.")]
    [Range(0f, 1f)] private float _roomDim = 0.75f;

    [SerializeField]
    [Tooltip("Colour of the 'sunrise' light on the temple.")]
    private Color _warmLight = new Color(1f, 0.78f, 0.52f);

    [Header("Timeline (seconds of narration)")]
    [SerializeField] private float _darkenEnd = 2f;
    [SerializeField] private float _sunriseStart = 3.5f;
    [SerializeField] private float _sunriseEnd = 8f;
    [SerializeField] private float _titleStart = 2.5f;
    [SerializeField] private float _titleEnd = 9.5f;
    [SerializeField] private float _collapseTime = 25.8f;
    [SerializeField] private float _rebuildTime = 36.3f;
    [SerializeField] private float _labelsOffTime = 68.5f;
    [SerializeField] private float _restoreTime = 74.2f;
    [SerializeField] private float _restoreDuration = 4f;
    [SerializeField] private float _brightenStart = 85f;
    [SerializeField] private float _brightenEnd = 88.6f;

    [Header("Title card")]
    [SerializeField] private string _title = "MOOVAR KOVIL";
    [SerializeField] private string _titleSubtitle = "Kodumbalur, Tamil Nadu  ·  Early Chola, 9th century CE";

    [Header("Temple parts")]
    [SerializeField] private PartCue[] _parts = DefaultParts();

    [Header("Captions")]
    [SerializeField] private bool _showCaptions = true;
    [SerializeField] private Caption[] _captions = DefaultCaptions();

    private StoryAudioController _story;
    private NetworkedDemolition _networkedDemolition;
    private TempleAnatomy _anatomy;
    private Transform _overlay;
    private AudioSource _sfx;
    private StoryCard _titleCard;
    private StoryCard _captionCard;
    private TMP_Text _captionText;
    private int _captionIndex = -1;
    private Vector3 _captionVelocity;
    private bool _captionPlaced;

    private float _lastTime;
    private float _ambience;          // 0 = room as normal, 1 = full story lighting
    private float _overlayAlpha = 1f;

    // Lighting state captured before the story first touches it.
    private Light[] _lights;
    private float[] _lightIntensity;
    private Color[] _lightColor;
    private SphericalHarmonicsL2 _ambientProbe;
    private Color _ambientLight;
    private bool _lightingCaptured;
    private bool _passthroughDimmed;
    private Vector3 _lastPassthrough;

    // =====================================================================  setup

    private void Awake()
    {
        _story = GetComponent<StoryAudioController>();
    }

    private void Start()
    {
        if (_demolition == null)
        {
            _demolition = FindAnyObjectByType<TempleDemolition>();
        }
        if (_passthrough == null)
        {
            _passthrough = FindAnyObjectByType<OVRPassthroughLayer>();
        }
        if (_demolition == null)
        {
            Debug.LogWarning("StorySequence: no TempleDemolition in the scene; the story will play without visuals.", this);
            return;
        }
        _networkedDemolition = _demolition.GetComponent<NetworkedDemolition>();

        // The overlay is its own root (not under the temple), so line widths and cards stay in metres.
        _overlay = new GameObject("Story Overlay").transform;
        _anatomy = _overlay.gameObject.AddComponent<TempleAnatomy>();
        var parts = new TempleAnatomy.Part[_parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = _parts[i].Part;
        }
        _anatomy.Init(_demolition.transform, parts);

        _sfx = _overlay.gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.spatialBlend = 0.85f;
        _sfx.minDistance = 0.6f;
        _sfx.maxDistance = 10f;
        _sfx.rolloffMode = AudioRolloffMode.Linear;

        _titleCard = new StoryCard("Title", _overlay, 640f, 0.0009f, false, false, 0f, 0f, TextAnchor.MiddleCenter);
        _titleCard.AddText(_title, 40f, FontStyles.Bold, StoryCard.Gold, TextAlignmentOptions.Center, 14f);
        _titleCard.AddText(_titleSubtitle, 13f, FontStyles.Normal, StoryCard.TextPrimary, TextAlignmentOptions.Center, 2f);

        _captionCard = new StoryCard("Caption", _overlay, 440f, 0.0007f, true, false, 18f, 12f, TextAnchor.MiddleCenter);
        _captionText = _captionCard.AddText("", 17f, FontStyles.Normal, StoryCard.TextPrimary, TextAlignmentOptions.Center);

        // Prepare the sounds now rather than on the first cue.
        _ = StorySounds.Rumble;
        _ = StorySounds.Shimmer;
        _ = StorySounds.Chime;
    }

    private void OnDestroy()
    {
        RestoreLighting();
        if (_overlay != null)
        {
            Destroy(_overlay.gameObject);
        }
    }

    // =====================================================================  per frame

    private void Update()
    {
        if (_anatomy == null)
        {
            return;
        }
        float t = _story.PlaybackTime;
        bool playing = _story.IsPlaying;
        bool active = playing || t > 0.05f;

        // One-off cues fire when playback passes them; a seek (a jump of more than a second and a half,
        // e.g. joining part-way through) skips them instead.
        if (playing && t > _lastTime && t - _lastTime < 1.5f)
        {
            FireCues(_lastTime, t);
        }
        _lastTime = t;

        // The room only stays dark while the story is actually playing.
        _ambience = Mathf.MoveTowards(_ambience, playing ? 1f : 0f, Time.deltaTime / 0.8f);
        UpdateLighting(t);

        _overlay.position = _anatomy.IsReady ? _anatomy.BaseCentre : _demolition.transform.position;
        UpdateOverlay(t, active);
    }

    private void FireCues(float from, float to)
    {
        bool Crossed(float time) => from < time && to >= time;

        if (Crossed(_collapseTime))
        {
            Play(StorySounds.Rumble, 1f);
            if (DrivesSharedState && _demolition.IsReady && !_demolition.IsDemolished)
            {
                ToggleDemolition();
            }
        }
        if (Crossed(_rebuildTime))
        {
            Play(StorySounds.Shimmer, 0.55f);
            if (DrivesSharedState && _demolition.IsDemolished)
            {
                ToggleDemolition();
            }
        }
        foreach (var cue in _parts)
        {
            if (Crossed(cue.Time))
            {
                Play(StorySounds.Chime, 0.3f);
            }
        }
        if (Crossed(_restoreTime))
        {
            Play(StorySounds.Shimmer, 0.45f);
        }
    }

    /// <summary>Offline, or the host of a session.</summary>
    private bool DrivesSharedState => _networkedDemolition == null || !_networkedDemolition.IsSpawned || _networkedDemolition.IsServer;

    private void ToggleDemolition()
    {
        if (_networkedDemolition != null && _networkedDemolition.IsSpawned)
        {
            _networkedDemolition.RequestToggle();
        }
        else
        {
            _demolition.Toggle();
        }
    }

    private void Play(AudioClip clip, float volume)
    {
        if (_sfx != null && clip != null)
        {
            _sfx.PlayOneShot(clip, volume);
        }
    }

    // =====================================================================  overlay

    private void UpdateOverlay(float t, bool active)
    {
        Camera cam = Camera.main;
        Vector3 eyes = cam != null ? cam.transform.position : Vector3.zero;

        // Part highlights hide while the temple lies in pieces.
        bool standing = !_demolition.IsDemolished && !_demolition.IsReassembling;
        _overlayAlpha = Mathf.MoveTowards(_overlayAlpha, standing ? 1f : 0f, Time.deltaTime / 0.3f);
        _anatomy.SetMasterAlpha(_overlayAlpha);

        for (int i = 0; i < _parts.Length; i++)
        {
            var cue = _parts[i];
            if (!active || t < cue.Time)
            {
                _anatomy.SetPart(i, 0f, 0f, -1f);
                continue;
            }
            float shown = Smooth(cue.Time, cue.Time + 0.5f, t) * (1f - Smooth(_labelsOffTime, _labelsOffTime + 0.8f, t));
            float bright = 1f - Smooth(cue.Time + cue.Hold, cue.Time + cue.Hold + 1f, t);
            float scan = cue.ScanTime > 0f ? (t - cue.Time) / cue.ScanTime : -1f;
            _anatomy.SetPart(i, shown * Mathf.Lerp(0.2f, 1f, bright), shown, scan <= 1f ? scan : -1f);
        }

        float sweep = (t - _restoreTime) / Mathf.Max(_restoreDuration, 0.01f);
        bool sweeping = active && sweep >= 0f && sweep <= 1f;
        _anatomy.SetSweep(sweeping ? sweep : -1f, sweeping ? Mathf.Sin(sweep * Mathf.PI) * 0.5f + 0.5f * Mathf.Clamp01(sweep * 8f) * (1f - sweep) : 0f);

        if (!_anatomy.IsReady || cam == null)
        {
            _titleCard.Alpha = 0f;
            _captionCard.Alpha = 0f;
            return;
        }

        // Title card floats above the temple.
        float title = active ? Smooth(_titleStart, _titleStart + 1f, t) * (1f - Smooth(_titleEnd - 1f, _titleEnd, t)) : 0f;
        _titleCard.Alpha = title;
        if (title > 0f)
        {
            _titleCard.Place(_anatomy.TopCentre + Vector3.up * (0.06f + _anatomy.WorldHeight * 0.1f), eyes);
        }

        UpdateCaption(t, active, eyes);
    }

    /// <summary>
    /// Captions sit like subtitles under the temple: in the direction of the temple, a little nearer
    /// than it, and just below its base as seen from the eyes, but never so far below the horizon
    /// that they drop out of view (a tall temple close by) or so high that they cover it.
    /// </summary>
    private void UpdateCaption(float t, bool active, Vector3 eyes)
    {
        int index = -1;
        if (active && _showCaptions)
        {
            for (int i = 0; i < _captions.Length; i++)
            {
                if (t >= _captions[i].Start && t < _captions[i].End)
                {
                    index = i;
                    break;
                }
            }
        }
        if (index != _captionIndex && index >= 0)
        {
            _captionText.text = _captions[index].Text;
        }
        _captionIndex = index;

        float alpha = 0f;
        if (index >= 0)
        {
            var c = _captions[index];
            alpha = Smooth(c.Start, c.Start + 0.15f, t) * (1f - Smooth(c.End - 0.15f, c.End, t));
        }
        _captionCard.Alpha = alpha;
        if (alpha <= 0f)
        {
            return;
        }

        Vector3 toBase = _anatomy.BaseCentre - eyes;
        Vector3 flat = new Vector3(toBase.x, 0f, toBase.z);
        float horizontal = flat.magnitude;
        if (horizontal < 1e-3f)
        {
            return;
        }
        float baseAngle = Mathf.Atan2(toBase.y, horizontal) * Mathf.Rad2Deg;
        float pitch = Mathf.Clamp(baseAngle - 5f, -28f, -8f) * Mathf.Deg2Rad;
        float d = Mathf.Clamp(horizontal * 0.8f, 0.45f, 1.4f);
        Vector3 target = eyes + (flat / horizontal * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * d;
        if (!_captionPlaced)
        {
            _captionCard.Transform.position = target;
            _captionPlaced = true;
        }
        Vector3 position = Vector3.SmoothDamp(_captionCard.Transform.position, target, ref _captionVelocity, 0.3f);
        // Same apparent size at any distance.
        _captionCard.Place(position, eyes, d / 0.8f);
    }

    // =====================================================================  lighting

    private void UpdateLighting(float t)
    {
        float w = _ambience;
        if (w <= 0f)
        {
            RestoreLighting();
            return;
        }
        CaptureLighting();

        // Room (passthrough): dark at the start, a little lighter once the temple is lit, darker
        // again for a moment as the temple falls, back to normal at the end.
        float collapse = Smooth(_collapseTime, _collapseTime + 0.6f, t) * (1f - Smooth(_collapseTime + 0.6f, _collapseTime + 5f, t));
        float end = 1f - Smooth(_brightenStart, _brightenEnd, t);
        float dim = Smooth(0f, _darkenEnd, t) * Mathf.Lerp(1f, 0.75f, Smooth(_sunriseStart, _sunriseEnd, t)) * end;
        dim = Mathf.Clamp01(dim + collapse * 0.25f * end) * _roomDim * w;
        SetPassthrough(new Vector3(-dim, dim * 0.15f, -dim * 0.8f));

        // Temple light: out at the start, then a warm sunrise; a golden lift when it's restored.
        float restore = Smooth(_restoreTime, _restoreTime + 1f, t) * (1f - Smooth(_restoreTime + _restoreDuration, _restoreTime + _restoreDuration + 2f, t));
        float level = Mathf.Lerp(1f, 0.08f, Smooth(0f, _darkenEnd, t));
        level = Mathf.Lerp(level, 1.15f, Smooth(_sunriseStart, _sunriseEnd, t));
        level = Mathf.Lerp(level, 1f, Smooth(_brightenStart, _brightenEnd, t));
        level += restore * 0.25f;
        // A flicker as it falls.
        level *= 1f - collapse * 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 47f) * Mathf.Sin(t * 31f));
        float warmth = Mathf.Clamp01(Smooth(_sunriseStart, _sunriseEnd, t) * 0.55f * end + restore * 0.35f);

        float k = Mathf.Lerp(1f, level, w);
        warmth *= w;
        for (int i = 0; i < _lights.Length; i++)
        {
            if (_lights[i] == null)
            {
                continue;
            }
            _lights[i].intensity = _lightIntensity[i] * k;
            _lights[i].color = Color.Lerp(_lightColor[i], _lightColor[i] * _warmLight, warmth);
        }
        RenderSettings.ambientProbe = _ambientProbe * Mathf.Lerp(1f, Mathf.Min(level, 1f), w);
        RenderSettings.ambientLight = _ambientLight * Mathf.Lerp(1f, Mathf.Min(level, 1f), w);
    }

    private void CaptureLighting()
    {
        if (_lightingCaptured)
        {
            return;
        }
        var all = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        var directional = new System.Collections.Generic.List<Light>();
        foreach (var l in all)
        {
            if (l.type == LightType.Directional)
            {
                directional.Add(l);
            }
        }
        _lights = directional.ToArray();
        _lightIntensity = new float[_lights.Length];
        _lightColor = new Color[_lights.Length];
        for (int i = 0; i < _lights.Length; i++)
        {
            _lightIntensity[i] = _lights[i].intensity;
            _lightColor[i] = _lights[i].color;
        }
        _ambientProbe = RenderSettings.ambientProbe;
        _ambientLight = RenderSettings.ambientLight;
        _lightingCaptured = true;
    }

    private void RestoreLighting()
    {
        if (_lightingCaptured)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].intensity = _lightIntensity[i];
                    _lights[i].color = _lightColor[i];
                }
            }
            RenderSettings.ambientProbe = _ambientProbe;
            RenderSettings.ambientLight = _ambientLight;
            _lightingCaptured = false;
        }
        if (_passthroughDimmed && _passthrough != null)
        {
            _passthrough.DisableColorMap();
        }
        _passthroughDimmed = false;
    }

    /// <param name="bcs">Brightness, contrast and saturation, each -1..1.</param>
    private void SetPassthrough(Vector3 bcs)
    {
        if (_passthrough == null)
        {
            return;
        }
        if (_passthroughDimmed && (bcs - _lastPassthrough).sqrMagnitude < 1e-6f)
        {
            return;
        }
        _passthrough.SetBrightnessContrastSaturation(bcs.x, bcs.y, bcs.z);
        _lastPassthrough = bcs;
        _passthroughDimmed = true;
    }

    private static float Smooth(float from, float to, float t)
    {
        if (to <= from)
        {
            return t >= to ? 1f : 0f;
        }
        return Mathf.SmoothStep(0f, 1f, (t - from) / (to - from));
    }

    // =====================================================================  default content

    private static PartCue[] DefaultParts()
    {
        return new[]
        {
            new PartCue
            {
                Time = 42.6f, ScanTime = 1.2f, Hold = 5f,
                Part = new TempleAnatomy.Part
                {
                    Title = "Adhisthana", Subtitle = "The base",
                    Body = "The moulded stone plinth the shrine stands on, here in the padma-pushkala style, with bands of lotus-petal mouldings.",
                    Kind = TempleAnatomy.PartKind.Band, From = 0f, To = 0.14f, Side = 1,
                },
            },
            new PartCue
            {
                Time = 44.7f, ScanTime = 1.2f, Hold = 5f,
                Part = new TempleAnatomy.Part
                {
                    Title = "Bhitti", Subtitle = "The wall",
                    Body = "The sanctum wall, stepped out in projections that break up the light and frame the niches.",
                    Kind = TempleAnatomy.PartKind.Band, From = 0.14f, To = 0.34f, Side = -1,
                },
            },
            new PartCue
            {
                Time = 49.9f, ScanTime = 0f, Hold = 6f,
                Part = new TempleAnatomy.Part
                {
                    Title = "Devakoshtha", Subtitle = "Wall niches",
                    Body = "Niches set into the outer walls to hold carved images of deities, one on each side except the entrance.",
                    Kind = TempleAnatomy.PartKind.Niches, From = 0.17f, To = 0.31f, Side = 1,
                },
            },
            new PartCue
            {
                Time = 52.6f, ScanTime = 3f, Hold = 6f,
                Part = new TempleAnatomy.Part
                {
                    Title = "Vimana", Subtitle = "The tower",
                    Body = "The tower over the sanctum. Here it rises in two storeys (talas) of carved pavilions to the domed shikhara and its kalasha finial.",
                    Kind = TempleAnatomy.PartKind.Band, From = 0.4f, To = 1f, Side = -1,
                },
            },
        };
    }

    private static Caption[] DefaultCaptions()
    {
        return new[]
        {
            new Caption(1.9f, 5.2f, "Welcome to the enchanting Mixed Reality immersive experience"),
            new Caption(5.2f, 8.8f, "of Muvar Koil temple in Tamil Nadu."),
            new Caption(8.9f, 12.5f, "This sacred site boasts three Siva temples"),
            new Caption(12.5f, 14.8f, "dating back to the ninth century,"),
            new Caption(14.8f, 19.8f, "standing as a timeless testament to early Chola architecture."),
            new Caption(20f, 25.6f, "However, the passage of time has not been kind to this architectural marvel."),
            new Caption(25.6f, 29.7f, "As a result of neglect, invasion, disasters,"),
            new Caption(29.7f, 32.5f, "and the relentless impact of climate change,"),
            new Caption(32.5f, 36.6f, "only two of the three temples have managed to survive."),
            new Caption(37.7f, 44.3f, "The temple's foundation showcases a captivating padma-pushkala-style adhisthana,"),
            new Caption(44.3f, 51.5f, "while the bhitti, or wall, is adorned with exquisite projections and devakoshthas niches."),
            new Caption(51.8f, 56.4f, "As your gaze ascends, the two-story vimana unfolds"),
            new Caption(56.4f, 62f, "with intricate carvings, each niche revealing the mastery of Chola art."),
            new Caption(62f, 69f, "These sculptures are celebrated as some of the finest specimens from that era."),
            new Caption(69f, 73.3f, "Embark on a captivating augmented reality expedition"),
            new Caption(73.3f, 78.6f, "as we revive and restore Muvar Koil, preserving its ancient charm."),
            new Caption(78.6f, 83.1f, "Immerse yourself in the rich history and interact with the temple,"),
            new Caption(83.1f, 88.4f, "unlocking a unique journey through time."),
        };
    }
}
