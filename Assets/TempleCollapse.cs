using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TempleCollapse : MonoBehaviour
{
    [SerializeField] private Transform temple;
    [SerializeField] private float expandFactor = 0.6f;

    private Toggle m_Toggle;
    private Transform[] m_Pieces;
    private Vector3[] m_AssembledLocalPositions;
    private Vector3[] m_ExplodeOffsets;

    void Start()
    {
        m_Toggle = GetComponent<Toggle>();
        m_Toggle.onValueChanged.AddListener(OnCollapseToggled);
        CachePieces();
    }

    void CachePieces()
    {
        if (temple == null)
        {
            m_Pieces = new Transform[0];
            return;
        }

        // The model bakes every piece's geometry at a shared origin, so the transforms
        // all sit on top of each other. Rendered bounds are what actually tell them apart.
        List<Renderer> renderers = new List<Renderer>();
        foreach (Transform child in temple)
        {
            if (!child.gameObject.activeSelf) continue;

            Renderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled)
            {
                renderers.Add(renderer);
            }
        }

        m_Pieces = new Transform[renderers.Count];
        m_AssembledLocalPositions = new Vector3[renderers.Count];
        m_ExplodeOffsets = new Vector3[renderers.Count];
        if (renderers.Count == 0) return;

        Bounds whole = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++)
        {
            whole.Encapsulate(renderers[i].bounds);
        }
        Vector3 centre = temple.InverseTransformPoint(whole.center);

        for (int i = 0; i < renderers.Count; i++)
        {
            m_Pieces[i] = renderers[i].transform;
            m_AssembledLocalPositions[i] = m_Pieces[i].localPosition;
            m_ExplodeOffsets[i] = temple.InverseTransformPoint(renderers[i].bounds.center) - centre;
        }
    }

    void OnCollapseToggled(bool isCollapsed)
    {
        ApplyCollapse(isCollapsed);
    }

    // Called by ResetHandler so a reset also reassembles the temple.
    public void Restore()
    {
        if (m_Toggle != null)
        {
            m_Toggle.SetIsOnWithoutNotify(false);
        }

        ApplyCollapse(false);
    }

    void ApplyCollapse(bool isCollapsed)
    {
        if (m_Pieces == null) return;

        for (int i = 0; i < m_Pieces.Length; i++)
        {
            if (m_Pieces[i] == null) continue;

            // Push each piece straight out from the centre of the temple, by an amount
            // proportional to how far off-centre it already sits: a classic exploded view.
            m_Pieces[i].localPosition = isCollapsed
                ? m_AssembledLocalPositions[i] + m_ExplodeOffsets[i] * expandFactor
                : m_AssembledLocalPositions[i];
        }
    }
}
