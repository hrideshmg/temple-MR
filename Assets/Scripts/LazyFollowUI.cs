using UnityEngine;

/// <summary>
/// Keeps a world-space UI panel floating in front of the user's head.
/// The panel stays put while the user looks around a little, and glides back
/// into view once their gaze drifts past the angle / distance thresholds, so it is
/// always reachable without being glued to the eyes.
/// </summary>
public class LazyFollowUI : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Head transform to follow. Defaults to Camera.main (CenterEyeAnchor on OVRCameraRig).")]
    private Transform _head;

    [SerializeField]
    [Tooltip("How far in front of the head the panel rests, in metres.")]
    private float _distance = 0.6f;

    [SerializeField]
    [Tooltip("Vertical offset from eye height, in metres (negative = below eye line).")]
    private float _heightOffset = -0.15f;

    [SerializeField]
    [Tooltip("Horizontal offset, in metres (positive = to the user's right).")]
    private float _sideOffset = 0f;

    [SerializeField]
    [Tooltip("Start re-centering once the panel is more than this many degrees off the view direction.")]
    private float _angleThreshold = 25f;

    [SerializeField]
    [Tooltip("Start re-centering once the panel is more than this far from its ideal spot, in metres.")]
    private float _distanceThreshold = 0.35f;

    [SerializeField]
    [Tooltip("Higher = snappier follow.")]
    private float _followSpeed = 4f;

    [SerializeField]
    [Tooltip("Keep the panel upright instead of tilting with head pitch.")]
    private bool _lockPitch = true;

    private bool _isMoving;

    private void Start()
    {
        if (_head == null && Camera.main != null)
        {
            _head = Camera.main.transform;
        }

        if (_head != null)
        {
            // Snap to the ideal pose on the first frame so the panel is visible immediately.
            transform.SetPositionAndRotation(TargetPosition(), TargetRotation());
        }
    }

    private void LateUpdate()
    {
        if (_head == null)
        {
            return;
        }

        Vector3 targetPos = TargetPosition();
        Quaternion targetRot = TargetRotation();

        Vector3 toPanel = transform.position - _head.position;
        float angle = Vector3.Angle(FlattenIfLocked(_head.forward), FlattenIfLocked(toPanel));
        float drift = Vector3.Distance(transform.position, targetPos);

        if (!_isMoving && (angle > _angleThreshold || drift > _distanceThreshold))
        {
            _isMoving = true;
        }

        if (_isMoving)
        {
            float t = 1f - Mathf.Exp(-_followSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);

            if (drift < 0.01f)
            {
                _isMoving = false;
            }
        }
    }

    private Vector3 TargetPosition()
    {
        Vector3 forward = FlattenIfLocked(_head.forward);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return _head.position + forward * _distance + right * _sideOffset + Vector3.up * _heightOffset;
    }

    private Quaternion TargetRotation()
    {
        // Face the user: panel's +Z points away from the head.
        Vector3 look = FlattenIfLocked(transform.position - _head.position);
        if (look.sqrMagnitude < 0.0001f)
        {
            look = FlattenIfLocked(_head.forward);
        }
        return Quaternion.LookRotation(look, Vector3.up);
    }

    private Vector3 FlattenIfLocked(Vector3 v)
    {
        if (_lockPitch)
        {
            v.y = 0f;
        }
        return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
    }
}
