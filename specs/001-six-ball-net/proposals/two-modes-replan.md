# Proposal: two game modes (Net Practice and Match Simulation), milestones M7 to M11

Status: **ACCEPTED by the owner 2026-10-26** and merged into spec.md, plan.md (§16–§18, including the single calibration table in §17), tasks.md (T047–T069) and validation.md. This file is the decision record.

Owner additions on approval:
1. M6.5 Test 2, the AC-14 numbers and the merge come before M7; no code changes until then.
2. The handover from physics to the simulated path is seamless: it starts from the exact contact position and exit velocity, with no jump or speed change. Keep the bat sounds; add landing, fielded, caught and boundary sounds. A PlayMode test checks that the visible ball and the scored path match (same landing point, same end position).
3. Played-on uses the same stumps box as the physics.
4. One rolling-resistance value in GroundDefinition, starting at 0.25 g, calibrated from logged hits.
5. The ±60 ms timing window is a setting; the timing offset is recorded in the CSV for every ball.
6. The leave thresholds (0.20 m, 3 m/s) are settings.
7. Decisions D-1 to D-5 accepted as recommended.
8. Every calibration setting is listed in one table in plan.md §17.

Source: the owner's brief "Re-plan the game as two modes" (2026-10-26).

Where the project stands today:
- M1 to M6 are done; AC-06 passed in NetMode (E-031).
- M6.5 (the cricket ground) is on the `m6.5-ground` branch. AC-11 passed (E-033). Not yet run: the AC-12 and AC-13 headset checks (M6.5 Test 2) and the AC-14 performance numbers. The branch is not merged into `main`.

---

## 1. Conflicts with the current docs, code, tests and earlier decisions

