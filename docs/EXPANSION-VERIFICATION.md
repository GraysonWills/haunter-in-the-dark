# Expansion verification

The four-level development build uses Unity 2023.1.14f1 and the same echo renderer and input bridge as the archive release.

## Native tests

- The [Windows expansion run](evidence/expansion-windows-report.json) passed 33 checks. It crossed real exit triggers, tested the collapsed crossing with physics and the rebuilt NavMesh, timed an inspection after arrival, and observed the listener returning to a reachable patrol point.
- The ballroom check used repeatable debug sound events to compare the estimated source with the real source. It verified fixed collision and navigation and exercised the reduced-distortion setting.
- The maze run staged the player at the authored throwing position. Two actual physical props landed on opposite sides and occupied the guard and roamer for a measured 3.90 seconds of overlap. Normal walking reached the live exit with both catches active.
- The [original Windows tutorial](evidence/expansion-tutorial-windows-report.json) passed 87 checks. The browser touch adapter passed 19 JavaScript tests.

Run from the Unity project folder in PowerShell:

```powershell
.\Build.ps1 -Target Windows
.\Test-Expansion.ps1
.\Test-Tutorial.ps1
node --test Tests/touch-controls.test.cjs
```

## Browser checks

Build with `Build.ps1 -Target WebGL` and serve `Builds/WebGL` over HTTP. The opt-in `?expansionSmoke=1` suite reports the same scripted scenarios and their fixtures. `?debug=1&level=2&touch=1` starts a developer preview of the courtyard. Levels 3 and 4 can be selected the same way. `observer=1` adds the listener camera and its audible estimates in those developer previews.

The [WebGL expansion run](evidence/expansion-webgl-report.json) repeated all 33 campaign checks. The browser matrix also passed 50 archive gameplay checks, and exercised Chrome, Edge, Firefox, and portrait and landscape iPhone WebKit profiles. The Ballroom's reduced-distortion control retained its setting after the WebKit touch pause and resume flow. Physical iPhone and iPad testing remains open.
