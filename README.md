# No Diesel - Steam Only

A [Derail Valley](https://store.steampowered.com/app/588030/Derail_Valley/) mod for a steam-only railway.
No more DE2 waiting at every station: the diesels are gone, and only steam spawns.

*Hyce made me do it.*

## What it does

- **Steam-only station spawns.** Every station's locomotive spawner keeps only its steam groups
  (S282 with tender, S060, and modded steam locomotives). A spawner with no steam group at all
  spawns an S060 instead.
- **Modded steam.** Custom Car Loader locomotives are added after the game's own spawn list is built,
  and No Diesel filters after them. Custom Car Loader reports locomotives marked as steam by their
  author to the game, so they are detected automatically. A modded steam locomotive without that mark
  can be listed by livery ID in `steam_ids.txt`.
- **Steam starting licenses.** A new career with the tutorial skipped starts with the S060 license,
  and the Jk3's S080 license if Custom Licenses is installed, instead of the DE2.
- **Steam tools.** A new career with the tutorial skipped also starts with a shovel, lighter and oil can.
  For existing saves, the mod's settings (Ctrl+F10) have a button that gives you the ones you don't
  own yet. If you own one that is lying around somewhere, use the lost and found instead.
- **No diesel licenses for sale.** DE2, DE6, DH4 and DM3 can't be bought (see `blocked_licenses.txt`).
  Licenses you already own are kept. The Career Manager tells you why.
- **Log.** The UMM log lists every station's original and filtered spawn list, so you can see what
  spawns where and find livery IDs of modded locomotives.

## Requirements

- [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) 0.27.3 or newer
- Optional: [Custom Car Loader](https://www.nexusmods.com/derailvalley/mods/324) for modded locomotives,
  and Custom Licenses for the Jk3's S080 license

## Installation

Install the release zip with Unity Mod Manager, or extract it into `Derail Valley\Mods\`
so you get `Mods\NoDiesel\Info.json`.

## Configuration

Both files are created in the mod folder on first start. One entry per line, `#` starts a comment.

| File | Content |
|---|---|
| `steam_ids.txt` | Livery IDs of modded locomotives that count as steam, for those not detected automatically |
| `blocked_licenses.txt` | General licenses that can't be bought: enum names (`DE2`) or license IDs |

## What it does not change

- **Existing saves keep everything they have.** Only new careers get the steam starting licenses.
- **The tutorial** is built around the DE2 and still grants it. Skip it for a steam-only start.
- Locomotives already standing in your save stay until the game clears them.
- Garage locomotives, restoration locomotives and the sandbox comms-radio spawner are not filtered.
- Licenses already owned are not removed.

## Compatibility

- **Custom Car Loader:** works with it; No Diesel runs after its spawn-group injection.
- **Multiplayer:** declared `MultiplayerCompatibility: Host`, since only the host spawns station locomotives.
  Not yet tested in a multiplayer session. Joining players already get the steam tools from the
  Multiplayer mod, so only the host needs the button.
- **Steam Start** (Nexus 853) also changes the starting licenses. Use one or the other.

## Building

Build against your game install:

```
dotnet build -c Release -p:DvInstallDir="C:\Path\To\Derail Valley"
```

## Credits and license

By Dabako. Code written with AI assistance (Claude).
MIT licensed, see [LICENSE](LICENSE).