| # | Conflict | Where | Recommendation |
| --- | --- | --- | --- |
| C-1 | **The product goal changes.** "Face six deliveries, see the results, play again" becomes two modes with menus, overs, scenarios, confidence ratings and simulated runs. | CLAUDE.md §1, §2, §7; spec.md goal, REQ-011/012 | Explicit owner instructions override CLAUDE.md. Record this in the plan.md decisions log. Rewrite spec.md's goal and scope to the two-mode game. CLAUDE.md itself says a more precise spec governs feature detail, so it doesn't need editing (offer it as an option). |
| C-2 | **CLAUDE.md defers** fielders, realistic fielding, catches, full innings rules and estimated carry. The virtual-field scorer adds simulated fielding, catches, a chase and estimated distances. | CLAUDE.md §2 "Deferred", §10 | Owner override, recorded. Everything is labelled "simulated runs" / "simulated carry". No run-outs, no extras, no LBW. |
| C-3 | **Net runs are retired.** REQ-009 (runs from net zones, PC-01 accepted) and the AC-06 pass contradict "Net Practice scores no runs". | spec.md REQ-009, AC-06, PC-01; validation E-030/E-031 | Amend REQ-009: net zones become a non-scoring "where the ball went" label. Keep E-030/E-031 as historical evidence for finalize-once and carry; mark their run-scoring parts superseded. See §7 for the code and test inventory. |
| C-4 | **NetMode was to be removed.** M6.5 planned to remove NetMode once the virtual-field scorer was done. The new structure makes it permanent. | plan.md §15 "Mode switch"; REQ-021 | Cancel the removal. Amend REQ-021: the start-menu choice selects the environment, the result pipeline and the UI. |
| C-5 | **GroundMode results.** M6.5 decision GD-8 gives "Hit, practice scoring pending". | REQ-021, GD-8, GroundModeAdjudicatorTests | In Match Simulation, pending becomes the virtual-field outcome (M10). Keep pending only as the interim GroundMode result until M10 lands. Bowled / Miss (unhit past z < −1 m) stay as GD-8 defined them. |
| C-6 | **What you see vs what's scored.** The brief says the physical ball is only for visuals after contact. But PhysX flies without drag, bounces off the outfield at 0.45, and rolls with 0.08 g drag (M6.5). The scorer uses drag k = 0.008/m, bounce 0.4/0.7 and roll 0.25 g. You would see the ball cross the rope while the scorer says "2 runs", or see it bounce short of a fielder who "caught" it. | REQ-019, BallRollingDrag, GroundDefinition | **In Match Simulation, after contact the ball follows the simulated path kinematically** (a recorded trajectory played back at render rate), ending in the catcher's hands, at the rope, or at the fielder who intercepts it. What you see is then exactly what is scored. PhysX keeps the ball up to contact (delivery bounce calibration unchanged) and in Net Practice. See C-7 for played-on. |
| C-7 | **Played-on bowled.** With playback (C-6), PhysX no longer moves the ball after contact, so it can't detect the ball rolling back onto the stumps. | brief §5 "physical events" | The simulation tests its path against the stumps box (the same box as the stumps collider, from GroundDefinition) for the first 1.5 s. A hit that reaches the stumps is bowled (played on). It stays deterministic and EditMode-testable. The alternative, keeping PhysX for the first 0.3 s and then handing over, is more complex and gives two sources of truth. |
| C-8 | **Two different rolling resistances.** M6.5's cosmetic rolling drag (0.08 g, `BallRollingDrag`) and the scorer's roll term (μ_roll 0.25) disagree. | GroundDefinition.rollingResistance | One value, in GroundDefinition, used by the scorer (and by the playback). `BallRollingDrag` stays only for Net Practice and any non-playback path. The starting μ_roll is calibrated (CAL-4). 0.25 g stops a 10 m/s roll in 20 m, which is likely too slow-grass for an outfield. |
| C-9 | **Distances.** REQ-010 says "no unmeasured open-field distance is displayed". The scorer's carry is simulated, not measured. | spec.md REQ-010, CLAUDE.md §10 | Amend REQ-010. Net Practice keeps "Measured carry" / "Net hit". Match Simulation shows **"Simulated carry"** and never calls it measured. |
| C-10 | **Session length and loop.** "Exactly six deliveries" (REQ-011/012, AC-07) and the six-result limit (T025) vs overs (Net Practice: 1, 2, 3 or 5) and a chase (6–36 balls, ending early on a win or when wickets run out). | spec.md REQ-011..014, AC-07..09; tasks T025..T028 | Rewrite REQ-011..014 and AC-07..09 for a ball budget per session that may end early, keeping their IDs so the traceability history stays intact. "A wicket does not end the session" now applies to Net Practice only. In Match Simulation, losing the last wicket ends it. |
| C-11 | **Left controller.** Your left controller is faulty. The menu uses the right controller's ray, but a right-hander's bat is also on the right controller, and left-handed batting needs the left one. | REQ-003, AC-03 (left pending) | The ray is active only on menus, from the right controller, while the bat is hidden. The bat-tap fallback (touch a button with the blade) also works. Left-handed play stays available but unvalidated until the controller works. |
| C-12 | **Bat-tap fallback.** The bat proxy collides with nothing (M5 matrix) and the contact resolver is ball-only. | BowlingBuilder collision matrix, plan.md §10 | The fallback is a distance test from the blade box to each button's rectangle. No new colliders and no change to the collision matrix. |
| C-13 | **Writing files.** CLAUDE.md says scores need no file persistence. A per-ball CSV log is a file. | CLAUDE.md §6/§7 | The CSV is a calibration log, not saved scores. On by default in development builds, off in release (GameConfig). Written to `Application.persistentDataPath`, one file per session; pull it with `adb pull`. Nothing is read back. |
| C-14 | **Delivery speeds.** Match difficulty asks for higher speeds and yorkers, but AC-04 bounce consistency is only measured for 60–100 km/h presets, and the absolute bounce error is 2.6–10.1 cm (M6.5 E-032). | REQ-004/005, AC-04 | Hard is capped at 120 km/h until an AC-04 re-run covers 100–120 km/h. Yorkers are lengths of 0.8–1.5 m from the striker's stumps. Every planned delivery goes through the solver before use; an unreachable one is redrawn (never silently changed) and logged. |
| C-15 | **Fielder reach on balls hit behind.** The keeper (180°/12 m) and slip are inside the catch model. The brief's catch window (0.2–2.5 m) with a 0.35 s reaction means a nick at 30 m/s reaches the keeper in about 0.4 s: barely catchable. | FieldConfig, brief §4 | Keep the brief's model and calibrate the keeper/slip reaction separately (CAL-6). The arcade default may need a shorter keeper reaction (0.25 s), or edges to the keeper never become catches. |
| C-16 | **Required rate on the first ball.** Required rate = runs needed ÷ (balls left ÷ 6) is undefined at 0 balls left. | brief Mode 2 | Show the required rate only while balls left > 0. At 0 balls left the session has already ended. |
| C-17 | **Strike.** Odd runs normally change strike. | brief decision 1 | See decision D-1. |
| C-18 | **M7 and M8 are renumbered.** The old M7 (six-ball loop) and M8 (polish) tasks (T025–T032) and AC-07..10 are re-planned as M7 (framework) and M11 (polish). | tasks.md, spec.md | Retire T025–T032 as written. New tasks start at T047. AC-10 (final device session) moves to M11 and is reworded for both modes. |
| C-19 | **Timing data.** The brief's timing window (where the ball was met) needs foot position or shot type, which we don't track; the same contact point is perfect for a cut and late for a drive. | brief Shot Confidence | Use **swing-phase timing** as the primary measure: contact time relative to the swing's peak bat speed. We have the bat-speed history; it needs a peak timestamp, a small addition to BatController's swing tracking. The contact-position window is used only for defensive shots, and calibrated (CAL-2). Details in §4. |
| C-20 | **Exit speed to bat speed ratio.** In the response model, exit-to-bat speed is a function of restitution, sweet factor and ball speed, so it largely duplicates Middle. | brief Power | Drop it from Power; Power uses exit speed relative to a calibrated target (§4). |
| C-21 | **"Smooth swing" control.** A contact can't happen while tracking-glitch suppression is active (M5: suppressed touches are ignored and the ball is a miss), so "no suppression on that ball" is always true for contacts. | brief Control | Replace it with "no tracking reset within 0.5 s before contact" (the tracker records the last reset time). |
| C-22 | **M6.5 is unfinished.** Your proposed order says M6.5 is "already specced", but its Test 2 (AC-12/13) and AC-14 numbers are still pending, and the branch isn't merged. | validation.md AC-12..14 | Finish M6.5 Test 2 and AC-14 on the current build, merge `m6.5-ground` into `main`, then start M7. The new structure doesn't change anything M6.5 built. |

