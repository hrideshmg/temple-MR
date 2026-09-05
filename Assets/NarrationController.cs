using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NarrationController : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip narrationClip;

    [Header("Playback Controls")]
    [SerializeField] private Toggle pauseToggle;
    [SerializeField] private Toggle stopToggle;
    [SerializeField] private TMP_Text pauseLabel;
    [SerializeField] private string pauseText = "Pause";
    [SerializeField] private string resumeText = "Resume";

    private Toggle m_Toggle;
    private bool m_IsNarrating;

    void Start()
    {
        m_Toggle = GetComponent<Toggle>();
        m_Toggle.onValueChanged.AddListener(OnNarrationToggled);

        if (pauseToggle != null)
        {
            pauseToggle.onValueChanged.AddListener(OnPauseToggled);
        }

        if (stopToggle != null)
        {
            stopToggle.onValueChanged.AddListener(OnStopPressed);
        }

        if (audioSource != null)
        {
            audioSource.clip = narrationClip;
            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }

        SetControlsVisible(false);
    }

    void Update()
    {
        // The clip also ends on its own, so fold the controls away when it does.
        if (m_IsNarrating && audioSource != null && !audioSource.isPlaying && !IsPaused)
        {
            m_Toggle.SetIsOnWithoutNotify(false);
            EndNarration();
        }
    }

    private bool IsPaused => pauseToggle != null && pauseToggle.isOn;

    void OnNarrationToggled(bool isOn)
    {
        if (audioSource == null) return;

        if (isOn)
        {
            StartNarration();
        }
        else
        {
            EndNarration();
        }
    }

    void OnPauseToggled(bool isPaused)
    {
        if (audioSource == null || !m_IsNarrating) return;

        if (isPaused)
        {
            audioSource.Pause();
        }
        else
        {
            audioSource.UnPause();
        }

        if (pauseLabel != null)
        {
            pauseLabel.text = isPaused ? resumeText : pauseText;
        }
    }

    void OnStopPressed(bool isOn)
    {
        // Stop is a momentary action driven by a toggle, so swallow the new state.
        if (stopToggle != null)
        {
            stopToggle.SetIsOnWithoutNotify(false);
        }

        if (!m_IsNarrating) return;

        m_Toggle.SetIsOnWithoutNotify(false);
        EndNarration();
    }

    void StartNarration()
    {
        audioSource.Stop();
        ResetPauseState();
        audioSource.Play();

        m_IsNarrating = true;
        SetControlsVisible(true);
    }

    void EndNarration()
    {
        audioSource.Stop();
        ResetPauseState();

        m_IsNarrating = false;
        SetControlsVisible(false);
    }

    void ResetPauseState()
    {
        if (pauseToggle != null)
        {
            pauseToggle.SetIsOnWithoutNotify(false);
        }

        if (pauseLabel != null)
        {
            pauseLabel.text = pauseText;
        }
    }

    void SetControlsVisible(bool visible)
    {
        if (pauseToggle != null)
        {
            pauseToggle.gameObject.SetActive(visible);
        }

        if (stopToggle != null)
        {
            stopToggle.gameObject.SetActive(visible);
        }
    }
}
