using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>
/// Demolishes the temple into rigid-body rubble and rebuilds it on demand.
///
/// At startup the temple is pre-fractured (off the main thread) in both styles, and each collapse
/// picks one (<see cref="Demolish(FractureStyle)"/>):
/// - <b>Architectural</b>: it comes apart into its real parts, the platform, base, the four walls,
///   cornice, storeys, dome and finial, plus Nandi and the door guardians (see
///   <see cref="TempleParts"/>), so it can be rebuilt piece by piece as a puzzle
///   (<see cref="TemplePuzzle"/>, added automatically). The panel's Demolish uses this by default.
/// - <b>Rubble</b>: large shell meshes are cut into irregular Voronoi chunks by assigning each
///   triangle to its nearest seed point; mid-sized parts (pillars, kalashas, statues) fall as pieces
///   of their own; tiny trim rides along with the nearest chunk. The story's collapse uses this.
///
/// <see cref="Demolish"/> trembles the temple, swaps it for the pieces and releases them bottom-first:
/// lower pieces kick outward, upper ones sag inward. With <see cref="_explosive"/> on, pieces that
/// overlap at rest shove each other apart and the rubble bursts across the room; off, it folds into a
/// heap where it stood. Every piece can be picked up by hand and thrown. <see cref="Rebuild"/> flies
/// the rubble back into place, base first, and swaps in the intact temple.
///
/// Requires Read/Write on the model import. Attach to the Temple root.
/// </summary>
public class TempleDemolition : MonoBehaviour
{
    public enum FractureStyle
    {
        /// <summary>Breaks along the temple's architectural parts, for the rebuild puzzle.</summary>
        Architectural,
        /// <summary>Random Voronoi rubble.</summary>
        Rubble,
    }

    [Header("Fracture")]
    [SerializeField]
    [Tooltip("Style for Demolish() without one (the panel's button). Architectural: the temple comes apart " +
             "into its named parts and can be rebuilt as a puzzle. Rubble: random chunks.")]
    private FractureStyle _fractureStyle = FractureStyle.Architectural;

    [SerializeField]
    [Tooltip("Architectural style: heights (fractions of the temple's height) where the stacked parts are cut.")]
    private TempleParts.Cuts _partCuts = new TempleParts.Cuts();

    [SerializeField]
    [Tooltip("Rubble style: how many chunks the large shell meshes are cut into.")]
    private int _chunkCount = 40;

    [SerializeField]
    [Tooltip("Seed for the fracture pattern, so the same temple breaks the same way every time.")]
    private int _fractureSeed = 7;

    [SerializeField]
    [Tooltip("Meshes with at least this many triangles are cut into chunks.")]
    private int _splitTriangleCount = 2500;

    [SerializeField]
    [Tooltip("Meshes larger than this fraction of the temple's size (with 200+ triangles) are cut into chunks.")]
    private float _splitSizeFraction = 0.12f;

    [SerializeField]
    [Tooltip("Meshes smaller than this fraction of the temple's size attach to the nearest chunk.")]
    private float _tinySizeFraction = 0.012f;

    [SerializeField]
    [Tooltip("Each piece's collision box as a fraction of its mesh bounds. Below 1 the mesh pokes out " +
             "of the box and sinks into whatever it lands on.")]
    [Range(0.5f, 1f)]
    private float _colliderFit = 1f;

    [Header("Collapse")]
    [SerializeField]
    [Tooltip("On: overlapping pieces push each other apart, so the temple bursts outward across the room. " +
             "Off: overlapping pieces pass through each other and it collapses into a heap in place.")]
    private bool _explosive = true;

    [SerializeField]
    [Tooltip("Seconds the temple trembles before it gives way.")]
    private float _trembleTime = 0.45f;

    [SerializeField]
    [Tooltip("Tremble amplitude as a fraction of the temple's height.")]
    private float _trembleAmount = 0.006f;

    [SerializeField]
    [Tooltip("Seconds between the base and the top being released.")]
    private float _releaseSpread = 0.3f;

    [SerializeField]
    [Tooltip("Outward kick of the lowest pieces, in temple-heights per second.")]
    private float _outwardKick = 0.35f;

    [SerializeField]
    [Tooltip("Inward sag of the highest pieces, in temple-heights per second.")]
    private float _inwardSag = 0.12f;

    [SerializeField]
    [Tooltip("Random tumble, in radians per second.")]
    private float _tumble = 2.5f;

    [SerializeField]
    [Tooltip("Gravity multiplier for the rubble. Below 1 gives a heavier, slower-motion feel.")]
    private float _gravityScale = 0.75f;

    [SerializeField]
    [Tooltip("Without a room scan, rubble lands on an invisible ground at the temple's base this many temple-widths across.")]
    private float _groundSize = 6f;

    [SerializeField]
    [Tooltip("With a room scan, a safety ground this many metres across sits at the real floor height, " +
             "catching anything that slips through gaps in the scanned floor.")]
    private float _roomGroundSize = 30f;

    [Header("Sound")]
    [SerializeField]
    [Tooltip("Played on every collapse (the cannon blast KovilLens used). If empty, Resources/<Collapse Clip Name> is loaded.")]
    private AudioClip _collapseClip;

    [SerializeField] private string _collapseClipName = "cannon_fire";

    [SerializeField]
    [Range(0f, 1f)] private float _collapseVolume = 0.9f;

