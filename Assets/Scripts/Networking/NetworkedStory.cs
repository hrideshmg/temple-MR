using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps the story narration in step on every headset in a multiplayer session.
///
/// The host holds the playback state: playing since a server time, or paused at a position. Play /
/// Pause from any player goes through the host. Each headset plays the clip locally and seeks to
/// match the shared clock whenever it drifts by more than <see cref="_maxDrift"/>, so everyone in the
/// room hears the same line at the same time, including players who join part-way through.
///
/// Without a session this does nothing and <see cref="StoryAudioController"/> plays as before.
/// Attach next to the <see cref="StoryAudioController"/>, with a <see cref="NetworkObject"/>.
/// </summary>
[RequireComponent(typeof(NetworkObject), typeof(StoryAudioController))]
public class NetworkedStory : NetworkBehaviour
{
    [SerializeField]
    [Tooltip("Seconds playback may drift from the shared clock before it's corrected with a seek.")]
    private float _maxDrift = 0.25f;

    private readonly NetworkVariable<bool> _playing = new NetworkVariable<bool>();
    private readonly NetworkVariable<double> _startTime = new NetworkVariable<double>(); // server time at clip position 0
    private readonly NetworkVariable<float> _pausedAt = new NetworkVariable<float>();

    private StoryAudioController _story;

    private void Awake()
    {
        _story = GetComponent<StoryAudioController>();
    }

    /// <summary>Play / Pause for every player; plays locally without a session.</summary>
    public void TogglePlayPause()
    {
        if (!IsSpawned)
        {
            _story.TogglePlayPause();
            return;
        }
        ToggleRpc();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Carry on from wherever the host's story was when the session started.
            float position = _story.PlaybackTime;
            _pausedAt.Value = position;
            _startTime.Value = ServerTime - position;
            _playing.Value = _story.IsPlaying;
        }
    }

    private double ServerTime => NetworkManager.ServerTime.Time;

    [Rpc(SendTo.Server)]
    private void ToggleRpc()
    {
        if (_playing.Value)
        {
            _pausedAt.Value = (float)(ServerTime - _startTime.Value);
            _playing.Value = false;
        }
        else
        {
            _startTime.Value = ServerTime - _pausedAt.Value;
            _playing.Value = true;
        }
    }

    private void Update()
    {
        if (!IsSpawned || _story.Length <= 0f)
        {
            return;
        }

        if (!_playing.Value)
        {
            if (_story.IsPlaying || Mathf.Abs(_story.PlaybackTime - _pausedAt.Value) > _maxDrift)
            {
                _story.PauseAt(_pausedAt.Value);
            }
            return;
        }

        double position = ServerTime - _startTime.Value;
        // A local clip can finish a moment before the shared clock does; don't restart its last frames.
        if (position >= _story.Length - 0.1f)
        {
            // Finished: back to the start, ready to play again.
            if (IsServer)
            {
                _playing.Value = false;
                _pausedAt.Value = 0f;
            }
            return;
        }
        if (!_story.IsPlaying || Mathf.Abs(_story.PlaybackTime - (float)position) > _maxDrift)
        {
            _story.PlayFrom((float)position);
        }
    }
}
