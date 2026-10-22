# Phase 12 — World and Player Save Serialization Specification

## 1. Evidence identity and extraction boundary

- Target: `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`; Terraria 1.4.5.8.
- SHA-256: `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`; MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`.
- REA world/player member evidence: `ev_7acdcf2dd34757685881699a36153c402bea5a278048a5230684432d5ea962a2`.
- REA item member evidence (for nested item records): `ev_34b341725a42e0a49a4d327a0fdbf0753d2bf8bafa2ae33650181111c4e77d84`.
- `docs\Decompiled_Terraria` was not present. This is metadata/CIL-anchor evidence, not parsed decompiled source. No target code was loaded/executed; CIL anchors are selective, not a full source listing.

## 2. World-file container and section order

### 2.1 Write/read pipeline

- `InternalSaveWorld(bool,bool,bool)` builds a `MemoryStream` and `BinaryWriter`, calls `SaveWorld_Version2`, converts the stream to bytes, validates when enabled, and writes through `FileUtilities` to `Main.get_worldPathName()`; rolling-backup calls are present in the same method.
- `LoadWorld` dispatches the old pre-release-88 reader or `LoadWorld_Version2`; the v2 path uses stream positions/pointers to enter the header and payload sections.

### 2.2 Exact v2 section call order observed in CIL

`WorldFile.SaveWorld_Version2` call anchors, in order:

1. `SaveFileFormatHeader`
2. `SaveWorldHeader`
3. `SaveWorldTiles`
4. `SaveChests`
5. `SaveSigns`
6. `SaveNPCs`
7. `SaveTileEntities`
8. `SaveWeightedPressurePlates`
9. `SaveTownManager`
10. `SaveBestiary`
11. `SaveCreativePowers`
12. `SaveFooter`
13. `SaveHeaderPointers`

`WorldFile.LoadWorld_Version2` seeks and reads in this observed order: format header, world header, tiles, chests, signs, NPCs, dummies, tile entities, weighted pressure plates, town manager, bestiary, legacy bestiary compatibility, creative powers, last-minute fixes, footer. The load path uses section positions from the file header between section readers.

### 2.3 Header fields and unresolved raw bytes

`SaveWorldHeader` writes these observed data members in this order:

| Order | Member/value | Encoding evidence |
|---:|---|---|
| 1 | `Main.worldName` | `BinaryWriter.Write` |
| 2 | `Main.ActiveWorldFileData.SeedText` | getter then `BinaryWriter.Write` |
| 3 | `WorldFileData.WorldGeneratorVersion` | `BinaryWriter.Write` |
| 4 | `WorldFileData.UniqueId` | `Guid.ToByteArray()` then `BinaryWriter.Write` |
| 5 | `WorldFileData.WorldId` | `BinaryWriter.Write` |
| 6–9 | `Main.leftWorld`, `rightWorld`, `topWorld`, `bottomWorld` | each passed to `BinaryWriter.Write` |
| 10–11 | `Main.maxTilesY`, `Main.maxTilesX` | each passed to `BinaryWriter.Write` |
| next | `SaveWorldFlags` | direct call |

`SaveFileFormatHeader` accesses `TileID.Count`, `Main.WorldFileMetadata`, and `Main.tileFrameImportant`; it calls `FileMetadata.IncrementAndWrite` and `BinaryWriter.Write`. Anchored constants in this method include `11`, `326`, and `128`. `SaveHeaderPointers(BinaryWriter,int[])` uses stream seeks and includes constants `326` and `20` before/around writes.

The above is **not** a byte-for-byte header dump. The selected CIL anchors do not expose every stack-producing instruction or each overload argument; therefore the exact magic bytes, exact pointer table byte offsets, and meaning/order of every header constant remain unresolved. Do not treat `11`, `326`, or `128` as file bytes without a full IL decode.

## 3. World payload structures

### 3.1 Tile matrix and run encoding

- Writer: `WorldFile.SaveWorldTiles(BinaryWriter)` — 1,067 IL bytes, 551 instructions.
- Reader: `WorldFile.LoadWorldTiles(BinaryReader,bool[])` — 1,012 IL bytes, 486 instructions.
- The loops use `Main.maxTilesX` and `Main.maxTilesY`; tile member accesses include `Tile.type`, `Tile.frameX`, `Tile.frameY`, `Tile.wall`, and `Tile.liquid`.
- The writer contains run-count/continuation branches and byte/word-width decisions. Anchored constants include `255`, `65280`, `128`, `64`, `32`, `24`, and `16`. The reader correspondingly consumes `ReadByte`, `ReadInt16`, and tile setters while reconstructing the tile matrix.
- Tile-type, frame-coordinate, wall, liquid, paint/wire and related presence flags are conditionally encoded. Exact flag-bit meanings and the full run-length transition table are not recoverable from the selected anchors alone; do not infer a replacement bit layout from this summary.

### 3.2 Chests

- `SaveChests` and `LoadChests` use an `8000` loop/sentinel bound in the observed IL.
- Per chest data includes coordinates (`x`,`y`), name, item count, and an item loop; load also carries a version gate at `WorldFile._versionNumber >= 294` for the newer item-count path.
- Chest inventory entries use item records described in §4.2. Chest lookup in UI uses the associated chest/item arrays but that is outside the file stream.

### 3.3 Signs

- `SaveSigns`/`LoadSigns` use a `32000` loop/sentinel bound in the observed IL.
- Sign records write/read text plus integer tile coordinates (`x`,`y`); load reconstructs the sign only when tile/sign validity checks succeed.

### 3.4 World flags and global state

`SaveWorldFlags` has a 2,145-byte, 632-instruction body. It serializes or consults a broad world-state matrix, including time/weather, seed modes, world-generation backgrounds/trees, invasion state, ore tiers, NPC/boss progression, town-NPC unlocks, and event state. The unique field-access inventory below is deduplicated and in first-observed access order; it is not asserted to be the exact write order.

```text
Main.drunkWorld, Main.getGoodWorld, Main.tenthAnniversaryWorld,
Main.dontStarveWorld, Main.notTheBeesWorld, Main.remixWorld,
Main.noTrapsWorld, Main.zenithWorld, Main.skyblockWorld,
WorldFileData.CreationTime, Main.moonType, Main.treeX, Main.treeStyle,
Main.caveBackX, Main.caveBackStyle, Main.iceBackStyle,
Main.jungleBackStyle, Main.hellBackStyle, Main.spawnTileX, Main.spawnTileY,
Main.worldSurface, Main.rockLayer, WorldFile._tempTime,
WorldFile._tempDayTime, WorldFile._tempMoonPhase, WorldFile._tempBloodMoon,
WorldFile._tempEclipse, Main.dungeonX, Main.dungeonY, WorldGen.crimson,
NPC.downedBoss1, NPC.downedBoss2, NPC.downedBoss3, NPC.downedQueenBee,
NPC.downedMechBoss1, NPC.downedMechBoss2, NPC.downedMechBoss3,
NPC.downedMechBossAny, NPC.downedPlantBoss, NPC.downedGolemBoss,
NPC.downedSlimeKing, NPC.savedGoblin, NPC.savedWizard, NPC.savedMech,
NPC.downedGoblins, NPC.downedClown, NPC.downedFrost, NPC.downedPirates,
WorldGen.shadowOrbSmashed, WorldGen.spawnMeteor, WorldGen.shadowOrbCount,
WorldGen.altarCount, Main.hardMode, Main.afterPartyOfDoom, Main.invasionDelay,
Main.invasionSize, Main.invasionType, Main.invasionX, Main.slimeRainTime,
Main.sundialCooldown, WorldFile._tempRaining, WorldFile._tempRainTime,
WorldFile._tempMaxRain, SavedOreTiers.Cobalt, SavedOreTiers.Mythril,
SavedOreTiers.Adamantite, WorldGen.treeBG1, WorldGen.corruptBG,
WorldGen.jungleBG, WorldGen.snowBG, WorldGen.hallowBG, WorldGen.crimsonBG,
WorldGen.desertBG, WorldGen.oceanBG, Main.cloudBGActive, Main.numClouds,
Main.windSpeedTarget, Main.anglerWhoFinishedToday, NPC.savedAngler,
Main.anglerQuest, NPC.savedStylist, NPC.savedTaxCollector, NPC.savedGolfer,
Main.invasionSizeStart, WorldFile._tempCultistDelay,
Main.fastForwardTimeToDawn, NPC.downedFishron, NPC.downedMartians,
NPC.downedAncientCultist, NPC.downedMoonlord, NPC.downedHalloweenKing,
NPC.downedHalloweenTree, NPC.downedChristmasIceQueen,
NPC.downedChristmasSantank, NPC.downedChristmasTree, NPC.downedTowerSolar,
NPC.downedTowerVortex, NPC.downedTowerNebula, NPC.downedTowerStardust,
NPC.TowerActiveSolar, NPC.TowerActiveVortex, NPC.TowerActiveNebula,
NPC.TowerActiveStardust, NPC.LunarApocalypseIsUp, WorldFile._tempPartyManual,
WorldFile._tempPartyGenuine, WorldFile._tempPartyCooldown,
WorldFile.TempPartyCelebratingNPCs, WorldFile._tempSandstormHappening,
WorldFile._tempSandstormTimeLeft, WorldFile._tempSandstormSeverity,
WorldFile._tempSandstormIntendedSeverity, NPC.savedBartender,
WorldGen.mushroomBG, WorldGen.underworldBG, WorldGen.treeBG2,
WorldGen.treeBG3, WorldGen.treeBG4, NPC.combatBookWasUsed,
WorldFile._tempLanternNightCooldown, WorldFile._tempLanternNightGenuine,
WorldFile._tempLanternNightManual, WorldFile._tempLanternNightNextNightIsGenuine,
WorldGen.TreeTops, Main.forceHalloweenForToday, Main.forceXMasForToday,
SavedOreTiers.Copper, SavedOreTiers.Iron, SavedOreTiers.Silver,
SavedOreTiers.Gold, NPC.boughtCat, NPC.boughtDog, NPC.boughtBunny,
NPC.downedEmpressOfLight, NPC.downedQueenSlime, NPC.downedDeerclops,
NPC.unlockedSlimeBlueSpawn, NPC.unlockedMerchantSpawn,
NPC.unlockedDemolitionistSpawn, NPC.unlockedPartyGirlSpawn,
NPC.unlockedDyeTraderSpawn, NPC.unlockedTruffleSpawn,
NPC.unlockedArmsDealerSpawn, NPC.unlockedNurseSpawn, NPC.unlockedPrincessSpawn,
NPC.combatBookVolumeTwoWasUsed, NPC.peddlersSatchelWasUsed,
NPC.unlockedSlimeGreenSpawn, NPC.unlockedSlimeOldSpawn,
NPC.unlockedSlimePurpleSpawn, NPC.unlockedSlimeRainbowSpawn,
NPC.unlockedSlimeRedSpawn, NPC.unlockedSlimeYellowSpawn,
NPC.unlockedSlimeCopperSpawn, Main.fastForwardTimeToDusk,
Main.moondialCooldown, Main.forceHalloweenForever, Main.forceXMasForever,
Main.vampireSeed, Main.infectedSeed, WorldFile._tempMeteorShowerCount,
WorldFile._tempCoinRain, Main.teamBasedSpawnsSeed, Main.dualDungeonsSeed,
Main.moreLightningSeed, Main.noLightningSeed, WorldGen.Manifest
```

## 4. Player stream layout

### 4.1 `Player.Serialize` ordered field families

`Player.Serialize` body: 2,258 IL bytes; 833 instructions. The observed sequence writes:

1. `name`, `difficulty`, `PlayerFileData.GetPlayTime().Ticks`, `hair`, `hairDye`, `team`.
2. `hideVisibleAccessory` flags (array loop), `hideMisc`, `skinVariant`.
3. `statLife`, `statLifeMax`, `statMana`, `statManaMax`, `extraAccessory`, biome-torch flags/state, artisan-bread/consumable-upgrade flags, death counters and tax/progression values.
4. RGB bytes for `hairColor`, `skinColor`, `eyeColor`, `shirtColor`, `underShirtColor`, `pantsColor`, and `shoeColor`.
5. Item arrays: `armor`, `dye`, `inventory`, `miscEquips`, `miscDyes`; then `bank`, `bank2`, `bank3`, `bank4` item arrays and `voidVaultInfo`.
6. Buff arrays `buffType`/`buffTime` for `Player.maxBuffs`; spawn-point records (`spX`, `spY`, `spI`, `spN`, with observed loop bound `200`); `hbLocked`; `hideInfo`; `anglerQuestsFinished`; `DpadRadial`; `builderAccStatus`; bartender quest log; `dead` and conditional `respawnTimer`.
7. Date/time, golfer score, creative tracker and temporary item slots; super-cart flags; `CurrentLoadoutIndex`; `Loadouts`; `voiceVariant`, `voicePitchOffset`; and `oneTimeDialoguesSeen` through an enumerator/count loop.

Observed loop facts include an inventory loop bound constant `58`, loops bounded by each array's length, a `Player.maxBuffs` loop, a `200` spawn-point bound, and an enumerator loop for dialogue strings. Armor/dye/misc/bank records delegate to item serializers.

### 4.2 Item record layout

`Terraria.Item.Serialize(BinaryWriter,bool)` is 52 IL bytes / 19 instructions:

```text
write Item.type as Int32
write Item.stack as Int32
write Item.prefix as Byte
if include-favorite argument is true: write Item.favorited as Boolean
```

`Item.DeserializeFrom(BinaryReader,bool)` reads the corresponding `Int32`, `Int32`, `Byte`, then conditionally `Boolean`, restoring item defaults when type validity checks require it. The bool parameter controls inclusion of the favorite field.

### 4.3 Player load/version branches

`Player.Deserialize` body: 4,224 IL bytes; 1,703 instructions. Anchored version gates include:

| Version condition observed | Read/fallback family |
|---|---|
| `<10` vs `>=10` | legacy Boolean vs byte-oriented difficulty path; a further `<17` branch is present |
| `>=82` | read `hairDye` byte |
| `>=83` | legacy accessory visibility branch |
| `>=107` | gender Boolean path |
| `>=119` | read `hideMisc` byte |
| `>=124` | accessory visibility bytes in the newer path |
| `>=138` | read playtime ticks as `Int64`; older path sets `TimeSpan.Zero` |
| `>=161` | read `skinVariant` byte |
| `>=283` | read `team` byte |

Hair is read as `Int32` and checked against the anchored threshold `228`; the exact fallback assignment is not specified here. The deserializer contains further compatibility branches beyond this table; the table is the verified subset, not an exhaustive version map.

`Player.Serialize`'s observed field list does not contain player world-position coordinates. Do not assume position is stored in this character stream; verify a separate spawn/position path before implementing persistence for coordinates.

## 5. Binary reader/writer behavior matrix

| Data family | Writer / reader | Observed primitive patterns |
|---|---|---|
| World name / seed text | `SaveWorldHeader` / `LoadHeader` | `Write` / `ReadString` plus metadata fields |
| GUID | `UniqueId.ToByteArray()` | byte-array write; reader consumes 16 bytes in the newer header branch |
| Tile matrix | `SaveWorldTiles` / `LoadWorldTiles` | conditional byte/Int16 fields; run counters; x/y dimension loops |
| Chest items | `SaveChests` / `LoadChests` | coordinates, name, item count, item records; loop cap 8000 |
| Signs | `SaveSigns` / `LoadSigns` | string, x, y; loop cap 32000 |
| Player scalar values | `Player.Serialize` / `Deserialize` | Int32, Int64 ticks, Byte, Boolean, and RGB component writes |
| Item record | `Item.Serialize` / `DeserializeFrom` | Int32 type + Int32 stack + Byte prefix + optional Boolean favorite |

## 6. Unresolved exacts

- Exact world-file magic/header bytes and all pointer byte offsets require complete CIL or a real saved-file sample; the current evidence only confirms methods, calls, fields and selected constants.
- Exact tile RLE bit/flag table and full version-compatibility decision tree are not reconstructed from selective anchors.
- No player-coordinate persistence claim is made.
- Method/field tokens are only valid for the recorded SHA-256 and MVID.
