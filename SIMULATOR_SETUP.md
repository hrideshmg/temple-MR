# Meta XR Simulator on this laptop (AMD Radeon 840M iGPU)

Meta's simulator frontend is officially "not supported on AMD" (Meta, Mar 2026). On this machine
it showed only a solid colour / black and triggered GPU TDR resets. Root cause (from the
simulator's own logs): the new WinUI frontend (`MetaXRSimulator.exe`) reads Unity's frames through
a cross-process Vulkan external-memory import that the AMD driver mis-handles
(`vkAllocateMemory -13`, "Client not responsive", single-texel uniform fills).

## The working setup (already applied — nothing to do)

| Piece | What | Where |
|---|---|---|
| Editor on **Vulkan** | Unity 6.5 auto-picks D3D12 for the Editor, so the project is launched with `-force-vulkan` | Unity Hub → project "⋮" → *Command line arguments* (stored in `%APPDATA%\UnityHub\projectsInfo.json`) |
| Simulator config | `disable_interop: true`, `use_offscreen_mode: false` (render in the runtime's own in-process window, bypassing the broken frontend readback), `ses_texture_format: "jpg"`, `ses_passthrough_resolution: 512x512`, `disable_config_cache: true`, `log_level: verbose` | `C:\Program Files\MetaXRSimulator\v205.0\config\sim_core_configuration.json` (original kept as `.bak`) |
| Auto-activate + room | Activates the simulator every Editor launch and starts the Synthetic Environment Server (Living Room) *before* Play so it claims VRAM first | `Assets/Editor/MetaXRSimulatorAutoActivate.cs` |
| OpenXR render mode | Multi-pass for the Standalone (Editor) profile; Android untouched | Project Settings → XR Plug-in Management → OpenXR |

## Day-to-day

1. Open the project from **Unity Hub** (title bar must say `<Vulkan>`).
2. Press **Play**. A window titled **"Meta XR Simulator"** appears *owned by the Unity process* — that is the
   one that renders your scene (temple, hands, menu, living-room passthrough, ~30 fps).
   The separate WinUI "Meta XR Simulator" app will sit on its "ready for your application /
   runtime disabled" page — ignore it; it can't render on this GPU.
3. Controls are the simulator's usual ones (WASD / mouse look, hands follow head, bracket keys to
   cycle devices). First time, tick **Do Not Show Again** on the tips popup.

## Switching to a real Quest (Link / Air Link)

Meta → Meta XR Simulator → **Auto-activate on load** (untick), then Meta → Meta XR Simulator →
**Deactivate**. Re-tick to go back to the simulator. The simulator's synthetic room can be
started/stopped or changed under Meta → Meta XR Simulator → **Synthetic Environment**.

## If it breaks again

- Solid colour in the in-process window → Editor isn't on Vulkan (check title bar / Hub launch args).
- Black passthrough (no room) → the Synthetic Environment Server crashed with Vulkan OOM
  (`%LOCALAPPDATA%Low\Meta Platforms, Inc_\SyntheticEnvironmentServer\Player.log`). Start it from the
  menu before pressing Play, or close other GPU-heavy apps. The 840M's VRAM carve-out is only
  ~460 MB; AMD Adrenalin → *Variable Graphics Memory* (raise it) helps if available.
- A simulator update may overwrite `sim_core_configuration.json` — re-apply the keys above.
- Simulator logs: `%APPDATA%\MetaXRSimulator\Logs\<session>\OpenXRRuntime-*.txt` and `MetaXRSimulator-*.txt`.

## Notes

- The Windows Standalone graphics API list is now Vulkan → D3D12 → D3D11 (needed for the Editor;
  only affects Windows builds, not Android).
- Camera clear settings (SolidColor, alpha 0) and the scene are otherwise as before.
