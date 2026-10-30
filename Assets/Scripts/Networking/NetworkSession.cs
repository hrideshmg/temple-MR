using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Hosts or joins a multiplayer session over the local network, with a direct connection between
/// headsets (no relay server). One headset hosts and is the authority for the shared temple state;
/// the others join it.
///
/// Finding the host: the host advertises its address over Colocation Discovery (Bluetooth), and
/// <see cref="Join()"/> listens for it. The advertisement's UUID doubles as the colocation group that
/// <see cref="SharedSpace"/> shares the room with. Where discovery isn't available (the editor, Link)
/// or finds nobody, Join falls back to the serialized join address.
///
/// Without a session nothing changes: the app runs single-player as before.
/// In the editor and development builds, H / J / L host, join and leave from the keyboard.
/// Attach next to the scene's <see cref="NetworkManager"/> and <see cref="UnityTransport"/>.
/// </summary>
[RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
public class NetworkSession : MonoBehaviour
{
    public enum State
    {
        Offline,
        Hosting,
        Searching,
        Joining,
        Joined,
    }

    // Leads every advertisement, so other apps' colocation sessions are ignored.
    private const string AdvertisementTag = "TempleMR/1";

    [SerializeField]
    [Tooltip("UDP port the host listens on and clients connect to.")]
    private ushort _port = 7777;

    [SerializeField]
    [Tooltip("Fallback address to join when no host is discovered nearby. 127.0.0.1 joins a host on this machine.")]
    private string _joinAddress = "127.0.0.1";

    [SerializeField]
    [Tooltip("Seconds Join looks for a nearby host before falling back to the join address.")]
    private float _searchTimeout = 15f;

    [SerializeField]
    [Tooltip("Seconds a join keeps trying to connect before giving up.")]
    private int _joinTimeout = 10;

    public static NetworkSession Instance { get; private set; }

    public State Current { get; private set; }

    /// <summary>While hosting: this headset's LAN address. While joining or joined: the host's.</summary>
    public string Address { get; private set; } = "";

    /// <summary>
    /// The colocation group for this session (the host's advertisement UUID), or empty when there is
    /// none, e.g. discovery isn't available or the join went by address.
    /// </summary>
    public Guid GroupUuid { get; private set; }

    /// <summary>Why the last session ended or failed, or empty.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>Connected players including the host. Only known on the host.</summary>
    public int PlayerCount => Current == State.Hosting ? _network.ConnectedClientsIds.Count : 0;

    public event Action StateChanged;

    private NetworkManager _network;
    private UnityTransport _transport;
    private bool _advertising;
    private bool _discovering;
    private float _searchDeadline;

    private void Awake()
    {
        Instance = this;
        _network = GetComponent<NetworkManager>();
        _transport = GetComponent<UnityTransport>();
        _network.OnClientConnectedCallback += OnClientConnected;
        _network.OnClientDisconnectCallback += OnClientDisconnected;
        _network.OnTransportFailure += OnTransportFailure;
    }

    private void OnDestroy()
    {
        StopDiscovery();
        StopAdvertising();
        if (_network != null)
        {
            _network.OnClientConnectedCallback -= OnClientConnected;
            _network.OnClientDisconnectCallback -= OnClientDisconnected;
            _network.OnTransportFailure -= OnTransportFailure;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (Current == State.Searching && Time.unscaledTime > _searchDeadline)
        {
            StopDiscovery();
            FallBackToJoinAddress("No host found nearby");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Testing shortcuts for Multiplayer Play Mode players, which have no headset to poke the panel with.
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null)
        {
            return;
        }
        if (keyboard.hKey.wasPressedThisFrame)
        {
            Host();
        }
        else if (keyboard.jKey.wasPressedThisFrame)
        {
            Join();
        }
        else if (keyboard.lKey.wasPressedThisFrame)
        {
            Leave();
        }
#endif
    }

    // =====================================================================  host

    public void Host()
    {
        if (Current != State.Offline)
        {
            return;
        }
        Address = LocalAddress();
        _transport.SetConnectionData(Address, _port, "0.0.0.0");
        if (!_network.StartHost())
        {
            SetState(State.Offline, $"Couldn't host on port {_port}");
            return;
        }
        SetState(State.Hosting, "");
        Advertise();
    }

    private async void Advertise()
    {
        byte[] data = Encoding.UTF8.GetBytes($"{AdvertisementTag} {Address} {_port}");
        try
        {
            var result = await OVRColocationSession.StartAdvertisementAsync(data);
            if (!result.Success)
            {
                Debug.LogWarning($"NetworkSession: nearby headsets won't discover this host ({result.Status}); " +
                                 "they can still join by address.", this);
                return;
            }
            _advertising = true;
            if (Current != State.Hosting)
            {
                StopAdvertising(); // left while the advertisement was starting
                return;
            }
            GroupUuid = result.Value;
            StateChanged?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"NetworkSession: colocation advertising unavailable ({e.Message}).", this);
        }
    }

    private async void StopAdvertising()
    {
        if (!_advertising)
        {
            return;
        }
        _advertising = false;
        try
        {
            await OVRColocationSession.StopAdvertisementAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"NetworkSession: couldn't stop advertising ({e.Message}).", this);
        }
    }

    // =====================================================================  join

    /// <summary>Looks for a nearby host and joins it; falls back to the join address.</summary>
    public async void Join()
    {
        if (Current != State.Offline)
        {
            return;
        }
        SetState(State.Searching, "");
        _searchDeadline = Time.unscaledTime + _searchTimeout;

        OVRColocationSession.ColocationSessionDiscovered += OnHostDiscovered;
        bool started;
        try
        {
            var result = await OVRColocationSession.StartDiscoveryAsync();
            started = result.Success;
            if (!started)
            {
                Debug.LogWarning($"NetworkSession: colocation discovery unavailable ({result.Status}).", this);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"NetworkSession: colocation discovery unavailable ({e.Message}).", this);
            started = false;
        }

        _discovering = started;
        if (Current != State.Searching)
        {
            StopDiscovery(); // cancelled, or a host was found while discovery was starting
            return;
        }
        if (!started)
        {
            OVRColocationSession.ColocationSessionDiscovered -= OnHostDiscovered;
            FallBackToJoinAddress("Couldn't search for nearby hosts");
        }
    }

    /// <summary>Joins the host at the given address directly, without discovery.</summary>
    public void Join(string address)
    {
        if (Current != State.Offline && Current != State.Searching)
        {
            return;
        }
        Connect(address, _port);
    }

    private void OnHostDiscovered(OVRColocationSession.Data data)
    {
        if (Current != State.Searching || data.Metadata == null)
        {
            return;
        }
        // "<tag> <address> <port>"
        string[] parts = Encoding.UTF8.GetString(data.Metadata).Split(' ');
        if (parts.Length != 3 || parts[0] != AdvertisementTag || !ushort.TryParse(parts[2], out ushort port))
        {
            return;
        }
        StopDiscovery();
        GroupUuid = data.AdvertisementUuid;
        Connect(parts[1], port);
    }

    private void FallBackToJoinAddress(string reason)
    {
        // Loopback only makes sense when the host runs on this machine, i.e. in the editor.
        if (Application.isEditor || !IPAddress.TryParse(_joinAddress, out var ip) || !IPAddress.IsLoopback(ip))
        {
            Connect(_joinAddress, _port);
        }
        else
        {
            SetState(State.Offline, reason);
        }
    }

    private void Connect(string address, ushort port)
    {
        Address = address;
        _transport.SetConnectionData(address, port);
        _transport.ConnectTimeoutMS = 1000;
        _transport.MaxConnectAttempts = Mathf.Max(1, _joinTimeout);
        if (_network.StartClient())
        {
            SetState(State.Joining, "");
        }
        else
        {
            SetState(State.Offline, $"Couldn't join {address}");
        }
    }

    private void StopDiscovery()
    {
        OVRColocationSession.ColocationSessionDiscovered -= OnHostDiscovered;
        if (!_discovering)
        {
            return;
        }
        _discovering = false;
        StopDiscoveryAsync();
    }

    private async void StopDiscoveryAsync()
    {
        try
        {
            await OVRColocationSession.StopDiscoveryAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"NetworkSession: couldn't stop discovery ({e.Message}).", this);
        }
    }

    // =====================================================================  leave

    /// <summary>Ends the session (or cancels a search or join). The scene stays as it is, single-player.</summary>
    public void Leave()
    {
        if (Current == State.Offline)
        {
            return;
        }
        StopDiscovery();
        StopAdvertising();
        if (_network.IsListening)
        {
            _network.Shutdown();
        }
        SetState(State.Offline, "");
    }

    // =====================================================================  network events

    private void OnClientConnected(ulong clientId)
    {
        if (Current == State.Joining && clientId == _network.LocalClientId)
        {
            SetState(State.Joined, "");
        }
        else if (Current == State.Hosting)
        {
            StateChanged?.Invoke(); // player count changed
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (Current == State.Hosting)
        {
            if (clientId != _network.LocalClientId)
            {
                StateChanged?.Invoke(); // player count changed
            }
            return;
        }
        if (Current == State.Joining || Current == State.Joined)
        {
            // Our own connection ended: the host left, or a join timed out.
            string reason = Current == State.Joining
                ? $"Couldn't reach {Address}"
                : string.IsNullOrEmpty(_network.DisconnectReason) ? "Host ended the session" : _network.DisconnectReason;
            _network.Shutdown();
            SetState(State.Offline, reason);
        }
    }

    private void OnTransportFailure()
    {
        StopAdvertising();
        SetState(State.Offline, "Network error");
    }

    private void SetState(State state, string error)
    {
        Current = state;
        LastError = error;
        if (state == State.Offline)
        {
            Address = "";
            GroupUuid = Guid.Empty;
        }
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning($"NetworkSession: {error}", this);
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// This device's address on the local network: the one its default route would use.
    /// Connecting a UDP socket sends nothing; it only picks the outgoing interface.
    /// </summary>
    private static string LocalAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch (SocketException)
        {
            // No route out (offline network): fall back to the interface list.
        }
        foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            {
                return address.ToString();
            }
        }
        return "127.0.0.1";
    }
}
