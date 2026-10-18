# Changelog

## v0.2.1 — Bugfix patch

### Fixes
- **Canyon microbes no longer spawn everywhere.** Biomes were matched with a text search, and "Canyon" contains the word "any", so microbes listed in a canyon biome could be found anywhere on their planet. This affected Lithotroph Münis (Mun), Halite Dresii and Lithosilicus Dresii (Dres), Cryoextrema Eelooii (Eeloo), and the Western Canyon food web on Duna (Perchlorovore Dunae, Crustose Lichen, Plasmodium Dunae, Dinoflagella Dunae). Biomes are now matched exactly, so "Highlands" also no longer matches "Polar Highlands".
- **Sporothrix Laythensis and Blastomyces Laythensis can be found on the surface and in flight.** A comma-separated `situation` (e.g. `Surface,InFlight`) was read as a single combined value that came out as InSpace, so they could only be found from orbit. Each listed situation is now read on its own.
- **Jool and Sun microbes can now be found.** Jool and the Sun have no biomes, and the bioscan gave up when there was no biome. Methanoxus Sporum, Aerobacter Joolii, Joolmoeba and Stellarsilicus were unobtainable. On bodies without biomes, microbes with `biomes = Any` are now found normally.
- Olivine Microcrystal Fragment listed a "Slopes" biome on Duna, and Fossilized Microbial Shells and Abiotic Shell listed "Northeast Basin". Duna has neither biome, so those entries are removed.
- Sample results text for Kerbin's Deserts biome now shows (the key was misspelled), and duplicate Mountains and Grasslands entries were removed.
- Config numbers (`spawnChance`, `scienceValue`, `detectionChance`) are now read the same way on every system language, including ones that use a comma for decimals.
- **Final Frontier ribbons now load.** The ribbon pack used the wrong file format, so Final Frontier loaded none of the ribbons and logged a warning for every line. The 12 ribbons with artwork now show up and can be awarded from the Hall of Fame; the rest are listed but switched off until their images exist. Thanks to **celino** for the converted file (#2).
- "NEW DISCOVERY" and "Sample collected" messages no longer show a stray `<color=lime>` tag; KSP doesn't recognize that color name. Thanks to **celino** for the report (#3).
- The MBX-9 Microbioscanner (radial) no longer sinks into the part it's attached to. Its attach point now sits on the back of the model. Thanks to **celino** for the report (#4).

### For config authors
- **New config check.** When the game loads, every MICROBE is checked against the planets in your game. KSP.log gets a `Config warning` line for unknown planets, biome names a planet doesn't have, specific biomes on a planet with no biomes, unknown trophic prey, and unknown situations. Planet pack authors: search KSP.log for `[Microbiology Expansion] Config`.
- Biome names now ignore case and spaces: `Ice Caps`, `IceCaps` and `ice caps` all match.
- The bioscanner's recharge time and power cost can now be set per part: `bioscanCooldown` (seconds, default 6) and `ecPerScan` (ElectricCharge, default 30) in the `MicroBioscanModule` node, so Module Manager patches can rebalance it (#5).

### Compatibility
- Saves from v0.2.0 are fully compatible.

## v0.2.0 — "Viking" update

### New parts
- **MBX-9 Microbioscanner** (radial). A surface-mount biology instrument package modeled on the Viking Lander Biology Instrument: an open-frame aluminum chassis with a chamfered corner, two incubation cylinders, three test cells, a manifold and truss, wiring harnesses, and a soil hopper on the lid.
- **MBX-9L Microbioscanner Laboratory Drum** (0.625 m inline). The same instrument package mounted inside a structural bay with a gold thermal blanket and a service window.
- Both parts use `MicroBioscanModule`, the same bioscan as the Mystery Goo patch. They unlock at Science Tech.
- New texture atlas and editor thumbnails.

### Codex & microbes
- Rewrote the Codex description for all 77 entries, keeping the existing jokes and adding habitat, behavior and how each organism fits its food web.
- **Trophic web expanded from 4 to 28 consumer species and 58 relations.** Every planet with life now has a food web:
  - Kerbin: grazers, predatory bacteria, filter-feeding bryozoans, tardigrades, parasites and a gut symbiote vs. pathogen rivalry.
  - Laythe: phototrophs, decomposers, flagellate hunters, phages, parasites and fungi.
  - Duna: the Western Canyon chain (Lichen → Dinoflagella → Plasmodium).
  - Eve: atmospheric phototrophs sinking to sea-surface scavengers.
  - Jool: sky bacteria → spores → Joolmoeba (which also eats Tylo impact ejecta).
- As before, a relation is revealed in the Codex once both species have been processed in a Mobile Processing Lab.

### Fixes
- **22 microbes had their hazard level ignored**: the key was spelled `hazardlevel` (lowercase), and KSP config keys are case-sensitive, so all of them loaded as Safe. They now load correctly. This means:
  - Risky: Adenovirus, Tania, Trichinella, Plasmodium, Schistosoma and Stellarsilicus now need a Level 2 Scientist to sample and give 1.5× science.
  - Hazardous: Helicobacter now needs a Level 3 Scientist and gives 3× science.
- Lithotroph Münis could never spawn, because it was set to the "Slopes" biome, which the Mun doesn't have. It now uses the Mun's Highlands, Midlands, Canyons, Highland Craters and Midland Craters.
- Fixed a biome typo for Helicobacter Kerbinii ("Waters" changed to "Water").
- Diatomus Kerbinii is now categorized as a Protist instead of Metazoa (diatoms are algae).
- Blastomyces Laythensis pointed to an icon that doesn't exist. It now uses the Laythe fungus icon.
- Renamed the Lithotroph icon to an ASCII filename (`Lithotroph_Munis.png`). Some zip tools mangled the `ü`, which broke the icon.

### Packaging
- The release zip now contains a top-level `GameData/` folder.
- Added a KSP-AVC `MicrobiologyExpansion.version` file.
- Removed the debug `.pdb` file from the release.

### Compatibility
- Existing saves are compatible: discoveries are stored by microbe name, and no names changed.
- `Microbes.cfg` now documents every field at the top of the file, including `trophicRelations`.

## v0.1.2
- Previous public release.
