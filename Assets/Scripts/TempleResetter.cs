using System.Collections;
using UnityEngine;

/// <summary>
/// Remembers a "home" pose for the temple and restores it on demand.
/// On start the temple is placed within arm's reach in front of the player (optional), and that
/// becomes home. Home can be moved
/// to wherever the temple currently is with <see cref="SetHomeToCurrent"/>.
/// Attach to the Temple root (the object carrying the Grabbable / Rigidbody).
/// </summary>
public class TempleResetter : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Object to reset. Defaults to this GameObject.")]
    private Transform _target;

    [Header("Start placement")]
    [SerializeField]
    [Tooltip("On scene start, move the temple in front of wherever the player is standing and facing.")]
    private bool _placeInFrontOfPlayer = true;

    [SerializeField]
    [Tooltip("Gap in metres between the eyes and the near side of the temple. ~0.5 m is a comfortable arm's reach.")]
    private float _startGap = 0.5f;

    [SerializeField]
    [Tooltip("Head transform. Defaults to Camera.main (CenterEyeAnchor).")]
    private Transform _head;

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

    private IEnumerator Start()
    {
        if (!_placeInFrontOfPlayer)
        {
            yield break;
        }
        if (_head == null && Camera.main != null)
        {
            _head = Camera.main.transform;
        }
        if (_head == null)
        {
            yield break;
        }

        // The head sits at the exact origin until tracking starts; give it a moment to settle.
        float timeout = 3f;
        while (_head.localPosition == Vector3.zero && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        yield return new WaitForSeconds(0.2f);

        PlaceInFrontOfPlayer();
    }

    /// <summary>
    /// Moves the temple straight ahead of the player, near side <see cref="_startGap"/> from the eyes,
    /// turned by the player's facing so the same side faces them as in the scene. Height is kept.
    /// </summary>
    private void PlaceInFrontOfPlayer()
    {
        Vector3 forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(_head.up, Vector3.up);
        }
        forward.Normalize();
        Quaternion turn = Quaternion.FromToRotation(Vector3.forward, forward);

        Quaternion rotation = turn * _target.rotation;
        _target.rotation = rotation;

        // Half-depth of the temple along the view direction, so the gap is measured to its near side.
        Bounds bounds = WorldBounds();
        Vector3 offset = bounds.center - _target.position;
        float halfDepth = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z;

        Vector3 center = _head.position + forward * (_startGap + halfDepth);
        Vector3 position = new Vector3(center.x - offset.x, _target.position.y, center.z - offset.z);

        if (_rigidbody != null)
        {
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
        }
        _target.SetPositionAndRotation(position, rotation);
        SetHomeToCurrent();
    }

    private Bounds WorldBounds()
    {
        // Visible renderers only: TempleDemolition keeps hidden fracture pieces under the temple.
        Bounds? bounds = null;
        foreach (var r in _target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled)
            {
                continue;
            }
            if (bounds.HasValue)
            {
                var b = bounds.Value;
                b.Encapsulate(r.bounds);
                bounds = b;
            }
            else
            {
                bounds = r.bounds;
            }
        }
        return bounds ?? new Bounds(_target.position, Vector3.zero);
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
