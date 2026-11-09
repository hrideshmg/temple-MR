using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// Points out the parts of the temple during the story: a glowing outline that hugs each part, a
/// scan line that sweeps up through it as it is named, and a label card with a leader line.
///
/// The model's meshes carry no part names, so the parts are found by height. At startup the
/// temple's surface is sampled into a stack of top-down occupancy grids, one per height slice, and
/// each part is a height range of that stack: the adhisthana (base), the bhitti (wall), the vimana
/// (tower) and the devakoshthas (wall niches). A part's outline is the traced edge of its slices
/// seen from above, so it follows the stepped wall projections and the front platform instead of a
/// box around them; small detached pieces (Nandi, statues out front) are left out. The niches sit at
/// the middle of each wall except the entrance side, which is found as the side where the base runs
/// furthest out past the wall (the front platform).
///
/// Everything is worked out in the temple's own space and drawn through its transform every frame,
/// so the outline stays on the model wherever the temple is moved or turned, on every headset.
///
/// Everything here is driven by <see cref="StorySequence"/>, which sets how visible each part is
/// from the narration's playback time. Created by the sequence at runtime; not placed in the scene.
/// </summary>
public class TempleAnatomy : MonoBehaviour
{
    public enum PartKind
    {
        /// <summary>A horizontal slice of the whole temple.</summary>
        Band,
        /// <summary>Niches on the walls (every side but the entrance) within the height range.</summary>
        Niches,
    }

    /// <summary>One labelled part of the temple.</summary>
    [Serializable]
    public class Part
    {
        public string Title = "Part";
        public string Subtitle = "";
        [TextArea(2, 4)] public string Body = "";
        public PartKind Kind = PartKind.Band;

        [Tooltip("Bottom of the part as a fraction of the temple's height (0 = ground, 1 = top).")]
        [Range(0f, 1f)] public float From;

        [Tooltip("Top of the part as a fraction of the temple's height.")]
        [Range(0f, 1f)] public float To = 1f;

        [Tooltip("Which side of the temple the label sits on, as seen by the viewer: -1 left, 1 right.")]
        public int Side = 1;
    }

    private const int Bins = 60;                  // height slices
    private const int GridCells = 96;             // cells along the temple's longer side
    private const int GridBorder = 3;             // empty cells around the grid, room for the margin
    private const float LineWidth = 0.0022f;      // metres
    private const float GlowWidth = 0.009f;
    private const float LabelScale = 0.0009f;     // metres per canvas unit at arm's length
    private const float LabelGap = 0.05f;         // metres between the temple and a label

    public bool IsReady { get; private set; }

    private Transform _temple;
    private Part[] _parts;
    private Material _lineMaterial;

    // Temple-space plan: per height slice, which grid cells (x by z) hold some of the temple's surface.
    private Bounds _bounds;
    private Vector2 _gridOrigin;                  // temple x, z of the grid's corner
    private float _cell;                          // cell size in temple units
    private int _nx, _nz;
    private bool[][] _occupancy;                  // [bin][x * _nz + z]
    private Vector2[][] _binOutline;              // per slice: the plan outline (temple x, z), for scan lines
    private Rect _wholeFootprint;
    private int _entranceSide; // 0 +x, 1 -x, 2 +z, 3 -z

    private class PartVisual
    {
        public Part Part;
        public GlowOutline Frame;
        public GlowOutline Scan;
        public GlowOutline Pin;
        public GlowOutline Leader;
        public StoryCard Label;
        public Vector2[] Middle;             // plan outline half way up the part (temple x, z)
        public List<Vector3> NicheCentres = new List<Vector3>(); // temple space
        public List<Vector3> NicheNormals = new List<Vector3>();
        public float FrameAlpha;
        public float LabelAlpha;
        public float ScanU = -1f;

        // Label layout for this frame (world space).
        public bool Shown;
        public Vector3 Anchor;
        public Vector3 CardInner;
        public Vector3 Right;
        public int SideSign;
        public float Size;
    }

    private readonly List<PartVisual> _visuals = new List<PartVisual>();
    private GlowOutline _sweep;
    private GlowOutline _sweepTrail;
    private float _sweepU = -1f;
    private float _sweepAlpha;
    private float _masterAlpha = 1f;