---

## 2. Owner decisions (with recommendations)

**D-1 Strike rotation in Match Simulation.**
**Recommendation: the player faces every ball** (arcade). Odd runs don't change strike, and the summary says "you face every ball". Simulating the non-striker's balls would mean a second batting model, which we don't have and can't test meaningfully, plus balls the player watches rather than plays.

**D-2 Wickets.**
**Recommendation: a wicket only reduces wickets in hand**, and the ball counts as a dot. The innings ends when wickets in hand reach 0 (loss) or the target is reached (win). No extra penalty; the lost wicket and the dot ball already raise the required rate.

**D-3 Field setting.**
**Recommendation: one fixed field for M10** (the M6.5 FieldConfig). Calibrate the scorer on it first. FieldConfig is already a list in an asset, so after M10 two presets are cheap to add: "standard" and "defensive" (two more fielders on the boundary when the required rate is above 10). They aren't in M10, so calibration doesn't have to chase two fields at once.

**D-4 Shot Confidence in Match Simulation.**
**Recommendation: yes**, as a small secondary line under each ball's result, using the same formula. It's free once M8 exists and helps you connect simulated runs to shot quality. It isn't averaged into the match result.

**D-5 Leave detection.** Your definition needs predicting whether a left ball would hit the stumps.
**Recommendation: simpler, and needs no prediction.** The unhit ball keeps flying under physics, so the stumps decide:
- It hits the stumps → **Bowled, 0**.
- It passes the stumps (z < −1 m) → classify by what the bat did:
  - **Played and missed (0, counted):** the closest bat–ball gap was under **0.20 m**, *or* the bat's contact-point speed toward the ball's path exceeded **3 m/s** within ±0.15 s of the ball passing the bat.
  - **Well left (excluded from the average, shown separately):** otherwise.

The resolver already tracks the closest gap (M5 near-miss log), and the bat-speed history exists. The two thresholds are calibrated (CAL-1).

---

## 3. Text for spec.md

### Replace "Goal and player story"
> Put on the headset and choose a mode from the start menu.
> - **Net Practice:** face a chosen number of overs in the practice nets, see a Shot Confidence rating for every ball, and finish with a session summary. No runs are scored.
> - **Match Simulation:** stand in the middle of a cricket ground and chase a target (runs, balls and wickets) against a bowling machine. Every shot is scored by a virtual field with static fielders. Win or lose, see the ball-by-ball summary.
>
> All runs are arcade **simulated runs**, not official cricket scoring.

### Requirements: amended rows (IDs kept)

