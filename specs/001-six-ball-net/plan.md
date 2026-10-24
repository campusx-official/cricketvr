# Technical plan: six-ball net session

Status: Proposed baseline, 2026-10-23. No Unity project or code exists yet. Every value here is a starting default to be measured and tuned during its milestone. Record changes in the decisions log below.

Owns: technical decisions and architecture for `spec.md` (REQ-001..016). Task order lives in `tasks.md`. Evidence lives in `validation.md`.

## Inspected environment (T001, read-only, 2026-10-23)

- macOS 26.5.2 on Apple Silicon (M4 MacBook Air, 16 GB), ~150 GB free.
- `~/Desktop` is not iCloud-synced, so `/Users/nitish/Desktop/cricketvr` is acceptable as the project folder.
- Not installed: Unity Hub, Unity Editor, Android SDK/NDK, `adb`. Present: system `git` 2.50.1.
- The folder is not yet a Git repository. It contains only `CLAUDE.md`, `README.md`, and `specs/`.

## Conflicts and gaps found

1. `plan.md` was missing from the starter package. This document resolves that.
2. Forward shots travel +Z toward the launcher, so the "front" scoring zones must sit behind the launcher end. The spec doesn't define what happens when a ball clears a wall (no roof) or hits the launcher. §6 proposes rules; these are **pending owner approval** (see `spec.md`).
3. `README.md` says to copy these documents into an existing Unity project. Instead, the Unity project will be created in this folder (§2).

## 1. Toolchain and versions (T001–T002)
- **Unity Hub** (latest) → **Unity 6 LTS, newest patch** (expected to be 6000.3.x LTS; the exact version gets confirmed in Hub and the Meta/OpenXR compatibility notes at T002). Use the Apple Silicon Editor. Install these modules: **Android Build Support + OpenJDK + Android SDK & NDK Tools**, all managed by Hub. Don't use a system Java or SDK.
- Packages (record exact versions in README after resolution):
  - `com.unity.render-pipelines.universal` (from template)
  - `com.unity.xr.openxr`
  - `com.unity.xr.management`
  - `com.unity.inputsystem`
  - `com.unity.xr.interaction.toolkit` 3.x, plus only its **Starter Assets** sample (for XR Origin/controller input actions)
  - `com.unity.textmeshpro` / uGUI (built into Unity 6)
  - `com.unity.test-framework`
  - No Meta All-in-One SDK. Add Meta XR Core SDK only if a demonstrated need appears (none is planned).
- Owner-controlled steps (documented precisely in README):
  - Meta developer account/organization
  - Enable Developer Mode in the Horizon phone app
  - Use a USB-C **data** cable and accept "Allow USB debugging" in the headset
  - Install MQDH (optional; used for install, logs, and the performance HUD)

## 2. Project creation and repo layout (T003)
- Create the project in Hub with the **Universal 3D** template, *not* the VR template (avoids demo content), in a temporary sibling folder. Move `Assets/`, `Packages/`, `ProjectSettings/` into `/Users/nitish/Desktop/cricketvr`, then reopen from Hub. Reason: Hub won't create into a non-empty folder.
- `git init`. Add a Unity `.gitignore` (Library, Temp, Obj, Logs, UserSettings, Builds, `.vs*`, `.idea`, `*.apk`, `.DS_Store`). Set Editor settings to **Force Text** serialization and **Visible Meta Files**. No LFS.
- Assemblies (asmdefs) keep pure logic testable and Editor code separate:
  - `CricketVR.Core`: **no `UnityEngine` MonoBehaviours**. Only plain C# plus `UnityEngine` math types (Vector3, Quaternion). Holds the solvers, rules, and state machine.
  - `CricketVR.Runtime`: MonoBehaviours and ScriptableObjects. References Core, XRI, and Input System.
  - `CricketVR.Editor`: setup and build tooling, Editor-only.
  - `CricketVR.Tests.EditMode`: tests Core.
  - `CricketVR.Tests.PlayMode`: tests Runtime physics and session flow.
- Folders follow CLAUDE.md §6 and are created when first needed.

## 3. XR configuration (T003–T005)
- Build Profile: **Android** (Meta Quest profile if offered by the installed Editor). Settings:
  - ARM64 only, IL2CPP, min API per current Meta requirement (check at T002)
  - Vulkan graphics API
  - Linear color space
  - Texture compression ASTC
- XR Plug-in Management (Android tab) → **OpenXR**.
  - Feature group: **Meta Quest Support**.
  - Interaction profiles: **Oculus Touch Controller Profile** and **Meta Quest Touch Plus Controller Profile**.
  - Render mode: Single Pass Instanced / Multiview.
  - Set Tracking Origin to **Floor** on XR Origin.
- Run **Project Validation → Fix All**, then review what it changed in Git.
- Render target: **72 Hz** baseline. The display frequency is left at the default unless profiling requires otherwise.
- URP asset: no post-processing, no realtime shadows initially (or one soft-off directional shadow), MSAA 4x, HDR off.

## 4. Scene construction strategy
- One scene: `Assets/CricketVR/Scenes/Main.unity`.
- An **idempotent Editor menu** `CricketVR ▸ Setup ▸ Build/Repair Main Scene` (`Scripts/Editor/MainSceneBuilder.cs`) creates the missing objects from the CLAUDE.md §5 hierarchy by name. It never deletes or overwrites existing objects or their transforms. It logs what it created. XR Origin is instantiated from the XRI Starter Assets prefab **only if no `XROrigin` exists** (prevents duplicate rigs).
- `Scripts/Editor/QuestBuild.cs`: menu `CricketVR ▸ Build ▸ Quest APK` plus a `-executeMethod CricketVR.Editor.QuestBuild.Build` entry for batch builds. Batch builds only run when the Editor is closed. Output goes to `Builds/` (gitignored).
- A `SceneValidator` (runtime `OnValidate`/`Awake` checks) raises actionable errors for missing serialized references.

