# Validation and evidence

## Current truth

As of 2026-10-23: the Unity project exists, its Editor scripts compile, OpenXR project validation reports 0 errors and 0 warnings for Android, and a development APK builds. **Nothing has been installed on or tested with the Quest yet.** Game implementation (M2+), headset testing, and performance profiling are not yet performed.

Documentation checks confirm that requirements map to tasks and scenarios. That is not game validation.

## Acceptance register

| Scenario | Requirements | Status | Evidence/build | Next action |
| --- | --- | --- | --- | --- |
| AC-01 | REQ-001 | Partial: 4/5 owner checks pass; left controller not working (cause not isolated) | APK sha256 2076861…a47fe; E-001–E-004 | Owner: test left controller in Quest Home, then in CricketVR |
| AC-02 | REQ-002, REQ-015 | **Pass** | APK sha256 f35ebef…f8192; E-005, E-006 | Re-check if the stance, net or scoreboard moves (M3/M7) |
| AC-03 | REQ-003, REQ-007 | **Pass (right controller)**; left-hand bat pending hardware | APK sha256 9a0e7fc…2663b; E-007–E-014 | Re-run left-hand checks when the left controller works |
| AC-04 | REQ-004, REQ-005 | **Pass on Quest** | APK sha256 b8f7d8d…9ace3; E-015–E-019 | Re-check if physics rate or pitch material changes |
| AC-05 | REQ-006, REQ-007, REQ-015 | **Pass on Quest (right controller)**: scripted matrix + owner tests 1-5 | APK sha256 fe6daeb…0800e; E-020–E-029 | Re-check if contact math, physics rate or bat alignment changes; left hand pending hardware |
| AC-06 | REQ-008, REQ-009, REQ-010 | **Pass on Quest** (NetMode): automated + owner M6 test 1 | APK sha256 225a148…00ee7; E-030, E-031 | Superseded later by the virtual-field scorer; NetMode kept until then |
| AC-07 | REQ-011, REQ-012 | Not run | None | M7 six-delivery loop |
| AC-08 | REQ-013 | Not run | None | M7 restart checks |
| AC-09 | REQ-014 | Not run | None | M7 interruption checks |
| AC-10 | REQ-016 | Not run | None | M8 regression/profile |
| AC-11 | REQ-017, REQ-018, REQ-024 | Editor preview renders checked (layout, black screen, no z-fighting at 1 cm near plane); headset run pending | APK sha256 c6b7ff9…0e41e; E-032 | Owner: M6.5 headset tests |
| AC-12 | REQ-019, REQ-020 | Automated physics checks pass; headset run pending | APK sha256 c6b7ff9…0e41e; E-032 | Owner: M6.5 headset tests |
| AC-13 | REQ-021, REQ-022 | Automated rules + NetMode regression pass; headset run pending | APK sha256 c6b7ff9…0e41e; E-032 | Owner: M6.5 headset tests |
| AC-14 | REQ-023 | Not run | None | M6.5 on-device performance (foveation on and off) |

## Evidence entry template

Copy for each executed check:

```text
Check ID / acceptance scenario:
Requirement IDs:
Date and tester:
Git commit or build identifier:
Unity and package versions:
Environment: static review / EditMode / PlayMode / APK build / Quest device
Configuration (speed, pose, seed, handedness, refresh rate as applicable):
Steps or exact command:
Expected result:
Actual result and measured values:
Outcome: Pass / Fail / Blocked / Partial
Log or artifact path:
Remaining limitation / follow-up task:
```

## Evidence log

```text
Check ID / acceptance scenario: E-001 / T001–T004 setup (supports AC-01)
Requirement IDs: REQ-001
Date and tester: 2026-10-23, agent
Git commit or build identifier: uncommitted working tree (first commit pending owner)
Unity and package versions: Unity 6000.3.27f1; URP 17.3.0; Input System 1.20.1; XR Management 4.7.0; OpenXR 1.18.0; XRI 3.6.1; XR Core Utils 2.6.0
Environment: Editor batch mode (compilation + project validation)
Configuration: Android, IL2CPP, ARM64, min API 32, Vulkan, Linear, ASTC; OpenXR + Meta Quest Support + Oculus Touch + Meta Quest Touch Plus profiles; Single Pass Instanced; Prioritize Input Polling
Steps or exact command: Unity -batchmode -projectPath . -executeMethod CricketVR.Editor.XrProjectConfigurator.ConfigureBatch, then MainSceneBuilder.BuildOrRepairBatch (run twice)
Expected result: no compile errors; 0 OpenXR validation errors; scene contains exactly one XR Origin; second builder run adds nothing
Actual result and measured values: no compile errors; "OpenXR validation: 0 error(s), 0 warning(s)"; scene created with Floor, Cube_1m, Post_1.8m, one XR Origin (XR Rig) with floor tracking origin and all 10 locomotion providers disabled; second run "Main scene already complete; nothing added."
Outcome: Pass (static/Editor checks only)
Log or artifact path: batch logs in the agent session scratchpad (not kept in repo)
Remaining limitation / follow-up task: Needs on-device AC-01. One earlier failure fixed: the OpenXR 1.18.0 Meta Quest validation rule threw a NullReferenceException in batch mode because selectedBuildTargetGroup stayed Standalone; the bootstrap now sets it to Android.
```

```text
Check ID / acceptance scenario: E-002 / T005 APK build (supports AC-01)
Requirement IDs: REQ-001
Date and tester: 2026-10-23, agent
Git commit or build identifier: Builds/CricketVR.apk, 56,307,154 bytes, sha256 207686185d625c9b30efee767300da6de73ae67684273b766328fd8f3f9a47fe
Unity and package versions: as E-001
Environment: APK build (batch mode)
Configuration: Development build, scene Assets/CricketVR/Scenes/Main.unity
Steps or exact command: Unity -batchmode -projectPath . -executeMethod CricketVR.Editor.QuestBuild.Build
Expected result: build succeeds; APK is arm64-only with Quest VR manifest entries
Actual result and measured values: "Build Succeeded ... 0 error(s), 0 warning(s)"; aapt2: package com.cricketvr.prototype, native-code arm64-v8a, targetSdk 36, categories LAUNCHER + com.oculus.intent.category.VR, android.hardware.vr.headtracking, com.oculus.supportedDevices, com.oculus.vr.focusaware
Outcome: Pass (build only; not proof of headset behavior)
Log or artifact path: Builds/CricketVR.apk (gitignored)
Remaining limitation / follow-up task: `adb devices` listed no device on 2026-10-23; install + AC-01 pending.
```

```text
Check ID / acceptance scenario: E-003 / T005 install + launch (partial AC-01, log evidence only)
Requirement IDs: REQ-001
Date and tester: 2026-10-23, agent (owner connected the headset)
Git commit or build identifier: Builds/CricketVR.apk sha256 207686185d625c9b30efee767300da6de73ae67684273b766328fd8f3f9a47fe
Unity and package versions: as E-001
Environment: Quest device (Quest 3 "eureka", Android 14 / API 34, build UP1A.231005.007.A1), USB/ADB
Configuration: development build, default 72 Hz
Steps or exact command: adb install -r Builds/CricketVR.apk; adb shell monkey -p com.cricketvr.prototype -c com.oculus.intent.category.VR 1; adb logcat -d --pid=<pid>
Expected result: installs; OpenXR session reaches FOCUSED; controller profiles load; no app errors
Actual result and measured values: "Success" install; session IDLE->READY->SYNCHRONIZED->VISIBLE->FOCUSED; profiles loaded: Meta Quest Touch Plus, Oculus Touch; only error lines are OpenXR-Loader "Could access neither the installable nor system runtime broker", followed by a successful fallback to /odm/etc/openxr/1/active_runtime.aarch64.json. VrApi: FPS=72/72, Tear=0, Stale=0, App=1.98 ms, CPU&GPU=5.04 ms (idle placeholder scene; not an AC-10 performance result)
Outcome: Partial. App launch and session are confirmed from logs; visual stereo, head tracking, controller tracking, floor scale, and untethered launch still need owner observation
Log or artifact path: device logcat (not stored)
Remaining limitation / follow-up task: owner headset checklist for AC-01
```

