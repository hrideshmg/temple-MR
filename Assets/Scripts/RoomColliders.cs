using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if !UNITY_EDITOR && UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Loads the headset's room scan (Space Setup) through MR Utility Kit and gives the floor, walls and
/// scanned furniture invisible colliders, so physics objects such as the temple rubble land on the real room.
///
/// Spawns itself after the scene loads; nothing needs to be placed in the scene. At launch it loads the
/// stored scan, opening Space Setup only if there is none (<see cref="RescanOnLaunch"/> opens it every
/// launch; Space Setup cannot run over Link, so in the editor the stored scan is loaded as is). If the scene already has an MRUK it is reused and
/// loads the room by its own settings.
///
/// World locking stays on: the room is placed once at load, so without it any later shift of the
/// headset's tracking origin (lost tracking, a recenter, a re-estimated floor height) leaves the colliders
/// offset from the real room. With it, MRUK nudges the camera rig every frame to keep them aligned.
/// Tracking events are logged so such shifts show up in the log.
///
/// For debugging, <see cref="CollidersVisible"/> draws the colliders as a translucent grid, coloured by
/// kind (floor and ceiling blue, walls green, furniture orange, room mesh light pink), and
/// <see cref="Rescan"/> runs Space Setup again; the control panel's developer popup exposes both.
/// </summary>
public class RoomColliders : MonoBehaviour
{
    /// <summary>On: open Space Setup at every launch. Off: load the stored scan, asking for one only if there is none.</summary>
    private static readonly bool RescanOnLaunch = false; // off while testing whether it causes the boundary circle

    private const string DebugShaderPath = "Shaders/ColliderDebug";

    private const MRUKAnchor.SceneLabels ColliderLabels =
        MRUKAnchor.SceneLabels.FLOOR |
        MRUKAnchor.SceneLabels.CEILING |
        MRUKAnchor.SceneLabels.WALL_FACE |
        MRUKAnchor.SceneLabels.INNER_WALL_FACE |
        MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE |
        MRUKAnchor.SceneLabels.TABLE |
        MRUKAnchor.SceneLabels.COUCH |
        MRUKAnchor.SceneLabels.STORAGE |
        MRUKAnchor.SceneLabels.BED |
        MRUKAnchor.SceneLabels.SCREEN |
        MRUKAnchor.SceneLabels.LAMP |
        MRUKAnchor.SceneLabels.PLANT |
        MRUKAnchor.SceneLabels.OTHER |
        // The scanned room mesh (Quest 3 / 3S): catches chairs and clutter that Space Setup has no label for.
        // Coarse and frozen at scan time; it overlaps the labelled shapes, which is harmless for physics.
        MRUKAnchor.SceneLabels.GLOBAL_MESH;

    private const MRUKAnchor.SceneLabels FloorLabels = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING;
    private const MRUKAnchor.SceneLabels WallLabels =
        MRUKAnchor.SceneLabels.WALL_FACE |
        MRUKAnchor.SceneLabels.INNER_WALL_FACE |
        MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE;

    // Collider view colours, by kind of surface.
    private static readonly Color FloorColor = new Color(0.3f, 0.6f, 1f);
    private static readonly Color WallColor = new Color(0.3f, 1f, 0.45f);
    private static readonly Color FurnitureColor = new Color(1f, 0.6f, 0.15f);
    private static readonly Color RoomMeshColor = new Color(1f, 0.7f, 0.85f);

    private static RoomColliders _instance;

    private EffectMesh _effectMesh;
    private bool _ownsMruk;
    private bool _busy;

    private Shader _debugShader;
    private Material _floorMaterial;
    private Material _wallMaterial;
    private Material _furnitureMaterial;
    private Material _roomMeshMaterial;
    private int _coloredCount = -1; // collider objects coloured so far; -1 forces a recolour

    /// <summary>True while Space Setup is open or the room is loading.</summary>
    public static bool IsBusy => _instance != null && _instance._busy;

    /// <summary>Draws the room colliders as a translucent grid, to check them against the real room.</summary>
    public static bool CollidersVisible
    {
        get => _instance != null && !_instance._effectMesh.HideMesh;
        set
        {
            if (_instance != null)
            {
                // Also applies to meshes built later, e.g. after a rescan.
                _instance._effectMesh.HideMesh = !value;
            }
        }
    }

    /// <summary>World height of the scanned floor, if a room has been loaded.</summary>
    public static bool TryGetFloorHeight(out float height)
    {
        MRUKRoom room = null;
        if (MRUK.Instance != null && MRUK.Instance.IsInitialized && MRUK.Instance.Rooms.Count > 0)
        {
            room = MRUK.Instance.GetCurrentRoom() ?? MRUK.Instance.Rooms[0];
        }
        if (room == null || room.FloorAnchors.Count == 0)
        {
            height = 0f;
            return false;
        }
        height = room.FloorAnchors[0].transform.position.y;
        return true;
    }

