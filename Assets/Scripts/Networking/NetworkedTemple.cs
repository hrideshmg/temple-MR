using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lets any player pick up the temple in a multiplayer session, one player at a time.
///
/// The temple's <see cref="Unity.Netcode.Components.NetworkTransform"/> is owner-authoritative: whoever
/// owns the temple moves it and everyone else follows. Grabbing it asks the host for ownership; the
/// host hands it over unless someone else is already holding it, in which case the grab is cancelled.
/// While another player holds it, it can't be grabbed here at all. Ownership stays with the last
/// player to let go, and returns to the host if they disconnect.
///
/// Without a session this does nothing and the temple behaves as in single-player.
/// Attach to the Temple root, next to its <see cref="Grabbable"/> and <see cref="NetworkObject"/>.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkedTemple : NetworkBehaviour
{
    private const ulong Nobody = ulong.MaxValue;

    private readonly NetworkVariable<ulong> _holder = new NetworkVariable<ulong>(Nobody);

    private Grabbable _grabbable;
    private TempleDemolition _demolition;
    private readonly List<Behaviour> _interactables = new List<Behaviour>();
    private bool _heldLocally;

    /// <summary>True while another player is holding the temple.</summary>
    public bool IsHeldByOther => IsSpawned && _holder.Value != Nobody && _holder.Value != NetworkManager.LocalClientId;

    private void Awake()
    {
        _grabbable = GetComponent<Grabbable>();
        _demolition = GetComponent<TempleDemolition>();
        _interactables.AddRange(GetComponentsInChildren<HandGrabInteractable>(true));
        _interactables.AddRange(GetComponentsInChildren<GrabInteractable>(true));

        // Keep the temple in the scene when its owner leaves; ownership goes back to the host.
        NetworkObject.DontDestroyWithOwner = true;
    }

    public override void OnNetworkSpawn()
    {
        _holder.OnValueChanged += OnHolderChanged;
        if (_demolition != null)
        {
            _demolition.DemolishedChanged += OnDemolishedChanged;
        }
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
        ApplyLock();
    }

    public override void OnNetworkDespawn()
    {
        _holder.OnValueChanged -= OnHolderChanged;
        if (_demolition != null)
        {
            _demolition.DemolishedChanged -= OnDemolishedChanged;
        }
        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        // Session over: back to single-player.
        _heldLocally = false;
        ApplyLock();
    }

    private void Update()
    {
        if (!IsSpawned || _grabbable == null)
        {
            return;
        }
        // Polled rather than event-driven, so it doesn't depend on when the grabbable updates its count.
        bool held = _grabbable.SelectingPointsCount > 0;
        if (held == _heldLocally)
        {
            return;
        }
        _heldLocally = held;
        if (held)
        {
            GrabRpc();
        }
        else
        {
            ReleaseRpc();
        }
    }

    // =====================================================================  host

    [Rpc(SendTo.Server)]
    private void GrabRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (_holder.Value != Nobody && _holder.Value != sender)
        {
            DenyRpc(RpcTarget.Single(sender, RpcTargetUse.Temp));
            return;
        }
        _holder.Value = sender;
        if (NetworkObject.OwnerClientId != sender)
        {
            NetworkObject.ChangeOwnership(sender);
        }
    }

    [Rpc(SendTo.Server)]
    private void ReleaseRpc(RpcParams rpcParams = default)
    {
        if (_holder.Value == rpcParams.Receive.SenderClientId)
        {
            _holder.Value = Nobody;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_holder.Value == clientId)
        {
            _holder.Value = Nobody;
        }
    }

    // =====================================================================  everyone

    [Rpc(SendTo.SpecifiedInParams)]
    private void DenyRpc(RpcParams rpcParams)
    {
        // Someone else got there first: drop the grab we started.
        StartCoroutine(CancelGrab());
    }

    private IEnumerator CancelGrab()
    {
        SetInteractables(false); // disabling an interactable cancels its selection
        yield return null;
        ApplyLock();
    }

    private void OnHolderChanged(ulong previous, ulong current)
    {
        ApplyLock();
    }

    private void OnDemolishedChanged(bool demolished)
    {
        // Rebuilding re-enables the grab components; keep them off if someone else is holding the temple.
        if (!demolished)
        {
            ApplyLock();
        }
    }

    private void ApplyLock()
    {
        if (IsHeldByOther)
        {
            SetInteractables(false);
        }
        else if (_demolition == null || !_demolition.IsDemolished)
        {
            // While demolished, TempleDemolition keeps them off.
            SetInteractables(true);
        }
    }

    private void SetInteractables(bool enabled)
    {
        foreach (var b in _interactables)
        {
            b.enabled = enabled;
        }
    }
}
