using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

/// <summary>
/// Rebuild the fallen temple piece by piece.
///
/// After an architectural collapse (<see cref="TempleDemolition.IsArchitectural"/>) every piece is a
/// real part of the temple: the platform, base, walls, cornice, storeys, dome and finial, plus Nandi
/// and the two door guardians (<see cref="TempleParts"/>). Pick one up and its slot
/// lights up where it belongs, gold if it can go in now, grey if something it rests on is still
/// missing. Let go of it near the slot, roughly the right way round, and it settles into place, with
/// a card saying what the part is. Parts go back in building order (walls on the base, the cornice
/// on all four walls, and so on). Placing the last one restores the standing temple.
///
/// While nothing is held, the slots that can be filled next glow faintly as a hint.
///
/// In multiplayer the host decides every placement: releases on the host are checked here, and
/// guests' throws reach <see cref="TrySnap"/> through <see cref="NetworkedDemolition"/>, which also
/// shares the set of placed pieces (<see cref="PlacedMask"/>) with every headset.
///
/// Added automatically by <see cref="TempleDemolition"/> in the architectural style.
/// </summary>
[RequireComponent(typeof(TempleDemolition))]
public class TemplePuzzle : MonoBehaviour
{
    [SerializeField]
    [Tooltip("How close a released piece must be to its slot, as a fraction of the temple's height.")]
    private float _snapDistance = 0.12f;

    [SerializeField]
    [Tooltip("Snap distance limits in metres, whatever the temple's size.")]
    private Vector2 _snapDistanceLimits = new Vector2(0.05f, 0.15f);

    [SerializeField]
    [Tooltip("How far (degrees) a released piece may be turned from its place and still snap in.")]
    private float _snapAngle = 40f;

    [SerializeField]
    [Tooltip("Seconds a piece takes to settle into its slot.")]
    private float _settleTime = 0.3f;

    [SerializeField]
    [Tooltip("Seconds the card about a placed part stays up.")]
    private float _infoTime = 7f;

    /// <summary>A piece was placed (index), on every headset.</summary>
    public event Action<int> PiecePlaced;

    public int PlacedCount { get; private set; }
    public int TotalCount => _demolition != null && _demolition.IsReady ? _demolition.PieceCount : 0;

    /// <summary>Bit i set: piece i is in place.</summary>
    public ulong PlacedMask { get; private set; }

    /// <summary>True while the temple lies in pieces that can be put back.</summary>
    public bool IsActive => _demolition != null && _demolition.IsArchitectural && _demolition.IsReady
                            && _demolition.IsDemolished && !_demolition.IsReassembling;

    // Ring-like parts look the same from every side, so only their tilt has to be right.
    private static readonly HashSet<TempleParts.Id> RoundParts = new HashSet<TempleParts.Id>
    {
        TempleParts.Id.Hara, TempleParts.Id.UpperTala, TempleParts.Id.Griva, TempleParts.Id.Shikhara, TempleParts.Id.Stupi,
    };

    private TempleDemolition _demolition;
    private NetworkedDemolition _network;
    private bool _built;
    private Grabbable[] _grabbables;
    private bool[] _held;
    private GlowOutline[] _slots;
    private readonly Dictionary<int, int> _pieceOfPart = new Dictionary<int, int>();
    private Material _lineMaterial;
    private Transform _overlay;
    private StoryCard _infoCard;
    private TMP_Text _infoTitle;
    private TMP_Text _infoSubtitle;
    private TMP_Text _infoBody;
    private TMP_Text _infoProgress;
    private StoryCard _hintCard;
    private TMP_Text _hintText;
    private float _infoUntil;
    private float _hintUntil;
    private int _infoPiece = -1;
    private int _hintPiece = -1;
    private AudioSource _sfx;
    private bool _completeShown;

    // =====================================================================  setup

