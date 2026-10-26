# Proposal: Delivery Types module ("Balls"), milestone M7.5

Status: **ACCEPTED by the owner 2026-10-27**, with the changes below. Merged into spec.md, plan.md, tasks.md and validation.md on branch `m7.5-delivery` (2026-10-27).

**Branching (owner, 2026-10-27, supersedes change 1 and DT-14):** start M7.5 now, without waiting for the M7 re-check. `m7.5-delivery` branches from `m7-session` (not `main`). Once M7 passes and is merged into `main`, rebase `m7.5-delivery` onto `main`. Nothing merges into `main` until the owner confirms the M7 re-check. An M7 problem that affects M7.5 is fixed on `m7-session` first, then rebased. AC-15 (menu) is recorded as passed (E-039), so M7.5 evidence starts at E-040.

**Owner changes on approval:**
1. **Order** *(superseded the same day, see Branching above)*: finish the M7 headset re-check and merge M7 into `main`; then branch `m7.5-delivery` from `main` and commit this proposal and the merged docs there (not on `m7-session`).
2. **Bounce measurement (DT-3, T070):** a measurement change only. Re-run the Stock calibration tests with the new measurement and record the new accuracy in validation.md, with the reason the old ~6 cm figure changed. Tighten the Stock tolerance to about 1 cm **only if it actually passes**.
3. **Natural variation:** a seeded per-ball variation (default ±15%) on swing amount, drift and turn angle, recorded in the per-ball log. The same seed gives the same ball. Tests use 0 variation unless a test is about variation.
4. **Speed cap (DT-5):** if 140 km/h fails AC-04 or the AC-05 extension, keep the cap at 120 km/h and **scale the swing targets down** (not weaken the tests); report the result to the owner.
5. Recommendations D-1..D-5 accepted as written.
6. Stock behaviour stays identical throughout.

Source: the owner's brief "Plan a reusable Delivery Types ("Balls") module" (2026-10-27).

**Where the project stands:**
- M7 (session framework and start menu) is on the `m7-session` branch.
- The B-press control and the ground-mode timing fix are built, with automated checks passing (E-037, E-038).
- The owner's M7 Test 1 re-check is still pending, and the branch isn't merged.

**Conventions:** all directions are for a right-handed batter; off side = +X; deliveries travel −Z. Everything mirrors (x → −x) for a left-hander.

---

## 1. Conflicts with the current docs, code, tests and earlier decisions

