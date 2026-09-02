using UnityEngine;
using UnityEngine.UI;

public class Example : MonoBehaviour
{
    Toggle m_Toggle;
    public GameObject targetObject; 

    void Start()
    {
        //Fetch the Toggle GameObject
        m_Toggle = GetComponent<Toggle>();
        //Add listener for when the state of the Toggle changes, to take action
        m_Toggle.onValueChanged.AddListener(delegate {
                ToggleValueChanged(m_Toggle);
            });

    }

    //Output the new state of the Toggle into Text
    void ToggleValueChanged(Toggle change)
    {
       Debug.Log("Toggle Changed! " + m_Toggle.isOn);
       targetObject.transform.position = new Vector3(0f, 1.25f, 0.4f);
       targetObject.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
    }
}