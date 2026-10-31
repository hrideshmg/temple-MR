# FACTS: KovilLens (HoloLens 2) vs Templ-MR (Quest 3)

Every fact below is taken from code, project files, git history or the session logs, with a reference.
Paths starting `../Kovilens/` are in the HoloLens 2 project (`C:\Users\hridesh\Desktop\Unity\Kovilens`).
Other paths are in this repo (`TestHands`). Line numbers are for the working tree on 2026-10-31.

The Templ-MR working tree has uncommitted changes (diagnostic logging, an error box in the panel, and
NGO scene management switched on). Facts about those are marked *(uncommitted)*.

---

## 1. Build environments

| | KovilLens (HoloLens 2) | Templ-MR (Quest 3) |
|---|---|---|
| Unity | 2022.3.19f1 (`git show HEAD:ProjectSettings/ProjectVersion.txt` in `../Kovilens`, line 1). The working copy has been opened in 6000.5.5f1 since, uncommitted. | 6000.5.5f1 (`ProjectSettings/ProjectVersion.txt:1`) |
| XR runtime layer | Unity OpenXR 1.10.0 + Microsoft Mixed Reality OpenXR 1.10.0 (`../Kovilens` HEAD `Packages/manifest.json:3,12`) | Unity OpenXR 1.17.1, Unity Meta OpenXR 2.5.1 (`Packages/manifest.json:17-18`) |
| Interaction toolkit | MRTK 2.8.3 foundation, standard assets, examples, extensions (HEAD `manifest.json:4,5,44,45`) | Meta XR SDK "all" 207.0.1 (`Packages/manifest.json:3`), which pulls Core, Interaction SDK and MR Utility Kit 207.0.1 (`Packages/packages-lock.json:8,26`) |
| Render pipeline | Built-in (no SRP package in HEAD manifest) | URP 17.5.0 (`Packages/manifest.json:12`) |
| Networking | none | Netcode for GameObjects 2.13.3, Multiplayer Play Mode 2.0.2 (`Packages/manifest.json:9-10`) |
| Target | UWP package `kovilAR`, product name "Muvar Koil" (HEAD `ProjectSettings/ProjectSettings.asset:16,682`) | Android, min SDK 32 (`ProjectSettings/ProjectSettings.asset:182`); product name still `TestHands` (`:16`) |
| Project scripts | 2 files, 429 lines: `Assets/main.cs` (366), `Assets/explode.cs` (63, standalone test of the explosion) | 15 files, 4,942 lines under `Assets/Scripts` (`wc -l`) |
| History | 4 commits, 2023-11-21 to 2024-02-23 (+ merge 2026-02-06) | 23 commits, 2026-07-30 to 2026-10-30 (`git log`) |

Shared assets (identical files, same MD5):
- Temple model `Assets/ThirdTempleModel/Temple_3D_Final_02.obj` in both projects: 151,874 faces / ~298,651 triangles after fan triangulation, 155,927 vertices (counted with awk over `f`/`v` lines).
- Narration `MuvarStory.mp3`: `../Kovilens/Assets/MuvarStory.mp3` = `Assets/Audio/Resources/MuvarStory.mp3`, 88.86 s (`ffprobe`).

## 2. KovilLens (HoloLens 2): how the key systems work

All in `../Kovilens/Assets/main.cs`.

- **Baseline / Anchor.** `Anchor()` stores the main object's local position, rotation and scale (`main.cs:207-217`); called at start (`:40`). This is the paper's "Anchor" button.
- **Reset ("Divine Restore").** `Reset()` restores the stored pose on both the exploding object and the main object, destroys the duplicate rubble and re-enables the intact temple (`main.cs:181-205`).
- **Destruction.** `Explode(bool applyGravity)` (`main.cs:60-72`) plays a cannon sound (`:68`), waits 0.5 s (`:77`), then:
  - instantiates a duplicate of the temple and hides the original (`:89-99`);
  - adds a `Rigidbody` and `BoxCollider` to every **direct child** of the duplicate, i.e. the parts as modelled, with no runtime fracturing (`:117-130`);
  - applies `AddExplosionForce(explosionForce * modifier, randomPoint, explosionRadius)` with `explosionForce = 50`, `explosionRadius = 10` (`:23-24`), the point random on a sphere around the temple and the modifier one of 7 random values in [0,1] (`:79`, `:135-142`, `GenerateRandomModifiers :52-59`);
  - adds MRTK `NearInteractionGrabbable` + `ObjectManipulator` to each part so it can be handled (`:144-150`).
  - **Shatter** = `ExplodeF` (no gravity); after 1 s `MakeKinetic` freezes all parts in the air (`:154-157`, `:162-173`). **Collapse** = `ExplodeT` (gravity on), parts fall under PhysX.
  - The paper's description of impact forces on contact and gradual kinetic-energy damping is **not in the code**; collisions and damping are Unity/PhysX defaults.