| # | Conflict | Where | Recommendation |
| --- | --- | --- | --- |
| DT-1 | **Swing, seam and spin are explicitly deferred.** | CLAUDE.md §2 and §9 ("Keep seam and spin disabled for MVP"); spec.md "Deferred" | Owner instruction overrides; record it in the plan.md decisions log. Remove "spin/swing/seam" from the spec's Deferred list. Arcade-believable movement only; no aerodynamics claims. |
| DT-2 | **The solver only aims in one plane.** `DeliverySolver` aims straight at the bounce point and refines only the elevation, by bisection against a gravity-only stepped flight. Sideways forces (swing, drift) need the aim angle (yaw) solved too, with the same forces integrated. | Core/DeliverySolver.cs | Add a **movement-aware solve**. For **Stock** keep today's code path byte-for-byte, so existing calibration stays unchanged. For other types, do a 2-D shooting solve (yaw + elevation) that integrates exactly the forces the flight component applies (§4), to ≤ 1 mm in simulation. |
| DT-3 | *(Cause corrected by T070/T072, E-040/E-042: PhysX turns the ball up to one step either side of the pitch point depending on the step phase (−9.8 to +11.9 cm), not always early. Implemented as the pitch point (crossing of contact height, interpolated or extrapolated within the step); Stock within 0.51 cm; rebound offset recorded separately.)* **"Within about 1 cm" vs today's measured bounce.** The PlayMode bounce error is 2.6–10.1 cm today (E-032), even for Stock. The cause, now confirmed: PhysX's default contact offset is **0.01 m**, so the "first bounce" is reported while the ball is still about 1 cm above the pitch. On a ball descending at about 10° that is about 6 cm early (matching the 6.0 cm on the default delivery). The ball's physical path is fine; the *measurement* is early. | BallController.FirstBounce (contact point at OnCollisionEnter); BowlingMachine bounce error; AC-04 tests (≤ 10 cm) | **Measure the bounce where it physically happens:** the ball's lowest point, where vertical velocity changes sign, interpolated within the step. That can then be held to about 1 cm for every type, Stock included. The physics, the pitch material and the launch velocities don't change; only the reported bounce point and its error figure do. Existing AC-04 tests (≤ 10 cm) keep passing with smaller errors. |
| DT-4 | **The turn must happen at the real bounce.** OnCollisionEnter fires when the ball is within 1 cm of the pitch, still descending (DT-3). Changing the bounce height there would act on the falling ball. | BallController | Apply the turn and the bounce-height change in the first physics step after the vertical velocity becomes positive (the ball has bounced). Deterministic, and independent of PhysX friction and spin. |
| DT-5 | **Swing speeds vs the speed cap.** The brief's swing target is at 115–140 km/h, but Hard is capped at 120 km/h until AC-04 is re-run for 100–120 km/h (C-14). The AC-05 contact matrix is tested at 20–24 m/s balls (≈ 72–86 km/h). | spec.md "Two modes", AC-04, AC-05 | Phase A includes an **AC-04 re-run to 140 km/h** and an **AC-05 matrix extension to 39 m/s** with swinging and spinning balls. The cap rises to 140 km/h only when both pass. Until then pace types clamp their range at 120 km/h, logged; requests are never silently changed, because the type's range itself is clamped. |
| DT-6 | **`DeliveryConfig` vs `DeliveryRequest`.** The brief makes `DeliveryRequest` the only way to ask for a ball. Today `DeliveryConfig` (label, speed, release, line, length) is used by the bowling machine, the debug presets, the M7 `DeliveryPlanner`, the per-ball record (CSV) and the tests. | Core, Runtime, Tests | `DeliveryRequest` = `DeliveryConfig` fields + type + movement 0–1 + handedness + line reference + seed. A Stock request with movement 0 converts both ways with `DeliveryConfig`, so every existing caller and test keeps working. New features use `DeliveryRequest` only. The record gains type and movement columns. |
| DT-7 | **Mirroring happens in two places.** The M7 planner already mirrors the line for left-handers. The brief wants one handedness setting in the request that mirrors the line **and** the movement. | DeliveryPlanner | Move all mirroring into one function, `DeliveryRequest.ForBatter()`, applied once by the bowling machine. The planner then emits right-handed requests plus a handedness. A test checks the exact symmetry. |
| DT-8 | **Visual spin reveals the variation.** A realistic seam shows the true spin axis after release, so a googly would visibly spin like an off-break. The brief wants no clue before release, and keeping the deception is the point. | brief §5 | **Before release** the machine gives no clue (it has no hand or wrist; speeds and release are the same for a stock ball and its variation). **After release**, by default the variation's seam *shows its stock ball's rotation* (the googly looks like a leg-break) until the bounce; a setting `honestSeam` shows the true axis (for learning). The owner decides (D-3). |
| DT-9 | **The ball has no seam today.** It is a plain white sphere whose rotation comes from the Rigidbody. | BowlingBuilder (ball) | Split the ball: the physics sphere keeps its collider and rigidbody (rotation unused); a child `BallVisual` carries a generated seam texture and its own rotation, driven by the flight component. No physics change. |
| DT-10 | **Strobing.** Real spin of 20–30 rev/s at 72 Hz is 100–150° per frame. A seam pattern repeats every 180°, so it would seem to spin backwards or stand still. | brief §5 | Visual rotation capped at **≤ 25° per frame** (5 rev/s at 72 Hz). Spinners show 4 rev/s and pace balls 3 rev/s of backspin with a steady, upright seam. Section 5 has the details. |
| DT-11 | **Delivery plans.** The M7 Net Practice and Easy/Medium/Hard plans draw only speed, line and length. | DeliveryPlanner, spec.md "Two modes" | Plans gain a **type mix** (weights per type, each with the type's own speed range). The existing plans become "Stock only", so M7 behaviour is unchanged until the M8/M10 mixes are switched on. |
| DT-12 | **The CLAUDE.md "no drag" MVP decision.** | plan.md §9 | Still no drag before contact. Swing, drift and dip are explicit accelerations, not drag. |
| DT-13 | **What the swing target means.** "0.2–0.6 m of sideways movement" needs a reference. Because the solver aims the bounce at the requested line, a swinging ball starts wider and curves in. | brief §2 | **Swing deviation** = the lateral distance between the actual path and the straight line from release to the bounce point, at its largest before the bounce (how much it curved), plus the arrival line at the batter (§3, D-1). Both are logged. The 0.2–0.6 m target applies to the curve. |
| DT-14 | **M7 is not merged.** The owner's M7 re-check is pending. | validation E-037/E-038 | *(Superseded by the owner 2026-10-27: branch from `m7-session` now and rebase onto `main` after M7 merges.)* |
| DT-15 | **Numbering.** The brief says to continue from REQ-040, AC-25 and T070. | spec.md (REQ-039, AC-24 last); tasks.md (T069 last) | No renumbering needed: REQ-040..REQ-048, AC-25..AC-31, T070..T083 (plus T072a). |

---

## 2. Owner decisions (recommendations)

**D-1 What "line" means.**
**Recommendation: support both, with the bounce line as the default.**
- **Bounce line (default, `LineReference.AtBounce`):** `line` = the x where the ball pitches, as today. The solver is exact here (≤ 1 mm in simulation), and Stock is unchanged.
- **Arrival line (`AtBatter`):** `line` = the x where the ball reaches the batter's popping crease (z = 1.22 m), for example "pitches on middle, finishes outside off". The solver then also simulates the bounce and turn, using the same turn rule as the game (§4) and the pitch restitution measured in M4. It is accurate to about **±3 cm**, because the after-bounce physics is PhysX. Used by drills and the M8 learning mixes.

**D-2 Seam movement after the bounce for pace balls.**
**Recommendation: not in v1.** The field exists in `DeliveryType` with a default of 0°. Real seam movement looks random; deterministic seam movement would look like small spin, which muddies the readable difference between swing and spin. Revisit after the headset test.

**D-3 Deception vs honest seam (DT-8).**
**Recommendation: deceptive by default.** A variation shows its stock ball's rotation until the bounce. Net Practice has a "Learning: show the true seam" option.

**D-4 Visual spin rate (DT-10).**
**Recommendation:** spinners 4 rev/s (20° per frame); pace 3 rev/s backspin (15° per frame); the cap is 5 rev/s. The direction is correct, and the speed is a readable stand-in rather than the real rate.

**D-5 Incoming spin affecting the shot (v2).**
**Recommendation:** v1 has none (documented simplification). For v2: a small, bounded tangential term in the response, `v_out += c_spin · (ω̂ × n) · min(1, spinRate / 25 rev/s) · |v_n|`, with c_spin ≈ 0.03–0.06. An off-break then tends to come off the face toward leg, and a leg-break toward off. It must not change the edge or sweet-spot rules. It goes on the open questions list for after M8 calibration.

---

## 3. Text for spec.md

### Scope
- Remove "spin/swing/seam" from Deferred.
- Add: *"Delivery types: Stock, inswinger, outswinger, off-spin, leg-spin, doosra and googly, from one reusable module used by both modes; arcade-believable movement, not an aerodynamics simulation (owner instruction 2026-10-27)."*

### New requirements

| ID | Requirement | Milestone |
| --- | --- | --- |
| REQ-040 | Every ball is requested with one `DeliveryRequest` (type, speed, length, line, line reference, movement 0–1, batter handedness, seed). Delivery plans, difficulty presets, tests and tools use only this. Stock with movement 0 behaves exactly as before M7.5. | M7.5 |
| REQ-041 | Seven delivery types are defined as `DeliveryType` assets (display name, description, pace or spin, speed range, and movement settings). Every movement value is a setting with documented allowed ranges. | M7.5 |
| REQ-042 | Swing (inswinger toward −X, outswinger toward +X for a right-hander) is a deterministic sideways acceleration before the bounce, optionally growing during the flight (late swing), giving a 0.2–0.6 m curve at 115–140 km/h. If 140 km/h fails AC-29, pace stays capped at 120 km/h and the swing targets are scaled down instead of weakening the tests. | M7.5 A |
| REQ-043 | Spin adds deterministic air drift (opposite to the turn) and dip (extra downward acceleration; the googly has the most), plus a defined turn applied at the bounce:<br>• a sideways velocity rotation (off-spin 3–6° toward −X; leg-spin 4–9° toward +X; doosra like leg-spin but 2.5–5°; googly like off-spin but 3.5–7.5°);<br>• a bounce-height factor.<br>It is applied in the step after the ball physically bounces, never through PhysX friction. The pitch material is unchanged. | M7.5 B |
| REQ-044 | The solver includes every movement force (with the ball's own variation applied before solving). A reachable request lands within 1 cm of its bounce point in simulation. In PlayMode physics, measured at the ball's lowest point (DT-3), it lands within the Stock tolerance: about 1 cm if T070 shows Stock achieves it, otherwise the measured Stock accuracy, for every type. With an arrival-line request it reaches the crease line within about 3 cm. Unreachable requests are rejected, never altered. | M7.5 |
| REQ-045 | The per-ball record and CSV add: delivery type, requested movement, predicted and measured swing deviation, measured turn angle, the arrival line at the crease, and the measured bounce point and its error. Nothing outside the module depends on a specific type. | M7.5 |
| REQ-046 | The ball shows a seam and rotation matching the delivery, at a readable rate (≤ 5 rev/s visual, no strobing at 72 Hz). Variations look like their stock ball until the bounce unless the honest-seam option is on. The machine shows no difference before release. | M7.5 B |
| REQ-047 | Left-handed batting mirrors every line and movement exactly through the request's handedness (unverified on device while the left controller is faulty). | M7.5 |
| REQ-048 | Natural variation: each ball's swing amount, drift and turn angle vary by a seeded random factor within ±v (setting, default 15%). The applied factors are in the per-ball record. The same request seed gives the same ball. Variation 0 gives exactly the nominal movement. | M7.5 |

### Amended requirements
- **REQ-028 (delivery plans):** plans draw a type from a **type mix**, then speed (from the type's range), length and line. The existing plans become Stock-only until M8 and M10 switch their mixes on.
- **REQ-029 / REQ-031 (M8):** the Net Practice menu offers a delivery mix (All, Pace, Spin, one type, Variations), shows the type after each ball (optionally before), and the summary includes Shot Confidence by delivery type.
- **REQ-036 / REQ-037 (M10):** difficulty presets define delivery mixes; a scenario may set a bowler style for an over; the type is revealed only after the ball.

### New acceptance scenarios

### AC-25 — One request path, Stock unchanged (REQ-040, REQ-041, REQ-045)
- **Automated:** every existing calibration and contact test passes unchanged with Stock requests.
- **Automated:** every `DeliveryType` asset value is within its allowed range.
- **Automated:** the CSV carries the new columns.
- **Automated:** no code outside the module branches on a delivery type (a test enumerates the type-specific code).

### AC-26 — Swing (REQ-042, REQ-044)
- **Automated:**
  - deterministic paths;
  - in- and outswing move toward −X / +X for a right-hander and mirror exactly for a left-hander;
  - over 10 PlayMode runs per type at 115, 125 and 140 km/h: the curve is within the type's range and the bounce within about 1 cm of the request.
- **Headset:** the owner sees each swing type's movement and its direction.

### AC-27 — Spin (REQ-043, REQ-044)
- **Automated:**
  - turn and drift directions per type, both hands;
  - measured turn within range over 10 runs per type;
  - the googly dips and bounces higher than leg-spin at the same speed and length;
  - every spin speed in 75–95 km/h reaches lengths 3–9 m.
- **Headset:** the owner tells off-spin from leg-spin by its movement.

### AC-28 — Deception and visuals (REQ-046)
- **Headset:** the doosra and googly look like their stock balls until they turn (deceptive mode); the seam reads correctly with no strobing.
- **Automated:** the visual rate is ≤ 25° per frame at 72 Hz.

### AC-29 — Speeds and contact (REQ-042, REQ-044; re-runs AC-04/AC-05)
- AC-04 bounce consistency at 100, 120 and 140 km/h.
- The AC-05 contact matrix with balls up to 39 m/s, including swinging and spinning balls: exactly one contact each, no tunnelling.
- The 140 km/h cap is raised only after this passes.

### AC-31 — Natural variation (REQ-048)
**Automated:**
- the same seed gives an identical path and identical recorded factors;
- over 1,000 seeds the factors stay within ±v and are spread across the range;
- variation 0 gives exactly the nominal movement.

All other type tests run with variation 0.

### AC-30 — Performance with types (REQ-039)
72 Hz holds with the ball visual and flight component active (VrApi `App=` GPU and `CPU&GPU=` recorded).

---

## 4. Text for plan.md: new §19 "Delivery types (M7.5)"

**Module (Core + Runtime).**
- `DeliveryTypeData` (Core struct) and `DeliveryType` (ScriptableObject wrapping it, one asset per type under `Assets/CricketVR/Settings/DeliveryTypes/`).
- `DeliveryRequest` (Core):
  - `ToConfig` / `FromConfig` for Stock;
  - `ForBatter(handedness)` mirrors x and every movement sign.
- `DeliveryFlightModel` (Core, pure):
  - `Acceleration(t, velocity, plan)` gives the extra acceleration before the bounce;
  - `TurnAtBounce(velocity, plan)` gives the post-bounce velocity.
  - Used identically by the solver and at runtime.
- `BallFlight` (Runtime, on the ball):
  - applies `Acceleration` with `AddForce(…, ForceMode.Acceleration)` in FixedUpdate before the bounce;
  - detects the physical bounce (vertical velocity changes sign);
  - applies `TurnAtBounce` in that step;
  - drives `BallVisual` rotation.
  - Execution order is before the contact resolver (−40), so the resolver sees the step's real velocity.
- Stock = zero acceleration and no turn: `BallFlight` does nothing, and the solver uses today's code path.

**Flight model** (per type; m = movement 0–1; s = +1 right-hander / −1 left-hander; X̂ = world +X; t = time since release; T = the solver's planned time to the bounce).

| Effect | Formula | Notes |
| --- | --- | --- |
| Swing | a = s·σ·A·(t/T)^p · ŝ, with ŝ = the horizontal unit vector ⟂ to the velocity, on the side of σX̂ | σ = −1 inswing, +1 outswing. A is set so the curve equals D(m) = D_min + m·(D_max − D_min): A = D·(p+1)(p+2)/T². p = 0 uniform, p = 2 late. |
| Drift (spin) | a = −s·τ·d · ŝ, constant before the bounce | τ = turn direction (−1 off-spin/googly, +1 leg-spin/doosra). Drift is opposite to the turn. d gives 0.05–0.15 m of drift by the bounce. |
| Dip (spin) | a = −k_dip · Ŷ, constant before the bounce | Top-spin dip. The solver compensates with a higher launch, so the ball arrives steeper and bounces higher. |
| Turn at the bounce | v_h′ = R_Y(s·τ·θ(m)) · v_h · (1 − g_grip); v_y′ = v_y · (1 + b) | θ(m) = θ_min + m·(θ_max − θ_min); b = bounce-height factor; g_grip = speed lost to grip. Applied once, in the step after the physical bounce. |
| Seam (pace, D-2) | v_h′ = R_Y(φ) · v_h | φ = 0 in v1. |

**Natural variation (owner change 3).**
- Each ball draws factors f_swing, f_drift, f_turn ∈ [1 − v, 1 + v] from `System.Random(request.seed)`, with v = `DeliveryTypeData.variation` (default 0.15).
- They multiply D, d and θ before the solve, so the solver still lands the ball where requested.
- The factors are written to the record (`var_swing`, `var_drift`, `var_turn`).
- Tests set v = 0 except the variation tests.

**Starting values per type** (all calibrated; §17 table):

| Type | Speed (km/h) | Swing D (m) / p | Drift d (m) | Dip k (m/s²) | Turn θ (°) / dir | Bounce b | Grip |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Stock | plan's range | 0 | 0 | 0 | 0 | 0 | 0 |
| Inswinger | 115–140 (120 cap, DT-5) | 0.2–0.6 / 1.5 | 0 | 0 | 0 | 0 | 0 |
| Outswinger | 115–140 (120 cap) | 0.2–0.6 / 1.5 | 0 | 0 | 0 | 0 | 0 |
| Off-spin | 80–92 | 0 | 0.05–0.10 (→ +X) | 0.6 | 3–6 / −X | +0.05 | 0.05 |
| Leg-spin | 75–90 | 0 | 0.06–0.12 (→ −X) | 0.8 | 4–9 / +X | +0.08 | 0.06 |
| Doosra | 80–92 (same as off-spin) | 0 | 0.05–0.10 (→ −X) | 0.6 | 2.5–5 / +X | +0.03 | 0.05 |
| Googly | 75–90 (same as leg-spin) | 0 | 0.06–0.12 (→ +X) | 1.2 | 3.5–7.5 / −X | +0.12 | 0.06 |

**Solver (DT-2).**
- *Stock:* today's `Solve`, unchanged.
- *Moving types:* shooting on (yaw, elevation).
  - Start from the no-movement solution.
  - Integrate gravity plus `Acceleration` with the same semi-implicit Euler step as PhysX (v += a·dt, then x += v·dt), until the ball's lowest point.
  - Correct yaw by the lateral miss over the distance, and elevation by the existing bisection on range.
  - Stop at < 1 mm, at most 20 outer iterations.
  - T (needed by swing) comes from the current iterate, so the converged plan is self-consistent.
- *Arrival line (D-1):* the same, then continue through `TurnAtBounce` with the measured pitch restitution to z = 1.22 m and correct yaw on the arrival miss (tolerance about 3 cm).
- *Unreachable:* rejected with the reason (speed too low for the length, or too much movement to reach the line).

**Bounce measurement (DT-3).**
- `BallController` reports the first bounce at the ball's lowest point: the step where v_y changes from negative to positive, interpolated in position.
- `OnCollisionEnter` still marks the event for the delivery logic.
- The machine's bounce error uses this point.
- The physics, the pitch material and the launch velocities are unchanged.

**Visual ball (DT-9, DT-10, D-3, D-4).**
- `BallVisual` is a child mesh with a generated seam texture. It is rotated by `BallFlight` at the visual rate about the type's display axis:
  - **pace:** backspin about the axis ⟂ to the seam plane, with the seam upright and angled toward fine leg (inswing) or slip (outswing);
  - **spin:** about the stock ball's axis (deceptive) or the true axis (honest), switching to the true axis at the bounce.
- The rate is at most 25° per frame.

**Contact and after.** No change to the swept contact or the response; they read the ball's velocity, which already includes any movement. Incoming spin has no effect on the shot in v1 (D-5 for v2). The M9 scorer and the M10 simulated path are unaffected.

**Plans and UI.** `DeliveryPlanSettings` gains a `typeMix` (type → weight).
- **Net Practice mixes:**
  - All: equal weights over the seven types.
  - Pace: Stock, In, Out.
  - Spin: Off, Leg, Doosra, Googly.
  - One type.
  - Variations: 60% stock spin, 40% doosra/googly.
- **Match difficulty mixes:**
  - Easy: Stock 80%, In/Out 10% each.
  - Medium: Stock 40%, In/Out 20% each, Off/Leg 10% each.
  - Hard: In/Out 25% each, Off/Leg 15% each, Doosra/Googly 10% each.
- **Scenarios (M10)** may set "spinner" or "pace" for an over, which restricts the mix to that style.
- **Reveal:** Net Practice shows the type after the ball (before, optionally). Match Simulation shows it after the ball only.

**Calibration table additions (§17).**

| Setting | Asset / field | Starting value | How it is measured | Checked by |
| --- | --- | --- | --- | --- |
| Swing curve range | DeliveryType.swingMin / swingMax | 0.2 / 0.6 m | Measured curve (CSV) vs what the owner sees; visible but hittable | AC-26 |
| Swing lateness | DeliveryType.swingLateness p | 1.5 | Owner's sense of "late swing"; position along the flight where half the curve has happened | AC-26 |
| Drift | DeliveryType.driftMin / driftMax | per table | Measured drift by the bounce; visible but subtle | AC-27 |
| Dip | DeliveryType.dip | 0.6–1.2 m/s² | Arrival angle and bounce height vs Stock at the same speed and length | AC-27 |
| Turn range | DeliveryType.turnMin / turnMax | per table | Measured turn (CSV); off-spin vs leg-spin distinguishable in the headset | AC-27 |
| Bounce factor | DeliveryType.bounceFactor | per table | Bounce height at the crease vs Stock | AC-27 |
| Grip loss | DeliveryType.grip | 0.05–0.06 | Speed after the bounce vs Stock | AC-27 |
| Visual spin rate | DeliveryType.visualRevPerSecond | 4 (spin) / 3 (pace) | Owner: reads as spinning the right way, no strobing | AC-28 |
| Pace speed cap | DeliveryPlanSettings | 120 → 140 km/h | AC-04 re-run and AC-05 matrix extension; on failure stay at 120 and scale the swing targets down | AC-29 |
| Natural variation | DeliveryTypeData.variation | ±15% | Owner: balls feel varied but each type stays recognisable | AC-31, AC-26/27 |
| Stock bounce tolerance | AC-04 tests | 10 cm → ~1 cm only if T070 passes | T070 lowest-point measurement over the presets | AC-04, T070 |

**Testing.** As AC-25..AC-30. The PlayMode type tests use the shipped `DeliveryType` assets and pitch material, so re-tuning them is caught.

**Decisions log rows:**
- 2026-10-27 — Delivery Types module (M7.5) — owner request; overrides the CLAUDE.md deferral of swing/seam/spin.
- 2026-10-27 — The bounce is measured at the ball's lowest point (contact offset 0.01 m made it ~6 cm early).
- 2026-10-27 — D-1..D-5 as resolved.

---

## 5. Text for tasks.md: new "M7.5 — Delivery types", between M7 and M8

Dependency: M7 code (branch `m7-session`); rebased onto `main` once M7 is merged. Requirements: REQ-040..048 (plus REQ-028 amended). Acceptance: AC-25..AC-31.

### Phase 0 — Foundation
- [ ] T070 Bounce measured at the ball's lowest point (DT-3), as a measurement change only (physics, materials and launches unchanged). Re-run the Stock calibration tests and record the before/after accuracy per preset in validation.md, with the reason the old ~6 cm changed. Tighten the Stock tolerance to about 1 cm only if every preset passes it; otherwise record the measured accuracy and keep the tolerance.
- [ ] T071 `DeliveryTypeData` / `DeliveryType` assets (seven types, starting values), `DeliveryRequest` (Stock ↔ `DeliveryConfig`, `ForBatter` mirroring), and plans with a type mix (existing plans Stock-only). EditMode tests: asset value ranges, exact mirror symmetry, Stock request = existing config.
- [ ] T072 Record and CSV columns (type, movement, predicted/measured curve, turn, arrival line, measured bounce and error, variation factors). Round-trip test.
- [ ] T072a Natural variation (REQ-048): seeded per-ball factors on swing, drift and turn (default ±15%), recorded; the solver applies them before solving. Tests (AC-31): same seed same ball; bounds over 1,000 seeds; 0 = nominal. Every other type test uses variation 0.

### Phase A — Swing
- [ ] T073 `DeliveryFlightModel.Acceleration` (swing) and the movement-aware solver (yaw + elevation). EditMode tests: determinism, direction per type and hand, simulated bounce within 1 mm, unreachable rejection.
- [ ] T074 `BallFlight` runtime (forces before the bounce) and seam-up visual for pace. PlayMode: 10 runs × in/out × 115/125/140 km/h (140 only behind the cap test): curve in range, bounce within about 1 cm.
- [ ] T075 AC-04 re-run at 100/120/140 km/h; extend the AC-05 contact matrix to 39 m/s with swinging balls (AC-29). Raise the pace cap only if both pass. If either fails at 140 km/h: keep 120 km/h, scale the swing targets down for 115–120 km/h, keep the tests as written, and report the result to the owner.
- [ ] T076 Headset: AC-26 (see the swing, both directions).

### Phase B — Spin
- [ ] T077 Drift and dip in the flight model and solver. Tests: drift opposite to turn; googly dips most; spin speeds 75–95 km/h reach lengths 3–9 m.
- [ ] T078 Turn at the physical bounce (`TurnAtBounce`, bounce factor, grip), applied in the step after v_y > 0. PlayMode: 10 runs × 4 spin types × both hands: turn in range, direction correct, bounce point unchanged by the turn, googly bounces higher than leg-spin.
- [ ] T079 Arrival-line requests (D-1). Test: crease line within about 3 cm for every type.
- [ ] T080 Spin visual: seam texture, readable rate (≤ 25°/frame), deceptive/honest seam (D-3). Test: rate limit; the variation's display axis equals its stock ball's axis until the bounce.
- [ ] T081 Contact matrix with spinning balls (AC-29); performance check with the ball visual (AC-30).
- [ ] T082 Headset: AC-27 and AC-28 (off-spin vs leg-spin, deception, no strobing, 72 Hz).
- [ ] T083 Calibration: tune the §17 type rows from the CSV and the owner's feedback; record before/after values.

**M8 / M10 tasks amended:**
- T056 (Net Practice) adds the delivery-mix menu option, type reveal (after; optional before) and confidence by type in the summary.
- T065 (Match difficulty) adds delivery mixes and per-over bowler style (with T062 scenarios).

---

## 6. Template additions for validation.md

Register rows AC-25..AC-31 as "Not run". Planned evidence starts at E-040 (T070 bounce measurement; E-039 is the AC-15 menu pass).

Extra fields:
- **Swing/spin runs:** type, hand, speed, requested length/line/movement; measured bounce and its error; curve (predicted/measured); drift; turn angle; arrival line; the 10-run range.
- **Bounce measurement change (T070):** per preset, the old error (contact report) and the new error (lowest point).
- **Headset (AC-26..28):** for each type, whether the movement was seen and its direction; whether off-spin and leg-spin could be told apart; whether the deception held; any strobing.
- **AC-29:** speeds tested; contact matrix counts per speed and type; the cap decision.

---

## 7. Open questions (to keep in plan.md)
1. Incoming spin effect on the shot (D-5), v2 after M8 calibration.
2. Seam movement for pace (D-2), revisit after the headset test.
3. "Flighted" spin (the solver's higher, loopier root) as an option for slower, loopier deliveries, if the flat trajectories feel machine-like.
4. Reverse swing (late inswing of an old ball) as a later variation of the swing types: it needs only different settings.
