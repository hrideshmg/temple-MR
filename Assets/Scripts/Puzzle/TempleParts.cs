using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The architectural parts the temple breaks into for the rebuild puzzle, with what each one is,
/// which parts must already be in place before it can go back, and the rules that sort the model's
/// triangles into them.
///
/// The parts follow the vertical order of a Dravida vimana (upapitha, adhisthana, bhitti, prastara,
/// hara, tala, griva, shikhara, stupi) plus the separately carved figures: Nandi and the two door
/// guardians. (The niche deities are flat panels in the model, so they stay with their walls.)
///
/// The model has no part names, so sorting uses what it does have:
/// - Figure meshes are recognised by their material (named after its texture on import:
///   Main_Nandi_CLR, Dwarapalakar_CLR, L_Figure_CLR, Petal_1, down_Big_Dragon_CLR, Gana_CLR...).
/// - Everything else is sorted triangle by triangle: by height (the cuts in <see cref="Cuts"/>, as
///   fractions of the temple's height) and, for the walls and figures, by which side of the shrine it
///   is on. The entrance side is the one where the base reaches furthest out past the walls (the front
///   platform), so "left" and "right" are as seen standing in front of the entrance.
///
/// Pure data and maths: safe to run on a worker thread.
/// </summary>
public static class TempleParts
{
    public enum Id
    {
        Upapitha,
        FrontPlatform,
        Adhisthana,
        Nandi,
        WallFront,
        WallLeft,
        WallRight,
        WallBack,
        DvarapalaLeft,
        DvarapalaRight,
        Prastara,
        Hara,
        UpperTala,
        Griva,
        Shikhara,
        Stupi,
    }

    public const int Count = 16;

    public class Info
    {
        public Id Id;
        public string Title;
        public string Subtitle;
        public string Body;
        public Id[] Needs;
    }

    /// <summary>Height cuts between the stacked parts, as fractions of the temple's height.</summary>
    [Serializable]
    public class Cuts
    {
        [Tooltip("Top of the plain platform (upapitha).")]
        [Range(0f, 1f)] public float Plinth = 0.06f;
        [Tooltip("Top of the moulded base (adhisthana).")]
        [Range(0f, 1f)] public float Base = 0.12f;
        [Tooltip("Top of the walls (bhitti).")]
        [Range(0f, 1f)] public float Wall = 0.335f;
        [Tooltip("Top of the cornice (prastara).")]
        [Range(0f, 1f)] public float Cornice = 0.445f;
        [Tooltip("Top of the first storey's parapet (hara).")]
        [Range(0f, 1f)] public float Hara = 0.62f;
        [Tooltip("Top of the second storey (tala).")]
        [Range(0f, 1f)] public float Tala = 0.8f;
        [Tooltip("Top of the neck (griva).")]
        [Range(0f, 1f)] public float Griva = 0.86f;
        [Tooltip("Top of the dome (shikhara); the finial (stupi) is above.")]
        [Range(0f, 1f)] public float Shikhara = 0.945f;
    }

