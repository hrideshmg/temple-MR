# EVAL_PLAN: how to fill every `[TODO: measure]` in main.tex

Nothing below has been measured yet. Each item says what to record, how, and where it goes in the paper
(Table III unless stated). Use one release build (not the `Meta Quest Dev` profile) with the `[MP]`
diagnostic logging removed, installed on every headset from the same APK.

## 0. Common set-up
- Headset: Meta Quest 3, battery above 80%, not charging, fan/thermal state cold (off for 10 min before).
- Record the OS version, Meta XR SDK (207.0.1), refresh rate, and eye-texture scale. Fill the
  `[VERIFY: refresh rate used]` marker in Section VI with the refresh rate.
- Room: one room with a completed Space Setup scan that includes the floor, walls and at least one table.
  Write down its size and lighting.
- Tools: OVR Metrics Tool (on-headset overlay + CSV export) and `adb logcat -s Unity`.

## 1. Performance (single headset)
Conditions, 60 s each, 5 runs each, temple at default scale ("home" pose):
1. Idle: temple intact, panel open, user looking at the temple.
2. Story: narration playing.
3. Collapse: press "Demolish temple" at t = 5 s, record the following 55 s.
4. Settled rubble: 60 s starting 20 s after a collapse, rubble resting.
5. Rubble in hand: user picks up and throws pieces for 60 s.

Record with OVR Metrics Tool: average FPS, 1% low FPS, GPU and CPU frame time, stale frames, CPU/GPU level.
Report mean and SD over the 5 runs per condition.

**Collapse frame-time spike:** the maximum frame time in the 5 s after Demolish (condition 3), from the CSV.

**Battery and thermals:** one 20 min session cycling the five conditions. Record battery % at start and end
(`adb shell dumpsys battery`) and whether the headset showed a thermal warning.

## 2. Fracture
Add two log lines (not in the code yet): one before `Task.Run` and one after `BuildPieces` in
`Assets/Scripts/TempleDemolition.cs` `BuildRoutine` (around lines 495 and 506), printing
`Time.realtimeSinceStartup` and `_pieces.Count`.
- **Fracture build time:** difference between the two timestamps, 5 cold launches, mean and SD.
  Also fills `[TODO: measure fracture build time on Quest 3]` in Section V-B.
- **Rubble piece count:** `_pieces.Count`. Check it is the same on both headsets (it should be; the
  fracture is seeded). If it differs, that is a finding for Section VII.

## 3. Multi-user (two headsets) - only after alignment works
Prerequisite: fix the ~4 m vertical alignment offset (FACTS.md Section 4.2). Until then these cannot be measured.
- **Alignment error:** put a small physical marker (tape cross) on the floor and one on a table. On the
  host, place a virtual pin by touching each marker; the guest records where it sees the pin relative to
  the marker (or log the pin's world position on both headsets and compare). 3 sessions x 2 markers,
  report mean and max error in cm.
- **Rubble stream bandwidth:** log bytes sent by `NetworkedDemolition.SendPoses` per second on the host
  (sum of `writer.Length`), during condition 3 and 4 above. Report peak and mean B/s, and compare with the
  bound in the paper (about 15 x 30 x N B/s for N moving pieces).
- **Narration drift:** on both headsets log `StoryAudioController.PlaybackTime` and
  `NetworkManager.ServerTime.Time - startTime` once per second for a full 88.9 s playback. Report the
  difference between the two headsets' playback positions (mean, max). Also do one late join at ~40 s.
- **Shared demolition check (pass/fail list):** collapse seen on both; rubble rests at the same real
  spots (eyeballed against the room); guest throw continues on the host; rebuild on both; reset refused
  while the other player holds the temple.

## 4. Usability pilot
- Participants: target 8-12 (fill `[TODO: measure: N participants]`), none of whom built the app.
  Record age range, prior XR experience, and whether they have visited a Dravidian temple.
- Ethics: check whether the institute requires approval for a pilot of this kind before running it
  `[VERIFY]`.
- Task script (single user, ~10 min): play the story to the end; move, rotate and scale the temple;
  "Set temple position"; demolish; pick up and throw two pieces; rebuild; reset.
- Questionnaires after the session:
  - SUS (10 items, 1-5) -> SUS score 0-100 (Brooke 1996).
  - SSQ (16 symptoms, 0-3) -> total and sub-scores (Kennedy et al. 1993), before and after the session.
  - The 5 KovilLens Likert items (overall experience, storytelling, realism, shatter/collapse, recommend),
    worded exactly as in KovilLens Table I, so results compare item by item. Note that "shatter" no longer
    exists in Templ-MR; keep the wording but record that only collapse was shown.
  - Two open questions as in KovilLens, plus: "Did seeing the room through the headset's cameras affect
    how real the temple felt?"
- Report: median and IQR per Likert item, SUS mean and SD, SSQ pre/post. With a pilot of this size,
  report descriptive statistics only.

## 5. Feature parity on the headset
Walk through Table II and mark each row on the headset: works / partly / no. Rows marked "Ported" must be
checked by someone other than the developer. Fills `[TODO: measure: on-device parity checklist]`.
