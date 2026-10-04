# Release kit — v0.2.0

## GitHub release

**Tag:** `v0.2.0`  **Title:** `v0.2.0 — The Viking Update`  **Asset:** `MicrobiologyExpansion-0.2.0.zip`

```markdown
## The Viking Update

Two new bioscanner parts modeled on the Viking Lander Biology Instrument, rewritten Codex descriptions for every entry, and a real food web on every inhabited world.

### New parts
- **MBX-9 Microbioscanner** (radial): open-frame Viking-style biology instrument package with incubation cylinders, test cells, wiring harnesses and a soil hopper
- **MBX-9L Microbioscanner Laboratory Drum** (0.625 m inline): the same instrument in a foil-wrapped equipment bay with a service window
- Both unlock at Science Tech

### Codex & trophic web
- Rewrote descriptions for all 77 Codex entries
- Trophic web expanded from 4 to **28 consumer species / 58 relations** across Kerbin, Laythe, Duna, Eve and Jool
- Process both species in a Mobile Processing Lab to reveal a relation

### Fixes
- 22 microbes had their hazard level ignored (`hazardlevel` key casing). Risky and Hazardous gating and science multipliers now apply to them
- Lithotroph Münis can now spawn (invalid Mun biome fixed)
- Fixed Helicobacter biome typo, Diatomus category and the missing Blastomyces icon
- Lithotroph icon renamed to an ASCII filename so zip tools don't mangle it

### Packaging
- Zip now contains a top-level `GameData/` folder
- Added a KSP-AVC `.version` file
- Debug `.pdb` removed

**Upgrading:** delete your old `GameData/MicrobiologyExpansion` folder before installing. Saves are compatible.

Requires **Module Manager**. KSP 1.12.x.
```

## SpaceDock

**Mod name:** Microbiology Expansion
**Game version:** 1.12.5
**License:** MIT
**Version:** 0.2.0

**Short description (summary field):**
> Discover, sample and catalogue microbial life across the Kerbol system. Bioscan for biosignatures, return samples, culture them in the lab and uncover each world's food web.

**Long description:**
```markdown
# Microbiology Expansion

Is there life out there? Probably. Is it tiny? Almost certainly.

Microbiology Expansion adds microscopic life to Kerbin and the worlds beyond: 77 Codex entries of bacteria, archaea, fungi, protists, parasites, viruses and stranger things, along with the instruments and lab work you need to find them.

## Features
- **Bioscanning**: the new **MBX-9** (radial) and **MBX-9L** (0.625 m inline) Microbioscanners, modeled on the Viking Lander Biology Instrument. The stock Mystery Goo can bioscan too.
- **Microbe discovery**: per-planet detection chances, biome and situation restrictions, weighted rarity, and Safe / Risky / Hazardous specimens with science multipliers.
- **EVA sampling**: scientists collect samples to return to Kerbin. Risky samples need a Level 2 Scientist, and Hazardous ones need Level 3.
- **Lab processing**: culture samples in the Mobile Processing Lab for bonus science. Better scientists work faster.
- **Trophic web**: 58 predator, parasite and decomposer relations, revealed once both species have been processed.
- **Microbiology Codex**: an in-game catalogue with icons, descriptions, discoverers, discovery dates, biomes, samples recovered and revealed food-web links, plus filters and sorting.
- **Kerbal XP** for discoveries.
- **Moddable**: add microbes and planets with simple `MICROBE` and `PLANET_BIOSCAN` config nodes.

## Requirements
- KSP 1.12.x
- Module Manager

## Installation
Merge the zip's `GameData` folder into your KSP `GameData` folder.

Source & issues: https://github.com/GrandAdmiralCrunch/MicrobiologyExpansion
```

**Changelog field (for the 0.2.0 version upload):** paste the GitHub release body above.

## Checklist
- [ ] Test in KSP 1.12.5: both parts appear under Science, bioscan runs, Codex lists 77 entries, and the trophic lines show after lab processing
- [ ] Upload `README.md`, `CHANGELOG.md`, `MicrobiologyExpansion.version`, `MicrobiologyExpansion.netkan`, `docs/` and the new `MicrobiologyExpansion.zip` to the repo
- [ ] Create the GitHub release `v0.2.0` with `MicrobiologyExpansion-0.2.0.zip` attached
- [ ] SpaceDock: Update → new version 0.2.0, upload the same zip, paste the changelog
- [ ] CKAN: submit `MicrobiologyExpansion.netkan` to KSP-CKAN/NetKAN (one-time)
