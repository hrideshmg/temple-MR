using UnityEngine;

/// <summary>
/// Remembers the temple's starting pose and restores it on demand.
/// Attach to the Temple root (the object carrying the Grabbable / Rigidbody).
/// </summary>
public class TempleResetter : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Object to reset. Defaults to this GameObject.")]
    private Transform _target;

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private Vector3 _initialScale;
    private Rigidbody _rigidbody;

    private void Awake()
    {
        if (_target == null)
        {
            _target = transform;
        }

        _initialPosition = _target.position;
        _initialRotation = _target.rotation;
        _initialScale = _target.localScale;
        _rigidbody = _target.GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Restores the temple to the position, rotation and scale it had when the scene started.
    /// Wired to the "Reset Temple" UI button.
    /// </summary>
    public void ResetTemple()
    {
        if (_rigidbody != null)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.position = _initialPosition;
            _rigidbody.rotation = _initialRotation;
        }

        _target.SetPositionAndRotation(_initialPosition, _initialRotation);
        _target.localScale = _initialScale;
    }
}
