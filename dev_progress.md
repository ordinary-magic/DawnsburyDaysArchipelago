v1 TODO:
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

Planned Changes and Improvements in fugure versions:
 - Invesigate how to log messages outside of combat, or at least show u encounter win rewards when you get them.
 - Add common reference file to synchronize location/item ids between apworld and mod
 - Add option to do seperate +1/striking bonuses or to just drop loot instead of progressive bonuses
 - Figure out how to give player runes and materials that would be normally attached to lootable weapons (non-progressive loot covers this)
 - Encounter order difficulty options
 - DLC Randomization (check if owned first)
 - Add More Locations and Rewards
  - Need to be granular so as to avoid awarding 10 checks on level clear
  - Achievements are a good source of potential checks, but need to be filtered
  - End of Battle Loot is super customizable, and would add more variety to reward quality

Long Term Possible Changes:
 - Patch Spell Prep menu to be correct level?
 - Enforced Character Build Randomizer Mode?
 - Option to Include Random Encounters in pool?
 
Things which could be improved but probably wont:
 - Archipealgo button on-hover tooltip (is in code, idk why it doesnt work - probably requires more extensive patching)
