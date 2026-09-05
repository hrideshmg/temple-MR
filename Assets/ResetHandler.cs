using UnityEngine;
using UnityEngine.UI;

public class ResetHandler : MonoBehaviour
{
    [SerializeField] private GameObject targetObject;
    [SerializeField] private TempleCollapse templeCollapse;

    private Toggle m_Toggle;
    private Rigidbody m_Rigidbody;
    private Vector3 m_AnchoredPosition;
    private Quaternion m_AnchoredRotation;
    private Vector3 m_AnchoredScale;

    void Start()
    {
        m_Toggle = GetComponent<Toggle>();
        m_Toggle.onValueChanged.AddListener(OnResetPressed);

        if (targetObject == null) return;

        // Remember the pose the temple was anchored at before anyone grabs it.
        Transform target = targetObject.transform;
        m_AnchoredPosition = target.position;
        m_AnchoredRotation = target.rotation;
        m_AnchoredScale = target.localScale;
        m_Rigidbody = targetObject.GetComponent<Rigidbody>();
    }

    void OnResetPressed(bool isOn)
    {
        // Reset is a momentary action driven by a toggle, so swallow the new state.
        m_Toggle.SetIsOnWithoutNotify(false);

        if (targetObject == null) return;

        if (m_Rigidbody != null && !m_Rigidbody.isKinematic)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }

        Transform target = targetObject.transform;
        target.SetPositionAndRotation(m_AnchoredPosition, m_AnchoredRotation);
        target.localScale = m_AnchoredScale;

        if (templeCollapse != null)
        {
            templeCollapse.Restore();
        }
    }
}