## 5. Coordinates and geometry (M2, T007–T008)
- 1 unit = 1 m. +Y up. Striker's **stumps base at origin**. Deliveries travel −Z, and +Z points toward the launcher. +X is the batter's right when facing the launcher, which is the off side for a right-hander.
- Pitch: 3.05 m wide, from z = −1.5 to z = +21.5, so the 20.12 m stump-to-stump reference is visible. Popping crease at z = +1.22, return creases at x = ±1.32.
- Stumps: 3 cylinders, 0.711 m tall, 0.2286 m total width, bails as visuals. A single **solid box collider** (layer `Stumps`) covers the stump set; M6 adds wicket handling on it (a trigger is only added if M6 needs one).
- `BattingAnchor`: the middle of the striker's popping crease (0, 0, 1.22). Floor stance markers: right-handed at (−0.3, 0, 1.0) (yellow), left-handed at (+0.3, 0, 1.0) (blue), i.e. on the leg side of the stumps just behind the crease. The XR Origin sits at the right-handed stance facing +Z, so the centre of the player's boundary maps there. Handedness switching (M3) moves the rig between markers before a session; never during a delivery.
- Non-striker stumps at z = 20.12 are visual only (depth cue behind the release point).
- `LauncherAnchor`: default release point (0, 2.0, 18.9), about at the bowler's popping crease, with height configurable. The launcher visual has **no collider**, so returning balls pass through.
- Net enclosure: inexpensive box colliders on a dedicated `Boundary` layer.
  - Side walls at x = ±1.8, 4 m high, z −3 → +24
  - Rear wall at z = −3
  - Front wall at z = +24, 6 m high
  - No roof
  - Visual net: tinted translucent quads (one shared URP Unlit transparent material, 35% alpha) plus dark poles and top rails. A fine alpha-tested grid was rejected because thin repeating lines alias/shimmer on Quest.
- Scoreboard: world-space board 1.0 × 0.6 m, about 2.4 m from the stance at 45° toward the off side, centre 1.9 m high, facing the player and inside the net (no collider). 3 m would put it through the side net. M2 places a blank placeholder; text arrives in M7. Start and results panels share that location. Handedness flips which side it uses.
- Lighting: one directional light (no shadows), flat ambient (0.55, 0.56, 0.60), URP Simple Lit materials. The outfield floor is a 50 m plane centred at z = 10. The ball is bright (white or high-visibility yellow), matching the contrast work in M8.

## 6. Practice zones and terminal rules (M6, T021–T024) — PC-01 accepted 2026-10-24

**Zones are regions of the real net wall colliders**, not separate trigger volumes. Each wall collider carries a `NetWall` component (Front, Left, Right, Rear). The zone is classified from the collision contact point, so no trigger can intercept the ball before it physically meets the net, and the wall still stops the ball. Earlier drafts proposed thin triggers just inside each wall. Using the colliders has the same effect with less geometry and no double-firing.

| Terminal event | Zone | Practice runs after bat contact | Without bat contact |
| --- | --- | ---: | --- |
| Front net, contact height y < 1.0 m | FrontLow | 2 | Miss, 0 |
| Front net, 1.0 ≤ y < 3.0 m | FrontMiddle | 4 | Miss, 0 |
| Front net, y ≥ 3.0 m | FrontUpper | 6 | Miss, 0 |
| Left or right net | SideNet | 1 | Miss, 0 |
| Rear net | RearNet | 1 | Miss, 0 |
| Ball leaves over the front net (no roof) | OverFront | 6 | Miss, 0 |
| Ball leaves over a side net | OverSide | 1 | Miss, 0 |
| Ball leaves over the rear net | OverRear | 1 | Miss, 0 |
| Stumps hit | — | 0, Bowled (contact flag kept) | Bowled, 0 |
| Ball settles (< 0.2 m/s for 0.5 s) | — | 0 (zero-run fallback) | Miss, 0 |
| Timeout (10 s after release) | — | 0 (zero-run fallback) | Miss, 0 |

- Height bounds are half-open intervals, so a boundary value maps to one zone deterministically. All run values and heights live in `GameConfig.scoring`.
- **Leaving the enclosure:** the ball counts as out once its centre is more than 0.3 m outside a wall plane (x beyond ±(1.8 + 0.3), z > 24.3 or z < −3.3). The walls use CCD, so being outside means the ball went over a wall. At a corner the side is chosen as Front, then Side, then Rear.
- **Finalize once, in time order.** `ScoreManager` (execution order −45) runs before the contact resolver (−40) in each physics step. It processes the collisions and exit check from the previous simulation step, so everything it sees happened before this step's bat contact. A bat contact applied at step N is therefore never credited to a collision from step N−1. Several terminal events in the same step are resolved by priority: Stumps > FrontUpper > FrontMiddle > FrontLow > SideNet > RearNet > OverFront > OverSide > OverRear. Settle and timeout come from `BallController` after the collision check and only apply if nothing else has finalized.
- **After finalization the ball is dead.** It stays visible and physical for 1.5 s, so the player sees it hit the net or stumps, but it can't be struck again and no event can change the result. Then it is parked. A relaunch or cancel before finalization discards the delivery: a cancelled ball produces no result, and M7 will replay it.
- **Stale events** carrying a different delivery id, or arriving after finalization, are ignored.
- **Bowled after a hit** (played on): the result is Bowled with 0 runs, and both the contact and wicket flags are true. A ball that reaches the net first is finalized there, so it can't later count as bowled.
- **Measured carry:** the horizontal |Δxz| from the bat contact point to the first ground (Pitch layer) touch after contact, provided no net was touched in that step or before.
  - If the net comes first (including the same step as the landing), carry shows **Net hit**.
  - If the ball clears the net, or is finalized before landing, carry shows **N/A**.
  - If there was no bat contact, there is no carry.
  - The label is always "Measured carry".
- **Per-delivery record (`DeliveryResult`):** id, outcome (Scored / NoRuns / Miss / Bowled), terminal event, zone, runs, contact, bowled, carry state and metres, exit speed, launch and direction angles, contact height on the bat, and the delivery config (for reproduction). Session totals are derived from these records in M7.

## 7. Core architecture (classes, owners, data flow)

