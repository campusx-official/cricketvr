# Feature 001: VR cricket batting — Net Practice and Match Simulation

Status: M1–M6 implemented and validated; M6.5 (cricket ground) in validation; two-mode structure accepted 2026-10-26 (`proposals/two-modes-replan.md`). The feature folder keeps its original name `001-six-ball-net`.
Source: Owner's agreed MVP and milestones from the conversation.

## Goal and player story

Put on the headset and choose a mode from the start menu.
- **Net Practice:** face a chosen number of overs in the practice nets, see a Shot Confidence rating for every ball, and finish with a session summary. No runs are scored.
- **Match Simulation:** stand in the middle of a cricket ground and chase a target (runs, balls and wickets) against a bowling machine. Every shot is scored by a virtual field with static fielders. Win or lose, see the ball-by-ball summary.

All runs are arcade **simulated runs**, not official cricket scoring. *(Replaces the original six-ball net goal, 2026-10-26; owner instruction overrides the CLAUDE.md product background.)*

Development machine: M4 MacBook Air, 16 GB RAM. Target: standalone Quest 3, controller-only play. Unity knowledge is beginner level. The game must run without the Mac after installation.

## Scope

Two modes chosen from a start menu, in one scene with two permanent environment roots: the practice net (Net Practice) and the cricket ground (Match Simulation). One tracked bat, one active ball, a configurable launcher with per-mode delivery plans, Shot Confidence (Net Practice), a deterministic virtual-field scorer with static fielders (Match Simulation), scoreboard and end screens, a per-ball record with a calibration CSV log (development builds), both handedness settings and interruption recovery.

Deferred: animated or moving fielders, run-outs, LBW, extras, non-striker simulation (the player faces every ball), animated bowlers, spin/swing/seam, physical bat requirements, multiplayer, cloud services, coaching claims, and AI models. *(Static simulated fielders, simulated catches and a stadium are now in scope by owner instruction: M6.5 and the two-mode re-plan.)*

## Requirements