- **Storytelling mode.** `Play()` (`main.cs:227-259`) disables manipulation (`SetStoryMode :272-280`), turns off the direct light, plays the narration, and schedules coroutines: light on at 7 s, blast (`Explode(false)`) at 25 s, reset at 36 s, tooltips Adhisthana 43 s, Bhitti 45 s, Devakoshtas 50 s, Vimana 54 s, end of story at 90 s. (The code comment at `:230-237` lists slightly different times, 41/44/50/54.) `StopThePlay()` stops everything and restores interaction (`:348-365`).
- **Manipulation.** MRTK `ObjectManipulator` + `NearInteractionGrabbable` + `BoxCollider` on the temple (`main.cs:276-278`, `286-288`). Scaling, rotation and translation come from MRTK, configured in the scene.
- **Multi-user / networking:** none. No networking package or code.
- **Room awareness in code:** none in project scripts (no spatial-mesh colliders or occlusion code). The paper describes HoloLens 2 spatial mapping and occlusion as device capabilities; whatever MRTK's spatial awareness profile did is in scene/profile settings, not verified here `[VERIFY]`.

## 3. Templ-MR (Quest 3): how the key systems work

### 3.1 Scene and interaction
- Scene components (counted from `Assets/Scenes/SampleScene.unity` by script GUID): Meta Building Blocks Camera Rig + Passthrough (`OVRCameraRig`, `OVRManager`, `OVRPassthroughLayer`), `ISDK_HandGrabInteraction`, temple with `Grabbable`, `GrabFreeTransformer`, `HandGrabInteractable`, `GrabInteractable`, `NetworkObject`, `NetworkTransform`, plus the project scripts.
- Temple grab: `Grabbable` with one transformer for one- and two-hand grabs, unlimited grab points, kinematic while held, throw on release (`SampleScene.unity:1252-1262`). `GrabFreeTransformer` scale limited to 0.2x-5x of the start scale on all axes, position and rotation free (`SampleScene.unity:1168-1216`).
- Hand rays and distance grab were removed on purpose: only poke and direct grab remain (commit `cddd330`, "Place temple within reach on start and disable hand rays"; user request in session `56712c6f`).
- Start placement: the temple is moved in front of the user with a 0.5 m gap from the eyes to its near side, turned to the user's facing, height kept; that pose becomes "home" (`Assets/Scripts/TempleResetter.cs:20-24`, `77-105`).
- Home / reset: `SetHomeToCurrent()` ("Set temple position") and `ResetTemple()` ("Reset temple"), which rebuilds the temple first if demolished (`TempleResetter.cs:139-176`). Equivalent to KovilLens Anchor / Divine Restore.

### 3.2 Control panel (replaces the MRTK dashboard)
`Assets/Scripts/UI/TempleControlPanel.cs`
- Built entirely in code at startup, no prefabs (`:21-23`, `Build :201-225`).
- Size: 240 x 484 canvas units at 0.0008 m/unit, i.e. about 19 cm x 39 cm (`:92`, `:99-100`).
- Docks beside the body, not the head: 0.24 m to the side, 0.32 m forward, 0.40 m below the eyes (`:59-71`). A "lazy body yaw" follows the head only after it stays turned more than 55 deg for 0.8 s, and never while the user looks within 35 deg of the panel (`:73-83`, `UpdatePlacement :528-607`). Eye height is low-pass filtered (`:553`).
- Grab bar above the panel moves it; where it is dropped becomes the new dock offset, clamped to 0.2-1.5 m from the eyes (`:111-113`, `:555-573`; `Assets/Scripts/UI/PanelGrabBar.cs:9-18`).
- Collapses to a "Menu" orb (`BuildHandle :389-401`).
- Sections: Story (play/pause, progress bar, time), Temple (Reset, Set position, Demolish/Rebuild), Session (status, Host/Leave, Join) (`:244-283`), and a developer popup with Rescan room and Show colliders (`BuildDevPopup :341-366`).
- Buttons are disabled when an action makes no sense: Set position and Reset while demolished, Demolish/Reset while another player holds the temple, Rescan while using the host's room (`UpdateStatus :694-718`).
- `PokeButton` fires on the press itself, not on release, because the uGUI release rule made finger pokes fail (`Assets/Scripts/UI/PokeButton.cs:8-14`).