    private static readonly Info[] _all =
    {
        new Info
        {
            Id = Id.Upapitha, Title = "Upapitha", Subtitle = "The platform",
            Body = "A plain stone platform that lifts the whole temple, with steps up to the entrance. Everything else stands on it.",
            Needs = new Id[0],
        },
        new Info
        {
            Id = Id.FrontPlatform, Title = "Mandapa base", Subtitle = "Front hall platform",
            Body = "The base of the hall that stood in front of the shrine, where worshippers gathered before entering.",
            Needs = new[] { Id.Upapitha },
        },
        new Info
        {
            Id = Id.Adhisthana, Title = "Adhisthana", Subtitle = "The moulded base",
            Body = "The shrine's moulded base: rows of lotus petals (padma) and a frieze of yalis, mythical lion-like beasts.",
            Needs = new[] { Id.Upapitha },
        },
        new Info
        {
            Id = Id.Nandi, Title = "Nandi", Subtitle = "Shiva's bull",
            Body = "Shiva's mount sits on the front platform facing the shrine, gazing at the deity inside.",
            Needs = new[] { Id.FrontPlatform },
        },
        new Info
        {
            Id = Id.WallFront, Title = "Bhitti", Subtitle = "Front wall",
            Body = "The front wall of the sanctum (garbhagriha), with the doorway through which the deity is seen.",
            Needs = new[] { Id.Adhisthana },
        },
        new Info
        {
            Id = Id.WallLeft, Title = "Bhitti", Subtitle = "Left wall",
            Body = "A wall of the sanctum, divided by pilasters into bays. Its central niche (devakoshtha) holds a carved deity.",
            Needs = new[] { Id.Adhisthana },
        },
        new Info
        {
            Id = Id.WallRight, Title = "Bhitti", Subtitle = "Right wall",
            Body = "A wall of the sanctum, divided by pilasters into bays. Its central niche (devakoshtha) holds a carved deity.",
            Needs = new[] { Id.Adhisthana },
        },
        new Info
        {
            Id = Id.WallBack, Title = "Bhitti", Subtitle = "Rear wall",
            Body = "The rear wall of the sanctum. Chola shrines set deities in niches (devakoshthas) on the three walls without a door.",
            Needs = new[] { Id.Adhisthana },
        },
        new Info
        {
            Id = Id.DvarapalaLeft, Title = "Dvarapala", Subtitle = "Door guardian",
            Body = "One of the pair of guardians who flank the doorway and protect the sanctum.",
            Needs = new[] { Id.WallFront },
        },
        new Info
        {
            Id = Id.DvarapalaRight, Title = "Dvarapala", Subtitle = "Door guardian",
            Body = "One of the pair of guardians who flank the doorway and protect the sanctum.",
            Needs = new[] { Id.WallFront },
        },
        new Info
        {
            Id = Id.Prastara, Title = "Prastara", Subtitle = "The cornice",
            Body = "The roof edge over the walls: a curved kapota cornice above a frieze of ganas, Shiva's dwarf attendants.",
            Needs = new[] { Id.WallFront, Id.WallLeft, Id.WallRight, Id.WallBack },
        },
        new Info
        {
            Id = Id.Hara, Title = "Hara", Subtitle = "First-storey parapet",
            Body = "A chain of miniature shrines around the roof: square kutas at the corners and oblong salas between.",
            Needs = new[] { Id.Prastara },
        },
        new Info
        {
            Id = Id.UpperTala, Title = "Tala", Subtitle = "Second storey",
            Body = "The upper storey repeats the shrine in miniature, smaller and set back, so the tower steps up like a mountain.",
            Needs = new[] { Id.Hara },
        },
        new Info
        {
            Id = Id.Griva, Title = "Griva", Subtitle = "The neck",
            Body = "The recessed neck that carries the dome, with figures seated at its corners.",
            Needs = new[] { Id.UpperTala },
        },
        new Info
        {
            Id = Id.Shikhara, Title = "Shikhara", Subtitle = "The crowning dome",
            Body = "The dome that crowns the vimana, the summit of the temple as a sacred mountain.",
            Needs = new[] { Id.Griva },
        },
        new Info
        {
            Id = Id.Stupi, Title = "Stupi", Subtitle = "The finial",
            Body = "The pot-shaped kalasha finial at the very top. It goes on last and completes the temple.",
            Needs = new[] { Id.Shikhara },
        },
    };

    public static Info Get(Id id) => _all[(int)id];

    // =====================================================================  classification

    /// <summary>One source mesh, already in temple space.</summary>
    public class Source
    {
        public Vector3[] Vertices;
        public int[][] SubmeshTriangles;
        /// <summary>Lower-case material name per submesh.</summary>
        public string[] MaterialNames;
    }

