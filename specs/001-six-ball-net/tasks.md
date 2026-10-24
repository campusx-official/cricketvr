# Implementation tasks

Status: M1 implemented, AC-01 partial (left controller hardware). M2 complete (AC-02 passed). M3 complete for the right controller (AC-03 pass; left hand pending hardware). M4 complete (AC-04 passed on Quest). M5 complete (AC-05 passed on Quest, right controller). M6 complete (AC-06 passed on Quest in NetMode). M6.5 T033–T045 implemented with automated checks; T046 (device performance) and the AC-11–AC-14 headset checks pending. Later milestones pending. This is a task specification, not evidence of a built game.

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
- [ ] T046 Turn on fixed foveated rendering. Make a development build and record the measured draw calls, triangles, texture memory, and CPU/GPU frame time on Quest with foveation on and off (AC-14). Owner headset checks AC-11 to AC-13.

## M7 — Six-ball session

Dependency: M6 (runs in NetMode until the virtual-field scorer exists). Requirements: REQ-011, REQ-012, REQ-013, REQ-014. Acceptance: AC-07, AC-08, AC-09.

- [ ] T025 Implement guarded session transitions, countdown, intervals, six-result limit, and cancellation generation/token.
- [ ] T026 Add Start, live scoreboard, six-row results, derived totals, and Restart.
- [ ] T027 Handle focus/tracking loss, interrupted-ball cancellation without consumption, explicit readiness recovery, and motion-history reset.
- [ ] T028 Test a full session, five restarts across phases, and interruptions; verify no seventh ball, stale callback, or corrupted total.

## M8 — Polish and device acceptance

Dependency: M7. Requirements: REQ-015, REQ-016 plus regression of all previous requirements. Acceptance: AC-10 and remaining open scenarios.

- [ ] T029 Improve visibility, menu readability, audio/haptics balance, and actionable error messages without expanding scope.
- [ ] T030 Profile on Quest against the selected rendering target; document CPU/GPU measurements and optimize demonstrated bottlenecks.
- [ ] T031 Complete three sessions, both-handedness checks, restart/recovery, and regression scenarios on the identified build.
- [ ] T032 Update README, validation status, known limitations, and requirement coverage. Claim MVP completion only when evidence supports it.

## Scope/change rule

Before adding work outside these tasks, update the affected requirement/scenario and plan, then add a mapped task. Preserve IDs; retire rather than silently renumber requirements. A bug fix within existing behavior can use an appended task with its requirement mapping.