| ID | Requirement | Milestone |
| --- | --- | --- |
| REQ-008 | Every finalized playable delivery produces exactly one per-ball record (see REQ-027), including contact state, wicket state and the measurements available in its mode. | M6, M7 |
| REQ-009 | *(Amended)* Net Practice scores no runs. A net zone reached after contact is recorded only as a "where it went" label. Misses, leaves and bowled balls score nothing in either mode. Runs exist only in Match Simulation, from the virtual-field scorer (REQ-034..036). | M6, M8 |
| REQ-010 | *(Amended)* Net Practice shows "Measured carry" only for a real landing before any net, otherwise "Net hit" / "N/A". Match Simulation shows "Simulated carry" from the scorer and never labels it measured. | M6, M10 |
| REQ-011 | *(Amended)* Choosing a mode starts a readiness countdown, then a session with a ball budget: Net Practice = overs × 6; Match Simulation = the scenario's balls. A ball is bowled only after the previous one is finalized and the between-ball interval has passed. The budget is never exceeded. | M7 |
| REQ-012 | *(Amended)* The end screen shows every ball's record and totals derived only from those records. Net Practice ends after its ball budget; wickets don't end it. Match Simulation ends on win (target reached), on balls running out, or on wickets running out. | M7, M8, M10 |
| REQ-013 | *(Amended)* "Play again" restarts the same mode and options. "Menu" returns to the start menu. Both leave no active ball, stale countdown or callback. "New scenario" (Match Simulation) draws a new scenario. | M7 |
| REQ-014 | *(Amended)* Focus or tracking loss pauses the session, stops bowling, and cancels any active delivery without consuming it. Resuming requires the player to confirm readiness. In both modes. | M7 |
| REQ-021 | *(Amended)* The NetMode/GroundMode environment switch is permanent. The start-menu choice sets the environment, the result pipeline (net finalization vs virtual-field scorer) and the UI. | M6.5, M7 |

### Requirements: new rows

| ID | Requirement | Milestone |
| --- | --- | --- |
| REQ-025 | A world-space start menu offers Net Practice (overs 1/2/3/5, default 2) and Match Simulation (scenario preset or random; difficulty Easy/Medium/Hard). It is operated by the right controller's ray, with a fallback of tapping a button with the bat blade. The bat is hidden and the ray hidden outside menus. | M7 |
| REQ-026 | A shared session framework drives both modes: menu → countdown → ball-by-ball (deliver, finalize, show the per-ball panel, interval) → end screen → play again / menu (/ new scenario), with pause (REQ-014). | M7 |
| REQ-027 | One per-ball record type holds: delivery parameters (and seed), contact data, `ShotLaunch`, the physical result, Shot Confidence components (when computed) and the virtual-field outcome (Match Simulation). In development builds each record is appended to a per-session CSV on the device for calibration. | M7 |
| REQ-028 | Each mode has a delivery plan: Net Practice mixes lines and lengths; Match Simulation uses a difficulty preset (speed range; how often yorkers and short balls come). Every planned delivery passes the solver before use; an unreachable one is redrawn and logged, never silently altered. | M7 |
| REQ-029 | Net Practice rates every contacted ball with Shot Confidence 0–100 from four components (Middle, Timing, Control, Power) with tunable weights. The ball's confidence and breakdown are shown briefly after it. | M8 |
| REQ-030 | Unhit balls are classified: Bowled (0, counted), Played and missed (0, counted), Well left (shown separately, excluded from the average), per D-5. | M8 |
| REQ-031 | The Net Practice end screen shows: average confidence and the average of each component; best and worst ball; counts of middled, off-centre, edged, played-and-missed, well-left and bowled balls; and a per-over trend of average confidence. | M8 |
| REQ-032 | At bat contact a `ShotLaunch` (position, exit velocity, contact quality) is captured, and a deterministic pure-C# simulation (≈200 Hz, no PhysX) produces the ball's path: flight with gravity and quadratic drag, bounces, roll, the first landing (simulated carry) and the shot angle. | M9 |
| REQ-033 | Boundaries come from GroundDefinition: carry over the rope = 6; crossing the rope after bouncing, with no earlier intercept = 4. | M9 |
| REQ-034 | Fielders from FieldConfig can catch (airborne ball 0.2–2.5 m high, reached with ≥ 0.3 s to spare = caught; less = "chance, dropped") and field on the ground (runs 0–3 from the time the ball gets back to the stumps). A ball that stops unfielded uses the nearest fielder's time. A hit path reaching the stumps within 1.5 s is bowled (played on). | M9 |
| REQ-035 | The scorer is fully EditMode-testable and calibrated by replaying the owner's logged headset contacts (CSV) through it, with the calibration results recorded. | M9 |
| REQ-036 | Match Simulation scenarios come from presets or a seeded random generator, bounded to a required rate of 6–15 per over, 6–36 balls and 1–5 wickets. The same seed gives the same scenario. | M10 |
| REQ-037 | The Match Simulation scoreboard updates after every ball: runs needed, balls left, wickets left and required rate (shown only while balls are left). Win, loss by balls and loss by wickets are detected exactly once. | M10 |
| REQ-038 | In Match Simulation the ball's post-contact motion is the scorer's simulated path, played back, so what the player sees is what is scored (C-6). | M10 |
| REQ-039 | Both modes hold the 72 Hz target on Quest 3 during normal play, with measured CPU/GPU frame time and draw calls, foveation on and off. | M11 |