```text
Check ID / acceptance scenario: E-004 / AC-01 owner headset observations
Requirement IDs: REQ-001
Date and tester: 2026-10-23, owner
Git commit or build identifier: Builds/CricketVR.apk sha256 207686185d625c9b30efee767300da6de73ae67684273b766328fd8f3f9a47fe
Unity and package versions: as E-001
Environment: Quest device, manual observation
Configuration: default 72 Hz; tethered, then untethered relaunch from App Library > Unknown Sources
Steps or exact command: owner checklist: (1) stereo/head tracking, (2) floor height and scale references, (3) both controllers tracked, (4) no thumbstick locomotion, (5) untethered relaunch
Expected result: all five pass
Actual result and measured values: owner reported (1) pass, (2) pass, (4) pass, (5) pass. (3) right controller tracked fine; left controller "not working right now". Cause not yet isolated: controller hardware/battery vs app.
Outcome: Partial
Log or artifact path: owner report in session
Remaining limitation / follow-up task: owner to check the left controller in Quest Home. If it fails there too (hardware), fix the battery/pairing and re-check in CricketVR. If it works in Home but not in the app, capture logcat and debug.
```

```text
Check ID / acceptance scenario: E-005 / T007–T008 practice net (supports AC-02)
Requirement IDs: REQ-002, REQ-015
Date and tester: 2026-10-23, agent
Git commit or build identifier: Builds/CricketVR.apk sha256 f35ebefbeedb6281cc65c24b5197b2dc4d70bf4dfa3ea742ee2441bba24f8192 (uncommitted tree)
Unity and package versions: as E-001
Environment: Editor batch mode (scene builder), EditMode tests, APK build, install on Quest
Configuration: right-handed stance (-0.3, 0, 1.0); release point (0, 2.0, 18.9)
Steps or exact command: MainSceneBuilder.BuildOrRepairBatch (twice); Unity -batchmode -runTests -testPlatform EditMode; QuestBuild.Build; adb install -r
Expected result: builder idempotent; layout tests pass; build clean; installs
Actual result and measured values: first run added layers Pitch/Boundary/Stumps, pitch, creases, stumps, launcher, net colliders and visuals, stance markers, scoreboard placeholder, and moved the XR Origin to the stance; second run "nothing added". EditMode 11/11 passed: one XR Origin; floor tracking + stance pose; stump collider 0.711 x 0.2286 m on the ground at the origin; stumps 20.12 m apart; pitch top 0-5 mm; launcher has no colliders; net walls on the documented planes; sight line clear at eye heights 1.4/1.6/1.9 m; scoreboard 1.5-3.5 m away and >30 degrees off the delivery line. Build: 0 errors, 0 warnings, OpenXR validation 0/0. adb install Success. Launch not observed: headset asleep (not worn).
Outcome: Pass for scripted/build checks; headset AC-02 pending
Log or artifact path: batch logs and editmode.xml in the agent session scratchpad
Remaining limitation / follow-up task: owner AC-02 check; confirm how Quest recentering interacts with the Floor tracking origin
```

```text
Check ID / acceptance scenario: E-006 / AC-02 owner headset observations
Requirement IDs: REQ-002, REQ-015
Date and tester: 2026-10-23, owner
Git commit or build identifier: Builds/CricketVR.apk sha256 f35ebefbeedb6281cc65c24b5197b2dc4d70bf4dfa3ea742ee2441bba24f8192
Unity and package versions: as E-001
Environment: Quest device, manual observation, right controller only
Configuration: right-handed stance; view reset (hold Meta button) while facing the launcher at the centre of the play area
Steps or exact command: owner checklist (a) on the stance marker behind the crease, stumps behind/right; (b) stumps knee height, pitch and net proportionate; (c) launcher clearly visible, nothing blocking; (d) scoreboard up-right, readable, off the delivery line; (e) natural head motion, nothing moves on its own
Expected result: all pass
Actual result and measured values: owner reported a-e all pass
Outcome: Pass
Log or artifact path: owner report in session
Remaining limitation / follow-up task: left-handed stance not yet exercised (M3); scoreboard text readability is checked in M7
```

```text
Check ID / acceptance scenario: E-007 / T010–T011 tracked bat (supports AC-03)
Requirement IDs: REQ-003, REQ-007
Date and tester: 2026-10-23, agent
Git commit or build identifier: Builds/CricketVR.apk sha256 247cc26576ee54093ae0aa35c6b7511e93d424f770110bad77bef8f469bdda13 (uncommitted on top of 3ab266e)
Unity and package versions: as E-001
Environment: Editor batch mode, EditMode + PlayMode tests, APK build, install on Quest
Configuration: GameConfig defaults: grip offsets 0; limits gap 0.1 s, grip 25 m/s, 60 rad/s, suppress 0.1 s, 3 samples, velocity window 0.03 s
Steps or exact command: MainSceneBuilder.BuildOrRepairBatch (twice); -runTests EditMode; -runTests PlayMode; QuestBuild.Build; adb install -r
Expected result: builder idempotent; tests pass; build clean
Actual result and measured values: builder added BatProxy layer (9), GameConfig asset, Bat (BatController, HandednessSetup, BatSetupControls), BatVisual, kinematic BatPhysicsProxy, and deactivated both Teleport Interactors; second run "nothing added". EditMode 27/27 passed (adds pose history velocity with uneven intervals, rad/s angular velocity, shortest path across quaternion sign flip, 0.05 degree precision, point velocity w x r, recentre jump resets with no velocity across it, rotation jump, time gap, tracking loss/recovery, fast plausible swing accepted (12 m/s, 25 rad/s), NaN rejected, bat frame mirroring). PlayMode 3/3 passed at 72 Hz: proxy follows grip within 2 mm / 0.5 degrees via MovePosition; toe speed = w*r within 10%; 2 m teleport resets and suppresses contacts. A first PlayMode run failed 3/3 ("Rotation quaternions must be unit length"): FixedUpdate ran before any pose was sampled; fixed by gating the proxy on a sampled pose. Build: 0 errors, 0 warnings, OpenXR validation 0/0. Install Success.
Outcome: Pass for scripted/build checks; headset AC-03 pending
Log or artifact path: EditMode.xml / PlayMode.xml in the agent session scratchpad
Remaining limitation / follow-up task: AC-03 on Quest; left-handed bat hand needs the left controller (hardware fault)
```

```text
Check ID / acceptance scenario: E-008 / AC-03 first headset attempt (build 247cc26…)
Requirement IDs: REQ-003, REQ-007
Date and tester: 2026-10-23, owner + agent (device log)
Git commit or build identifier: Builds/CricketVR.apk sha256 247cc26576ee54093ae0aa35c6b7511e93d424f770110bad77bef8f469bdda13
Unity and package versions: as E-001
Environment: Quest device, right controller; adb logcat
Configuration: right-handed, default grip
Steps or exact command: owner held the bat, nudged alignment, swung, reset view; agent read the Unity log
Expected result: bat hangs naturally in a bat-handle grip; recentre resets history; no false fast swings
Actual result and measured values: FAIL on grip: the bat was 180 degrees reversed (blade up in a normal grip; owner had to hold the controller upside down). Owner hit the +/-45 degree tilt clamp trying to correct it. Log: 11 OriginChanged resets on Meta-button recentres, 7 FocusChanged, 20 TrackingLost; 26 swing summaries with peak toe 3.0-8.9 m/s and no ImplausibleMotion resets; no swing logged immediately after a recentre.
Outcome: Fail (grip orientation); recentre gating behaved as designed
Log or artifact path: device logcat summary in session
Remaining limitation / follow-up task: Fixed in build c6e08b439bf25eb9332a7ebf1d7bcf1319d1a0f2110d5ef423243467e9f85fa5: blade along controller +Z; face turn unclamped with a 180 degree flip (hold stick click); saved alignments moved to a v2 key. EditMode 27/27, PlayMode 3/3, build clean. Re-check AC-03, including which side the face points.
```