    private void Awake()
    {
        _demolition = GetComponent<TempleDemolition>();
        _network = GetComponent<NetworkedDemolition>();
        _demolition.DemolishedChanged += OnDemolishedChanged;
    }

    private void OnDestroy()
    {
        if (_demolition != null)
        {
            _demolition.DemolishedChanged -= OnDemolishedChanged;
        }
        if (_overlay != null)
        {
            Destroy(_overlay.gameObject);
        }
        if (_lineMaterial != null)
        {
            Destroy(_lineMaterial);
        }
    }

    private void Build()
    {
        _built = true;
        int count = _demolition.PieceCount;
        _grabbables = new Grabbable[count];
        _held = new bool[count];
        _slots = new GlowOutline[count];

        _overlay = new GameObject("Puzzle Overlay").transform;
        _lineMaterial = GlowOutline.CreateMaterial();
        for (int i = 0; i < count; i++)
        {
            _grabbables[i] = _demolition.GetPieceBody(i).GetComponent<Grabbable>();
            int part = _demolition.GetPiecePart(i);
            if (part >= 0)
            {
                _pieceOfPart[part] = i;
            }

            // The slot is drawn as the piece's bounding box at its home pose.
            Bounds b = _demolition.GetPieceBounds(i);
            Vector3 min = b.min, max = b.max;
            Vector3 C(float x, float y, float z) => new Vector3(x, y, z);
            var slot = new GlowOutline("Slot_" + i, _overlay, _lineMaterial);
            slot.AddLine(new[] { C(min.x, min.y, min.z), C(max.x, min.y, min.z), C(max.x, min.y, max.z), C(min.x, min.y, max.z) }, true);
            slot.AddLine(new[] { C(min.x, max.y, min.z), C(max.x, max.y, min.z), C(max.x, max.y, max.z), C(min.x, max.y, max.z) }, true);
            slot.AddLine(new[] { C(min.x, min.y, min.z), C(min.x, max.y, min.z) }, false);
            slot.AddLine(new[] { C(max.x, min.y, min.z), C(max.x, max.y, min.z) }, false);
            slot.AddLine(new[] { C(max.x, min.y, max.z), C(max.x, max.y, max.z) }, false);
            slot.AddLine(new[] { C(min.x, min.y, max.z), C(min.x, max.y, max.z) }, false);
            _slots[i] = slot;
        }

        _infoCard = new StoryCard("PuzzleInfo", _overlay, 250f, 0.0009f, true, true);
        _infoProgress = _infoCard.AddText("", 10f, FontStyles.Bold, StoryCard.TextSecondary, TextAlignmentOptions.TopLeft, 6f);
        _infoTitle = _infoCard.AddText("", 21f, FontStyles.Bold, StoryCard.Gold);
        _infoSubtitle = _infoCard.AddText("", 10.5f, FontStyles.Bold, StoryCard.TextSecondary, TextAlignmentOptions.TopLeft, 8f);
        _infoBody = _infoCard.AddText("", 12f, FontStyles.Normal, StoryCard.TextPrimary);

        _hintCard = new StoryCard("PuzzleHint", _overlay, 220f, 0.0009f, true, false, 12f, 8f);
        _hintText = _hintCard.AddText("", 12f, FontStyles.Normal, StoryCard.TextPrimary);

        _sfx = _overlay.gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.spatialBlend = 0.85f;
        _sfx.minDistance = 0.6f;
        _sfx.maxDistance = 10f;
        _sfx.rolloffMode = AudioRolloffMode.Linear;
    }

    private void OnDemolishedChanged(bool demolished)
    {
        if (demolished)
        {
            // A fresh collapse: everything is loose again.
            ResetPlaced();
            _completeShown = false;
            return;
        }
        // Standing again: celebrate if it was rebuilt by hand (on every headset), then start over.
        if (_built && PlacedCount > 0 && PlacedCount == TotalCount && !_completeShown)
        {
            _completeShown = true;
            ShowCompletion();
        }
        ResetPlaced();
    }

