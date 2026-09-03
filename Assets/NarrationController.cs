using UnityEngine;
using UnityEngine.UI;

public class NarrationController : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip narrationClip;

    private Toggle m_Toggle;

    void Start()
    {
        m_Toggle = GetComponent<Toggle>();
        m_Toggle.onValueChanged.AddListener(OnNarrationToggled);

        if (audioSource != null)
        {
            audioSource.clip = narrationClip;
            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }
    }

    void OnNarrationToggled(bool isOn)
    {
        if (audioSource == null) return;

        if (isOn)
        {
            audioSource.Play();
        }
        else
        {
            audioSource.Pause();
        }
    }
}
