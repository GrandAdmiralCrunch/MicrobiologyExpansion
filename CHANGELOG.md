# Changelog

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