    [Header("Rebuild")]
    [SerializeField]
    [Tooltip("Seconds the rubble takes to fly back into place on Rebuild. 0 snaps the temple back at once.")]
    private float _reassembleTime = 2.4f;

    [SerializeField]
    [Tooltip("How high each piece arcs on its way back, as a fraction of the temple's height.")]
    private float _reassembleLift = 0.12f;

    public bool IsDemolished { get; private set; }

    /// <summary>
    /// True while the rubble flies back into place after <see cref="Rebuild"/>. The temple already
    /// counts as standing (<see cref="IsDemolished"/> is false); only the pieces are still moving.
    /// </summary>
    public bool IsReassembling { get; private set; }

    /// <summary>True once the fracture has been precomputed and <see cref="Demolish"/> can run.</summary>
    public bool IsReady { get; private set; }

    public event Action<bool> DemolishedChanged;

    /// <summary>
    /// On: a remote copy in multiplayer. The collapse still trembles and swaps in the rubble, but the
    /// pieces stay kinematic and are posed from the network instead of simulated here.
    /// </summary>
    public bool Driven { get; set; }

    public int PieceCount => _pieces.Count;

    /// <summary>The style of the pieces in use: of the current collapse, or of the last one.</summary>
    public FractureStyle CurrentStyle => _active.Style;

    public Rigidbody GetPieceBody(int index) => _pieces[index].Body;

    /// <summary>True when the pieces in use are the architectural parts (that can be rebuilt as a puzzle).</summary>
    public bool IsArchitectural => _active.Style == FractureStyle.Architectural;

    /// <summary>Which architectural part (<see cref="TempleParts.Id"/>) a piece is, or -1 for rubble.</summary>
    public int GetPiecePart(int index) => _pieces[index].PartId;

    /// <summary>The piece's mesh bounds in its own space.</summary>
    public Bounds GetPieceBounds(int index) => _pieces[index].Bounds;

    /// <summary>World pose a piece has in the standing temple (its slot).</summary>
    public Pose GetPieceHome(int index)
    {
        Piece p = _pieces[index];
        return new Pose(_debrisRoot.TransformPoint(p.LocalPosition), _debrisRoot.rotation * p.LocalRotation);
    }

    /// <summary>The temple's height in metres.</summary>
    public float WorldHeight => _localBounds.size.y * transform.lossyScale.y;

    public bool IsPieceLocked(int index) => _pieces[index].Locked;

    /// <summary>
    /// Fixes a piece in its slot: posed there, kinematic, no longer grabbable. Used by the puzzle once
    /// a piece is placed. Undone when the temple is demolished again or rebuilt.
    /// </summary>
    public void LockPiece(int index)
    {
        Piece p = _pieces[index];
        if (p.Locked)
        {
            return;
        }
        p.Locked = true;
        _pieces[index] = p;
        _falling.Remove(p.Body);
        if (!p.Body.isKinematic)
        {
            p.Body.linearVelocity = Vector3.zero;
            p.Body.angularVelocity = Vector3.zero;
        }
        p.Body.isKinematic = true;
        p.Body.transform.SetLocalPositionAndRotation(p.LocalPosition, p.LocalRotation);
        SetPieceGrabbable(p, false);
    }