| Class | Assembly | Responsibility |
|---|---|---|
| `GameConfig` (SO) | Runtime | Delivery count 6, countdown 3 s, inter-ball 3 s, timeout 10 s, settle thresholds, bat tuning, zone runs/priority, debug toggles |
| `DeliveryConfig` (SO + serializable struct) | Runtime/Core | Release pos, speed km/h, line X at bounce, length (bounce Z), optional seed |
| `Units` | Core | `KmhToMs = v / 3.6` |
| `DeliverySolver` | Core | Release + bounce target + speed → initial velocity or `Unreachable` with reason |
| `PoseHistory` | Core | Ring buffer of (t, pos, rot). Linear/angular velocity over real intervals, plus point velocity |
| `BatContactMath` | Core | Relative-velocity/normal response, restitution vs. off-centre, clamps, finiteness |
| `SweptBatBallTest` | Core | Substepped relative-motion sphere-vs-OBB test |
| `ScoringRules` | Core | Zone → runs, priority, terminal decisions, carry rule |
| `DeliveryResult` / `SessionResults` | Core | Immutable records. Totals derived from records |
| `SessionStateMachine` | Core | Guarded transitions, generation counter, six-record limit |
| `BatController` | Runtime | Controller binding, grip offsets, visual, kinematic proxy, pose sampling, validity gating |
| `BallController` | Runtime | Ball lifecycle, launch/reset, collision → event routing with delivery ID |
| `BatBallContactResolver` | Runtime | **Sole authority** for bat impulse. Combines callback and sweep detections |
| `BowlingMachine` | Runtime | Picks config, calls solver, asks the ball to launch |
| `ScoreManager` | Runtime | Wraps ScoringRules. Receives ball events and emits `DeliveryFinalized` |
| `SessionManager` | Runtime | Drives the state machine with Update-based timers, handles pause/interrupts |
| `ScoreboardUI`, `StartPanel`, `ResultsPanel` | Runtime | Display only. Buttons call SessionManager |
| `FeedbackController` | Runtime | Impact audio + haptics |
| `DebugDeliveryPanel` | Runtime (dev builds) | Desktop keys / in-VR debug launch, markers |

Events are plain C# `event Action<…>` on owners: `DeliveryStarted(id)`, `BatContact(id, ContactData)`, `DeliveryFinalized(DeliveryResult)`, `SessionCompleted`. Subscribe in `OnEnable` and unsubscribe in `OnDisable`. There is no event bus and there are no singletons.

## 8. Bat tracking (M3, T010–T012)
- Use the XRI Starter Assets XR Origin. The controller GameObjects use `TrackedPoseDriver` (Input System) with the `isTracked`/`trackingState` actions.
- `BatController` binds to dedicated **Left Grip / Right Grip** transforms (TrackedPoseDriver on the grip pose `devicePosition`/`deviceRotation`; the Starter Assets controllers use the tilted aim pose and are kept for UI rays) based on `Handedness`, which is chosen on the Start panel before Ready. Grip offset: local position/rotation per hand, stored in `GameConfig` and editable in the Inspector and through a small in-VR nudge UI (T012). Mirror only the offset, never the rig or scales.
- **Visual bat** (blade 0.108 × 0.04 × 0.56 m, handle 0.30 m, primitives) is parented to the controller via the grip offset. It updates every frame, including at `onBeforeRender` through the TrackedPoseDriver.
- **Physics proxy**: a separate kinematic Rigidbody (BoxCollider = blade) on layer `BatProxy`. In `FixedUpdate` it calls `MovePosition/MoveRotation` toward the latest sampled pose. Interpolation is off, and CCD is `ContinuousSpeculative`, the only CCD mode valid for kinematic bodies.
- `PoseHistory` samples the grip pose in `Update` using `Time.unscaledTimeAsDouble` (pose at the frame the tracking was applied). It holds 16 samples.
  - Linear velocity comes from a least-squares or two-point fit over the last ~30 ms.
  - Angular velocity: `Δq = q1 * inverse(q0)`, force shortest path (w ≥ 0), `ToAngleAxis` → radians / real dt.
  - `v_point = v_ref + ω × (p − p_ref)`.
- **Discontinuity gating**: clear history and set `contactSuppressedUntil = now + 0.1 s` (plus ≥ 3 valid samples) when any of these happens:
  - `isTracked` becomes false
  - `XRInputSubsystem.trackingOriginUpdated` fires (recenter)
  - dt > 0.1 s
  - grip speed > 25 m/s or ω > 60 rad/s (tunable)
  - app focus is regained
  
  While suppressed, the resolver rejects all contacts.

## 9. Bowling and ball (M4, T013–T015)
- Ball: SphereCollider r = 0.036, mass 0.16 kg, dynamic Rigidbody, `ContinuousDynamic`, interpolation on, linear damping 0 (no drag in MVP, documented), layer `Ball`. Pitch PhysicMaterial: bounciness ~0.5, friction ~0.35 (tuned at M4). Combine modes are set so the pitch value dominates.
- `DeliverySolver`:
  - Displacement d = B − P0, horizontal D, vertical Δy (bounce point at y = r).
  - For speed s: `tanθ = (s² − √(s⁴ − g(gD² + 2Δy s²))) / (gD)`, taking the **lower** (flat) root.
  - A negative discriminant means Unreachable, reported with the minimum feasible speed. The speed is never silently changed.
  - Then **numeric refinement**: simulate the same semi-implicit Euler integrator at `Time.fixedDeltaTime` and shoot-adjust θ (bisection, ≤ 20 iterations) so the discrete trajectory lands at B within 1 cm. This removes the integrator bias.
