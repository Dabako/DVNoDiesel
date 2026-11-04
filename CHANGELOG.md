# Changelog

## 0.5.1 (2026-11-04)

- New careers (tutorial skipped) now start with a shovel, lighter and oil can,
  so the first S060 can actually be fired up.
- New button in the mod's settings (Ctrl+F10) that gives you these tools. Tools
  you already own are skipped; the lost and found brings back dropped ones.
- Multiplayer: not tested yet. Joining players already get these tools from the
  Multiplayer mod, so only the host needs the button.
- Docs: modded steam locomotives marked as steam by their author are detected
  automatically. `steam_ids.txt` is only needed for the others.

## 0.5.0 (2026-11-04) - first public test release

- Stations only spawn steam locomotives: the stock S282 and S060, plus modded
  steam locomotives listed in `steam_ids.txt` (the VR Class Jk3 by default).
- A station spawner left with no steam group spawns an S060 instead.
- New careers (tutorial skipped) start with the S060 license, plus the Jk3's
  S080 license when Custom Licenses is installed, instead of the DE2.
- Diesel licenses (DE2, DE6, DH4, DM3 by default, see `blocked_licenses.txt`)
  can't be bought. Licenses you already own are kept.
- The Career Manager explains why a blocked license is unavailable.
- Every station's original and filtered spawn list is written to the UMM log.
