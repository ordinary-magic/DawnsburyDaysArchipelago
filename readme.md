# Dawnsbury Days Archipelago and Randomizer
This repo contains the code for the Dawnsbury Days archipelago multiworld, and the game mod that runs it.

## What this Does
This mod will let you run Dawnsbury Days as part of an archipelago multiworld randomizer.

When installed in Dawnsbury Days, this will add an archipelago button to the home screen, which you can use to configure your connection information, and will add a new campaign to the game which you can use to play the randomizer.

If you are connected to Archipelago, the home screen button will be blue, and you will be able to select the "**Archipelago**" campaign. The campaign only exists when you are connected to the server!

Currently, this mod can affect every official campaign in the game. The DLC is not required to run the mod, but must be installed in order to access it via the randomizer. 

## Installation
* First, download and extract the most recent release from the [releases page](https://github.com/ordinary-magic/DawnsburyDaysArchipelago/releases).
* To install the archipelago world, double click the .apworld to install it into your archipelago launcher.
* To install the game mod, copy the rest of the files into the CustomMods folder in your Dawnsbury Days game installation.

## Archipelago Gameplay Considerations
* When you start the campaign you will plan your characters builds all the way to their max level (lvl 4/8/9), so that they can be automatically leveled by the game later.
* New Item Bonuses (in automatic mode) and Action unlocks apply anytime the game does a state check (before, during, and after most actions).
* Character levels do not change during combat (but you can always reload).
* If you have item bonuses enabled, do not give your characters +1/striking weapons, magic armor, handwraps, or gate attenuators during character creation as these will override the awarded bonuses from archipelago (these are excluded from shops during the campaign).
* When preparing spells, your spell prep menu will be for your max level, instead of your current one. When you enter a battle, you will only have the spells and slots appropriate to your level. EG: if you are 1st level, and would have 2 1st level slots, but you prepared animate dead, bane, and heal in the three slots in the menu, you will only have animate dead and bane after your level is scaled down.
* In automatic bonus mode, you may equip any number of property runes onto items (regardless of fundamental runes), but characters who have not unlocked a high enough potency upgrade to handle that many runes will not be able to wield them.
* Any pending traps will trigger automatically at the start of your turn (at most one per turn).
* Bonus "Loot Bags" will award a variety of items which are scaled to the party's current levels.
* If you have Automatic Item Bonuses enabled and generate filler items, they will include bonuses to trained skills and perception.
* If you have locked actions enabled, the following considerations apply:
  * Locked Strikes prevent using most things with the attack tag, except for spell attack rolls.
  * Locked Spells prevent you from casting focus spells, cantrips, leveled spells, and kineticest impulses. Spells from items are unaffected.
  * Even if stride is locked, you are always allowed to step.
  * Interact affects most actions with the interact or manipulate tag, such as drawing/stowing items, using held items, and even opening doors. Because this is often required for certain missions, one character is always given this at the start in order to prevent softlocks.
  * Likewise, if a character cannot attack or cast spells, they are given a special "Tantrum" action which can do a small amount of damage.

## World Generation, Filler & Padding
The randomizer allows you to pick which campaign(s) you wish to play. It also contains settings for various unlocks (Item Bonuses and Locked Actions) which can affect the number of required item drops in the run. Further, there are settings to include extra random encounters in the encounter pool, which can change the number of locations from which to drop items. As a result, we must dynamically increase either the size of the item drop pool or the number of locations in order to ensure these two values match up.

A run always has 4 level ups for every level the campaign spans. Item bonuses incease this amount depending on the level range, and Locked actions will increase this number by either 2 or 4 per character. For example, Dawnsbury Days has room for 20 item drops over 21 encounters (the final encounter never drops an item, since you have already won). It ranges from level 1-4, which means 3 level ups each for a total of 12. This means it has, by default, 8 slots left. If you turn on either item bonuses or basic locked actions, both will increase the amount by 8, meaning it perfectly matches, and there will be no need for filler or padding. But this is very precisely balanced, any extra encounters or slightly different settings will throw this ratio off, requireng either padding or filler.

If you have too few item drops for your encounter pool, archipelago will generate "filler" items to increase the count. In order, these are generated from traps (up to your maximum amount), any skill or perception bonuses unlocked at that level range (if automatic bonuses are on), and lastly "Loot Bags", which are randomly generated useful loot scaled to your party's level.

If you have too many item drops for your encounter count, one of two things can happen. If you have encounter padding on, we will add extra encounters, pulled from the game's pool of random encounters to increase the size of the campaign. These encounters can include modded encounters too, if you have mods enabled in the randomizer settings. Encounters are never repeated, which means that adding an extrodinary amount of items can cause us to run out. This can also happen if you have "extra encounters" enabled and add too many, as the world generation does not have any way of knowing how many encounters you have installed on your local game. If we run out of encounters before awarding every item, or if you have disabled padding entirely, the game will try to stack additional location checks on encounters, preferring the later encounters in each module. The game has, without any mods, enough extra encounters to add 2 per level without any issue.

## Progression Balancing
The base game is generally divided up into "modules", which expect you to level up between them. These form the basis of the randomizer's "regions". The game's balancing is by default very loose, since there aren't strict locks between these regions, and most of the progression balancing is to prevent "worst case" scenarios instead.

The regions are locked by a required number of "level ups" for each character, one level behind what you would normally have at that point in the game. This means that you should not be able to progress past a region until you have unlocked the level ups you would start that region with. Eg, in dawnsbury days, everyone will be at least level two by the end of the second (level two) module. This should even out the level curve between the party, and will mean that your average level up will be around the start of the module where you would normally get it.

In games with locked abilities, the regions are also locked by a minimum number of unlocks. Before you finish the first region, you are expected to have at least two "useful" abilities. The randomizer assumes that Annacoesta and Saffi are spellcasters, and Scarlet and Tok'dar are martials, and thus will try to give you at least two of Annacoesta/Saffi Spellcasting and Scarlet/Tok'dar Strikes. For every region beyond the first, two additional offensive unlocks are required, but without any care for "usefulness" This means that, by the end of region 4, you will be expected to have unlocked all offensive abilities.

In "Extreme" action locking runs, you are given a random character's interact from the start as an anti softlock measure. In addition, by the end of the first region someone will be expected to unlock "Stride" as well. Every further region will expect you to have at least two more move/interact unlocks, in the same manner as with your offensive abilities.

## Known Issues
* Sometimes (especially when first loading a save), items will show their equipped runestones as "inactive", this is a visual bug only and should not affect the item in combat.

## Building from Source
To build either the apworld or mod yourself, run the build scripts in the respective directories. The Mod will attempt to install itself in your game's CustomMods folder automatically (directory can be configured via the [Dawnsbury.Mod.Targets](Mod/Dawnsbury.Mod.Targets) file), but can be manually copied from the genertaed CustomMods folder instead. The Arhcipelago multiworld must be installed manually, by double clicking the newly built file.

## Contributing
If you encounter any notable bugs, please document them as best as you can, and submit them to the issues page.
Please reach out to me if you have something you want to contribute, as I might already be doing it. [dev_progress.md](dev_progress.md) is a rough tracker of what I'm working/wanting to work on.