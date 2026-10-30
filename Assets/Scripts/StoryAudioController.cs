using UnityEngine;
using TMPro;

/// <summary>
/// Plays / pauses a narration clip and keeps the button label in sync.
/// Wired to the "Play Story" UI button.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StoryAudioController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Audio clip to narrate. If left empty, Resources/<Resource Clip Name> is loaded at startup.")]
    private AudioClip _storyClip;

    [SerializeField]
    [Tooltip("Fallback: name of a clip inside any Resources folder (e.g. Assets/Audio/Resources/MuvarStory.mp3).")]
    private string _resourceClipName = "MuvarStory";

    [SerializeField]
    [Tooltip("Optional label that shows Play / Pause state.")]
    private TMP_Text _buttonLabel;

    [SerializeField] private string _playText = "Play Story";
    [SerializeField] private string _pauseText = "Pause Story";

    private AudioSource _audioSource;
    private bool _wasPlaying;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;

        if (_storyClip == null && !string.IsNullOrEmpty(_resourceClipName))
        {
            _storyClip = Resources.Load<AudioClip>(_resourceClipName);
        }

        if (_storyClip != null)
        {
            _audioSource.clip = _storyClip;
        }
        else
        {
            Debug.LogWarning($"StoryAudioController: no clip assigned and Resources/{_resourceClipName} not found.", this);
        }
    }

    private void Start()
    {
        RefreshLabel();
    }

    private void Update()
    {
        // When the clip finishes naturally, flip the label back to "Play".
        if (_wasPlaying && !_audioSource.isPlaying)
        {
            _wasPlaying = false;
            RefreshLabel();
        }
    }

    /// <summary>
    /// Toggles between playing and paused. Resumes from where it was paused.
    /// </summary>
    public void TogglePlayPause()
    {
        if (_audioSource.clip == null)
        {
            Debug.LogWarning("StoryAudioController: no AudioClip assigned.", this);
            return;
        }

        if (_audioSource.isPlaying)
        {
            _audioSource.Pause();
            _wasPlaying = false;
        }
        else
        {
            _audioSource.UnPause();
            if (!_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
            _wasPlaying = true;
        }

        RefreshLabel();
    }

    /// <summary>Plays from the given position in seconds (used to follow the host in multiplayer).</summary>
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
        _wasPlaying = true;
        RefreshLabel();
    }

    /// <summary>Pauses at the given position in seconds (used to follow the host in multiplayer).</summary>
    public void PauseAt(float time)
    {
        if (_audioSource.clip == null)
        {
            return;
        }
        _audioSource.Pause();
        _audioSource.time = Mathf.Clamp(time, 0f, Length - 0.01f);
        _wasPlaying = false;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (_buttonLabel != null)
        {
            _buttonLabel.text = _audioSource.isPlaying ? _pauseText : _playText;
        }
    }

    public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;

    /// <summary>Playback position in seconds.</summary>
    public float PlaybackTime => _audioSource != null ? _audioSource.time : 0f;

    /// <summary>Clip length in seconds (0 when no clip).</summary>
    public float Length => _audioSource != null && _audioSource.clip != null ? _audioSource.clip.length : 0f;
}
