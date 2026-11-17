# Browser touch verification — November 17, 2026

Verification results for the browser touch update (v0.5.0). The Windows download remains v0.4.0.

## Browser input adapter

`node --test Tests/touch-controls.test.cjs` passes **16 tests, 0 failures**. These tests exercise the browser adapter with a small DOM/event harness; they do not emulate Safari or prove physical-device behavior.

Coverage includes touch detection and overrides; independent movement/look pointers; one-shot cane and pickup actions; quick and charged throws; cancellation and lost capture; crouch toggle and held sprint; tutorial action gates; pause and native menu buttons; visibility/orientation interruption; exit interaction; input-mode switching; and the fullscreen fallback.

## Unity build and gameplay suites

The final WebGL build completed successfully with Unity **2023.1.14f1**, process exit code **0**, and **5,447,615 bytes** reported by `HAUNTER_WEBGL_BUILD_OK`. No C# compiler errors were reported. The build includes the tutorial observation-safety correction.

| Check | Result |
|---|---|
| Final WebGL build | Passed; exit code 0, 5,447,615 bytes |
| Guided tutorial suite (`?tutorialSmoke=1`) | **87 passed, 0 failed, 0 runtime errors**; [report](evidence/touch/tutorial-smoke.json) |
| Gameplay/rendering suite (`?smoke=1`) | **50 passed, 0 failed, 0 runtime errors**; [report](evidence/touch/gameplay-smoke.json) |
| Browser release receipt | [Live build metadata](https://graysonwills.github.io/haunter-in-the-dark/build-info.json) records the deployed source commit and asset hashes |

The first final-build guided tutorial suite run timed out after 74 passing checks while the browser was backgrounded. The foreground rerun completed with all 87 checks passing; the timed-out run is not counted as a pass.

The 50-check gameplay suite ran before the final tutorial throw-target timing adjustment; the final build was then covered by the full 87-check tutorial rerun and normal touch tutorial. The tutorial regression now checks that physical proximity cannot capture a player during the forced `WatchDistraction` lesson, and that ordinary proximity capture works again after tutorial completion. Creature travel, physical impacts and the inspection timer continue during observation.

## Normal browser input

Normal touch-mode checks use `?debug=1&touch=1`, without smoke flags. The scripted suites disable ordinary player input and bypass the mouse-capture pause guard, so they cannot replace these checks.

The final build completed the full tutorial through the visible touch controls at **19.92 simulated seconds**. At **32.00 simulated seconds**, the game remained `Playing` with `touchControls=true`, `pointerLocked=false`, and the tutorial `Complete`. This exercises the normal input path beyond the former one-second pause loop. See the [saved state](evidence/touch/normal-tutorial-complete.json).

Observed during this final-build run:

- Stick movement and cane input advanced the opening; drag-to-look produced camera yaw **48.57°** and pitch **11.47°**.
- The physical glass bottle was picked up and thrown. Pause and resume preserved the frozen throw lesson; the actual impact led to inspection, the crouch lesson and completion.
- Crouch movement, restart, and the title's **Skip tutorial** button worked.
- The control overlay remained visible in fullscreen at **1024 × 768 landscape**; see the [capture](evidence/touch/fullscreen-controls.jpg).

Manual drop, sprint, exit, simultaneous multi-touch and orientation handling were not independently exercised in this browser run. Adapter tests cover sprint, exit, simultaneous pointers and orientation handling; drop has no separate recorded test. Adapter tests are a different evidence level from an interactive browser or device test. Portrait layout validation is not recorded here.

Desktop Enter/Resume input was received, but pointer capture was not acquired during browser automation. Desktop keyboard/mouse gameplay and pause/resume therefore do not have a complete end-to-end pass for this update.

## Limits

An actual iPad was not available. Physical iPad multi-touch, Safari behavior, memory usage and device performance remain unverified. Browser touch-mode checks and Node event tests must not be described as physical iPad testing.