    private void UnlockAll()
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            Piece p = _pieces[i];
            if (!p.Locked)
            {
                continue;
            }
            p.Locked = false;
            _pieces[i] = p;
            SetPieceGrabbable(p, true);
        }
    }

    private static void SetPieceGrabbable(Piece p, bool grabbable)
    {
        if (p.Grabbable != null)
        {
            p.Grabbable.enabled = grabbable;
        }
        if (p.HandGrab != null)
        {
            p.HandGrab.enabled = grabbable;
        }
    }

    /// <summary>Releases a piece into the simulation with the given velocity (e.g. thrown by another player).</summary>
    public void ThrowPiece(int index, Vector3 velocity, Vector3 angularVelocity)
    {
        if (_pieces[index].Locked)
        {
            return;
        }
        Rigidbody rb = _pieces[index].Body;
        rb.isKinematic = false;
        rb.linearVelocity = velocity;
        rb.angularVelocity = angularVelocity;
        if (!_falling.Contains(rb))
        {
            _falling.Add(rb);
        }
    }

    private struct Piece
    {
        public Rigidbody Body;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public float Height;          // 0 = base, 1 = top
        public Vector3 Radial;        // horizontal direction away from the temple's axis (temple space)
        public int PartId;            // TempleParts.Id, or -1 for rubble
        public bool Locked;           // placed back in its slot by the puzzle
        public Bounds Bounds;         // mesh bounds in the piece's own space
        public Grabbable Grabbable;
        public HandGrabInteractable HandGrab;
    }

    /// <summary>The pieces of one fracture style, under their own (normally hidden) root.</summary>
    private class FractureSet
    {
        public FractureStyle Style;
        public readonly List<Piece> Pieces = new List<Piece>();
        // Pieces whose boxes overlap in the intact temple. Unless explosive, they must not collide with
        // each other, or the solver blasts them apart the moment they turn dynamic.
        public readonly List<(Collider, Collider)> IgnoredPairs = new List<(Collider, Collider)>();
        public Transform Root;
        public BoxCollider Ground;
    }

    private readonly FractureSet _architecturalSet = new FractureSet { Style = FractureStyle.Architectural };
    private readonly FractureSet _rubbleSet = new FractureSet { Style = FractureStyle.Rubble };
    private FractureSet _active;

    private List<Piece> _pieces => _active.Pieces;
    private Transform _debrisRoot => _active.Root;
    private BoxCollider _ground => _active.Ground;
    private List<(Collider, Collider)> _ignoredPairs => _active.IgnoredPairs;

    private readonly List<Renderer> _intactRenderers = new List<Renderer>();
    private readonly List<Collider> _intactColliders = new List<Collider>();
    private readonly List<Behaviour> _grabBehaviours = new List<Behaviour>();
    private readonly List<Rigidbody> _falling = new List<Rigidbody>();
    private readonly Dictionary<Material, Material> _doubleSided = new Dictionary<Material, Material>();

    private Bounds _localBounds;
    private Coroutine _routine;
    private bool _trembling;
    private Vector3 _trembleOrigin;
    private Vector3 _preDemolishPosition;
    private Quaternion _preDemolishRotation;
    private Rigidbody _rootBody;
    private bool _rootWasKinematic;
    private AudioSource _audio;

    private void Awake()
    {
        _active = SetFor(_fractureStyle);
    }

    private void Start()
    {
        if (GetComponent<TemplePuzzle>() == null)
        {
            gameObject.AddComponent<TemplePuzzle>();
        }
        if (_collapseClip == null && !string.IsNullOrEmpty(_collapseClipName))
        {
            _collapseClip = Resources.Load<AudioClip>(_collapseClipName);
        }
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0.85f;
        _audio.minDistance = 0.8f;
        _audio.maxDistance = 12f;
        _audio.rolloffMode = AudioRolloffMode.Linear;
        StartCoroutine(BuildRoutine());
    }

    private FractureSet SetFor(FractureStyle style) => style == FractureStyle.Rubble ? _rubbleSet : _architecturalSet;

    /// <summary>Collapses in the default style (<see cref="_fractureStyle"/>).</summary>
    public void Demolish()
    {
        Demolish(_fractureStyle);
    }

    public void Demolish(FractureStyle style)
    {
        if (!IsReady || IsDemolished)
        {
            return;
        }
        if (IsReassembling)
        {
            // Knocked down again mid-rebuild: finish the rebuild instantly, then collapse from there.
            StopRoutine();
            FinishRebuild();
        }
        _active = SetFor(style);
        // Remember where the temple stood; the root's own Rigidbody must not fall with the rubble.
        _preDemolishPosition = transform.position;
        _preDemolishRotation = transform.rotation;
        _rootBody = GetComponent<Rigidbody>();
        if (_rootBody != null)
        {
            _rootWasKinematic = _rootBody.isKinematic;
            if (!_rootBody.isKinematic)
            {
                _rootBody.linearVelocity = Vector3.zero;
                _rootBody.angularVelocity = Vector3.zero;
            }
            _rootBody.isKinematic = true;
        }
        UnlockAll();
        IsDemolished = true;
        // The blast lands as the tremble gives way, every collapse and on every headset.
        if (_audio != null && _collapseClip != null)
        {
            _audio.PlayOneShot(_collapseClip, _collapseVolume);
        }
        DemolishedChanged?.Invoke(true);
        _routine = StartCoroutine(CollapseRoutine());
    }

    public void Rebuild()
    {
        if (!IsDemolished)
        {
            return;
        }
        StopRoutine();
        if (_trembling)
        {
            transform.localPosition = _trembleOrigin;
            _trembling = false;
        }

        _falling.Clear();
        foreach (var p in _pieces)
        {
            p.Body.isKinematic = true;
        }

        // Put the temple back where it stood before it fell.
        transform.SetPositionAndRotation(_preDemolishPosition, _preDemolishRotation);
        if (_rootBody != null)
        {
            _rootBody.position = _preDemolishPosition;
            _rootBody.rotation = _preDemolishRotation;
        }

        IsDemolished = false;
        if (_reassembleTime > 0f && _debrisRoot.gameObject.activeSelf)
        {
            // The intact temple stays hidden until the last piece lands.
            IsReassembling = true;
            _routine = StartCoroutine(ReassembleRoutine());
        }
        else
        {
            FinishRebuild();
        }
        DemolishedChanged?.Invoke(false);
    }

    /// <summary>Swaps the rubble for the intact temple.</summary>
    private void FinishRebuild()
    {
        IsReassembling = false;
        UnlockAll();
        _debrisRoot.gameObject.SetActive(false);
        if (_rootBody != null)
        {
            _rootBody.isKinematic = _rootWasKinematic;
        }
        SetIntactVisible(true);
    }

    private void StopRoutine()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
    }

    public void Toggle()
    {
        if (IsDemolished)
        {
            Rebuild();
        }
        else
        {
            Demolish();
        }
    }

    private IEnumerator CollapseRoutine()
    {
        float height = _localBounds.size.y * transform.lossyScale.y;

        // 1. Tremble.
        Vector3 origin = transform.localPosition;
        _trembleOrigin = origin;
        _trembling = true;
        for (float t = 0f; t < _trembleTime; t += Time.deltaTime)
        {
            float ramp = t / _trembleTime;
            Vector3 jitter = Random.insideUnitSphere * (_trembleAmount * height * ramp);
            transform.localPosition = origin + (transform.parent != null
                ? transform.parent.InverseTransformVector(jitter)
                : jitter);
            yield return null;
        }
        transform.localPosition = origin;
        _trembling = false;

        // 2. Swap the intact temple for the pieces, laid out exactly where the temple is.
        _debrisRoot.SetPositionAndRotation(transform.position, transform.rotation);
        _debrisRoot.localScale = transform.lossyScale;
        foreach (var p in _pieces)
        {
            p.Body.isKinematic = true;
            p.Body.transform.SetLocalPositionAndRotation(p.LocalPosition, p.LocalRotation);
        }
        PlaceGround();
        _debrisRoot.gameObject.SetActive(true);
        SetIntactVisible(false);
        // Ignore state does not survive collider deactivation, so (re)apply it on every collapse.
        foreach (var (a, b) in _ignoredPairs)
        {
            Physics.IgnoreCollision(a, b, !_explosive);
        }
        if (Driven)
        {
            _routine = null; // the network moves the pieces from here
            yield break;
        }

        // 3. Release bottom-first. Each piece is given a velocity that blends from an outward
        //    kick at the base to an inward sag at the top, so the structure folds in on itself.
        _falling.Clear();
        var released = new bool[_pieces.Count];
        int remaining = _pieces.Count;
        float start = Time.time;
        while (remaining > 0)
        {
            float elapsed = Time.time - start;
            for (int i = 0; i < _pieces.Count; i++)
            {
                Piece p = _pieces[i];
                // A piece grabbed before its turn waits until it's let go; Grabbable restores the
                // kinematic state it had when grabbed, and releasing it under the hand would fight the grab.
                if (released[i] || elapsed < p.Height * _releaseSpread || p.Body.IsLocked())
                {
                    continue;
                }
                released[i] = true;
                remaining--;

                float radialSpeed = Mathf.Lerp(_outwardKick, -_inwardSag, p.Height) * height;
                Vector3 radial = _debrisRoot.TransformDirection(p.Radial).normalized;
                Vector3 velocity = radial * radialSpeed * Random.Range(0.6f, 1.3f)
                                   + Random.insideUnitSphere * (0.05f * height);

                p.Body.isKinematic = false;
                p.Body.linearVelocity = velocity;
                p.Body.angularVelocity = Random.insideUnitSphere * _tumble;
                _falling.Add(p.Body);
            }
            yield return null;
        }
        _routine = null;
    }

    /// <summary>
    /// Flies every piece from wherever it lies back to its place in the temple, base first, each on a
    /// low arc, then swaps in the intact temple. Runs in the temple's own frame, so it keeps up if the
    /// temple is moved meanwhile.
    /// </summary>
    private IEnumerator ReassembleRoutine()
    {
        int count = _pieces.Count;
        var startPosition = new Vector3[count];
        var startRotation = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            Transform t = _pieces[i].Body.transform;
            startPosition[i] = t.position;
            startRotation[i] = t.rotation;
        }

        // Move the rubble's frame onto the temple without moving the pieces, then work in that frame.
        _debrisRoot.SetPositionAndRotation(transform.position, transform.rotation);
        _debrisRoot.localScale = transform.lossyScale;
        for (int i = 0; i < count; i++)
        {
            startPosition[i] = _debrisRoot.InverseTransformPoint(startPosition[i]);
            startRotation[i] = Quaternion.Inverse(_debrisRoot.rotation) * startRotation[i];
        }

        float spread = _reassembleTime * 0.45f;    // base pieces leave first, the top ones last
        float flight = _reassembleTime - spread;
        float lift = _localBounds.size.y * _reassembleLift;
        float start = Time.time;
        float elapsed = 0f;
        while (elapsed < _reassembleTime)
        {
            elapsed = Time.time - start;
            _debrisRoot.SetPositionAndRotation(transform.position, transform.rotation);
            _debrisRoot.localScale = transform.lossyScale;
            for (int i = 0; i < count; i++)
            {
                Piece p = _pieces[i];
                float u = Mathf.Clamp01((elapsed - p.Height * spread) / flight);
                float e = u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f; // ease in-out
                Vector3 position = Vector3.LerpUnclamped(startPosition[i], p.LocalPosition, e)
                                   + Vector3.up * (Mathf.Sin(e * Mathf.PI) * lift);
                p.Body.transform.SetLocalPositionAndRotation(position,
                    Quaternion.Slerp(startRotation[i], p.LocalRotation, e));
            }
            yield return null;
        }
        _routine = null;
        FinishRebuild();
    }

    private void FixedUpdate()
    {
        if (_falling.Count == 0)
        {
            return;
        }
        Vector3 g = Physics.gravity * _gravityScale;
        foreach (var rb in _falling)
        {
            if (!rb.IsSleeping())
            {
                rb.AddForce(g, ForceMode.Acceleration);
            }
        }
    }

    /// <summary>
    /// With a room scan the rubble falls onto the real floor and furniture (see <see cref="RoomColliders"/>),
    /// so the fallback ground drops to the real floor. Without one it stays at the temple's base.
    /// </summary>
    private void PlaceGround()
    {
        float thickness = _localBounds.size.y * 0.5f;
        if (RoomColliders.TryGetFloorHeight(out float floorY))
        {
            Vector3 floor = _debrisRoot.InverseTransformPoint(new Vector3(transform.position.x, floorY, transform.position.z));
            float width = _roomGroundSize / Mathf.Max(transform.lossyScale.x, 1e-4f);
            _ground.center = new Vector3(_localBounds.center.x, floor.y - thickness * 0.5f, _localBounds.center.z);
            _ground.size = new Vector3(width, thickness, width);
        }
        else
        {
            float width = Mathf.Max(_localBounds.size.x, _localBounds.size.z) * _groundSize;
            _ground.center = new Vector3(_localBounds.center.x, _localBounds.min.y - thickness * 0.5f, _localBounds.center.z);
            _ground.size = new Vector3(width, thickness, width);
        }
    }

    private void SetIntactVisible(bool visible)
    {
        foreach (var r in _intactRenderers)
        {
            r.enabled = visible;
        }
        foreach (var c in _intactColliders)
        {
            c.enabled = visible;
        }
        foreach (var b in _grabBehaviours)
        {
            b.enabled = visible;
        }
    }

    private class SourceMesh
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Matrix4x4 ToTemple;
        public Bounds LocalBounds;
        public MeshFilter Filter;

        // Filled for meshes that get cut into chunks.
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[][] SubmeshTriangles;
        public string[] MaterialNames; // architectural style: lower-case material and texture names
    }

    private class ChunkData
    {
        public int PartId = -1;
        public Vector3 Center;
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public List<int[]> Submeshes = new List<int[]>();
        public List<Material> Materials = new List<Material>();
    }

    private IEnumerator BuildRoutine()
    {
        // Collect every mesh and its transform into temple space.
        var sources = new List<SourceMesh>();
        Matrix4x4 worldToTemple = transform.worldToLocalMatrix;
        bool first = true;
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null)
            {
                continue;
            }
            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogError($"TempleDemolition: '{mf.sharedMesh.name}' is not readable. " +
                               "Enable Read/Write on the model's import settings.", this);
                yield break;
            }

            var src = new SourceMesh
            {
                Mesh = mf.sharedMesh,
                Materials = mr.sharedMaterials,
                ToTemple = worldToTemple * mf.transform.localToWorldMatrix,
                Filter = mf,
            };
            src.LocalBounds = TransformBounds(src.ToTemple, src.Mesh.bounds);
            sources.Add(src);
            _intactRenderers.Add(mr);

            if (first)
            {
                _localBounds = src.LocalBounds;
                first = false;
            }
            else
            {
                _localBounds.Encapsulate(src.LocalBounds);
            }
        }
        GetComponentsInChildren(true, _intactColliders);
        foreach (var grab in GetComponentsInChildren<Grabbable>(true))
        {
            _grabBehaviours.Add(grab);
        }
        foreach (var grab in GetComponentsInChildren<HandGrabInteractable>(true))
        {
            _grabBehaviours.Add(grab);
        }
        foreach (var grab in GetComponentsInChildren<GrabInteractable>(true))
        {
            _grabBehaviours.Add(grab);
        }

        // Rubble first: it reads the vertex data of the big meshes and frees it again; then the
        // architectural parts read every mesh afresh.
        yield return BuildRubbleRoutine(sources);
        yield return BuildArchitecturalRoutine(sources);
        _active = SetFor(_fractureStyle);
        IsReady = true;
    }

    private IEnumerator BuildRubbleRoutine(List<SourceMesh> sources)
    {
        // Classify by size.
        float size = _localBounds.size.magnitude;
        var split = new List<SourceMesh>();
        var whole = new List<SourceMesh>();
        var tiny = new List<SourceMesh>();
        foreach (var src in sources)
        {
            int tris = TriangleCount(src.Mesh);
            float extent = MaxComponent(src.LocalBounds.size);
            if (tris >= _splitTriangleCount || (extent > _splitSizeFraction * size && tris >= 200))
            {
                split.Add(src);
            }
            else if (extent < _tinySizeFraction * size)
            {
                tiny.Add(src);
            }
            else
            {
                whole.Add(src);
            }
        }

        // Pull mesh data on the main thread (Mesh API), then cut on a worker thread.
        foreach (var src in split)
        {
            src.Vertices = src.Mesh.vertices;
            src.Normals = src.Mesh.normals;
            src.Uvs = src.Mesh.uv;
            src.SubmeshTriangles = new int[src.Mesh.subMeshCount][];
            for (int s = 0; s < src.Mesh.subMeshCount; s++)
            {
                src.SubmeshTriangles[s] = src.Mesh.GetTriangles(s);
            }
        }

        int chunkCount = split.Count > 0 ? Mathf.Max(1, _chunkCount) : 0;
        int seed = _fractureSeed;
        Task<List<ChunkData>> task = Task.Run(() => Fracture(split, chunkCount, seed));
        while (!task.IsCompleted)
        {
            yield return null;
        }
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception, this);
            yield break;
        }

        BuildPieces(_rubbleSet, task.Result, whole, tiny);

        // Free the CPU-side copies we no longer need.
        foreach (var src in split)
        {
            src.Vertices = null;
            src.Normals = null;
            src.Uvs = null;
            src.SubmeshTriangles = null;
        }
    }

    /// <summary>Worker-thread Voronoi fracture. Pure data: no Unity object access except value types.</summary>
    private static List<ChunkData> Fracture(List<SourceMesh> split, int chunkCount, int seed)
    {
        if (chunkCount == 0)
        {
            return new List<ChunkData>();
        }

        // Transform everything into temple space and gather triangle centroids.
        ToTempleSpace(split);
        var centroids = new List<Vector3>();
        foreach (var src in split)
        {
            foreach (int[] tris in src.SubmeshTriangles)
            {
                for (int t = 0; t < tris.Length; t += 3)
                {
                    centroids.Add((src.Vertices[tris[t]] + src.Vertices[tris[t + 1]] + src.Vertices[tris[t + 2]]) / 3f);
                }
            }
        }

        // Seeds: best-candidate sampling over the geometry, so chunks are evenly sized.
        var rng = new System.Random(seed);
        var seeds = new List<Vector3> { centroids[rng.Next(centroids.Count)] };
        while (seeds.Count < chunkCount)
        {
            Vector3 best = default;
            float bestDist = -1f;
            for (int c = 0; c < 12; c++)
            {
                Vector3 candidate = centroids[rng.Next(centroids.Count)];
                float d = NearestDistanceSq(seeds, candidate, out _);
                if (d > bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }
            seeds.Add(best);
        }

        // Assign triangles to their nearest seed, per source mesh and submesh.
        // assignment[src][submesh][triangle] = chunk index
        var assignment = new int[split.Count][][];
        int centroidIndex = 0;
        for (int s = 0; s < split.Count; s++)
        {
            var src = split[s];
            assignment[s] = new int[src.SubmeshTriangles.Length][];
            for (int sm = 0; sm < src.SubmeshTriangles.Length; sm++)
            {
                int triCount = src.SubmeshTriangles[sm].Length / 3;
                var a = new int[triCount];
                for (int t = 0; t < triCount; t++)
                {
                    NearestDistanceSq(seeds, centroids[centroidIndex++], out a[t]);
                }
                assignment[s][sm] = a;
            }
        }

        var chunks = BuildChunks(split, assignment, chunkCount);
        foreach (var chunk in chunks)
        {
            chunk.PartId = -1; // rubble
        }
        return chunks;
    }

    /// <summary>Worker thread: sorts every triangle into its architectural part, one chunk per part.</summary>
    private static List<ChunkData> FractureArchitectural(List<SourceMesh> sources, TempleParts.Cuts cuts)
    {
        ToTempleSpace(sources);
        var parts = new List<TempleParts.Source>(sources.Count);
        foreach (var src in sources)
        {
            parts.Add(new TempleParts.Source
            {
                Vertices = src.Vertices,
                SubmeshTriangles = src.SubmeshTriangles,
                MaterialNames = src.MaterialNames,
            });
        }
        int[][][] assignment = TempleParts.Classify(parts, cuts);
        return BuildChunks(sources, assignment, TempleParts.Count);
    }

    /// <summary>Moves vertices and normals into temple space, in place.</summary>
    private static void ToTempleSpace(List<SourceMesh> sources)
    {
        foreach (var src in sources)
        {
            Matrix4x4 m = src.ToTemple;
            Matrix4x4 n = m.inverse.transpose;
            for (int i = 0; i < src.Vertices.Length; i++)
            {
                src.Vertices[i] = m.MultiplyPoint3x4(src.Vertices[i]);
            }
            if (src.Normals != null && src.Normals.Length == src.Vertices.Length)
            {
                for (int i = 0; i < src.Normals.Length; i++)
                {
                    src.Normals[i] = n.MultiplyVector(src.Normals[i]).normalized;
                }
            }
        }
    }

    /// <summary>
    /// Builds one mesh per chunk index from a per-triangle assignment
    /// (assignment[source][submesh][triangle] = chunk), remapping shared vertices. Empty chunks are skipped.
    /// </summary>
    private static List<ChunkData> BuildChunks(List<SourceMesh> split, int[][][] assignment, int chunkCount)
    {
        var chunks = new List<ChunkData>();
        // Build each chunk's vertex/index buffers, remapping shared vertices.
        var remap = new Dictionary<int, int>();
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (int c = 0; c < chunkCount; c++)
        {
            var chunk = new ChunkData { PartId = c };
            verts.Clear();
            normals.Clear();
            uvs.Clear();

            for (int s = 0; s < split.Count; s++)
            {
                var src = split[s];
                bool hasNormals = src.Normals != null && src.Normals.Length == src.Vertices.Length;
                bool hasUvs = src.Uvs != null && src.Uvs.Length == src.Vertices.Length;
                remap.Clear();

                for (int sm = 0; sm < src.SubmeshTriangles.Length; sm++)
                {
                    int[] tris = src.SubmeshTriangles[sm];
                    int[] a = assignment[s][sm];
                    var indices = new List<int>();
                    for (int t = 0; t < a.Length; t++)
                    {
                        if (a[t] != c)
                        {
                            continue;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            int old = tris[t * 3 + k];
                            if (!remap.TryGetValue(old, out int idx))
                            {
                                idx = verts.Count;
                                remap[old] = idx;
                                verts.Add(src.Vertices[old]);
                                normals.Add(hasNormals ? src.Normals[old] : Vector3.up);
                                uvs.Add(hasUvs ? src.Uvs[old] : Vector2.zero);
                            }
                            indices.Add(idx);
                        }
                    }
                    if (indices.Count > 0)
                    {
                        chunk.Submeshes.Add(indices.ToArray());
                        chunk.Materials.Add(sm < src.Materials.Length ? src.Materials[sm] : null);
                    }
                }
            }

            if (verts.Count == 0)
            {
                continue;
            }

            // Pivot each chunk on its own centre so it tumbles naturally.
            Vector3 min = verts[0], max = verts[0];
            foreach (var v in verts)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            chunk.Center = (min + max) * 0.5f;
            chunk.Vertices = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                chunk.Vertices[i] = verts[i] - chunk.Center;
            }
            chunk.Normals = normals.ToArray();
            chunk.Uvs = uvs.ToArray();
            chunks.Add(chunk);
        }
        return chunks;
    }

    /// <summary>Architectural style: every visible mesh is sorted into the temple's parts.</summary>
    private IEnumerator BuildArchitecturalRoutine(List<SourceMesh> all)
    {
        // Hidden meshes in the model are left out: they never show in the standing temple either.
        var sources = new List<SourceMesh>();
        foreach (var src in all)
        {
            if (!src.Filter.gameObject.activeInHierarchy)
            {
                continue;
            }
            src.Vertices = src.Mesh.vertices;
            src.Normals = src.Mesh.normals;
            src.Uvs = src.Mesh.uv;
            src.SubmeshTriangles = new int[src.Mesh.subMeshCount][];
            src.MaterialNames = new string[src.Mesh.subMeshCount];
            for (int s = 0; s < src.Mesh.subMeshCount; s++)
            {
                src.SubmeshTriangles[s] = src.Mesh.GetTriangles(s);
                Material m = s < src.Materials.Length ? src.Materials[s] : null;
                string texture = m != null && m.mainTexture != null ? m.mainTexture.name : "";
                src.MaterialNames[s] = m != null ? (m.name + "|" + texture).ToLowerInvariant() : "";
            }
            sources.Add(src);
        }

        TempleParts.Cuts cuts = _partCuts;
        Task<List<ChunkData>> task = Task.Run(() => FractureArchitectural(sources, cuts));
        while (!task.IsCompleted)
        {
            yield return null;
        }
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception, this);
            yield break;
        }

        BuildPieces(_architecturalSet, task.Result, new List<SourceMesh>(), new List<SourceMesh>());
        foreach (var src in sources)
        {
            src.Vertices = null;
            src.Normals = null;
            src.Uvs = null;
            src.SubmeshTriangles = null;
        }
    }

    private void BuildPieces(FractureSet set, List<ChunkData> chunks, List<SourceMesh> whole, List<SourceMesh> tiny)
    {
        _active = set; // AddPiece and FindOverlappingPairs work on the active set
        var rootGo = new GameObject($"{name} ({set.Style} pieces)");
        rootGo.SetActive(false);
        set.Root = rootGo.transform;

        // Invisible ground, placed on every collapse by PlaceGround.
        var groundGo = new GameObject("Ground");
        groundGo.transform.SetParent(set.Root, false);
        set.Ground = groundGo.AddComponent<BoxCollider>();

        var chunkTransforms = new List<Transform>();
        int index = 0;
        foreach (var chunk in chunks)
        {
            var mesh = new Mesh { name = $"TempleChunk_{index}" };
            if (chunk.Vertices.Length > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.SetVertices(chunk.Vertices);
            mesh.SetNormals(chunk.Normals);
            mesh.SetUVs(0, chunk.Uvs);
            mesh.subMeshCount = chunk.Submeshes.Count;
            for (int s = 0; s < chunk.Submeshes.Count; s++)
            {
                mesh.SetTriangles(chunk.Submeshes[s], s, false);
            }
            mesh.RecalculateBounds();

            string pieceName = chunk.PartId >= 0
                ? $"{TempleParts.Get((TempleParts.Id)chunk.PartId).Title} ({TempleParts.Get((TempleParts.Id)chunk.PartId).Subtitle})"
                : $"Chunk_{index}";
            index++;
            var t = AddPiece(pieceName, mesh, chunk.Materials.ToArray(), Matrix4x4.Translate(chunk.Center), chunk.PartId);
            chunkTransforms.Add(t);
        }

        foreach (var src in whole)
        {
            AddPiece(src.Filter.name, src.Mesh, src.Materials, src.ToTemple);
        }

        // Tiny trim rides on the nearest chunk (or falls on its own if there are no chunks).
        foreach (var src in tiny)
        {
            if (chunkTransforms.Count == 0)
            {
                AddPiece(src.Filter.name, src.Mesh, src.Materials, src.ToTemple);
                continue;
            }
            Transform nearest = chunkTransforms[0];
            float best = float.MaxValue;
            foreach (var ct in chunkTransforms)
            {
                float d = (ct.localPosition - src.LocalBounds.center).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = ct;
                }
            }
            var go = new GameObject(src.Filter.name);
            go.transform.SetParent(nearest, false);
            Matrix4x4 local = Matrix4x4.Translate(-nearest.localPosition) * src.ToTemple;
            go.transform.SetLocalPositionAndRotation(local.GetPosition(), local.rotation);
            go.transform.localScale = local.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = src.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = DoubleSided(src.Materials);
        }

        FindOverlappingPairs();
    }

    private void FindOverlappingPairs()
    {
        // Box bounds in debris-root (temple) space, slightly padded.
        var colliders = new List<BoxCollider>();
        var bounds = new List<Bounds>();
        foreach (var p in _pieces)
        {
            var box = p.Body.GetComponent<BoxCollider>();
            Matrix4x4 m = Matrix4x4.TRS(p.LocalPosition, p.LocalRotation, p.Body.transform.localScale);
            Bounds b = TransformBounds(m, new Bounds(box.center, box.size));
            b.Expand(b.size * 0.05f);
            colliders.Add(box);
            bounds.Add(b);
        }
        for (int i = 0; i < bounds.Count; i++)
        {
            for (int j = i + 1; j < bounds.Count; j++)
            {
                if (bounds[i].Intersects(bounds[j]))
                {
                    _ignoredPairs.Add((colliders[i], colliders[j]));
                }
            }
        }
    }

    private Transform AddPiece(string pieceName, Mesh mesh, Material[] materials, Matrix4x4 toTemple, int partId = -1)
    {
        var go = new GameObject(pieceName);
        var t = go.transform;
        t.SetParent(_debrisRoot, false);
        t.SetLocalPositionAndRotation(toTemple.GetPosition(), toTemple.rotation);
        t.localScale = toTemple.lossyScale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = DoubleSided(materials);

        // A box around the mesh. Neighbouring boxes overlap at rest; in explosive mode that overlap is
        // what flings the rubble outward when the pieces turn dynamic.
        var box = go.AddComponent<BoxCollider>();
        Bounds b = mesh.bounds;
        Vector3 boxSize = b.size * _colliderFit;
        float minThickness = MaxComponent(b.size) * 0.15f;
        box.center = b.center;
        box.size = Vector3.Max(boxSize, Vector3.one * minThickness);

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false; // gravity is applied with _gravityScale in FixedUpdate
        rb.maxDepenetrationVelocity = 0.15f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.2f;
        rb.sleepThreshold = 0.02f; // let settled rubble go to sleep instead of creeping
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // don't tunnel through thin room planes
        Vector3 worldSize = Vector3.Scale(box.size, t.lossyScale) * transform.lossyScale.x;
        rb.mass = Mathf.Clamp(worldSize.x * worldSize.y * worldSize.z * 2500f, 0.02f, 50f);

        // Pick up with either hand and throw on release: Grabbable keeps the body kinematic while held,
        // then hands it the hand's velocity when let go.
        var grabbable = go.AddComponent<Grabbable>();
        grabbable.InjectOptionalRigidbody(rb);
        grabbable.InjectOptionalKinematicWhileSelected(true);
        grabbable.InjectOptionalThrowWhenUnselected(true);
        var handGrab = go.AddComponent<HandGrabInteractable>();
        handGrab.InjectRigidbody(rb);
        handGrab.InjectOptionalPointableElement(grabbable);

        // Where in the temple this piece sits drives the collapse choreography.
        Vector3 centre = toTemple.MultiplyPoint3x4(b.center);
        float height = Mathf.InverseLerp(_localBounds.min.y, _localBounds.max.y, centre.y);
        Vector3 radial = centre - _localBounds.center;
        radial.y = 0f;
        if (radial.sqrMagnitude < 1e-6f)
        {
            radial = Random.insideUnitSphere;
            radial.y = 0f;
        }

        _pieces.Add(new Piece
        {
            Body = rb,
            LocalPosition = t.localPosition,
            LocalRotation = t.localRotation,
            Height = height,
            Radial = radial,
            PartId = partId,
            Bounds = b,
            Grabbable = grabbable,
            HandGrab = handGrab,
        });
        return t;
    }

    /// <summary>Rubble shows its insides, so render both faces of each material.</summary>
    private Material[] DoubleSided(Material[] materials)
    {
        var result = new Material[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m == null)
            {
                continue;
            }
            if (!_doubleSided.TryGetValue(m, out Material copy))
            {
                copy = new Material(m) { name = m.name + " (Rubble)" };
                if (copy.HasProperty("_Cull"))
                {
                    copy.SetFloat("_Cull", (float)CullMode.Off);
                }
                copy.doubleSidedGI = true;
                if (copy.HasProperty("_NoOcclusion"))
                {
                    copy.SetFloat("_NoOcclusion", 1f); // real-world occlusion fringes and swallows small pieces
                }
                _doubleSided[m] = copy;
            }
            result[i] = copy;
        }
        return result;
    }

    private void OnDestroy()
    {
        foreach (var set in new[] { _architecturalSet, _rubbleSet })
        {
            if (set.Root != null)
            {
                Destroy(set.Root.gameObject);
            }
        }
        foreach (var m in _doubleSided.Values)
        {
            Destroy(m);
        }
    }

    private static float NearestDistanceSq(List<Vector3> points, Vector3 p, out int index)
    {
        float best = float.MaxValue;
        index = 0;
        for (int i = 0; i < points.Count; i++)
        {
            float d = (points[i] - p).sqrMagnitude;
            if (d < best)
            {
                best = d;
                index = i;
            }
        }
        return best;
    }

    private static int TriangleCount(Mesh mesh)
    {
        long count = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            count += mesh.GetIndexCount(s);
        }
        return (int)(count / 3);
    }

    private static float MaxComponent(Vector3 v)
    {
        return Mathf.Max(v.x, Mathf.Max(v.y, v.z));
    }

    private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        Vector3 c = m.MultiplyPoint3x4(b.center);
        Vector3 e = b.extents;
        Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0, 0));
        Vector3 ay = m.MultiplyVector(new Vector3(0, e.y, 0));
        Vector3 az = m.MultiplyVector(new Vector3(0, 0, e.z));
        Vector3 ext = new Vector3(
            Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
            Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
            Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
        return new Bounds(c, ext * 2f);
    }
}
