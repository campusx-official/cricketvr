# Implementation tasks

Status: M1 implemented, AC-01 partial (left controller hardware). M2 complete (AC-02 passed). M3 complete for the right controller (AC-03 pass; left hand pending hardware). M4 complete (AC-04 passed on Quest). M5 complete (AC-05 passed on Quest, right controller). M6 complete (AC-06 passed on Quest in NetMode). M6.5 T033–T045 implemented with automated checks; AC-11 passed; T046 (AC-14 performance) and the AC-12/AC-13 headset checks (M6.5 Test 2) pending, then the merge into `main`. Re-planned 2026-10-26 into two modes: M7 session framework and start menu, M8 Net Practice, M9 virtual-field scorer, M10 Match Simulation, M11 polish (T047–T069; old T025–T032 retired). This is a task specification, not evidence of a built game.

Use `[ ]` for incomplete and `[x]` only for the named completed work. Keep device acceptance pending in `validation.md` when unavailable. Dependencies apply in the order shown. Automated preparatory work may continue while waiting for a physical check, but do not represent untested prerequisites as accepted.

## M1 — First Quest scene

Requirements: REQ-001. Acceptance: AC-01.

- [x] T001 Inspect repository instructions, Git state, Mac/macOS, Unity installations, Android modules, and current packages without disturbing existing work.
- [x] T002 Record exact compatible Editor/package versions and prerequisites; install/configure available tooling and provide owner-controlled account/device steps.
- [x] T003 Create/open the URP project; configure Android/Quest ARM64, OpenXR, controller profile, Input System, and XRI.
- [x] T004 Add a single XR Origin, floor, reference objects, and visible tracked controllers. Validate required scene dependencies.
- [x] T005 Run compilation/project validation and produce/install a development build using available tools. Record actual commands/results. *(2026-10-23: E-001–E-003.)*
- [ ] T006 Perform AC-01 on Quest or leave it pending with exact instructions. Add reproducible project setup/build notes to README.

## M2 — Practice net

Dependency: M1 implementation; headset scale acceptance requires device access. Requirements: REQ-002, REQ-015. Acceptance: AC-02.

- [x] T007 Establish metre scale, axes, batting/launcher anchors, and floor-origin conventions.
- [x] T008 Add primitive pitch, crease, striker stumps, inexpensive net boundaries, simple lighting, and scoreboard placement.
- [x] T009 Verify AC-02 at the real batting stance and record any alignment changes.

## M3 — Tracked bat

Dependency: M2. Requirements: REQ-003, REQ-007. Acceptance: AC-03.

- [x] T010 Add selected-controller binding, grip offsets, handedness selection, bat visual, and kinematic physics proxy.
- [x] T011 Implement timestamped pose history, contact-point motion calculation, validity checks, and history reset after discontinuities.
- [x] T012 Add alignment/debug controls and verify AC-03 for both hands on Quest.

## M4 — Bowling machine

Dependency: M3 setup. Requirements: REQ-004, REQ-005. Acceptance: AC-04.

- [x] T013 Add DeliveryConfig, speed conversion, initial-velocity calculation, and unreachable-configuration handling.
- [x] T014 Implement one reusable ball with gravity/bounce and complete reset of velocity, trail, flags, and timers.
- [x] T015 Add repeatable debug launch controls and bounce measurement; run AC-04's ten-delivery check and record configuration/tolerance results.

## M5 — Bat–ball contact

Dependency: M4. Requirements: REQ-006, REQ-007, REQ-015. Acceptance: AC-05 plus relevant AC-03 checks.

- [x] T016 Configure primitive colliders, layers, supported CCD, and one contact-resolution authority.
- [x] T017 Implement bounded, orientation-aware response using relative/contact-point velocity and off-centre tuning.
- [x] T018 Add focused sweep/substep handling if measured CCD gaps require it; handle overlaps and duplicate callback/sweep encounters.
- [x] T019 Add impact audio, modest selected-controller haptics, and optional contact debug markers.
- [x] T020 Run scripted contact matrix and owner headset checks; record speeds, angles, counts, failures, and feel adjustments.

## M6 — Outcomes and scoring

Dependency: M5. Requirements: REQ-008, REQ-009, REQ-010. Acceptance: AC-06.

- [x] T021 Implement typed DeliveryResult and identity-based single finalization, terminal rules, timeout, and stale-event rejection.
- [x] T022 Define nonoverlapping practice zones, priority, no-contact rules, wicket zero-score behavior, and zero-run fallback.
- [x] T023 Record measured carry only on a real post-contact ground landing; expose net-first/N/A state.
- [x] T024 Add meaningful scoring/outcome tests and run AC-06, including contact-then-bowled and competing terminal signals.

## M6.5 — Cricket ground

Dependency: M6 committed. Requirements: REQ-017 to REQ-024. Acceptance: AC-11 to AC-14.

