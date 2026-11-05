# ArmorRestored

The armor from A Township Tale 0.0.32, back in the game: leather, metal and Wyrm, eight pieces each.
You wear it on your body and it takes damage off the hits that land there, with the same numbers the
old build used.

PC only. The same DLL goes on the server and on every player's PC.

## What it adds

24 items, numbers 52830 to 52853:

| Set | Numbers |
|---|---|
| Leather | 52830 to 52837 |
| Metal | 52838 to 52845 |
| Wyrm | 52846 to 52853 |

Each set has, in that order: a helmet, a chest plate, a left and a right shoulder pad, a left and a
right gauntlet, and a left and a right tasset.

Everyone sees what everyone wears. Worn armor stays on through death, leaving and server restarts.

## Wearing it

**Put on:** hold the piece where it goes and let go. The helmet on top of your head, the chest plate
against your chest, a pad on its shoulder, a tasset at its hip. For a gauntlet, hold it against its
forearm with your other hand. If that spot already has a piece, the old one drops.

**Take off:** close an empty hand on a worn piece and hold it for a moment. It comes off into that
hand. A gauntlet only comes off with the other hand.

You do not see your own helmet, so it never blocks your view. `own_helmet = show` draws it.

## Protection

A hit on a covered part does this share of its damage. 1 is the same as bare.

| Set | Pierce | Slash | Blunt | Caustic | Explosive | Heat |
|---|---|---|---|---|---|---|
| Leather | 0.7 | 0.7 | 0.85 | 1 | 1 | 0.2 |
| Metal | 0.5 | 0.4 | 0.9 | 0.6 | 0.4 | 1.2 |
| Wyrm | 1 | 0.8 | 0.6 | 0.1 | 1 | 0.8 |

Each piece covers only its own part:

| Piece | Covers |
|---|---|
| Helmet | head |
| Chest plate | chest |
| Shoulder pad | that shoulder |
| Gauntlet | that arm, elbow and wrist |
| Tasset | the hips |

The hips are one zone, so one tasset covers them and the second adds nothing. The game has no leg
zones. Armor does not wear out.

## Where it drops

Armor is loot again, as it was in 0.0.32. It turns up in the world's loot spots: leather as Rare
loot, metal as Epic. Wyrm armor never dropped in 0.0.32 and does not here unless the server sets
`wyrm_loot = on`, which adds it as Epic.

## Install

1. Put `ArmorRestored.dll` in the server's `Mods` folder and restart the server.
2. Every player puts the same `ArmorRestored.dll` in `Mods` in their game folder.

A player without the mod, or with another version of it, cannot join. Quest players cannot join a server that runs it.

To remove it, delete the DLL from the server and from the players. The game cannot load armor
without the mod, so pieces left lying in the world are lost.

## Settings

`UserData\ArmorRestored\settings.txt`, written on the first start. Restart after a change.

| Setting | Where | What |
|---|---|---|
| `join = refuse` | server | refuse players without the mod or on another version; `log` only logs them, `off` skips the check |
| `loot = on` | server | leather and metal armor in the loot |
| `wyrm_loot = off` | server | `on` adds Wyrm armor to the loot as Epic |
| `own_helmet = hide` | player | `show` draws your own helmet |
| `debug = off` | both | `on` logs every armor grab and let go on a player, every hit and its armor on the server |

## Console commands

On the server console.

| Command | What |
|---|---|
| `armor list` | every piece, its number, and the loot line |
| `armor give <player> <set> <slot>` | drop armor in front of a player |
| `armor wear <player> <set> <slot>` | put armor straight on a player |
| `armor strip <player>` | take everything off a player and drop it in front of them |
| `armor status <player>` | what a player wears and which parts it covers |
| `armor testhit <player> <zone> <type> <damage>` | hit one part through the game's own damage code, show what was taken, then give the health back |
| `armor loot <rolls>` | roll the game's loot on every loaded loot spot and count the armor |
| `armor debug <on/off>` | the server hit log, until the next restart |

Sets: `leather`, `metal`, `wyrm`. Slots: `helmet`, `chest`, `shoulder_l`, `shoulder_r`,
`gauntlet_l`, `gauntlet_r`, `tasset_l`, `tasset_r`, or `all`. Zones: `head`, `chest`, `hips`,
`leftshoulder`, `rightshoulder`, `leftarm`, `rightarm`. Types: `pierce`, `slash`, `blunt`, `caustic`,
`explosive`.

## Build

`build.ps1` checks the style, compiles `src` against your game with the .NET Framework's `csc`,
embeds `assets\armor.pack` (the models and textures), checks the DLL, and writes
`build\ArmorRestored.dll` and the release zip. It needs Python 3. If your game is not in
`C:\Games\Alta\A Township Tale`, pass `-Game "<your game folder>"`.

## Credit

The armor models, textures and numbers are Alta's own, from A Township Tale 0.0.32. This mod puts
them back in the game.
