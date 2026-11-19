# Browser build and hosting

Play at **https://graysonwills.github.io/haunter-in-the-dark/** using a desktop keyboard/mouse or the browser's touch controls. The browser and Windows builds include all four levels and use the same gameplay code.

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

Select **Play in browser** to load the game, then use the game's start button or Enter. On desktop, a click on the canvas captures the mouse if the browser supports it. If mouse capture is unavailable, drag to look and use the keyboard to move. Escape opens the pause menu. Audio starts after interaction with the page. Fullscreen is available from the page button.

Use WASD to move. Hold **either Shift key or C** to crouch, and hold **R** to sprint. **Shift + W** moves forward quietly; **R + W** runs forward. These browser bindings avoid the Ctrl + W tab-closing shortcut. Crouching takes priority if both crouch and sprint are held. R still restarts the game after an ending. The Windows player retains Ctrl/C for crouching and Shift for sprinting.

Hold left mouse while carrying an object to charge it. The ring around the crosshair fills and the object draws back. The aim arc shows the intended flight before any collision. Press **Space** or right mouse while charging to reveal nearby obstacles with the cane.

### Touch controls on phones and tablets

Touch mode activates automatically on compatible touch devices. Use the **Touch controls** toggle before beginning or while paused to change input modes. Append `?touch=1` to force touch mode, or `?touch=0` to choose keyboard/mouse controls.

- Drag the **left stick** to move; drag an empty area of the view to look.
- Tap **Cane** to send an echo. Tap **Pick up** or **Exit** when the corresponding interaction is available.
- Hold **Throw** to charge, then release. Tap **Cane** with a second finger to reveal surfaces while charging. Tap **Drop** to put down the held object.
- Tap **Crouch** to toggle quiet movement; hold **Sprint** while moving to run.
- Tap **Pause** to open the menu. Its large buttons provide **Resume**, **Start again**, and **Skip tutorial & resume**. The touch title screen offers **Skip tutorial**; the desktop title labels that option **Play without tutorial**.

Multiple touches can move, look and use actions together. The tutorial prompts adapt to touch input and limit controls to the current lesson, including pickup and throwing while game time is frozen. Touch mode does not request mouse capture or pause because mouse capture is unavailable. Leaving the browser window or hiding the page still pauses the game and clears held controls. Pointer cancellation clears movement and cancels a charged throw rather than throwing accidentally.

The game requires **WebGL 2 and WebAssembly**. Use a current browser. On iPhone and iPad, landscape gives the touch controls more room. The fullscreen control keeps the touch overlay with the game and uses an expanded page view when browser fullscreen is unavailable. The renderer uses a lower pixel ratio in touch mode to reduce the load on mobile graphics hardware.

Phone-sized layouts are tested with iPhone profiles in WebKit. Testing on physical iPhone and iPad hardware is still pending.

Audio uses browser playback of the packaged Unity sound effects. If the browser cannot initialize Web Audio, use the visible echoes to play. The tutorial and creature still respond to sound events.

## Diagnostics

Append `?debug=1` to expose read-only game state at `window.haunterState` and `window.render_game_to_text()`, including touch mode, the tutorial stage and freeze state. The same snapshot is shown below the game for inspection. Use `?debug=1&touch=1` for a normal interactive touch-mode check.

Append `?smoke=1` to run the gameplay/rendering checks, or `?tutorialSmoke=1` to exercise the guided opening in the actual WebGL player. Select **Play in browser** to start the selected suite. Its final report appears below the game and is available at `window.haunterSmokeReport` and in the browser console. Normal visits do not run either test sequence. These scripted checks disable ordinary player input and bypass the mouse-capture pause guard, so a separate normal touch-mode run is required to verify touch input and the pause fix.