REQ-016 (three consecutive sessions plus restart and interruption on Quest) stays, re-targeted to M11 and to both modes.

### Acceptance scenarios: amended

- **AC-06 (amended):** in Net Practice each delivery finalizes once; a miss, a bowled ball and contacts into each net zone produce one record each, with no runs; carry is shown only when measured. *(E-030/E-031 remain valid for finalization and carry. Their run assertions are superseded.)*
- **AC-07 — Complete session (REQ-011, REQ-012, REQ-026):** a Net Practice session of 2 overs delivers exactly 12 balls, then the end screen, with totals matching the records. A Match Simulation scenario ends on the ball that reaches the target, or on the last ball or wicket, and never bowls an extra ball.
- **AC-08 — Restart and menu (REQ-013):** from the countdown, an active ball, between balls and the end screen, "Play again" and "Menu" each leave no stale ball or callback; repeat 5 times per mode.
- **AC-09 — Interruption (REQ-014):** unchanged in intent, in both modes.
- **AC-10 — Final device sessions (REQ-016, REQ-039):** three consecutive sessions per mode on Quest, plus restart and interruption, with frame timing recorded.

### Acceptance scenarios: new

- **AC-15 — Start menu (REQ-025):** with only the right controller, the owner selects each mode and each option by ray, and once by bat-tap. The ray and the bat never show at the same time.
- **AC-16 — Per-ball record and log (REQ-027, REQ-028):** in a development build, a session writes one CSV row per finalized ball with every field filled for its mode. No planned delivery is unreachable; redraws are logged.
- **AC-17 — Shot Confidence (REQ-029, REQ-030):**
  - **Automated:** fixed inputs give the documented component values; edges are capped; leaves are excluded.
  - **Headset:** the owner plays a middled drive, a high-on-blade drive, an edge, a block, a leave and a play-and-miss. The ratings rank in the expected order: middled drive > high-on-blade drive > edge; block rated on Middle/Control without a Power penalty; leave excluded; play-and-miss 0.
- **AC-18 — Net Practice summary (REQ-031):** the end screen values equal values recomputed from the CSV of the same session.
- **AC-19 — Scorer physics (REQ-032, REQ-033):**
  - **Automated:** the drag-free path matches the analytic range within 1%; drag shortens the range monotonically with k; bounce and roll match the formulas; the sim is deterministic; boundaries give 6 and 4 correctly, including at the rope.
- **AC-20 — Fielding (REQ-034):**
  - **Automated:** table-driven cases: catches with and without enough time to spare (dropped); ground intercepts giving runs 0, 1, 2 and 3; an unfielded stop; played-on; a ball hit behind the wicket.
- **AC-21 — Calibration (REQ-035):** the owner's CSV of at least 100 contacts is replayed. A distribution report (outcomes, carry, run histogram) is recorded and tuning changes are justified against it.
- **AC-22 — Scenarios (REQ-036):** the same seed gives the same scenario; 1,000 random scenarios all fall within the bounds.
- **AC-23 — Chase (REQ-037, REQ-038):**
  - **Automated:** each win/loss path ends exactly once.
  - **Headset:** the owner plays three scenarios. The scoreboard is always correct, and the ball visibly goes where the result says (rope, catch, fielder).
- **AC-24 — Performance (REQ-039):** recorded numbers for both modes, foveation on and off.

---

## 4. Shot Confidence: formula, thresholds and calibration

### Inputs
Everything below exists today except the swing-peak timestamp (C-19).

| Symbol | Meaning | Source |
| --- | --- | --- |
| S | Sweet factor at contact, 0–1 (Gaussian, centre 0.20 m from the toe, falloff 0.22 m) | `BatResponse.SweetFactor` |
| E | Edge factor magnitude, 0–1 | `BatResponse.EdgeFactor` |
| v_out | Exit speed (m/s) | `BatContactReport.BallVelocityOut` |
| λ | Launch angle (°) | from v_out |
| α | Horizontal angle between the face normal and the exit direction (°) | `Normal`, `BallVelocityOut` |
| v_peak | Peak bat toe speed in the swing that made contact (m/s) | BatController swing tracking |
| Δt | t_contact − t_peak (s) | needs the peak timestamp added (small change in M8) |
| reset | A tracking reset within 0.5 s before contact | `BatMotionTracker.LastResetTime` |
| z_c, y_c | Contact point position along the pitch and height (m) | `BatContactReport.Point` |

**Shot type:** **attacking** if v_peak ≥ 4 m/s; otherwise **defensive** (CAL-3).

### Components (each 0–100)

