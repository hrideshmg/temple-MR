using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// Makes the Meta XR Simulator work out of the box on this machine (AMD Radeon 840M iGPU).
/// See SIMULATOR_SETUP.md in the project root for the full story. In short:
///  1. The Editor must run on Vulkan (Unity Hub launches this project with -force-vulkan).
///  2. The simulator must be activated per Editor process (this script does it on load).
///  3. The Synthetic Environment Server (the "passthrough room") must be running BEFORE
///     Play so it can claim GPU memory first; otherwise it crashes with Vulkan OOM on the
///     iGPU's small VRAM carve-out. This script starts it on load and stops it on quit.
/// Toggle everything via Meta > Meta XR Simulator > Auto-activate on load.
/// </summary>
[InitializeOnLoad]
public static class MetaXRSimulatorAutoActivate
{
    private const string PrefKey = "TempleMR.AutoActivateXRSim";
    private const string RoomPrefKey = "TempleMR.XRSimRoom";
    private const string MenuRoot = "Meta/Meta XR Simulator/";
    private const string ToggleMenu = MenuRoot + "Auto-activate on load";
    private const string ActivateMenu = MenuRoot + "Activate";
    private const string SesProcessName = "synth_env_server";
    private static readonly string[] Rooms = { "LivingRoom", "Bedroom", "GameRoom" };

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    private static string Room
    {
        get => EditorPrefs.GetString(RoomPrefKey, Rooms[0]);
        set => EditorPrefs.SetString(RoomPrefKey, value);
    }

    static MetaXRSimulatorAutoActivate()
    {
        EditorApplication.quitting += StopSyntheticEnvironment;

        // Only once per Editor process, not on every domain reload.
        if (SessionState.GetBool("TempleMR.XRSimAutoActivated", false))
        {
            return;
        }
        SessionState.SetBool("TempleMR.XRSimAutoActivated", true);

        EditorApplication.delayCall += () =>
        {
            if (!Enabled)
            {
                Debug.Log("[MetaXRSimulator] Auto-activate on load is OFF (Meta > Meta XR Simulator).");
                return;
            }

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
            {
                Debug.LogWarning(
                    $"[MetaXRSimulator] Editor is on {SystemInfo.graphicsDeviceType}, not Vulkan. " +
                    "The simulator will show a solid colour on this AMD GPU. Open the project from Unity Hub " +
                    "(its launch args include -force-vulkan).");
            }

            if (EditorApplication.ExecuteMenuItem(ActivateMenu))
            {
                Debug.Log("[MetaXRSimulator] Activated for this Editor session.");
            }

            StartSyntheticEnvironment(Room);
        };
    }

    // ---- Synthetic Environment Server -------------------------------------------------

    private static string SesExePath()
    {
        // The SDK sets this on activation: <install>\meta_openxr_simulator.json
        var runtimeJson = Environment.GetEnvironmentVariable("XR_SELECTED_RUNTIME_JSON");
        if (!string.IsNullOrEmpty(runtimeJson))
        {
            var path = Path.Combine(Path.GetDirectoryName(runtimeJson) ?? "", "synthetic_env_server", SesProcessName + ".exe");
            if (File.Exists(path))
            {
                return path;
            }
        }
        // Fallback when activation didn't set the env var: newest installed simulator.
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MetaXRSimulator");
        if (!Directory.Exists(root))
        {
            return null;
        }
        var versions = Directory.GetDirectories(root, "v*");
        Array.Sort(versions);
        for (var i = versions.Length - 1; i >= 0; i--)
        {
            var path = Path.Combine(versions[i], "synthetic_env_server", SesProcessName + ".exe");
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    private static bool SesRunning() => Process.GetProcessesByName(SesProcessName).Length > 0;

    private static void StartSyntheticEnvironment(string room)
    {
        if (SesRunning())
        {
            return;
        }
        var exe = SesExePath();
        if (exe == null)
        {
            Debug.LogWarning("[MetaXRSimulator] Synthetic Environment Server not found; passthrough will show the fallback pattern.");
            return;
        }
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, room)
            {
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
            });
            if (p != null)
            {
                SessionState.SetInt("TempleMR.SesPid", p.Id);
                Debug.Log($"[MetaXRSimulator] Started Synthetic Environment Server ({room}).");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MetaXRSimulator] Could not start Synthetic Environment Server: {e.Message}");
        }
    }

    private static void StopSyntheticEnvironment()
    {
        foreach (var p in Process.GetProcessesByName(SesProcessName))
        {
            try { p.Kill(); } catch { /* already gone */ }
        }
    }

    // ---- Menu -----------------------------------------------------------------------------

    [MenuItem(ToggleMenu, priority = 1000)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Debug.Log($"[MetaXRSimulator] Auto-activate on load: {(Enabled ? "ON" : "OFF")}");
    }

    [MenuItem(ToggleMenu, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(ToggleMenu, Enabled);
        return true;
    }

    [MenuItem(MenuRoot + "Synthetic Environment/Start (current room)", priority = 1010)]
    private static void MenuStartSes() => StartSyntheticEnvironment(Room);

    [MenuItem(MenuRoot + "Synthetic Environment/Stop", priority = 1011)]
    private static void MenuStopSes() => StopSyntheticEnvironment();

    [MenuItem(MenuRoot + "Synthetic Environment/Room: Living Room", priority = 1020)]
    private static void RoomLiving() => SetRoom(Rooms[0]);

    [MenuItem(MenuRoot + "Synthetic Environment/Room: Bedroom", priority = 1021)]
    private static void RoomBedroom() => SetRoom(Rooms[1]);

    [MenuItem(MenuRoot + "Synthetic Environment/Room: Game Room", priority = 1022)]
    private static void RoomGame() => SetRoom(Rooms[2]);

    [MenuItem(MenuRoot + "Synthetic Environment/Room: Living Room", true)]
    private static bool RoomLivingValidate() { Menu.SetChecked(MenuRoot + "Synthetic Environment/Room: Living Room", Room == Rooms[0]); return true; }

    [MenuItem(MenuRoot + "Synthetic Environment/Room: Bedroom", true)]
    private static bool RoomBedroomValidate() { Menu.SetChecked(MenuRoot + "Synthetic Environment/Room: Bedroom", Room == Rooms[1]); return true; }

    [MenuItem(MenuRoot + "Synthetic Environment/Room: Game Room", true)]
    private static bool RoomGameValidate() { Menu.SetChecked(MenuRoot + "Synthetic Environment/Room: Game Room", Room == Rooms[2]); return true; }

    private static void SetRoom(string room)
    {
        Room = room;
        StopSyntheticEnvironment();
        StartSyntheticEnvironment(room);
    }
}