### 3.3 Narration
- `StoryAudioController` plays/pauses the 88.9 s clip and resumes from the pause point (`Assets/Scripts/StoryAudioController.cs:68-92`); `PlayFrom`/`PauseAt` exist for network sync (`:94-121`).
- **No tooltips and no choreographed animation.** Templ-MR's story is audio only; the KovilLens schedule (light, blast at 25 s, reset, four labels) was not ported. Manipulation is not locked during the story.

### 3.4 Destruction: seeded Voronoi pre-fracture and staged collapse
`Assets/Scripts/TempleDemolition.cs`
- Pre-fracture once at startup (`BuildRoutine :402-518`). Every mesh is classified by size (`:457-478`):
  - split into chunks if it has >= 2,500 triangles, or is larger than 12% of the temple's size and has >= 200 triangles (`:37-43`, `:466`);
  - "tiny" if smaller than 1.2% of the temple's size: rides on the nearest chunk with no rigid body (`:45-47`, `:712-738`);
  - otherwise falls whole (pillars, kalashas, statues) (`:16-17`, `:707-710`).
- Fracture runs on a worker thread (`Task.Run`, `:495`) as pure data (`Fracture :521-670`):
  - 40 seeds by default (`:31`), fixed RNG seed 7 so the same temple breaks the same way every time (`:35`, `:556`);
  - seeds chosen by best-candidate sampling over triangle centroids, 12 candidates per seed, for evenly sized chunks (`:555-573`);
  - each triangle goes to its nearest seed (a Voronoi partition of the surface, `:575-593`); chunk vertex buffers are rebuilt with index remapping, pivoted on their own centre (`:595-667`).
  - Model import must have Read/Write on (`:415-420`).
- Each piece: box collider with a minimum thickness of 15% of its largest extent (`:780-787`); `Rigidbody` kinematic until release, gravity applied manually, max depenetration 0.15 m/s, linear damping 0.05, angular 0.2, sleep threshold 0.02, continuous speculative collision, mass = box volume x 2500 kg/m^3 clamped to 0.02-50 kg (`:789-798`). Rubble materials are double-sided copies (`DoubleSided :833-860`).
- Every piece is grabbable and throwable with either hand (ISDK `Grabbable` + `HandGrabInteractable`, kinematic while held, throw on release) (`:800-808`).
- Box pairs that overlap in the intact temple are recorded (`FindOverlappingPairs :743-767`). Non-explosive mode ignores collisions between them so the temple folds into a heap; explosive mode (default on, `:57-59`) lets the overlap push pieces apart so rubble bursts across the room (`:274-278`).
- Collapse choreography (`CollapseRoutine :243-319`): tremble for 0.45 s with amplitude ramping to 0.6% of height (`:61-67`, `:247-261`); swap intact temple for pieces in place (`:263-273`); release bottom-first over 0.3 s by normalised height (`:69-71`, `:299`); each piece gets a radial velocity blended from an outward kick of 0.35 heights/s at the base to an inward sag of 0.12 heights/s at the top, with random scale 0.6-1.3, small noise, and random tumble 2.5 rad/s (`:73-83`, `:306-313`). Gravity is scaled by 0.75 (`:85-87`, `FixedUpdate :321-335`). A piece being held is not released until let go (`:297-301`).
- Rebuild restores the temple's pre-demolition pose, not where it was when the rubble landed (`Rebuild :191-227`; commit `2dfe220`).
- **Compared with KovilLens:** KovilLens moves the parts as modelled with one random explosion force; Templ-MR cuts the large shells itself, keeps the cut deterministic, and choreographs the fall. KovilLens has a mid-air "Shatter" (freeze) mode; Templ-MR does not.

### 3.5 Room awareness
- `RoomColliders` (`Assets/Scripts/RoomColliders.cs`) self-spawns after scene load (`:122-160`), loads the stored Space Setup scan through MRUK (opening Space Setup only if none, `:243-272`), and gives floor, ceiling, walls, furniture labels and the scanned global mesh invisible colliders via `EffectMesh` (`:33-49`, `:145-152`).
- Rescan on launch is off (`:29`).
- MRUK world lock is on (`:142`). The session logs say it was enabled because the room colliders sometimes shifted by a vertical offset after demolition (session `2c9ada70`, user message; see also session `7ac7c667`).
- Without a room scan, rubble lands on an invisible ground at the temple base 6 temple-widths wide; with one, a 30 m safety ground sits at the real floor height (`TempleDemolition.cs:89-96`, `PlaceGround :341-357`).
- Developer popup: Rescan room (`RoomColliders.Rescan :114-120`) and a colour-coded collider view (floor/ceiling blue, walls green, furniture orange, room mesh light pink, `:57-61`, `:171-202`).

