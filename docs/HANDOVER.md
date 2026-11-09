# KoviLens developer handover

Read this first. It covers what the project is and how to get it running. For the details of how each system works, see [PROJECT_DOCUMENTATION.md](PROJECT_DOCUMENTATION.md).

*Written 2026-11-09.*

## 1. What this is, in one paragraph

KoviLens is a mixed-reality app for Meta Quest 3 / 3S that puts a 3D model of Moovar Kovil (an early Chola temple at Kodumbalur, Tamil Nadu, 9th century CE) on a real table or floor in front of you, seen through passthrough. You can pick it up, move, rotate and scale it with your hands. You can demolish it into its architectural parts (base, walls, storeys, dome and so on), which land on your real floor and furniture, throw the pieces around, and then rebuild it by putting each part back in its place, or have it fly back together in one go. A narrated story mode dims the room, lights the temple, collapses and restores it, and lights up and labels the parts of the temple as the narrator names them. Several headsets in the same room can join one session and share the temple; this part is built but not yet working end to end.

It is a Quest port and redesign of an earlier HoloLens 2 app, also called KovilLens (separate project, `C:\Users\hridesh\Desktop\Unity\Kovilens`).

> On naming: the repo folder is `TestHands`, the GitHub repo is `temple-MR`, code comments say "Templ-MR" / "TempleMR", and the app shown on the headset is KoviLens. They are all the same project. The Android package id is still Unity's template default (`com.UnityTechnologies.com.unity.template.urpblank`); changing it means a fresh install on every headset.

## 2. Repository

All work is on `main`. Remote: `https://github.com/hrideshmg/temple-MR.git`.

Commit messages are plain imperative sentences describing the change (e.g. "Add grab bar to move the control panel").

## 3. Getting it running

### 3.1 You need
- Unity 6000.5.5f1 (Unity 6.5) with Android Build Support (SDK, NDK, OpenJDK). Other 6.x versions will try to upgrade the project; don't.
- A Meta Quest 3 or 3S in developer mode, with a Space Setup room scan done in the room you test in (Settings → Physical Space → Space Setup). Quest 2 / Pro run the app but have no Depth API, so no occlusion.
- USB-C cable, and `adb` (comes with Unity's Android module) for logs.

All packages come from `Packages/manifest.json` and download on first open. The main ones: Meta XR SDK (all) 207.0.1, URP 17.5, Unity OpenXR 1.17.1 + Meta OpenXR 2.5.1, Netcode for GameObjects 2.13.3, Input System.

### 3.2 Open and build
1. Clone, open the folder in Unity Hub with 6000.5.5f1. First import takes a while (the temple model is ~300k triangles).
2. Open `Assets/Scenes/SampleScene.unity`. It is the only scene.
3. File → Build Profiles: two profiles exist in `Assets/Settings/Build Profiles/`:
   - Meta Quest Dev: development build, for day-to-day work (enables the H/J/L keyboard shortcuts in development builds, attachable profiler).
   - Meta Quest: release build. Use this for demos and for any performance measurement.
4. Plug in the headset, pick it as the run device, Build And Run. Or build an APK and install with `adb install -r build.apk`.
5. On first launch, accept the spatial data permission prompt (needed for the room scan). If the headset has no room scan, Space Setup opens by itself.

### 3.3 Testing in the Editor
- Quest Link (USB or Air Link): press Play with the headset on Link. Depth occlusion and room scan sharing don't work over Link; the stored room scan does load.
- Without a headset: Play mode runs, but the camera sits at the origin. Move `[BuildingBlock] Camera Rig` to see the temple.
- Multiplayer in the Editor: Multiplayer Play Mode 2.0 is installed (open it from the Window menu, under Multiplayer) to run a second virtual player. In the Editor and dev builds, keys H / J / L host, join (falls back to `127.0.0.1`) and leave.

### 3.4 Known setup problems on the original dev laptop
The original developer's laptop has an AMD Radeon 840M integrated GPU, which caused two problems you may hit too:
- Meta XR Simulator shows a black / solid-colour window on AMD GPUs. Fix: Editor on Vulkan (`-force-vulkan` in Unity Hub's per-project command-line args), and in `C:\Program Files\MetaXRSimulator\v205.0\config\sim_core_configuration.json` set `"disable_interop": true` and `"use_offscreen_mode": false`. On an NVIDIA machine you shouldn't need any of this.
- Editor crashes during Play over Link are in Meta's Link runtime (`LibOVRRTImpl64_1` / `OVRPlugin` in the crash stack), not in project code. Use `-force-d3d11` for Link sessions, a USB 3 cable/port, and a lower Link refresh rate / resolution if the stream stutters. Simulator sessions still need Vulkan, so you'll swap the Hub argument depending on which you're using.

### 3.5 Logs
```bash
adb logcat -s Unity
```
Multiplayer lines are tagged `[MP]`. Room/tracking events come from `RoomColliders`. Unity Editor crash logs are under `%LOCALAPPDATA%\Temp\Unity\Editor\Crashes\`.

## 4. Where to start reading code

In this order:
1. `Assets/Scripts/UI/TempleControlPanel.cs`: every button and what it calls. The quickest map of the features.
2. `Assets/Scripts/TempleResetter.cs` (small): placement and home pose.
3. `Assets/Scripts/TempleDemolition.cs`: the largest and most interesting script.
4. `Assets/Scripts/Story/StorySequence.cs`: the story timeline.
5. `Assets/Scripts/Puzzle/TemplePuzzle.cs` and `TempleParts.cs`: the rebuild puzzle and how the model is cut into parts.
6. `Assets/Scripts/Networking/`: start with `NetworkSession.cs`, then `SharedSpace.cs` (where the alignment bug most likely is).

Conventions the existing code follows, worth keeping:
- UI is built in code, no prefabs or imported art (`UISpriteFactory` draws the sprites). Change the panel in `TempleControlPanel.Build*`.
- Self-spawning systems: `RoomColliders` and `DepthOcclusion` create themselves with `[RuntimeInitializeOnLoadMethod]`; you won't find them in the scene.
- Single-player first: every `Networked*` component does nothing without a session, and the panel calls the plain single-player method when offline. Keep that working.
- Tunables are serialized fields with tooltips: change behaviour in the Inspector before changing code.
- Commit messages are plain imperative sentences describing the change.