- [x] T033 Add `GroundDefinition` and `FieldConfig` ScriptableObjects with defaults, plus EditMode tests:
  - pitch 20.12 m;
  - popping crease at 1.22 m;
  - creases and stumps equal to FieldDimensions;
  - 30-yard circle shape (semicircles + joining lines, point spacing ~5 m);
  - fielder polar → world conversion and the left-hand mirror;
  - every fielder in FieldConfig at least 3 m inside the rope at its own angle, for the configured boundary (owner addition).
- [x] T034 Add the `Outfield` layer, the `OutfieldGrass` physics material and the collision matrix entry. Treat Pitch or Outfield as ground in the first-bounce and carry checks. Existing tests must still pass. *(test)*
- [x] T035 Add `EnvironmentModeSwitch` and `GameConfig.environment`, move the net objects under `Environment/PracticeNet` (scene builder migration) and add the GroundMode build menu. All existing tests must pass in NetMode. *(test)*
- [x] T036 `GroundBuilder`: outfield disc with a generated stripe texture, and the outfield collider. *(headset: stripes visible, no shimmer)*
- [x] T037 Pitch strip mesh with crease markings painted into its texture, crease extensions (GD-4), and non-striker stumps and bails. *(headset: both ends readable, no flicker)*
- [x] T038 Boundary rope tube and 30-yard circle disc mesh. *(headset: rope continuous at 65 m, circle discs visible and stable)*
- [x] T039 Sight screens (GD-2), advertising boards, stands with vertex-colour crowd and AO, a gap behind the screens. *(headset: screen behind the machine, ball easy to pick up)*
- [x] T040 Sky dome, fog and URP shadow settings (GD-12). *(headset: far stands soft, horizon stable)*
- [x] T041 Fielder placeholders from `FieldConfig` with a hide toggle. *(headset + test: positions match config)*
- [x] T042 `BallRollingDrag` on the outfield only. *(PlayMode: a rolling ball stops on the outfield; a ball on the pitch is unaffected)*
- [x] T043 `GroundBoundaryMonitor` and `BallLeftGround`, with hide after 1.5 s, plus the safety despawn below the outfield or beyond 150 m. *(PlayMode: raised once, ball hidden; safety despawn fires)*
- [x] T044 GroundMode terminal rules in ScoreManager (GD-8). *(EditMode + PlayMode: bowled / miss / pending exactly once; unhit wide ball past z < −1 m is a miss; hit edge behind the wicket is pending, never a miss; unhit ball that stops is a miss; stumps beat everything in the same step)*
- [x] T045 PlayMode physics checks:
  - bowling calibration still within 1 cm with the outfield present;
  - 45 m/s balls at 10 angles never pass below the outfield top;
  - NetMode M6 scoring tests unchanged.
- [ ] T046 Turn on fixed foveated rendering (done in code, E-032). Make a development build and record the measured draw calls, triangles, texture memory, and CPU/GPU frame time on Quest with foveation on and off (AC-14). Owner headset checks AC-11 (passed, E-033), AC-12 and AC-13 (M6.5 Test 2). Then merge `m6.5-ground` into `main`; only then start M7.

## Retired tasks (superseded 2026-10-26)

The old M7 six-ball session and M8 polish tasks are replaced by the two-mode plan below. Kept for history, not to be implemented as written:

- ~~T025 Implement guarded session transitions, countdown, intervals, six-result limit, and cancellation generation/token.~~
- ~~T026 Add Start, live scoreboard, six-row results, derived totals, and Restart.~~
- ~~T027 Handle focus/tracking loss, interrupted-ball cancellation without consumption, explicit readiness recovery, and motion-history reset.~~
- ~~T028 Test a full session, five restarts across phases, and interruptions; verify no seventh ball, stale callback, or corrupted total.~~
- ~~T029 Improve visibility, menu readability, audio/haptics balance, and actionable error messages without expanding scope.~~
- ~~T030 Profile on Quest against the selected rendering target; document CPU/GPU measurements and optimize demonstrated bottlenecks.~~
- ~~T031 Complete three sessions, both-handedness checks, restart/recovery, and regression scenarios on the identified build.~~
- ~~T032 Update README, validation status, known limitations, and requirement coverage. Claim MVP completion only when evidence supports it.~~

## M7 — Session framework and start menu

Dependency: M6.5 complete and merged. Requirements: REQ-008, REQ-011..014, REQ-021, REQ-025..028. Acceptance: AC-07, AC-08, AC-09, AC-15, AC-16.