    private void ResetPlaced()
    {
        PlacedMask = 0;
        PlacedCount = 0;
        if (_held != null)
        {
            Array.Clear(_held, 0, _held.Length);
        }
    }

    // =====================================================================  rules

    /// <summary>Offline, or the host of a session: this headset decides placements.</summary>
    private bool HasAuthority => _network == null || !_network.IsSpawned || _network.IsServer;

    public bool IsPlaced(int index) => (PlacedMask & (1UL << index)) != 0;

    /// <summary>The parts this piece rests on that are not in place yet (empty when it can go in).</summary>
    public List<TempleParts.Id> MissingSupports(int index)
    {
        var missing = new List<TempleParts.Id>();
        int part = _demolition.GetPiecePart(index);
        if (part < 0)
        {
            return missing;
        }
        foreach (var need in TempleParts.Get((TempleParts.Id)part).Needs)
        {
            // A part the model doesn't have can't hold anything up.
            if (_pieceOfPart.TryGetValue((int)need, out int support) && !IsPlaced(support))
            {
                missing.Add(need);
            }
        }
        return missing;
    }

    private float SnapDistance => Mathf.Clamp(_demolition.WorldHeight * _snapDistance, _snapDistanceLimits.x, _snapDistanceLimits.y);

    /// <summary>Close enough to its slot, and turned roughly the right way.</summary>
    private bool IsNearSlot(int index)
    {
        Transform t = _demolition.GetPieceBody(index).transform;
        Pose home = _demolition.GetPieceHome(index);
        // Compare the bounds centres, not the pivots, so big pieces aren't judged by a far corner.
        Vector3 centre = _demolition.GetPieceBounds(index).center;
        Vector3 here = t.TransformPoint(centre);
        Vector3 there = home.position + home.rotation * Vector3.Scale(centre, t.lossyScale);
        if ((here - there).magnitude > SnapDistance)
        {
            return false;
        }
        int part = _demolition.GetPiecePart(index);
        if (part >= 0 && RoundParts.Contains((TempleParts.Id)part))
        {
            return Vector3.Angle(t.up, home.rotation * Vector3.up) <= _snapAngle;
        }
        return Quaternion.Angle(t.rotation, home.rotation) <= _snapAngle;
    }

    /// <summary>
    /// Host / offline: places the piece if it was let go near its slot and everything under it is in
    /// place. Returns true if it went in. Called for local releases, and for guests' throws by
    /// <see cref="NetworkedDemolition"/>.
    /// </summary>
    public bool TrySnap(int index)
    {
        if (!IsActive || !HasAuthority || index < 0 || index >= _demolition.PieceCount || IsPlaced(index))
        {
            return false;
        }
        if (!IsNearSlot(index))
        {
            return false;
        }
        var missing = MissingSupports(index);
        if (missing.Count > 0)
        {
            ShowHint(index, missing);
            return false;
        }
        Place(index, true);
        return true;
    }

    /// <summary>Guests: adopt the host's set of placed pieces.</summary>
    public void ApplyPlacedMask(ulong mask)
    {
        if (!_built || mask == PlacedMask)
        {
            return;
        }
        for (int i = 0; i < _demolition.PieceCount && i < 64; i++)
        {
            bool shouldBe = (mask & (1UL << i)) != 0;
            if (shouldBe && !IsPlaced(i))
            {
                Place(i, true);
            }
        }
    }

    private void Place(int index, bool animate)
    {
        Transform t = _demolition.GetPieceBody(index).transform;
        Vector3 fromPosition = t.position;
        Quaternion fromRotation = t.rotation;

        PlacedMask |= 1UL << index;
        PlacedCount++;
        _demolition.LockPiece(index);
        if (_hintPiece == index)
        {
            _hintUntil = 0f;
        }
        if (animate && _settleTime > 0f)
        {
            StartCoroutine(SettleRoutine(t, fromPosition, fromRotation, index));
        }

        Play(StorySounds.Chime, 0.45f);
        ShowInfo(index);
        PiecePlaced?.Invoke(index);

        if (HasAuthority && PlacedCount == TotalCount)
        {
            StartCoroutine(CompleteRoutine());
        }
    }

