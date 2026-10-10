using UnityEngine;

/// <summary>
/// Remembers a "home" pose for the temple and restores it on demand.
/// Home starts as the pose the temple has when the scene loads, and can be moved
/// to wherever the temple currently is with <see cref="SetHomeToCurrent"/>.
/// Attach to the Temple root (the object carrying the Grabbable / Rigidbody).
/// </summary>
public class TempleResetter : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Object to reset. Defaults to this GameObject.")]
    private Transform _target;

    private Vector3 _homePosition;
    private Quaternion _homeRotation;
    private Vector3 _homeScale;
    private Rigidbody _rigidbody;

    private void Awake()
    {
        if (_target == null)
        {
            _target = transform;
        }

        _rigidbody = _target.GetComponent<Rigidbody>();
        SetHomeToCurrent();
    }

    /// <summary>
    /// Makes the temple's current position, rotation and scale the pose that
    /// <see cref="ResetTemple"/> returns to. Wired to the "Set temple position" button.
    /// </summary>
    public void SetHomeToCurrent()
    {
        _homePosition = _target.position;
        _homeRotation = _target.rotation;
        _homeScale = _target.localScale;
    }

    /// <summary>
    /// Restores the temple to its home pose, rebuilding it first if it was demolished.
    /// Wired to the "Reset temple" button.
    /// </summary>
    public void ResetTemple()
    {
        if (TryGetComponent(out TempleDemolition demolition))
        {
            demolition.Rebuild();
        }

        if (_rigidbody != null)
        {
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            _rigidbody.position = _homePosition;
            _rigidbody.rotation = _homeRotation;
        }

        _target.SetPositionAndRotation(_homePosition, _homeRotation);
        _target.localScale = _homeScale;
    }
}
