using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;

/// <summary>
/// Turns on the Unity OpenXR Meta features the Depth API needs ("Meta Quest: Session" and
/// "Meta Quest: Occlusion") for Android and Windows (Link), once per machine for this project.
/// Features are matched by type name so this compiles before the package has finished importing.
/// Run again from Tools > TestHands > Enable Depth Occlusion Features.
/// </summary>
[InitializeOnLoad]
internal static class DepthOcclusionSetup
{
    private static readonly string[] RequiredFeatures = { "ARSessionFeature", "AROcclusionFeature" };
    private static readonly string DoneKey = "TestHands.DepthOcclusionSetup." + Application.dataPath;

    static DepthOcclusionSetup()
    {
        if (!EditorPrefs.GetBool(DoneKey))
        {
            EditorApplication.delayCall += () =>
            {
                if (EnableFeatures())
                {
                    EditorPrefs.SetBool(DoneKey, true);
                }
            };
        }
    }

    [MenuItem("Tools/TestHands/Enable Depth Occlusion Features")]
    private static void EnableFromMenu()
    {
        if (!EnableFeatures())
        {
            Debug.LogWarning("DepthOcclusionSetup: Unity OpenXR Meta features not found. Is com.unity.xr.meta-openxr installed?");
        }
    }

    /// <returns>True if every required feature was found (and is now enabled) on both platforms.</returns>
    private static bool EnableFeatures()
    {
        bool allFound = true;
        foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
        {
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                allFound = false;
                continue;
            }
            foreach (string typeName in RequiredFeatures)
            {
                bool found = false;
                foreach (var feature in settings.GetFeatures())
                {
                    if (feature.GetType().Name != typeName)
                    {
                        continue;
                    }
                    found = true;
                    if (!feature.enabled)
                    {
                        feature.enabled = true;
                        EditorUtility.SetDirty(feature);
                        Debug.Log($"DepthOcclusionSetup: enabled '{feature.name}' for {group}.");
                    }
                }
                allFound &= found;
            }
        }
        if (allFound)
        {
            AssetDatabase.SaveAssets();
        }
        return allFound;
    }
}
