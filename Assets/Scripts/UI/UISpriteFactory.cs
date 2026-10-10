using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates the control panel's sprites at runtime (rounded rects, soft shadow and line icons),
/// so the UI needs no imported art. Icons are rasterised from simple signed-distance shapes
/// and cached, so each one is built once per session.
/// </summary>
public static class UISpriteFactory
{
    public enum Icon
    {
        Play,
        Pause,
        Home,
        Pin,
        Collapse,
        Menu,
    }

    private const int IconSize = 128;
    private const int RoundedSize = 64;
    private const int RoundedRadius = 24;

    private static Sprite _rounded;
    private static Sprite _shadow;
    private static readonly Dictionary<Icon, Sprite> _icons = new Dictionary<Icon, Sprite>();

    /// <summary>
    /// 9-sliced rounded rect with a 24px corner. Scale the corner with
    /// <see cref="UnityEngine.UI.Image.pixelsPerUnitMultiplier"/> = 24 / desiredRadius.
    /// </summary>
    public static Sprite Rounded
    {
        get
        {
            if (_rounded == null)
            {
                _rounded = BuildRounded(RoundedSize, RoundedRadius, 1f);
            }
            return _rounded;
        }
    }

    /// <summary>9-sliced soft blurred rounded rect used as a drop shadow / glow.</summary>
    public static Sprite Shadow
    {
        get
        {
            if (_shadow == null)
            {
                _shadow = BuildRounded(RoundedSize, RoundedRadius, 14f);
            }
            return _shadow;
        }
    }

    public static float RoundedCornerPixels => RoundedRadius;

    public static Sprite GetIcon(Icon icon)
    {
        if (!_icons.TryGetValue(icon, out Sprite sprite) || sprite == null)
        {
            sprite = BuildIcon(IconShape(icon));
            sprite.name = "Icon_" + icon;
            _icons[icon] = sprite;
        }
        return sprite;
    }

    private static Sprite BuildRounded(int size, int radius, float softness)
    {
        var tex = NewTexture(size);
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                float d = RoundedBox(p, new Vector2(half, half), radius);
                float a = softness <= 1f
                    ? Mathf.Clamp01(0.5f - d)
                    : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(-softness, 0f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(pixels);
        // Regenerate the mip chain: world-space panels are viewed small, and unfilled mips render as hard squares.
        tex.Apply(true, true);
        var border = new Vector4(radius, radius, radius, radius);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, border);
    }

    private static Sprite BuildIcon(Func<Vector2, float> shape)
    {
        var tex = NewTexture(IconSize);
        var pixels = new Color32[IconSize * IconSize];
        // Shapes are authored in [-0.5, 0.5] space; one pixel in that space:
        float px = 1f / IconSize;
        for (int y = 0; y < IconSize; y++)
        {
            for (int x = 0; x < IconSize; x++)
            {
                var p = new Vector2((x + 0.5f) * px - 0.5f, (y + 0.5f) * px - 0.5f);
                float d = shape(p);
                float a = Mathf.Clamp01(0.5f - d / px);
                pixels[y * IconSize + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(true, true);
        return Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Texture2D NewTexture(int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
    }

    // ---------------------------------------------------------------- icon shapes

    private const float Stroke = 0.045f;

    private static Func<Vector2, float> IconShape(Icon icon)
    {
        switch (icon)
        {
            case Icon.Play:
                return p => Polygon(p, new Vector2(-0.2f, -0.28f), new Vector2(-0.2f, 0.28f), new Vector2(0.28f, 0f)) - 0.02f;
            case Icon.Pause:
                return p => Mathf.Min(
                    RoundedBox(p - new Vector2(-0.12f, 0f), new Vector2(0.065f, 0.26f), 0.03f),
                    RoundedBox(p - new Vector2(0.12f, 0f), new Vector2(0.065f, 0.26f), 0.03f));
            case Icon.Home:
                return p =>
                {
                    float roof = Mathf.Min(Segment(p, new Vector2(-0.3f, 0.02f), new Vector2(0f, 0.3f)),
                        Segment(p, new Vector2(0f, 0.3f), new Vector2(0.3f, 0.02f)));
                    float walls = Mathf.Min(Segment(p, new Vector2(-0.2f, -0.06f), new Vector2(-0.2f, -0.3f)),
                        Mathf.Min(Segment(p, new Vector2(-0.2f, -0.3f), new Vector2(0.2f, -0.3f)),
                            Segment(p, new Vector2(0.2f, -0.3f), new Vector2(0.2f, -0.06f))));
                    float door = RoundedBox(p - new Vector2(0f, -0.2f), new Vector2(0.055f, 0.1f), 0.02f);
                    return Mathf.Min(Mathf.Min(roof, walls) - Stroke, door);
                };
            case Icon.Pin:
                return p =>
                {
                    float head = (p - new Vector2(0f, 0.13f)).magnitude - 0.15f;
                    float stem = Segment(p, new Vector2(0f, -0.02f), new Vector2(0f, -0.33f)) - Stroke * 0.8f;
                    return Mathf.Min(head, stem);
                };
            case Icon.Collapse:
                return p => Mathf.Min(Segment(p, new Vector2(-0.22f, 0.1f), new Vector2(0f, -0.12f)),
                    Segment(p, new Vector2(0f, -0.12f), new Vector2(0.22f, 0.1f))) - Stroke;
            case Icon.Menu:
                return p => Mathf.Min(Segment(p, new Vector2(-0.24f, 0.17f), new Vector2(0.24f, 0.17f)),
                    Mathf.Min(Segment(p, new Vector2(-0.24f, 0f), new Vector2(0.24f, 0f)),
                        Segment(p, new Vector2(-0.24f, -0.17f), new Vector2(0.24f, -0.17f)))) - Stroke;
            default:
                return p => 1f;
        }
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude;
    }

    private static float RoundedBox(Vector2 p, Vector2 halfSize, float r)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - halfSize + new Vector2(r, r);
        return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }

    /// <summary>Signed distance to a convex polygon (any winding).</summary>
    private static float Polygon(Vector2 p, params Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i];
            Vector2 w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y;
            bool c2 = p.y < v[j].y;
            bool c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
            {
                s *= -1f;
            }
        }
        return s * Mathf.Sqrt(d);
    }
}
