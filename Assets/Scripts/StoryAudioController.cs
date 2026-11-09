using UnityEngine;

/// <summary>
/// Plays and pauses the story narration. Driven by the control panel, and by
/// <see cref="NetworkedStory"/> to follow the host in multiplayer.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StoryAudioController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Narration clip.")]
    private AudioClip _storyClip;

    private AudioSource _audioSource;

    public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;

    /// <summary>Playback position in seconds.</summary>
    public float PlaybackTime => _audioSource != null ? _audioSource.time : 0f;

    /// <summary>Clip length in seconds (0 when no clip).</summary>
    public float Length => _audioSource != null && _audioSource.clip != null ? _audioSource.clip.length : 0f;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;
        _audioSource.clip = _storyClip;
        if (_storyClip == null)
        {
            Debug.LogWarning("StoryAudioController: no narration clip assigned.", this);
        }
    }

    /// <summary>Toggles between playing and paused, resuming from where it was paused.</summary>
    public void TogglePlayPause()
    {
        if (_audioSource.clip == null)
        {
            return;
        }
        if (_audioSource.isPlaying)
        {
            _audioSource.Pause();
            return;
        }
        _audioSource.UnPause();
        if (!_audioSource.isPlaying)
        {
            _audioSource.Play();
        }
    }

    public void PlayFrom(float time)
    {
        if (_audioSource.clip == null)
        {
            return;
        }
        _audioSource.time = Mathf.Clamp(time, 0f, Length - 0.01f);
        if (!_audioSource.isPlaying)
        {
            _audioSource.Play();
        }
    }

    public void PauseAt(float time)
    {
        if (_audioSource.clip == null)
        {
            return;
        }
        _audioSource.Pause();
        _audioSource.time = Mathf.Clamp(time, 0f, Length - 0.01f);
    }
}
