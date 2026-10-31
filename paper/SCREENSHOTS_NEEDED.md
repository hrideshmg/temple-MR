# SCREENSHOTS_NEEDED

Placeholders in `main.tex` (Fig. 4). Capture on the Quest 3 with the system capture (Meta button > Camera >
Take photo), or `adb shell screencap`, which includes passthrough. Save as PNG in `paper/figs/` with the
file name given, then replace the matching `\shot{...}{...}` in `main.tex` with
`\includegraphics[width=\linewidth]{figs/<file>}`.

| Fig. | File name | What to show | Notes |
|---|---|---|---|
| 4(a) | `panel.png` | The control panel docked at the user's side, expanded, with the developer popup open, temple visible behind it. | Look slightly down and to the right so both the panel and temple fit. |
| 4(b) | `rubble.png` | Rubble after a collapse, resting on the real floor and on a table. | Shows the room colliders at work. Don't try to show rubble hidden behind furniture: rubble is never occluded by design. |
| (new, optional) | `occlusion.png` | The intact temple partly hidden behind a real chair or person. | Only shot that shows Depth API occlusion; add a sub-figure 4(c) if space allows. |

Optional, if space allows (not referenced in the paper yet):

| File name | What to show |
|---|---|
| `collapse_sequence.png` | 3 frames of a collapse: tremble, base spreading, top falling in. |
| `scale.png` | Temple scaled up to near life size in the room. |
| `colliders.png` | Developer view "Show colliders" on, with the colour-coded room colliders. |
| `two_users.png` | Third-person photo of two people in headsets around the same spot, once multi-user alignment works. |
| `kovillens_vs_templmr.png` | Same viewpoint on HoloLens 2 (KovilLens) and Quest 3 (Templ-MR), to show the field-of-view difference. Only if a HoloLens 2 is still available. |
