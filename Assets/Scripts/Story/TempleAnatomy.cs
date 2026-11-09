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
/// temple's vertices are binned by height into a profile of footprints (the temple's width at
/// each level), and each part is a height range of that profile: the adhisthana (base), the
/// bhitti (wall), the vimana (tower) and the devakoshthas (wall niches). The niches sit at the
/// middle of each wall except the entrance side, which is found as the side where the base runs
/// furthest out past the wall (the front platform).
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

    private const int Bins = 60;
    private const float LineWidth = 0.0022f;      // metres
    private const float GlowWidth = 0.009f;
    private const float LabelScale = 0.0009f;     // metres per canvas unit at arm's length
    private const float LabelGap = 0.05f;         // metres between the temple and a label

    public bool IsReady { get; private set; }

    private Transform _temple;
    private Part[] _parts;
    private Material _lineMaterial;

    // Temple-space profile.
    private Bounds _bounds;
    private readonly Rect[] _profile = new Rect[Bins];   // x = temple x, y = temple z
    private readonly bool[] _profileValid = new bool[Bins];
    private int _entranceSide; // 0 +x, 1 -x, 2 +z, 3 -z

    private class PartVisual
    {
        public Part Part;
        public GlowOutline Frame;
        public GlowOutline Scan;
        public GlowOutline Pin;
        public GlowOutline Leader;
        public StoryCard Label;
        public Rect Footprint;               // temple space, at the part's height range
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
        // Gather the visible temple meshes' vertices in temple space (main thread: Mesh API).
        Matrix4x4 worldToTemple = _temple.worldToLocalMatrix;
        var meshes = new List<(Vector3[] vertices, Matrix4x4 toTemple)>();
        foreach (var mf in _temple.GetComponentsInChildren<MeshFilter>(false))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null || !mf.sharedMesh.isReadable)
            {
                continue;
            }
            meshes.Add((mf.sharedMesh.vertices, worldToTemple * mf.transform.localToWorldMatrix));
            yield return null; // spread the copies over a few frames
        }
        if (meshes.Count == 0)
        {
            Debug.LogWarning("TempleAnatomy: no readable temple meshes; the story will run without part highlights.", this);
            yield break;
        }

        Task<Bounds> task = Task.Run(() => BuildProfile(meshes));
        while (!task.IsCompleted)
        {
            yield return null;
        }
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception, this);
            yield break;
        }
        _bounds = task.Result;
        _entranceSide = FindEntranceSide();
        BuildVisuals();
        IsReady = true;
    }

    /// <summary>Worker thread: temple bounds and the per-height footprint profile.</summary>
    private Bounds BuildProfile(List<(Vector3[] vertices, Matrix4x4 toTemple)> meshes)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = -min;
        foreach (var (vertices, m) in meshes)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = m.MultiplyPoint3x4(vertices[i]);
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }
        float height = Mathf.Max(max.y - min.y, 1e-5f);
        var binMin = new Vector2[Bins];
        var binMax = new Vector2[Bins];
        for (int b = 0; b < Bins; b++)
        {
            binMin[b] = new Vector2(float.MaxValue, float.MaxValue);
            binMax[b] = new Vector2(float.MinValue, float.MinValue);
        }
        foreach (var (vertices, m) in meshes)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = m.MultiplyPoint3x4(vertices[i]);
                int b = Mathf.Clamp((int)((v.y - min.y) / height * Bins), 0, Bins - 1);
                binMin[b] = Vector2.Min(binMin[b], new Vector2(v.x, v.z));
                binMax[b] = Vector2.Max(binMax[b], new Vector2(v.x, v.z));
            }
        }
        for (int b = 0; b < Bins; b++)
        {
            _profileValid[b] = binMin[b].x <= binMax[b].x;
            _profile[b] = _profileValid[b] ? Rect.MinMaxRect(binMin[b].x, binMin[b].y, binMax[b].x, binMax[b].y) : default;
        }
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    /// <summary>The union of the footprints between two height fractions.</summary>
    private Rect Footprint(float from, float to)
    {
        int b0 = Mathf.Clamp(Mathf.FloorToInt(from * Bins), 0, Bins - 1);
        int b1 = Mathf.Clamp(Mathf.CeilToInt(to * Bins) - 1, b0, Bins - 1);
        bool any = false;
        Rect r = default;
        for (int b = b0; b <= b1; b++)
        {
            if (!_profileValid[b])
            {
                continue;
            }
            r = any ? Union(r, _profile[b]) : _profile[b];
            any = true;
        }
        if (!any)
        {
            r = Rect.MinMaxRect(_bounds.min.x, _bounds.min.z, _bounds.max.x, _bounds.max.z);
        }
        return r;
    }

    private static Rect Union(Rect a, Rect b)
    {
        return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
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

    private Rect Pad(Rect r, float fraction)
    {
        float p = Mathf.Max(_bounds.size.x, _bounds.size.z) * fraction;
        return Rect.MinMaxRect(r.xMin - p, r.yMin - p, r.xMax + p, r.yMax + p);
    }

    private void BuildVisuals()
    {
        var root = transform;
        foreach (var part in _parts)
        {
            var v = new PartVisual { Part = part };
            v.Footprint = Pad(Footprint(part.From, part.To), 0.03f);
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

    /// <summary>Rings that follow the temple's silhouette through the band, joined at the corners.</summary>
    private void BuildBandFrame(GlowOutline outline, float from, float to)
    {
        int rings = Mathf.Max(2, Mathf.RoundToInt((to - from) / 0.1f) + 1);
        var corners = new List<Vector3[]>();
        for (int i = 0; i < rings; i++)
        {
            float f = Mathf.Lerp(from, to, i / (float)(rings - 1));
            float half = 0.5f / Bins;
            Rect r = Pad(Footprint(Mathf.Max(from, f - half * 2f), Mathf.Min(to, f + half * 2f)), 0.03f);
            float y = Y(f);
            var ring = RectCorners(r, y);
            corners.Add(ring);
            // Only the bottom and top rings are drawn; the inner ones shape the corner lines.
            if (i == 0 || i == rings - 1)
            {
                outline.AddLine(ring, true);
            }
        }
        for (int c = 0; c < 4; c++)
        {
            var edge = new Vector3[rings];
            for (int i = 0; i < rings; i++)
            {
                edge[i] = corners[i][c];
            }
            outline.AddLine(edge, false);
        }
    }

    private void BuildNiches(PartVisual v)
    {
        Rect wall = Footprint(0.2f, 0.3f);
        float y0 = Y(v.Part.From);
        float y1 = Y(v.Part.To);
        float yc = (y0 + y1) * 0.5f;
        float out_ = Mathf.Max(_bounds.size.x, _bounds.size.z) * 0.012f;
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

    private static Vector3[] RectCorners(Rect r, float y)
    {
        return new[]
        {
            new Vector3(r.xMin, y, r.yMin),
            new Vector3(r.xMax, y, r.yMin),
            new Vector3(r.xMax, y, r.yMax),
            new Vector3(r.xMin, y, r.yMax),
        };
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
                v.Scan.SetSingleLoop(RectCorners(Pad(Footprint(f - 1f / Bins, f + 1f / Bins), 0.04f), Y(f)));
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
            _sweep.SetSingleLoop(RectCorners(Pad(Footprint(f - 1f / Bins, f + 1f / Bins), 0.05f), Y(f)));
            _sweep.Draw(toWorld, Color.Lerp(StoryCard.Gold, Color.white, 0.4f), _sweepAlpha, LineWidth * 2f, GlowWidth * 3f);
            float g = Mathf.Max(0f, f - 0.05f);
            _sweepTrail.SetSingleLoop(RectCorners(Pad(Footprint(g - 1f / Bins, g + 1f / Bins), 0.035f), Y(g)));
            _sweepTrail.Draw(toWorld, StoryCard.Gold, _sweepAlpha * 0.45f, LineWidth, GlowWidth * 2f);
        }
        else
        {
            _sweep.Draw(toWorld, Color.white, 0f, 0f, 0f);
            _sweepTrail.Draw(toWorld, Color.white, 0f, 0f, 0f);
        }
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
            Rect r = v.Footprint;
            Vector3 dir = rightLocal * side;
            float tx = Mathf.Abs(dir.x) > 1e-4f ? r.width * 0.5f / Mathf.Abs(dir.x) : float.MaxValue;
            float tz = Mathf.Abs(dir.z) > 1e-4f ? r.height * 0.5f / Mathf.Abs(dir.z) : float.MaxValue;
            float t = Mathf.Min(tx, tz);
            float yFrac = v.Part.To > 0.9f ? Mathf.Lerp(v.Part.From, v.Part.To, 0.3f) : (v.Part.From + v.Part.To) * 0.5f;
            anchorLocal = new Vector3(r.center.x, Y(yFrac), r.center.y) + dir * t;
        }

        Vector3 anchor = toWorld.MultiplyPoint3x4(anchorLocal);
        Vector3 right = _temple.TransformDirection(rightLocal).normalized;

        // The card sits beside the temple's widest point at this height, out past the edge.
        Rect wide = Footprint(0f, 1f);
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
