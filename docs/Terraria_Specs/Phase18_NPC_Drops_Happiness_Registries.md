# Terraria NPC drop-rule and happiness registries

## Evidence identity and scope

- Target: Terraria 1.4.5.8; SHA-256 960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3; MVID 2c29f6c3-4bd9-4add-9c58-da159804e083.
- Static targeted managed-assembly analysis of ItemDropDatabase, NPC.NPCLoot, NPC.NPCLoot_DropItems, and personality registry types. No game execution. Registry rows are normalized data descriptors, not source-code excerpts; integer item/NPC identifiers resolve through Phase14_ID_Global_Matrices.md.
- Boundary: a registered rule is not an unconditional final drop. Conditions, expert/master branches, world seeds, luck, player interactions, NPC net IDs, legacy NPCLoot helpers, and runtime item spawning affect outcomes. This sheet preserves the static registry graph; it does not flatten dynamic outcomes into a single unconditional item array.

## Drop pipeline state and ordering

ItemDropDatabase.Populate() order: PrepareNPCNetIDsByTypeDictionary → RegisterGlobalRules → RegisterFoodDrops → RegisterWeirdRules → RegisterTownNPCDrops → RegisterDD2EventDrops → RegisterMiscDrops → RegisterHardmodeFeathers → RegisterYoyos → RegisterStatusImmunityItems → RegisterPirateDrops → RegisterBloodMoonFishingEnemies → RegisterMartianDrops → RegisterBossTrophies → RegisterBosses → RegisterHardmodeDungeonDrops → RegisterMimic → RegisterEclipse → RegisterBloodMoonFishing → TrimDuplicateRulesForNegativeIDs.

NPC-type registration also fans the same rule out to mapped net IDs; global rules are combined by GetRulesForNPCID. The resolver walks the chosen rules in order and resolves each rule node against DropAttemptInfo.

NPC.NPCLoot() rejects client-side/invalid cases and selected world/seed/death-state cases; selects the closest player; grants achievement/bestiary/banner credit for eligible interactions; applies statue-spawn loot gates; calls DoDeathEvents_BeforeLoot → NPCLoot_DropItems → DoDeathEvents; runs Skyblock-only altar/dungeon/hellforge/temple reward helpers for qualifying NPC IDs; then runs money and heal drops. The drop attempt carries player, NPC, expert/master flags, IsInSimulation=false, and Main.rand.

## Normalized static item-drop rule graph

Rows: 425 direct NPC registrations; 186 multiple-NPC registrations; 19 global registrations; additional rows represent condition nodes and success/failure edges. Argument arrays preserve source integer order; rule-node names identify the observed resolver helper. Local rule variables remain named references where the static graph uses them.

Rule argument semantics: Common-family nodes use item ID, chance denominator, then optional quantity bounds; OneFromOptions-family nodes use chance denominator followed by option item IDs; NormalvsExpert-family nodes encode normal/expert branches; BossBag and MasterMode nodes identify their item IDs and mode gates. Other helper nodes retain ordered integer arguments as emitted. Rule references and graph edges are scoped by the Method column. Item names are resolved through the complete ItemID literal map in Phase14.

