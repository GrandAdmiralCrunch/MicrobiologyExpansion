# Release kit — v0.2.1

**Tag:** `v0.2.1`  **Title:** `v0.2.1 — Bugfix patch`  **Asset:** `MicrobiologyExpansion-0.2.1.zip`

## Packaging checklist
1. Build `Source/MicrobiologyExpansion.sln` in **Release**. The DLL lands in `Source/MicrobiologyExpansion/bin/Release/`.
2. Make a folder `GameData/MicrobiologyExpansion/`, copy this repo's `GameData/MicrobiologyExpansion/` into it, and add `Plugins/MicrobiologyExpansion.dll`. Don't include the `.pdb`.
3. Zip it with `GameData/` at the top level, plus `README.md`, `CHANGELOG.md` and `LICENSE`.
4. Check the version: `GameData/MicrobiologyExpansion/MicrobiologyExpansion.version` must say 0.2.1.

## GitHub release text

```markdown
## v0.2.1 — Bugfix patch

### Fixes
- Canyon microbes no longer spawn everywhere on their planet. Biome matching was a text search, and "Canyon" contains "any". Affected the Mun, Dres, Eeloo and Duna's Western Canyon food web
- Sporothrix and Blastomyces Laythensis can now be found on the surface and in flight. A `Surface,InFlight` situation was being read as orbit-only
- Jool and Sun microbes (Methanoxus Sporum, Aerobacter Joolii, Joolmoeba, Stellarsilicus) can now be found. Bodies without biomes previously returned nothing
- Removed invalid Duna biomes from Olivine Microcrystal Fragment, Fossilized Microbial Shells and Abiotic Shell
- Fixed the Kerbin Deserts sample text, and config numbers now load correctly on every system language
- Final Frontier ribbons now load. Thanks @celino (#2)
- Discovery and sample messages no longer show a raw `<color=lime>` tag (#3)
- The MBX-9 radial scanner no longer sinks into the hull (#4)

### For config authors
- New load-time check: KSP.log lists any MICROBE with an unknown planet, a biome its planet doesn't have, or unknown trophic prey. Search for `[Microbiology Expansion] Config`
- Biome names now ignore case and spaces
- Bioscanner recharge time and power cost can be set per part with `bioscanCooldown` and `ecPerScan` (#5)

Saves from v0.2.0 are compatible. Requires **Module Manager**. KSP 1.12.x.
```
