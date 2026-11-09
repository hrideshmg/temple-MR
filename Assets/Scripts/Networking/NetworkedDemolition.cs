using System.Collections.Generic;
using Oculus.Interaction;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Demolishes and rebuilds the temple for every player, with the rubble landing in the same places.
///
/// Demolish / Rebuild from any player goes through the host. Every headset plays the tremble and swaps
/// in its own copy of the rubble (the fracture is seeded, so the pieces match), but only the host
/// simulates it: guests' pieces are <see cref="TempleDemolition.Driven"/> and follow the poses the host
/// streams (pieces that moved, ~15 times a second, plus a full snapshot every couple of seconds and
/// for players who join mid-collapse).
///
/// Guests can still pick up and throw rubble: while a guest holds a piece, its pose is sent to the
/// host, and on release the throw velocity, so the host carries on simulating it.
///
/// For the rebuild puzzle (<see cref="TemplePuzzle"/>) the host decides every placement, including
/// guests' (checked when their throw arrives), and shares which pieces are in place as a bit mask.
///
/// Without a session this does nothing and <see cref="TempleDemolition"/> works as before.
/// Attach to the Temple root, next to <see cref="TempleDemolition"/> and <see cref="NetworkedTemple"/>.
/// </summary>
[RequireComponent(typeof(TempleDemolition), typeof(NetworkedTemple))]
public class NetworkedDemolition : NetworkBehaviour
{
    private const string PoseMessage = "TempleMR.RubblePoses";
    private const int PiecesPerMessage = 32;         // keeps an unreliable message within one packet
    private const float MinMove = 0.001f;            // metres
    private const float MinTurn = 0.5f;              // degrees

    [SerializeField]
    [Tooltip("Rubble pose updates per second while pieces are moving.")]
    private float _sendRate = 15f;

    [SerializeField]
    [Tooltip("Seconds between full, reliable rubble snapshots while demolished.")]
    private float _snapshotInterval = 2f;

    [SerializeField]
    [Tooltip("How quickly guests' pieces catch up with the host's poses.")]
    private float _smoothing = 18f;

    private readonly NetworkVariable<bool> _demolished = new NetworkVariable<bool>();
    private readonly NetworkVariable<ulong> _placed = new NetworkVariable<ulong>(); // puzzle: bit i = piece i in place

    private TemplePuzzle _puzzle;

    // Added by TempleDemolition at Start, so looked up lazily.
    private TemplePuzzle Puzzle => _puzzle != null ? _puzzle : (_puzzle = GetComponent<TemplePuzzle>());

    private TempleDemolition _demolition;
    private NetworkedTemple _temple;

    // Host: what was last sent for each piece.
    private Vector3[] _sentPosition;
    private Quaternion[] _sentRotation;
    private float _sendTimer;
    private float _snapshotTimer;

    // Guest: where the host says each piece is, and which pieces this player is holding.
    private Vector3[] _targetPosition;
    private Quaternion[] _targetRotation;
    private bool[] _hasTarget;
    private Grabbable[] _grabbables;
    private bool[] _held;
    private Vector3[] _lastHeldPosition;
    private Quaternion[] _lastHeldRotation;
    private Vector3[] _heldVelocity;
    private Vector3[] _heldAngularVelocity;

    private void Awake()
    {
        _demolition = GetComponent<TempleDemolition>();
        _temple = GetComponent<NetworkedTemple>();
    }

