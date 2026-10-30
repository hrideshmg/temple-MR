using System;
using Meta.XR.MRUtilityKit;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Puts every player in the same coordinate space, so the temple stands in the same real spot for all.
///
/// The host shares its room scan (MR Utility Kit space sharing) with the session's colocation group
/// (<see cref="NetworkSession.GroupUuid"/>) and tells each guest the room and where its floor is in the
/// host's world. A guest loads that room, and MRUK moves the guest's camera rig so the shared floor
/// lines up with the host's. After that, world coordinates match on every headset, and the guest's
/// room colliders are the host's scan.
///
/// Needs a colocation group, so it only does anything on headsets (not in the editor or over Link).
/// Attach next to <see cref="NetworkSession"/>.
/// </summary>
[RequireComponent(typeof(NetworkSession))]
public class SharedSpace : MonoBehaviour
{
    public enum State
    {
        None,
        Sharing,    // host: sharing its room
        Shared,     // host: room shared, guests can align
        Aligning,   // guest: loading the host's room
        Aligned,    // guest: in the host's space
        Failed,
    }

    private const string MessageName = "TempleMR.SharedSpace";

    [SerializeField]
    [Tooltip("Seconds the host waits for its own room scan to load before giving up on sharing.")]
    private float _roomWaitTimeout = 20f;

    public static SharedSpace Instance { get; private set; }

    public State Current { get; private set; }

    /// <summary>Why sharing or aligning failed, or empty.</summary>
    public string Error { get; private set; } = "";

    /// <summary>True while this guest's room is the host's shared scan (so it must not be rescanned).</summary>
    public static bool UsingHostRoom => Instance != null && Instance.Current == State.Aligned;

    private NetworkManager _network;
    private NetworkSession _session;

    // Host: what guests need to align, once the room is shared.
    private bool _spaceReady;
    private Guid _roomUuid;
    private Pose _floorPose;

    private void Awake()
    {
        Instance = this;
        _network = GetComponent<NetworkManager>();
        _session = GetComponent<NetworkSession>();
        _session.StateChanged += OnSessionChanged;
        _network.OnClientStarted += OnNetworkStarted;
        _network.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.StateChanged -= OnSessionChanged;
        }
        if (_network != null)
        {
            _network.OnClientStarted -= OnNetworkStarted;
            _network.OnClientConnectedCallback -= OnClientConnected;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnSessionChanged()
    {
        switch (_session.Current)
        {
            case NetworkSession.State.Offline:
                // A guest stays where it was aligned; its room stays the host's until the next load or rescan.
                _spaceReady = false;
                SetState(State.None, "");
                break;
            case NetworkSession.State.Hosting when Current == State.None && _session.GroupUuid != Guid.Empty:
                ShareRoom(_session.GroupUuid);
                break;
        }
    }

    private void OnNetworkStarted()
    {
        // Runs on host and guests; the messaging manager is recreated on every start.
        _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnSpaceMessage);
    }

    // =====================================================================  host

    private async void ShareRoom(Guid group)
    {
        SetState(State.Sharing, "");

        // The room loads after startup (see RoomColliders); wait for it.
        float deadline = Time.unscaledTime + _roomWaitTimeout;
        MRUKRoom room = CurrentRoom();
        while (room == null || RoomColliders.IsBusy)
        {
            if (Time.unscaledTime > deadline || _session.Current != NetworkSession.State.Hosting)
            {
                SetState(State.Failed, "No room scan to share");
                return;
            }
            await Awaitable.NextFrameAsync();
            room = CurrentRoom();
        }

        try
        {
            var result = await room.ShareRoomAsync(group);
            if (_session.Current != NetworkSession.State.Hosting)
            {
                return;
            }
            if (!result.Success)
            {
                SetState(State.Failed, $"Couldn't share the room ({result.Status})");
                return;
            }
        }
        catch (Exception e)
        {
            SetState(State.Failed, $"Couldn't share the room ({e.Message})");
            return;
        }

        // MRUK aligns on the room's first floor anchor (what the obsolete FloorAnchor property returned).
        Transform floor = room.FloorAnchors[0].transform;
        _roomUuid = room.Anchor.Uuid;
        _floorPose = new Pose(floor.position, floor.rotation);
        _spaceReady = true;
        SetState(State.Shared, "");

        foreach (ulong clientId in _network.ConnectedClientsIds)
        {
            SendSpace(clientId);
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (_network.IsServer && _spaceReady)
        {
            SendSpace(clientId);
        }
    }

    private void SendSpace(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId)
        {
            return;
        }
        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteValueSafe(_session.GroupUuid.ToString());
        writer.WriteValueSafe(_roomUuid.ToString());
        writer.WriteValueSafe(_floorPose.position);
        writer.WriteValueSafe(_floorPose.rotation);
        _network.CustomMessagingManager.SendNamedMessage(MessageName, clientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private static MRUKRoom CurrentRoom()
    {
        if (MRUK.Instance == null || !MRUK.Instance.IsInitialized || MRUK.Instance.Rooms.Count == 0)
        {
            return null;
        }
        var room = MRUK.Instance.GetCurrentRoom() ?? MRUK.Instance.Rooms[0];
        return room.FloorAnchors.Count > 0 ? room : null;
    }

    // =====================================================================  guest

    private void OnSpaceMessage(ulong senderId, FastBufferReader reader)
    {
        if (_network.IsServer)
        {
            return;
        }
        reader.ReadValueSafe(out string group);
        reader.ReadValueSafe(out string room);
        reader.ReadValueSafe(out Vector3 floorPosition);
        reader.ReadValueSafe(out Quaternion floorRotation);
        Align(Guid.Parse(group), Guid.Parse(room), new Pose(floorPosition, floorRotation));
    }

    private async void Align(Guid group, Guid room, Pose floorOnHost)
    {
        if (MRUK.Instance == null)
        {
            SetState(State.Failed, "MR Utility Kit isn't running");
            return;
        }
        SetState(State.Aligning, "");
        try
        {
            var result = await MRUK.Instance.LoadSceneFromSharedRooms(group, (room, floorOnHost));
            if (_session.Current != NetworkSession.State.Joined)
            {
                return;
            }
            if (result == MRUK.LoadDeviceResult.Success)
            {
                SetState(State.Aligned, "");
            }
            else
            {
                SetState(State.Failed, $"Couldn't load the host's room ({result})");
            }
        }
        catch (Exception e)
        {
            SetState(State.Failed, $"Couldn't load the host's room ({e.Message})");
        }
    }

    private void SetState(State state, string error)
    {
        Current = state;
        Error = error;
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning($"SharedSpace: {error}", this);
        }
    }
}