| Source line | Method | Kind | Target NPC(s) | Normalized rule data |
|---:|---|---|---|---|
| 166 | RegisterBloodMoonFishing | Multiple-NPC rule | 587 (EyeballFlyingFish), 586 (ZombieMerman) | rule=Common{args=[4608, 2, 4, 6]} |
| 167 | RegisterBloodMoonFishing | Multiple-NPC rule | 620 (GoblinShark), 621 (BloodEelHead), 618 (BloodNautilus) | rule=Common{args=[4608, 2, 7, 10]} |
| 168 | RegisterBloodMoonFishing | Multiple-NPC rule | 587 (EyeballFlyingFish), 586 (ZombieMerman) | rule=OneFromOptions{args=[8, 4273]} |
| 169 | RegisterBloodMoonFishing | Multiple-NPC rule | 587 (EyeballFlyingFish), 586 (ZombieMerman) | rule=OneFromOptions{args=[8, 4381]} |
| 170 | RegisterBloodMoonFishing | Multiple-NPC rule | 587 (EyeballFlyingFish), 586 (ZombieMerman) | rule=OneFromOptions{args=[8, 4325]} |
| 171 | RegisterBloodMoonFishing | Multiple-NPC rule | 587 (EyeballFlyingFish), 586 (ZombieMerman) | rule=Common{args=[3213, 15]} |
| 172 | RegisterBloodMoonFishing | NPC rule | 620 (GoblinShark) | rule=Common{args=[4270, 8]} |
| 173 | RegisterBloodMoonFishing | NPC rule | 620 (GoblinShark) | rule=Common{args=[4317, 8]} |
| 174 | RegisterBloodMoonFishing | NPC rule | 621 (BloodEelHead) | rule=Common{args=[4272, 8]} |
| 175 | RegisterBloodMoonFishing | NPC rule | 621 (BloodEelHead) | rule=Common{args=[4317, 8]} |
| 176 | RegisterBloodMoonFishing | NPC rule | 618 (BloodNautilus) | rule=NormalvsExpert{args=[4269, 2, 1]} |
| 177 | RegisterBloodMoonFishing | NPC rule | 618 (BloodNautilus) | rule=Common{args=[4054, 10]} |
| 178 | RegisterBloodMoonFishing | NPC rule | 618 (BloodNautilus) | rule=NormalvsExpert{args=[4271, 2, 1]} |
| 179 | RegisterBloodMoonFishing | Multiple-NPC rule | 53 (TheGroom), 536 (TheBride) | rule=ScalingWithOnlyBadLuck{args=[4271, 5]} |
| 180 | RegisterBloodMoonFishing | Condition/rule node |  | condition=IsBloodMoonAndNotFromStatue; variable=condition; args=[] |
| 181 | RegisterBloodMoonFishing | Multiple-NPC rule | 489 (BloodZombie), 490 (Drippler) | rule=ByCondition{args=[condition, 4271, 100]} |
| 182 | RegisterBloodMoonFishing | Multiple-NPC rule | 587 (EyeballFlyingFish), 586 (ZombieMerman), 621 (BloodEelHead), 620 (GoblinShark) | rule=ByCondition{args=[condition, 4271, 25]} |
| 187 | RegisterEclipse | NPC rule | 461 (CreatureFromTheDeep) | rule=ExpertGetsRerolls{args=[497, 50, 1]} |
| 188 | RegisterEclipse | Multiple-NPC rule | 159 (Vampire), 158 (VampireBat) | rule=ExpertGetsRerolls{args=[900, 35, 1]} |
| 189 | RegisterEclipse | Multiple-NPC rule | 159 (Vampire), 158 (VampireBat) | rule=Common{args=[5597, 40]} |
| 190 | RegisterEclipse | NPC rule | 251 (Eyezor) | rule=ExpertGetsRerolls{args=[1311, 15, 1]} |
| 191 | RegisterEclipse | NPC rule | 251 (Eyezor) | rule=Common{args=[5239, 15]} |
| 192 | RegisterEclipse | NPC rule | 251 (Eyezor) | rule=Common{args=[5236, 15]} |
| 193 | RegisterEclipse | NPC rule | 477 (Mothron) | rule=Common{args=[5237, 15]} |
| 194 | RegisterEclipse | NPC rule | 253 (Reaper) | rule=Common{args=[5223, 60]} |
| 195 | RegisterEclipse | NPC rule | 460 (Butcher) | rule=Common{args=[5227, 60]} |
| 196 | RegisterEclipse | NPC rule | 469 (ThePossessed) | rule=Common{args=[5260, 60]} |
| 197 | RegisterEclipse | Multiple-NPC rule | 166 (SwampThing), 162 (Frankenstein) | rule=Common{args=[5261, 450]} |
| 198 | RegisterEclipse | NPC rule | 462 (Fritz) | rule=Common{args=[5262, 60]} |
| 199 | RegisterEclipse | Condition/rule node |  | condition=DownedAllMechBosses; variable=condition; args=[] |
| 200 | RegisterEclipse | Condition/rule node |  | condition=DownedPlantera; variable=condition2; args=[] |
| 201 | RegisterEclipse | NPC rule | 477 (Mothron) | rule=LeadingConditionRule{args=[condition]} |
| 202 | RegisterEclipse | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=LeadingConditionRule{args=[condition2]} |
| 203 | RegisterEclipse | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=ExpertGetsRerolls{args=[1570, 4, 1]} |
| 204 | RegisterEclipse | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=ExpertGetsRerolls{args=[2770, 20, 1]} |
| 205 | RegisterEclipse | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=ExpertGetsRerolls{args=[3292, 3, 1]} |
| 206 | RegisterEclipse | NPC rule | 253 (Reaper) | rule=LeadingConditionRule{args=[condition]}; tail=.OnSuccess{args=[ExpertGetsRerolls{args=[1327, 40, 1]}]}; |
| 207 | RegisterEclipse | NPC rule | 460 (Butcher) | rule=LeadingConditionRule{args=[condition2]}; tail=.OnSuccess{args=[ExpertGetsRerolls{args=[3098, 40, 1]}]}; |
| 208 | RegisterEclipse | NPC rule | 460 (Butcher) | rule=ExpertGetsRerolls{args=[4740, 50, 1]} |
| 209 | RegisterEclipse | NPC rule | 460 (Butcher) | rule=ExpertGetsRerolls{args=[4741, 50, 1]} |
| 210 | RegisterEclipse | NPC rule | 460 (Butcher) | rule=ExpertGetsRerolls{args=[4742, 50, 1]} |
| 211 | RegisterEclipse | NPC rule | 468 (DrManFly) | rule=LeadingConditionRule{args=[condition2]}; tail=.OnSuccess{args=[ExpertGetsRerolls{args=[3105, 40, 1]}]}; |
| 212 | RegisterEclipse | NPC rule | 468 (DrManFly) | rule=ExpertGetsRerolls{args=[4738, 50, 1]} |
| 213 | RegisterEclipse | NPC rule | 468 (DrManFly) | rule=ExpertGetsRerolls{args=[4739, 50, 1]} |
| 214 | RegisterEclipse | NPC rule | 466 (Psycho) | rule=LeadingConditionRule{args=[condition2]}; tail=.OnSuccess{args=[ExpertGetsRerolls{args=[3106, 40, 1]}]}; |
| 215 | RegisterEclipse | NPC rule | 467 (DeadlySphere) | rule=LeadingConditionRule{args=[condition2]}; tail=.OnSuccess{args=[ExpertGetsRerolls{args=[3249, 30, 1]}]}; |
| 218 | RegisterEclipse | Rule-graph edge |  | owner=itemDropRule; outcome=OnSuccess; child=Common{args=[3108, 1, 100, 200]} |
| 219 | RegisterEclipse | Rule-graph edge |  | owner=itemDropRule2; outcome=OnSuccess; child=Common{args=[3108, 1, 100, 200]} |
| 220 | RegisterEclipse | NPC rule | 463 (Nailhead) | rule=LeadingConditionRule{args=[condition2]}; tail=.OnSuccess{args=[DropBasedOnExpertMode{args=[itemDropRule, itemDropRule2]}]}; |
| 225 | RegisterMimic | NPC rule | 85 (Mimic) | rule=LeadingConditionRule{args=[Cond.NotRemixSeedHardmode{args=[]}]}; tail=.OnSuccess{args=[OneFromOptions{args=[1, 437, 517, 535, 536, 532, 554]}]}; |
| 226 | RegisterMimic | NPC rule | 85 (Mimic) | rule=LeadingConditionRule{args=[Cond.RemixSeedHardmode{args=[]}]}; tail=.OnSuccess{args=[OneFromOptions{args=[1, 437, 3069, 535, 536, 532, 554]}]}; |
| 227 | RegisterMimic | NPC rule | 85 (Mimic) | rule=LeadingConditionRule{args=[Cond.Easymode{args=[]}]}; tail=.OnSuccess{args=[OneFromOptions{args=[1, 49, 50, 53, 54, 5011, 975]}]}; |
| 228 | RegisterMimic | NPC rule | 85 (Mimic) | rule=LeadingConditionRule{args=[Cond.Easymode{args=[]}]}; tail=.OnSuccess{args=[Common{args=[930, 20]}]}; |
| 229 | RegisterMimic | NPC rule | 85 (Mimic) | rule=LeadingConditionRule{args=[Cond.Easymode{args=[]}]}; tail=.OnSuccess{args=[Common{args=[997, 20]}]}; |
| 236 | RegisterIceMimic | Rule-graph edge |  | owner=itemDropRule; outcome=OnFailedRoll; child=LeadingConditionRule{args=[Cond.NotRemixSeedHardmode{args=[]}]} |
| 237 | RegisterIceMimic | Rule-graph edge |  | owner=itemDropRule; outcome=OnFailedRoll; child=LeadingConditionRule{args=[Cond.RemixSeedHardmode{args=[]}]} |
| 238 | RegisterIceMimic | Rule-graph edge |  | owner=itemDropRule; outcome=OnFailedRoll; child=LeadingConditionRule{args=[Cond.RemixSeedEasymode{args=[]}]} |
| 239 | RegisterIceMimic | Rule-graph edge |  | owner=itemDropRule; outcome=OnFailedRoll; child=LeadingConditionRule{args=[Cond.NotRemixSeedEasymode{args=[]}]} |
| 240 | RegisterIceMimic | NPC rule | 629 (IceMimic) | rule=itemDropRule |
| 241 | RegisterIceMimic | NPC rule | 629 (IceMimic) | rule=LeadingConditionRule{args=[Cond.Easymode{args=[]}]}; tail=.OnSuccess{args=[Common{args=[997, 20]}]}; |
| 258 | RegisterHardmodeDungeonDrops | NPC rule | 290 (Paladin) | rule=ExpertGetsRerolls{args=[1513, 15, 1]} |
| 259 | RegisterHardmodeDungeonDrops | NPC rule | 290 (Paladin) | rule=ExpertGetsRerolls{args=[938, 10, 1]} |
| 260 | RegisterHardmodeDungeonDrops | NPC rule | 287 (BoneLee) | rule=ExpertGetsRerolls{args=[977, 12, 1]} |
| 261 | RegisterHardmodeDungeonDrops | NPC rule | 287 (BoneLee) | rule=ExpertGetsRerolls{args=[963, 12, 1]} |
| 262 | RegisterHardmodeDungeonDrops | NPC rule | 291 (SkeletonSniper) | rule=ExpertGetsRerolls{args=[1300, 12, 1]} |
| 263 | RegisterHardmodeDungeonDrops | NPC rule | 291 (SkeletonSniper) | rule=ExpertGetsRerolls{args=[1254, 12, 1]} |
| 264 | RegisterHardmodeDungeonDrops | NPC rule | 292 (TacticalSkeleton) | rule=ExpertGetsRerolls{args=[1514, 12, 1]} |
| 265 | RegisterHardmodeDungeonDrops | NPC rule | 292 (TacticalSkeleton) | rule=ExpertGetsRerolls{args=[679, 12, 1]} |
| 266 | RegisterHardmodeDungeonDrops | NPC rule | 293 (SkeletonCommando) | rule=ExpertGetsRerolls{args=[759, 18, 1]} |
| 267 | RegisterHardmodeDungeonDrops | NPC rule | 289 (GiantCursedSkull) | rule=ExpertGetsRerolls{args=[4789, 25, 1]} |
| 268 | RegisterHardmodeDungeonDrops | Multiple-NPC rule | 281 (RaggedCaster), 282 (RaggedCasterOpenCoat) | rule=ExpertGetsRerolls{args=[1446, 20, 1]} |
| 269 | RegisterHardmodeDungeonDrops | Multiple-NPC rule | 283 (Necromancer), 284 (NecromancerArmored) | rule=ExpertGetsRerolls{args=[1444, 20, 1]} |
| 270 | RegisterHardmodeDungeonDrops | Multiple-NPC rule | 285 (DiabolistRed), 286 (DiabolistWhite) | rule=ExpertGetsRerolls{args=[1445, 20, 1]} |
| 271 | RegisterHardmodeDungeonDrops | Multiple-NPC rule | npcNetIds | rule=ExpertGetsRerolls{args=[1183, 400, 1]} |
| 272 | RegisterHardmodeDungeonDrops | Multiple-NPC rule | npcNetIds | rule=ExpertGetsRerolls{args=[1266, 300, 1]} |
| 273 | RegisterHardmodeDungeonDrops | Multiple-NPC rule | npcNetIds | rule=ExpertGetsRerolls{args=[671, 200, 1]} |
| 274 | RegisterHardmodeDungeonDrops | Multiple-NPC rule | npcNetIds | rule=ExpertGetsRerolls{args=[4679, 200, 1]} |
| 275 | RegisterHardmodeDungeonDrops | NPC rule | 288 (DungeonSpirit) | rule=Common{args=[1508, 1, 1, 2]} |
| 307 | RegisterBoss_QueenSlime | NPC rule | 657 (QueenSlimeBoss) [local type] | rule=BossBag{args=[4957]} |
| 308 | RegisterBoss_QueenSlime | NPC rule | 657 (QueenSlimeBoss) [local type] | rule=MasterModeCommonDrop{args=[4950]} |
| 309 | RegisterBoss_QueenSlime | NPC rule | 657 (QueenSlimeBoss) [local type] | rule=MasterModeDropOnAllPlayers{args=[4960, _masterModeDropRng]} |
| 310 | RegisterBoss_QueenSlime | Condition/rule node |  | branch=leadingConditionRule; gate=Cond.NotExpert{args=[]} |
| 311 | RegisterBoss_QueenSlime | NPC rule | 657 (QueenSlimeBoss) [local type] | rule=leadingConditionRule |
| 312 | RegisterBoss_QueenSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4986, 1, 25, 75]} |
| 313 | RegisterBoss_QueenSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4959, 7]} |
| 314 | RegisterBoss_QueenSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=OneFromOptions{args=[1, 4982, 4983, 4984]} |
| 315 | RegisterBoss_QueenSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4758, 4]} |
| 316 | RegisterBoss_QueenSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4981, 4]} |
| 317 | RegisterBoss_QueenSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=NotScalingWithLuck{args=[4980, 3]} |
| 323 | RegisterBoss_HallowBoss | NPC rule | 636 (HallowBoss) [local type] | rule=BossBag{args=[4782]} |
| 324 | RegisterBoss_HallowBoss | NPC rule | 636 (HallowBoss) [local type] | rule=MasterModeCommonDrop{args=[4949]} |
| 325 | RegisterBoss_HallowBoss | NPC rule | 636 (HallowBoss) [local type] | rule=MasterModeDropOnAllPlayers{args=[4811, _masterModeDropRng]} |
| 326 | RegisterBoss_HallowBoss | Condition/rule node |  | branch=leadingConditionRule; gate=Cond.NotExpert{args=[]} |
| 327 | RegisterBoss_HallowBoss | NPC rule | 636 (HallowBoss) [local type] | rule=leadingConditionRule; tail=.OnSuccess{args=[OneFromOptions{args=[1, 4923, 4952, 4953, 4914]}]}; |
| 328 | RegisterBoss_HallowBoss | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4823, 15]} |
| 329 | RegisterBoss_HallowBoss | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4778, 4, 3, 3]} |
| 330 | RegisterBoss_HallowBoss | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4715, 50]} |
| 331 | RegisterBoss_HallowBoss | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[4784, 7]} |
| 332 | RegisterBoss_HallowBoss | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[5075, 20]} |
| 333 | RegisterBoss_HallowBoss | Condition/rule node |  | branch=entry; gate=Cond.EmpressOfLightIsGenuinelyEnraged{args=[]} |
| 334 | RegisterBoss_HallowBoss | NPC rule | 636 (HallowBoss) [local type] | rule=entry; tail=.OnSuccess{args=[Common{args=[5005]}]}; |
| 339 | RegisterBoss_PumpkinMoon | Condition/rule node |  | condition=PumpkinMoonDropGatingChance; variable=condition; args=[] |
| 340 | RegisterBoss_PumpkinMoon | Condition/rule node |  | condition=PumpkinMoonDropGateForTrophies; variable=condition2; args=[] |
| 341 | RegisterBoss_PumpkinMoon | Condition/rule node |  | Cond.IsPumpkinMoon{args=[]}; |
| 342 | RegisterBoss_PumpkinMoon | Condition/rule node |  | Cond.FromCertainWaveAndAbove{args=[15]}; |
| 343 | RegisterBoss_PumpkinMoon | NPC rule | 315 (HeadlessHorseman) | rule=ByCondition{args=[condition, 1857, 20]} |
| 345 | RegisterBoss_PumpkinMoon | Multiple-NPC rule | npcNetIds | rule=LeadingConditionRule{args=[condition]} |
| 346 | RegisterBoss_PumpkinMoon | NPC rule | 325 (MourningWood) | rule=LeadingConditionRule{args=[condition]} |
| 348 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=itemDropRule; outcome=OnSuccess; child=Common{args=[1836, 1, 30, 60]} |
| 349 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=OneFromRulesRule{args=[1, Common{args=[1829]}, Common{args=[1831]}, itemDropRule, Common{args=[1837]}, Common{args=[1845]}]} |
| 350 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=ByCondition{args=[condition2, 1855]} |
| 351 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=ByCondition{args=[Cond.IsExpert{args=[]}, 4444, 5]} |
| 352 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=MasterModeCommonDrop{args=[4941]} |
| 353 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=MasterModeDropOnAllPlayers{args=[4793, _masterModeDropRng]} |
| 355 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=itemDropRule2; outcome=OnSuccess; child=Common{args=[1783, 1, 50, 100]} |
| 357 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=itemDropRule3; outcome=OnSuccess; child=Common{args=[1785, 1, 25, 50]} |
| 358 | RegisterBoss_PumpkinMoon | NPC rule | 327 (Pumpking) | rule=LeadingConditionRule{args=[condition]} |
| 359 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=OneFromRulesRule{args=[1, itemDropRule2, itemDropRule3, Common{args=[1811]}, Common{args=[1826]}, Common{args=[1801]}, Common{args=[1802]}, Common{args=[4680]}, Common{args=[1798]}]} |
| 360 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=ByCondition{args=[condition2, 1856]} |
| 361 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=MasterModeCommonDrop{args=[4942]} |
| 362 | RegisterBoss_PumpkinMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=MasterModeDropOnAllPlayers{args=[4812, _masterModeDropRng]} |
| 363 | RegisterBoss_PumpkinMoon | NPC rule | 326 (Splinterling) | rule=DropBasedOnMasterAndExpertMode{args=[CommonDrop{args=[1729, 1, 1, 3]}, CommonDrop{args=[1729, 1, 1, 4]}, CommonDrop{args=[1729, 1, 2, 4]}]} |
| 364 | RegisterBoss_PumpkinMoon | NPC rule | 325 (MourningWood) | rule=DropBasedOnMasterAndExpertMode{args=[CommonDrop{args=[1729, 1, 15, 30]}, CommonDrop{args=[1729, 1, 25, 40]}, CommonDrop{args=[1729, 1, 30, 50]}]} |
| 369 | RegisterBoss_FrostMoon | Condition/rule node |  | condition=FrostMoonDropGatingChance; variable=condition; args=[] |
| 370 | RegisterBoss_FrostMoon | Condition/rule node |  | condition=FrostMoonDropGateForTrophies; variable=condition2; args=[] |
| 371 | RegisterBoss_FrostMoon | Condition/rule node |  | condition=FromCertainWaveAndAbove; variable=condition3; args=[15] |
| 372 | RegisterBoss_FrostMoon | NPC rule | 344 (Everscream) | rule=LeadingConditionRule{args=[condition]} |
| 373 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=ByCondition{args=[condition2, 1962]} |
| 374 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=Common{args=[1871, 15]} |
| 375 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=MasterModeCommonDrop{args=[4944]} |
| 376 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=MasterModeDropOnAllPlayers{args=[4813, _masterModeDropRng]} |
| 377 | RegisterBoss_FrostMoon | NPC rule | 345 (IceQueen) | rule=LeadingConditionRule{args=[condition]} |
| 378 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=ByCondition{args=[condition2, 1960]} |
| 379 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=ByCondition{args=[condition3, 1914, 15]} |
| 380 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=Common{args=[1959, 15]} |
| 381 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=MasterModeCommonDrop{args=[4943]} |
| 382 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule2; outcome=OnSuccess; child=MasterModeDropOnAllPlayers{args=[4814, _masterModeDropRng]} |
| 383 | RegisterBoss_FrostMoon | NPC rule | 346 (SantaNK1) | rule=LeadingConditionRule{args=[condition]} |
| 384 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule3; outcome=OnSuccess; child=ByCondition{args=[condition2, 1961]} |
| 385 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule3; outcome=OnSuccess; child=OneFromOptions{args=[1, 1910, 1929]} |
| 386 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule3; outcome=OnSuccess; child=MasterModeCommonDrop{args=[4945]} |
| 387 | RegisterBoss_FrostMoon | Rule-graph edge |  | owner=rule3; outcome=OnSuccess; child=MasterModeDropOnAllPlayers{args=[4794, _masterModeDropRng]} |
| 389 | RegisterBoss_FrostMoon | Multiple-NPC rule | npcNetIds | rule=OneFromOptions{args=[200, 1943, 1944, 1945]} |
| 390 | RegisterBoss_FrostMoon | NPC rule | 341 (PresentMimic) | rule=ByCondition{args=[Cond.IsChristmas{args=[]}, 1869]} |
| 396 | RegisterBoss_KingSlime | NPC rule | 50 (KingSlime) [local type] | rule=BossBag{args=[3318]} |
| 397 | RegisterBoss_KingSlime | NPC rule | 50 (KingSlime) [local type] | rule=MasterModeCommonDrop{args=[4929]} |
| 398 | RegisterBoss_KingSlime | NPC rule | 50 (KingSlime) [local type] | rule=MasterModeDropOnAllPlayers{args=[4797, _masterModeDropRng]} |
| 399 | RegisterBoss_KingSlime | Condition/rule node |  | branch=leadingConditionRule; gate=Cond.NotExpert{args=[]} |
| 400 | RegisterBoss_KingSlime | NPC rule | 50 (KingSlime) [local type] | rule=leadingConditionRule |
| 401 | RegisterBoss_KingSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[2430, 4]} |
| 402 | RegisterBoss_KingSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[2493, 7]} |
| 403 | RegisterBoss_KingSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=OneFromOptions{args=[1, 256, 257, 258]} |
| 404 | RegisterBoss_KingSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=NotScalingWithLuck{args=[2585, 3]} |
| 405 | RegisterBoss_KingSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[998]} |
| 406 | RegisterBoss_KingSlime | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[1309, 30]} |
| 412 | RegisterBoss_Plantera | NPC rule | 262 (Plantera) [local type] | rule=BossBag{args=[3328]} |
| 413 | RegisterBoss_Plantera | NPC rule | 262 (Plantera) [local type] | rule=MasterModeCommonDrop{args=[4934]} |
| 414 | RegisterBoss_Plantera | NPC rule | 262 (Plantera) [local type] | rule=MasterModeDropOnAllPlayers{args=[4806, _masterModeDropRng]} |
| 415 | RegisterBoss_Plantera | Condition/rule node |  | branch=leadingConditionRule; gate=Cond.NotExpert{args=[]} |
| 416 | RegisterBoss_Plantera | NPC rule | 262 (Plantera) [local type] | rule=leadingConditionRule |
| 417 | RegisterBoss_Plantera | Condition/rule node |  | branch=leadingConditionRule2; gate=Cond.FirstTimeKillingPlantera{args=[]} |
| 418 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=leadingConditionRule2 |
| 419 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[2109, 7]} |
| 420 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[1141]} |
| 421 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[1182, 20]} |
| 422 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[1305, 50]} |
| 423 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[1157, 4]} |
| 424 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=Common{args=[3021, 10]} |
| 426 | RegisterBoss_Plantera | Rule-graph edge |  | owner=itemDropRule; outcome=OnSuccess; child=Common{args=[771, 1, 50, 150]} |
| 427 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule2; outcome=OnSuccess; child=itemDropRule |
| 428 | RegisterBoss_Plantera | Rule-graph edge |  | owner=leadingConditionRule2; outcome=OnFailedConditions; child=OneFromRulesRule{args=[1, itemDropRule, Common{args=[1255]}, Common{args=[788]}, Common{args=[1178]}, Common{args=[1259]}, Common{args=[1155]}, Common{args=[3018]}, Common{args=[5477]}]} |
| 433 | RegisterBoss_SkeletronPrime | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 435 | RegisterBoss_SkeletronPrime | NPC rule | 127 (SkeletronPrime) [local type] | rule=BossBag{args=[3327]} |
| 436 | RegisterBoss_SkeletronPrime | NPC rule | 127 (SkeletronPrime) [local type] | rule=MasterModeCommonDrop{args=[4933]} |
| 437 | RegisterBoss_SkeletronPrime | NPC rule | 127 (SkeletronPrime) [local type] | rule=MasterModeDropOnAllPlayers{args=[4805, _masterModeDropRng]} |
| 438 | RegisterBoss_SkeletronPrime | NPC rule | 127 (SkeletronPrime) [local type] | rule=ByCondition{args=[condition, 2107, 7]} |
| 439 | RegisterBoss_SkeletronPrime | NPC rule | 127 (SkeletronPrime) [local type] | rule=ByCondition{args=[condition, 1225, 1, 15, 30]} |
| 440 | RegisterBoss_SkeletronPrime | NPC rule | 127 (SkeletronPrime) [local type] | rule=ByCondition{args=[condition, 547, 1, 25, 40]} |
| 441 | RegisterBoss_SkeletronPrime | NPC rule | 127 (SkeletronPrime) [local type] | rule=ByCondition{args=[Cond.MechdusaKill{args=[]}, 5382]} |
| 446 | RegisterBoss_TheDestroyer | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 448 | RegisterBoss_TheDestroyer | NPC rule | 134 (TheDestroyer) [local type] | rule=BossBag{args=[3325]} |
| 449 | RegisterBoss_TheDestroyer | NPC rule | 134 (TheDestroyer) [local type] | rule=MasterModeCommonDrop{args=[4932]} |
| 450 | RegisterBoss_TheDestroyer | NPC rule | 134 (TheDestroyer) [local type] | rule=MasterModeDropOnAllPlayers{args=[4803, _masterModeDropRng]} |
| 451 | RegisterBoss_TheDestroyer | NPC rule | 134 (TheDestroyer) [local type] | rule=ByCondition{args=[condition, 2113, 7]} |
| 452 | RegisterBoss_TheDestroyer | NPC rule | 134 (TheDestroyer) [local type] | rule=ByCondition{args=[condition, 1225, 1, 15, 30]} |
| 453 | RegisterBoss_TheDestroyer | NPC rule | 134 (TheDestroyer) [local type] | rule=ByCondition{args=[condition, 548, 1, 25, 40]} |
| 454 | RegisterBoss_TheDestroyer | NPC rule | 134 (TheDestroyer) [local type] | rule=ByCondition{args=[Cond.MechdusaKill{args=[]}, 5382]} |
| 459 | RegisterBoss_Twins | Condition/rule node |  | branch=leadingConditionRule; gate=Cond.MissingTwin{args=[]} |
| 460 | RegisterBoss_Twins | Condition/rule node |  | branch=leadingConditionRule2; gate=Cond.NotExpert{args=[]} |
| 461 | RegisterBoss_Twins | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=BossBag{args=[3326]} |
| 462 | RegisterBoss_Twins | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=leadingConditionRule2 |
| 463 | RegisterBoss_Twins | Rule-graph edge |  | owner=leadingConditionRule2; outcome=OnSuccess; child=Common{args=[2106, 7]} |
| 464 | RegisterBoss_Twins | Rule-graph edge |  | owner=leadingConditionRule2; outcome=OnSuccess; child=Common{args=[1225, 1, 15, 30]} |
| 465 | RegisterBoss_Twins | Rule-graph edge |  | owner=leadingConditionRule2; outcome=OnSuccess; child=Common{args=[549, 1, 25, 40]} |
| 466 | RegisterBoss_Twins | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=MasterModeCommonDrop{args=[4931]} |
| 467 | RegisterBoss_Twins | Rule-graph edge |  | owner=leadingConditionRule; outcome=OnSuccess; child=MasterModeDropOnAllPlayers{args=[4804, _masterModeDropRng]} |
| 468 | RegisterBoss_Twins | Multiple-NPC rule | 126 (Spazmatism), 125 (Retinazer) | rule=leadingConditionRule |
| 469 | RegisterBoss_Twins | Multiple-NPC rule | 126 (Spazmatism), 125 (Retinazer) | rule=ByCondition{args=[Cond.MechdusaKill{args=[]}, 5382]} |
| 474 | RegisterBoss_EOC | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 475 | RegisterBoss_EOC | Condition/rule node |  | condition=IsCrimsonAndNotExpert; variable=condition2; args=[] |
| 476 | RegisterBoss_EOC | Condition/rule node |  | condition=IsCorruptionAndNotExpert; variable=condition3; args=[] |
| 478 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=BossBag{args=[3319]} |
| 479 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=MasterModeCommonDrop{args=[4924]} |
| 480 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=MasterModeCommonDrop{args=[3763]} |
| 481 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=MasterModeDropOnAllPlayers{args=[4798, _masterModeDropRng]} |
| 482 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=ByCondition{args=[condition, 2112, 7]} |
| 483 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=ByCondition{args=[condition, 1299, 40]} |
| 484 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=ByCondition{args=[condition, 47, 1, 20, 50]} |
| 485 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=ByCondition{args=[condition2, 880, 1, 30, 90]} |
| 486 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=ByCondition{args=[condition2, 2171, 1, 1, 3]} |
| 487 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=ByCondition{args=[condition3, 56, 1, 30, 90]} |
| 488 | RegisterBoss_EOC | NPC rule | 4 (EyeofCthulhu) [local type] | rule=ByCondition{args=[condition3, 59, 1, 1, 3]} |
| 493 | RegisterBoss_BOC | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 495 | RegisterBoss_BOC | NPC rule | 266 (BrainofCthulhu) [local type] | rule=BossBag{args=[3321]} |
| 496 | RegisterBoss_BOC | NPC rule | 266 (BrainofCthulhu) [local type] | rule=MasterModeCommonDrop{args=[4926]} |
| 497 | RegisterBoss_BOC | NPC rule | 266 (BrainofCthulhu) [local type] | rule=MasterModeDropOnAllPlayers{args=[4800, _masterModeDropRng]} |
| 498 | RegisterBoss_BOC | NPC rule | 266 (BrainofCthulhu) [local type] | rule=ByCondition{args=[condition, 880, 1, 40, 90]} |
| 499 | RegisterBoss_BOC | NPC rule | 266 (BrainofCthulhu) [local type] | rule=ByCondition{args=[condition, 2104, 7]} |
| 500 | RegisterBoss_BOC | NPC rule | 266 (BrainofCthulhu) [local type] | rule=ByCondition{args=[condition, 3060, 20]} |
| 502 | RegisterBoss_BOC | NPC rule | type2 | rule=DropBasedOnMasterAndExpertMode{args=[CommonDrop{args=[1329, 3, 2, 5, 2]}, CommonDrop{args=[1329, 3, 1, 3, 2]}, CommonDrop{args=[1329, 4, 1, 2, 2]}]} |
| 503 | RegisterBoss_BOC | NPC rule | type2 | rule=DropBasedOnMasterAndExpertMode{args=[CommonDrop{args=[880, 3, 5, 12, 2]}, CommonDrop{args=[880, 3, 5, 7, 2]}, CommonDrop{args=[880, 3, 2, 4, 2]}]} |
| 508 | RegisterBoss_EOW | Condition/rule node |  | condition=LegacyHack_IsBossAndExpert; variable=condition; args=[] |
| 509 | RegisterBoss_EOW | Condition/rule node |  | condition=LegacyHack_IsBossAndNotExpert; variable=condition2; args=[] |
| 511 | RegisterBoss_EOW | Multiple-NPC rule | npcNetIds | rule=DropBasedOnMasterAndExpertMode{args=[Common{args=[86, 2, 1, 2]}, Common{args=[86, 5, 1, 2]}, Common{args=[86, 10, 1, 2]}]} |
| 512 | RegisterBoss_EOW | Multiple-NPC rule | npcNetIds | rule=DropBasedOnMasterAndExpertMode{args=[Common{args=[56, 2, 2, 5]}, Common{args=[56, 2, 1, 3]}, Common{args=[56, 3, 1, 2]}]} |
| 513 | RegisterBoss_EOW | Multiple-NPC rule | npcNetIds | rule=BossBagByCondition{args=[condition, 3320]} |
| 514 | RegisterBoss_EOW | Multiple-NPC rule | npcNetIds | rule=LeadingConditionRule{args=[Cond.LegacyHack_IsABoss{args=[]}]} |
| 515 | RegisterBoss_EOW | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=MasterModeCommonDrop{args=[4925]} |
| 516 | RegisterBoss_EOW | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=MasterModeDropOnAllPlayers{args=[4799, _masterModeDropRng]} |
| 517 | RegisterBoss_EOW | Multiple-NPC rule | npcNetIds | rule=ByCondition{args=[condition2, 56, 1, 20, 60]} |
| 518 | RegisterBoss_EOW | Multiple-NPC rule | npcNetIds | rule=ByCondition{args=[condition2, 994, 20]} |
| 519 | RegisterBoss_EOW | Multiple-NPC rule | npcNetIds | rule=ByCondition{args=[condition2, 2111, 7]} |
| 524 | RegisterBoss_Deerclops | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 526 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=BossBag{args=[5111]} |
| 527 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=MasterModeCommonDrop{args=[5110]} |
| 528 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=MasterModeDropOnAllPlayers{args=[5090, _masterModeDropRng]} |
| 529 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=ByCondition{args=[condition, 5109, 7]} |
| 530 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=ByCondition{args=[condition, 5098, 3]} |
| 531 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=ByCondition{args=[condition, 5101, 3]} |
| 532 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=ByCondition{args=[condition, 5113, 3]} |
| 533 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=ByCondition{args=[condition, 5385, 14]} |
| 534 | RegisterBoss_Deerclops | NPC rule | 668 (Deerclops) [local type] | rule=LeadingConditionRule{args=[condition]}; tail=.OnSuccess{args=[OneFromRulesRule{args=[1, OneFromOptionsNotScalingWithLuck{args=[1, 5117, 5118, 5119, 5095]}]}]}; |
| 539 | RegisterBoss_QueenBee | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 541 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=BossBag{args=[3322]} |
| 542 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=MasterModeCommonDrop{args=[4928]} |
| 543 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=MasterModeDropOnAllPlayers{args=[4802, _masterModeDropRng]} |
| 544 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 2108, 7]} |
| 545 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=DropBasedOnExpertMode{args=[OneFromOptionsNotScalingWithLuck{args=[1, 1121, 1123, 2888]}, DropNothing{args=[]}]} |
| 546 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 1132, 3]} |
| 547 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 1170, 15]} |
| 548 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 2502, 20]} |
| 549 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 5483, 15]} |
| 550 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 1129, 3]}; tail=.OnFailedRoll{args=[OneFromOptionsNotScalingWithLuck{args=[2, 842, 843, 844]}]}; |
| 551 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 1130, 4, 10, 30, 3]} |
| 552 | RegisterBoss_QueenBee | NPC rule | 222 (QueenBee) [local type] | rule=ByCondition{args=[condition, 2431, 1, 17, 30]} |
| 557 | RegisterBoss_Skeletron | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 558 | RegisterBoss_Skeletron | Condition/rule node |  | condition=RedHatSkeletron; variable=condition2; args=[] |
| 560 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=BossBag{args=[3323]} |
| 561 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=MasterModeCommonDrop{args=[4927]} |
| 562 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=MasterModeDropOnAllPlayers{args=[4801, _masterModeDropRng]} |
| 563 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=ByCondition{args=[condition, 1281, 7]}; tail=.OnFailedRoll{args=[Common{args=[1273, 7]}]}.OnFailedRoll{args=[Common{args=[1313, 7]}]}; |
| 564 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=Common{args=[4993, 7]} |
| 565 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=ByCondition{args=[condition2, 5624]} |
| 566 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=ByCondition{args=[condition2, 5625]} |
| 567 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=ByCondition{args=[condition2, 5626]} |
| 568 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=ByCondition{args=[condition2, 5737]} |
| 569 | RegisterBoss_Skeletron | NPC rule | 35 (SkeletronHead) [local type] | rule=ByCondition{args=[condition2, 5628]} |
| 574 | RegisterBoss_WOF | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 576 | RegisterBoss_WOF | NPC rule | 113 (WallofFlesh) [local type] | rule=BossBag{args=[3324]} |
| 577 | RegisterBoss_WOF | NPC rule | 113 (WallofFlesh) [local type] | rule=MasterModeCommonDrop{args=[4930]} |
| 578 | RegisterBoss_WOF | NPC rule | 113 (WallofFlesh) [local type] | rule=MasterModeDropOnAllPlayers{args=[4795, _masterModeDropRng]} |
| 579 | RegisterBoss_WOF | NPC rule | 113 (WallofFlesh) [local type] | rule=ByCondition{args=[condition, 2105, 7]} |
| 580 | RegisterBoss_WOF | NPC rule | 113 (WallofFlesh) [local type] | rule=ByCondition{args=[condition, 367]} |
| 581 | RegisterBoss_WOF | NPC rule | 113 (WallofFlesh) [local type] | rule=LeadingConditionRule{args=[condition]}; tail=.OnSuccess{args=[OneFromOptionsNotScalingWithLuck{args=[1, 490, 491, 489, 2998]}]}; |
| 582 | RegisterBoss_WOF | NPC rule | 113 (WallofFlesh) [local type] | rule=LeadingConditionRule{args=[condition]}; tail=.OnSuccess{args=[OneFromOptionsNotScalingWithLuck{args=[1, 426, 434, 514, 4912]}]}; |
| 588 | RegisterBoss_AncientCultist | NPC rule | 439 (CultistBoss) [local type] | rule=MasterModeCommonDrop{args=[4937]} |
| 589 | RegisterBoss_AncientCultist | NPC rule | 439 (CultistBoss) [local type] | rule=MasterModeDropOnAllPlayers{args=[4809, _masterModeDropRng]} |
| 590 | RegisterBoss_AncientCultist | NPC rule | 439 (CultistBoss) [local type] | rule=Common{args=[3372, 7]} |
| 591 | RegisterBoss_AncientCultist | NPC rule | 439 (CultistBoss) [local type] | rule=Common{args=[3549]} |
| 596 | RegisterBoss_MoonLord | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 598 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=BossBag{args=[3332]} |
| 599 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=MasterModeCommonDrop{args=[4938]} |
| 600 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=MasterModeDropOnAllPlayers{args=[4810, _masterModeDropRng]} |
| 601 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=ByCondition{args=[condition, 3373, 7]} |
| 602 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=ByCondition{args=[condition, 4469, 10]} |
| 603 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=ByCondition{args=[condition, 3384]} |
| 604 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=ByCondition{args=[condition, 3460, 1, 70, 90]} |
| 605 | RegisterBoss_MoonLord | NPC rule | 398 (MoonLordCore) [local type] | rule=LeadingConditionRule{args=[condition]}; tail=.OnSuccess{args=[FromOptionsWithoutRepeatsDropRule{args=[2, 3063, 3389, 3065, 1553, 3930, 3541, 3570, 3571, 3569, 5480]}]}; |
| 610 | RegisterBoss_LunarTowers | Condition/rule node |  | DropOneByOne.Parameters parameters = DropOneByOne.Parameters |
| 626 | RegisterBoss_LunarTowers | NPC rule | 517 (LunarTowerSolar) | rule=DropBasedOnExpertMode{args=[DropOneByOne{args=[3458, parameters]}, DropOneByOne{args=[3458, parameters2]}]} |
| 627 | RegisterBoss_LunarTowers | NPC rule | 422 (LunarTowerVortex) | rule=DropBasedOnExpertMode{args=[DropOneByOne{args=[3456, parameters]}, DropOneByOne{args=[3456, parameters2]}]} |
| 628 | RegisterBoss_LunarTowers | NPC rule | 507 (LunarTowerNebula) | rule=DropBasedOnExpertMode{args=[DropOneByOne{args=[3457, parameters]}, DropOneByOne{args=[3457, parameters2]}]} |
| 629 | RegisterBoss_LunarTowers | NPC rule | 493 (LunarTowerStardust) | rule=DropBasedOnExpertMode{args=[DropOneByOne{args=[3459, parameters]}, DropOneByOne{args=[3459, parameters2]}]} |
| 634 | RegisterBoss_Betsy | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 636 | RegisterBoss_Betsy | NPC rule | 551 (DD2Betsy) [local type] | rule=BossBag{args=[3860]} |
| 637 | RegisterBoss_Betsy | NPC rule | 551 (DD2Betsy) [local type] | rule=MasterModeCommonDrop{args=[4948]} |
| 638 | RegisterBoss_Betsy | NPC rule | 551 (DD2Betsy) [local type] | rule=MasterModeDropOnAllPlayers{args=[4817, _masterModeDropRng]} |
| 639 | RegisterBoss_Betsy | NPC rule | 551 (DD2Betsy) [local type] | rule=ByCondition{args=[condition, 3863, 7]} |
| 640 | RegisterBoss_Betsy | NPC rule | 551 (DD2Betsy) [local type] | rule=ByCondition{args=[condition, 3883, 4]} |
| 641 | RegisterBoss_Betsy | NPC rule | 551 (DD2Betsy) [local type] | rule=LeadingConditionRule{args=[condition]}; tail=.OnSuccess{args=[OneFromOptionsNotScalingWithLuck{args=[1, 3827, 3859, 3870, 3858]}]}; |
| 646 | RegisterBoss_Golem | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 648 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=BossBag{args=[3329]} |
| 649 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=MasterModeCommonDrop{args=[4935]} |
| 650 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=MasterModeDropOnAllPlayers{args=[4807, _masterModeDropRng]} |
| 651 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=ByCondition{args=[condition, 2110, 7]} |
| 652 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=ByCondition{args=[condition, 1294, 4]} |
| 653 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=ByCondition{args=[condition, 6158, 6]} |
| 655 | RegisterBoss_Golem | Rule-graph edge |  | owner=itemDropRule; outcome=OnSuccess; child=Common{args=[1261, 1, 60, 180]} |
| 656 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=LeadingConditionRule{args=[condition]}; tail=.OnSuccess{args=[OneFromRulesRule{args=[1, itemDropRule, Common{args=[1122]}, Common{args=[899]}, Common{args=[1248]}, Common{args=[1295]}, Common{args=[1296]}, Common{args=[1297]}]}]}; |
| 657 | RegisterBoss_Golem | NPC rule | 245 (Golem) [local type] | rule=ByCondition{args=[condition, 2218, 1, 4, 8]} |
| 662 | RegisterBoss_DukeFishron | Condition/rule node |  | condition=NotExpert; variable=condition; args=[] |
| 664 | RegisterBoss_DukeFishron | NPC rule | 370 (DukeFishron) [local type] | rule=BossBag{args=[3330]} |
| 665 | RegisterBoss_DukeFishron | NPC rule | 370 (DukeFishron) [local type] | rule=MasterModeCommonDrop{args=[4936]} |
| 666 | RegisterBoss_DukeFishron | NPC rule | 370 (DukeFishron) [local type] | rule=MasterModeDropOnAllPlayers{args=[4808, _masterModeDropRng]} |
| 667 | RegisterBoss_DukeFishron | NPC rule | 370 (DukeFishron) [local type] | rule=ByCondition{args=[condition, 2588, 7]} |
| 668 | RegisterBoss_DukeFishron | NPC rule | 370 (DukeFishron) [local type] | rule=ByCondition{args=[condition, 2609, 15]} |
| 669 | RegisterBoss_DukeFishron | NPC rule | 370 (DukeFishron) [local type] | rule=LeadingConditionRule{args=[Cond.NotRemixSeedHardmode{args=[]}]}; tail=.OnSuccess{args=[LeadingConditionRule{args=[condition]}]}.OnSuccess{args=[OneFromOptions{args=[1, 5526, 2624, 2622, 2621, 5478, 3291, 2623]}]}; |
| 670 | RegisterBoss_DukeFishron | NPC rule | 370 (DukeFishron) [local type] | rule=LeadingConditionRule{args=[Cond.RemixSeed{args=[]}]}; tail=.OnSuccess{args=[LeadingConditionRule{args=[condition]}]}.OnSuccess{args=[OneFromOptions{args=[1, 5526, 2624, 2622, 2621, 5478, 3291, 157]}]}; |
| 675 | RegisterWeirdRules | Condition/rule node |  | condition=BeatAnyMechBoss; variable=condition; args=[] |
| 676 | RegisterWeirdRules | Multiple-NPC rule | 86 (Unicorn) | rule=NormalvsExpert{args=[3260, 40, 30]} |
| 677 | RegisterWeirdRules | NPC rule | 75 (Pixie) | rule=ByCondition{args=[condition, 5662, 200]} |
| 678 | RegisterWeirdRules | Multiple-NPC rule | 171 (PigronHallow), 475 (BigMimicHallow), 84 (EnchantedSword), 137 (IlluminantBat), 138 (IlluminantSlime), 527 (DesertGhoulHallow), 120 (ChaosElemental) | rule=NormalvsExpert{args=[5488, 200, 150]} |
| 679 | RegisterWeirdRules | Multiple-NPC rule | 170 (PigronCorruption), 180 (PigronCrimson), 473 (BigMimicCorruption), 474 (BigMimicCrimson), 83 (CursedHammer), 179 (CrimsonAxe), 101 (Clinger), 98 (SeekerHead), 94 (Corruptor), 182 (FloatyGross), 268 (IchorSticker), 525 (DesertGhoulCorruption), 526 (DesertGhoulCrimson), 529 (DesertLamiaDark), 533 (DesertDjinn) | rule=NormalvsExpert{args=[5489, 200, 150]} |
| 684 | RegisterGlobalRules | Global rule | global | rule=MechBossSpawnersDropRule{args=[]} |
| 685 | RegisterGlobalRules | Global rule | global | rule=SlimeBodyItemDropRule{args=[]} |
| 686 | RegisterGlobalRules | Global rule | global | rule=ByCondition{args=[Cond.HalloweenWeapons{args=[]}, 1825, 2000]}; tail=.OnFailedRoll{args=[Common{args=[1827, 2000]}]}; |
| 687 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[1533, 2500, 1, 1, Cond.JungleKeyCondition{args=[]}]} |
| 688 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[1534, 2500, 1, 1, Cond.CorruptKeyCondition{args=[]}]} |
| 689 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[1535, 2500, 1, 1, Cond.CrimsonKeyCondition{args=[]}]} |
| 690 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[1536, 2500, 1, 1, Cond.HallowKeyCondition{args=[]}]} |
| 691 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[1537, 2500, 1, 1, Cond.FrozenKeyCondition{args=[]}]} |
| 692 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[4714, 2500, 1, 1, Cond.DesertKeyCondition{args=[]}]} |
| 693 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[1774, 80, 1, 1, Cond.HalloweenGoodieBagDrop{args=[]}]} |
| 694 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[1869, 13, 1, 1, Cond.XmasPresentDrop{args=[]}]} |
| 695 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[2701, 50, 20, 50, Cond.LivingFlames{args=[]}]} |
| 696 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[520, 5, 1, 1, Cond.SoulOfLight{args=[]}]} |
| 697 | RegisterGlobalRules | Global rule | global | rule=ItemDropWithConditionRule{args=[521, 5, 1, 1, Cond.SoulOfNight{args=[]}]} |
| 698 | RegisterGlobalRules | Global rule | global | rule=ByCondition{args=[Cond.PirateMap{args=[]}, 1315, 100]} |
| 703 | RegisterFoodDrops | NPC rule | 48 (Harpy) | rule=Food{args=[4016, 50]} |
| 704 | RegisterFoodDrops | NPC rule | 224 (FlyingFish) | rule=Food{args=[4021, 50]} |
| 705 | RegisterFoodDrops | NPC rule | 44 (UndeadMiner) | rule=Food{args=[4037, 10]} |
| 706 | RegisterFoodDrops | NPC rule | 469 (ThePossessed) | rule=Food{args=[4037, 100]} |
| 707 | RegisterFoodDrops | Multiple-NPC rule | 163 (BlackRecluse), 238 (BlackRecluseWall), 164 (WallCreeper), 165 (WallCreeperWall), 530 (DesertScorpionWalk), 531 (DesertScorpionWall) | rule=Food{args=[4020, 30]} |
| 708 | RegisterFoodDrops | Multiple-NPC rule | 480 (Medusa), 481 (GreekSkeleton) | rule=Food{args=[4029, 50]} |
| 709 | RegisterFoodDrops | Multiple-NPC rule | 498 (Salamander), 499 (Salamander2), 500 (Salamander3), 501 (Salamander4), 502 (Salamander5), 503 (Salamander6), 504 (Salamander7), 505 (Salamander8), 506 (Salamander9), 496 (GiantShelly), 497 (GiantShelly2), 494 (Crawdad), 495 (Crawdad2) | rule=Food{args=[4030, 75]} |
| 710 | RegisterFoodDrops | Multiple-NPC rule | 482 (GraniteGolem), 483 (GraniteFlyer) | rule=Food{args=[4036, 50]} |
| 711 | RegisterFoodDrops | Multiple-NPC rule | 6 (EaterofSouls), 173 (Crimera) | rule=Food{args=[4015, 100]} |
| 712 | RegisterFoodDrops | Multiple-NPC rule | 150 (IceBat), 147 (IceSlime), 184 (SpikedIceSlime) | rule=Food{args=[4026, 150]} |
| 713 | RegisterFoodDrops | Multiple-NPC rule | 154 (IceTortoise), 206 (IcyMerman) | rule=Food{args=[4027, 75]} |
| 714 | RegisterFoodDrops | Multiple-NPC rule | 170 (PigronCorruption), 180 (PigronCrimson), 171 (PigronHallow) | rule=Food{args=[3532, 15]} |
| 715 | RegisterFoodDrops | NPC rule | 289 (GiantCursedSkull) | rule=Food{args=[4018, 35]} |
| 716 | RegisterFoodDrops | NPC rule | 34 (CursedSkull) | rule=Food{args=[4018, 70]} |
| 717 | RegisterFoodDrops | Multiple-NPC rule | 293 (SkeletonCommando), 291 (SkeletonSniper), 292 (TacticalSkeleton) | rule=Food{args=[4013, 21]} |
| 718 | RegisterFoodDrops | Multiple-NPC rule | 43 (ManEater), 175 (AngryTrapper), 56 (Snatcher) | rule=Food{args=[5042, 30]} |
| 719 | RegisterFoodDrops | NPC rule | 287 (BoneLee) | rule=Food{args=[5042, 10]} |
| 720 | RegisterFoodDrops | Multiple-NPC rule | 21 (Skeleton), 201 (HeadacheSkeleton), 202 (MisassembledSkeleton), 203 (PantlessSkeleton), 322 (SkeletonTopHat), 323 (SkeletonAstonaut), 324 (SkeletonAlien), 635 (SporeSkeleton), 449 (BoneThrowingSkeleton), 450 (BoneThrowingSkeleton2), 451 (BoneThrowingSkeleton3), 452 (BoneThrowingSkeleton4) | rule=Food{args=[5041, 150]} |
| 721 | RegisterFoodDrops | NPC rule | 290 (Paladin) | rule=Food{args=[4013, 7]} |
| 722 | RegisterFoodDrops | Multiple-NPC rule | 39 (BoneSerpentHead), 156 (RedDevil) | rule=Food{args=[4025, 30]} |
| 723 | RegisterFoodDrops | Multiple-NPC rule | 177 (Derpling), 152 (GiantFlyingFox) | rule=Food{args=[4023, 40]} |
| 724 | RegisterFoodDrops | Multiple-NPC rule | 581 (FlyingAntlion), 509 (GiantFlyingAntlion), 580 (WalkingAntlion), 508 (GiantWalkingAntlion), 69 (Antlion) | rule=Food{args=[4012, 50]} |
| 725 | RegisterFoodDrops | Multiple-NPC rule | 546 (Tumbleweed), 542 (SandShark), 544 (SandsharkCrimson), 543 (SandsharkCorrupt), 545 (SandsharkHallow) | rule=Food{args=[4028, 30]} |
| 726 | RegisterFoodDrops | Multiple-NPC rule | 67 (Crab), 65 (Shark), 692 (Orca) | rule=Food{args=[4035, 50]} |
| 727 | RegisterFoodDrops | Multiple-NPC rule | 120 (ChaosElemental), 137 (IlluminantBat), 138 (IlluminantSlime) | rule=Food{args=[4011, 150]} |
| 728 | RegisterFoodDrops | NPC rule | 122 (Gastropod) | rule=Food{args=[4017, 75]} |
| 733 | RegisterTownNPCDrops | NPC rule | 22 (Guide) | rule=ItemDropWithConditionRule{args=[867, 1, 1, 1, Cond.NamedNPC{args=["GuideNames.Andrew"]}]} |
| 734 | RegisterTownNPCDrops | NPC rule | 178 (Steampunker) | rule=ItemDropWithConditionRule{args=[4372, 1, 1, 1, Cond.NamedNPC{args=["SteampunkerNames.Whitney"]}]} |
| 735 | RegisterTownNPCDrops | NPC rule | 227 (Painter) | rule=ItemDropWithConditionRule{args=[5290, 1, 1, 1, Cond.NamedNPC{args=["PainterNames.Jim"]}]} |
| 736 | RegisterTownNPCDrops | NPC rule | 353 (Stylist) | rule=Common{args=[3352, 8]} |
| 737 | RegisterTownNPCDrops | NPC rule | 441 (TaxCollector) | rule=Common{args=[3351, 8]} |
| 738 | RegisterTownNPCDrops | NPC rule | 227 (Painter) | rule=Common{args=[3350, 8]} |
| 739 | RegisterTownNPCDrops | NPC rule | 550 (DD2Bartender) | rule=Common{args=[3821, 8]} |
| 740 | RegisterTownNPCDrops | NPC rule | 208 (PartyGirl) | rule=Common{args=[3548, 4, 30, 60]} |
| 741 | RegisterTownNPCDrops | NPC rule | 207 (DyeTrader) | rule=Common{args=[3349, 8]} |
| 742 | RegisterTownNPCDrops | NPC rule | 124 (Mechanic) | rule=Common{args=[4818, 8]} |
| 743 | RegisterTownNPCDrops | NPC rule | 663 (Princess) | rule=ByCondition{args=[Cond.IsHardmode{args=[]}, 5065, 8]} |
| 744 | RegisterTownNPCDrops | NPC rule | 54 (Clothier) | rule=Common{args=[260]} |
| 745 | RegisterTownNPCDrops | NPC rule | 368 (TravellingMerchant) | rule=Common{args=[2222]} |
| 750 | RegisterDD2EventDrops | Condition/rule node |  | Cond.IsExpert{args=[]}; |
| 751 | RegisterDD2EventDrops | NPC rule | 576 (DD2OgreT2) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3814, 4]}, NotScalingWithLuck{args=[3814, 2]}]} |
| 752 | RegisterDD2EventDrops | NPC rule | 576 (DD2OgreT2) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3815, 4, 4, 4]}, NotScalingWithLuck{args=[3815, 2, 4, 4]}]} |
| 753 | RegisterDD2EventDrops | NPC rule | 576 (DD2OgreT2) | rule=CommonDropNotScalingWithLuck{args=[3865, 7, 1, 1]} |
| 754 | RegisterDD2EventDrops | NPC rule | 576 (DD2OgreT2) | rule=NormalvsExpertOneFromOptionsNotScalingWithLuck{args=[3, 2, 3811, 3812]} |
| 755 | RegisterDD2EventDrops | NPC rule | 576 (DD2OgreT2) | rule=NormalvsExpertOneFromOptionsNotScalingWithLuck{args=[2, 1, 3852, 3854, 3823, 3835, 3836]} |
| 756 | RegisterDD2EventDrops | NPC rule | 576 (DD2OgreT2) | rule=NormalvsExpertNotScalingWithLuck{args=[3856, 5, 4]} |
| 757 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3814, 8]}, NotScalingWithLuck{args=[3814, 4]}]} |
| 758 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3815, 8, 4, 4]}, NotScalingWithLuck{args=[3815, 4, 4, 4]}]} |
| 759 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=CommonDropNotScalingWithLuck{args=[3865, 14, 1, 1]} |
| 760 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=MasterModeCommonDrop{args=[4947]} |
| 761 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=MasterModeDropOnAllPlayers{args=[4816, _masterModeDropRng]} |
| 762 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=OneFromOptionsNotScalingWithLuck{args=[6, 3811, 3812]} |
| 763 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=OneFromOptionsNotScalingWithLuck{args=[4, 3852, 3854, 3823, 3835, 3836]} |
| 764 | RegisterDD2EventDrops | NPC rule | 577 (DD2OgreT3) | rule=Common{args=[3856, 10]} |
| 765 | RegisterDD2EventDrops | NPC rule | 564 (DD2DarkMageT1) | rule=Common{args=[3864, 7]} |
| 766 | RegisterDD2EventDrops | NPC rule | 564 (DD2DarkMageT1) | rule=MasterModeDropOnAllPlayers{args=[4796, _masterModeDropRng]} |
| 767 | RegisterDD2EventDrops | NPC rule | 564 (DD2DarkMageT1) | rule=NormalvsExpertOneFromOptionsNotScalingWithLuck{args=[2, 1, 3810, 3809]} |
| 768 | RegisterDD2EventDrops | NPC rule | 564 (DD2DarkMageT1) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3814, 2]}, NotScalingWithLuck{args=[3814]}]} |
| 769 | RegisterDD2EventDrops | NPC rule | 564 (DD2DarkMageT1) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3815, 2, 4, 4]}, NotScalingWithLuck{args=[3815, 1, 4, 4]}]} |
| 770 | RegisterDD2EventDrops | NPC rule | 564 (DD2DarkMageT1) | rule=NormalvsExpertOneFromOptionsNotScalingWithLuck{args=[3, 2, 3857, 3855]} |
| 771 | RegisterDD2EventDrops | NPC rule | 565 (DD2DarkMageT3) | rule=Common{args=[3864, 14]} |
| 772 | RegisterDD2EventDrops | NPC rule | 565 (DD2DarkMageT3) | rule=MasterModeCommonDrop{args=[4946]} |
| 773 | RegisterDD2EventDrops | NPC rule | 565 (DD2DarkMageT3) | rule=MasterModeDropOnAllPlayers{args=[4796, _masterModeDropRng]} |
| 774 | RegisterDD2EventDrops | NPC rule | 565 (DD2DarkMageT3) | rule=OneFromOptionsNotScalingWithLuck{args=[6, 3810, 3809]} |
| 775 | RegisterDD2EventDrops | NPC rule | 565 (DD2DarkMageT3) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3814, 8]}, NotScalingWithLuck{args=[3814, 4]}]} |
| 776 | RegisterDD2EventDrops | NPC rule | 565 (DD2DarkMageT3) | rule=DropBasedOnExpertMode{args=[NotScalingWithLuck{args=[3815, 8, 4, 4]}, NotScalingWithLuck{args=[3815, 4, 4, 4]}]} |
| 777 | RegisterDD2EventDrops | NPC rule | 565 (DD2DarkMageT3) | rule=OneFromOptionsNotScalingWithLuck{args=[6, 3857, 3855]} |
| 782 | RegisterHardmodeFeathers | NPC rule | 156 (RedDevil) | rule=Common{args=[1518, 50]} |
| 783 | RegisterHardmodeFeathers | NPC rule | 243 (IceGolem) | rule=Common{args=[1519, 3]} |
| 784 | RegisterHardmodeFeathers | Multiple-NPC rule | 269 (RustyArmoredBonesAxe), 270 (RustyArmoredBonesFlail), 271 (RustyArmoredBonesSword), 272 (RustyArmoredBonesSwordNoArmor), 273 (BlueArmoredBones), 274 (BlueArmoredBonesMace), 275 (BlueArmoredBonesNoPants), 276 (BlueArmoredBonesSword), 277 (HellArmoredBones), 278 (HellArmoredBonesSpikeShield), 279 (HellArmoredBonesMace), 280 (HellArmoredBonesSword) | rule=Common{args=[1517, 300]} |
| 785 | RegisterHardmodeFeathers | Multiple-NPC rule | 159 (Vampire), 158 (VampireBat) | rule=Common{args=[1520, 40]} |
| 786 | RegisterHardmodeFeathers | NPC rule | 48 (Harpy) | rule=Common{args=[1516, 150]} |
| 787 | RegisterHardmodeFeathers | NPC rule | 176 (MossHornet) | rule=ItemDropWithConditionRule{args=[1521, 100, 1, 1, Cond.BeatAnyMechBoss{args=[]}]} |
| 788 | RegisterHardmodeFeathers | NPC rule | 205 (Moth) | rule=ItemDropWithConditionRule{args=[1611, 2, 1, 1, Cond.BeatAnyMechBoss{args=[]}]} |
| 793 | RegisterYoyos | Global rule | global | rule=ItemDropWithConditionRule{args=[3282, 400, 1, 1, Cond.YoyoCascade{args=[]}]} |
| 794 | RegisterYoyos | Global rule | global | rule=ItemDropWithConditionRule{args=[3289, 300, 1, 1, Cond.YoyosAmarok{args=[]}]} |
| 795 | RegisterYoyos | Global rule | global | rule=ItemDropWithConditionRule{args=[3286, 200, 1, 1, Cond.YoyosYelets{args=[]}]} |
| 796 | RegisterYoyos | Global rule | global | rule=ItemDropWithConditionRule{args=[3290, 400, 1, 1, Cond.YoyosHelFire{args=[]}]} |
| 801 | RegisterStatusImmunityItems | Multiple-NPC rule | 104 (Werewolf), 102 (AnglerFish), 269 (RustyArmoredBonesAxe), 270 (RustyArmoredBonesFlail), 271 (RustyArmoredBonesSword), 272 (RustyArmoredBonesSwordNoArmor) | rule=StatusImmunityItem{args=[885, 100]} |
| 802 | RegisterStatusImmunityItems | Multiple-NPC rule | 77 (ArmoredSkeleton), 273 (BlueArmoredBones), 274 (BlueArmoredBonesMace), 275 (BlueArmoredBonesNoPants), 276 (BlueArmoredBonesSword) | rule=StatusImmunityItem{args=[886, 100]} |
| 803 | RegisterStatusImmunityItems | Multiple-NPC rule | 141 (ToxicSludge), 176 (MossHornet), 42 (Hornet), 231 (HornetFatty), 232 (HornetHoney), 233 (HornetLeafy), 234 (HornetSpikey), 235 (HornetStingy) | rule=StatusImmunityItem{args=[887, 100]} |
| 804 | RegisterStatusImmunityItems | Multiple-NPC rule | 81 (CorruptSlime), 79 (DarkMummy), 183 (Crimslime), 630 (BloodMummy) | rule=StatusImmunityItem{args=[888, 100]} |
| 805 | RegisterStatusImmunityItems | Multiple-NPC rule | 78 (Mummy), 82 (Wraith), 75 (Pixie) | rule=StatusImmunityItem{args=[889, 100]} |
| 806 | RegisterStatusImmunityItems | Multiple-NPC rule | 103 (GreenJellyfish), 75 (Pixie), 79 (DarkMummy), 630 (BloodMummy) | rule=StatusImmunityItem{args=[890, 100]} |
| 807 | RegisterStatusImmunityItems | Multiple-NPC rule | 34 (CursedSkull), 83 (CursedHammer), 84 (EnchantedSword), 179 (CrimsonAxe), 289 (GiantCursedSkull) | rule=StatusImmunityItem{args=[891, 100]} |
| 808 | RegisterStatusImmunityItems | Multiple-NPC rule | 94 (Corruptor), 182 (FloatyGross) | rule=StatusImmunityItem{args=[892, 100]} |
| 809 | RegisterStatusImmunityItems | Multiple-NPC rule | 93 (GiantBat), 109 (Clown), 80 (LightMummy) | rule=StatusImmunityItem{args=[893, 100]} |
| 815 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[905, 4000]} |
| 816 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[855, 2000]} |
| 817 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[854, 1000]} |
| 818 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2584, 1000]} |
| 819 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[3033, 500]} |
| 820 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[672, 200]} |
| 821 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[5460, 200]} |
| 822 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1277, 500]} |
| 823 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1278, 500]} |
| 824 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1279, 500]} |
| 825 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1280, 500]} |
| 826 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1704, 300]} |
| 827 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1705, 300]} |
| 828 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1710, 300]} |
| 829 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1716, 300]} |
| 830 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1720, 300]} |
| 831 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2379, 300]} |
| 832 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2389, 300]} |
| 833 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2405, 300]} |
| 834 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2843, 300]} |
| 835 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[3885, 300]} |
| 836 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2663, 300]} |
| 837 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[3904, 150, 80, 130]} |
| 838 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[3910, 300]} |
| 839 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2238, 300]} |
| 840 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2133, 300]} |
| 841 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2137, 300]} |
| 842 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2143, 300]} |
| 843 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2147, 300]} |
| 844 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2151, 300]} |
| 845 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2155, 300]} |
| 846 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[3263, 500]} |
| 847 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[3264, 500]} |
| 848 | RegisterPirateDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[3265, 500]} |
| 849 | RegisterPirateDrops | NPC rule | 216 (PirateCaptain) | rule=Common{args=[905, 1000]} |
| 850 | RegisterPirateDrops | NPC rule | 216 (PirateCaptain) | rule=Common{args=[855, 500]} |
| 851 | RegisterPirateDrops | NPC rule | 216 (PirateCaptain) | rule=Common{args=[854, 250]} |
| 852 | RegisterPirateDrops | NPC rule | 216 (PirateCaptain) | rule=Common{args=[2584, 250]} |
| 853 | RegisterPirateDrops | NPC rule | 216 (PirateCaptain) | rule=Common{args=[3033, 125]} |
| 854 | RegisterPirateDrops | NPC rule | 216 (PirateCaptain) | rule=Common{args=[672, 50]} |
| 855 | RegisterPirateDrops | NPC rule | 216 (PirateCaptain) | rule=Common{args=[5460, 50]} |
| 856 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[905, 50]} |
| 857 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[855, 15]} |
| 858 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[854, 15]} |
| 859 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[2584, 15]} |
| 860 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[3033, 15]} |
| 861 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[4471, 20]} |
| 862 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[672, 10]} |
| 863 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=Common{args=[5460, 10]} |
| 864 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=MasterModeCommonDrop{args=[4940]} |
| 865 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=MasterModeDropOnAllPlayers{args=[4792, _masterModeDropRng]} |
| 866 | RegisterPirateDrops | NPC rule | 491 (PirateShip) | rule=OneFromOptions{args=[1, 1704, 1705, 1710, 1716, 1720, 2379, 2389, 2405, 2843, 3885, 2663, 3910, 2238, 2133, 2137, 2143, 2147, 2151, 2155]} |
| 875 | RegisterBossTrophies | Condition/rule node |  | condition=LegacyHack_IsABoss; variable=condition; args=[] |
| 876 | RegisterBossTrophies | NPC rule | 4 (EyeofCthulhu) | rule=ByCondition{args=[condition, 1360, 10]} |
| 877 | RegisterBossTrophies | NPC rule | 13 (EaterofWorldsHead) | rule=ByCondition{args=[condition, 1361, 10]} |
| 878 | RegisterBossTrophies | NPC rule | 14 (EaterofWorldsBody) | rule=ByCondition{args=[condition, 1361, 10]} |
| 879 | RegisterBossTrophies | NPC rule | 15 (EaterofWorldsTail) | rule=ByCondition{args=[condition, 1361, 10]} |
| 880 | RegisterBossTrophies | NPC rule | 266 (BrainofCthulhu) | rule=ByCondition{args=[condition, 1362, 10]} |
| 881 | RegisterBossTrophies | NPC rule | 35 (SkeletronHead) | rule=ByCondition{args=[condition, 1363, 10]} |
| 882 | RegisterBossTrophies | NPC rule | 222 (QueenBee) | rule=ByCondition{args=[condition, 1364, 10]} |
| 883 | RegisterBossTrophies | NPC rule | 113 (WallofFlesh) | rule=ByCondition{args=[condition, 1365, 10]} |
| 884 | RegisterBossTrophies | NPC rule | 134 (TheDestroyer) | rule=ByCondition{args=[condition, 1366, 10]} |
| 885 | RegisterBossTrophies | NPC rule | 127 (SkeletronPrime) | rule=ByCondition{args=[condition, 1367, 10]} |
| 886 | RegisterBossTrophies | NPC rule | 262 (Plantera) | rule=ByCondition{args=[condition, 1370, 10]} |
| 887 | RegisterBossTrophies | NPC rule | 245 (Golem) | rule=ByCondition{args=[condition, 1371, 10]} |
| 888 | RegisterBossTrophies | NPC rule | 50 (KingSlime) | rule=ByCondition{args=[condition, 2489, 10]} |
| 889 | RegisterBossTrophies | NPC rule | 370 (DukeFishron) | rule=ByCondition{args=[condition, 2589, 10]} |
| 890 | RegisterBossTrophies | NPC rule | 439 (CultistBoss) | rule=ByCondition{args=[condition, 3357, 10]} |
| 891 | RegisterBossTrophies | NPC rule | 395 (MartianSaucerCore) | rule=ByCondition{args=[condition, 3358, 10]} |
| 892 | RegisterBossTrophies | NPC rule | 398 (MoonLordCore) | rule=ByCondition{args=[condition, 3595, 10]} |
| 893 | RegisterBossTrophies | NPC rule | 636 (HallowBoss) | rule=ByCondition{args=[condition, 4783, 10]} |
| 894 | RegisterBossTrophies | NPC rule | 657 (QueenSlimeBoss) | rule=ByCondition{args=[condition, 4958, 10]} |
| 895 | RegisterBossTrophies | NPC rule | 668 (Deerclops) | rule=ByCondition{args=[condition, 5108, 10]} |
| 896 | RegisterBossTrophies | NPC rule | 125 (Retinazer) | rule=Common{args=[1368, 10]} |
| 897 | RegisterBossTrophies | NPC rule | 126 (Spazmatism) | rule=Common{args=[1369, 10]} |
| 898 | RegisterBossTrophies | NPC rule | 491 (PirateShip) | rule=Common{args=[3359, 10]} |
| 899 | RegisterBossTrophies | NPC rule | 551 (DD2Betsy) | rule=Common{args=[3866, 10]} |
| 900 | RegisterBossTrophies | NPC rule | 564 (DD2DarkMageT1) | rule=Common{args=[3867, 10]} |
| 901 | RegisterBossTrophies | NPC rule | 565 (DD2DarkMageT3) | rule=Common{args=[3867, 10]} |
| 902 | RegisterBossTrophies | NPC rule | 576 (DD2OgreT2) | rule=Common{args=[3868, 10]} |
| 903 | RegisterBossTrophies | NPC rule | 577 (DD2OgreT3) | rule=Common{args=[3868, 10]} |
| 908 | RegisterMartianDrops | Multiple-NPC rule | 520 (MartianWalker), 383 (MartianOfficer), 389 (GigaZapper), 385 (GrayGrunt), 382 (RayGunner), 381 (BrainScrambler), 390 (ScutlixRider), 386 (MartianEngineer) | rule=Common{args=[2860, 8, 8, 20]} |
| 910 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2798, 800]} |
| 911 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2800, 800]} |
| 912 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[2882, 800]} |
| 914 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds2 | rule=Common{args=[2806, 200]} |
| 915 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds2 | rule=Common{args=[2807, 200]} |
| 916 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds2 | rule=Common{args=[2808, 200]} |
| 918 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds3 | rule=Common{args=[2803, 200]} |
| 919 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds3 | rule=Common{args=[2804, 200]} |
| 920 | RegisterMartianDrops | Multiple-NPC rule | npcNetIds3 | rule=Common{args=[2805, 200]} |
| 921 | RegisterMartianDrops | NPC rule | 395 (MartianSaucerCore) | rule=OneFromOptionsNotScalingWithLuck{args=[1, 2797, 2749, 2795, 2796, 2880, 2769]} |
| 922 | RegisterMartianDrops | NPC rule | 395 (MartianSaucerCore) | rule=MasterModeCommonDrop{args=[4939]} |
| 923 | RegisterMartianDrops | NPC rule | 395 (MartianSaucerCore) | rule=MasterModeDropOnAllPlayers{args=[4815, _masterModeDropRng]} |
| 924 | RegisterMartianDrops | NPC rule | 390 (ScutlixRider) | rule=Common{args=[2771, 30]} |
| 925 | RegisterMartianDrops | NPC rule | 395 (MartianSaucerCore) | rule=Common{args=[6173, 50]} |
| 930 | RegisterMiscDrops | NPC rule | 68 (DungeonGuardian) | rule=Common{args=[1169]} |
| 931 | RegisterMiscDrops | Multiple-NPC rule | 483 (GraniteFlyer), 482 (GraniteGolem) | rule=Common{args=[3086, 1, 5, 10]} |
| 932 | RegisterMiscDrops | Multiple-NPC rule | 483 (GraniteFlyer), 482 (GraniteGolem) | rule=Common{args=[6167, 80]} |
| 933 | RegisterMiscDrops | NPC rule | 77 (ArmoredSkeleton) | rule=Common{args=[723, 150]} |
| 934 | RegisterMiscDrops | Multiple-NPC rule | 195 (LostGirl), 196 (Nymph) | rule=NormalvsExpert{args=[3102, 2, 1]} |
| 935 | RegisterMiscDrops | NPC rule | 471 (GoblinSummoner) | rule=NormalvsExpertOneFromOptions{args=[2, 1, 3052, 3053, 3054]} |
| 936 | RegisterMiscDrops | NPC rule | 153 (GiantTortoise) | rule=Common{args=[1328, 12]} |
| 937 | RegisterMiscDrops | NPC rule | 59 (LavaSlime) | rule=LeadingConditionRule{args=[Cond.RemixSeed{args=[]}]}; tail=.OnSuccess{args=[Gel{args=[1, 1, 2]}]}; |
| 938 | RegisterMiscDrops | NPC rule | 59 (LavaSlime) | rule=LeadingConditionRule{args=[Cond.RemixSeed{args=[]}]}; tail=.OnSuccess{args=[NormalvsExpert{args=[1309, 8000, 5600]}]}; |
| 939 | RegisterMiscDrops | NPC rule | 120 (ChaosElemental) | rule=LeadingConditionRule{args=[Cond.TenthAnniversaryIsUp{args=[]}]}; tail=.OnSuccess{args=[Common{args=[1326, 100]}]}; |
| 940 | RegisterMiscDrops | NPC rule | 120 (ChaosElemental) | rule=LeadingConditionRule{args=[Cond.TenthAnniversaryIsNotUp{args=[]}]}; tail=.OnSuccess{args=[NormalvsExpert{args=[1326, 500, 400]}]}; |
| 941 | RegisterMiscDrops | NPC rule | 49 (CaveBat) | rule=LeadingConditionRule{args=[Cond.NotRemixSeed{args=[]}]}; tail=.OnSuccess{args=[Common{args=[1325, 250]}]}; |
| 942 | RegisterMiscDrops | NPC rule | 49 (CaveBat) | rule=LeadingConditionRule{args=[Cond.RemixSeed{args=[]}]}; tail=.OnSuccess{args=[Common{args=[1314, 250]}]}; |
| 943 | RegisterMiscDrops | NPC rule | 109 (Clown) | rule=LeadingConditionRule{args=[Cond.NotRemixSeedHardmode{args=[]}]}; tail=.OnSuccess{args=[Common{args=[1314, 5]}]}; |
| 944 | RegisterMiscDrops | NPC rule | 109 (Clown) | rule=LeadingConditionRule{args=[Cond.RemixSeed{args=[]}]}; tail=.OnSuccess{args=[Common{args=[1325, 5]}]}; |
| 945 | RegisterMiscDrops | NPC rule | 156 (RedDevil) | rule=LeadingConditionRule{args=[Cond.NotRemixSeedHardmode{args=[]}]}; tail=.OnSuccess{args=[Common{args=[683, 30]}]}; |
| 946 | RegisterMiscDrops | NPC rule | 156 (RedDevil) | rule=LeadingConditionRule{args=[Cond.RemixSeed{args=[]}]}; tail=.OnSuccess{args=[Common{args=[112, 30]}]}; |
| 947 | RegisterMiscDrops | NPC rule | 634 (SporeBat) | rule=Common{args=[4764, 40]} |
| 948 | RegisterMiscDrops | NPC rule | 185 (SnowFlinx) | rule=Common{args=[951, 25]} |
| 949 | RegisterMiscDrops | NPC rule | 185 (SnowFlinx) | rule=DropBasedOnExpertMode{args=[Common{args=[5070, 1, 1, 2]}, CommonDrop{args=[5070, 1, 1, 3]}]} |
| 950 | RegisterMiscDrops | NPC rule | 44 (UndeadMiner) | rule=Common{args=[1320, 20]} |
| 951 | RegisterMiscDrops | NPC rule | 44 (UndeadMiner) | rule=Common{args=[88, 20]} |
| 952 | RegisterMiscDrops | NPC rule | 60 (Hellbat) | rule=Common{args=[1322, 150]} |
| 953 | RegisterMiscDrops | NPC rule | 151 (Lavabat) | rule=Common{args=[1322, 50]} |
| 954 | RegisterMiscDrops | NPC rule | 24 (FireImp) | rule=Common{args=[1323, 20]} |
| 955 | RegisterMiscDrops | NPC rule | 109 (Clown) | rule=Common{args=[1324, 10]} |
| 956 | RegisterMiscDrops | NPC rule | 109 (Clown) | rule=Common{args=[4271, 10]} |
| 958 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds | rule=Common{args=[1308, 40]} |
| 959 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds | rule=DropBasedOnExpertMode{args=[Common{args=[2607, 2, 1, 3]}, CommonDrop{args=[2607, 10, 1, 3, 9]}]} |
| 960 | RegisterMiscDrops | Multiple-NPC rule | 197 (ArmoredViking), 206 (IcyMerman), 169 (IceElemental), 154 (IceTortoise) | rule=Common{args=[1306, 100]} |
| 961 | RegisterMiscDrops | NPC rule | 301 (Raven) | rule=Common{args=[5576, 10]} |
| 962 | RegisterMiscDrops | NPC rule | 244 (RainbowSlime) | rule=Gel{args=[1, 1, 20]} |
| 963 | RegisterMiscDrops | NPC rule | 244 (RainbowSlime) | rule=Common{args=[662, 1, 30, 60]} |
| 964 | RegisterMiscDrops | NPC rule | 250 (AngryNimbus) | rule=Common{args=[1244, 15]} |
| 965 | RegisterMiscDrops | NPC rule | 172 (RuneWizard) | rule=Common{args=[754]} |
| 966 | RegisterMiscDrops | NPC rule | 172 (RuneWizard) | rule=Common{args=[755]} |
| 967 | RegisterMiscDrops | NPC rule | 110 (SkeletonArcher) | rule=Common{args=[682, 200]} |
| 968 | RegisterMiscDrops | NPC rule | 110 (SkeletonArcher) | rule=Common{args=[1321, 40]} |
| 969 | RegisterMiscDrops | Multiple-NPC rule | 170 (PigronCorruption), 180 (PigronCrimson), 171 (PigronHallow) | rule=Common{args=[4428, 100]} |
| 970 | RegisterMiscDrops | Multiple-NPC rule | 170 (PigronCorruption), 180 (PigronCrimson), 171 (PigronHallow) | rule=ItemDropWithConditionRule{args=[4613, 25, 1, 1, Cond.WindyEnoughForKiteDrops{args=[]}]} |
| 971 | RegisterMiscDrops | Multiple-NPC rule | 170 (PigronCorruption), 180 (PigronCrimson), 171 (PigronHallow) | rule=ItemDropWithConditionRule{args=[5096, 10, 1, 1, Cond.DontStarveIsUp{args=[]}]} |
| 972 | RegisterMiscDrops | Multiple-NPC rule | 170 (PigronCorruption), 180 (PigronCrimson), 171 (PigronHallow) | rule=ItemDropWithConditionRule{args=[5096, 25, 1, 1, Cond.DontStarveIsNotUp{args=[]}]} |
| 973 | RegisterMiscDrops | NPC rule | 154 (IceTortoise) | rule=Common{args=[1253, 50]} |
| 974 | RegisterMiscDrops | Multiple-NPC rule | 169 (IceElemental), 206 (IcyMerman) | rule=Common{args=[726, 50]} |
| 975 | RegisterMiscDrops | NPC rule | 243 (IceGolem) | rule=Common{args=[2161]} |
| 976 | RegisterMiscDrops | NPC rule | 155 (Wolf) | rule=NormalvsExpert{args=[5130, 30, 25]} |
| 977 | RegisterMiscDrops | NPC rule | 480 (Medusa) | rule=Common{args=[3269, 25]} |
| 978 | RegisterMiscDrops | NPC rule | 480 (Medusa) | rule=NormalvsExpert{args=[3781, 40, 20]} |
| 980 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds2 | rule=Common{args=[1172, 1000]} |
| 981 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds2 | rule=Common{args=[1293, 50]} |
| 982 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds2 | rule=Common{args=[2766, 7, 1, 2]} |
| 984 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds3 | rule=Common{args=[870, 75]} |
| 985 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds3 | rule=Common{args=[871, 75]} |
| 986 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds3 | rule=Common{args=[872, 75]} |
| 987 | RegisterMiscDrops | NPC rule | 473 (BigMimicCorruption) | rule=OneFromOptions{args=[1, 3008, 3014, 3012, 3015, 3023]} |
| 988 | RegisterMiscDrops | NPC rule | 474 (BigMimicCrimson) | rule=OneFromOptions{args=[1, 3006, 3007, 3013, 3016, 3020]} |
| 989 | RegisterMiscDrops | NPC rule | 475 (BigMimicHallow) | rule=OneFromOptions{args=[1, 3029, 3030, 3051, 6168, 3022]} |
| 990 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[52, 3]} |
| 991 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[1724, 3]} |
| 992 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[2353, 3, 5, 10]} |
| 993 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[1922, 3]} |
| 994 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[678, 3, 3, 5]} |
| 995 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[1336, 3]} |
| 996 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[2676, 3, 2, 4]} |
| 997 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[2272, 3]} |
| 998 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[5395, 3]} |
| 999 | RegisterMiscDrops | NPC rule | 476 (BigMimicJungle) | rule=Common{args=[4986, 3, 69, 69]} |
| 1001 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds4 | rule=Common{args=[499, 1, 5, 10]} |
| 1002 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds4 | rule=Common{args=[500, 1, 5, 15]} |
| 1003 | RegisterMiscDrops | NPC rule | 87 (WyvernHead) | rule=ItemDropWithConditionRule{args=[4379, 25, 1, 1, Cond.WindyEnoughForKiteDrops{args=[]}]} |
| 1004 | RegisterMiscDrops | NPC rule | 87 (WyvernHead) | rule=DropBasedOnExpertMode{args=[Common{args=[575, 1, 5, 10]}, Common{args=[575, 1, 10, 20]}]} |
| 1005 | RegisterMiscDrops | Multiple-NPC rule | 161 (ZombieEskimo), 431 (ArmedZombieEskimo) | rule=OneFromOptions{args=[10, 803, 804, 805]} |
| 1006 | RegisterMiscDrops | NPC rule | 217 (CochinealBeetle) | rule=Common{args=[1115]} |
| 1007 | RegisterMiscDrops | NPC rule | 218 (CyanBeetle) | rule=Common{args=[1116]} |
| 1008 | RegisterMiscDrops | NPC rule | 219 (LacBeetle) | rule=Common{args=[1117]} |
| 1009 | RegisterMiscDrops | NPC rule | 220 (SeaSnail) | rule=Common{args=[1118]} |
| 1010 | RegisterMiscDrops | NPC rule | 221 (Squid) | rule=Common{args=[1119]} |
| 1011 | RegisterMiscDrops | NPC rule | 167 (UndeadViking) | rule=Common{args=[879, 50]} |
| 1012 | RegisterMiscDrops | NPC rule | 628 (Dandelion) | rule=Common{args=[313, 2, 1, 2]} |
| 1014 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds5 | rule=Common{args=[593, 1, 5, 10]} |
| 1015 | RegisterMiscDrops | Multiple-NPC rule | 79 (DarkMummy), 630 (BloodMummy) | rule=Common{args=[527, 10]} |
| 1016 | RegisterMiscDrops | NPC rule | 80 (LightMummy) | rule=Common{args=[528, 10]} |
| 1017 | RegisterMiscDrops | NPC rule | 524 (DesertGhoul) | rule=Common{args=[3794, 10, 1, 3]} |
| 1018 | RegisterMiscDrops | NPC rule | 525 (DesertGhoulCorruption) | rule=Common{args=[3794, 10]} |
| 1019 | RegisterMiscDrops | NPC rule | 525 (DesertGhoulCorruption) | rule=Common{args=[522, 3, 1, 3]} |
| 1020 | RegisterMiscDrops | NPC rule | 525 (DesertGhoulCorruption) | rule=Common{args=[527, 15]} |
| 1021 | RegisterMiscDrops | NPC rule | 526 (DesertGhoulCrimson) | rule=Common{args=[3794, 10]} |
| 1022 | RegisterMiscDrops | NPC rule | 526 (DesertGhoulCrimson) | rule=Common{args=[1332, 3, 1, 3]} |
| 1023 | RegisterMiscDrops | NPC rule | 526 (DesertGhoulCrimson) | rule=Common{args=[527, 15]} |
| 1024 | RegisterMiscDrops | NPC rule | 527 (DesertGhoulHallow) | rule=Common{args=[3794, 10]} |
| 1025 | RegisterMiscDrops | NPC rule | 527 (DesertGhoulHallow) | rule=Common{args=[528, 15]} |
| 1026 | RegisterMiscDrops | NPC rule | 513 (TombCrawlerHead) | rule=Common{args=[3380, 2, 1, 2]} |
| 1027 | RegisterMiscDrops | NPC rule | 532 (DesertBeast) | rule=Common{args=[3380, 1, 1, 3]} |
| 1028 | RegisterMiscDrops | NPC rule | 532 (DesertBeast) | rule=Common{args=[3771, 50]} |
| 1029 | RegisterMiscDrops | NPC rule | 528 (DesertLamiaLight) | rule=Common{args=[2802, 25]} |
| 1030 | RegisterMiscDrops | NPC rule | 529 (DesertLamiaDark) | rule=Common{args=[2801, 25]} |
| 1031 | RegisterMiscDrops | Multiple-NPC rule | 528 (DesertLamiaLight), 529 (DesertLamiaDark) | rule=OneFromOptions{args=[40, 3786, 3785, 3784]} |
| 1032 | RegisterMiscDrops | Multiple-NPC rule | 528 (DesertLamiaLight), 529 (DesertLamiaDark) | rule=Common{args=[6157, 40]} |
| 1033 | RegisterMiscDrops | Multiple-NPC rule | 49 (CaveBat), 51 (JungleBat), 150 (IceBat), 93 (GiantBat), 634 (SporeBat) | rule=Common{args=[18, 100]} |
| 1034 | RegisterMiscDrops | Multiple-NPC rule | 49 (CaveBat), 51 (JungleBat), 150 (IceBat), 93 (GiantBat), 634 (SporeBat), 151 (Lavabat), 60 (Hellbat), 137 (IlluminantBat), 152 (GiantFlyingFox) | rule=ItemDropWithConditionRule{args=[5097, 300, 1, 1, Cond.DontStarveIsNotUp{args=[]}]} |
| 1035 | RegisterMiscDrops | Multiple-NPC rule | 49 (CaveBat), 51 (JungleBat), 150 (IceBat), 93 (GiantBat), 634 (SporeBat), 151 (Lavabat), 60 (Hellbat), 137 (IlluminantBat), 152 (GiantFlyingFox) | rule=ItemDropWithConditionRule{args=[5097, 100, 1, 1, Cond.DontStarveIsUp{args=[]}]} |
| 1036 | RegisterMiscDrops | Multiple-NPC rule | 16 (MotherSlime), 185 (SnowFlinx), 167 (UndeadViking), 197 (ArmoredViking) | rule=Common{args=[393, 50]} |
| 1037 | RegisterMiscDrops | NPC rule | 58 (Piranha) | rule=Common{args=[393, 75]} |
| 1043 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds6 | rule=Common{args=[18, 80]} |
| 1049 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds7 | rule=Common{args=[954, 100]} |
| 1050 | RegisterMiscDrops | NPC rule | 6 (EaterofSouls) | rule=OneFromOptions{args=[175, 956, 957, 958]} |
| 1052 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds8 | rule=OneFromOptions{args=[100, 960, 961, 962]} |
| 1054 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds9 | rule=Common{args=[959, 450]} |
| 1055 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds9 | rule=Common{args=[1307, 300]} |
| 1056 | RegisterMiscDrops | NPC rule | 32 (DarkCaster) | rule=Common{args=[5632, 150]} |
| 1057 | RegisterMiscDrops | Multiple-NPC rule | 174 (Herpling), 179 (CrimsonAxe), 182 (FloatyGross), 183 (Crimslime), 98 (SeekerHead), 83 (CursedHammer), 94 (Corruptor), 81 (CorruptSlime), 101 (Clinger) | rule=Common{args=[996, 200]} |
| 1058 | RegisterMiscDrops | Multiple-NPC rule | 179 (CrimsonAxe), 83 (CursedHammer) | rule=Common{args=[6159, 25]} |
| 1059 | RegisterMiscDrops | Multiple-NPC rule | 101 (Clinger), 98 (SeekerHead) | rule=Common{args=[522, 1, 2, 5]} |
| 1060 | RegisterMiscDrops | NPC rule | 98 (SeekerHead) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4611, 25]} |
| 1061 | RegisterMiscDrops | NPC rule | 86 (Unicorn) | rule=Common{args=[526]} |
| 1062 | RegisterMiscDrops | NPC rule | 86 (Unicorn) | rule=Common{args=[856, 100]} |
| 1063 | RegisterMiscDrops | NPC rule | 86 (Unicorn) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4684, 25]} |
| 1064 | RegisterMiscDrops | NPC rule | 224 (FlyingFish) | rule=Common{args=[4057, 100]} |
| 1065 | RegisterMiscDrops | Multiple-NPC rule | 186 (PincushionZombie), 432 (ArmedZombiePincussion) | rule=Common{args=[40, 1, 1, 9]} |
| 1066 | RegisterMiscDrops | NPC rule | 225 (UmbrellaSlime) | rule=Common{args=[1243, 45]}; tail=.OnFailedRoll{args=[Gel{args=[1, 2, 6]}]}; |
| 1067 | RegisterMiscDrops | NPC rule | 537 (SandSlime) | rule=Gel{args=[1, 2, 3]} |
| 1068 | RegisterMiscDrops | NPC rule | 537 (SandSlime) | rule=NormalvsExpert{args=[1309, 8000, 5600]} |
| 1070 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds10 | rule=Common{args=[1906, 20]} |
| 1071 | RegisterMiscDrops | NPC rule | -4 (Pinky) | rule=Common{args=[3111, 1, 25, 50]} |
| 1072 | RegisterMiscDrops | NPC rule | -4 (Pinky) | rule=NormalvsExpert{args=[1309, 100, 70]} |
| 1080 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds11 | rule=Gel{args=[1, 1, 2]} |
| 1082 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds12 | rule=Gel{args=[1, 2, 5]} |
| 1083 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds11 | rule=NormalvsExpert{args=[1309, 10000, 7000]} |
| 1085 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds12 | rule=NormalvsExpert{args=[1309, 10000, 7000]} |
| 1086 | RegisterMiscDrops | NPC rule | 75 (Pixie) | rule=Common{args=[501, 1, 1, 3]} |
| 1087 | RegisterMiscDrops | Multiple-NPC rule | 81 (CorruptSlime), 183 (Crimslime) | rule=Gel{args=[1, 2, 4]} |
| 1088 | RegisterMiscDrops | NPC rule | 122 (Gastropod) | rule=Gel{args=[1, 5, 10]} |
| 1089 | RegisterMiscDrops | NPC rule | 71 (DungeonSlime) | rule=Common{args=[327]} |
| 1091 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds14 | rule=Common{args=[236, 100]} |
| 1092 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds14 | rule=ItemDropWithConditionRule{args=[43, 50, 1, 1, Cond.EyeOfCthulhuDefeatedAndNoAltarsInWorld{args=[]}]} |
| 1093 | RegisterMiscDrops | NPC rule | 133 (WanderingEye) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4683, 25]} |
| 1094 | RegisterMiscDrops | NPC rule | 104 (Werewolf) | rule=Common{args=[485, 60]} |
| 1095 | RegisterMiscDrops | NPC rule | 58 (Piranha) | rule=Common{args=[263, 250]}; tail=.OnFailedRoll{args=[Common{args=[118, 30]}]}; |
| 1096 | RegisterMiscDrops | NPC rule | 102 (AnglerFish) | rule=Common{args=[263, 250]} |
| 1103 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds15 | rule=Common{args=[216, 50]} |
| 1104 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds15 | rule=Common{args=[1304, 250]} |
| 1105 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds15 | rule=Common{args=[5332, 1500]} |
| 1106 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds15 | rule=ItemDropWithConditionRule{args=[1786, 15, 1, 1, Cond.SkyblockIsUpNoSickle{args=[]}]} |
| 1107 | RegisterMiscDrops | Multiple-NPC rule | 590 (TorchZombie), 591 (ArmedTorchZombie) | rule=Common{args=[8, 1, 5, 20]} |
| 1109 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds16 | rule=ItemDropWithConditionRule{args=[9, 2, 5, 20, Cond.SkyblockIsUp{args=[]}]} |
| 1110 | RegisterMiscDrops | Multiple-NPC rule | 489 (BloodZombie), 490 (Drippler) | rule=NormalvsExpert{args=[3212, 150, 75]} |
| 1111 | RegisterMiscDrops | Multiple-NPC rule | 489 (BloodZombie), 490 (Drippler) | rule=NormalvsExpert{args=[3213, 200, 100]} |
| 1112 | RegisterMiscDrops | NPC rule | 223 (ZombieRaincoat) | rule=OneFromOptions{args=[20, 1135, 1136]} |
| 1113 | RegisterMiscDrops | NPC rule | 66 (VoodooDemon) | rule=Common{args=[267]} |
| 1114 | RegisterMiscDrops | Multiple-NPC rule | 62 (Demon), 66 (VoodooDemon) | rule=Common{args=[272, 35]} |
| 1115 | RegisterMiscDrops | NPC rule | 52 (DoctorBones) | rule=Common{args=[251]} |
| 1116 | RegisterMiscDrops | NPC rule | 53 (TheGroom) | rule=Common{args=[239]} |
| 1117 | RegisterMiscDrops | NPC rule | 536 (TheBride) | rule=Common{args=[3478]} |
| 1118 | RegisterMiscDrops | NPC rule | 536 (TheBride) | rule=Common{args=[3479]} |
| 1119 | RegisterMiscDrops | Multiple-NPC rule | 69 (Antlion), 581 (FlyingAntlion), 580 (WalkingAntlion), 508 (GiantWalkingAntlion), 509 (GiantFlyingAntlion) | rule=Common{args=[323, 3, 1, 2]} |
| 1120 | RegisterMiscDrops | NPC rule | 582 (LarvaeAntlion) | rule=Common{args=[323, 6]} |
| 1121 | RegisterMiscDrops | Multiple-NPC rule | 581 (FlyingAntlion), 580 (WalkingAntlion), 508 (GiantWalkingAntlion), 509 (GiantFlyingAntlion) | rule=Common{args=[3772, 50]} |
| 1122 | RegisterMiscDrops | NPC rule | 73 (GoblinScout) | rule=Common{args=[362, 1, 1, 2]} |
| 1124 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds17 | rule=Common{args=[3109, 30]} |
| 1125 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds17 | rule=Common{args=[4400, 20]} |
| 1126 | RegisterMiscDrops | Multiple-NPC rule | 6 (EaterofSouls), 94 (Corruptor) | rule=Common{args=[68, 3]} |
| 1127 | RegisterMiscDrops | Multiple-NPC rule | 181 (FaceMonster), 173 (Crimera), 239 (BloodCrawler), 182 (FloatyGross), 240 (BloodCrawlerWall) | rule=Common{args=[1330, 3]} |
| 1128 | RegisterMiscDrops | Multiple-NPC rule | 7 (DevourerHead), 8 (DevourerBody), 9 (DevourerTail) | rule=Common{args=[68, 3, 1, 2]} |
| 1129 | RegisterMiscDrops | Multiple-NPC rule | 7 (DevourerHead), 8 (DevourerBody), 9 (DevourerTail) | rule=Common{args=[69, 1, 3, 8]} |
| 1130 | RegisterMiscDrops | Multiple-NPC rule | 6 (EaterofSouls), 7 (DevourerHead), 8 (DevourerBody), 9 (DevourerTail), 173 (Crimera), 181 (FaceMonster), 239 (BloodCrawler), 240 (BloodCrawlerWall) | rule=ItemDropWithConditionRule{args=[5094, 100, 1, 1, Cond.DontStarveIsUp{args=[]}]} |
| 1131 | RegisterMiscDrops | Multiple-NPC rule | 6 (EaterofSouls), 7 (DevourerHead), 8 (DevourerBody), 9 (DevourerTail), 173 (Crimera), 181 (FaceMonster), 239 (BloodCrawler), 240 (BloodCrawlerWall) | rule=ItemDropWithConditionRule{args=[5094, 525, 1, 1, Cond.DontStarveIsNotUp{args=[]}]} |
| 1132 | RegisterMiscDrops | Multiple-NPC rule | 6 (EaterofSouls), 7 (DevourerHead), 8 (DevourerBody), 9 (DevourerTail), 94 (Corruptor), 81 (CorruptSlime), 101 (Clinger), 173 (Crimera), 181 (FaceMonster), 239 (BloodCrawler), 240 (BloodCrawlerWall), 174 (Herpling), 183 (Crimslime), 242 (BloodJelly), 241 (BloodFeeder), 268 (IchorSticker), 182 (FloatyGross), 98 (SeekerHead), 99 (SeekerBody), 100 (SeekerTail) | rule=ItemDropWithConditionRule{args=[5091, 500, 1, 1, Cond.DontStarveIsUp{args=[]}]} |
| 1133 | RegisterMiscDrops | Multiple-NPC rule | 6 (EaterofSouls), 7 (DevourerHead), 8 (DevourerBody), 9 (DevourerTail), 94 (Corruptor), 81 (CorruptSlime), 101 (Clinger), 173 (Crimera), 181 (FaceMonster), 239 (BloodCrawler), 240 (BloodCrawlerWall), 174 (Herpling), 183 (Crimslime), 242 (BloodJelly), 241 (BloodFeeder), 268 (IchorSticker), 182 (FloatyGross), 98 (SeekerHead), 99 (SeekerBody), 100 (SeekerTail) | rule=ItemDropWithConditionRule{args=[5091, 1500, 1, 1, Cond.DontStarveIsNotUp{args=[]}]} |
| 1134 | RegisterMiscDrops | NPC rule | 690 (StatueMimic) | rule=StatueMimicItemDropRule{args=[]} |
| 1135 | RegisterMiscDrops | Multiple-NPC rule | 10 (GiantWormHead), 11 (GiantWormBody), 12 (GiantWormTail), 95 (DiggerHead), 96 (DiggerBody), 97 (DiggerTail) | rule=DropBasedOnExpertMode{args=[Common{args=[215, 50]}, WithRerolls{args=[215, 1, 50]}]} |
| 1136 | RegisterMiscDrops | Multiple-NPC rule | 47 (CorruptBunny), 464 (CrimsonBunny) | rule=Common{args=[243, 75]} |
| 1137 | RegisterMiscDrops | Multiple-NPC rule | 168 (CorruptPenguin), 470 (CrimsonPenguin) | rule=OneFromOptions{args=[50, 3757, 3758, 3759]} |
| 1138 | RegisterMiscDrops | NPC rule | 533 (DesertDjinn) | rule=Common{args=[3795, 40]}; tail=.OnFailedRoll{args=[Common{args=[3770, 30]}]}; |
| 1140 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds18 | rule=Common{args=[1303, 100]} |
| 1141 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds18 | rule=Common{args=[282, 1, 1, 4]} |
| 1142 | RegisterMiscDrops | Multiple-NPC rule | 223 (ZombieRaincoat) | rule=Common{args=[282, 1, 1, 4]} |
| 1143 | RegisterMiscDrops | Multiple-NPC rule | 224 (FlyingFish) | rule=Common{args=[282, 1, 1, 4]} |
| 1144 | RegisterMiscDrops | NPC rule | 63 (BlueJellyfish) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4649, 50]} |
| 1145 | RegisterMiscDrops | NPC rule | 64 (PinkJellyfish) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4650, 50]} |
| 1146 | RegisterMiscDrops | NPC rule | 691 (MossZombie) | rule=OneFromOptions{args=[1, 4352, 4350, 4349, 4353, 4351, 4354, 5127, 4378, 4377, 4389]} |
| 1147 | RegisterMiscDrops | NPC rule | 481 (GreekSkeleton) | rule=Common{args=[3094, 2, 40, 80]} |
| 1148 | RegisterMiscDrops | NPC rule | 481 (GreekSkeleton) | rule=OneFromOptions{args=[7, 3187, 3188, 3189]} |
| 1149 | RegisterMiscDrops | NPC rule | 481 (GreekSkeleton) | rule=Common{args=[4463, 20]} |
| 1150 | RegisterMiscDrops | NPC rule | 481 (GreekSkeleton) | rule=Common{args=[5543, 100]} |
| 1156 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds19 | rule=Common{args=[118, 25]} |
| 1157 | RegisterMiscDrops | NPC rule | 44 (UndeadMiner) | rule=Common{args=[118, 25]}; tail=.OnFailedRoll{args=[OneFromOptions{args=[4, 410, 411]}]}.OnFailedRoll{args=[Common{args=[166, 1, 1, 3]}]}; |
| 1158 | RegisterMiscDrops | NPC rule | 45 (Tim) | rule=Common{args=[238]} |
| 1159 | RegisterMiscDrops | NPC rule | 23 (MeteorHead) | rule=Common{args=[116, 50]} |
| 1160 | RegisterMiscDrops | NPC rule | 23 (MeteorHead) | rule=Common{args=[5486, 100]} |
| 1161 | RegisterMiscDrops | NPC rule | 24 (FireImp) | rule=Common{args=[244, 250]} |
| 1163 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds20 | rule=Common{args=[932, 250]} |
| 1164 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds20 | rule=ByCondition{args=[Cond.IsExpert{args=[]}, 154, 1, 2, 6]} |
| 1165 | RegisterMiscDrops | NPC rule | 694 (WaterBoltMimic) | rule=Common{args=[165, 40]} |
| 1167 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds21 | rule=Common{args=[160, 200]} |
| 1168 | RegisterMiscDrops | NPC rule | 175 (AngryTrapper) | rule=Common{args=[1265, 100]} |
| 1169 | RegisterMiscDrops | NPC rule | 175 (AngryTrapper) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4675, 25]} |
| 1170 | RegisterMiscDrops | Multiple-NPC rule | 42 (Hornet), 231 (HornetFatty), 232 (HornetHoney), 233 (HornetLeafy), 234 (HornetSpikey), 235 (HornetStingy) | rule=DropBasedOnExpertMode{args=[CommonDrop{args=[209, 3, 1, 1, 2]}, Common{args=[209]}]} |
| 1171 | RegisterMiscDrops | NPC rule | 176 (MossHornet) | rule=Common{args=[209, 6]} |
| 1172 | RegisterMiscDrops | NPC rule | 177 (Derpling) | rule=ItemDropWithConditionRule{args=[5089, 100, 1, 1, Cond.DontStarveIsNotUp{args=[]}]} |
| 1173 | RegisterMiscDrops | NPC rule | 177 (Derpling) | rule=ItemDropWithConditionRule{args=[5089, 40, 1, 1, Cond.DontStarveIsUp{args=[]}]} |
| 1174 | RegisterMiscDrops | NPC rule | 204 (SpikedJungleSlime) | rule=NormalvsExpert{args=[209, 2, 1]} |
| 1175 | RegisterMiscDrops | NPC rule | 43 (ManEater) | rule=NormalvsExpert{args=[210, 2, 1]} |
| 1176 | RegisterMiscDrops | NPC rule | 43 (ManEater) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4648, 25]} |
| 1177 | RegisterMiscDrops | NPC rule | 39 (BoneSerpentHead) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4610, 15]} |
| 1178 | RegisterMiscDrops | NPC rule | 65 (Shark) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4651, 25]} |
| 1179 | RegisterMiscDrops | NPC rule | 65 (Shark) | rule=Common{args=[268, 20]}; tail=.OnFailedRoll{args=[Common{args=[319]}]}; |
| 1180 | RegisterMiscDrops | NPC rule | 692 (Orca) | rule=Common{args=[268, 20]}; tail=.OnFailedRoll{args=[Common{args=[319]}]}; |
| 1181 | RegisterMiscDrops | NPC rule | 48 (Harpy) | rule=NotScalingWithLuck{args=[320, 2]} |
| 1182 | RegisterMiscDrops | NPC rule | 541 (SandElemental) | rule=Common{args=[3783]} |
| 1183 | RegisterMiscDrops | Multiple-NPC rule | 542 (SandShark), 543 (SandsharkCorrupt), 544 (SandsharkCrimson), 545 (SandsharkHallow) | rule=Common{args=[319, 8]} |
| 1184 | RegisterMiscDrops | Multiple-NPC rule | 542 (SandShark), 543 (SandsharkCorrupt), 544 (SandsharkCrimson), 545 (SandsharkHallow) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4669, 25]} |
| 1185 | RegisterMiscDrops | NPC rule | 543 (SandsharkCorrupt) | rule=Common{args=[527, 25]} |
| 1186 | RegisterMiscDrops | NPC rule | 544 (SandsharkCrimson) | rule=Common{args=[527, 25]} |
| 1187 | RegisterMiscDrops | NPC rule | 545 (SandsharkHallow) | rule=Common{args=[528, 25]} |
| 1188 | RegisterMiscDrops | NPC rule | 47 (CorruptBunny) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4670, 25]} |
| 1189 | RegisterMiscDrops | NPC rule | 464 (CrimsonBunny) | rule=ByCondition{args=[Cond.WindyEnoughForKiteDrops{args=[]}, 4671, 25]} |
| 1190 | RegisterMiscDrops | NPC rule | 268 (IchorSticker) | rule=Common{args=[1332, 1, 2, 5]} |
| 1191 | RegisterMiscDrops | NPC rule | 631 (RockGolem) | rule=Common{args=[3, 1, 10, 20]} |
| 1192 | RegisterMiscDrops | NPC rule | 631 (RockGolem) | rule=Common{args=[4761, 3]} |
| 1194 | RegisterMiscDrops | Condition/rule node |  | branch=leadingConditionRule; gate=Cond.NeverTrue{args=[]} |
| 1196 | RegisterMiscDrops | Rule-graph edge |  | owner=IItemDropRule rule = leadingConditionRule; outcome=OnSuccess; child=OneFromOptions{args=[8, options]} |
| 1198 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4367, chanceDenominator]} |
| 1199 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4368, chanceDenominator]} |
| 1200 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4369, chanceDenominator]} |
| 1201 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4370, chanceDenominator]} |
| 1202 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4371, chanceDenominator]} |
| 1203 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4612, chanceDenominator]} |
| 1204 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4674, chanceDenominator]} |
| 1205 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4343, chanceDenominator, 2, 5]} |
| 1206 | RegisterMiscDrops | Rule-graph edge |  | owner=rule; outcome=OnSuccess; child=CommonDrop{args=[4344, chanceDenominator, 2, 5]} |
| 1207 | RegisterMiscDrops | Multiple-NPC rule | npcNetIds22 | rule=leadingConditionRule |

