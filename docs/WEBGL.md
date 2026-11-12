# Browser build and hosting

Play at **https://graysonwills.github.io/haunter-in-the-dark/** using a desktop browser, keyboard and mouse. The browser and Windows versions use the same scene and gameplay code.

## Build

1. Install Unity **2023.1.14f1** with **WebGL Build Support** using Unity Hub.
2. Open the project and choose **Haunter → Build browser prototype**. Alternatively, close the editor and run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target WebGL` from the project folder. Use `-UnityPath` if the editor is installed outside its standard location.
3. The complete site is written to `Builds/WebGL`. Build logs are in `Logs/build-webgl.log`.

To preview locally with Python installed:

```powershell
python -m http.server 8000 --directory Builds/WebGL
```

Then open http://localhost:8000/ . Opening the HTML through a `file://` URL will not work.

## Hosting

Upload the **entire contents** of `Builds/WebGL` to a static web host, preserving the paths and filenames. The included `.nojekyll` file makes the output suitable for GitHub Pages. The Unity build uses compressed files with JavaScript decompression fallback, so custom compression headers are not required. The build is single threaded and does not require cross-origin isolation headers.

This repository's live GitHub Pages site publishes from the root of the `codex/pages` branch. Game source stays on the repository's default branch; only generated website files belong on the Pages branch. A new Unity build must be deployed to that branch to update the live game.

## Browser controls

Click **Play** to load the game, then use the game's start button or Enter. A click on the canvas captures the mouse. Escape pauses and releases it; click Resume to continue. Audio starts after interaction with the page. Fullscreen is available from the page button.

Use WASD to move. Hold **either Shift key or C** to crouch, and hold **R** to sprint. **Shift + W** moves forward quietly; **R + W** runs forward. These browser bindings avoid the Ctrl + W tab-closing shortcut. Crouching takes priority if both crouch and sprint are held. R still restarts the game after an ending. The Windows player retains Ctrl/C for crouching and Shift for sprinting.

The game needs WebGL 2 and a keyboard/mouse. Mobile touch controls are not included. The Windows download remains available from the repository's release page.

## Diagnostics

Append `?debug=1` to expose read-only game state at `window.haunterState` and `window.render_game_to_text()`, including the tutorial stage and freeze state. Append `?smoke=1` to run the gameplay/rendering checks, or `?tutorialSmoke=1` to exercise the guided opening in the actual WebGL player. Click **Play in browser** to start the selected suite. Its final report appears below the game and is available at `window.haunterSmokeReport` and in the browser console. Normal visits do not run either test sequence.
