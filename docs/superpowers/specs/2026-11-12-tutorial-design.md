# In-game tutorial

Implement the user's specified opening in the existing Unity game: mandatory cane tap, a paw sound and camera pan to the exit guard with “Oh no... I've got to sneak past it. It's blocking the only exit.”, two walking footsteps, a charge, a frozen emergency pickup, a frozen guided throw, inspection, and crouching past the creature. Include a skip button. Each fresh run, including retry after death, starts the tutorial; completing it does not repeat it during that run.

Use a runtime tutorial director, retaining the existing player, physical props, noise system and creature investigation. This is preferable to a separate tutorial level because the lesson happens inside the actual encounter. A passive tooltip sequence would not provide the requested action locks.

Implementation choices: captioned inner dialogue; no new recorded voice assets. Give the player unlimited time to perform mandatory actions. Script only the initial pursuit (spawn separation exceeds normal hearing); keep normal-game balance unchanged. Select a reachable nearby prop, repositioning a loose prop if the first steps put all props out of reach. Aim a clear ballistic throw close enough for actual impact hearing. Use unscaled time for frozen camera pans and input; preserve the freeze across pause/resume. Skip clears all temporary controls and presentation. Both Windows and WebGL use their existing crouch bindings.

Verify the sequence, real impact/inspection, wrong-action locks, pause, skip, and replay after death in the actual player. Build Windows and WebGL. Publishing the updated build is a separate action from these local edits.