## NPC happiness / biome preference matrix

PersonalityDatabasePopulator registers the complete 25-entry biome-preference table below. AffectionLevel values: Love=100, Like=50, Dislike=-50, Hate=-100. A preference list selects the highest-affection matching biome before applying a price multiplier. NPCPreferenceTrait exists as a type, but this biome-population method registers no NPC-to-NPC preferences.

| NPC ID | NPC | Love | Like | Dislike | Hate |
|---:|---|---|---|---|---|
| 22 | Guide |  | Forest | Ocean |  |
| 17 | Merchant |  | Forest | Desert |  |
| 588 | Golfer |  | Forest | Underground |  |
| 633 | BestiaryGirl |  | Forest | Desert |  |
| 441 | TaxCollector |  | Snow | Hallow |  |
| 124 | Mechanic |  | Snow | Underground |  |
| 209 | Cyborg |  | Snow | Jungle |  |
| 142 | SantaClaus | Snow |  |  | Desert |
| 207 | DyeTrader |  | Desert | Forest |  |
| 19 | ArmsDealer |  | Desert | Snow |  |
| 178 | Steampunker |  | Desert | Jungle |  |
| 20 | Dryad |  | Jungle | Desert |  |
| 228 | WitchDoctor |  | Jungle | Hallow |  |
| 227 | Painter |  | Jungle | Forest |  |
| 369 | Angler |  | Ocean | Desert |  |
| 229 | Pirate |  | Ocean | Underground |  |
| 353 | Stylist |  | Ocean | Snow |  |
| 38 | Demolitionist |  | Underground | Ocean |  |
| 107 | GoblinTinkerer |  | Underground | Jungle |  |
| 54 | Clothier |  | Underground | Hallow |  |
| 108 | Wizard |  | Hallow | Ocean |  |
| 18 | Nurse |  | Hallow | Snow |  |
| 208 | PartyGirl |  | Hallow | Underground |  |
| 550 | DD2Bartender |  | Hallow | Snow |  |
| 160 | Truffle |  | Mushroom |  |  |

Biome predicates: Ocean=ZoneBeach; Forest=ShoppingZone_Forest; Snow=ZoneSnow; Desert=ZoneDesert; Jungle=ZoneJungle; Underground=ShoppingZone_BelowSurface; Hallow=ZoneHallow; Mushroom=ZoneGlowshroom. Dangerous-biome override uses Corruption, Crimson, and Dungeon predicates. Price multipliers and residence/neighborhood modifiers are in Phase17_GameContent_Environment_Biomes.md.