```text
Check ID / acceptance scenario: E-009 / AC-03 second attempt (build c6e08b4…) and root cause
Requirement IDs: REQ-003
Date and tester: 2026-10-23, owner + agent
Git commit or build identifier: Builds/CricketVR.apk sha256 c6e08b439bf25eb9332a7ebf1d7bcf1319d1a0f2110d5ef423243467e9f85fa5
Unity and package versions: as E-001
Environment: Quest device, right controller; inspection of XRI Default Input Actions
Configuration: right-handed, default grip
Steps or exact command: owner held the bat in a normal grip
Expected result: bat in line with the controller handle
Actual result and measured values: FAIL: bat at roughly 20-30 degrees to the handle (owner estimate); owner nudges only reached tilt -8..0, face 4..8. Root cause: the Starter Assets controller transforms follow pointerPosition/pointerRotation (the aim pose) first, which is tilted relative to the handle. This also explains the earlier 180 degree reversal (E-008).
Outcome: Fail
Log or artifact path: XRI Default Input Actions.inputactions (XRI Right/Left Position/Rotation bindings)
Remaining limitation / follow-up task: Fixed in build bfc96e2184897a8e4da404842dfeec69e78b2f81a2cfdbd24452bf54c7d9b071: new Left Grip / Right Grip objects with TrackedPoseDriver on devicePosition/deviceRotation (grip pose, UpdateAndBeforeRender); bat bound to them; bat frame back to handle along grip +Z; saved alignments moved to a v3 key. Regression test BatFollowsGripPoseTrackersNotAimPose added. EditMode 28/28, PlayMode 3/3, build clean. Re-check AC-03.
```

```text
Check ID / acceptance scenario: E-010 / AC-03 third attempt (build bfc96e2…, grip pose)
Requirement IDs: REQ-003
Date and tester: 2026-10-23, owner
Git commit or build identifier: Builds/CricketVR.apk sha256 bfc96e2184897a8e4da404842dfeec69e78b2f81a2cfdbd24452bf54c7d9b071
Unity and package versions: as E-001
Environment: Quest device, right controller
Configuration: grip pose; handle along grip +Z, face along palm
Steps or exact command: owner held the bat in a normal grip
Expected result: bat orientation matches the owner's intent
Actual result and measured values: FAIL: "completely off again". Owner supplied a reference photo (a commercial VR bat mount): the bat runs along the controller handle, the blade extends past the ring end, and the blade is parallel to the controller's button face. Owner asked for joystick control of the orientation.
Outcome: Fail
Log or artifact path: owner photo in session
Remaining limitation / follow-up task: Build 45addb12d87462439b03020b7469b2173ede3c0c85039e50d5b72d281960e53e: default bat frame from the reference (toe along grip +Z, face normal along grip +Y, blade starts 9 cm past the grip); three rotation axes (tilt, face turn, lean) plus slide, 5 degree / 1 cm steps with repeat; saved alignments v4. EditMode 29/29, PlayMode 3/3. Owner to align and report; agent reads the final values from the device log and bakes them into GameConfig.
```

```text
Check ID / acceptance scenario: E-011 / AC-03 owner alignment (build 45addb1…)
Requirement IDs: REQ-003
Date and tester: 2026-10-23, owner + agent (device log)
Git commit or build identifier: Builds/CricketVR.apk sha256 45addb12d87462439b03020b7469b2173ede3c0c85039e50d5b72d281960e53e
Unity and package versions: as E-001
Environment: Quest device, right controller
Configuration: reference-mount default frame
Steps or exact command: owner aligned with the thumbstick controls; agent read the last logged values
Expected result: owner-approved orientation
Actual result and measured values: final right-hand values tilt 35, face 5, lean 0 degrees, slide 0 cm. Owner: "the last configuration is perfect, just flip the bat" and confirmed (multiple choice) this means swapping face and back, not reversing end-to-end.
Outcome: Pass for orientation (right hand) after flip
Log or artifact path: device logcat in session
Remaining limitation / follow-up task: Build 18bec10bf28ce636008cbc87dacbef0dd788f5e627e5a1dd213c47fb381b7e78 bakes right hand tilt 35 / face 5 / lean 0 / slide 0 / faceFlipped into GameConfig. The face flip is now a 180 degree spin about the bat's own length applied last; the previous in-game flip added 180 to the face turn, which would have mirrored the tilt. Left hand is set to a mirrored guess (tilt 35, face -5, flipped), unverified until the left controller works. Saved alignments moved to v5. EditMode 30/30, PlayMode 3/3, build clean.
```

```text
Check ID / acceptance scenario: E-012 / T012 spin control (owner request)
Requirement IDs: REQ-003
Date and tester: 2026-10-23, agent
Git commit or build identifier: Builds/CricketVR.apk sha256 1c4d2ceee2525d22bafdf9e2299be97dbc62936ef874eaa4d1a9f679b41900e6
Unity and package versions: as E-001
Environment: EditMode + PlayMode tests, APK build, install
Configuration: baked right-hand alignment (tilt 35, face 5, flipped), spin 0
Steps or exact command: -runTests EditMode/PlayMode; QuestBuild.Build; adb install -r
Expected result: thumbstick left/right twists the bat about its own visible handle without changing its angle
Actual result and measured values: owner asked for clockwise/anticlockwise rotation and chose "spin around handle". The existing face turn rotated about the pre-tilt axis, so with 35 degrees of tilt it wobbled instead of twisting. Added `spin`, applied last about the bat's own length; thumbstick left/right now spins; face turn moved to grip + left/right. Test SpinTwistsAboutTheTiltedHandleWithoutChangingTheBatAngle added. EditMode 31/31, PlayMode 3/3, build clean, install Success.
Outcome: Pass (scripted/build); headset feel pending
Log or artifact path: EditMode.xml / PlayMode.xml in the agent session scratchpad
Remaining limitation / follow-up task: owner to try spin in the headset; bake any final spin value
```

```text
Check ID / acceptance scenario: E-013 / final owner alignment + realistic bat (owner request)
Requirement IDs: REQ-003, REQ-015
Date and tester: 2026-10-23, owner + agent
Git commit or build identifier: Builds/CricketVR.apk sha256 9a0e7fcf50cf543f0a490968e1cc92d0a4e876bd6981b40c2d913d95c762663b
Unity and package versions: as E-001
Environment: device log (alignment), Editor batch mode (mesh generation, preview renders), EditMode/PlayMode tests, APK build, install
Configuration: baked right hand tilt 25, face 5, lean 0, spin 10, slide 0, face flipped (owner: "now the bat position is perfect"); left hand mirrored guess (tilt 25, face -5, spin -10, flipped), unverified
Steps or exact command: read the last alignment log line; MainSceneBuilder (generates Models/Bat*.asset and WillowGrain.png); BatPreviewRenderer.Render; -runTests; QuestBuild.Build; adb install -r
Expected result: alignment baked; realistic bat with the visible face on the proxy face; tests and build pass
Actual result and measured values: procedural bat: flat face, curved back with spine (peak 62 mm, 0.20 m above the toe), edges 38 mm, sloped shoulders, rounded toe, willow grain texture (mipmapped), ribbed oval grip, V splice on both sides, unbranded sticker band. Preview renders (face/back/side) checked by the agent. Mesh normal check: all 1083 face vertices +Z. Found and fixed: accent stripe mesh had a single vertex row (no triangles); the preview's first render used stale light (a preview-only artifact). New test VisibleBladeFaceMatchesPhysicsProxyFace (face plane at z = +0.02, width 0.108 m, toe, depth 55-67 mm). Builder second run "nothing added". EditMode 32/32, PlayMode 3/3, build clean, install Success.
Outcome: Pass (scripted/build/preview); owner visual check pending
Log or artifact path: Builds/Preview/Bat_{Face,Back,Side}.png (gitignored)
Remaining limitation / follow-up task: the back of the visual blade is up to 42 mm behind the face while the proxy box is 40 mm thick (20 mm behind the face): back-of-bat contacts differ slightly; M5 decides whether the collider follows the spine.
```