| ID | Observable requirement | Milestone |
| --- | --- | --- |
| REQ-001 | The app installs from macOS and launches standalone on Quest 3 with stereo head tracking and both controllers tracked. | M1 |
| REQ-002 | The practice environment has consistent metre scale, floor alignment, visible crease/stumps, and an unobstructed view toward the launcher. | M2 |
| REQ-003 | The selected controller drives a virtual bat with adjustable alignment and right/left-handed selection before starting. | M3 |
| REQ-004 | The launcher produces repeatable deliveries with configurable speed, line, and bounce length; only one ball is active. | M4 |
| REQ-005 | Flight and bounce respond consistently to the configured launch state and pitch; impossible delivery settings are reported. | M4 |
| REQ-006 | Bat contact reflects contact-point velocity, orientation, and contact position, producing distinguishable blocks, drives, and lofted responses. | M5 |
| REQ-007 | The agreed fast-swing test set shows no observed tunneling or duplicate bat impulse; tracking discontinuities cannot create a shot. | M5 |
| REQ-008 | Every finalized playable delivery produces exactly one per-ball record (see REQ-027), including contact state, wicket state and the measurements available in its mode. | M6, M7 |
| REQ-009 | *(Amended 2026-10-26)* Net Practice scores no runs. A net zone reached after contact is recorded only as a "where it went" label. Misses, leaves and bowled balls score nothing in either mode. Runs exist only in Match Simulation, from the virtual-field scorer (REQ-032..034). | M6, M8 |
| REQ-010 | *(Amended 2026-10-26)* Net Practice shows "Measured carry" only for a real landing before any net, otherwise "Net hit" / "N/A". Match Simulation shows "Simulated carry" from the scorer and never labels it measured. | M6, M10 |
| REQ-011 | *(Amended 2026-10-26)* Choosing a mode starts a readiness countdown, then a session with a ball budget: Net Practice = overs × 6 (overs 1, 2, 3 or 5; default 2); Match Simulation = the scenario's balls. A ball is bowled only after the previous one is finalized and the between-ball interval has passed. The budget is never exceeded. | M7 |
| REQ-012 | *(Amended 2026-10-26)* The end screen shows every ball's record and totals derived only from those records. Net Practice ends after its ball budget; wickets don't end it. Match Simulation ends on a win (target reached), on balls running out, or on wickets running out. | M7, M8, M10 |
| REQ-013 | *(Amended 2026-10-26)* "Play again" restarts the same mode and options; "Menu" returns to the start menu; "New scenario" (Match Simulation) draws a new scenario. All leave no active ball, stale countdown or callback. | M7 |
| REQ-014 | *(Amended 2026-10-26)* Focus or tracking loss pauses the session, stops bowling, and cancels any active delivery without consuming it. Resuming requires the player to confirm readiness. Both modes. | M7 |
| REQ-015 | Menus/scoreboard are readable from the stance; impacts produce sound and modest vibration; there is no forced camera movement. | M5/M8 |
| REQ-016 | Three consecutive sessions per mode plus restart/interruption checks succeed on Quest at the selected measured rendering target. | M11 |
| REQ-017 | A ground mode replaces the practice net with a believable cricket ground seen from the striker's crease: outfield with mowing stripes, pitch strip with Laws-sized markings at both ends, both sets of stumps, boundary rope, 30-yard circle markers, sight screens, advertising boards, low stands with a crowd impression, sky and distance fog. | M6.5 |
| REQ-018 | All ground dimensions come from one `GroundDefinition` asset. The ground can be rebuilt from it reproducibly with an Editor menu, with no hand-placed geometry. | M6.5 |
| REQ-019 | The pitch keeps its collider and physics material, so delivery bounce calibration is unchanged. The outfield has its own thicker collider with lower bounce and higher friction, and a 45 m/s ball can't tunnel through it. A documented rolling drag slows the ball only on the outfield. | M6.5 |
| REQ-020 | The game detects a ball leaving the ground (over the rope or above the stands) once per delivery, raises `BallLeftGround(position, time)` without scoring, and hides the ball 1.5 s later. A safety despawn removes a ball that goes below the outfield or more than 150 m from the ground centre. | M6.5 |
| REQ-021 | *(Amended 2026-10-26)* The NetMode/GroundMode environment switch is permanent. The start-menu choice sets the environment, the result pipeline (net finalization vs virtual-field scorer) and the UI. Until M10, GroundMode results read "Hit, practice scoring pending" (Bowled/Miss as GD-8). | M6.5, M7 |
| REQ-022 | Static placeholder fielders stand at the positions in a `FieldConfig` list (angle and radius from the striker's stumps). They can be hidden and don't move or field. | M6.5 |
| REQ-023 | GroundMode holds the 72 Hz target on Quest 3 within these budgets: ≤ 150 draw calls, < 250k triangles, < 150 MB texture memory. Textures are ASTC with mipmaps. There are no large transparent surfaces and no animated environment. | M6.5 |
| REQ-024 | The ball stays high-contrast against the sight screen behind the machine, the pitch and the outfield. Thin distant lines (rope, circle markers, stands) don't visibly shimmer, and markings don't z-fight. | M6.5 |
| REQ-025 | A world-space start menu offers Net Practice (overs 1/2/3/5, default 2) and Match Simulation (scenario preset or random; difficulty Easy/Medium/Hard). It is operated by the right controller's XRI ray, with a fallback of tapping a button with the bat blade. Outside menus the ray is hidden; in menus the bat is hidden. | M7 |
| REQ-026 | A shared session framework drives both modes: menu → countdown → ball-by-ball (deliver, finalize, per-ball panel, interval) → end screen → play again / menu (/ new scenario), with pause (REQ-014). | M7 |
| REQ-027 | One per-ball record type holds the delivery parameters (and seed), contact data, `ShotLaunch`, the physical result, the swing timing offset Δt, the Shot Confidence components (when computed) and the virtual-field outcome (Match Simulation). In development builds each record is appended to a per-session CSV on the device for calibration. | M7 |
| REQ-028 | Each mode has a delivery plan: Net Practice mixes lines and lengths; Match Simulation uses a difficulty preset (speed range; how often yorkers and short balls come). Every planned delivery passes the solver before use; an unreachable one is redrawn and logged, never silently altered. | M7 |
| REQ-029 | Net Practice rates every contacted ball with Shot Confidence 0–100 from four components (Middle, Timing, Control, Power) with tunable weights and thresholds (plan.md §17). The ball's confidence and breakdown are shown briefly after it. | M8 |
| REQ-030 | Unhit balls are classified: Bowled (0, counted), Played and missed (0, counted), Well left (shown separately, excluded from the average). Played and missed = closest bat–ball gap under a gap threshold (default 0.20 m) or bat speed toward the ball over a speed threshold (default 3 m/s) near the ball passing; both are settings. | M8 |
| REQ-031 | The Net Practice end screen shows: average confidence and the average of each component; best and worst ball; counts of middled, off-centre, edged, played-and-missed, well-left and bowled balls; and a per-over trend of average confidence. | M8 |
| REQ-032 | At bat contact a `ShotLaunch` (position, exit velocity, contact quality) is captured, and a deterministic pure-C# simulation (≈200 Hz, no PhysX) produces the ball's path: flight with gravity and quadratic drag, bounces, roll, the first landing (simulated carry) and the shot angle. Rolling resistance is the single GroundDefinition value (starting 0.25 g). | M9 |
| REQ-033 | Boundaries come from GroundDefinition: carry over the rope = 6; crossing the rope after bouncing, with no earlier intercept = 4. | M9 |
| REQ-034 | Fielders from FieldConfig can catch (airborne ball 0.2–2.5 m high, reached with ≥ 0.3 s to spare = caught; less = "chance, dropped") and field on the ground (runs 0–3 from the time the ball gets back to the stumps). A ball that stops unfielded uses the nearest fielder's time. A hit path that reaches the stumps within 1.5 s is bowled (played on), tested against the same stumps box the physics uses, so "bowled" means the same thing in both modes. | M9 |
| REQ-035 | The scorer is fully EditMode-testable and calibrated by replaying the owner's logged headset contacts (CSV), with the calibration results recorded. | M9 |
| REQ-036 | Match Simulation scenarios come from presets or a seeded random generator, bounded to a required rate of 6–15 per over, 6–36 balls and 1–5 wickets. The same seed gives the same scenario. The player faces every ball (no strike rotation); a wicket only reduces wickets in hand and that ball is a dot. | M10 |
| REQ-037 | The Match Simulation scoreboard updates after every ball: runs needed, balls left, wickets left and required rate (shown only while balls are left). Win, loss by balls and loss by wickets are each detected exactly once. Shot Confidence is shown per ball as a secondary line, not part of the result. | M10 |
| REQ-038 | In Match Simulation the ball's post-contact motion is the scorer's simulated path, played back so what the player sees is what is scored. The handover is seamless: playback starts from the exact contact position and exit velocity, with no visible jump or speed change. The existing bat-impact sounds stay; simple sounds play when the ball lands, is fielded or caught, or crosses the rope. | M10 |
| REQ-039 | Both modes hold the 72 Hz target on Quest 3 during normal play, with measured CPU/GPU frame time and draw calls, foveation on and off. | M11 |

## Proposed defaults and precise rules

These make the baseline implementable; they are not claims that every value was explicitly selected by the owner. Keep tuning configurable and document later changes.

- Delivery count: *(superseded 2026-10-26)* overs × 6 in Net Practice, the scenario's balls in Match Simulation (REQ-011). Three-second starting countdown and three-second inter-ball readiness interval; configurable.
- Default delivery: repeatable straight ball at approximately 80 km/h; line/length selected during M4 tuning. Start slower during initial batting tests.
- Ball timeout: bounded, initially 10 seconds after release; configurable.
- One wicket per delivery at most. It does not end a Net Practice session; in Match Simulation it reduces wickets in hand and the session ends when none are left.
- A miss means no bat contact and no wicket. Bat contact followed by bowled counts as a contact and a wicket, not a miss.
- *(Superseded 2026-10-26: net zones no longer score runs; they label where the ball went.)* Zones are nonoverlapping; at a shared edge, deterministic configured priority breaks a tie.
- First valid terminal event finalizes the delivery. Evaluate collisions in time order; bowled must not later gain runs. Do not award provisional runs before a terminal outcome.
- For a post-contact ground landing before net impact, measured carry is horizontal displacement from bat contact to landing. A prior net hit produces `Net hit / N/A`, not an invented carry.
- An interrupted delivery is cancelled and retried. Previously finalized records remain unchanged.
- MVP requires one accepted bat strike per delivery; overlap callbacks must not repeatedly accelerate the ball.
- Baseline render target: 72 Hz, approximately 13.9 ms frame budget. Changes require a recorded plan decision and matching validation.

### Cricket ground (M6.5, accepted 2026-10-24)

- **Ground centre and boundary:** ground centre (0, 0, 10.06). Boundary circle radius 65 m (allowed 55–75 m), with an optional ellipse (radius X and radius Z) for later.
- **Fielding circle:** the 30-yard circle is two 27.43 m semicircles centred on each middle stump, joined by straight lines and marked by discs about every 5 m.
- **Fielder angles:**
  - 0° points straight down the ground toward the machine (+Z), and positive angles are on the off side for a right-hander (+X).
  - The radius is measured from the striker's stumps.
  - World position = (r·sin θ, 0, r·cos θ).
  - For a left-hander, the angles mirror (θ → −θ).
- **Default field:**
  - Keeper 180°/12 m
  - Slip 160°/16 m
  - Point 95°/25 m
  - Cover 55°/28 m
  - Mid-off 20°/27 m
  - Mid-on −20°/27 m
  - Midwicket −60°/28 m
  - Square leg −95°/25 m
  - Third man 145°/50 m (GD-3)
  - Fine leg −150°/50 m (GD-3)
- GroundMode results (GD-8) and leaving the ground (GD-9) follow REQ-020 and REQ-021; decision record in `proposals/M6.5-cricket-ground.md`.

### Two modes (accepted 2026-10-26)

Decision record: `proposals/two-modes-replan.md` (conflicts C-1..C-22, decisions D-1..D-5).

- **D-1 Strike:** the player faces every ball in Match Simulation; odd runs don't change strike.
- **D-2 Wickets:** a wicket only reduces wickets in hand; that ball is a dot. Loss when wickets in hand reach 0.
- **D-3 Field:** one fixed field (the M6.5 FieldConfig) through M10; presets may follow after calibration.
- **D-4 Confidence in Match Simulation:** shown per ball as a secondary line; not part of the match result.
- **D-5 Leaves:** an unhit ball keeps flying under physics, so the stumps decide bowled. Past the stumps (z < −1 m): played and missed if the closest bat–ball gap < 0.20 m or the bat moved toward the ball faster than 3 m/s within ±0.15 s of it passing; otherwise well left. Both thresholds are settings.
- **Required rate** = runs needed ÷ (balls left ÷ 6), shown only while balls are left.
- **Scenario bounds:** required rate 6–15 per over, 6–36 balls, 1–5 wickets; seeded.
- **Difficulty presets** (starting values, calibrated): Easy 70–85 km/h, yorkers 5%, short balls 10%; Medium 80–100 km/h, 10% / 15%; Hard 95–120 km/h, 20% / 20%. Hard stays capped at 120 km/h until AC-04 is re-run for 100–120 km/h. Yorker = bounce 0.8–1.5 m from the striker's stumps.
- **Net Practice delivery mix:** lengths 3.5–9 m, lines −0.1 to +0.4 m (off side for a right-hander), 70–90 km/h.
- **Simulated ball after contact (Match Simulation):** follows the scorer's path from the exact contact position and exit velocity; sounds for landing, fielded, caught and crossing the rope.

## Proposed changes pending owner approval

- **PC-01 (affects REQ-009, AC-06): zone geometry and boundary exit.** **Accepted by the owner on 2026-10-24** ("scoring rules look fine"); now part of REQ-009 scope.
  - Front scoring zones sit on the front net wall behind the launcher end and are split by impact height: low < 1.0 m scores 2, 1.0–3.0 m scores 4, above 3.0 m scores 6.
  - Side and rear walls score 1.
  - The enclosure has no roof. A contacted ball that leaves over the front wall scores 6. A contacted ball that leaves over a side or rear wall scores 1. An unhit ball leaving the enclosure scores 0. All of these are terminal.
  - The launcher has no collider, so returning balls pass through it.
  - Details are in `plan.md` §6.
  - **Superseded 2026-10-26** for runs: Net Practice scores no runs (REQ-009 amended). The zone geometry remains as a "where it went" label.

## Acceptance scenarios

### AC-01 — On-device launch (REQ-001)

Given the project is built on the owner's Mac and installed on Quest, when the owner opens it independently, then the scene renders in stereo, head movement works, and both controller poses are available. The game does not depend on a connected Mac.

### AC-02 — Standing at the crease (REQ-002, REQ-015)

Given a valid floor-relative origin, when the player stands at the batting marker, then the crease, stumps, launcher, and net are proportionate and visible, the scoreboard does not obstruct delivery sight, and head motion is natural.

### AC-03 — Bat setup (REQ-003, REQ-007)

Given either handedness setting, when the player aligns and moves the bat, then it follows the selected controller consistently. After recentering or tracking recovery, pose history is reset and no false high-speed impact is possible.

### AC-04 — Reproducible delivery (REQ-004, REQ-005)

Given unchanged configuration and seed, when ten debug deliveries are launched separately, then exactly one ball is active at a time and bounce points remain within a 0.25 m radius of the first measured bounce point. Changing line/length changes the corresponding trajectory. An unreachable configuration produces an actionable error. This tolerance is a prototype default to review during M4.

### AC-05 — Contact matrix (REQ-006, REQ-007, REQ-015)

Given the deterministic delivery, test stationary-face block, soft defence, drive, loft, off-centre contact, and rotation-dominated bat-tip contact. Run ten scripted attempts for each applicable test at documented speeds/angles; no observed missed collision or duplicate impulse is allowed in that matrix. At comparable central contact, a faster aligned attack should produce more outgoing speed than soft defence. Repeat representative cases on Quest and have the owner assess feel. Scripted success does not imply all human swings are covered.

### AC-06 — Outcomes and honest metrics (REQ-008, REQ-009, REQ-010)

*(Amended 2026-10-26.)* In Net Practice each delivery finalizes once; a miss, a bowled ball and contacts into each net zone produce one record each, with no runs; carry is shown only when measured. A contact then bowled has both contact and wicket flags. *(E-030/E-031 remain valid for finalization and carry; their run assertions are superseded.)*

### AC-07 — Complete session (REQ-011, REQ-012, REQ-026)

A Net Practice session of 2 overs delivers exactly 12 balls, then the end screen, with totals matching the records. A Match Simulation scenario ends on the ball that reaches the target, or on the last ball or wicket, and never bowls an extra ball.

### AC-08 — Restart and menu (REQ-013)

From the countdown, an active ball, between balls and the end screen, "Play again" and "Menu" each leave no stale ball or callback. Repeat five times per mode.

### AC-09 — Interruption (REQ-014)

Given a countdown or active delivery in either mode, when app focus or required tracking is lost, then bowling stops. Any active incomplete delivery is cancelled without changing finalized results. After tracking returns, a player confirmation is required and the next attempt cannot have a velocity spike.

### AC-10 — Final device sessions (REQ-016, REQ-039)

Given the installed build and normal play conditions, complete three consecutive sessions per mode, both-handedness checks (left hand once the controller works), restart, and interruption cases. Record CPU/GPU frame timing against the target and any over-budget frames. Normal-play intervals should meet the selected frame budget; recurring misses are failures to investigate. Pending checks cannot be marked passed.

### AC-11 — Standing in the middle (REQ-017, REQ-018, REQ-024)

Given GroundMode, when the owner stands at the crease, then:
- the ground looks true to scale;
- both sets of stumps and all creases are visible without flicker;
- the rope, circle markers and stands don't shimmer when the head moves;
- the sight screen sits directly behind the machine and the ball is easy to pick up at release.

### AC-12 — Ground physics (REQ-019, REQ-020)

Given GroundMode:
- **Bowling:** the ten-launch bounce check still lands within the AC-04 limits, and adding the outfield moves the delivery bounce by less than 1 cm (as built, plan.md §15: the absolute error to the target was already 2.6–10.1 cm before M6.5).
- **Outfield:** a ball driven along the ground slows to a stop on the outfield, and a ball dropped or hit at up to 45 m/s never falls through it.
- **Leaving the ground:** a ball lofted over the rope raises `BallLeftGround` exactly once, then disappears 1.5 s later.

### AC-13 — Mode switch (REQ-021, REQ-022)

Given NetMode, all M6 behaviour and every existing test are unchanged. Given GroundMode:
- each delivery shows exactly one result (bowled, miss, or "hit, practice scoring pending"); an unhit wide ball past the stumps is a miss; an edge going behind the wicket is "hit, practice scoring pending", never a miss;
- the fielders appear at their configured positions and hide when toggled.

### AC-14 — Ground performance (REQ-023)

Given GroundMode on Quest 3 during normal batting for at least 2 minutes:
- the frame rate holds 72 Hz with no sustained drops;
- draw calls ≤ 150 and triangles < 250k (Unity Profiler on a development build);
- texture memory < 150 MB;
- the measured numbers are recorded with fixed foveated rendering on and off (the estimate in plan.md is not the pass);
- CPU and GPU frame times are recorded separately.

### AC-15 — Start menu (REQ-025)

With only the right controller, the owner selects each mode and each option by ray, and once by bat-tap. The ray and the bat are never shown at the same time.

### AC-16 — Per-ball record and log (REQ-027, REQ-028)

In a development build, a session writes one CSV row per finalized ball with every field filled for its mode, including the swing timing offset Δt. No planned delivery is unreachable; redraws are logged.

### AC-17 — Shot Confidence (REQ-029, REQ-030)

- **Automated:** fixed inputs give the documented component values; the edge cap applies; leaves are excluded; thresholds come from settings.
- **Headset:** the owner plays a middled drive, a high-on-blade drive, an edge, a block, a leave and a play-and-miss. Ratings rank middled drive > high-on-blade drive > edge; the block is rated without a Power penalty; the leave is excluded; the play-and-miss is 0.

### AC-18 — Net Practice summary (REQ-031)

The end screen values equal values recomputed from the CSV of the same session.

### AC-19 — Scorer physics (REQ-032, REQ-033)

**Automated:** the drag-free path matches the analytic range within 1%; drag shortens the range monotonically with k; bounce and roll match the formulas; the simulation is deterministic; boundaries give 6 and 4 correctly, including at the rope.

### AC-20 — Fielding (REQ-034)

**Automated, table-driven:** catches with and without enough time to spare (dropped); ground intercepts giving 0, 1, 2 and 3 runs; an unfielded stop; played-on against the physics stumps box; a ball hit behind the wicket.

### AC-21 — Calibration (REQ-035)

The owner's CSV of at least 100 contacts is replayed through the scorer. A distribution report (outcomes, carry, run histogram) is recorded, and every tuning change is justified against it (plan.md §17).

### AC-22 — Scenarios (REQ-036)

The same seed gives the same scenario; 1,000 random scenarios all fall within the bounds.

### AC-23 — Chase (REQ-037, REQ-038)

- **Automated:** each win/loss path ends exactly once. A PlayMode test plays back simulated paths and checks that the visible ball and the scored path match: the same first landing point and the same end position (within 2 cm), with no position jump or speed change at contact.
- **Headset:** the owner plays three scenarios. The scoreboard is always correct; the ball visibly goes where the result says (rope, catch, fielder) with no jump at contact; landing, fielded, caught and boundary sounds play.

### AC-24 — Performance in both modes (REQ-039)

Recorded numbers for both modes with foveation on and off, as in AC-14.

## Completion rule

All requirements must have mapped tasks and acceptance evidence. If a criterion fails, keep it open or revise it transparently with the owner's scope/intent in view; do not silently weaken it to make a test pass.