**Middle (35%)**
- M = 100 · S · (1 − E)²
- Classification for the summary counts:
  - **Edged** if E ≥ 0.5.
  - **Middled** if S ≥ 0.7 and E < 0.5.
  - **Off-centre** otherwise.

**Timing (25%)**
- *Attacking:* T = 100 · exp(−(Δt / 0.06)²).
  - Contact at the swing's peak = 100.
  - 60 ms early or late ≈ 37.
  - 120 ms off ≈ 2.
- *Defensive:* T = 100 when the ball is met within the window z_c ∈ [0.8, 2.2] m and y_c ≤ 1.1 m, falling linearly to 0 over 0.5 m outside it. The z window is roughly the popping crease ± 0.6 m and in front; it's a proxy and needs calibration (CAL-2).
- *Reliability flag:* the attacking timing is solid once the peak timestamp exists. The defensive window can't account for footwork and is the weakest measure in the formula. If calibration shows it doesn't separate good blocks from bad, set defensive Timing to Middle's value (that is, drop it).

**Control (25%)**
- c_face = clamp01(1 − (α − 10°) / 30°): full credit at α ≤ 10°, zero at α ≥ 40°. This measures slicing across the line.
- c_launch = 1 if λ ≤ 15° or S ≥ 0.6; otherwise clamp01(1 − (λ − 15°) / 30°). This penalises unmiddled lofts and top edges.
- c_track = 1, or 0.5 if there was a tracking reset within 0.5 s before contact.
- C = 100 · (1 − E) · (0.5·c_face + 0.5·c_launch) · c_track
- *Reliability flag:* in our response model the exit direction comes from the face normal plus bat velocity. So c_face mostly measures the bat moving across the face (a real slice). It can't measure the intent ("I meant to play to cover"). It is a fair stand-in for "control".

**Power (15%)**
- *Attacking:* P = 100 · clamp01(v_out / v_target), with v_target = **32 m/s** to start. Your best logged drive was 35.5 m/s. Calibrate v_target to the 90th percentile of your middled attacking contacts (CAL-5).
- *Defensive:* Power is not applied. Its 15% moves to Control, so a good block isn't punished for being soft.
- Exit speed to bat speed ratio: dropped (C-20).

### Overall
- Confidence = round(w_M·M + w_T·T + w_C·C + w_P·P), with weights 0.35 / 0.25 / 0.25 / 0.15 from a `ConfidenceSettings` ScriptableObject. They are renormalised when Power is dropped for a defensive shot.
- **Edge cap:** if E ≥ 0.5, Confidence = min(Confidence, 30).
- **Unhit balls:** Bowled = 0 (counted); Played and missed = 0 (counted); Well left = excluded, shown as "well left (n)".
- **Labels:**
  - 85 and above: "Excellent"
  - 70–84: "Good"
  - 50–69: "OK"
  - 30–49: "Poor"
  - under 30: "Mis-hit"

### Needs calibration from headset data
From the M7 CSV log, with the CAL-n settings stored in `ConfidenceSettings`:

| ID | What | Starting value | How |
| --- | --- | --- | --- |
| CAL-1 | Play-and-miss gap and bat-speed thresholds | 0.20 m, 3 m/s | You label 20 leaves and 20 play-and-misses by announcing each in a test; pick thresholds that separate them. |
| CAL-2 | Defensive timing window | z 0.8–2.2 m, y ≤ 1.1 m | Distribution of your defensive contacts; check it separates blocks you rate good and bad. |
| CAL-3 | Attacking/defensive split | v_peak 4 m/s | The gap in the v_peak histogram. |
| CAL-4 | Timing width | 0.06 s | Δt spread of your middled drives. |
| CAL-5 | Power target | 32 m/s | 90th percentile of middled attacking exit speed. |
| CAL-6 | Keeper/slip reaction (scorer) | 0.35 s | Replay of logged edges. |
| CAL-7 | Scorer drag, bounce, roll (k, e_v, e_h, μ_roll) | 0.008, 0.4, 0.7, 0.25 | Run histogram and carry distribution; for example a hard straight drive should usually reach the rope for 4. |

---

## 5. Virtual-field scorer: design notes for plan.md

These add to the brief's design and become plan.md §16.

- **Pure C# in Core:** `VirtualField` with `Simulate(ShotLaunch, GroundDefinitionData, FieldData, ScorerSettings) → ScorerOutcome`.
  - `GroundDefinitionData` and `FieldData` are plain structs copied from the assets, so EditMode tests need no assets.
  - Fixed step 1/200 s, semi-implicit Euler, at most 15 s of simulated time.
  - Output: a path (samples every 1/50 s for playback), the first landing (simulated carry), the shot angle, and the events below.
