v1:
 x Write and Design the code
 x Test Offline, Non-Archipelago Shuffle mode (seems good?)
 x Test archipelago generation (generates successfully)
 x Test buffs and integrations via mock
 x Test Loot Shuffler
 x Run server and Connect to Client
 X Test that level mods and qeffects are restricted to the archipelago campaign
 x Test In Full Archipelago
   x Test Stat Adjustments
     x Start of Turn (weapons)
     x Start of Combat (Level)
   x Test sending locations
   x Test getting items
   x Test Deathlink
   x Test loot and encounter shuffle settings
   x Test completing game
   x Test archipelago log and chat messages
 x Write the Readme
 x Test Offline Progress tracking & Save/Resume

v1.1:
 x Version Mismatch Check
 x Encounter Order Options
 x Harmony Dll Conflict
 x Add "Gameplay Considerations" section of readme to steam description

v1.2:
 - Integrate Profane Barrier DLC
   x Build means to check existence of and load from dlc dll
   x Add DLC campaign metadata to archipelago
   x Design and add new loot bonuses
   x Make campaign selection options
   x Implement merged campaign
   x Test DLC/merged campaigns in an archipelago
 x Remove offline mode from official release. (add debug mode - only in build)
 - bugs:
   x Heaven is empty and last? (level 7/8 not splitting correctly)
     x Chapters are opened by cutscenes and encounters, not by narration. Need mechanism to address this
   - Find a seed to use such that fine? does not re-randomize (should use world seed)

v1.2.1:
 - Add option to apply level ups more evenly (all at once?).
 - Determine means by which to handle runes
   - Ideally, every weapon can equip a non-fundamental rune?
   - Alternatley, use runes form inventory slots (jank)?

Ideas for future versions:
 - Roguelike Integration (let it handle its own encounter order and loot, but can do level scaling - ask dev for collab)
 - Invesigate how to log messages outside of combat, or at least show u encounter win rewards when you get them.
 - Add common reference file to synchronize location/item ids between apworld and mod
 - Add More Locations and Rewards
  - Need to be granular so as to avoid awarding 10 checks on level clear
  - Achievements are a good source of potential checks, but need to be filtered
  - End of Battle Loot is super customizable, and would add more variety to reward quality
 - Investigate per enemy rewards
 - Investigate locking basic actions (cast/strike) behind drops.
 - Forced Character Builds Mode (ask community to make these for me)
 - Option to Include Random Encounters in pool? (not sure how to find these in code)
 - Add option to do seperate +1/striking bonuses or to just drop loot instead of progressive bonuses (prob needed if more location checks are added)

Random Small Bugs:
 - Offline mode is starting at the ending level (doesnt affect encounters, but does affect level up stops). I've checked a lot of stuff and idk why this is happening.
 - Archipealgo button on-hover tooltip is missing (is in code, idk why it doesnt work - probably requires more extensive patching)
 - Spell prep menu is the incorrect level because arhcipelago treats chars as max level outside of encounters.