    /// <summary>
    /// Opens Space Setup (headset only) and reloads the room; the colliders rebuild to match.
    /// Ignored while a scan or load is already running.
    /// </summary>
    public static void Rescan()
    {
        if (_instance != null && !_instance._busy && MRUK.Instance != null)
        {
            _instance.StartCoroutine(_instance.LoadRoutine(true));
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<OVRCameraRig>() == null)
        {
            return; // MRUK requires the camera rig
        }

        var go = new GameObject("Room Colliders");
        go.SetActive(false); // configure before Awake runs

        bool ownsMruk = MRUK.Instance == null;
        if (ownsMruk)
        {
            var mruk = go.AddComponent<MRUK>();
            mruk.SceneSettings = new MRUK.MRUKSettings
            {
                DataSource = MRUK.SceneDataSource.Device,
                LoadSceneOnStartup = false, // loaded by Start below, after the rescan
            };
            mruk.EnableWorldLock = true;
        }

        var effectMesh = go.AddComponent<EffectMesh>();
        effectMesh.Labels = ColliderLabels;
        effectMesh.Colliders = true;
        // Renderers exist only for the debug view; hidden, the real room shows through passthrough.
        var debugShader = Resources.Load<Shader>(DebugShaderPath);
        effectMesh.MeshMaterial = debugShader != null ? new Material(debugShader) : null;
        effectMesh.HideMesh = true;
        effectMesh.CastShadow = false;

        _instance = go.AddComponent<RoomColliders>();
        _instance._effectMesh = effectMesh;
        _instance._ownsMruk = ownsMruk;
        _instance._debugShader = debugShader;

        go.SetActive(true);
    }

    private void Update()
    {
        // EffectMesh gives every collider object the same material; recolour by kind whenever new ones appear.
        if (CollidersVisible && _debugShader != null && _effectMesh.EffectMeshObjects.Count != _coloredCount)
        {
            ApplyDebugColors();
        }
    }

    private void ApplyDebugColors()
    {
        if (_floorMaterial == null)
        {
            _floorMaterial = DebugMaterial(FloorColor, 0.2f);
            _wallMaterial = DebugMaterial(WallColor, 0.15f);
            _furnitureMaterial = DebugMaterial(FurnitureColor, 0.25f);
            // The room mesh drapes over everything, so keep its fill faint.
            _roomMeshMaterial = DebugMaterial(RoomMeshColor, 0.08f);
        }
        foreach (var kv in _effectMesh.EffectMeshObjects)
        {
            if (kv.Value.effectMeshGO != null && kv.Value.effectMeshGO.TryGetComponent(out MeshRenderer renderer))
            {
                renderer.sharedMaterial = DebugMaterialFor(kv.Key);
            }
        }
        _coloredCount = _effectMesh.EffectMeshObjects.Count;
    }

    private Material DebugMaterialFor(MRUKAnchor anchor)
    {
        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.GLOBAL_MESH))
        {
            return _roomMeshMaterial;
        }
        if (anchor.HasAnyLabel(FloorLabels))
        {
            return _floorMaterial;
        }
        return anchor.HasAnyLabel(WallLabels) ? _wallMaterial : _furnitureMaterial;
    }

    private Material DebugMaterial(Color color, float fillAlpha)
    {
        var m = new Material(_debugShader);
        m.SetColor("_Color", new Color(color.r, color.g, color.b, fillAlpha));
        m.SetColor("_LineColor", new Color(color.r, color.g, color.b, 0.9f));
        return m;
    }

    private void OnEnable()
    {
        OVRManager.TrackingLost += OnTrackingLost;
        OVRManager.TrackingAcquired += OnTrackingAcquired;
        OVRManager.TrackingOriginChangePending += OnTrackingOriginChange;
    }

    private void OnDisable()
    {
        OVRManager.TrackingLost -= OnTrackingLost;
        OVRManager.TrackingAcquired -= OnTrackingAcquired;
        OVRManager.TrackingOriginChangePending -= OnTrackingOriginChange;
    }

    private static void OnTrackingLost() => Debug.Log("RoomColliders: headset tracking lost.");

    private static void OnTrackingAcquired() => Debug.Log("RoomColliders: headset tracking acquired.");

    private static void OnTrackingOriginChange(OVRManager.TrackingOrigin origin, OVRPose? change)
    {
        Debug.Log($"RoomColliders: tracking origin changing to {origin}, moved by {change?.position.ToString("F3") ?? "unknown"}.");
    }

    private IEnumerator Start()
    {
        if (_ownsMruk)
        {
            yield return LoadRoutine(RescanOnLaunch);
        }
    }

    private IEnumerator LoadRoutine(bool openSpaceSetup)
    {
        _busy = true;
#if !UNITY_EDITOR && UNITY_ANDROID
        // OVRManager asks for the spatial data permission at startup; the room can't be read without it.
        yield return new WaitUntil(() => Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission));
        if (openSpaceSetup)
        {
            // Pauses the app until the user finishes or cancels; cancelling keeps the previous scan.
            var setup = OVRScene.RequestSpaceSetup();
            yield return new WaitUntil(() => setup.IsCompleted);
        }
#else
        yield return null;
#endif
        // After a rescan, don't open Space Setup a second time if the user cancelled it with no room saved.
        var load = MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: !openSpaceSetup);
        yield return new WaitUntil(() => load.IsCompleted);
        _busy = false;
        _coloredCount = -1; // a reload may rebuild collider objects without changing their count
        if (load.IsFaulted)
        {
            Debug.LogException(load.Exception, this);
            yield break;
        }
        var rooms = MRUK.Instance.Rooms;
        Debug.Log(load.Result == MRUK.LoadDeviceResult.Success && rooms.Count > 0
            ? $"RoomColliders: loaded room with {rooms[0].Anchors.Count} surfaces and objects."
            : $"RoomColliders: no room loaded ({load.Result}); rubble uses the fallback ground.");
    }
}
