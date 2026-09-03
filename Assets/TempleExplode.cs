using UnityEngine;
using UnityEngine.UI;

public class TempleExplode : MonoBehaviour
{
    [SerializeField] private Transform[] pieces;
    [SerializeField] private Vector3[] explodeDirections;
    [SerializeField] private float explodeDistance = 0.3f;

    private Toggle m_Toggle;
    private Vector3[] m_OriginalLocalPositions;

    void Start()
    {
        m_Toggle = GetComponent<Toggle>();
        m_Toggle.onValueChanged.AddListener(OnExplodeToggled);

        m_OriginalLocalPositions = new Vector3[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i] != null)
            {
                m_OriginalLocalPositions[i] = pieces[i].localPosition;
            }
        }
    }

    void OnExplodeToggled(bool isOn)
    {
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i] == null) continue;

            if (isOn)
            {
                Vector3 direction = (explodeDirections != null && i < explodeDirections.Length)
                    ? explodeDirections[i]
                    : Vector3.up;
                pieces[i].localPosition = m_OriginalLocalPositions[i] + direction.normalized * explodeDistance;
            }
            else
            {
                pieces[i].localPosition = m_OriginalLocalPositions[i];
            }
        }
    }
}
