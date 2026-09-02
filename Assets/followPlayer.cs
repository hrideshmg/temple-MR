using UnityEngine;

public class CanvasFollowPlayer : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform cameraTransform; // assign CenterEyeAnchor

    [Header("Positioning")]
    [SerializeField] private float distance = 1.5f;
    [SerializeField] private float heightOffset = 0f;

    [Header("Follow Behavior")]
    [SerializeField] private float positionSmoothTime = 0.15f;
    [SerializeField] private float rotationSmoothSpeed = 5f;
    [SerializeField] private bool lockYRotation = true; // keep canvas upright, don't tilt

    private Vector3 velocity;

    void Start()
    {
        if (cameraTransform == null)
        {
            // OVRCameraRig's CenterEyeAnchor, or Camera.main as fallback
            var cameraRig = FindObjectOfType<OVRCameraRig>();
            cameraTransform = cameraRig != null ? cameraRig.centerEyeAnchor : Camera.main.transform;
        }
    }

    void LateUpdate()
    {
        if (cameraTransform == null) return;

        // Target position: in front of the camera at fixed distance
        Vector3 flatForward = cameraTransform.forward;
        if (lockYRotation) flatForward.y = 0;
        flatForward.Normalize();

        Vector3 targetPos = cameraTransform.position + flatForward * distance;
        targetPos.y = cameraTransform.position.y + heightOffset;

        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, positionSmoothTime);

        // Face the player
        Vector3 lookDir = transform.position - cameraTransform.position;
        if (lockYRotation) lookDir.y = 0;
        Quaternion targetRot = Quaternion.LookRotation(lookDir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSmoothSpeed);
    }
}