- **Phases:**
  - **Flight:** a = g − k·|v|·v.
  - **Ground contact** at y ≤ radius:
    - v_y ← −e_v·v_y and v_h ← e_h·v_h;
    - roll when |v_y| after the bounce < 1 m/s.
  - **Roll:** decelerate horizontally by μ_roll·g + k·v².
  - **Stop** below 0.1 m/s.
- **Stumps (played on, C-7):** during the first 1.5 s, the path is swept against the stumps box. Touching it = Bowled.
- **Rope:** checked with `GroundGeometry.DistanceInsideBoundary` (shared with M6.5).
  - Rope crossed before the first landing = 6.
  - Crossed after a landing, with no intercept earlier on the path = 4.
- **Catch check:** at each path sample before the first landing with height 0.2–2.5 m, for each fielder:
  - spare = (t − reaction) − (|p_xz − f| − reach) / runSpeed.
  - The earliest sample with spare ≥ 0 is a catch chance.
  - Caught if spare ≥ 0.3 s; otherwise "chance, dropped" and the simulation continues.
- **Ground fielding:** after the first landing, or for samples with height < 0.2 m, the earliest t with spare ≥ 0 gives t_f and the intercept point.
  - T = t_f + 0.5 + throwDistance / 28, where throwDistance runs from the intercept to the striker's stumps.
  - runs = clamp(floor((T − 1.0) / 3.0), 0, 3).
  - A four reached before any intercept stays 4.
- **Stopped, no intercept:** t_f = the nearest fielder's arrival time at the stop point; then the same formula.
- **Order of events** is the same principle as M6 (earliest first). For ties: stumps > catch > rope > intercept.
- **The result enters the existing finalization** as one terminal event, `VirtualOutcome`, at contact plus the playback time of its end point, so the result hold and "exactly once" are unchanged. A pre-contact physical bowled or miss still wins, because contact never happened.
- **Left-handers:** fielders mirror (M6.5); the shot angle uses the same convention.

---

## 6. Text for tasks.md

Retire T025–T032 (old M7/M8, superseded; kept in history). New tasks:

### M6.5 (finish)
- T046 (existing): AC-14 numbers with foveation on and off. Plus M6.5 Test 2 (AC-12/13) and the merge into `main`.

### M7 — Session framework and start menu
Requirements REQ-008, REQ-011..014, REQ-021, REQ-025..028. Acceptance AC-07, AC-08, AC-09, AC-15, AC-16.
- [ ] T047 Session state machine in Core (Menu, Countdown, Delivering, Resolving, BallPanel, Interval, Paused, SessionEnd), with a generation token, a ball budget, early end, and no stale callbacks. EditMode tests for every transition, restart from each state, and pause without consuming a ball.
- [ ] T048 `BallRecord` type in Core and the CSV writer (development builds), with a test that the CSV round-trips.
- [ ] T049 Delivery plans (Net mix; Easy/Medium/Hard), solver-checked with redraw and log. Test: 1,000 draws per plan are all reachable, and redraws are counted.
- [ ] T050 World-space start menu with the right-controller XRI ray and the bat-tap fallback; the ray and bat show only in their own phases. *(headset: AC-15)*
- [ ] T051 Wire both modes to the framework, with an interim result display (Net: hit/miss/bowled; Ground: pending). Per-ball panel, end screen, play again / menu. *(headset: AC-07/08/09)*
- [ ] T052 Retire net-zone runs from the player-facing game (§7). Update or remove the affected tests deliberately.

### M8 — Net Practice with Shot Confidence
Requirements REQ-009, REQ-029..031. Acceptance AC-06 (amended), AC-17, AC-18.
- [ ] T053 Add the swing-peak timestamp and the per-contact swing data (v_peak, Δt, reset age) to the contact report.
- [ ] T054 `ShotConfidence` in Core with `ConfidenceSettings`. EditMode tests for each component, the edge cap, defensive renormalisation and unhit classification.
- [ ] T055 Leave / played-and-missed classification (D-5). Tests using recorded gap and bat-speed traces.
- [ ] T056 Overs selection, per-ball confidence panel and the Net Practice summary (averages, best/worst, counts, per-over trend). Test: summary values = values recomputed from records.
- [ ] T057 Headset: AC-17 shot set and AC-18. Collect the first CSV for CAL-1..5.

### M9 — Virtual-field scorer
Requirements REQ-032..035. Acceptance AC-19..21.
- [ ] T058 `ShotLaunch` capture; `VirtualField` flight/bounce/roll; tests (analytic range, drag monotonicity, bounce/roll formulas, determinism).
- [ ] T059 Boundary 4/6 and the stumps sweep (played on); tests including balls landing exactly at the rope and the stumps edge.
- [ ] T060 Catch and ground fielding, runs formula, unfielded stop; a table-driven test suite.
- [ ] T061 A calibration tool (Editor menu and batch) that replays a CSV and writes a distribution report; tune CAL-6/7. *(evidence: AC-21)*

