using System.Collections.Generic;
using Meta.XR.EnvironmentDepth;
using UnityEngine;

/// <summary>
/// Hides virtual objects behind real-world ones (chairs, cupboards, people) using the headset's
/// live depth (Meta Depth API, Quest 3 / 3S).
///
/// Spawns itself after the scene loads; nothing needs to be placed in the scene. It adds an
/// <see cref="EnvironmentDepthManager"/> and switches every URP Lit material in the scene to the
/// occlusion-aware "TestHands/Occluded Lit" shader. Materials copied later from those (such as the
/// temple rubble) inherit it. When depth is unavailable (Link without Spatial Data, the simulator,
/// older headsets) nothing is changed and the scene renders as before.
///
/// Soft occlusion is enabled scene-wide for smooth edges on the intact temple. Small rubble fringes and
/// gets swallowed by the floor, so TempleDemolition turns occlusion off for its rubble materials
/// (_NoOcclusion in OccludedLit.shader). The scanned floor height is passed to the shader so things
/// lying on the floor aren't swallowed by it (see OcclusionBias in OccludedLit.shader).
/// </summary>
public class DepthOcclusion : MonoBehaviour
{
    private const string ShaderPath = "Shaders/OccludedLit";
    private const float NoFloor = -10000f;

    private static readonly int FloorHeightId = Shader.PropertyToID("_OcclusionFloorHeight");

    private static readonly string[] ConvertibleShaders =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!EnvironmentDepthManager.IsSupported)
        {
            Debug.Log("DepthOcclusion: environment depth is not supported here; virtual objects won't be occluded.");
            return;
        }
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"DepthOcclusion: shader 'Resources/{ShaderPath}' is missing.");
            return;
        }

        var go = new GameObject("Depth Occlusion");
        go.SetActive(false); // configure before Awake runs
        if (FindAnyObjectByType<EnvironmentDepthManager>(FindObjectsInactive.Include) == null)
        {
            var manager = go.AddComponent<EnvironmentDepthManager>();
            manager.OcclusionShadersMode = OcclusionShadersMode.SoftOcclusion;
            manager.RemoveHands = true; // tracked hands are drawn; don't let the real ones cut holes in the scene
        }
        go.AddComponent<DepthOcclusion>();
        go.SetActive(true);

        var converted = new Dictionary<Material, Material>();
        foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null || !IsConvertible(m))
                {
                    continue;
                }
                if (!converted.TryGetValue(m, out Material occluded))
                {
                    occluded = Convert(m, shader);
                    converted[m] = occluded;
                }
                materials[i] = occluded;
                changed = true;
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    private void Update()
    {
        // Every frame: the room loads after startup and may reload after a rescan.
        Shader.SetGlobalFloat(FloorHeightId, RoomColliders.TryGetFloorHeight(out float floor) ? floor : NoFloor);
    }

    private static bool IsConvertible(Material m)
    {
        if (System.Array.IndexOf(ConvertibleShaders, m.shader.name) < 0)
        {
            return false;
        }
        // Transparent materials keep their own blending.
        return !m.HasProperty("_Surface") || m.GetFloat("_Surface") == 0f;
    }

    private static Material Convert(Material source, Shader shader)
    {
        var m = new Material(shader) { name = source.name + " (Occluded)" };
        if (source.HasProperty("_BaseMap"))
        {
            m.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            m.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
            m.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
        }
        CopyColor(source, m, "_BaseColor");
        CopyFloat(source, m, "_Smoothness");
        CopyFloat(source, m, "_Metallic");
        CopyFloat(source, m, "_Cull");
        m.renderQueue = source.renderQueue;
        return m;
    }

    private static void CopyColor(Material from, Material to, string property)
    {
        if (from.HasProperty(property))
        {
            to.SetColor(property, from.GetColor(property));
        }
    }

    private static void CopyFloat(Material from, Material to, string property)
    {
        if (from.HasProperty(property))
        {
            to.SetFloat(property, from.GetFloat(property));
        }
    }
}