    /// <summary>Demolish / Rebuild for every player; acts locally without a session.</summary>
    public void RequestToggle()
    {
        if (!IsSpawned)
        {
            _demolition.Toggle();
            return;
        }
        ToggleRpc();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _demolished.Value = _demolition.IsDemolished;
        }
        // Registered on the host too: sending to all delivers to the host's own client as well.
        NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(PoseMessage, OnPoses);
    }

    public override void OnNetworkDespawn()
    {
        NetworkManager?.CustomMessagingManager?.UnregisterNamedMessageHandler(PoseMessage);
        // Session over: the rubble stays where it is and simulates locally again.
        if (_demolition.Driven)
        {
            _demolition.Driven = false;
            if (_demolition.IsDemolished)
            {
                for (int i = 0; i < _demolition.PieceCount; i++)
                {
                    _demolition.ThrowPiece(i, Vector3.zero, Vector3.zero);
                }
            }
        }
        _sentPosition = null;
        _targetPosition = null;
    }

    private void Update()
    {
        if (!IsSpawned || !_demolition.IsReady)
        {
            return;
        }
        if (IsServer)
        {
            UpdateHost();
        }
        else
        {
            UpdateGuest();
        }
    }

    [Rpc(SendTo.Server)]
    private void ToggleRpc()
    {
        if (!_demolition.IsReady)
        {
            return;
        }
        if (_demolition.IsDemolished)
        {
            _demolition.Rebuild(); // the temple goes back to where it stood, sent by its NetworkTransform
            _demolished.Value = false;
        }
        else if (!_temple.IsHeld)
        {
            _temple.TakeOwnershipForHost();
            _demolition.Demolish();
            _demolished.Value = true;
        }
    }

    private void UpdateHost()
    {
        if (Puzzle != null && _placed.Value != Puzzle.PlacedMask)
        {
            _placed.Value = Puzzle.PlacedMask;
        }
        if (!_demolition.IsDemolished)
        {
            return;
        }
        int count = _demolition.PieceCount;
        if (_sentPosition == null || _sentPosition.Length != count)
        {
            _sentPosition = new Vector3[count];
            _sentRotation = new Quaternion[count];
            _snapshotTimer = 0f; // send everything on the next tick
        }

        _sendTimer -= Time.deltaTime;
        _snapshotTimer -= Time.deltaTime;
        if (_sendTimer > 0f)
        {
            return;
        }
        _sendTimer = 1f / _sendRate;

        bool snapshot = _snapshotTimer <= 0f;
        if (snapshot)
        {
            _snapshotTimer = _snapshotInterval;
        }

        var changed = new List<int>();
        for (int i = 0; i < count; i++)
        {
            Transform t = _demolition.GetPieceBody(i).transform;
            if (snapshot
                || (t.position - _sentPosition[i]).sqrMagnitude > MinMove * MinMove
                || Quaternion.Angle(t.rotation, _sentRotation[i]) > MinTurn)
            {
                changed.Add(i);
                _sentPosition[i] = t.position;
                _sentRotation[i] = t.rotation;
            }
        }
        SendPoses(changed, snapshot ? NetworkDelivery.ReliableSequenced : NetworkDelivery.UnreliableSequenced, null);
    }

    /// <summary>Sends piece poses in world space (shared by every aligned player) to one client, or all.</summary>
    private void SendPoses(List<int> pieces, NetworkDelivery delivery, ulong? clientId)
    {
        for (int start = 0; start < pieces.Count; start += PiecesPerMessage)
        {
            int n = Mathf.Min(PiecesPerMessage, pieces.Count - start);
            using var writer = new FastBufferWriter(8 + n * 30, Allocator.Temp);
            writer.WriteValueSafe((ushort)_demolition.PieceCount);
            writer.WriteValueSafe((ushort)n);
            for (int k = 0; k < n; k++)
            {
                int i = pieces[start + k];
                Transform t = _demolition.GetPieceBody(i).transform;
                writer.WriteValueSafe((ushort)i);
                writer.WriteValueSafe(t.position);
                writer.WriteValueSafe(t.rotation);
            }
            var messaging = NetworkManager.CustomMessagingManager;
            if (clientId.HasValue)
            {
                messaging.SendNamedMessage(PoseMessage, clientId.Value, writer, delivery);
            }
            else
            {
                messaging.SendNamedMessageToAll(PoseMessage, writer, delivery);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestSnapshotRpc(RpcParams rpcParams = default)
    {
        if (!_demolition.IsDemolished)
        {
            return;
        }
        var all = new List<int>();
        for (int i = 0; i < _demolition.PieceCount; i++)
        {
            all.Add(i);
        }
        SendPoses(all, NetworkDelivery.ReliableSequenced, rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void HoldPieceRpc(ushort index, Vector3 position, Quaternion rotation)
    {
        if (!_demolition.IsDemolished || index >= _demolition.PieceCount || _demolition.IsPieceLocked(index))
        {
            return;
        }
        Rigidbody rb = _demolition.GetPieceBody(index);
        rb.isKinematic = true;
        rb.MovePosition(position);
        rb.MoveRotation(rotation);
    }

    [Rpc(SendTo.Server)]
    private void ThrowPieceRpc(ushort index, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        if (!_demolition.IsDemolished || index >= _demolition.PieceCount || _demolition.IsPieceLocked(index))
        {
            return;
        }
        Rigidbody rb = _demolition.GetPieceBody(index);
        rb.transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation;
        _demolition.ThrowPiece(index, velocity, angularVelocity);
        // Let go near its slot: the puzzle may set it in place.
        Puzzle?.TrySnap(index);
    }

    private void UpdateGuest()
    {
        // Follow the host's demolished state (also catches up once the local fracture is ready).
        if (_demolished.Value && !_demolition.IsDemolished)
        {
            _demolition.Driven = true;
            _demolition.Demolish();
            RequestSnapshotRpc();
        }
        else if (!_demolished.Value && _demolition.IsDemolished)
        {
            _demolition.Rebuild();
        }
        if (!_demolition.IsDemolished)
        {
            return;
        }
        Puzzle?.ApplyPlacedMask(_placed.Value);

        EnsureGuestArrays();
        float dt = Time.deltaTime;
        float k = 1f - Mathf.Exp(-_smoothing * dt);
        for (int i = 0; i < _grabbables.Length; i++)
        {
            Transform t = _demolition.GetPieceBody(i).transform;
            // SelectingPoints is null until the piece first wakes (the rubble stays inactive while the temple trembles).
            bool held = _grabbables[i] != null && _grabbables[i].SelectingPoints != null
                        && _grabbables[i].SelectingPointsCount > 0 && !_demolition.IsPieceLocked(i);
            if (held)
            {
                TrackHeldPiece(i, t, dt);
                HoldPieceRpc((ushort)i, t.position, t.rotation);
            }
            else if (_held[i])
            {
                // Let go: the host simulates the throw from here.
                _held[i] = false;
                ThrowPieceRpc((ushort)i, t.position, t.rotation, _heldVelocity[i], _heldAngularVelocity[i]);
                _targetPosition[i] = t.position;
                _targetRotation[i] = t.rotation;
            }
            else if (_hasTarget[i])
            {
                t.SetPositionAndRotation(Vector3.Lerp(t.position, _targetPosition[i], k),
                    Quaternion.Slerp(t.rotation, _targetRotation[i], k));
            }
        }
    }

    private void TrackHeldPiece(int i, Transform t, float dt)
    {
        if (_held[i] && dt > 0f)
        {
            // Smoothed hand velocity, handed to the host as the throw on release.
            Vector3 velocity = (t.position - _lastHeldPosition[i]) / dt;
            (t.rotation * Quaternion.Inverse(_lastHeldRotation[i])).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }
            Vector3 angular = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
            _heldVelocity[i] = Vector3.Lerp(_heldVelocity[i], velocity, 0.5f);
            _heldAngularVelocity[i] = Vector3.Lerp(_heldAngularVelocity[i], angular, 0.5f);
        }
        else
        {
            _heldVelocity[i] = Vector3.zero;
            _heldAngularVelocity[i] = Vector3.zero;
        }
        _held[i] = true;
        _lastHeldPosition[i] = t.position;
        _lastHeldRotation[i] = t.rotation;
    }

    private void OnPoses(ulong senderId, FastBufferReader reader)
    {
        if (IsServer || !_demolition.IsReady)
        {
            return;
        }
        EnsureGuestArrays();
        reader.ReadValueSafe(out ushort hostCount);
        reader.ReadValueSafe(out ushort n);
        if (hostCount != _demolition.PieceCount)
        {
            // Only happens if the fracture came out differently here (e.g. editor vs headset float maths).
            Debug.LogWarning($"NetworkedDemolition: host has {hostCount} rubble pieces, this device {_demolition.PieceCount}.", this);
        }
        for (int k = 0; k < n; k++)
        {
            reader.ReadValueSafe(out ushort i);
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out Quaternion rotation);
            if (i < _targetPosition.Length && !_held[i])
            {
                _targetPosition[i] = position;
                _targetRotation[i] = rotation;
                _hasTarget[i] = true;
            }
        }
    }

    private void EnsureGuestArrays()
    {
        int count = _demolition.PieceCount;
        if (_targetPosition != null && _targetPosition.Length == count)
        {
            return;
        }
        _targetPosition = new Vector3[count];
        _targetRotation = new Quaternion[count];
        _hasTarget = new bool[count];
        _grabbables = new Grabbable[count];
        _held = new bool[count];
        _lastHeldPosition = new Vector3[count];
        _lastHeldRotation = new Quaternion[count];
        _heldVelocity = new Vector3[count];
        _heldAngularVelocity = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            _grabbables[i] = _demolition.GetPieceBody(i).GetComponent<Grabbable>();
        }
    }
}