### M10 — Match Simulation
Requirements REQ-010 (amended), REQ-012, REQ-036..038. Acceptance AC-22, AC-23.
- [ ] T062 Scenario presets and the seeded random generator (bounded); tests for bounds and repeatability.
- [ ] T063 Chase state and scoreboard (needed, balls, wickets, required rate), with win/loss detection exactly once; tests.
- [ ] T064 Kinematic playback of the simulated path after contact (C-6), ending at the catch, rope or intercept. Plus "Simulated carry" labels and the per-ball summary.
- [ ] T065 Difficulty presets wired to delivery plans (Hard ≤ 120 km/h until AC-04 is re-run).
- [ ] T066 Headset: AC-23, three scenarios.

### M11 — Polish and device verification
Requirements REQ-015, REQ-016, REQ-039. Acceptance AC-10, AC-24.
- [ ] T067 Readability, audio/haptics balance, error messages.
- [ ] T068 AC-24 performance in both modes (probe and OVR Metrics), foveation on and off.
- [ ] T069 AC-10: three sessions per mode, restart and interruption; README, validation and limitations up to date.

---

## 7. Retiring M6 net-zone runs: what is kept, changed or removed

| Item | Action |
| --- | --- |
| `DeliveryAdjudicator`: finalize once, time ordering, same-step priority, stale rejection, cancel, carry | **Keep** (both modes) |
| `BallEvent`, `BallController.Finish/Despawn`, 1.5 s dead-ball hold, `ScoreManager` event gathering | **Keep**; ScoreManager becomes the shared "delivery recorder" (it may be renamed in M7) |
| `NetWall`, `PracticeScoring.NetZone/ExitSide/ZoneLabel` | **Keep** as a non-scoring "where it went" label (REQ-009) |
| `ScoringRules` run values, `PracticeScoring.Runs`, `DeliveryOutcome.Scored/NoRuns`, `DeliveryResult.Runs` (Net) | **Change.** Net outcomes become Hit / Miss / Bowled / Left. Runs move to the Match Simulation outcome. Remove the run fields from `ScoringRules` (heights stay). |
| `SessionTotals` | **Change**: per mode. Net: confidence summary and counts. Match: simulated runs, wickets, balls. |
| `BowlingDebugControls` readout | **Change**: becomes a debug overlay, toggleable. Player-facing results move to the M7 UI. |
| EditMode `DeliveryAdjudicatorTests` | Remove run assertions: rewrite `ZoneRunsMatchTheAcceptedPracticeTable`, `HitIntoFrontMiddleScoresFour`, `ClearingTheFrontNetScoresSix`, `ClearingASideOrRearNetScoresOne` and `TotalsAreDerivedFromRecords` to assert zone labels and outcomes. Keep the others (finalize once, priority, stale, carry, cancel, settle) unchanged. |
| PlayMode `ScoringPlayModeTests` | Rewrite `FrontNetHitsScoreByHeight`, `ClearingTheFrontNetScoresSix` and `HitIntoASideNetScoresOne` to assert zone and "Hit" with no runs. Keep the rest. |
| `GroundModeAdjudicatorTests` | Keep in M7–M9 (interim pending result); in M10 replace "pending" with virtual-field outcome tests. |
| REQ-009, REQ-010, AC-06, PC-01 | **Amend** as in §3; PC-01's run table is marked superseded with the date. |
| E-030, E-031 | **Keep** as historical evidence; add a note that the run-scoring parts are superseded by this proposal. |

---

## 8. Template additions for validation.md

Rows AC-15 to AC-24 go into the acceptance register as "Not run". Planned check IDs:
- E-034: M6.5 Test 2 (AC-12/13)
- E-035: AC-14
- E-036 onward: per task as run

Extra fields per check type, on top of the standard template:
- **Confidence (AC-17/18):** per shot: S, E, Δt, v_peak, α, λ, v_out, each component, overall; summary values vs CSV recomputation.
- **Scorer (AC-19..21):** test counts; calibration CSV row count; outcome histogram (dot / 1 / 2 / 3 / 4 / 6 / caught / dropped / played on); carry percentiles; before and after values of each CAL-n.
- **Chase (AC-23):** scenario seed, target / balls / wickets, the ball-by-ball log, the end condition, and whether the visual matched the result on each ball.
- **Performance (AC-24):** as AC-14, per mode.