- Launch: reset first, then `rb.position = P0`, zero velocities, `rb.linearVelocity = v0` (Unity 6 API), `trail.Clear()`, clear flags and timers, and assign a new `deliveryId` (monotonic int + session generation).
- The ball is reused and never instantiated mid-session. A single `BallController` instance guarantees one ball.
- Debug: `F` key / tap A launches the selected preset; `N` / tap B selects the next preset and bowls it (taps < 0.4 s, so they don't clash with the 1 s bat-setup holds). Presets live in `GameConfig.delivery.debugPresets`. A cyan target marker, a red measured-bounce marker, a log line per delivery and a TextMeshPro readout on the scoreboard support the AC-04 check.
- Ball collision layers: `Ball` touches only Pitch, Boundary, Stumps and BatProxy; `BatProxy` touches only Ball. Physics materials: pitch/outfield bounciness 0.72, dynamic friction 0.20, static 0.30 (owner-tuned, E-018: ball centre at the crease ~0.58 m full / 0.95 m good / 1.20 m short at 80 km/h); nets 0.10 / 0.80; stumps 0.30 / 0.30; ball 0 with Minimum combine, surfaces Maximum, so the surface value decides each bounce.
- **Physics rate 144 Hz** (fixed timestep 1/144 s, maximum allowed timestep 0.05 s). Measured 2026-10-23 (E-015): at 50 Hz the ball moves 44 cm and drops up to 13 cm per step, sinks up to 4.7 cm below the pitch despite ContinuousDynamic CCD, and bounces up to 40 cm from the requested point. 144 Hz: worst 8.4 cm over 60/80/100 km/h x 5 lengths, no sinking below the surface, and exactly 2 steps per 72 Hz frame. Quest CPU cost to be confirmed in M8 profiling.

## 10. Contact detection and response (M5, T016–T020)
- Layer collision matrix: `Ball` collides with `Pitch`, `Boundary`, `Stumps` only. `BatProxy` collides with nothing. `ZoneTrigger` (M6) is trigger-only.
- **One detector, one authority** (revised 2026-10-23 after E-019): PhysX's own Ball/BatProxy contact let balls pass through the bat on the headset and would add an uncontrolled impulse, so it is disabled. `BatBallContactResolver` (execution order -40, after `BatController` at -50) runs `BatContactMath.Sweep` every `FixedUpdate` before the simulation:
  - Bat: `BatController.StepStartPose` -> `StepEndPose` (lerp/slerp). After any motion reset the start pose snaps to the end pose, so a discontinuity is never swept.
  - Ball: its position now -> where PhysX will put it this step (`b0 + (v + g dt) dt`).
  - Substeps sized so neither the ball nor any blade corner moves more than r/2 per substep (cap 64), then 12 bisection steps for the contact moment. A ball already touching at the start counts as an initial-overlap contact at t = 0.
  - Accepts only the first contact per delivery, only while `BatController.ContactAllowed`, and only if approaching (vn < 0). It sets the ball's position (contact point + normal x (r + 1 mm)) and velocity via `BallController.ApplyBatHit`.
  - Logs each contact (speeds, launch/direction angles, e, sweet/edge factors, substeps) and, for deliveries without contact, the closest gap if under 15 cm.
- Response (`BatContactMath`):
  - `n` = blade face normal at the contact, sign toward the ball. `v_bat = PoseHistory.PointVelocity(contactPoint)`. `v_rel = v_ball − v_bat`, `vn = v_rel·n`. If vn ≥ 0, reject (separating).
  - `e = lerp(eEdge, eSweet, sweetFactor)`. The sweet spot is about 0.15–0.25 m from the blade toe and falls off smoothly. Toward the blade edge in width, e is reduced and n is tilted by a bounded, deterministic angle (no random numbers).
  - `v_out_rel = −e·vn·n + (1 − μt)·v_t`, and `v_out = v_bat + v_out_rel`.
  - Clamp |v_out| ≤ 45 m/s. Assert finite, otherwise fall back to a dead-bat drop and log it.
  - As a result: a stationary face deflects, a soft defence gives low exit speed, a fast aligned swing gives high exit speed, face angle sets direction, and loft sets launch angle.
- Feedback (`FeedbackController`, revised after E-023): the owner's own recorded bat hits (Sector 66.m4a), cut by `Tools/audio/derive_bat_sounds.py` into 3 middle clips, 3 off-centre clips (low-passed, pitched down 15%, shorter ring) and 3 edge clips (bright attack only). Each contact blends them by weight (edge = |edge factor|; middle = sweet x (1 - edge); off-centre = (1 - sweet) x (1 - edge)), loudness 0.45-1 by approach speed (full at 30 m/s), spatial blend 0.4. Haptics on the bat-hand controller, amplitude 0.55-1, distinct rhythms: middle one 40 ms pulse; off-centre a full-strength jolt then a fading buzz (~0.26 s); edge two 20 ms ticks. Synthesised sounds were tried and rejected (metallic, then rubbery: E-022, E-023).
- Tracked state comes from `UnityEngine.XR.InputDevices` at the hand's XRNode; the Input System `isTracked` binding is a fallback only (E-021).
- Debug: a yellow contact marker sphere (`GameConfig.contact.showContactMarker`) and contact/near-miss log lines (`logContacts`).
- Response tuning defaults (`GameConfig.contact.response`): e 0.50 at the sweet spot (0.20 m above the toe, falloff 0.22 m) to 0.35 off-centre; swing assist `batSpeedGain` 1.4 (measured contact-point bat velocity x1.4 in the response; a stationary bat is unaffected); edge zone = outer 25% of the half-width, edge e x0.55 and up to 14 degrees deflection; tangential loss 0.25; exit cap 45 m/s.
- Fixed timestep: 1/144 s (set in M4 from bounce measurements, E-015). The M5 scripted contact matrix runs at this rate.
- Known limitation: the physics bat uses the controller pose sampled in the previous frame's Update, while the visual bat uses the late (before-render) pose, so the physics bat can trail the visual by about one frame in a fast swing. To be judged on the headset (AC-05 feel); pose extrapolation is the follow-up if needed.

## 11. Session flow (M7, T025–T028)
- States: `Ready → Countdown → DeliveryActive → Resolving → BetweenBalls → (Countdown | SessionComplete)`. `SessionComplete → Ready` on Restart. `Paused` can be entered from Countdown, DeliveryActive, Resolving, or BetweenBalls.
- Timers are float fields advanced in `SessionManager.Update`. **No coroutines or Invoke.** Every scheduled action captures `generation`, so Restart and Pause increment `generation` and stale actions no-op. Ball events carry `deliveryId`, and a mismatch is dropped.
- Finalize: the `SessionStateMachine.AddResult(result)` guard rejects a duplicate ID, a wrong ID, or a seventh record. Totals (balls faced, practice runs, contacts, misses, bowled) are computed from the records.
- Interruption: `Application.focusChanged(false)`, `OnApplicationPause(true)`, HMD `isTracked` false, or both-controller loss → Paused. Paused means:
  - the ball is reset and the active delivery is cancelled, not recorded
  - the timers stop
  - bat history is cleared
  
  The UI then shows "Paused — press Ready to retry ball N". Confirming goes to Countdown for the same ball number. There is no auto-resume.
- UI: the Start panel (handedness toggle, alignment nudge, clear-space reminder, Start), the live scoreboard (ball n/6, runs, contacts, misses, bowled, last result + carry), and the Results panel (6 rows + totals, "Practice runs" label, Restart). Use the XRI ray interactor on the non-bat controller for UI.

## 12. Testing (per CLAUDE.md §13)
- **EditMode (Core):**
  - km/h conversion
  - solver reachability and refined bounce error < 1 cm
  - point velocity with pure rotation
  - shortest-path quaternion
  - stationary-face deflection
  - defence slower than attack
  - finite and clamped outputs across random-seeded fuzz
  - scoring priority and half-open bounds
  - unhit zone = 0
  - contact-then-bowled = 0 with both flags
  - carry N/A after a net hit
  - state machine: six-limit, duplicate/stale rejection, restart reset, pause without consumption
- **PlayMode:**
  - 10 identical launches with bounce within 0.25 m (AC-04)
  - no residual state after reset
  - scripted bat sweeps (a `ScriptedBatDriver` replaces the controller source): block, soft defence, drive, loft, off-centre, tip arc at several speeds, initial overlap, 10 runs each, asserting exactly one impulse and no tunneling (AC-05 scripted part)
  - countdown cancel, restart in each phase × 5 (AC-08)
  - simulated focus loss (AC-09 logic)
- Run tests through the Test Runner, or in batch mode with `-runTests -testPlatform EditMode|PlayMode` when the Editor is closed. Results go to `validation.md`.

## 13. Performance (M8)
- Budget: 72 Hz (13.9 ms). Measure with the MQDH Performance HUD / OVR Metrics Tool and the Unity Profiler connected to a development build over USB/ADB. Record CPU/GPU ms separately.
- Expected cost is trivial. Investigate only demonstrated problems: avoid per-frame allocation in PoseHistory and the resolver, keep shared materials, and turn off debug logs in release builds.

## 14. Milestone execution order
Execute strictly M1 → M8 per `tasks.md`, with M6.5 (cricket ground, owner request 2026-10-24) between M6 and M7. Each milestone ends with:
- a tasks.md tick for the code work
- a `validation.md` evidence entry for each check actually run
- a `Docs/PROGRESS.md` row update
- precise owner headset steps when device validation is pending

The MVP isn't claimed done until AC-10 has real device evidence. **First implementation action: M1 (T001–T006).** The owner installs Unity Hub, the Editor, and the modules listed in §1. The agent then configures the project, scene builder, and build script, produces the APK, and hands over the AC-01 headset checklist.

## 15. Cricket ground (M6.5, T033–T046)

Owner decisions GD-1 to GD-12 (accepted 2026-10-24) are recorded in `proposals/M6.5-cricket-ground.md`. Summary: M6 Test 1 and an M6 commit first; black sight screens with the white ball; Outfield layer; GroundDefinition as the single geometry source (Laws values checked against FieldDimensions); vertex-baked shading, no lightmaps; static merged meshes for markers and fielders; 25 m shadow distance.

**Mode switch.** *(As built: objects are not moved under `Environment/PracticeNet`; the switch holds explicit lists of NetMode objects (Floor, NetFrameAndVisuals, NetBoundaryColliders, Pitch/Markings), NetMode renderers (the Pitch/Surface renderer; its collider stays) and GroundMode objects (Environment/Ground, Gameplay/GroundBoundaryMonitor). The hierarchy, the M2–M6 builders and the scene-layout tests are untouched.)*
- `GameConfig.environment` holds the mode: `NetMode` (default) or `GroundMode`. `Main.unity` contains both `Environment/PracticeNet` (the existing nets, floor and net colliders) and `Environment/Ground`.
- At startup `EnvironmentModeSwitch` activates one of them and tells ScoreManager which terminal rules to use.
- The pitch, the striker's stumps, `BattingAnchor`, `LauncherAnchor`, the bowling machine and the scoreboard are shared and never duplicated.
- Tests and the default build use NetMode. A GroundMode build is made with the menu item `CricketVR/Build/Quest APK (Ground mode)`. NetMode is removed only after the virtual-field scorer (a later task).

**Geometry, one source of truth.**
- `GroundDefinition` is a ScriptableObject at `Assets/CricketVR/Settings/GroundDefinition.asset`. It holds the pitch, creases, stumps, ground centre, boundary (circle radius or X/Z radii), rope, 30-yard circle, sight screens, apron, boards, stands, fog and sky colours, and the outfield stripe width.
- Pitch, crease and stump values default from `FieldDimensions` (Laws constants); an EditMode test enforces equality (GD-7).
- `FieldConfig` is a ScriptableObject holding the fielder list.
- The future virtual-field scorer will read the boundary shape, ground centre and fielder positions from these two assets.

**Build tool.**
- `CricketVR/Setup/7. Build or Rebuild Ground` (`GroundBuilder`, Editor-only) deletes and regenerates `Environment/Ground` from `GroundDefinition`. The generated meshes are saved as assets under `Assets/CricketVR/Generated/Ground/`, so builds don't depend on running the tool.
- It never touches the shared objects or the NetMode objects. It is idempotent: rebuilding twice gives identical assets.

**Visual construction (draw-call estimates).**

| Piece | How | Draw calls | Triangles |
| --- | --- | ---: | ---: |
| Outfield disc (r + 3 m) | Procedural disc mesh, 96 segments; stripes and the shading gradient in a 1024² generated texture plus UV tiling; Simple Lit | 1 | ~200 |
| Pitch strip + all crease markings | One mesh 2 mm above the collider top. Markings painted into a 512×4096 generated texture, so lines can't z-fight. GD-4 crease extensions are a second strip mesh 1 mm above the outfield | 2 | ~50 |
| Non-striker stumps + bails | Existing primitives, visual only | ~5 | ~3k |
| Boundary rope | Tube mesh, 6 sides × 256 segments, Simple Lit | 1 | ~3k |
| 30-yard circle discs | ~43 discs merged into one mesh, 2 mm above the outfield (GD-11) | 1 | ~1k |
| Sight screens (2) | Boxes, black unlit (GD-2), 10 m wide × 6 m high, 5 m beyond the rope | 2 | 24 |
| Advertising boards | Ring of 1 m-high panels 3 m beyond the rope; plain colours/owner text via one atlas texture; one mesh | 1 | ~1k |
| Stands | Stepped ring mesh (5 tiers, 6–12 m high), gaps behind the sight screens; crowd = vertex-colour noise + baked AO (GD-10); one opaque material | 1 | ~10k |
| Sky | Unlit gradient dome (inverted hemisphere), drawn before the ground | 1 | ~1k |
| Fielders (10) | Capsule + head merged into one mesh (GD-11), toggled as one object | 1 | ~6k |
| Existing bat, ball, machine, scoreboard, markers | Unchanged | ~25 | ~40k |
| **Total estimate** | | **~45** | **~70k** |

The estimates leave large headroom under the 150 draw-call and 250k-triangle budgets. The only large textures are the outfield (1024²) and pitch (512×4096) textures plus the board atlas (1024×256): under 10 MB with ASTC and mipmaps.

**Rendering settings.**
- One directional light (the existing one), no lightmaps (GD-10). URP Mobile asset: shadow distance 25 m, 1 cascade, soft shadows off. Ground meshes don't cast shadows.
- HDR stays off, MSAA stays 4×, no post-processing.
- Linear distance fog (Lighting > Environment): starts at 60 m and ends at 220 m, coloured to match the sky horizon, so the far stands soften.
- Textures: ASTC 6×6, mipmaps on, trilinear filtering, anisotropic 4× on the outfield and pitch.
- **Fixed foveated rendering:**
  - The OpenXR "Foveated Rendering" feature is installed but switched off.
  - Turn it on for Android, then set `XRDisplaySubsystem.foveatedRenderingLevel = 0.5` (medium) and `foveatedRenderingFlags = None` (fixed, not eye-tracked) at startup.
  - This works with Vulkan + URP on Quest 3.
  - Verify the effect with OVR Metrics Tool (GPU time drops, edges slightly softer).

**Ground physics.**
- **Pitch:** unchanged collider and `PitchSurface` material (bounciness 0.72, friction 0.2, Maximum combine).
- **Outfield:**
  - A box collider on the new `Outfield` layer (GD-6), 1 m thick, top at y = 0 and just below the pitch top (pitch top 0.002 m).
  - The box covers the boundary disc plus 5 m; the pitch collider sits on top inside it.
  - New `OutfieldGrass` physics material: bounciness 0.45, dynamic friction 0.45, static friction 0.5, Maximum combine.
  - The ball already uses ContinuousDynamic. 45 m/s at 144 Hz is 0.31 m per step against a 1 m-thick box, so tunnelling is impossible even without CCD.
- **Rolling drag:**
  - `BallRollingDrag` sits on the ball. While the ball touches the Outfield layer (OnCollisionStay), it applies a deceleration a = μr·g opposite the horizontal velocity, with μr = 0.08 (a grass rolling-resistance coefficient; tunable).
  - It is clamped so it never reverses the velocity. It is off on the pitch and in the air.
  - This is cosmetic and documented as such.
- **Collision matrix:** Ball also collides with Outfield; BatProxy still collides with nothing.

**Leaving the ground (GD-9, owner-resolved).**
- `GroundBoundaryMonitor` (runtime) checks the ball position each physics step against the boundary shape from `GroundDefinition`. This is a position test rather than a physics trigger, which keeps it deterministic and matches the M6 exit check.
- The ball has left the ground when its centre crosses the rope (horizontal test against the boundary ellipse). *(As built: a separate "above the stand roof" test was dropped; the stands are beyond the rope, so any ball above them has already crossed it, and a high catch inside the ground must not count.)*
- It raises `BallLeftGround(deliveryId, position, time)` once per delivery and hides the ball 1.5 s later.
- **Safety despawn** (immediate, no event needed): the ball centre goes below the outfield top (y < −0.5 m) or more than 150 m from the ground centre.
- The brief asked for trigger volumes; the owner accepted the position test instead (deterministic, cheaper, no trigger layer).

**GroundMode terminal rules (GD-8).**
- The ScoreManager adjudicator gets a GroundMode rule set, still finalizing exactly once with same-step priority (stumps highest):
  - stumps → **Bowled** (contact flag kept if hit first);
  - **unhit** ball whose centre passes z < −1 m, at any x → **Miss**; an unhit ball that settles or times out first → **Miss**;
  - **hit** ball (including edges and glances behind the wicket) that leaves the ground, settles or times out → **"Hit, practice scoring pending"**, no runs. A hit ball is never a Miss.
- Measured carry (first landing on pitch or outfield after contact) still works and is labelled the same.
- NetMode rules are unchanged.

**Performance measurement.**
- **Unity Profiler:**
  1. Make a development build (`CricketVR/Build/Quest APK (Development)`, with Autoconnect Profiler).
  2. Open Window > Analysis > Profiler and choose the Quest under the target selector (over USB via adb).
  3. In the Rendering module, read Batches/SetPass/Triangles; in Memory, read Texture memory; in CPU, read the frame time.
- **OVR Metrics Tool** (install from the Meta Horizon Store on the headset): enable the persistent overlay and record FPS, CPU and GPU frame time, and GPU utilization during 2 minutes of batting.
- **Record actual numbers** in validation.md with fixed foveated rendering **on and off**; the table estimate above is not evidence.
- **Pass:**
  - 72 FPS sustained (≥ 99% of frames at 72);
  - GPU frame time < 11 ms and CPU < 11 ms;
  - draw calls ≤ 150;
  - triangles < 250k;
  - texture memory < 150 MB.

**As built (2026-10-24), beyond the table above.**
- Measured triangle count of the generated ground: about 10k (stands 3.0k, rope 3.1k, fielders 1.3k per hand, outfield 0.9k, boards 0.8k, sky 0.6k, markers 0.5k).
- The outfield disc runs under the boards up to the stands (10 m past the rope), and the sky dome is closed underneath, so the default skybox never shows through.
- `GroundLit` has a per-material fog amount; the sight screens use 0.1, otherwise fog at 80 m turned the black screens mid-grey.
- Realtime shadow casters: the bat, ball, both sets of stumps and the machine. Everything else (scoreboard, debug markers, controller pointer lines, ground) casts none; the thin scoreboard otherwise drew a stray line across the pitch.
- Rolling drag acts only on real outfield contact (separation ≤ 1 mm). PhysX reports surfaces within its ~1 cm contact offset as touching, and the outfield is 2 mm below the pitch.
- **Calibration check (conflict with the brief's "within about 1 cm"):** with the outfield present, every preset bounces at exactly the same point as without it (shift 0.00 cm). The absolute distance from the solver's target was already 2.6–10.1 cm before M6.5 (PhysX contact timing; for example 6.0 cm on the default delivery). That is covered by the AC-04 limit of 10 cm on the default delivery. The 1 cm figure applies to the solver's own simulated trajectory (DeliverySolverTests). The M6.5 test therefore asserts "outfield moves the bounce < 1 cm".
- **Outfield and fast balls:** a 45 m/s ball never passes through or ends up under the 1 m outfield. PhysX CCD does let it dip up to about 21 cm into the box for one 7 ms step before it bounces out (measured at angles of −5° to −85°). Removing the dip would mean changing the ball's collision mode, which could shift the delivery calibration, so it is recorded as a limitation.
- **Performance probe:** `PerformanceProbe` (development builds) logs FPS, frames on time, CPU and GPU frame time, draw calls, SetPass calls, triangles and texture memory every 10 s. The build option "Ground mode, foveation A-B measurement" alternates fixed foveated rendering between 0.5 and off every 30 s, so one play session yields AC-14 numbers for both. Frame timing stats are on in Player Settings.

## Requirement traceability

| Requirement | Plan sections | Tasks | Acceptance |
| --- | --- | --- | --- |
| REQ-001 | §1, §2, §3, §4 | T001–T006 | AC-01 |
| REQ-002 | §5 | T007–T009 | AC-02 |
| REQ-003 | §8 | T010, T012 | AC-03 |
| REQ-004 | §9 | T013–T015 | AC-04 |
| REQ-005 | §9 | T013, T015 | AC-04 |
| REQ-006 | §10 | T016–T017, T020 | AC-05 |
| REQ-007 | §8, §10 | T011, T016, T018, T020 | AC-03, AC-05 |
| REQ-008 | §6, §7, §11 | T021, T024 | AC-06 |
| REQ-009 | §6 | T022, T024 | AC-06 |
| REQ-010 | §6 | T023–T024 | AC-06 |
| REQ-011 | §11 | T025–T026, T028 | AC-07 |
| REQ-012 | §11 | T026, T028 | AC-07 |
| REQ-013 | §11 | T025, T028 | AC-08 |
| REQ-014 | §8, §11 | T027–T028 | AC-09 |
| REQ-015 | §5, §10, §11 | T008, T019, T029 | AC-02, AC-05 |
| REQ-016 | §13, §14 | T030–T032 | AC-10 |
| REQ-017 | §15 | T036–T041 | AC-11 |
| REQ-018 | §15 | T033, T036 | AC-11 |
| REQ-019 | §15 | T034, T036, T042, T045 | AC-12 |
| REQ-020 | §15 | T043, T045 | AC-12 |
| REQ-021 | §15 | T035, T044, T045 | AC-13 |
| REQ-022 | §15 | T033, T041 | AC-13 |
| REQ-023 | §15 | T046 | AC-14 |
| REQ-024 | §15 | T037–T040 | AC-11 |

## Open owner decisions

1. ~~Zone geometry and boundary-exit rule (§6)~~: accepted by the owner 2026-10-24 (PC-01).
2. ~~Unity 6 LTS patch version~~: resolved 2026-10-23, 6000.3.27f1 (see decisions log).
3. **Default delivery length.** The proposal is a bounce 6 m from the stumps at 80 km/h. It gets confirmed after M4 measurement, with slower deliveries used for early M5 batting.

## Decisions log

| Date | Decision | Reason | Affects |
| --- | --- | --- | --- |
| 2026-10-23 | Create plan.md baseline (this document) | Missing from starter package | All |
| 2026-10-23 | Bat impulse owned by `BatBallContactResolver`, not the physics solver | Spec requires one contact authority and bounded, explainable response | REQ-006, REQ-007 |
| 2026-10-23 | Session timers in `Update` with a generation counter, no coroutines | Restart and pause must cancel all pending work deterministically | REQ-013, REQ-014 |
| 2026-10-23 | Keep fixed timestep 0.02 s until measured at M5 | CLAUDE.md: measure before reducing | REQ-007, REQ-016 |
| 2026-10-23 | Fixed timestep 1/144 s (max allowed 0.05 s), superseding the 0.02 s default | Measured in M4 (E-015): CCD did not stop the ball sinking into the pitch at 50 Hz; bounce error 40 cm -> 8.4 cm worst at 144 Hz; 2 steps per 72 Hz frame | REQ-004, REQ-005, REQ-007, REQ-016 |
| 2026-10-23 | Impact sounds from the owner's own recording instead of synthesis; stronger, rhythm-coded haptics | Owner: synthesised sound metallic then rubbery, too quiet; vibration same for all shots (E-022, E-023) | REQ-015 |
| 2026-10-23 | Bat contact: single swept detector + resolver; PhysX Ball/BatProxy collision disabled | Ball passed through the bat with PhysX contact on Quest (E-019); one authority makes duplicate impulses impossible | REQ-006, REQ-007 |
| 2026-10-23 | Pitch bounciness 0.55 -> 0.72, friction 0.30 -> 0.20 | Owner: ball 'dying' and lengths not distinct; a real pitch is less lively, game feel chosen (E-018) | REQ-005, REQ-015 |
| 2026-10-23 | Unreachable delivery check uses 30 km/h for a 6 m length (minimum ~37.5 km/h from a 2 m release) | 40 km/h was reachable with a lofted trajectory | REQ-005 |
| 2026-10-23 | TextMeshPro essential resources committed under Assets/TextMesh Pro; imported with Unity's -importPackage flag | AssetDatabase.ImportPackage is asynchronous and never completes in batch mode | REQ-015 |
| 2026-10-23 | Unity 6000.3.27f1; packages resolved by Package Manager for that Editor (OpenXR 1.18.0, XRI 3.6.1, XR Management 4.7.0, Input System 1.20.1, URP 17.3.0) | Newest 6.3 LTS patch offered by Hub; resolves open decision 2 | REQ-001 |
| 2026-10-23 | Project created with `-createProject -cloneFromTemplate` (Universal 3D 17.0.14) in a scratch folder, then moved here; template tutorial/sample scene/project-wide input actions removed; unused packages (Visual Scripting, Multiplayer Center, Collab, AI Navigation, Timeline) removed | CLAUDE.md: keep only what the prototype uses | REQ-001 |
| 2026-10-23 | Quest quality level uses `Mobile_RPAsset` changed to HDR off, MSAA 4x, render scale 1.0; SSAO feature removed from the desktop renderer | Plan §3; render scale 0.8 blurs the ball; SSAO triggers an OpenXR performance warning | REQ-001, REQ-015, REQ-016 |
| 2026-10-23 | OpenXR latency mode "Prioritize Input Polling" | Lower controller-pose latency for batting | REQ-003, REQ-007 |
| 2026-10-23 | XRI Starter Assets locomotion providers disabled in the scene, not deleted | Stationary game; the prefab stays unmodified for future XRI updates | REQ-002 |
| 2026-10-23 | M2 geometry constants in `CricketVR.Core/FieldDimensions.cs`; layers Pitch (6), Boundary (7), Stumps (8) | One source of truth for builder, bowling and scoring | REQ-002, REQ-004, REQ-009 |
| 2026-10-23 | Translucent net panels instead of a grid texture; scoreboard 2.4 m away rather than 3 m; stumps single solid box | Quest aliasing; 3 m would sit in the side net; simplest reliable wicket collider | REQ-002, REQ-015 |
| 2026-10-23 | Builder migrates untouched M1 defaults only (floor resize, XR Origin to stance, M1 scale references deactivated as they stand in the lane/swing space) | Preserve owner edits while letting the scene evolve | REQ-002 |
| 2026-10-23 | Owner alignment baked: right hand tilt 25, face 5, lean 0, spin 10, slide 0, face flipped (spin and face flip rotate about the bat's own length, applied last); left hand mirrored guess, unverified | Owner-approved in headset (E-011, E-013) | REQ-003 |
| 2026-10-23 | Realistic procedural bat mesh (blade, grip, splice, sticker, willow grain texture) generated by `BatMeshGenerator`; no downloaded models | Owner request ("looks like a toy"). Explicit owner instruction overrides CLAUDE.md's primitives-first guidance for the bat; no third-party assets per CLAUDE.md §14. Visible face kept on the proxy's face plane | REQ-003, REQ-015 |
| 2026-10-23 | Default bat frame from the owner's reference mount: toe along grip +Z (past the ring), face parallel to the button face (grip +Y); in-headset 3-axis + slide alignment, final values to be baked into GameConfig | Owner rejected the palm-facing frame (E-010) | REQ-003 |
| 2026-10-23 | Bat follows dedicated grip-pose trackers (TrackedPoseDriver on devicePosition/deviceRotation), not the Starter Assets controllers (aim pose); handle along grip +Z, face along the palm; face turn wraps with an in-game 180 degree flip | Aim pose is tilted relative to the handle: caused the reversed (E-008) and then 20-30 degree offset (E-009) bat | REQ-003 |
| 2026-10-23 | Proceed with M2 while the left controller is out of service | Owner request; left controller is a hardware issue (OVRRemoteService: paired, Searching, battery unknown) | REQ-001, REQ-003 |
| 2026-10-24 | Swing assist: bat contact-point velocity x1.4 in the response (`batSpeedGain`); sweet spot 0.16 -> 0.20 m from the toe, falloff 0.17 -> 0.22 m, off-centre e 0.28 -> 0.35 | Owner: hard swings "not powerful enough" (E-028). Quest log E-022: most contacts 0.42-0.56 m above the toe (sweet factor <= 0.10, e 0.16-0.30) at contact-point bat speeds 1.9-11.3 m/s; a weightless controller is swung slower than a real bat. A game assist, not a physical claim; toe and shoulder hits remain weaker than the middle | REQ-007 |
| 2026-10-24 | Practice zones = regions of the net wall colliders (`NetWall`), classified from the contact point; boundary exit by position 0.3 m beyond a wall plane; ScoreManager at execution order -45 processes the previous step's events before bat contact; dead-ball linger 1.5 s | PC-01 accepted; avoids triggers intercepting before physical net contact and keeps events chronological without timestamps | REQ-008, REQ-009, REQ-010 |
| 2026-10-24 | M6.5 cricket ground ahead of M7, behind a NetMode/GroundMode switch; decisions GD-1 to GD-12 in proposals/M6.5-cricket-ground.md (black sight screens; GroundMode results: unhit past z < -1 m or stopped = Miss, any hit = scoring pending; BallLeftGround at the rope, hide after 1.5 s, safety despawn below outfield or > 150 m) | Owner request; explicit owner instruction overrides the CLAUDE.md primitives-first default. NetMode keeps M6 and all tests intact until the virtual-field scorer | REQ-017–REQ-024 |
| 2026-10-24 | M6.5 as built: mode switch by object lists (no hierarchy moves); left-ground = rope crossing only; sight screens 10% fog; calibration test = outfield shift < 1 cm (absolute error 2.6-10.1 cm pre-existing); outfield dip up to ~21 cm for one step at 45 m/s accepted (no tunnelling); development-build PerformanceProbe with a foveation A/B option | Keeps NetMode and tests unchanged; honest about the brief's 1 cm figure; avoids changing ball CCD (calibration) | REQ-019, REQ-020, REQ-021, REQ-023 |