```text
Check ID / acceptance scenario: E-014 / AC-03 owner headset check (realistic bat build)
Requirement IDs: REQ-003, REQ-007, REQ-015
Date and tester: 2026-10-23, owner + agent (device log)
Git commit or build identifier: Builds/CricketVR.apk sha256 9a0e7fcf50cf543f0a490968e1cc92d0a4e876bd6981b40c2d913d95c762663b
Unity and package versions: as E-001
Environment: Quest device, right controller
Configuration: baked alignment (tilt 25, face 5, spin 10, flipped)
Steps or exact command: owner: bat look and position; defensive pushes and 5 full swings; Meta-button recentre (red flash); hold A to switch handedness and back
Expected result: all pass
Actual result and measured values: owner reported all pass. Device log (last ~40 s; 256 KiB buffer had rolled over): 3 OriginChanged resets, Handedness Left then Right with HandChanged resets, TrackingLost resets around recentres. Swing summaries for this build were not captured (rolled off the buffer); swing measurement evidence comes from E-008 (26 swings, peak toe 3.0-8.9 m/s, no false fast swings) and PlayMode tests.
Outcome: Pass for right controller. Left-handed bat hand not exercised (left controller hardware fault).
Log or artifact path: device logcat in session
Remaining limitation / follow-up task: left controller: bat alignment (mirrored guess), handedness with the left bat hand. Capture swing logs to a file on the Mac for future device checks.
```

```text
Check ID / acceptance scenario: E-015 / physics-rate measurement for ball bounce (supports AC-04)
Requirement IDs: REQ-004, REQ-005
Date and tester: 2026-10-23, agent
Git commit or build identifier: uncommitted on top of c327387
Unity and package versions: as E-001
Environment: PlayMode (temporary probe, since removed), PhysX via Unity 6000.3.27f1
Configuration: plain sphere r 0.036 m, ContinuousDynamic, pitch box with bounciness 0.55 / friction 0.30 Maximum; release (0, 2.0, 18.9); solver refined for each rate
Steps or exact command: for each rate, bowl 60/80/100 km/h x lengths 3.5/4.7/6/7.3/9 m; record z where vertical velocity turns upward and the lowest ball-centre height
Expected result: bounce near the requested length without the ball entering the pitch
Actual result and measured values: worst |error| over 15 deliveries: 50 Hz 40.4 cm (centre down to -4.7 cm, i.e. sinking below the surface despite CCD); 72 Hz 23.9 cm (-3.4 cm); 90 Hz 19.7 cm (-1.7 cm); 120 Hz 13.6 cm; 144 Hz 8.4 cm (lowest centre +1.2 cm); 180 Hz 7.7 cm; 216 Hz 7.7 cm; 240 Hz 7.7 cm (consistently +3 to +4 cm). Diagnosis: at 50 Hz the ball penetrates the pitch and is resolved at the end of the step, quantizing the bounce to the step grid.
Outcome: Informational; selected 144 Hz
Log or artifact path: probe logs in the agent session scratchpad
Remaining limitation / follow-up task: Quest CPU cost of 144 Hz physics to be measured in M8
```

```text
Check ID / acceptance scenario: E-016 / AC-04 scripted (T013-T015)
Requirement IDs: REQ-004, REQ-005
Date and tester: 2026-10-23, agent
Git commit or build identifier: Builds/CricketVR.apk sha256 44ee900a06733dc9dc3f40124a6425d0a848998f3c81a7edfb8f0a8ab5490a97 (uncommitted on top of c327387)
Unity and package versions: as E-001
Environment: EditMode + PlayMode tests (real PhysX, 144 Hz), APK build, install
Configuration: default delivery 80 km/h, line 0, length 6 m, release (0, 2.0, 18.9); presets in GameConfig
Steps or exact command: MainSceneBuilder (x3 incl. TMP import via -importPackage); -runTests EditMode/PlayMode; QuestBuild.Build; adb install -r
Expected result: AC-04: 10 identical deliveries bounce within 0.25 m of the first; length/line change the bounce; speed units correct; one ball; unreachable reported
Actual result and measured values: EditMode 40/40 (solver: exact requested speed; continuous landing within 5 mm; stepped landing within 1 cm for 3.5/6/9 m; fuller length = higher elevation; line moves bounce; 30 km/h to 6 m rejected with minimum ~37.5 km/h and zero velocity; invalid inputs rejected). PlayMode 9/9: 10 identical deliveries max spread from first 0.00 cm, max error from target 5.97 cm; lengths 3.5/6/9 m bounced at 3.474/5.940/8.916 m, line +0.25 at x 0.251; release speed 27.78 m/s for 100 km/h; relaunch cancels the previous delivery (Cancelled event, no events for the old id, one BallController); timeout parks the ball. Collision matrix verified in ProjectSettings: Ball only with Pitch/Boundary/Stumps/BatProxy; BatProxy only with Ball. Builder idempotent. Build 0 errors / 0 warnings; install Success. Earlier failures fixed on the way: test fixture lists not cleared between tests; 40 km/h unreachable case was actually reachable; parked ball rendered in the sight line.
Outcome: Pass (scripted/build); headset AC-04 pending
Log or artifact path: EditMode.xml / PlayMode.xml in the agent session scratchpad
Remaining limitation / follow-up task: headset check of the ten-delivery repeatability and readout; bat contact is still PhysX's default (M5 replaces it)
```

```text
Check ID / acceptance scenario: E-017 / AC-04 owner tests 1-2 on Quest (readout, repeatability)
Requirement IDs: REQ-004, REQ-005, REQ-015
Date and tester: 2026-10-23, owner + agent (device log)
Git commit or build identifier: Builds/CricketVR.apk sha256 44ee900a06733dc9dc3f40124a6425d0a848998f3c81a7edfb8f0a8ab5490a97
Unity and package versions: as E-001
Environment: Quest device, right controller; adb logcat (4 MB buffer), VrApi
Configuration: 144 Hz physics; GameConfig debug presets
Steps or exact command: owner test 1 (scoreboard readout readable from the stance; stance correct) and test 2 (repeated deliveries, bat held clear)
Expected result: readable readout; identical deliveries bounce within 0.25 m of the first
Actual result and measured values: owner: test 1 pass, test 2 pass. Log shows ~58 deliveries cycling through all presets in order (consistent with tap B rather than tap A; to be confirmed with the owner). Per preset, measured bounce spread from the first delivery was 0.00 cm in every case: Full n=10 z 3.499 (0.1 cm from target); Short n=10 z 9.070 (7.0 cm); Outside off n=10 x +0.250 z 5.998 (0.2 cm); Quick 100 km/h n=10 z 6.592 (9.2 cm); Slow 60 km/h n=9 z 5.000 (0.0 cm); Good length n=9 z 5.998 (0.2 cm). Unreachable preset rejected each cycle. VrApi: FPS 72-73/72, App 2.3-2.6 ms, CPU&GPU 5.8-6.9 ms.
Outcome: Pass (repeatability and accuracy on device)
Log or artifact path: device logcat in session
Remaining limitation / follow-up task: owner tests 3-5; confirm which button triggered the cycling
```

