# STYLE NOTES: how the KovilLens paper is written

Source: P. R. Chinthakuntla, U. Gopalakrishnan, N. Ramkumar, B. Hariharan, "KovilLens: A Mixed Reality
Application for Digital Preservation and Exploration of Moovar Kovil," Proc. 11th ICVR, 2025, pp. 283-289,
DOI 10.1109/ICVR66534.2025.11172645 (`paper/KovilLens.pdf`). Notes only; no sentences are reused.

## Format
- IEEE conference template, two columns, 7 pages including references.
- Title in title case; authors in separate IEEE author blocks with department, school, university, city, email.
- Funding footnote on page 1 (a DST grant number).
- Abstract is one paragraph (~200 words), set as "Abstract--". "Index Terms--" list of 8 terms in title case.
- Sections numbered with Roman numerals in small caps: Introduction, Related Work, framework section, application design, experimental design and metrics, results and discussion, conclusion, acknowledgment, references. Subsections lettered A, B, C.
- Acknowledgment thanks the Chancellor (Amma) and names the people who drew, modelled, textured and voiced the temple.

## Voice and wording
- First person plural ("we present", "we digitally reconstruct"). Mostly present tense for the system, past tense for the study.
- Opens broad (heritage destruction, India's temples, Shilpa Shastra, Chola period) and narrows to one site, then to the app.
- Describes features in order of the user's experience: what the user sees and does, then how it is implemented (e.g. "a duplicate of the model is created and the original disabled").
- Implementation detail is mid-level: names Unity concepts (coroutines, rigid bodies, box colliders, MRTK components) but gives no parameter values, no algorithms, no code.
- Feature names are quoted or capitalised as UI labels: 'Anchor', 'Divine Restore', "Shatter", "Collapse", Story Telling Mode, Intuitive Interactions Mode.
- Evaluation is a Likert survey (23 participants) reported as percentages in prose plus a stacked bar figure, then participant quotes.
- Related work is narrative, one or two sentences per cited work, grouped loosely by theme (heritage XR, accessibility of heritage, HoloLens in heritage, immersive learning by the same group, tourism).

What to keep in Templ-MR: the same section rhythm, the same plain first-person register, UI labels in quotes,
features described from the user's side first. What to change: Templ-MR's core is a platform decision and a
re-implementation, so it needs more technical depth (parameters, algorithm steps, message formats) than
KovilLens gave, and an honest gained/lost accounting.

## Figures (kinds used)
- Fig. 1: photograph of the real site.
- Fig. 2: development-framework flow diagram (assets -> Unity + MRTK -> build -> device scanning -> placement), boxes with logos and arrows, single column.
- Fig. 3: feature mind-map (Dashboard / Play-Will Animation / Shatter-Collapse / Intuitive interaction, each with bullet leaves).
- Figs. 4-9: headset captures (tooltips, first-person + onlooker view, shatter vs collapse, menu, life-size temple, reconstruction overlay).
- Fig. 10: stacked horizontal bar chart of Likert responses.
- Captions: "Fig. N." followed by a short noun phrase, under the figure.

For Templ-MR we keep a framework/architecture block diagram (TikZ), add a collaboration sequence diagram and a
fracture pipeline diagram (TikZ), and use placeholders for headset captures.

## Tables
- One table: "TABLE I / EVALUATION QUESTIONS AND RESPONSE TYPES", caption above the table in small caps, centred.
- Plain grid: columns No. | Question | Type; horizontal rules only at the top, under the header and at the bottom (booktabs-like look).
- Templ-MR uses the same look: caption above, `\toprule/\midrule/\bottomrule`, small font.

## Citations
- IEEE numeric style, square brackets, numbered in order of first appearance: "[2]", "[8], [9]", "[17], [18]".
- 21 references; mix of journals (J. Cultural Heritage, Tourism Management, Multimedia Tools and Applications), ACM IMX/JOCCH, IEEE conferences, arXiv, an ASI web page with access date, and several self-citations from the Amrita group (Gopalakrishnan [17], [18], Prabhakaran SurgiColab [19], Murali [20]).
- Names given as initials + surname; titles in sentence case inside quotes; URLs given for web sources.
- Templ-MR follows the same with `IEEEtran.bst`. Vendor specifications are cited as `@misc` with URL and access date.
