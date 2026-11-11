# Haunter in the Dark — quick concept

A first-person, single-room horror prototype about navigating by sound. This is a fictional sensory mechanic inspired by earth-sensing imagery, not a simulation of blindness.

## Scope
- Windows keyboard/mouse; Unity 2023.1.14f1 with the built-in render pipeline.
- A cluttered abandoned archive with an exit at the far end, four-legged stalker, and scattered throwable cans, bottles, and stones.
- White outlines on black. Multiple expanding sound waves reveal per-surface outlines with a short afterglow. Floor rings communicate propagation. Held objects and the cane remain outlined.
- Walking, crouch walking, cane taps, and thrown impacts make different-strength sounds. A loud reveal helps navigation but draws the creature.
- Version 0.3: a faster-than-player creature guards the exit with reduced hearing ranges. A nearby object impact can interrupt a player pursuit. It commits to the full route to that impact, ignores other noises during travel and a five-second linger on arrival, then returns to the door. Player sounds outside a distraction still use finite pursuit budgets. Paw sounds reveal nearby geometry but never attract the creature itself. Major obstacles affect navigation.
- A persistent top exit compass, contextual controls, restart, pause, caught and escaped states.

## Rendering decision
Use a world-space echo mask in an opaque surface shader, followed by depth/normal edge detection. This avoids whole-object reveals and works on primitives, physics props, and an animated procedural creature. A single material supports floor rings and a persistent held-object mask. A URP renderer feature would add unnecessary setup for this installed editor; barycentric wireframes would expose triangulation on rounded props.

## Implementation plan
1. Rendering/audio: shared NoiseSystem events, up to 32 simultaneous shader pulses, synthesized spatial sound, surface mask and outline composite.
2. Player: CharacterController, grounded distance-based steps, crouch, cane, physics pickup/throw, always-visible hand prop, game-state HUD.
3. World/creature: procedural authored room, many props, exit trigger, baked runtime navigation around major clutter, procedural four-paw animation and bounded noise investigation.
4. Integration: generated editable Unity scene, Windows build, automated in-engine smoke checks and rendered evidence; document controls and tuning.

## Shared interfaces
Namespace `Haunter`.
- `NoiseKind { Footstep, Cane, Impact, Creature, Intro }`
- `NoiseEvent` fields `Position : Vector3`, `Loudness : float`, `Kind : NoiseKind`, `Source : GameObject`.
- `NoiseSystem.Instance`; `NoiseSystem.Heard : Action<NoiseEvent>` (static event).
- `NoiseSystem.Emit(Vector3 position, float loudness, NoiseKind kind, GameObject source = null)` (static). Loudness typical 0.25–1.5; creature hearing uses source-specific ranges via `ListenerCreature.HearingRange`.
- `EchoRenderer` camera component; `EchoRenderer.OutlineShader : Shader`.
- `EchoMaterial.SetPersistent(GameObject root, bool persistent)` updates renderer property blocks `_AlwaysVisible`.
- `GameSession.Instance`; `State : SessionState` (`Ready`, `Playing`, `Paused`, `Caught`, `Escaped`); `Player : PlayerController`; `Exit : Transform`; `Creature : ListenerCreature`; `Begin()`, `Catch()`, `Escape()`, `Restart()`.
- `PlayerController.ViewCamera : Camera`; `HeldObject : ThrowableObject`; `Initialize(Camera camera)`.
- `ThrowableObject.DisplayName : string`; `IsHeld : bool`; `Pickup(Transform anchor, Collider playerCollider)`; `Throw(Vector3 velocity)`; `Drop()`.
- `PrototypeWorld.Build(Material material)` creates environment, player, camera with EchoRenderer, NoiseSystem and GameSession, creature and props. Called in editor. Positions: player `(0,0,-12)`, exit `(0,0,13.7)`, creature/guard `(0,0,11.5)`; room x ±11, z ±15.
- `ListenerCreature.Initialize(Transform player, Material material)`; `RemainingSteps : int`; `TargetPosition : Vector3`; `IsInvestigating : bool`. Runtime NavMesh built from static geometry on layer 0; throwables layer 8, player layer 9, creature layer 10. Agent dimensions radius .55, height 1.6.

## Verification
Compile and build with the installed Unity editor. In-engine checks must exercise noise retargeting, self-noise exclusion, bounded stop, pickup/throw impact, reachable navigation, and caught/escaped restart. Inspect captured gameplay frames to confirm the shader is visible and monochrome. Keep generated assets editable and document any incomplete checks honestly.
