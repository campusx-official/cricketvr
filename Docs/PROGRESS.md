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
| M6.5 Cricket ground | Implemented (T033–T046) | EditMode 91/91, PlayMode 38/38 (E-032) | **AC-11–AC-14 pass** (E-033–E-035): 72 fps, GPU 2.5 ms (FFR on) / 3.0 ms (off), ≤ 63 draw calls, ≤ 59k triangles, 82 MB graphics | Done; merged to main |
| M7 Session framework + start menu | Implemented on branch m7-session (T047–T052) | EditMode 108/108, PlayMode 44/44 (E-036–E-038) | AC-15 menu pass (E-039); AC-07 pass, B press and ground timing confirmed, device CSV collected (E-050); AC-08/09 pending | Owner: confirm restart/menu and pause checks (AC-08/09); choose AC-16 Δt handling; then go-ahead to merge into main |
| M7.5 Delivery types | In progress on branch m7.5-delivery (from m7-session): docs merged; T070–T073 done | EditMode 133/133, PlayMode 46/46; pitch point within 0.51 cm (was 2.2–10.1 cm), tolerance 1 cm; PhysX rebound −9.8..+11.9 cm recorded (E-040, E-042); types + request path (E-041); record columns (E-043); natural variation (E-044); swing solver (E-045) | Pending | T074 runtime swing, T075 speeds/contact; owner: swing size definition before T076; (T073–T075); owner decision on the pitch bounce before Phase B |
| M8 Net Practice + Shot Confidence | Not started | Pending | Pending | After M7 |
| M9 Virtual-field scorer | Not started | Pending | Pending | After M8 (needs calibration CSV) |
| M10 Match Simulation | Not started | Pending | Pending | After M9 |
| M11 Polish + device verification | Not started | Pending | Pending | After M10 |