    /// <summary>
    /// Sorts every triangle into a part. Returns [source][submesh][triangle] = (int)<see cref="Id"/>.
    /// </summary>
    public static int[][][] Classify(List<Source> sources, Cuts cuts)
    {
        var frame = Frame.Measure(sources);
        var result = new int[sources.Count][][];
        for (int s = 0; s < sources.Count; s++)
        {
            var src = sources[s];
            Id? whole = ClassifyObject(src, frame);
            result[s] = new int[src.SubmeshTriangles.Length][];
            for (int sm = 0; sm < src.SubmeshTriangles.Length; sm++)
            {
                int[] tris = src.SubmeshTriangles[sm];
                var parts = new int[tris.Length / 3];
                string material = sm < src.MaterialNames.Length ? src.MaterialNames[sm] : "";
                for (int t = 0; t < parts.Length; t++)
                {
                    if (whole.HasValue)
                    {
                        parts[t] = (int)whole.Value;
                        continue;
                    }
                    Vector3 c = (src.Vertices[tris[t * 3]] + src.Vertices[tris[t * 3 + 1]] + src.Vertices[tris[t * 3 + 2]]) / 3f;
                    parts[t] = (int)ClassifyPoint(c, material, frame, cuts);
                }
                result[s][sm] = parts;
            }
        }
        return result;
    }

    /// <summary>Carved figures go whole into their own part, recognised by material.</summary>
    private static Id? ClassifyObject(Source src, Frame frame)
    {
        string material = DominantMaterial(src);
        Vector3 centre = Centre(src);
        if (material.Contains("nandi"))
        {
            return Id.Nandi;
        }
        if (material.Contains("dwarapal") || material.Contains("dvarapal"))
        {
            return frame.LeftRight(centre) < 0f ? Id.DvarapalaLeft : Id.DvarapalaRight;
        }
        if (material.Contains("l_figure") || material.Contains("r_side_figure") || material.Contains("b_figure"))
        {
            // The niche deities are flat carved panels: they stay with their wall.
            switch (frame.Side(centre))
            {
                case WallSide.Left: return Id.WallLeft;
                case WallSide.Right: return Id.WallRight;
                case WallSide.Back: return Id.WallBack;
                default: return Id.WallFront;
            }
        }
        if (material.Contains("petal") || material.Contains("big_dragon"))
        {
            return frame.InFront(centre) ? Id.FrontPlatform : Id.Adhisthana;
        }
        if (material.Contains("gana"))
        {
            return Id.Prastara;
        }
        if (material.Contains("step"))
        {
            return Id.Upapitha;
        }
        return null;
    }

    private static Id ClassifyPoint(Vector3 p, string material, Frame frame, Cuts cuts)
    {
        float h = frame.Height(p);
        if (h < cuts.Plinth)
        {
            return Id.Upapitha;
        }
        if (h < cuts.Wall && frame.InFront(p))
        {
            return Id.FrontPlatform;
        }
        if (h < cuts.Base)
        {
            return Id.Adhisthana;
        }
        if (h < cuts.Wall)
        {
            switch (frame.Side(p))
            {
                case WallSide.Front: return Id.WallFront;
                case WallSide.Left: return Id.WallLeft;
                case WallSide.Right: return Id.WallRight;
                default: return Id.WallBack;
            }
        }
        if (h < cuts.Cornice)
        {
            return Id.Prastara;
        }
        if (h < cuts.Hara)
        {
            return Id.Hara;
        }
        if (h < cuts.Tala)
        {
            return Id.UpperTala;
        }
        if (h < cuts.Griva)
        {
            return Id.Griva;
        }
        return h < cuts.Shikhara ? Id.Shikhara : Id.Stupi;
    }

    private static string DominantMaterial(Source src)
    {
        int best = -1;
        string name = "";
        for (int sm = 0; sm < src.SubmeshTriangles.Length && sm < src.MaterialNames.Length; sm++)
        {
            if (src.SubmeshTriangles[sm].Length > best)
            {
                best = src.SubmeshTriangles[sm].Length;
                name = src.MaterialNames[sm];
            }
        }
        return name ?? "";
    }

