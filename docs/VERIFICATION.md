# Verification — version 0.3, November 11, 2026

Built successfully with the installed Unity 2023.1.14f1 editor for Windows x64 using Direct3D 11 and the built-in renderer. The delivered player is a non-development build.

Before publishing v0.3.0, a fresh copy of `Assets`, `Packages`, `ProjectSettings` and `Build.ps1` was built successfully with no existing Library or build cache. The PowerShell script detected the editor automatically and produced the Windows executable. The release includes a standalone play guide and SHA-256 checksums for both download archives.

The final in-player integration run passed **50 checks**, with **zero failed checks and zero Unity runtime errors/exceptions**. Exact results are preserved in `evidence/smoke-report.json`.

Checks cover scene references, title rendering, beginning gameplay, runtime navigation, exit reachability, visible monochrome cane ripples, sound pulses, 32 throwable props, pickup, persistent held outlines, near-wall viewmodel retraction and actual visible pixels, physics/scale restoration on throw, collision sound, tabletop decoys, bounded noise investigation, self-sound exclusion, hearing range, retargeting, pawstep generation, finite stopping, actual proximity capture, restart and the physical exit threshold.

Version 0.3 verifies reduced hearing, immediate diversion from a fresh player pursuit, full travel to a 7.5 m-away impact, a full five-second timer beginning on arrival, and immunity to player footsteps, cane taps, other objects and repeated bounces during both travel and inspection. The timer is not extended by these sounds. Logged drift and target displacement during the linger are both 0.0000 m. Actual movement remains faster than the player's sprint, and the creature returns to the exit afterward.

A physical escape test moves the CharacterController from (0,0,6) toward the door at the real 1.35 m/s crouch speed while emitting crouched footsteps every metre. A distraction to the right of the door keeps the creature committed until the player crosses the actual exit threshold. This test passes without calling Escape directly.

The saved scene explicitly contains chase speed 5.8, return speed 4.8, paw interval .22, movement hearing scale 80, cane multiplier 14, impact multiplier 6, and object listen time 5. Hearing caps are 28 m for footsteps, 16 m for cane taps and 8 m for impacts. The existing environment was preserved during the targeted scene migration.

Inspected actual captured frames for the title, cane wave, carried bottle (including facing a nearby wall), creature, and ending screens. Also used the normal Windows player UI to verify Enter starts play, Space taps the cane, Escape pauses and releases the cursor, and Start Again returns to the title.

## Limits

This is a one-room prototype with procedural placeholder models/audio. Automated checks cover the core loop but do not replace a full player-driven balance test. Mouse look, movement, crouching and charged throws should receive more playtesting before expanding the level. No validation was performed on macOS, Linux, mobile, URP or HDRP.

Earlier checks caught and corrected HUD transform scaling, held Rigidbody interpolation, near-wall occlusion, unreachable tabletop sound targets, and a partial-stride reset that could suppress footsteps. Live keyboard input is disabled during scripted checks to keep creature behavior tests deterministic. Hidden Windows test windows cannot provide reliable screenshot evidence, so visual tests use a visible player.
