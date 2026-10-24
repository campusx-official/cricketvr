# Progress

Detailed evidence lives in `specs/001-six-ball-net/validation.md`. This table is the short status view.

| Milestone | Implementation | Automated/build checks | Quest validation | Blocker/next action |
| --- | --- | --- | --- | --- |
| M1 First Quest scene | Implemented (T001–T004) | Compiles; OpenXR validation 0 errors/0 warnings; dev APK built 2026-10-23 (E-001, E-002) | Partial: installed, session FOCUSED, 72/72 FPS in logs (E-003) | Owner: left controller check (E-004); other AC-01 items pass |
| M2 Practice net | Implemented (T007–T009) | Builder idempotent; EditMode 11/11; APK built and installed (E-005) | **AC-02 passed** (E-006) | Done; next M3 tracked bat |
| M3 Tracked bat | Implemented (T010–T012), realistic bat | EditMode 32/32, PlayMode 3/3 (E-007–E-013) | **AC-03 pass, right controller** (E-014) | Left-hand checks when the left controller works |
| M4 Bowling machine | Implemented (T013–T015), 144 Hz physics, tuned bounce | EditMode 40/40, PlayMode 10/10 (E-015, E-016, E-018) | **AC-04 pass** (E-017–E-019) | Done |
| M5 Batting contact | Implemented (T016–T020): swept resolver, response with swing assist, recorded impact sounds, rhythm-coded haptics | EditMode 52/52, PlayMode 17/17 incl. AC-05 matrix (E-020, E-028) | **AC-05 pass, right controller** (E-022–E-029) | Done |
| M6 Delivery outcomes | Implemented (T021–T024): DeliveryAdjudicator, practice zones on tagged net walls, boundary exit, measured carry, ScoreManager records | EditMode 76/76, PlayMode 28/28 (E-030) | **AC-06 pass** (E-031, NetMode) | Done; net scoring kept until the virtual-field scorer |
| M6.5 Cricket ground | Implemented on branch m6.5-ground (T033–T045): GroundDefinition/FieldConfig, generated ground, GroundLit/sky shaders, outfield physics + rolling drag, BallLeftGround, GroundMode rules, foveation, perf probe | EditMode 91/91, PlayMode 38/38, previews (E-032) | Pending | Owner: M6.5 headset tests + AC-14 measurement |
| M7 Six-ball loop | Not started | Pending | Pending | After M6 |
| M8 Quest polish | Not started | Pending | Pending | After M7 |