```text
Check ID / acceptance scenario: E-018 / AC-04 owner tests 3-5 and bounce tuning
Requirement IDs: REQ-004, REQ-005, REQ-015
Date and tester: 2026-10-23, owner + agent
Git commit or build identifier: tested build 44ee900a06733dc9dc3f40124a6425d0a848998f3c81a7edfb8f0a8ab5490a97; retuned build b8f7d8dd37987a9c0b5fdc4e27b8bed81e6e0f64bf524c02af18f808bc89ace3
Unity and package versions: as E-001
Environment: Quest device (owner); PlayMode with the shipped PitchSurface material (agent)
Configuration: 80 km/h presets; pitch material before: bounciness 0.55, friction 0.30/0.40; after: 0.72, 0.20/0.30
Steps or exact command: owner test 3 (presets look different), test 4 (unreachable rejected), test 5 (visibility, speed, bounce, gentle blocks); agent measured ball-centre height at the crease (z = 1.2 m)
Expected result: presets distinguishable; impossible request refused with no ball; readable, believable deliveries
Actual result and measured values: owner: test 3 pass (all six presets), test 4 pass (no ball, REJECTED message, A repeats the refusal, B wraps to Good length). Test 5: visibility fine, 80 km/h feels right, gentle blocks show no odd behaviour (PhysX default bat response), but bounce "dying on the surface" and lengths should differ in height (full knee or below, short rising high). Measured height at the crease before: modelled 0.40/0.60/0.68 m (full/good/short). Retuned pitch to bounciness 0.72, friction 0.20/0.30: PlayMode measured full 0.58 m, good 0.95 m, short 1.20 m. Intermediate trials: 0.75 -> 0.62/1.01/1.30; 0.68 -> 0.54/0.87/1.06. New PlayMode test ShorterLengthsArriveHigherAtTheBatter (full <= 0.6, good 0.7-1.05, short >= 1.1, ordered). PlayMode 10/10, EditMode 40/40, build clean, install Success.
Outcome: Pass for tests 3-4; test 5 bounce retuned, owner re-check pending
Log or artifact path: PlayMode logs in the agent session scratchpad
Remaining limitation / follow-up task: 0.72 is livelier than a real pitch (a game-feel choice); owner to confirm in the headset
```

```text
Check ID / acceptance scenario: E-019 / AC-04 bounce re-check (build b8f7d8d…)
Requirement IDs: REQ-004, REQ-005, REQ-007
Date and tester: 2026-10-23, owner
Git commit or build identifier: Builds/CricketVR.apk sha256 b8f7d8dd37987a9c0b5fdc4e27b8bed81e6e0f64bf524c02af18f808bc89ace3
Unity and package versions: as E-001
Environment: Quest device, right controller
Configuration: pitch bounciness 0.72, friction 0.20/0.30; 144 Hz physics
Steps or exact command: owner bowled full, good and short lengths and attempted blocks
Expected result: bounce heights feel right by length
Actual result and measured values: owner: "bounce seems good now". Also: "blocking is not working at all. The ball is passing through the bat." The bat-ball hit is still PhysX's default kinematic-vs-dynamic contact (no custom detection yet); a thin (40 mm) kinematic blade against a ball moving ~15 cm per 144 Hz step is the failure CLAUDE.md §8 predicts. Earlier gentle blocks (build 44ee900, lower bounce) were reported fine.
Outcome: AC-04 Pass. Tunneling observed: input for M5 (REQ-007).
Log or artifact path: owner report in session
Remaining limitation / follow-up task: M5 T016-T018: own swept contact detection and resolver; log every bat-ball encounter
```

```text
Check ID / acceptance scenario: E-020 / AC-05 scripted contact matrix (T016-T020)
Requirement IDs: REQ-006, REQ-007, REQ-015
Date and tester: 2026-10-23, agent
Git commit or build identifier: Builds/CricketVR.apk sha256 7260e1f98134e1a3e9c162feece191b093c17640d4f21eff3e1d495cc7c1d61e (uncommitted on top of c988eac)
Unity and package versions: as E-001
Environment: EditMode; PlayMode with real BatController/BallController/BatBallContactResolver, 144 Hz physics, 72 Hz frames, scripted controller driver
Configuration: GameConfig defaults (e 0.50 sweet / 0.28 off-centre, edge x0.55 + 14 deg, tangential loss 0.25, cap 45 m/s); ball 20-24 m/s; 10 runs per case varying x +/-2 cm, height +/-2 cm, speed
Steps or exact command: -runTests EditMode; -runTests PlayMode
Expected result: AC-05: no missed collision or duplicate impulse in the matrix; attack faster than soft defence; finite bounded results
Actual result and measured values: EditMode 51/51 (sweep catches a 0.6 m/step crossing, a 120-degree rotation through a stationary ball that both end poses miss, initial overlap; beside-blade miss; stationary face e x speed; soft < block < drive; face angle -> direction; loft -> launch; toe weaker; edge deflects and loses pace; separating ignored; 5000 random contacts finite and <= 45 m/s). PlayMode 17/17. Matrix, each 10/10 runs with exactly one contact: stationary block mean exit 10.8 m/s (all back toward the bowler); soft defence (bat -2.5 m/s) 7.1 m/s vs drive (bat 14 m/s) 31.8 m/s; lofted drive (face +25 deg) 26.7 m/s at 36.8 deg launch; off-centre toe sweet factor 0.55 and slower than the middle; rotation-dominated swing 25 rad/s (~20 degrees per frame) 30.2 m/s, no tunnelling; initial overlap resolved once with the ball ending in front of the face; contact during an origin-change reset not applied. Collision matrix saved: Ball with Pitch/Boundary/Stumps only, BatProxy with nothing. Build 0 errors / 0 warnings; install Success.
Outcome: Pass (scripted). Scripted success does not prove all human swings are covered.
Log or artifact path: EditMode.xml / PlayMode.xml in the agent session scratchpad
Remaining limitation / follow-up task: owner headset tests (blocks, drives, lofts, off-centre, fast swings, feel, audio/haptics); physics bat may trail the visual by about one frame in fast swings
```

```text
Check ID / acceptance scenario: E-021 / M5 test 1 on Quest: ball passed through the bat (build 7260e1f…)
Requirement IDs: REQ-003, REQ-007
Date and tester: 2026-10-23, owner + agent (device log)
Git commit or build identifier: Builds/CricketVR.apk sha256 7260e1f98134e1a3e9c162feece191b093c17640d4f21eff3e1d495cc7c1d61e
Unity and package versions: as E-001
Environment: Quest device, right controller; adb logcat
Configuration: swept resolver; tracked state from Input System binding <XRController>{RightHand}/isTracked
Steps or exact command: owner blocked ~10 good-length deliveries with a still bat
Expected result: every ball hits the bat
Actual result and measured values: FAIL: "ball is still passing through as before". Log: zero "Bat contact" and zero near-miss lines; no swing summaries for the whole session; TrackingLost resets after startup. BatController treated the controller as untracked, so HasStepPoses was false and the resolver (by design) did nothing. The Input System isTracked binding did not report the tracked state of the controller driving the grip pose. No swing summaries had been captured since the switch to grip-pose trackers (E-014 also had none), consistent with this.
Outcome: Fail; root cause in tracked-state source
Log or artifact path: device logcat in session
Remaining limitation / follow-up task: Fixed in build e04774ae0d0999890b7d7bc60c301db21f48e108d1bb520468aa225eca8ceb9d: tracked state read from UnityEngine.XR InputDevices at the hand's XRNode first, Input System binding only as fallback; diagnostics log added.
```