- [ ] T047 Session state machine in Core (Menu, Countdown, Delivering, Resolving, BallPanel, Interval, Paused, SessionEnd), with a generation token, a ball budget and an early end. EditMode tests for every transition, restart and menu from each state, and pause without consuming a ball.
- [ ] T048 `BallRecord` in Core and the per-session CSV writer (development builds; includes Δt, swing data and closest gap). Test: CSV round trip, one row per finalized ball.
- [ ] T049 Delivery plans (Net Practice mix; Easy/Medium/Hard), solver-checked with redraw and log, never altering the requested speed. Test: 1,000 draws per plan are all reachable; redraws are counted.
- [ ] T050 World-space start menu: right-controller XRI ray (menus only), bat-tap fallback by blade distance, bat hidden in menus. Options: mode; overs 1/2/3/5 (default 2); scenario preset or random; difficulty. *(headset: AC-15)*
- [ ] T051 Wire both modes to the framework with interim results (Net: hit/miss/bowled; Ground: pending), the per-ball panel, the end screen, Play again / Menu / New scenario, and pause/resume. *(headset: AC-07, AC-08, AC-09)*
- [ ] T052 Retire net-zone runs from the player-facing game (plan.md §16, proposal §7). Rewrite the run assertions in DeliveryAdjudicatorTests and ScoringPlayModeTests to zones and outcomes; keep every finalization test. No test left failing.

## M8 — Net Practice with Shot Confidence

Dependency: M7. Requirements: REQ-009, REQ-029..031. Acceptance: AC-06 (amended), AC-17, AC-18.

- [ ] T053 Swing-peak timestamp in BatController; per-contact swing data (v_peak, t_peak, Δt, reset age) in the contact report and the CSV for every contacted ball.
- [ ] T054 `ShotConfidence` and `ConfidenceSettings` (every plan.md §17 calibration setting, including the ±60 ms timing window). EditMode tests per component, edge cap, defensive renormalisation, labels.
- [ ] T055 Leave / played-and-missed classification with the 0.20 m and 3 m/s thresholds as settings. Tests with recorded gap and bat-speed traces.
- [ ] T056 Overs selection, per-ball confidence panel with breakdown, and the session summary (averages, best/worst, counts, per-over trend). Test: summary = values recomputed from records.
- [ ] T057 Headset: the AC-17 shot set and AC-18; collect the first calibration CSV; tune the confidence rows of the plan.md §17 table and record before/after values.

## M9 — Virtual-field scorer

Dependency: M8 (calibration CSV). Requirements: REQ-032..035. Acceptance: AC-19, AC-20, AC-21.

- [ ] T058 `ShotLaunch` capture; `VirtualField` flight, bounce and roll in Core (200 Hz, deterministic), with μ_roll from GroundDefinition (the single value, changed from 0.08 to 0.25 g here). Tests: analytic range without drag within 1%, drag monotonicity, bounce and roll formulas, determinism.
- [ ] T059 Boundary 4/6 and the played-on sweep against the same stumps box the physics uses. Tests at the rope and at the stumps edges; a played-on case matching the physics stumps collider dimensions.
- [ ] T060 Catches (dropped chances), ground fielding, the runs formula, an unfielded stop, a separate close-catcher reaction. Table-driven EditMode suite (AC-20).
- [ ] T061 Calibration replay tool (Editor menu and batch): replays a CSV through the scorer and writes a distribution report. Tune the scorer rows of the §17 table and record before/after values (AC-21).

## M10 — Match Simulation

Dependency: M9. Requirements: REQ-010 (amended), REQ-012, REQ-036..038. Acceptance: AC-22, AC-23.

- [ ] T062 Scenario presets and the seeded random generator (RR 6–15, 6–36 balls, 1–5 wickets). Tests: bounds over 1,000 draws; same seed, same scenario.
- [ ] T063 Chase state and scoreboard (runs needed, balls left, wickets left, required rate while balls remain); win/loss exactly once; D-1/D-2 rules; Shot Confidence as a secondary line (D-4). Tests for every end path.
- [ ] T064 Simulated ball path: kinematic playback from the exact contact position and exit velocity (no jump or speed change), ending at the catch, rope or intercept; "Simulated carry" labels; landing / fielded / caught / boundary sounds alongside the existing bat sounds. PlayMode test: the visible ball and the scored path have the same first landing point and end position (within 2 cm) and no discontinuity at contact.
- [ ] T065 Difficulty presets wired to the delivery plans (Hard ≤ 120 km/h until an AC-04 re-run covers 100–120 km/h).
- [ ] T066 Headset: AC-23, three scenarios; record each ball and whether the visual matched the result.

## M11 — Polish and device verification

Dependency: M10. Requirements: REQ-015, REQ-016, REQ-039 plus regression of all earlier requirements. Acceptance: AC-10, AC-24 and any open scenarios.

- [ ] T067 Readability of menus, panels and scoreboards; audio and haptics balance; actionable error messages.
- [ ] T068 AC-24: performance in both modes (PerformanceProbe and OVR Metrics), foveation on and off.
- [ ] T069 AC-10: three sessions per mode, restart and interruption; README, validation status and known limitations; claim completion only with evidence.

## Scope/change rule

Before adding work outside these tasks, update the affected requirement/scenario and plan, then add a mapped task. Preserve IDs; retire rather than silently renumber requirements. A bug fix within existing behavior can use an appended task with its requirement mapping.