    private static Vector3 Centre(Source src)
    {
        if (src.Vertices.Length == 0)
        {
            return Vector3.zero;
        }
        Vector3 min = src.Vertices[0], max = src.Vertices[0];
        foreach (var v in src.Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return (min + max) * 0.5f;
    }

    private enum WallSide
    {
        Front,
        Back,
        Left,
        Right,
    }

    /// <summary>The temple's height range, the shrine's square footprint and which way it faces.</summary>
    private class Frame
    {
        private float _minY;
        private float _height;
        private Vector2 _centre;    // shrine centre (x, z)
        private Vector2 _half;      // shrine half-size (x, z)
        private Vector2 _forward;   // towards the entrance (an axis direction)
        private Vector2 _right;     // to the right of someone facing the entrance from outside

        public float Height(Vector3 p) => (p.y - _minY) / _height;

        public static Frame Measure(List<Source> sources)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var s in sources)
            {
                foreach (var v in s.Vertices)
                {
                    minY = Mathf.Min(minY, v.y);
                    maxY = Mathf.Max(maxY, v.y);
                }
            }
            var f = new Frame { _minY = minY, _height = Mathf.Max(maxY - minY, 1e-5f) };

            // Footprints of the wall zone (the shrine) and the lower base (shrine plus front platform).
            Vector2 wallMin = new Vector2(float.MaxValue, float.MaxValue), wallMax = -wallMin;
            Vector2 baseMin = wallMin, baseMax = -wallMin;
            foreach (var s in sources)
            {
                foreach (var v in s.Vertices)
                {
                    float h = f.Height(v);
                    var xz = new Vector2(v.x, v.z);
                    if (h >= 0.2f && h < 0.3f)
                    {
                        wallMin = Vector2.Min(wallMin, xz);
                        wallMax = Vector2.Max(wallMax, xz);
                    }
                    else if (h >= 0.02f && h < 0.12f)
                    {
                        baseMin = Vector2.Min(baseMin, xz);
                        baseMax = Vector2.Max(baseMax, xz);
                    }
                }
            }
            if (wallMin.x > wallMax.x)
            {
                wallMin = baseMin;
                wallMax = baseMax;
            }
            f._centre = (wallMin + wallMax) * 0.5f;
            f._half = Vector2.Max((wallMax - wallMin) * 0.5f, new Vector2(1e-4f, 1e-4f));

            // The entrance faces the side where the base runs furthest out past the walls.
            float[] reach = { baseMax.x - wallMax.x, wallMin.x - baseMin.x, baseMax.y - wallMax.y, wallMin.y - baseMin.y };
            Vector2[] dirs = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
            int best = 0;
            for (int i = 1; i < 4; i++)
            {
                if (reach[i] > reach[best])
                {
                    best = i;
                }
            }
            f._forward = dirs[best];
            // Facing the entrance from outside means looking along -forward; right is then forward x up.
            f._right = new Vector2(-f._forward.y, f._forward.x);
            return f;
        }

        private Vector2 Local(Vector3 p)
        {
            Vector2 d = new Vector2(p.x, p.z) - _centre;
            return new Vector2(Vector2.Dot(d, _right) / Half(_right), Vector2.Dot(d, _forward) / Half(_forward));
        }

        private float Half(Vector2 axis) => Mathf.Abs(axis.x) > 0.5f ? _half.x : _half.y;

        /// <summary>Beyond the front wall, on the entrance side.</summary>
        public bool InFront(Vector3 p) => Local(p).y > 1.06f;

        /// <summary>Negative left of the shrine's centre line, positive right.</summary>
        public float LeftRight(Vector3 p) => Local(p).x;

        public WallSide Side(Vector3 p)
        {
            Vector2 l = Local(p);
            if (Mathf.Abs(l.x) > Mathf.Abs(l.y))
            {
                return l.x < 0f ? WallSide.Left : WallSide.Right;
            }
            return l.y > 0f ? WallSide.Front : WallSide.Back;
        }
    }
}