```text
Check ID / acceptance scenario: E-022 / M5 test 1 re-run (build e04774a…): blocking and feedback
Requirement IDs: REQ-006, REQ-007, REQ-015
Date and tester: 2026-10-23, owner + agent (device log)
Git commit or build identifier: Builds/CricketVR.apk sha256 e04774ae0d0999890b7d7bc60c301db21f48e108d1bb520468aa225eca8ceb9d
Unity and package versions: as E-001
Environment: Quest device, right controller
Configuration: as E-020 with the tracked-state fix
Steps or exact command: owner blocked good-length deliveries
Expected result: no pass-through; sound and vibration on contact
Actual result and measured values: owner: "blocking is working". Log: 24 bat contacts; near misses with gaps 1.3-9.7 cm (balls that genuinely passed by). Most contacts 0.42-0.56 m above the toe (top of the blade; sweet factor 0.00-0.10, e 0.16-0.30) because good-length balls now arrive about hip high; bat point speeds 1.9-11.3 m/s; exit 8-20 m/s. Owner feedback: (1) impact sound "metallic, like two metal objects", and the same for every shot; should vary with connection and bat region. (2) vibration present but the same for every shot; should vary with connection. Diagnostics lines were not in the captured buffer.
Outcome: Pass for blocking (REQ-007 pass-through fixed on device); feedback needs redesign (REQ-015)
Log or artifact path: device logcat in session
Remaining limitation / follow-up task: Build b8337157030441312e8f9114b16fd5e720a6aaf1da1551f661d4300df97710e1: modal-synthesis wooden sounds (middle "tock", off-centre "thud", edge "click", 3 variants each) blended by sweet/edge factors with loudness by impact speed; haptic patterns by connection (middle light short pulse, off-centre stinging buzz, edge sharp tick). Owner re-check pending. Many contacts high on the blade: review sweet-spot height vs good-length bounce height with the owner.
```

```text
Check ID / acceptance scenario: E-023 / M5 feedback re-check (build b833715…) and switch to recorded sounds
Requirement IDs: REQ-015
Date and tester: 2026-10-23, owner + agent
Git commit or build identifier: tested b8337157030441312e8f9114b16fd5e720a6aaf1da1551f661d4300df97710e1; new b830c577e3b265847ecd004754d1e0e218c4dcc660782ab4b00e14f44b28e52a
Unity and package versions: as E-001
Environment: Quest device (owner); Mac speakers for A/B listening; Python analysis
Configuration: modal-synthesis sounds; haptics 0.3-0.6
Steps or exact command: owner tested contacts in the headset; agent played four synthesised candidates on the Mac; owner supplied their own recording ("Sector 66.m4a", 24 s, owner's recording, approved for use)
Expected result: wooden, varied sound and vibration
Actual result and measured values: owner: synthesised bat "rubbery", edges "clicky", volume low; vibration low and the same for all shots. Recording analysis: ~20 impacts, spectral centroid ~2.8-3.8 kHz, ring ~90-100 ms to -20 dB, dominant ~570 Hz, all peaks clipped (9-61 samples). Three least-clipped hits (1.148, 3.045, 4.488 s) cut as middle clips; off-centre = low-pass 1.3 kHz x2, pitch -15%, faster decay; edge = 60 ms attack high-passed at 2.5 kHz. Owner approved the clips after listening on the Mac. Tools/audio/derive_bat_sounds.py reproduces all 9 clips byte-for-byte. Game: AudioSource spatial blend 0.4 (was 1), loudness 0.45-1 by approach speed (was 0.2-1); clips PCM, decompress on load. Haptics: amplitude 0.55-1 (was 0.3-0.6) with distinct rhythms (middle one 40 ms pulse; off-centre full-strength jolt then fading buzz ~0.26 s; edge two 20 ms ticks 50 ms apart). EditMode 51/51, PlayMode 17/17, build clean, install Success.
Outcome: Fail for synthesised feedback; replaced, re-check pending
Log or artifact path: Assets/CricketVR/Audio/Bat*_N.wav; Tools/audio/derive_bat_sounds.py
Remaining limitation / follow-up task: owner re-check in the headset
```

```text
Check ID / acceptance scenario: E-024 / M5 test 1 re-check: impact sound and vibration by connection
Requirement IDs: REQ-015
Date and tester: 2026-10-24, owner
Git commit or build identifier: b830c577e3b265847ecd004754d1e0e218c4dcc660782ab4b00e14f44b28e52a
Unity and package versions: as E-001
Environment: Quest 3 standalone, right hand
Configuration: recorded clips (Tools/audio/derive_bat_sounds.py); spatial blend 0.4; loudness 0.45-1; haptics 0.55-1 with per-kind rhythm
Steps or exact command: Good length, A to bowl; middle, high-on-blade, toe and edge contacts
Expected result: middle sounds like a real bat; middle/off-centre/edge distinguishable by sound and vibration; volume and strength adequate
Actual result and measured values: owner: "all pass". Device log not captured (Unity logcat buffer empty on read).
Outcome: Pass (owner observation)
Log or artifact path: none
Remaining limitation / follow-up task: M5 tests 2-5 (drives, lofts, off-centre/edge direction, fast swings)
```

```text
Check ID / acceptance scenario: E-025 / M5 test 2: drives (straight, off, on; push vs full drive)
Requirement IDs: REQ-006, REQ-007
Date and tester: 2026-10-24, owner
Git commit or build identifier: b830c577e3b265847ecd004754d1e0e218c4dcc660782ab4b00e14f44b28e52a
Unity and package versions: as E-001
Environment: Quest 3 standalone, right hand
Configuration: Good length, default speed
Steps or exact command: 3-4 straight drives, 2-3 off drives, 2-3 on drives, one gentle push vs one full drive
Expected result: direction follows the face; full drive clearly faster than a push; no implausible launch or tunnelling
Actual result and measured values: owner: "all pass". Device log not captured (Unity lines were no longer in the logcat buffer when read), so no per-shot speeds or counts.
Outcome: Pass (owner observation)
Log or artifact path: none
Remaining limitation / follow-up task: M5 tests 3-5
```

```text
Check ID / acceptance scenario: E-026 / M5 test 3: lofted shots
Requirement IDs: REQ-006, REQ-007
Date and tester: 2026-10-24, owner
Git commit or build identifier: b830c577e3b265847ecd004754d1e0e218c4dcc660782ab4b00e14f44b28e52a
Unity and package versions: as E-001
Environment: Quest 3 standalone, right hand
Configuration: Good length, default speed
Steps or exact command: 2 ground drives (face down), 3-4 lofted drives (face open, low-to-high), 2 full-effort lofted hits
Expected result: open face + upward swing lifts the ball, closed face keeps it low; believable launch angles; no implausible launch or tunnelling
Actual result and measured values: owner: "all pass". No Unity lines in logcat: the game wrote no log output after build b830c57 was installed (logging worked on the earlier build, E-021).
Outcome: Pass (owner observation)
Log or artifact path: none
Remaining limitation / follow-up task: per-shot device log measurements missing for E-025-E-027 (see open items)
```

```text
Check ID / acceptance scenario: E-027 / M5 test 4: off-centre and edge direction and pace
Requirement IDs: REQ-007
Date and tester: 2026-10-24, owner
Git commit or build identifier: b830c577e3b265847ecd004754d1e0e218c4dcc660782ab4b00e14f44b28e52a
Unity and package versions: as E-001
Environment: Quest 3 standalone, right hand
Configuration: Good length, default speed
Steps or exact command: firm drives: 2 middle, 2-3 toe, 2-3 high on the blade, 3-4 outer/inner edge
Expected result: toe and high-blade hits slower than middle; edges deflect sideways; no off-centre hit faster than middle or at a wild angle
Actual result and measured values: owner: "all pass". Logcat at 15:31 showed the game had not been opened since the agent closed it at 15:28 (relaunch was blocked: controllers asleep), so no device log for this test.
Outcome: Pass (owner observation)
Log or artifact path: none
Remaining limitation / follow-up task: per-shot device log measurements missing for E-025-E-027 (see open items)
```