    /// <summary>Glides a just-placed piece from where it was let go into its slot.</summary>
    private IEnumerator SettleRoutine(Transform t, Vector3 fromPosition, Quaternion fromRotation, int index)
    {
        for (float time = 0f; time < _settleTime; time += Time.deltaTime)
        {
            if (!IsPlaced(index))
            {
                yield break;
            }
            Pose home = _demolition.GetPieceHome(index);
            float e = 1f - Mathf.Pow(1f - time / _settleTime, 3f);
            t.SetPositionAndRotation(Vector3.LerpUnclamped(fromPosition, home.position, e),
                Quaternion.Slerp(fromRotation, home.rotation, e));
            yield return null;
        }
        if (IsPlaced(index))
        {
            Pose home = _demolition.GetPieceHome(index);
            t.SetPositionAndRotation(home.position, home.rotation);
        }
    }

    private IEnumerator CompleteRoutine()
    {
        yield return new WaitForSeconds(_settleTime + 0.6f);
        if (!_demolition.IsDemolished || PlacedCount != TotalCount)
        {
            yield break;
        }
        // Everything is already in place, so the rebuild just swaps in the intact temple.
        if (_network != null && _network.IsSpawned)
        {
            _network.RequestToggle();
        }
        else
        {
            _demolition.Rebuild();
        }
    }

    // =====================================================================  per frame

    private void Update()
    {
        if (_demolition == null || !_demolition.IsReady || !_demolition.IsArchitectural)
        {
            return;
        }
        if (!_built)
        {
            Build();
        }
        Camera cam = Camera.main;
        Vector3 eyes = cam != null ? cam.transform.position : Vector3.zero;
        bool active = IsActive;

        int heldPiece = -1;
        for (int i = 0; i < _slots.Length; i++)
        {
            bool held = active && !IsPlaced(i) && _grabbables[i] != null && _grabbables[i].SelectingPointsCount > 0;
            if (_held[i] && !held && active && !IsPlaced(i))
            {
                OnReleased(i);
            }
            _held[i] = held;
            if (held)
            {
                heldPiece = i;
            }
        }

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);
        for (int i = 0; i < _slots.Length; i++)
        {
            float alpha = 0f;
            Color color = StoryCard.Gold;
            if (active && !IsPlaced(i))
            {
                bool ready = MissingSupports(i).Count == 0;
                if (i == heldPiece)
                {
                    alpha = ready ? (IsNearSlot(i) ? 1f : 0.7f * pulse) : 0.45f;
                    color = ready ? (IsNearSlot(i) ? Color.Lerp(StoryCard.Gold, Color.white, 0.5f) : StoryCard.Gold) : new Color(0.75f, 0.75f, 0.8f);
                }
                else if (heldPiece < 0 && ready)
                {
                    alpha = 0.18f * pulse; // a quiet hint: these can go in next
                }
            }
            DrawSlot(i, color, alpha);
        }

