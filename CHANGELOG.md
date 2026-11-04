# Changelog

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