```text
Check ID / acceptance scenario: E-028 / M5 test 5: fast swings and feel; swing-power tuning
Requirement IDs: REQ-006, REQ-007
Date and tester: 2026-10-24, owner + agent
Git commit or build identifier: tested b830c577e3b265847ecd004754d1e0e218c4dcc660782ab4b00e14f44b28e52a; new fe6daebde87b29468639e2ef7605ddfbefe405b902f9e8e208efa17389e0800e
Unity and package versions: as E-001
Environment: Quest 3 standalone, right hand; Mac batch-mode tests
Configuration: tested: e 0.50 sweet (0.16 m from toe, falloff 0.17) / 0.28 off-centre, no assist. New: sweet 0.20 m, falloff 0.22, off-centre 0.35, batSpeedGain 1.4
Steps or exact command: 6-8 full-speed swings, 2-3 late whipped hits, 3-4 at one step faster bowling
Expected result: no tunnelling or double hits; hard swings powerful but believable
Actual result and measured values: owner: no ball passed through the bat and none was hit twice; hard swings "not powerful enough". No device log for this test (game not logging / not running when read). Change: swing assist and wider sweet area (plan.md decisions log). Estimated at a 10 m/s contact-point swing vs a 22 m/s ball: high on the blade (0.45 m from toe) 19 -> 28 m/s exit; middle 26 -> 32 m/s; stationary block unchanged. Scripted matrix: drive (bat 14 m/s) 31.8 -> 40.0 m/s; soft defence 5.5 m/s; stationary block 10.8 m/s (unchanged); lofted drive 32.9 m/s at 33.6 deg; toe slower than middle (sweet 0.54); all cases 10/10 single contacts. EditMode 52/52 (new: swing assist speeds up drives, leaves a stationary block alone). PlayMode 17/17 on 3 consecutive runs after a harness fix: a run is repeated (max 2, logged) only if the Editor stalled > 0.1 s during it, because the tracker then suppresses contacts by design (two earlier failures each coincided with such a stall). Build Success, install Success.
Outcome: Partial: tunnelling/double-hit check passes (owner observation); power fails, retuned, re-check pending
Log or artifact path: none
Remaining limitation / follow-up task: owner re-check of hard-swing power; confirm the game writes to logcat on the new build
```

```text
Check ID / acceptance scenario: E-029 / M5 test 5 re-check: hard-swing power (AC-05 sign-off)
Requirement IDs: REQ-006, REQ-007, REQ-015
Date and tester: 2026-10-24, owner + agent
Git commit or build identifier: fe6daebde87b29468639e2ef7605ddfbefe405b902f9e8e208efa17389e0800e
Unity and package versions: as E-001
Environment: Quest 3 standalone, right hand, USB connected for logcat
Configuration: batSpeedGain 1.4; sweet 0.20 m from toe, falloff 0.22; e 0.50 sweet / 0.35 off-centre
Steps or exact command: 4-5 full-power drives, 2-3 gentle pushes, 1-2 dead-bat blocks (owner also played further balls)
Expected result: hard swings powerful but believable; pushes and blocks soft
Actual result and measured values: owner: "all pass". Logcat (game logging again on this build; tracking diagnostics: XR node valid, isTracked True, step poses True): 136 bat contacts (#2-#140), 55 near misses (closest gaps 2.0-13.4 cm), 0 contacts ignored for suppressed motion, 0 duplicate contacts. Exit speeds up to 35.5 m/s (128 km/h), none at the 45 m/s cap; blocks and pushes (contact-point bat speed 0.5-2.8 m/s) 5.5-14.8 m/s; firm swings (bat 6.5-14 m/s) 16-34 m/s. Contacts still mostly 0.40-0.56 m above the toe. A toe contact (0.06 m from toe) now has sweet factor ~0.65 because of the wider falloff, so toe hits are less penalised than before; owner accepted.
Outcome: Pass (owner observation + device log). AC-05 passes for the right controller.
Log or artifact path: adb logcat -s Unity (session 2026-10-24 15:50-16:05 IST)
Remaining limitation / follow-up task: left-hand bat checks pending hardware; physics bat may trail the visual by about one frame in fast swings; many blocks leave at steep downward angles (face angled down), worth watching in M6 scoring
```

```text
Check ID / acceptance scenario: E-030 / AC-06 automated: outcomes, practice zones, finalize-once, carry (T021-T024)
Requirement IDs: REQ-008, REQ-009, REQ-010
Date and tester: 2026-10-24, agent
Git commit or build identifier: uncommitted working tree; APK sha256 225a1480a15d45195c20664f80a5fdb2fa4de15d2dbe6bdba91eac7fa7800ee7
Unity and package versions: as E-001
Environment: Mac batch-mode Test Runner; Quest install only
Configuration: PC-01 accepted (spec.md); GameConfig.scoring defaults (front <1 m 2, 1-3 m 4, >=3 m 6; side/rear 1; over front 6, over side/rear 1; exit margin 0.3 m; dead-ball linger 1.5 s)
Steps or exact command: Unity -batchmode -runTests -testPlatform EditMode|PlayMode; scene rebuilt with CricketVR.Editor.MainSceneBuilder.BuildOrRepairBatch (added NetWall to the 4 walls, Gameplay/ScoreManager); QuestBuild.Build; adb install
Expected result: every playable delivery finalizes once; bowled 0; unhit zone contacts 0; timeout/exit terminate; carry only when measured
Actual result and measured values: EditMode 76/76 (24 new DeliveryAdjudicatorTests: half-open front-net bounds at 0.999/1.0/2.999/3.0 m, run table, exit side + corner priority, unhit zone/exit never scores, bowled 0 not a miss, contact-then-bowled 0 with both flags, stumps beat net in the same step, front-low beats side net, net beats exit, finalize-once against later stumps/timeout/contact, stale contact and stumps ignored, first contact only, cancel leaves no result, settle/timeout fallback, carry from contact to first post-contact landing (10.0 m case), net before or in the same step as the landing = net hit, totals derived from records). PlayMode 28/28 (11 new ScoringPlayModeTests with real physics, one result each: unhit at stumps Bowled; unhit into back net Miss/RearNet; timeout Miss; hits aimed at 0.5/2/4.5 m on the front net -> FrontLow 2 / FrontMiddle 4 / FrontUpper 6 with carry "net hit"; landing before the net measured carry 6.9 m vs ballistic 7.0 m (tolerance 0.15 m); clearing the front net 6, carry N/A; side net 1; contact then bowled 0 with both flags; drop-and-settle NoRuns; relaunch before a result leaves no record; decided ball is dead (ApplyBatHit refused) then parks with reason Finalized). BatControllerPlayModeTests.SteadySwing now repeats a swing only after an Editor frame stall (TimeGap), like the AC-05 harness. Build Success, install Success.
Outcome: Pass (automated). Headset AC-06 not run.
Log or artifact path: scratchpad test XML (not committed)
Remaining limitation / follow-up task: PhysX has no rolling resistance, so a soft forward block can roll to the front net and score 2 rather than settling (to observe with the owner); bails are not dislodged visually
```

