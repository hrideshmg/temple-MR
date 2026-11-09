# KoviLens project documentation

Reference documentation for the KoviLens (a.k.a. Templ-MR / TestHands) mixed-reality temple app for Meta Quest 3 / 3S. For setup, read [HANDOVER.md](HANDOVER.md) first.

*Describes `main` as of 2026-11-09 (commit `1eebf08`). Line numbers are approximate; search for the method name.*

## Contents
1. [Overview](#1-overview)
2. [Tech stack](#2-tech-stack)
3. [Repository layout](#3-repository-layout)
4. [Scene structure](#4-scene-structure)
5. [Architecture](#5-architecture)
6. [Systems in detail](#6-systems-in-detail)
   - 6.1 Temple placement and home pose
   - 6.2 Control panel UI
   - 6.3 Demolition and rebuild
   - 6.4 Room awareness
   - 6.5 Depth occlusion
   - 6.6 Story mode
   - 6.7 Rebuild puzzle
7. [Multiplayer](#7-multiplayer)
8. [Configuration reference](#8-configuration-reference)
9. [Build and deploy](#9-build-and-deploy)
10. [Debugging and diagnostics](#10-debugging-and-diagnostics)
11. [Extending the project](#11-extending-the-project)
12. [Glossary](#12-glossary)

## 1. Overview

The user sees their real room through passthrough with a scale model of Moovar Kovil (Kodumbalur, Tamil Nadu, early Chola, 9th century CE) standing in front of them. Interaction is hands-only: direct grab and finger poke. Hand rays and distance grab were removed on purpose (commit `cddd330`).

Features:
- Manipulate the temple with one or two hands: move, rotate, scale (0.2× to 5×).
- Reset it to its home pose, or Set position to make the current pose home.
- Demolish it: it trembles, then breaks bottom-first into its 16 architectural parts (platform, base, four walls, cornice, storeys, dome, finial, Nandi and the door guardians), which land on the real floor and furniture. Every piece can be grabbed and thrown. A switch in the Inspector brings back the older random rubble (~40 Voronoi chunks).
- Rebuild it: put the parts back one by one in building order, as a puzzle that explains each part as it goes in, or press Rebuild to fly them all back at once.
- Story mode: an 88.9 s narration with captions, room dimming and a warm "sunrise" light, a collapse and rebuild at the right lines, a tour that lights up and labels four parts of the temple, and a golden "restore" ring.
- Occlusion: real objects (furniture, people) hide the virtual temple behind them.
- Colocated multiplayer (in progress): headsets in the same room share one temple, its demolition and the story.

## 2. Tech stack

| | |
|---|---|
| Engine | Unity 6000.5.5f1, URP 17.5.0 (`Mobile_RPAsset` for Quest) |
| Target | Android (Quest 3 / 3S; manifest also lists Quest 2 / Pro), min SDK 32, Horizon OS SDK min 60 / target 205 |
| XR | Unity OpenXR 1.17.1, Unity Meta OpenXR 2.5.1, Meta XR SDK (all) 207.0.1: Core (OVRCameraRig, passthrough), Interaction SDK (ISDK), MR Utility Kit (MRUK), Depth API |
| Networking | Netcode for GameObjects 2.13.3, Unity Transport (UDP, direct LAN), Meta Colocation Discovery, MRUK space sharing; Multiplayer Play Mode 2.0.2 for Editor testing |
| UI | uGUI + TextMeshPro, world-space canvases built in code |
| Input | Input System 1.19 (only used for Editor keyboard shortcuts) |
| Other | `com.unity.pipeline` 0.7.0-exp.1 (Unity CLI editor automation), AI Navigation, Timeline, Visual Scripting (installed, unused by project code) |

## 3. Repository layout

```
TestHands/
├── Assets/
│   ├── Scenes/SampleScene.unity        the only scene
│   ├── Scripts/                        all project code (~7,800 lines C#)
│   │   ├── TempleResetter.cs           start placement, home pose, reset
│   │   ├── TempleDemolition.cs         fracture, collapse, rubble, rebuild
│   │   ├── RoomColliders.cs            MRUK room scan → physics colliders (self-spawning)
│   │   ├── DepthOcclusion.cs           Depth API occlusion setup (self-spawning)
│   │   ├── StoryAudioController.cs     narration playback
│   │   ├── Story/                      story staging
│   │   │   ├── StorySequence.cs        timeline: lighting, captions, cues
│   │   │   ├── TempleAnatomy.cs        finds temple parts; outlines, scan line, labels
│   │   │   ├── StoryCard.cs            floating world-space text cards
│   │   │   ├── StorySounds.cs          procedurally synthesised SFX
│   │   │   └── GlowOutline.cs          glowing line helper (story tour, puzzle slots)
│   │   ├── Puzzle/
│   │   │   ├── TempleParts.cs          the 16 parts: names, texts, build order, triangle sorting
│   │   │   └── TemplePuzzle.cs         put the parts back (added at runtime)
│   │   ├── UI/
│   │   │   ├── TempleControlPanel.cs   the menu (built in code)
│   │   │   ├── PokeButton.cs           press-on-touch world-space button
│   │   │   ├── PanelGrabBar.cs         grab handle to move the panel
│   │   │   └── UISpriteFactory.cs      runtime-generated rounded rects and icons
│   │   ├── Networking/
│   │   │   ├── NetworkSession.cs       host / join / leave, discovery
│   │   │   ├── SharedSpace.cs          room sharing and alignment
│   │   │   ├── NetworkedTemple.cs      temple ownership, home pose
│   │   │   ├── NetworkedDemolition.cs  shared demolition, rubble pose streaming
│   │   │   └── NetworkedStory.cs       shared narration clock
│   │   └── Editor/DepthOcclusionSetup.cs  enables OpenXR Meta Session + Occlusion features
│   ├── Resources/
│   │   ├── Shaders/OccludedLit.shader  URP Lit replacement with depth occlusion
│   │   ├── Shaders/ColliderDebug.shader  translucent grid for the collider view
│   │   └── (Meta SDK settings assets)
│   ├── Audio/Resources/MuvarStory.mp3  narration (loaded by name)
│   ├── ThirdTempleModel/               temple OBJ (Temple_3D_Final_02.obj) + textures
│   ├── Settings/                       URP assets, volume profile, Build Profiles
│   ├── Plugins/Android/AndroidManifest.xml
│   ├── XR/, Oculus/, InteractionSDK/   SDK configuration
│   └── DefaultNetworkPrefabs.asset
├── Packages/manifest.json
├── ProjectSettings/
└── docs/                               this documentation
```

Git-ignored: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Build/`, `*.apk`, `*.csproj`, Burst debug folders.

## 4. Scene structure

`Assets/Scenes/SampleScene.unity` contains:

| Object | What's on it |
|---|---|
| `[BuildingBlock] Camera Rig` | `OVRCameraRig`, `OVRManager`; `TrackingSpace` → `CenterEyeAnchor` (= `Camera.main`), hand and controller anchors |
| `[BuildingBlock] Passthrough` | `OVRPassthroughLayer` (dimmed by the story) |
| `ISDK_HandGrabInteraction` | Interaction SDK hand interactors (grab + poke; rays disabled) |
| Temple (model instance of `Temple_3D_Final_02`) | `Grabbable`, `GrabFreeTransformer` (scale 0.2–5×), `HandGrabInteractable`, `GrabInteractable`, `Rigidbody`, `NetworkObject`, `NetworkTransform`, `TempleResetter`, `TempleDemolition`, `NetworkedTemple`, `NetworkedDemolition` |
| `StoryAudio` | `AudioSource`, `StoryAudioController`, `StorySequence`, `NetworkObject`, `NetworkedStory` |
| `TempleControlPanel` | `TempleControlPanel` (builds its canvas at runtime) |
| `NetworkManager` | `NetworkManager` (scene management on), `UnityTransport`, `NetworkSession`, `SharedSpace` |
| `Directional Light`, `Directional Light (1)`, `Global Volume`, `EventSystem` | lighting / post / UI events |

Created at runtime, not in the scene: `Room Colliders` (MRUK + `EffectMesh`), `Depth Occlusion` (`EnvironmentDepthManager`), the rubble pieces, `TemplePuzzle` (added to the Temple) with its slot outlines and cards, the panel canvas, story cards and `TempleAnatomy` overlays.

## 5. Architecture

```
                 ┌────────────────────────┐
                 │   TempleControlPanel   │  buttons (PokeButton)
                 └───────────┬────────────┘
      in a session ──────────┼────────── offline
      ┌──────────────────────┼──────────────────────────┐
      ▼                      ▼                          ▼
NetworkedTemple      NetworkedDemolition         NetworkedStory
 (RPC → host)          (RPC → host)               (RPC → host)
      │                      │                          │
      ▼                      ▼                          ▼
TempleResetter  ◄──── TempleDemolition ◄──────── StoryAudioController
 home pose,            fracture, collapse,          ▲   playback time
 reset                 rebuild, pieces              │
                             ▲                StorySequence ──► TempleAnatomy, StoryCard,
                             │ colliders            │            StorySounds, passthrough,
                       RoomColliders (MRUK)         └─ collapse/rebuild (host only)
                             │ floor height
                       DepthOcclusion → OccludedLit.shader

NetworkSession (host/join, discovery) ──► SharedSpace (room share + rig alignment)
```

Design rules that run through the code:
1. Single-player is the base case. Each `Networked*` component is a thin wrapper. Without a session the panel calls the plain component directly (`TempleControlPanel` actions section, ~line 405), and the wrappers do nothing.
2. Host-authoritative state, local simulation. Requests from any player go to the host via `[Rpc(SendTo.Server)]`; the host changes `NetworkVariable`s or broadcasts; every headset runs the visuals locally.
3. Determinism saves bandwidth. The fracture is seeded, so piece *i* is the same chunk on every headset and only poses need to be sent.
4. Time-keyed story. The story reads the audio's playback position each frame rather than running timers, so pause, seek and network resync all just work.
5. No prefabs / art for UI. Panels, cards and icons are built and drawn in code.

## 6. Systems in detail

### 6.1 Temple placement and home pose (`TempleResetter`)
- On start, waits for head tracking (up to 3 s, then 0.2 s), then moves the temple straight ahead of the user so its near side is 0.5 m (`_startGap`) from the eyes, turned to the user's facing, keeping its height. That pose becomes home.
- `SetHomeToCurrent()`: "Set temple position" button. Disabled while demolished.
- `ResetTemple()`: "Reset temple" button. Rebuilds first if demolished, zeroes velocity, restores position/rotation/scale.
- `SetHome(...)`: used by `NetworkedTemple` to apply the host's home.
- Bounds are computed from enabled renderers only, because hidden fracture pieces live under the temple.

### 6.2 Control panel UI (`TempleControlPanel`, `PokeButton`, `PanelGrabBar`, `UISpriteFactory`)
Layout (240 × 484 canvas units at 0.0008 m/unit ≈ 19 × 39 cm):
- Header: title, `</>` developer button, collapse button.
- Story: Play/Pause, progress bar, time.
- Temple: Reset temple, Set temple position, Demolish / Rebuild temple.
- Session: status line, Host/Leave, Join, plus an error box that hangs under the panel and grows to fit multi-line network errors.
- Developer popup (`</>`): Rescan room, Show colliders.
- Collapses to a glowing "Menu" orb.

Placement: the panel docks to the body like a lectern, 0.24 m to the side (right by default), 0.32 m forward, 0.40 m below the eyes. It follows a lazily-tracked body yaw: only a turn held past 55° for 0.8 s swings it round, and never while the user looks within 35° of the panel. Eye height is low-pass filtered. It always tilts to face the eyes.

Moving it: grab the bar above the panel (hand grab, pinch, controller grip or ray). Where it's dropped becomes the new dock offset, clamped to 0.2–1.5 m from the eyes.

Button states (`UpdateStatus`): Set position and Reset are disabled while demolished; Demolish and Reset while another player holds the temple; Rescan while a guest is using the host's room; Host/Join when multiplayer is unavailable.

`PokeButton` fires on the press itself, not on release. The uGUI rule (release over the same element without dragging) made finger pokes fail when the finger slid. `SetEnabled(bool)` greys a button out. Styles: Primary, Secondary, Ghost, Orb, Danger.

`UISpriteFactory` rasterises rounded rectangles, shadows and line icons (Play, Pause, Home, Pin, Collapse, Menu, Crack, …) from signed-distance shapes once per session.

### 6.3 Demolition and rebuild (`TempleDemolition`)
Pre-fracture runs once at startup (`BuildRoutine`), off the main thread, in one of two styles chosen by Fracture Style. **The model import must have Read/Write enabled** for both.

Architectural (default) breaks the temple into 16 named parts (`TempleParts`), one piece per part, for the rebuild puzzle (§6.7). Only meshes active in the hierarchy are used; hidden meshes in the model are left out. `FractureArchitectural` sorts every triangle into a part, since the model has no part names:
- Carved figures (Nandi, the door guardians and others) are recognised by their material and texture names and go whole into their part.
- Everything else is cut by height at the fractions in Part Cuts (`TempleParts.Cuts`): platform 0.06, base 0.12, walls 0.335, cornice 0.445, hara 0.62, tala 0.8, griva 0.86, shikhara 0.945, with the stupi above. Walls and figures are also split by which side of the shrine they are on.
- The entrance is the side where the base reaches furthest past the walls (the front platform). Left and right are as seen standing at the entrance.

The parts, bottom up: upapitha (platform), mandapa base (front hall platform), adhisthana (moulded base), Nandi, four bhitti walls (front, left, right, rear), two dvarapalas (door guardians), prastara (cornice), hara (first-storey parapet), tala (second storey), griva (neck), shikhara (dome) and stupi (finial). The niche deities are flat panels in the model, so they stay with their walls. Pieces are named after their part, e.g. "Adhisthana (The moulded base)".

Rubble sorts each mesh under the temple by size:
- Split into Voronoi chunks if ≥ 2,500 triangles, or larger than 12 % of the temple and ≥ 200 triangles.
- Tiny (< 1.2 % of the temple): rides on the nearest chunk, no rigid body.
- Whole otherwise: pillars, kalashas, statues fall as single pieces.

Fracturing runs as pure data on a worker thread (`Task.Run` → `Fracture`): 40 seeds by default, RNG seed 7, seeds placed by best-candidate sampling over triangle centroids (12 candidates per seed) for even sizes; each triangle goes to its nearest seed; chunk meshes are rebuilt with remapped indices and pivoted on their own centre.

Each piece: box collider (min thickness 15 % of its largest extent), `Rigidbody` kinematic until released, gravity applied manually (× 0.75), continuous speculative collisions, damping 0.05 / 0.2, mass = box volume × 2,500 kg/m³ clamped to 0.02–50 kg, double-sided material copies with occlusion off. Each piece has ISDK `Grabbable` + `HandGrabInteractable`, kinematic while held, throw on release.

Collapse (`Demolish` → `CollapseRoutine`):
1. Tremble for 0.45 s, amplitude ramping to 0.6 % of the height.
2. Swap the intact temple for the pieces in place.
3. Release bottom-first over 0.3 s by normalised height. Each piece gets a radial velocity blended from an outward kick (0.35 heights/s) at the base to an inward sag (0.12 heights/s) at the top, randomised, plus random tumble.
4. Explosive mode (default): box pairs that overlapped in the intact temple are allowed to collide, so pieces shove each other and burst across the room. Off: those pairs ignore each other and the temple folds into a heap.

A piece being held isn't released until it's let go.

Ground: with a room scan, a 30 m safety ground sits at the real floor height; without one, an invisible ground at the temple's base, 6 temple-widths across.

Rebuild (`Rebuild`): animates the pieces back along an arc, base first (`Reassemble Time`, 2.4 s; 0 snaps), then swaps in the intact temple at its pre-demolition pose.

Public API: `Demolish()`, `Rebuild()`, `Toggle()`, `IsDemolished`, `IsReassembling`, `IsReady` (fracture finished), `Driven` (guest mode: pieces are kinematic and follow streamed poses), `GetPieceBody(i)`, `ThrowPiece(i, v, w)`. For the puzzle: `IsArchitectural`, `GetPiecePart(i)`, `GetPieceBounds(i)`, `GetPieceHome(i)` (the piece's slot in world space), `WorldHeight`, `IsPieceLocked(i)` and `LockPiece(i)`, which fixes a piece in its slot (kinematic, not grabbable). `Demolish()` and `Rebuild()` unlock every piece.

### 6.4 Room awareness (`RoomColliders`)
- Self-spawns after scene load (`[RuntimeInitializeOnLoadMethod]`). Reuses an MRUK already in the scene if there is one.
- Loads the stored Space Setup scan; opens Space Setup only if there is none (`RescanOnLaunch = false`). Space Setup can't run over Link, so the Editor uses the stored scan.
- Gives invisible colliders (via MRUK `EffectMesh`) to floor, ceiling, walls, labelled furniture (table, couch, storage, bed, screen, lamp, plant, other) and the global mesh (catches unlabelled chairs and clutter on Quest 3 / 3S).
- MRUK world lock is on: without it, any shift of the tracking origin (recenter, lost tracking) left the colliders offset from the real room.
- `TryGetFloorHeight(out float)` is used by occlusion and demolition.
- Debug: `Rescan()` and `CollidersVisible` (floor/ceiling blue, walls green, furniture orange, room mesh pink), both on the developer popup.

### 6.5 Depth occlusion (`DepthOcclusion`, `OccludedLit.shader`)
- Self-spawns only if `EnvironmentDepthManager.IsSupported` (Quest 3 / 3S on device; not the simulator, not Link without spatial data).
- Adds an `EnvironmentDepthManager` with soft occlusion and `RemoveHands = true` (the tracked virtual hands are drawn; real hands shouldn't cut holes).
- Replaces every opaque URP Lit / Simple Lit material in the scene with a copy using `TestHands/Occluded Lit` (`Resources/Shaders/OccludedLit.shader`). Transparent materials are left alone.
- The shader's depth bias is 0.06 × camera distance normally, and 0.3 within 0.15 m above the scanned floor so things lying on the floor aren't swallowed by it. The floor height is pushed as a global shader float every frame. Per-material `_NoOcclusion` turns it off (used by rubble, which fringed and vanished into the floor otherwise).
- `Editor/DepthOcclusionSetup.cs` turns on the OpenXR features Meta Quest: Session and Meta Quest: Occlusion for Android and Standalone, once per machine. Re-run from Tools → TestHands → Enable Depth Occlusion Features.

### 6.6 Story mode (`StoryAudioController`, `StorySequence`, `TempleAnatomy`, `StoryCard`, `StorySounds`)
Playback (`StoryAudioController`): loads `Resources/MuvarStory` if no clip is assigned. `TogglePlayPause()` resumes from the pause point. `PlayFrom(t)` / `PauseAt(t)` exist for network sync. Exposes `PlaybackTime`, `Length`, `IsPlaying`.

Sequence (`StorySequence`). Everything is computed from `PlaybackTime`:

| Narration time | What happens |
|---|---|
| 0–2 s | Room (passthrough) dims by 75 %, scene lights go down |
| 2.5–9.5 s | Title card "MOOVAR KOVIL — Kodumbalur, Tamil Nadu · Early Chola, 9th century CE" |
| 3.5–8 s | Warm "sunrise" light rises on the temple |
| 25.8 s | Rumble; temple collapses ("neglect, invasion, disasters") |
| 36.3 s | Rubble flies back; temple stands again (shimmer sound) |
| 42.6–~58 s | Part tour: adhisthana (base), bhitti (wall), devakoshthas (wall niches), vimana (tower). Each gets a glowing outline, a scan line sweeping up and a label card with a leader line, plus a bell |
| 68.5 s | Labels fade |
| 74.2 s | Golden ring rises up the temple ("revive and restore"), 4 s |
| 85–88.6 s | Room brightens back to normal |
| throughout | Captions under the temple, like subtitles |

All times, the title, the captions and the part cues are serialized fields on `StoryAudio`: adjust them in the Inspector. Lighting (lights, ambient probe, passthrough) is captured before the story first changes it and restored after.

Collapse and rebuild change shared state, so only the host (or a single offline player) triggers them; guests follow via `NetworkedDemolition`. Everything else runs locally on every headset off the synced clock.

`TempleAnatomy` finds the parts without any part names in the model: it bins the temple's vertices by height (60 bins) into a footprint profile; each part is a height range (`PartKind.Band`) or the niches in the middle of each wall except the entrance (`PartKind.Niches`). The entrance is the side where the base extends furthest past the wall (the front platform). Changing the model means re-checking these ranges.

`StoryCard`: floating rounded card in the panel's style (dark, gold accent), height fitted to the text, `Alpha` to fade, `Place()` to position and face the eyes.

`StorySounds`: rumble, rising shimmer and bell are synthesised in code once at startup; no audio assets.

### 6.7 Rebuild puzzle (`TemplePuzzle`, `TempleParts`, `GlowOutline`)
`TempleDemolition` adds `TemplePuzzle` to the temple at start in the architectural style, so it isn't in the scene. The puzzle runs while the temple lies demolished and isn't flying back.

- Picking up a piece outlines its slot (the piece's bounding box at its home pose): gold if it can go in now, grey if a part it rests on is still missing. While nothing is held, the slots that can be filled next pulse faintly.
- A piece let go within Snap Distance of its slot (0.12 of the temple's height, clamped to 0.05–0.15 m) and within Snap Angle (40°) snaps in. Round parts (hara, tala, griva, shikhara, stupi) only need the right tilt. The piece glides in over 0.3 s and locks, a chime plays, and a card with the part's name and a short description stays up for 7 s.
- Parts go back in building order (`TempleParts.Info.Needs`): the mandapa base and adhisthana on the upapitha, Nandi on the mandapa base, the walls on the adhisthana, the dvarapalas after the front wall, the prastara after all four walls, then each storey on the one below. A part let go near its slot too early shows "Not yet: this rests on the …" for 3.5 s.
- Once the last part is in, the intact temple swaps back in (through the network toggle in a session) and every headset shows a "Temple restored" card with a shimmer.
- Meanwhile the panel's Rebuild button reads "Rebuild (n/N placed)". Pressing it still flies every piece back at once.

`GlowOutline` draws the glowing lines for both the puzzle slots and the story's part tour.

## 7. Multiplayer

Status: in progress. Host/join works between two headsets; room alignment is off by ~4 m vertically so the guest can't see the shared temple. The likely cause is `SharedSpace.CurrentRoom()` falling back to `MRUK.Instance.Rooms[0]` and sharing a stored room the host isn't standing in.

### 7.1 Sessions (`NetworkSession`)
- Topology: one headset hosts (NGO server + client) and is authoritative; others join directly over the LAN with Unity Transport on UDP 7777. No relay, no internet needed.
- Discovery: the host advertises `"TempleMR/1 <ip> <port>"` with Meta Colocation Discovery (Bluetooth). `Join()` listens for up to 15 s, then falls back to `_joinAddress` (default `127.0.0.1`). The advertisement's UUID doubles as the colocation group used for room sharing.
- States: `Offline → Hosting` / `Offline → Searching → Joining → Joined`. `LastError` explains failures: "refused" (answered but rejected, usually a different build) vs. "no answer" (network can't reach host).
- Editor / dev builds: H host, J join, L leave.
- **NGO scene management must stay on** (`EnableSceneManagement: 1` on the NetworkManager). With it off, NGO destroys in-scene NetworkObjects on joining clients and the guest gets no temple.

### 7.2 Shared space (`SharedSpace`)
1. Host waits up to 20 s for its room, then `ShareRoomAsync` with the colocation group.
2. Host sends each guest (named message `TempleMR.SharedSpace`, reliable) the group UUID, room UUID and the host's floor-anchor pose.
3. Guest calls `MRUK.LoadSceneFromSharedRooms(group, (room, floorPose))`; MRUK moves the guest's camera rig so the floors coincide. From then on world coordinates match, and the guest's room colliders are the host's scan.
4. Guests can't rescan while using the host's room.

Only works on headsets (needs a colocation group). States: None, Sharing, Shared (host); Aligning, Aligned (guest); Failed. Host waits up to 20 s (`_roomWaitTimeout`) for its room.

### 7.3 Temple (`NetworkedTemple`)
- Owner-authoritative `NetworkTransform`. Grabbing sends `GrabRpc` to the host, which grants ownership unless someone else is holding it; otherwise `DenyRpc` cancels the local grab. Others' grab interactables are disabled while someone holds it.
- Home pose is three host-owned `NetworkVariable`s. Set position and Reset go through the host (`RequestSetHome`, `RequestReset`); Reset is refused while the temple is held.
- Ownership returns to the host if the holder disconnects.

### 7.4 Demolition (`NetworkedDemolition`)
- Demolish/Rebuild → `ToggleRpc` on the host → all headsets run the collapse locally.
- Only the host simulates. Guest pieces are `Driven` (kinematic) and smooth toward host poses (rate 18).
- Host streams world poses of pieces that moved (> 1 mm or > 0.5°) at 15 Hz, unreliable (`TempleMR.RubblePoses`; 2-byte index + Vector3 + Quaternion, ≤ 32 pieces per message), plus a reliable full snapshot every 2 s and on request (late join).
- Guests can grab and throw rubble: the held pose streams to the host, and on release the measured hand velocity, so the host continues the throw.
- Puzzle: the host decides every placement. A guest's release reaches the host as a throw, and `ThrowPieceRpc` calls `TemplePuzzle.TrySnap`. The host shares which pieces are in place as a 64-bit mask (`NetworkVariable<ulong>`), which guests apply with `ApplyPlacedMask`. Hold and throw messages for locked pieces are ignored. Guests show the "not yet" hint themselves.
- Known limits: extra tremble jitter on guests; piece count could differ across float hardware (logged); simultaneous grabs of one piece aren't arbitrated.

### 7.5 Story (`NetworkedStory`)
- Host holds `playing`, `startTime` (server time at clip position 0) and `pausedAt`.
- Play/Pause from anyone → host. Each headset seeks its local clip when it drifts > 0.25 s from the shared clock, so late joiners hear the right line. `StorySequence` follows automatically because it's keyed to playback time.

## 8. Configuration reference

Most behaviour is tunable in the Inspector (all fields have tooltips). The important ones:

| Component (object) | Field | Default | Effect |
|---|---|---|---|
| `TempleResetter` (Temple) | Place In Front Of Player / Start Gap | on / 0.5 m | start placement |
| `TempleDemolition` (Temple) | Fracture Style | Architectural | named parts + puzzle, or random rubble |
| | Part Cuts | see §6.3 | height cuts between the stacked parts |
| | Seed count / Seed | 40 / 7 | rubble style: number of chunks / fracture pattern |
| | Explosive | on | burst across room vs. fold into heap |
| | Tremble time / amplitude | 0.45 s / 0.6 % | pre-collapse shake |
| | Gravity scale | 0.75 | slower, heavier fall |
| | Reassemble Time / Lift | 2.4 s / 0.12 | rebuild animation (0 s snaps) and arc height |
| `StorySequence` (StoryAudio) | Room Dim, timeline fields, Title, Parts, Captions | see §6.6 | story staging |
| `NetworkSession` (NetworkManager) | Port / Join Address / Search Timeout / Join Timeout | 7777 / 127.0.0.1 / 15 s / 10 s | session |
| `NetworkedStory` (StoryAudio) | Max Drift | 0.25 s | resync threshold |
| `NetworkedDemolition` (Temple) | Send Rate / Snapshot Interval / Smoothing | 15 Hz / 2 s / 18 | rubble streaming |
| `TempleControlPanel` | Side, docking offsets, turn thresholds | right, see §6.2 | panel placement |
| `TemplePuzzle` (Temple, added at runtime; add it by hand to edit) | Snap Distance / limits / Snap Angle | 0.12 of height / 0.05–0.15 m / 40° | how close a piece must be to snap in |
| | Settle Time / Info Time | 0.3 s / 7 s | snap animation / how long the part card stays |
| `RoomColliders` (code const) | `RescanOnLaunch` | false | open Space Setup every launch |

Model import (`ThirdTempleModel/Temple_3D_Final_02.obj`): **Read/Write must be enabled** for fracturing and anatomy.

## 9. Build and deploy

- Build profiles (`Assets/Settings/Build Profiles/`): Meta Quest Dev (development build) and Meta Quest (release). Both Android, product name KoviLens.
- Scripting backend IL2CPP, ARM64 (Quest requirement).
- Package id: `com.UnityTechnologies.com.unity.template.urpblank` (template default, not yet changed).
- `AndroidManifest.xml`: launcher + VR category, focus aware, `BOUNDARYLESS_APP` required (no guardian in passthrough), supported devices quest/quest2/questpro/quest3/quest3s.
- Install: Build And Run from the profile window, or `adb install -r <apk>`.
- **Every headset in a session must run the same build**; otherwise the join is refused.
- For performance measurements use the release profile with the `[MP]` diagnostics removed.

## 10. Debugging and diagnostics

| Tool | How |
|---|---|
| Device logs | `adb logcat -s Unity`; multiplayer lines are prefixed `[MP]` |
| Room colliders | Panel `</>` → Show colliders |
| Re-scan room | Panel `</>` → Rescan room (or Quest Settings → Space Setup) |
| Multiplayer without two headsets | Multiplayer Play Mode virtual players + H/J/L keys; join falls back to `127.0.0.1` |
| Performance | OVR Metrics Tool on the headset |
| Meta Immersive Debugger | settings in `Resources/ImmersiveDebuggerSettings.asset` |
| Editor automation | `com.unity.pipeline` provides the `unity` CLI to drive the open Editor (set `MSYS_NO_PATHCONV=1` in Git Bash) |

Common problems:
- No occlusion → not a Quest 3/3S, or running in Editor/Simulator, or OpenXR Occlusion feature off (run the Tools menu item).
- Rubble falls through the floor → no room scan; run Space Setup.
- Colliders offset from the room → tracking origin shifted; world lock should correct it, else Rescan.
- Guest sees no temple → check scene management is on and the `[MP] Joined … Spawned here:` line lists `Temple` and `StoryAudio`; if they're spawned, it's the alignment bug.
- Editor crashes over Link → Meta Link runtime, not project code (see HANDOVER §3.4).

## 11. Extending the project

- Adding a panel button: in `TempleControlPanel.BuildPanel`, call `Button(name, style, icon, label, x, y, w, h, action, fontSize)`, add it to `_panelButtons`, grow `Height` if needed, and set its enabled state in `UpdateStatus`. New icons go in `UISpriteFactory.Icon` as signed-distance shapes.
- Adding a networked action: put the single-player logic in the plain component; add a `Request…()` method on the `Networked*` wrapper that calls the plain method when there's no session and an `[Rpc(SendTo.Server)]` otherwise; have the panel call the wrapper.
- Changing story timing or text: Inspector on `StoryAudio`. For a new narration, replace `MuvarStory.mp3` (or set the clip) and retime every cue and caption.
- Swapping the temple model: needs Read/Write on import, the same components on the root, and re-checking `TempleAnatomy` part ranges, the `TempleParts` height cuts and figure material names, and the rubble fracture thresholds. The fracture seed keeps rubble identical across devices only for the same model.
- New networked object: add `NetworkObject`, keep it in-scene (scene management is on) or register its prefab in `DefaultNetworkPrefabs.asset`.

## 12. Glossary

| Term | Meaning |
|---|---|
| Moovar Kovil / Muvar Koil | "Temple of the three"; the 9th-century Chola temple at Kodumbalur the model depicts |
| Adhisthana | Moulded base of the temple |
| Bhitti | Wall section |
| Upapitha | Plain platform the whole temple stands on |
| Mandapa | Hall in front of the shrine |
| Prastara | Cornice above the walls |
| Hara | Parapet running round a storey |
| Tala | Storey of the tower |
| Griva | Recessed neck carrying the dome |
| Shikhara | Crowning dome |
| Stupi | Finial on top of the dome |
| Dvarapala | Door guardian flanking the entrance |
| Nandi | Shiva's bull, facing the shrine |
| Devakoshtha | Wall niche holding a deity |
| Vimana | Tower over the sanctum |
| Kalasha | Pot-shaped finial |
| ISDK | Meta Interaction SDK (hands, grab, poke) |
| MRUK | Meta MR Utility Kit (room scan, anchors, space sharing, world lock) |
| Space Setup | Quest system flow that scans the room |
| NGO | Unity Netcode for GameObjects |
| Colocation | Several headsets in the same physical room sharing coordinates |
| Host / guest | The headset running the session / the others |
| Driven | `TempleDemolition` mode where pieces follow streamed poses instead of simulating |
| KovilLens (HoloLens 2) | The earlier HoloLens 2 app this project ports and redesigns |