        UpdateCards(eyes);
    }

    private void OnReleased(int index)
    {
        if (HasAuthority)
        {
            TrySnap(index);
            return;
        }
        // Guests: the host decides (through NetworkedDemolition's throw); just explain a refusal.
        if (IsNearSlot(index))
        {
            var missing = MissingSupports(index);
            if (missing.Count > 0)
            {
                ShowHint(index, missing);
            }
        }
    }

    private void DrawSlot(int index, Color color, float alpha)
    {
        if (alpha <= 0.001f)
        {
            _slots[index].Draw(Matrix4x4.identity, color, 0f, 0f, 0f);
            return;
        }
        Pose home = _demolition.GetPieceHome(index);
        Vector3 scale = _demolition.GetPieceBody(index).transform.lossyScale;
        _slots[index].Draw(Matrix4x4.TRS(home.position, home.rotation, scale), color, alpha, 0.0025f, 0.01f);
    }

    // =====================================================================  cards and sound

    private void ShowInfo(int index)
    {
        int part = _demolition.GetPiecePart(index);
        if (part < 0)
        {
            return;
        }
        var info = TempleParts.Get((TempleParts.Id)part);
        _infoProgress.text = PlacedCount == TotalCount
            ? $"ALL {TotalCount} PARTS IN PLACE"
            : $"{PlacedCount} OF {TotalCount} PARTS IN PLACE";
        _infoTitle.text = info.Title;
        _infoSubtitle.text = info.Subtitle.ToUpperInvariant();
        _infoBody.text = info.Body;
        _infoPiece = index;
        _infoUntil = Time.time + _infoTime;
    }

    private void ShowHint(int index, List<TempleParts.Id> missing)
    {
        var names = new List<string>();
        foreach (var id in missing)
        {
            var info = TempleParts.Get(id);
            string name = $"{info.Title} ({info.Subtitle.ToLowerInvariant()})";
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }
        _hintText.text = "Not yet: this rests on the " + string.Join(", ", names) + ". Put that back first.";
        _hintPiece = index;
        _hintUntil = Time.time + 3.5f;
    }

    private void ShowCompletion()
    {
        if (!_built)
        {
            return;
        }
        _infoProgress.text = $"ALL {TotalCount} PARTS IN PLACE";
        _infoTitle.text = "Temple restored";
        _infoSubtitle.text = "REBUILT PIECE BY PIECE";
        _infoBody.text = "From the platform up to the finial, in the order its builders raised it.";
        _infoPiece = -1;
        _infoUntil = Time.time + _infoTime;
        Play(StorySounds.Shimmer, 0.6f);
    }

    private void UpdateCards(Vector3 eyes)
    {
        _overlay.position = transform.position;

        float info = Mathf.Clamp01((_infoUntil - Time.time) / 0.5f);
        _infoCard.Alpha = info;
        if (info > 0f)
        {
            PlaceCard(_infoCard, _infoPiece, eyes, 0.12f);
        }

        float hint = Mathf.Clamp01((_hintUntil - Time.time) / 0.4f);
        _hintCard.Alpha = hint;
        if (hint > 0f)
        {
            PlaceCard(_hintCard, _hintPiece, eyes, 0.04f);
        }
    }

    /// <summary>Above the piece's slot (or the temple), a little towards the viewer.</summary>
    private void PlaceCard(StoryCard card, int index, Vector3 eyes, float lift)
    {
        Vector3 anchor;
        if (index >= 0)
        {
            Pose home = _demolition.GetPieceHome(index);
            Bounds b = _demolition.GetPieceBounds(index);
            Vector3 scale = _demolition.GetPieceBody(index).transform.lossyScale;
            anchor = home.position + home.rotation * Vector3.Scale(b.center, scale)
                     + Vector3.up * (b.extents.y * scale.y + lift);
        }
        else
        {
            anchor = transform.position + Vector3.up * (_demolition.WorldHeight + lift);
        }
        Vector3 toEyes = eyes - anchor;
        toEyes.y = 0f;
        if (toEyes.sqrMagnitude > 1e-6f)
        {
            anchor += toEyes.normalized * 0.08f;
        }
        float size = Mathf.Clamp(Vector3.Distance(anchor, eyes) / 0.9f, 1f, 2.2f);
        card.Place(anchor + Vector3.up * (card.WorldHeight * size * 0.5f), eyes, size);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (_sfx != null && clip != null)
        {
            _sfx.transform.position = transform.position;
            _sfx.PlayOneShot(clip, volume);
        }
    }
}