```text
Check ID / acceptance scenario: E-031 / AC-06 headset: M6 test 1 results on the scoreboard
Requirement IDs: REQ-008, REQ-009, REQ-010
Date and tester: 2026-10-24, owner + agent
Git commit or build identifier: APK sha256 225a1480a15d45195c20664f80a5fdb2fa4de15d2dbe6bdba91eac7fa7800ee7
Unity and package versions: as E-001
Environment: Quest 3 standalone, right hand, USB for logcat
Configuration: NetMode; GameConfig.scoring defaults
Steps or exact command: leave one (bowled), miss one (back net), ground hit to the far net, lofted drive, side-net hit, soft forward block; owner kept playing
Expected result: exactly one result per ball matching what happened; totals add up
Actual result and measured values: owner: "all pass". Logcat (buffer held the end of one session and a full second one): first session reached #108 with totals Balls 103 / practice runs 99 / contacts 74 / misses 28 / bowled 1 (Balls < ids because cancelled or rejected launches have no record); second session #1-#32. 40 results in the buffer, no duplicate ids, no "cancelled before a result" lines: 39 net hits, 1 stumps; unhit -> RearNet Miss 16, Bowled 1; hits -> SideNet 1 run 22, RearNet 1 run 1; carry: 18 net hit, 5 measured (0.6-0.8 m, balls chopped into the ground then into a side net), 17 not hit. Running totals in each line equal the sum of the listed records. Front-net zone lines were in the earlier, rotated part of the log (95 runs from 70 contacts imply them) and are not counted here. Per owner instruction (GD-1), no further net-scoring tuning.
Outcome: Pass (owner observation + device log)
Log or artifact path: adb logcat -s Unity, 2026-10-24
Remaining limitation / follow-up task: the 3.6 m-wide lane sends most hits into a side net (1 run); net scoring to be replaced by the virtual-field scorer after M6.5
```

```text
Check ID / acceptance scenario: E-032 / M6.5 automated: ground geometry, ground physics, GroundMode rules, previews, build (T033-T045)
Requirement IDs: REQ-017, REQ-018, REQ-019, REQ-020, REQ-021, REQ-022, REQ-024
Date and tester: 2026-10-24, agent
Git commit or build identifier: branch m6.5-ground; APK sha256 c6b7ff9fec7596f131f0df5204a576f28ba27096264730d4d0c7143a8e50e41e (GroundMode, development, foveation A/B every 30 s), 91.7 MB
Unity and package versions: as E-001
Environment: Mac batch-mode Test Runner; Editor preview renders (Builds/Preview/Ground_*.png, camera near plane 1 cm); Quest install
Configuration: GroundDefinition defaults (boundary 65 m circle, rope 6 cm, circle 27.43 m markers ~5 m, black screens 12 x 6 m 5 m past the rope with 10% fog, boards 3 m, stands 9-27 m past the rope 2-12 m high, fog 60-220 m); FieldConfig defaults (third man / fine leg 50 m, GD-3); OutfieldGrass 0.45 / 0.45 / 0.5; rolling drag 0.08 g; shadow distance 25 m, 1 cascade, hard
Steps or exact command: MainSceneBuilder.BuildOrRepairBatch (regenerates Environment/Ground; Outfield layer 11; collision matrix Ball-Outfield); GroundPreviewRenderer.Render; -runTests EditMode/PlayMode; QuestBuild.Build -cricketvrGround -cricketvrFoveationAB; adb install
Expected result: geometry matches the Laws and the accepted decisions; no tunnelling; drag only on the outfield; BallLeftGround once; GroundMode results exactly once; NetMode unchanged
Actual result and measured values: EditMode 91/91 (15 new: pitch/creases/stumps equal FieldDimensions; popping creases at 1.22 m and 18.90 m; 30-yard circle = semicircles + lines, 43 markers evenly spaced; fielder polar -> world incl. left-hand mirror; every FieldConfig fielder >= 3 m inside the rope at its own angle for both hands; circle/ellipse boundary maths; validation rejects 50/80 m radii and the brief's 58 m third man; GroundMode rules: unhit past z < -1 = Miss at any x, hit behind the wicket = pending never Miss, unhit stop/timeout = Miss, stumps beat everything, nets ignored, finalize once, outfield carry, pending totals). PlayMode 38/38 (10 new, real physics): 45 m/s at 10 angles never through or under the outfield (one-step dip up to 20.9 cm at -49 deg, see plan.md); a 5 m/s rolling ball stops on the outfield; rolling on the pitch keeps >= 97% speed over 1 s; outfield moves the delivery bounce 0.00 cm on all 5 reachable presets (absolute target error 2.6 / 6.0 / 5.7 / 10.1 / 8.4 cm, unchanged pre-existing); lob over the rope -> BallLeftGround once, hidden 1.5 s later (reason Finalized), result pending; safety despawn below the outfield and beyond 150 m, one result each; unhit wide ball past the stumps = Miss (PassedStumps); edge behind the wicket = pending; unhit ball that stops = Miss (Settled); stumps = Bowled. All 28 earlier PlayMode and 76 earlier EditMode tests unchanged and passing (NetMode). Preview renders: black sight screen behind the machine, creases crisp with no z-fighting at a 1 cm near plane, no default sky showing, no stray shadows. Generated ground about 10k triangles. OpenXR validation 0/0; Foveated Rendering feature enabled for Android. Build Success, install Success.
Outcome: Pass (automated and Editor preview). Headset AC-11 to AC-14 not run.
Log or artifact path: Builds/Preview/Ground_*.png (gitignored); scratchpad test XML
Remaining limitation / follow-up task: owner headset checks; on-device performance numbers (AC-14) with foveation on and off; bails not dislodged
```

## M6.5 evidence template (AC-11 to AC-14)

Add rows AC-11 to AC-14 to the status table ("Not run"). For each check, record:

```text
Check ID / acceptance scenario: E-0xx / AC-1x <short name>
Requirement IDs: REQ-0xx
Date and tester:
Git commit or build identifier: commit + APK sha256; environment mode (NetMode/GroundMode)
Unity and package versions: as E-001
Environment: Quest 3 standalone / Mac batch tests / Profiler over USB
Configuration: GroundDefinition values changed from default (boundary radius, etc.); FieldConfig; foveation level
Steps or exact command:
Expected result:
Actual result and measured values:
  - Visual (AC-11): scale / creases both ends / flicker yes-no / shimmer yes-no (where) / ball visibility at release (good / ok / poor)
  - Physics (AC-12): bounce error per launch (cm) x10; outfield tunnelling cases n/N; rolling stop distance (m); BallLeftGround count per lofted ball
  - Mode (AC-13): results per delivery (exactly one? y/n); NetMode test counts
  - Performance (AC-14): FPS min/avg; % frames at 72; CPU ms; GPU ms; draw calls (SetPass/batches); triangles; texture memory MB; foveation on/off and level; method (Profiler / OVR Metrics)
Outcome: Pass / Partial / Fail
Log or artifact path:
Remaining limitation / follow-up task:
```

## Rules

- Name the tested build. Re-test impacted scenarios after material changes; old evidence remains historical.
- Separate compilation, scripted simulation, and manual headset observations.
- If the agent cannot operate the headset, request a precise owner check and leave the result pending until reported.
- For AC-04/05, include per-case counts and failure counts, not only 'looks good'.
- For AC-10, include measured CPU/GPU frame times, refresh target, normal-play interval, observed over-budget behavior, and collection method.
- Capture failures and known limitations. Do not substitute an invented measurement or modify acceptance silently.
- Spec changes should list affected requirements/tasks and invalidate relevant prior evidence explicitly.

## Known open items

- Initial read-only environment inspection on 2026-10-23, before tooling was installed (static check, not validation):
  - macOS 26.5.2 on Apple Silicon, ~150 GB free.
  - Unity Hub, Unity Editor, Android SDK/NDK, and `adb` are not installed.
  - System `git` 2.50.1 is present, but the project folder is not a Git repository.
  - Later the same day: Unity Hub 3.22.4 and Editor 6000.3.27f1 (Android SDK/NDK/OpenJDK) were installed, Git was initialized, and the project was created (E-001). Meta Developer Mode and USB debugging remain owner steps.
- Owner decision PC-01 (zone geometry and boundary exit; see `spec.md` and `plan.md` §6) is pending.
- Grip offsets, delivery launch pose/target, zone geometry, and contact tuning need implementation-time selection and measurement.
- Real headset tracking, bat feel, and frame timing require on-device evidence.
