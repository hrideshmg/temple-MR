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
/// Spawns itself after the scene loads; nothing needs to be placed in the scene. On the headset it opens
/// Space Setup on every launch so the room is rescanned before it loads (Space Setup cannot run over Link,
/// so in the editor the stored scan is loaded as is). If the scene already has an MRUK it is reused and
/// loads the room by its own settings. World locking is left off so loading the room never moves the
/// camera rig (and with it everything placed relative to the player at startup).
/// </summary>
public class RoomColliders : MonoBehaviour
{
    /// <summary>On: open Space Setup at every launch. Off: load the stored scan, asking for one only if there is none.</summary>
    private static readonly bool RescanOnLaunch = true;

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
        MRUKAnchor.SceneLabels.OTHER;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<OVRCameraRig>() == null)
        {
            return; // MRUK requires the camera rig
        }

        var go = new GameObject("Room Colliders");
        go.SetActive(false); // configure before Awake runs

        if (MRUK.Instance == null)
        {
            var mruk = go.AddComponent<MRUK>();
            mruk.SceneSettings = new MRUK.MRUKSettings
            {
                DataSource = MRUK.SceneDataSource.Device,
                LoadSceneOnStartup = false, // loaded by Start below, after the rescan
            };
            mruk.EnableWorldLock = false;
            go.AddComponent<RoomColliders>();
        }

        var effectMesh = go.AddComponent<EffectMesh>();
        effectMesh.Labels = ColliderLabels;
        effectMesh.Colliders = true;
        effectMesh.MeshMaterial = null; // no renderer, colliders only; the real room shows through passthrough

        go.SetActive(true);
    }

    private IEnumerator Start()
    {
#if !UNITY_EDITOR && UNITY_ANDROID
        // OVRManager asks for the spatial data permission at startup; the room can't be read without it.
        yield return new WaitUntil(() => Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission));
        if (RescanOnLaunch)
        {
            // Pauses the app until the user finishes or cancels; cancelling keeps the previous scan.
            var setup = OVRScene.RequestSpaceSetup();
            yield return new WaitUntil(() => setup.IsCompleted);
        }
#else
        yield return null;
#endif
        LoadRoom();
    }

    private async void LoadRoom()
    {
        // After a rescan, don't open Space Setup a second time if the user cancelled it with no room saved.
        MRUK.LoadDeviceResult result = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: !RescanOnLaunch);
        var rooms = MRUK.Instance.Rooms;
        Debug.Log(result == MRUK.LoadDeviceResult.Success && rooms.Count > 0
            ? $"RoomColliders: loaded room with {rooms[0].Anchors.Count} surfaces and objects."
            : $"RoomColliders: no room loaded ({result}); rubble uses the fallback ground.");
    }
}
