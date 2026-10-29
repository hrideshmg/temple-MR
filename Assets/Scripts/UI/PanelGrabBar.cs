using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A pill-shaped "window bar" that lets the user pick up and move a world-space panel, like the
/// grab bar under a Quest system window.
///
/// It responds to every way of grabbing in the rig: near hand grab (pinch or palm), controller
/// grip, and ray select (hand pinch-ray or controller trigger). While held it reports
/// <see cref="TargetPosition"/>, where the panel root should be so it keeps the same offset from
/// the hand (or, for rays, from a point at a fixed distance along the ray). The owner decides
/// what to do with that; the bar itself never moves anything.
/// </summary>
public class PanelGrabBar : MonoBehaviour
{
    private const float AnimSharpness = 16f;

    private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.38f);
    private static readonly Color ActiveColor = new Color32(0xF2, 0xA3, 0x3A, 0xFF);

    private Transform _root;
    private RectTransform _pill;
    private Image _pillImage;
    private Vector2 _pillSize;
    private GameObject _interactionRoot;
    private HandGrabInteractable _handGrab;
    private GrabInteractable _grab;
    private RayInteractable _ray;

    // Current grabber (only one at a time; a second hand is ignored until the first lets go).
    private HandGrabInteractor _handGrabber;
    private GrabInteractor _controllerGrabber;
    private RayInteractor _rayGrabber;
    private float _rayDistance;
    private Vector3 _grabOffset;

    private float _hover;
    private float _held;

    public RectTransform Rect => (RectTransform)transform;

    public bool IsGrabbed => _handGrabber != null || _controllerGrabber != null || _rayGrabber != null;

    /// <summary>Where the root should be this frame. Only meaningful while <see cref="IsGrabbed"/>.</summary>
    public Vector3 TargetPosition { get; private set; }

    /// <summary>
    /// Builds the bar under <paramref name="parent"/>. <paramref name="hitSize"/> is the grabbable
    /// area and <paramref name="pillSize"/> the visible pill, both in canvas units. Call while the
    /// canvas is still inactive (see <see cref="PokeButton.Create"/>).
    /// </summary>
    public static PanelGrabBar Create(RectTransform parent, Transform root, Vector2 hitSize, Vector2 pillSize)
    {
        var rt = PokeButton.NewRect("GrabBar", parent);
        rt.sizeDelta = hitSize;
        var bar = rt.gameObject.AddComponent<PanelGrabBar>();
        bar._root = root;
        bar._pillSize = pillSize;

        bar._pillImage = PokeButton.NewImage("Pill", rt, UISpriteFactory.Rounded, IdleColor);
        bar._pill = bar._pillImage.rectTransform;
        bar._pill.sizeDelta = pillSize;
        bar._pillImage.pixelsPerUnitMultiplier = UISpriteFactory.RoundedCornerPixels / (pillSize.y * 0.5f);
        return bar;
    }

    /// <summary>Adds the grab / ray hit volume. Call after the bar has its final size and position.</summary>
    public void BuildInteraction()
    {
        Vector2 size = Rect.rect.size;

        _interactionRoot = new GameObject("Interaction");
        _interactionRoot.layer = gameObject.layer;
        _interactionRoot.SetActive(false);
        _interactionRoot.transform.SetParent(transform, false);

        // Ray: same clipped-plane setup as PokeButton.
        var plane = _interactionRoot.AddComponent<PlaneSurface>();
        plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
        var clipper = _interactionRoot.AddComponent<BoundsClipper>();
        clipper.Position = Vector3.zero;
        clipper.Size = new Vector3(size.x, size.y, 50f);
        var clipped = _interactionRoot.AddComponent<ClippedPlaneSurface>();
        clipped.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { clipper });
        _ray = _interactionRoot.AddComponent<RayInteractable>();
        _ray.InjectAllRayInteractable(clipped);

        // Near grab needs a rigidbody + collider for scoring. Kinematic trigger, so it never pushes anything.
        var rb = _interactionRoot.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var box = _interactionRoot.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(size.x, size.y, 60f);

        _handGrab = _interactionRoot.AddComponent<HandGrabInteractable>();
        _handGrab.InjectAllHandGrabInteractable(GrabTypeFlags.Pinch | GrabTypeFlags.Palm, rb,
            GrabbingRule.DefaultPinchRule, GrabbingRule.DefaultPalmRule);

        _grab = _interactionRoot.AddComponent<GrabInteractable>();
        _grab.InjectAllGrabInteractable(rb);

        _interactionRoot.SetActive(true);
    }

    /// <summary>Enables or disables input (e.g. when the panel collapses). Drops any current grab.</summary>
    public void SetInteractable(bool interactable)
    {
        if (!interactable)
        {
            Release();
        }
        if (_interactionRoot != null && _interactionRoot.activeSelf != interactable)
        {
            _interactionRoot.SetActive(interactable);
        }
    }

    private void Update()
    {
        UpdateGrab();

        bool hovered = _handGrab != null && (_handGrab.State != InteractableState.Normal ||
                                             _grab.State != InteractableState.Normal ||
                                             _ray.State != InteractableState.Normal);
        float t = 1f - Mathf.Exp(-AnimSharpness * Time.unscaledDeltaTime);
        _hover = Mathf.Lerp(_hover, hovered ? 1f : 0f, t);
        _held = Mathf.Lerp(_held, IsGrabbed ? 1f : 0f, t);

        _pill.sizeDelta = new Vector2(_pillSize.x * (1f + _hover * 0.25f), _pillSize.y * (1f + _hover * 0.35f));
        Color c = Color.Lerp(IdleColor, new Color(1f, 1f, 1f, 0.85f), _hover);
        _pillImage.color = Color.Lerp(c, ActiveColor, _held);
    }

    private void UpdateGrab()
    {
        // Let go once the current grabber stops selecting.
        if ((_handGrabber != null && !Contains(_handGrab, _handGrabber)) ||
            (_controllerGrabber != null && !Contains(_grab, _controllerGrabber)) ||
            (_rayGrabber != null && !Contains(_ray, _rayGrabber)))
        {
            Release();
        }

        if (!IsGrabbed && _interactionRoot != null && _interactionRoot.activeInHierarchy)
        {
            foreach (var i in _handGrab.SelectingInteractors)
            {
                _handGrabber = i;
                break;
            }
            if (_handGrabber == null)
            {
                foreach (var i in _grab.SelectingInteractors)
                {
                    _controllerGrabber = i;
                    break;
                }
            }
            if (_handGrabber == null && _controllerGrabber == null)
            {
                foreach (var i in _ray.SelectingInteractors)
                {
                    _rayGrabber = i;
                    _rayDistance = Mathf.Max(0.05f, Vector3.Dot(transform.position - i.Origin, i.Forward));
                    break;
                }
            }
            if (IsGrabbed)
            {
                _grabOffset = _root.position - GrabPoint();
            }
        }

        if (IsGrabbed)
        {
            TargetPosition = GrabPoint() + _grabOffset;
        }
    }

    private Vector3 GrabPoint()
    {
        if (_handGrabber != null)
        {
            return _handGrabber.WristPoint.position;
        }
        if (_controllerGrabber != null)
        {
            return _controllerGrabber.Rigidbody.transform.position;
        }
        return _rayGrabber.Origin + _rayGrabber.Forward * _rayDistance;
    }

    private void Release()
    {
        _handGrabber = null;
        _controllerGrabber = null;
        _rayGrabber = null;
    }

    private static bool Contains<TInteractor, TInteractable>(Interactable<TInteractor, TInteractable> interactable,
        TInteractor interactor)
        where TInteractor : Interactor<TInteractor, TInteractable>
        where TInteractable : Interactable<TInteractor, TInteractable>
    {
        foreach (var i in interactable.SelectingInteractors)
        {
            if (i == interactor)
            {
                return true;
            }
        }
        return false;
    }
}
