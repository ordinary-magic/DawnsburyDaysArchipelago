# Dawnsbury Days Archipelago and Randomizer
This repo contains the code for the Dawnsbury Days archipelago multiworld, and the game mod that runs it.

## What this Does
This mod will let you run Dawnsbury Days as part of an archipelago multiworld randomizer. It also contains an offline mode, leftover from my testing, which you can use if you want to play the game with shuffled encounters and loot.

When installed in Dawnsbury Days, this will add an archipelago button to the home screen, which you can use to configure your connection information, and will add a new campaign to the game which you can use to play the randomizer.

If you are connected to Archipelago, the home screen button will be blue, the new campaign will say "**Arhcipelago Dawnsbury Days**", and will run the archipelago with your configured settings.

If you are not connected, this button will be red, the new campaign will say "**Randomized Dawnsbury Days**", and will run the offline version of the randomizer instead.

Currently, this mod only affects the base game's Dawnsbury Days campaign.

## Installation
* First, download and extract the most recent release from the [releases page](https://github.com/ordinary-magic/DawnsburyDaysArchipelago/releases).
* To install the archipalgo world, double click the .apworld to install it into your archipelago launcher.
* To install the game mod, copy the rest of the files into the CustomMods folder in your Dawnsbury Days game installation.

## Archipelago Gameplay Considerations
* When you start the campaign you will plan your characters builds all the way to their max level (lvl 4), so that they can be automatically leveled by the game later.
* New Item Bonuses are applied at the start of each of your turns, but levels can't change during combat.
* In order for the item bonus progression to work, do not give your characters +1/striking weapons, handwraps, or gate attenuators during character creation as these will override the awarded bonuses from archipelago.
* When preparing spells, your spell prep menu will be for your max level, instead of your current one. When you enter a battle, you will only have the spells and slots appropriate to your level. EG: if you are 1st level, and would have 2 1st level slots, but you prepared animate dead, bane, and heal in the three slots in the menu, you will only have animate dead and bane after your level is scaled down.
* Currently, I can only send messages during battles, in the chat log. This means that if you get any items you wont see them until the start of your turn, and you wont know what your rewards are for winning until you start the next fight.

## Building from Source
To build either the apworld or mod yourself, run the build scripts in the respective directories. The Mod will attempt to isntall itself in your game's CustomMods folder automatically (directory can be configured via the [Dawnsbury.Mod.Targets](Mod/Dawnsbury.Mod.Targets) file), but can be manually copied from the genertaed CustomMods folder instead. The Arhcipelago multiworld must be installed manually, by double clicking the newly built file.

## Contributing
If you encounter any notable bugs, please document them as best as you can, and submit them to the issues page.
Please reach out to me if you have something you want to contribute, as I might already be doing it. [dev_progress.md](dev_progress.md) is a rough tracker of what I'm working/wanting to work on.