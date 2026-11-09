using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A set of polylines, each drawn as a thin bright core inside a wide, faint glow. Points are kept
/// in whatever space the caller likes and mapped to world space on every <see cref="Draw"/>, so line
/// widths stay in metres whatever the scale of the thing being outlined. Used by the story overlay
/// and the rebuild puzzle.
/// </summary>
public class GlowOutline
{
    private readonly Transform _parent;
    private readonly Material _material;
    private readonly string _name;
    private readonly List<Vector3[]> _lines = new List<Vector3[]>();
    private readonly List<bool> _loops = new List<bool>();
    private readonly List<LineRenderer> _core = new List<LineRenderer>();
    private readonly List<LineRenderer> _glow = new List<LineRenderer>();
    private readonly List<Vector3[]> _buffers = new List<Vector3[]>();

    public GlowOutline(string name, Transform parent, Material material)
    {
        _name = name;
        _parent = parent;
        _material = material;
    }

    public void AddLine(Vector3[] points, bool loop)
    {
        _lines.Add(points);
        _loops.Add(loop);
        _buffers.Add(new Vector3[points.Length]);
        _glow.Add(NewRenderer(_name + "_Glow", 0));
        _core.Add(NewRenderer(_name, 1));
    }

    /// <summary>Replaces the outline with one closed loop (allocates the renderers once).</summary>
    public void SetSingleLoop(Vector3[] points)
    {
        SetSingle(points, true);
    }

    public void SetSingleLine(Vector3[] points)
    {
        SetSingle(points, false);
    }

    private void SetSingle(Vector3[] points, bool loop)
    {
        if (_lines.Count == 0)
        {
            AddLine(points, loop);
            return;
        }
        _lines[0] = points;
        _loops[0] = loop;
        if (_buffers[0].Length != points.Length)
        {
            _buffers[0] = new Vector3[points.Length];
        }
    }

    private LineRenderer NewRenderer(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = _material;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.alignment = LineAlignment.View;
        lr.sortingOrder = order;
        lr.enabled = false;
        return lr;
    }

    public void Draw(Matrix4x4 toWorld, Color color, float alpha, float width, float glowWidth)
    {
        bool visible = alpha > 0.002f;
        for (int i = 0; i < _lines.Count; i++)
        {
            var core = _core[i];
            var glow = _glow[i];
            if (!visible)
            {
                core.enabled = false;
                glow.enabled = false;
                continue;
            }
            Vector3[] pts = _lines[i];
            Vector3[] buffer = _buffers[i];
            for (int k = 0; k < pts.Length; k++)
            {
                buffer[k] = toWorld.MultiplyPoint3x4(pts[k]);
            }
            Setup(core, buffer, _loops[i], width, new Color(color.r, color.g, color.b, alpha));
            Setup(glow, buffer, _loops[i], glowWidth, new Color(color.r, color.g, color.b, alpha * 0.18f));
        }
    }

    private static void Setup(LineRenderer lr, Vector3[] positions, bool loop, float width, Color color)
    {
        lr.enabled = true;
        lr.positionCount = positions.Length;
        lr.SetPositions(positions);
        lr.loop = loop;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
    }

    /// <summary>
    /// The shared transparent line material (Sprites/Default, always included in builds), falling
    /// back to the UI material.
    /// </summary>
    public static Material CreateMaterial()
    {
        var shader = Shader.Find("Sprites/Default");
        var material = shader != null ? new Material(shader) : new Material(UnityEngine.UI.Graphic.defaultGraphicMaterial);
        material.name = "GlowLines";
        return material;
    }
}