### 3.6 Depth occlusion
- `DepthOcclusion` (`Assets/Scripts/DepthOcclusion.cs`) self-spawns if `EnvironmentDepthManager.IsSupported`, adds a manager with **soft** occlusion and `RemoveHands = true` (`:33-57`), and swaps every opaque URP Lit/Simple Lit material to a custom occlusion-aware shader (`:59-83`, `Convert :102-117`).
- Shader `Assets/Resources/Shaders/OccludedLit.shader`: depth bias 0.06 of camera distance normally, 0.3 within a 0.15 m band above the scanned floor so objects lying on the floor are not swallowed (`:14-17`, `OcclusionBias :91-95`); per-material hard/soft/none (`:18-20`, `:97-103`). Floor height comes from the room scan each frame (`DepthOcclusion.cs:86-90`).
- Rubble materials set `_NoOcclusion` (never occluded) because soft occlusion fringed small pieces and the floor swallowed them (`TempleDemolition.cs:851-854`; user report in session `7ac7c667`).
- An editor script enables the OpenXR "Meta Quest: Session" and "Occlusion" features (`Assets/Scripts/Editor/DepthOcclusionSetup.cs:5-14`).

## 4. Collaboration / multi-user: status

**Status: partly built. Implemented end to end in code; tested on device only in parts; the shared experience does not yet work on two headsets.** It belongs in the paper as work in progress, not as a finished contribution.

### 4.1 What is built
- **Sessions** (`Assets/Scripts/Networking/NetworkSession.cs`): LAN host/join with Unity Transport on UDP port 7777, no relay server (`:9-21`, `:38-40`, `Host :141-156`). Host advertises `"TempleMR/1 <ip> <port>"` over Meta Colocation Discovery (Bluetooth) (`:35-36`, `Advertise :158-183`); Join discovers it for up to 15 s, else falls back to a typed address (`:46-48`, `Join :205-242`, `OnHostDiscovered :254-269`). The advertisement UUID is the colocation group (`:265-267`). Join failures are explained (refused vs no answer) *(uncommitted, `:374-399`)*.
- **Shared space** (`Assets/Scripts/Networking/SharedSpace.cs`): host waits up to 20 s for its room, shares it with the group through MRUK `ShareRoomAsync` (`:106-141`), and sends each guest the group UUID, room UUID and the host's floor-anchor pose as a reliable named message (`SendSpace :167-179`). The guest calls `MRUK.LoadSceneFromSharedRooms(group, (room, floorPose))`, which moves the guest's camera rig so the floors line up (`Align :206-237`). Rescan is disabled while a guest uses the host's room (`TempleControlPanel.cs:713-714`).
- **Temple pose and holding** (`Assets/Scripts/Networking/NetworkedTemple.cs`): owner-authoritative `NetworkTransform`; grabbing asks the host for ownership, the host grants it unless someone else holds it, otherwise sends a deny RPC that cancels the local grab (`:8-21`, `GrabRpc :201-215`, `DenyRpc :236-248`). Others' grab interactables are disabled while one player holds it (`ApplyLock :274-285`). Home pose is three host-owned `NetworkVariable`s; Set position and Reset go through the host; Reset is refused while held (`:28-33`, `:141-199`). Ownership returns to the host when the holder disconnects (`:226-232`).
- **Demolition** (`Assets/Scripts/Networking/NetworkedDemolition.cs`): Demolish/Rebuild go through the host (`ToggleRpc :132-150`). Every headset plays the collapse locally; because the fracture is seeded, piece *i* is the same chunk on every device, so only poses need sending (`:10-14`). Guests' pieces are `Driven` (kinematic, `TempleDemolition.cs:105-109`, `:279-283`). Host streams world-space poses of moved pieces (> 1 mm or > 0.5 deg) at 15 Hz unreliable, plus a full reliable snapshot every 2 s and on request (`:26-36`, `UpdateHost :152-194`, `RequestSnapshotRpc :225-238`). Message: 2-byte index + Vector3 + Quaternion per piece, up to 32 pieces per message (`SendPoses :197-223`). Guests smooth toward targets with rate 18 (`:38-40`, `:289-311`). A guest holding a piece streams its pose to the host and on release sends the measured hand velocity so the host continues the throw (`:240-265`, `:293-306`, `TrackHeldPiece :315-338`).
- **Narration** (`Assets/Scripts/Networking/NetworkedStory.cs`): host keeps `playing`, `startTime` (server time at clip position 0) and `pausedAt` (`:22-24`); Play/Pause from anyone goes through the host (`ToggleRpc :71-85`); each headset seeks its local clip when it drifts more than 0.25 s from the shared clock (`:18-20`, `Update :87-119`), so late joiners start at the right line.
- Without a session every wrapper falls back to the single-player call (`TempleControlPanel.cs:405-439`).

