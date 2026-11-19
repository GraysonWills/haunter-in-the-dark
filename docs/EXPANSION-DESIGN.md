# Haunter in the Dark expansion

The archive remains Level 1. The courtyard turns a lesson about sound into a route choice. The ballroom makes the same sound harder to locate. The maze asks the player to use two sounds deliberately and leave while both creatures are occupied.

## Research and design choices

| Reference and documented design | Application in Haunter |
| --- | --- |
| *Alien: Isolation* creative lead Alistair Hope describes an underpowered player facing a creature that responds to sensory evidence. Sprinting and the flashlight both help the player and give away information. His GDC talk frames those decisions as the basis for encounters with a single antagonist. [Hope interview](https://blog.playstation.com/archive/2014/03/26/behind-terror-alien-isolation-exclusive-interview) · [GDC talk](https://www.gdcvault.com/play/1021852/Building-Fear-in-Aliens) | Give the courtyard an exposed crossing, recognizable cover and a longer perimeter route. Players choose when a cane tap is worth being heard. Let nearby pawsteps reveal movement beyond the immediate view. |
| Creative Assembly lead designer Gary Napper describes separate control of how often and how visibly the alien hunts, while it uses its senses to make immediate decisions. He also describes alternating periods of tension and relief. [Napper and Bond interview](https://www.pcgamesn.com/interview-creative-assembly-alien-isolations-terrifying-alien-ai) | Separate encounter pacing from perception. Script the collapse and provide quieter recovery spaces. Once a creature is active, its destinations come from authored patrols, its assigned post, and heard or remembered positions. A small scoring layer can follow tension without passing the player's location to either creature. |
| Creative Assembly sound designers Sam Cooper and Byron Bullock describe off-screen sound cues, quiet Foley passages, and a score and mix driven by stealth and threat. They withheld a musical cue when it would reveal an unseen creature. [Cooper and Bullock interview](https://audiomediainternational.com/the-sound-of-alien-isolation/) | Let spatial sound lead. Give each creature a readable footstep rhythm. Keep the score low during throws, investigation, and recovery so the next footstep and tap remain intelligible. |
| *Outlast* cofounder Philippe Morin says early playtesters felt like spectators. The team moved material out of the first act and simplified the camera and door interactions. Composer Samuel Laflamme describes working with audio director Francis Brus in *Outlast 2* to test scene-specific music and use it to guide the player's response. [Morin's postmortem](https://www.gamedeveloper.com/design/horror-in-the-making-how-red-barrels-outlasted-i-outlast-i-) · [Laflamme interview](https://gameranx.com/features/id/106899/article/gameranx-interviews-samuel-laflamme-outlast-2/) | Keep the collapse introduction brief and playable. Give arrival onto the courtyard, the ballroom's quiet edges, and the final exit distinct acoustic treatments. Test whether a new player can identify a landmark, choose a route, and recover after a wrong choice. |

## Shared encounter rules

- The primary sound produces a local reveal and a hearing event. Pawsteps reveal the nearby scene. Music and decorative ambience play outside the hearing and reveal systems.
- A creature can interrupt a pursuit to investigate an audible, reachable impact. The five-second timer starts on arrival. Its destination and timer stay fixed during any subsequent sounds. It walks back to a reachable patrol point or its guard post afterward.
- Give each throw a stable event identity. Bounces and reflected descendants share that identity. Keep one owner per distraction, pick among reachable and available creatures using hearing and path distance, and release the claim if the destination fails or the investigation ends.
- A developer visualization can mark the creature's orientation, accepted sound, estimated destination, and state. In the ballroom it can show the source and the estimate together with explicit labels. The normal player view uses echoes and the existing objective guidance.

## Level 2 · Courtyard

Top-down plan, approximately 26 × 31 m plus the recessed archive service arch. North is up and Unity `x` increases to the east. Lines indicate connections and sightlines, with landmarks shown in their approximate locations.

```text
 NORTH ↑                         x east →
 ┌──────────────────────────────────────────────┐
 │ West arcade       D ▓▓                     U │  D  ground approach (0, 12)
 │       ↑          crash       north apron   ║ │  ▓  rubble blocks entrance at z≈13.7
 │       │                                     ║│  U  upstairs door (10, 13), y ≈ 2.8
 │   planter      exposed paving    planter   ║ │  ║  raised balcony above east lane
 │       │      dry fountain  H        │     ╱  │  H  halfway trigger, z ≈ −0.65
 │       │                             │     ╱  │  ╱  east service ramp, z −6 to +4
 │       └── cover ─────┬──── cover ─────┘    ↑ │
 │                     open shortcut         │  │
 │ Archive recess M      S ─────── east lane ─┘ │  M  creature at the service arch (−4, −17.05)
 └──────────────────────────────────────────────┘  S  arrival (2.8, −13.3)
```

1. Cross about half the courtyard before the timber supports buckle. The same arch and canopy pieces tumble in sequence for 2.7 seconds. The final crash sends a strong wave through the yard and the fallen masonry physically blocks the entry.
2. Look back toward the archive as the creature charges toward the crash. The brief guided turn protects the player while the creature emerges. The creature slows to inspect the debris, then joins its authored patrol.
3. Backtrack to the base of the east stairs. Cut across the yard beside the fountain at `(−0.4, 1.1)` or move from cover to cover along the perimeter. The west arcade piers and stone screen distinguish one route from the east service bench and east planter. Use props to redirect the creature, then take the east ramp and balcony to the upstairs door.
4. Verify that waiting beside the archive leaves the crossing open, walking halfway starts the staged fall, and landing debris blocks the ground route. Walk back to the stairs and through the live upstairs exit. Check that the creature reaches the crash, inspects it for five seconds, and returns to its patrol.

## Level 3 · Ballroom

Top-down plan, approximately 30 × 38 m. The curtain alcoves center on `(±12.95, 5.7)`. Column rows run at `x ±7.7`. The open floor and both perimeter routes connect around the stage.

```text
 NORTH ↑
 ┌──────────────────────────────────────────────┐
 │ West bypass ─────── X ─────── East bypass    │  X  backstage door (0, 17.6)
 │       ↑         raised stage         ↑       │
 │  Q ── A ─ o                      o ─ A ─ Q   │  Q  quieter curtain alcoves
 │       │ seats      dance floor   seats│      │  A  alcove entrances
 │       │      o   F · F · F · F    o  │      │  F  staggered solid furniture
 │       └────────────┬─────────────────┘       │
 │                 S  lobby                     │  S  arrival (0, −16)
 └──────────────────────────────────────────────┘
```

1. Tap the cane in the lobby to see a clean reveal. A short series of room echoes follows it, then fades even while the creature keeps patrolling.
2. Weave around the service table, display pedestal, folding screen, and banquet table. Overlapping footfalls, taps, and impacts split and bend the distant outlines. Nearby silhouettes and held objects stay readable. Quieter alcoves reconnect to the routes around the stage.
3. Derive reflections from the originating event and nearby authored surfaces. Delay and attenuate them, cap how many can remain active, and let them expire. Mix a stable nearby outline with fading or slightly displaced distant contours. Reduced distortion suppresses the contour interference while preserving the underlying sound events. All collisions, interactions and navigation stay in place.
4. The creature uses the same returns to form an estimate. Weak or overlapping evidence can make it listen longer or turn toward a reachable reflection. Confidence and remembered positions decay. A committed throw remains locked to its chosen destination until the five-second investigation ends.
5. Verify a clear and overloaded view from the same position against unchanged geometry. Test reduced distortion, an apparent reflection that causes a wrong estimate, and the absence of repeated investigations from one echo chain.

## Level 4 · Final maze

Top-down plan, approximately 24 × 27 m. Three partition rows at `z −8.2`, `−1.6` and `+3.3` form connected loops. Those loops merge at the covered north gallery and short center exit approach. Cistern pipes mark the western bay and enclosure rails mark the eastern bay.

```text
 NORTH ↑
 ┌──────────────────────────────────────────────┐
 │                    X                         │  X  exit (0, 12), entry at z≈11.2
 │                    G                         │  G  exit guard (0, 9.5)
 │     W ←──── covered F ─────→ E               │  F  throw staging (0, 4.1)
 │       R           props                      │  W  west landing (−5.6, 6.9)
 │      ┌──────┐            ┌──────┐            │  E  east landing (5.5, 7.6)
 │      │ west ├── cross ───┤ east │            │  R  roamer starts west (−5.2, 6.4)
 │      └──────┴─────┬──────┴──────┘            │
 │                   S                          │  S  arrival (0, −12)
 └──────────────────────────────────────────────┘
```

1. Read the quick overhead scan of the actual maze walls. It marks your heading and the exit and can be replayed with M or the touch scan button. The rover begins on a northbound leg away from the entrance.
2. Follow a loop to the covered gallery. The roaming creature's footstep cadence marks a moving route, while the guard's paws locate the exit post. A throw toward the west landing can draw the rover while the guard stays near the door.
3. Pick up the second prop at the staging point. Throw toward the east landing while the rover is still committed. Once both are occupied, cross the central approach to the door. If the first investigation is about to end, use cover and set up again.
4. The center route measures approximately 7.1 m from staging to the exit threshold. Staging is beyond the guard's crouch hearing range. Walking at 2.8 m/s takes about 2.54 seconds once both creatures are committed.
5. Verify two owners for two separate throws, one owner for all reflections and bounces from each throw, and independent timers that start on each creature's arrival. Show the guard and rover each returning to their role after expiry.

## Original sound palette

| Level | Exploration and recovery | Suspicion, investigation and pursuit |
| --- | --- | --- |
| Archive | Close, dry steps and short cane reveals. Leave room around the tutorial's existing sounds. | Pawstep and impact cues stay prominent under a restrained sustained tone. |
| Courtyard | Sparse air and structure creaks with long gaps. The collapse uses a heavy impact and a settling tail. | A low pulse enters with audible danger and falls away as the creature settles. Keep the route-establishing crash distinct from throwables. |
| Ballroom | Thin bowed tones and faint resonant returns. Curtain alcoves attenuate returns and make it easier to listen. | Let scrape and drone layers grow gradually with acoustic overlap. Reduce score level around taps and impacts so intensity comes from spatial uncertainty. |
| Maze | Muffled ambience between the two distinct paw rhythms. Release to silence or a short resolving tone at the exit. | Sparse percussion during pursuit and deliberate pauses during investigation. Neither score nor ambience enters the creature's hearing queue. |
