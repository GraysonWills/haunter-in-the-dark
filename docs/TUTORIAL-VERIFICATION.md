# Guided opening verification — 2026-11-12

Windows and WebGL built successfully with Unity 2023.1.14f1.

- Final Windows tutorial run: **86 passed, 0 failed, 0 runtime errors**. See [report](evidence/tutorial-windows-report.json).
- Browser tutorial development run: **85 passed, 0 failed, 0 runtime errors**. See [report](evidence/tutorial-web-report.json). The final Windows suite additionally verifies that the ordinary E-interaction ray reaches the selected object. The final build also enlarges the prompt text and tightens low-frame-rate chase/throw safety.
- Final WebGL gameplay/rendering regression run: **50 passed, 0 failed, 0 runtime errors**. See [report](evidence/tutorial-regression-web-report.json).

Tutorial coverage includes real movement producing the first two footsteps, physical chase, frozen pickup and throw, real impact and full inspection, crouch-required movement, completion, restart after death, pause/resume, cancellation at all ten active stages, and backward/sideways opening routes. A separate impact cannot steal the demonstration. Frozen props remain selectable by the ordinary pickup ray.

The hidden Windows runner is intended for behavioral assertions; hidden Unity windows produce black screen captures. Browser rendering checks verify the actual outlines, held-object visibility and UI. The embedded Codex browser can run the automated suites, but rejects pointer lock for interactive play; use a regular desktop browser for the playable preview.

Reproduce with `Build.ps1`, `Test-Tutorial.ps1`, and `Build.ps1 -Target WebGL`. Serve `Builds/WebGL` locally; `?tutorialSmoke=1` runs the tutorial checks and `?smoke=1` runs the gameplay/rendering regression suite after clicking Play. Results appear below the canvas. A URL without these query parameters runs the normal game.