### 4.2 What was tested, and what happened
Source: session transcript `~/.claude/projects/C--Users-hridesh-Desktop-Unity-TestHands/2c9ada70-4862-4aa3-8a66-b606fd3f0794.jsonl` (2026-10-30/31).
- Phase 1 (session host/join) was tested with the Editor as host and one Quest: the Quest showed "joined".
- Phases 2-5 (shared room, temple sync, home pose + story, demolition) were committed untested (commit messages `d1ca748`, `b8ab77f`, `7b3409e`, `43a1eb9`; assistant summary after `43a1eb9`).
- Two-headset runs (serials `2G0YC1ZFBK00S2` host, `2G0YC1ZFB801F5` guest):
  1. First join failed with NGO "max connection attempts" (network-level; host not reachable over the hotspot). A later run connected.
  2. Guest reported "Joined · aligned" but saw no temple and no story. Log: the guest had **no spawned network objects**. Cause: NGO scene management was off, and in that mode NGO destroys in-scene NetworkObjects on a joining client. Fix: `EnableSceneManagement: 1` (`Assets/Scenes/SampleScene.unity:2303`) *(uncommitted)*.
  3. After the fix the guest log showed `Spawned here: Temple#1 StoryAudio#2` and the temple's position matched the host's, and the guest's Play request reached the host. But the guest **still could not see the temple**: MRUK alignment logged a pose delta of about `[-0.26, -3.97, -0.41]` m, moving the guest's camera to y ≈ -2.5 m, so the temple was ~4 m above the guest. Suspected cause: the host shared a room it was not standing in (`CurrentRoom()` falls back to `Rooms[0]`, `SharedSpace.cs:181-189`). Unresolved at the end of the log; diagnostic logging added *(uncommitted)*.
- Not yet observed on two headsets: shared demolition, rubble streaming, guest throws, story heard in sync, pose/ownership handover.
- Known limits listed when Phase 5 was written: extra tremble jitter on guests; piece-count mismatch possible across different float hardware (logged, `NetworkedDemolition.cs:349-353`); two players grabbing the same rubble piece is not arbitrated.

## 5. Feature map (KovilLens -> Templ-MR)

| KovilLens feature (paper / code) | Templ-MR | Status |
|---|---|---|
| Hand manipulation: move, rotate, scale (MRTK ObjectManipulator, `main.cs:276-278`) | ISDK Grabbable + GrabFreeTransformer, scale 0.2-5x (`SampleScene.unity:1168-1262`) | Ported |
| Far/ray manipulation (MRTK far interaction) `[VERIFY in scene]` | Hand rays removed by design (`cddd330`) | Dropped on purpose |
| Dashboard menu (MRTK buttons) | Code-built body-docked poke panel (`TempleControlPanel.cs`) | Redesigned |
| Anchor | Set temple position (`TempleResetter.cs:139-142`) | Ported |
| Divine Restore (reset) | Reset temple (`TempleResetter.cs:156-176`) | Ported; also rebuilds |
| Storytelling: narration | Play/pause/resume with progress bar (`StoryAudioController.cs`) | Ported |
| Storytelling: timed light, blast, reset, 4 tooltips (`main.cs:227-259`) | none | Not ported |
| Manipulation locked during story (`main.cs:272-280`) | not locked | Not ported |
| Collapse (gravity) via explosion force on modelled parts (`main.cs:60-158`) | Seeded Voronoi pre-fracture + staged collapse (`TempleDemolition.cs`) | Re-implemented |
| Shatter (pieces freeze mid-air, `main.cs:154-173`) | none | Not ported |
| Individual parts grabbable after destruction (`main.cs:144-150`) | Grabbable and throwable pieces (`TempleDemolition.cs:800-808`) | Ported, adds throwing |
| Rubble lands on ground | Lands on the scanned room's floor and furniture (`RoomColliders.cs`) | Extended |
| Occlusion by real objects (device capability per paper) | Depth API soft occlusion, floor-aware bias (`DepthOcclusion.cs`, `OccludedLit.shader`) | Re-implemented |
| Single user | Colocated multi-user | Partly built (Section 4) |

## 6. Numbers we do not have
No frame-rate, frame-time, battery, thermal, network-latency, bandwidth, alignment-error or user-study data exists for Templ-MR. Rubble piece count at runtime is not logged anywhere we found. See `EVAL_PLAN.md`.