    public void Init(Transform temple, Part[] parts)
    {
        _temple = temple;
        _parts = parts;
        _lineMaterial = GlowOutline.CreateMaterial();
        StartCoroutine(AnalyseRoutine());
    }

    private IEnumerator AnalyseRoutine()
    {
        // Gather the visible temple meshes in temple space (main thread: Mesh API). The copies are
        // spread over a few frames, and the temple may be moved meanwhile (placed in front of the
        // player, aligned to the host's room), so each mesh's transform into temple space is taken
        // from the hierarchy below the temple, never from a world pose captured on another frame.
        var meshes = new List<(Vector3[] vertices, int[] triangles, Matrix4x4 toTemple)>();
        foreach (var mf in _temple.GetComponentsInChildren<MeshFilter>(false))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null || !mf.sharedMesh.isReadable)
            {
                continue;
            }
            meshes.Add((mf.sharedMesh.vertices, mf.sharedMesh.triangles, ToTemple(mf.transform)));
            yield return null;
        }
        if (meshes.Count == 0)
        {
            Debug.LogWarning("TempleAnatomy: no readable temple meshes; the story will run without part highlights.", this);
            yield break;
        }

        Task task = Task.Run(() => BuildPlan(meshes));
        while (!task.IsCompleted)
        {
            yield return null;
        }
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception, this);
            yield break;
        }
        _entranceSide = FindEntranceSide();
        BuildVisuals();
        IsReady = true;
    }

    /// <summary>A transform below the temple, into temple space, from the local poses in between.</summary>
    private Matrix4x4 ToTemple(Transform t)
    {
        Matrix4x4 m = Matrix4x4.identity;
        for (Transform p = t; p != null && p != _temple; p = p.parent)
        {
            m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
        }
        return m;
    }

    /// <summary>
    /// Worker thread: the temple's bounds, the per-slice occupancy grids (from points sampled across
    /// every triangle, so big flat walls count as much as carved detail) and each slice's outline.
    /// </summary>
    private void BuildPlan(List<(Vector3[] vertices, int[] triangles, Matrix4x4 toTemple)> meshes)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = -min;
        var local = new List<Vector3[]>(meshes.Count);
        foreach (var (vertices, _, m) in meshes)
        {
            var v = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                v[i] = m.MultiplyPoint3x4(vertices[i]);
                min = Vector3.Min(min, v[i]);
                max = Vector3.Max(max, v[i]);
            }
            local.Add(v);
        }
        _bounds.SetMinMax(min, max);

        _cell = Mathf.Max(max.x - min.x, max.z - min.z, 1e-5f) / GridCells;
        _gridOrigin = new Vector2(min.x - GridBorder * _cell, min.z - GridBorder * _cell);
        _nx = Mathf.CeilToInt((max.x - min.x) / _cell) + GridBorder * 2 + 1;
        _nz = Mathf.CeilToInt((max.z - min.z) / _cell) + GridBorder * 2 + 1;
        _occupancy = new bool[Bins][];
        for (int b = 0; b < Bins; b++)
        {
            _occupancy[b] = new bool[_nx * _nz];
        }

        float height = Mathf.Max(max.y - min.y, 1e-5f);
        float step = _cell * 0.5f;
        for (int k = 0; k < meshes.Count; k++)
        {
            Vector3[] v = local[k];
            int[] tris = meshes[k].triangles;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = v[tris[t]], ab = v[tris[t + 1]] - a, ac = v[tris[t + 2]] - a;
                float edge = Mathf.Max(ab.magnitude, ac.magnitude, (ac - ab).magnitude);
                int n = Mathf.Clamp(Mathf.CeilToInt(edge / step), 1, 256);
                for (int i = 0; i <= n; i++)
                {
                    for (int j = 0; j <= n - i; j++)
                    {
                        Vector3 p = a + ab * (i / (float)n) + ac * (j / (float)n);
                        int b = Mathf.Clamp((int)((p.y - min.y) / height * Bins), 0, Bins - 1);
                        int x = (int)((p.x - _gridOrigin.x) / _cell);
                        int z = (int)((p.z - _gridOrigin.y) / _cell);
                        if (x >= 0 && x < _nx && z >= 0 && z < _nz)
                        {
                            _occupancy[b][x * _nz + z] = true;
                        }
                    }
                }
            }
        }

        _binOutline = new Vector2[Bins][];
        for (int b = 0; b < Bins; b++)
        {
            _binOutline[b] = Outline((b - 1f) / Bins, (b + 2f) / Bins);
        }
        _wholeFootprint = Footprint(0f, 1f);
    }

    // ---- Plan masks and outlines ----

    /// <summary>
    /// The temple's plan between two height fractions: the slices merged, small gaps closed, only the
    /// main body kept (not statues standing apart), holes filled, and grown by a cell so the outline
    /// sits just off the surface.
    /// </summary>
    private bool[] Mask(float from, float to)
    {
        int b0 = Mathf.Clamp(Mathf.FloorToInt(from * Bins), 0, Bins - 1);
        int b1 = Mathf.Clamp(Mathf.CeilToInt(to * Bins) - 1, b0, Bins - 1);
        var m = new bool[_nx * _nz];
        for (int b = b0; b <= b1; b++)
        {
            bool[] o = _occupancy[b];
            for (int i = 0; i < m.Length; i++)
            {
                m[i] |= o[i];
            }
        }
        m = Erode(Dilate(m));
        KeepLargest(m);
        FillHoles(m);
        return Dilate(m);
    }

    private bool[] Dilate(bool[] m)
    {
        var r = new bool[m.Length];
        for (int x = 0; x < _nx; x++)
        {
            for (int z = 0; z < _nz; z++)
            {
                if (!m[x * _nz + z])
                {
                    continue;
                }
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int px = x + dx, pz = z + dz;
                        if (px >= 0 && px < _nx && pz >= 0 && pz < _nz)
                        {
                            r[px * _nz + pz] = true;
                        }
                    }
                }
            }
        }
        return r;
    }

    private bool[] Erode(bool[] m)
    {
        var r = new bool[m.Length];
        for (int x = 1; x < _nx - 1; x++)
        {
            for (int z = 1; z < _nz - 1; z++)
            {
                bool all = true;
                for (int dx = -1; dx <= 1 && all; dx++)
                {
                    for (int dz = -1; dz <= 1 && all; dz++)
                    {
                        all = m[(x + dx) * _nz + z + dz];
                    }
                }
                r[x * _nz + z] = all;
            }
        }
        return r;
    }

    /// <summary>Clears every 4-connected group of cells but the biggest.</summary>
    private void KeepLargest(bool[] m)
    {
        var label = new int[m.Length];
        var queue = new Queue<int>();
        int bestLabel = 0, bestSize = 0, next = 0;
        for (int start = 0; start < m.Length; start++)
        {
            if (!m[start] || label[start] != 0)
            {
                continue;
            }
            next++;
            int size = 0;
            label[start] = next;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                size++;
                int x = c / _nz, z = c % _nz;
                if (x > 0) Visit(c - _nz);
                if (x < _nx - 1) Visit(c + _nz);
                if (z > 0) Visit(c - 1);
                if (z < _nz - 1) Visit(c + 1);
            }
            if (size > bestSize)
            {
                bestSize = size;
                bestLabel = next;
            }
        }
        for (int i = 0; i < m.Length; i++)
        {
            m[i] = m[i] && label[i] == bestLabel;
        }

        void Visit(int c)
        {
            if (m[c] && label[c] == 0)
            {
                label[c] = next;
                queue.Enqueue(c);
            }
        }
    }

    /// <summary>Fills every empty cell that can't be reached from the grid's edge.</summary>
    private void FillHoles(bool[] m)
    {
        var outside = new bool[m.Length];
        var queue = new Queue<int>();
        for (int x = 0; x < _nx; x++)
        {
            for (int z = 0; z < _nz; z++)
            {
                if ((x == 0 || z == 0 || x == _nx - 1 || z == _nz - 1) && !m[x * _nz + z])
                {
                    outside[x * _nz + z] = true;
                    queue.Enqueue(x * _nz + z);
                }
            }
        }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int x = c / _nz, z = c % _nz;
            if (x > 0) Visit(c - _nz);
            if (x < _nx - 1) Visit(c + _nz);
            if (z > 0) Visit(c - 1);
            if (z < _nz - 1) Visit(c + 1);
        }
        for (int i = 0; i < m.Length; i++)
        {
            m[i] = !outside[i];
        }

        void Visit(int c)
        {
            if (!m[c] && !outside[c])
            {
                outside[c] = true;
                queue.Enqueue(c);
            }
        }
    }

    /// <summary>The outline of the plan between two height fractions, counter-clockwise in temple x, z.</summary>
    private Vector2[] Outline(float from, float to)
    {
        return Trace(Mask(from, to));
    }

    /// <summary>
    /// Walks the cell edges around the filled cells, keeping the longest loop, with straight runs
    /// merged into single segments.
    /// </summary>
    private Vector2[] Trace(bool[] m)
    {
        int stride = _nz + 1;
        var edges = new Dictionary<int, List<int>>();
        void Edge(int x0, int z0, int x1, int z1)
        {
            int from = x0 * stride + z0;
            if (!edges.TryGetValue(from, out var list))
            {
                edges[from] = list = new List<int>(1);
            }
            list.Add(x1 * stride + z1);
        }
        bool Filled(int x, int z) => x >= 0 && x < _nx && z >= 0 && z < _nz && m[x * _nz + z];

        for (int x = 0; x < _nx; x++)
        {
            for (int z = 0; z < _nz; z++)
            {
                if (!m[x * _nz + z])
                {
                    continue;
                }
                // Interior on the left of each edge (x right, z up).
                if (!Filled(x, z - 1)) Edge(x, z, x + 1, z);
                if (!Filled(x + 1, z)) Edge(x + 1, z, x + 1, z + 1);
                if (!Filled(x, z + 1)) Edge(x + 1, z + 1, x, z + 1);
                if (!Filled(x - 1, z)) Edge(x, z + 1, x, z);
            }
        }

        List<int> best = null;
        while (edges.Count > 0)
        {
            var e = edges.GetEnumerator();
            e.MoveNext();
            int start = e.Current.Key;
            var loop = new List<int>();
            int at = start;
            while (edges.TryGetValue(at, out var outs))
            {
                loop.Add(at);
                int to = outs[outs.Count - 1];
                outs.RemoveAt(outs.Count - 1);
                if (outs.Count == 0)
                {
                    edges.Remove(at);
                }
                at = to;
                if (at == start)
                {
                    break;
                }
            }
            if (best == null || loop.Count > best.Count)
            {
                best = loop;
            }
        }
        if (best == null || best.Count < 3)
        {
            Rect r = Rect.MinMaxRect(_bounds.min.x, _bounds.min.z, _bounds.max.x, _bounds.max.z);
            return new[] { r.min, new Vector2(r.xMax, r.yMin), r.max, new Vector2(r.xMin, r.yMax) };
        }

        var points = new List<Vector2>(best.Count);
        for (int i = 0; i < best.Count; i++)
        {
            int prev = best[(i + best.Count - 1) % best.Count], cur = best[i], nxt = best[(i + 1) % best.Count];
            bool straight = (prev / stride == cur / stride && cur / stride == nxt / stride)
                            || (prev % stride == cur % stride && cur % stride == nxt % stride);
            if (!straight)
            {
                points.Add(_gridOrigin + new Vector2(cur / stride, cur % stride) * _cell);
            }
        }
        return points.ToArray();
    }

    /// <summary>The bounding rectangle of the plan between two height fractions (temple x, z).</summary>
    private Rect Footprint(float from, float to)
    {
        return Bounds2D(Outline(from, to));
    }

    private static Rect Bounds2D(Vector2[] points)
    {
        Vector2 min = points[0], max = points[0];
        foreach (var p in points)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private int FindEntranceSide()
    {
        Rect wall = Footprint(0.2f, 0.32f);
        Rect plinth = Footprint(0.02f, 0.12f);
        float[] reach =
        {
            plinth.xMax - wall.xMax, // +x
            wall.xMin - plinth.xMin, // -x
            plinth.yMax - wall.yMax, // +z
            wall.yMin - plinth.yMin, // -z
        };
        int best = 0;
        for (int i = 1; i < 4; i++)
        {
            if (reach[i] > reach[best])
            {
                best = i;
            }
        }
        return best;
    }

    private float Y(float fraction) => Mathf.Lerp(_bounds.min.y, _bounds.max.y, fraction);

    private Vector2[] BinOutline(float fraction) => _binOutline[Mathf.Clamp((int)(fraction * Bins), 0, Bins - 1)];

    private void BuildVisuals()
    {
        var root = transform;
        foreach (var part in _parts)
        {
            var v = new PartVisual { Part = part };
            float middle = part.To > 0.9f ? Mathf.Lerp(part.From, part.To, 0.3f) : (part.From + part.To) * 0.5f;
            v.Middle = Outline(middle - 1.5f / Bins, middle + 1.5f / Bins);
            v.Frame = new GlowOutline("Frame_" + part.Title, root, _lineMaterial);
            v.Scan = new GlowOutline("Scan_" + part.Title, root, _lineMaterial);
            v.Pin = new GlowOutline("Pin_" + part.Title, root, _lineMaterial);
            v.Leader = new GlowOutline("Leader_" + part.Title, root, _lineMaterial);

            if (part.Kind == PartKind.Band)
            {
                BuildBandFrame(v.Frame, part.From, part.To);
            }
            else
            {
                BuildNiches(v);
            }

            v.Label = new StoryCard("Label_" + part.Title, root, 230f, LabelScale, true, true);
            v.Label.AddText(part.Title, 21f, FontStyles.Bold, StoryCard.Gold);
            if (!string.IsNullOrEmpty(part.Subtitle))
            {
                v.Label.AddText(part.Subtitle.ToUpperInvariant(), 10.5f, FontStyles.Bold, StoryCard.TextSecondary,
                    TextAlignmentOptions.TopLeft, 8f);
            }
            if (!string.IsNullOrEmpty(part.Body))
            {
                v.Label.AddText(part.Body, 12f, FontStyles.Normal, StoryCard.TextPrimary);
            }
            _visuals.Add(v);
        }

        _sweep = new GlowOutline("RestoreSweep", root, _lineMaterial);
        _sweepTrail = new GlowOutline("RestoreSweepTrail", root, _lineMaterial);
    }

    /// <summary>
    /// The band's plan outline at its bottom and top, joined by four corner lines that run up the
    /// temple's outermost corners through the band, following its taper. Where the plan steps in
    /// sharply (the front platform ends below the shrine's walls) the corner lines stop instead of
    /// slanting across empty space, and the outline above the step is drawn as a ring of its own.
    /// </summary>
    private void BuildBandFrame(GlowOutline outline, float from, float to)
    {
        int rings = Mathf.Max(2, Mathf.RoundToInt((to - from) / 0.05f) + 1);
        float half = 1.5f / Bins;
        float maxJump = Mathf.Max(_bounds.size.x, _bounds.size.z) * 0.15f;
        var plans = new Vector2[rings][];
        var heights = new float[rings];
        for (int i = 0; i < rings; i++)
        {
            float f = Mathf.Lerp(from, to, i / (float)(rings - 1));
            // The end rings look into the band only, so they don't pick up the part above or below.
            plans[i] = Outline(i == 0 ? f : Mathf.Max(from, f - half), i == rings - 1 ? f : Mathf.Min(to, f + half));
            heights[i] = Y(f);
        }

        var steps = new HashSet<int>();
        for (int c = 0; c < 4; c++)
        {
            var line = new List<Vector3>();
            for (int i = 0; i < rings; i++)
            {
                Vector2 p = Corner(plans[i], c);
                var point = new Vector3(p.x, heights[i], p.y);
                if (line.Count > 0 && new Vector2(point.x - line[line.Count - 1].x, point.z - line[line.Count - 1].z).magnitude > maxJump)
                {
                    if (line.Count > 1)
                    {
                        outline.AddLine(line.ToArray(), false);
                    }
                    line.Clear();
                    steps.Add(i);
                }
                line.Add(point);
            }
            if (line.Count > 1)
            {
                outline.AddLine(line.ToArray(), false);
            }
        }

        outline.AddLine(Ring(plans[0], heights[0]), true);
        foreach (int i in steps)
        {
            if (i < rings - 1)
            {
                outline.AddLine(Ring(plans[i], heights[i]), true);
            }
        }
        outline.AddLine(Ring(plans[rings - 1], heights[rings - 1]), true);
    }

    /// <summary>The outline's point furthest out towards one diagonal: 0 (+x +z), 1 (+x -z), 2 (-x -z), 3 (-x +z).</summary>
    private static Vector2 Corner(Vector2[] plan, int corner)
    {
        var dir = new Vector2(corner < 2 ? 1f : -1f, corner == 0 || corner == 3 ? 1f : -1f);
        Vector2 best = plan[0];
        float bestDot = float.MinValue;
        foreach (var p in plan)
        {
            float d = Vector2.Dot(p, dir);
            if (d > bestDot)
            {
                bestDot = d;
                best = p;
            }
        }
        return best;
    }

    private static Vector3[] Ring(Vector2[] plan, float y)
    {
        var ring = new Vector3[plan.Length];
        for (int i = 0; i < plan.Length; i++)
        {
            ring[i] = new Vector3(plan[i].x, y, plan[i].y);
        }
        return ring;
    }

    private void BuildNiches(PartVisual v)
    {
        Rect wall = Footprint(0.2f, 0.3f);
        float y0 = Y(v.Part.From);
        float y1 = Y(v.Part.To);
        float yc = (y0 + y1) * 0.5f;
        float out_ = _cell;
        for (int side = 0; side < 4; side++)
        {
            if (side == _entranceSide)
            {
                continue;
            }
            Vector3 normal;
            Vector3 along;
            Vector3 centre;
            float length;
            switch (side)
            {
                case 0: normal = Vector3.right; along = Vector3.forward; centre = new Vector3(wall.xMax, yc, wall.center.y); length = wall.height; break;
                case 1: normal = Vector3.left; along = Vector3.forward; centre = new Vector3(wall.xMin, yc, wall.center.y); length = wall.height; break;
                case 2: normal = Vector3.forward; along = Vector3.right; centre = new Vector3(wall.center.x, yc, wall.yMax); length = wall.width; break;
                default: normal = Vector3.back; along = Vector3.right; centre = new Vector3(wall.center.x, yc, wall.yMin); length = wall.width; break;
            }
            centre += normal * out_;
            float w = length * 0.15f;
            float h = (y1 - y0) * 0.5f;
            v.Frame.AddLine(new[]
            {
                centre - along * w - Vector3.up * h,
                centre + along * w - Vector3.up * h,
                centre + along * w + Vector3.up * h,
                centre - along * w + Vector3.up * h,
            }, true);
            v.NicheCentres.Add(centre);
            v.NicheNormals.Add(normal);
        }
    }

    /// <summary>Sets how visible part <paramref name="index"/> is.</summary>
    /// <param name="frame">GlowOutline brightness, 0-1.</param>
    /// <param name="label">Label card opacity, 0-1.</param>
    /// <param name="scan">Progress of the scan line up through the part, 0-1, or negative for none.</param>
    public void SetPart(int index, float frame, float label, float scan)
    {
        if (index < 0 || index >= _visuals.Count)
        {
            return;
        }
        var v = _visuals[index];
        v.FrameAlpha = frame;
        v.LabelAlpha = label;
        v.ScanU = scan;
    }

    /// <summary>A golden ring rising up the whole temple. <paramref name="u"/> 0-1, negative hides it.</summary>
    public void SetSweep(float u, float alpha)
    {
        _sweepU = u;
        _sweepAlpha = alpha;
    }

    /// <summary>Scales everything (e.g. hides the overlay while the temple lies in pieces).</summary>
    public void SetMasterAlpha(float alpha)
    {
        _masterAlpha = alpha;
    }

    /// <summary>World position of the temple's top centre.</summary>
    public Vector3 TopCentre => _temple.TransformPoint(new Vector3(_bounds.center.x, _bounds.max.y, _bounds.center.z));

    /// <summary>World position of the temple's base centre.</summary>
    public Vector3 BaseCentre => _temple.TransformPoint(new Vector3(_bounds.center.x, _bounds.min.y, _bounds.center.z));

    /// <summary>Half the temple's larger plan extent in metres (a conservative radius around the base centre).</summary>
    public float WorldRadius =>
        Mathf.Max(_bounds.extents.x * _temple.lossyScale.x, _bounds.extents.z * _temple.lossyScale.z);

    /// <summary>The temple's height in metres.</summary>
    public float WorldHeight => _bounds.size.y * _temple.lossyScale.y;

    private void LateUpdate()
    {
        if (!IsReady || _temple == null)
        {
            return;
        }
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }
        Vector3 eyes = cam.transform.position;
        Matrix4x4 toWorld = _temple.localToWorldMatrix;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 5f);

        // The viewer's right, flattened, in temple space: labels go to either side of the temple.
        Vector3 right = cam.transform.right;
        right.y = 0f;
        right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
        Vector3 rightLocal = _temple.InverseTransformDirection(right);
        rightLocal.y = 0f;
        rightLocal.Normalize();

        foreach (var v in _visuals)
        {
            float frame = v.FrameAlpha * _masterAlpha;
            v.Frame.Draw(toWorld, StoryCard.Gold, frame * pulse, LineWidth, GlowWidth);

            if (v.ScanU >= 0f && v.ScanU <= 1f && v.Part.Kind == PartKind.Band && frame > 0.001f)
            {
                float f = Mathf.Lerp(v.Part.From, v.Part.To, v.ScanU);
                v.Scan.SetSingleLoop(Ring(BinOutline(f), Y(f)));
                float fade = Mathf.Sin(v.ScanU * Mathf.PI);
                v.Scan.Draw(toWorld, Color.Lerp(StoryCard.Gold, Color.white, 0.5f), fade * _masterAlpha, LineWidth * 1.6f, GlowWidth * 1.2f);
            }
            else
            {
                v.Scan.Draw(toWorld, Color.white, 0f, 0f, 0f);
            }

            LayoutLabel(v, eyes, rightLocal, toWorld);
        }
        StackLabels(1);
        StackLabels(-1);
        foreach (var v in _visuals)
        {
            DrawLabel(v, eyes, toWorld);
        }

        if (_sweepU >= 0f && _sweepU <= 1f && _sweepAlpha > 0.001f)
        {
            float f = Mathf.Clamp01(_sweepU);
            _sweep.SetSingleLoop(Ring(BinOutline(f), Y(f)));
            _sweep.Draw(toWorld, Color.Lerp(StoryCard.Gold, Color.white, 0.4f), _sweepAlpha, LineWidth * 2f, GlowWidth * 3f);
            float g = Mathf.Max(0f, f - 0.05f);
            _sweepTrail.SetSingleLoop(Ring(BinOutline(g), Y(g)));
            _sweepTrail.Draw(toWorld, StoryCard.Gold, _sweepAlpha * 0.45f, LineWidth, GlowWidth * 2f);
        }
        else
        {
            _sweep.Draw(toWorld, Color.white, 0f, 0f, 0f);
            _sweepTrail.Draw(toWorld, Color.white, 0f, 0f, 0f);
        }
    }

    /// <summary>
    /// How far along a ray from <paramref name="origin"/> its last crossing of the outline is (the
    /// outermost edge of the plan in that direction), or 0 if it crosses none.
    /// </summary>
    private static float FarthestHit(Vector2[] plan, Vector2 origin, Vector2 dir)
    {
        float best = 0f;
        for (int i = 0; i < plan.Length; i++)
        {
            Vector2 a = plan[i], b = plan[(i + 1) % plan.Length];
            Vector2 e = b - a;
            float denom = dir.x * e.y - dir.y * e.x;
            if (Mathf.Abs(denom) < 1e-8f)
            {
                continue;
            }
            Vector2 w = a - origin;
            float t = (w.x * e.y - w.y * e.x) / denom;
            float s = (w.x * dir.y - w.y * dir.x) / denom;
            if (t > best && s >= 0f && s <= 1f)
            {
                best = t;
            }
        }
        return best;
    }

    private void LayoutLabel(PartVisual v, Vector3 eyes, Vector3 rightLocal, Matrix4x4 toWorld)
    {
        v.Shown = v.LabelAlpha * _masterAlpha > 0.001f;
        if (!v.Shown)
        {
            return;
        }

        int side = v.Part.Side >= 0 ? 1 : -1;
        Vector3 anchorLocal;
        if (v.Part.Kind == PartKind.Niches && v.NicheCentres.Count > 0)
        {
            // The niche on the wall that faces the viewer most.
            Vector3 toEyes = _temple.InverseTransformPoint(eyes);
            int best = 0;
            float bestDot = float.MinValue;
            for (int i = 0; i < v.NicheCentres.Count; i++)
            {
                float d = Vector3.Dot(v.NicheNormals[i], (toEyes - v.NicheCentres[i]).normalized);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = i;
                }
            }
            anchorLocal = v.NicheCentres[best];
        }
        else
        {
            // Where the viewer's left or right meets the part's outline, half way up the part.
            Rect r = Bounds2D(v.Middle);
            var dir = new Vector2(rightLocal.x * side, rightLocal.z * side);
            float t = FarthestHit(v.Middle, r.center, dir);
            float yFrac = v.Part.To > 0.9f ? Mathf.Lerp(v.Part.From, v.Part.To, 0.3f) : (v.Part.From + v.Part.To) * 0.5f;
            Vector2 p = r.center + dir * t;
            anchorLocal = new Vector3(p.x, Y(yFrac), p.y);
        }

        Vector3 anchor = toWorld.MultiplyPoint3x4(anchorLocal);
        Vector3 right = _temple.TransformDirection(rightLocal).normalized;

        // The card sits beside the temple's widest point at this height, out past the edge.
        Rect wide = _wholeFootprint;
        float halfWidth = Mathf.Max(wide.width, wide.height) * 0.5f * _temple.lossyScale.x;
        Vector3 centre = _temple.TransformPoint(new Vector3(wide.center.x, anchorLocal.y, wide.center.y));
        v.Anchor = anchor;
        v.Right = right;
        v.SideSign = side;
        v.CardInner = centre + right * side * (halfWidth + LabelGap);
        // Cards grow with distance so they stay readable from across the room.
        v.Size = Mathf.Clamp(Vector3.Distance(v.CardInner, eyes) / 0.9f, 1f, 2.2f);
    }

    /// <summary>
    /// Labels on the same side of the temple are pushed apart vertically (upwards, from the lowest)
    /// so their cards never overlap.
    /// </summary>
    private void StackLabels(int side)
    {
        var column = new List<PartVisual>();
        foreach (var v in _visuals)
        {
            if (v.Shown && v.SideSign == side)
            {
                column.Add(v);
            }
        }
        column.Sort((a, b) => a.CardInner.y.CompareTo(b.CardInner.y));
        for (int i = 1; i < column.Count; i++)
        {
            PartVisual below = column[i - 1];
            PartVisual above = column[i];
            float need = (below.Label.WorldHeight * below.Size + above.Label.WorldHeight * above.Size) * 0.5f + 0.012f;
            float gap = above.CardInner.y - below.CardInner.y;
            if (gap < need)
            {
                above.CardInner += Vector3.up * (need - gap);
            }
        }
    }

    private void DrawLabel(PartVisual v, Vector3 eyes, Matrix4x4 toWorld)
    {
        float alpha = v.LabelAlpha * _masterAlpha;
        v.Label.Alpha = v.Shown ? alpha : 0f;
        if (!v.Shown)
        {
            v.Pin.Draw(toWorld, Color.white, 0f, 0f, 0f);
            v.Leader.Draw(toWorld, Color.white, 0f, 0f, 0f);
            return;
        }
        Vector3 anchor = v.Anchor;
        Vector3 cardInner = v.CardInner;
        Vector3 cardCentre = cardInner + v.Right * v.SideSign * (v.Label.WorldWidth * 0.5f * v.Size);
        v.Label.Place(cardCentre, eyes, v.Size);

        // Pin on the part and a leader line out to the card.
        Vector3 toEye = (eyes - anchor).normalized;
        Vector3 pinRight = Vector3.Cross(Vector3.up, toEye).normalized;
        float pinR = 0.006f;
        var pin = new Vector3[10];
        for (int i = 0; i < pin.Length; i++)
        {
            float a = i / (float)pin.Length * Mathf.PI * 2f;
            pin[i] = anchor + (pinRight * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * pinR;
        }
        v.Pin.SetSingleLoop(pin);
        v.Pin.Draw(Matrix4x4.identity, StoryCard.Gold, alpha, LineWidth * 1.4f, GlowWidth);
        v.Leader.SetSingleLine(new[] { anchor + (cardInner - anchor).normalized * pinR, cardInner });
        v.Leader.Draw(Matrix4x4.identity, StoryCard.Gold, alpha * 0.9f, LineWidth * 0.8f, GlowWidth * 0.6f);
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null)
        {
            Destroy(_lineMaterial);
        }
    }
}
