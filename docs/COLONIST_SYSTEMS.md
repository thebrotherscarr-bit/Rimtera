# Colonist systems: RimWorld structure vs Rimtera (diff and design)

Evidence: `rea inspect-managed-members` on RimWorld 1.6 `Assembly-CSharp.dll` (SHA-256 `5cf1b5be…356a`), saved as `docs/rimworld-evidence/health.json`, `mood.json`, `social.json`. Only type/member/field names and their relationships were used. RimWorld's body, hediff, thought and interaction *definitions* live in its XML data and were not read or copied; Rimtera's anatomy, conditions, thoughts, interactions and all numbers are original.

## 1. Observed structure (RimWorld)

**Health.** `Pawn_HealthTracker` owns a `HediffSet` (all conditions), a `PawnCapacitiesHandler` (cached capacity levels) and a health state (`ShouldBeDowned`, `ShouldBeDead`, `ShouldBeDeadFromRequiredCapacity`, `ShouldBeDeadFromLethalDamageThreshold`, `InPainShock`). The body is a `BodyDef` with a `corePart` and a tree of `BodyPartRecord`s carrying `coverage`, `depth` (inside/outside), `height`, `groups`, `parent` and child `parts`. Damage goes through `DamageWorker_AddInjury.ChooseHitPart` / `ApplyDamageToPart` / `ReduceDamageToPreserveOutsideParts` / `CheckDuplicateDamageToOuterParts`; `CalculateMissingPartHediffsFromInjury` turns a destroyed part into `Hediff_MissingPart` (with `IsFresh`, bleeding while fresh). A `Hediff` has a `part`, `severity`, staged effects (`HediffStage`: `capMods`, `painOffset`/`painFactor`, `partEfficiencyOffset`, `lifeThreatening`, `blocksMentalBreaks`, `opinionOfOthersFactor`, `pctConditionalThoughtsNullified`), `BleedRate`, `Tended`, `TendableNow`, `Heal`. Comps add tending (`HediffComp_TendDuration`: `tendQuality`, `tendTicksLeft`) and infection (`HediffComp_Infecter`: chance from severity/tend quality/room). `HediffSet` derives `CalculatePain`, `CalculateBleedRate`, `GetPartHealth`, `GetInjuredParts`, `GetMissingPartsCommonAncestors`, `GetNotMissingParts`. `PawnCapacityUtility.CalculateCapacityLevel` / `CalculatePartEfficiency` / `CalculateLimbEfficiency` / `CalculateTagEfficiency` compute capacities from tagged parts.

**Mood.** `Need_Mood` holds a `ThoughtHandler` (`memories` + `situational`); mood is driven by `TotalMoodOffset` and reaches its instant level over time. `MemoryThoughtHandler` adds/merges/expires `Thought_Memory` (`age`, `moodOffset`, `otherPawn`, stacking via `GroupsWith`/`TryMergeWithExistingMemory`, `ShouldDiscard`). `SituationalThoughtHandler` recalculates situational thoughts on an interval from `ThoughtWorker`s (e.g. `ThoughtWorker_Hediff`, `ThoughtWorker_Pain`) and caches social ones. `Thought_MemorySocial` adds an `opinionOffset` that fades with age. `MentalBreaker` has minor/major/extreme thresholds, `ticksBelow*` gates, MTB rolls, `TryDoRandomMoodCausedMentalBreak`, and a recovery notice.

**Social.** `Pawn_RelationsTracker` holds `directRelations` (`DirectPawnRelation`: def, `otherPawn`, `startTicks`), `OpinionOf` (= social thoughts + compatibility), `CompatibilityWith` / `ConstantPerPawnsPairCompatibilityOffset`, friend/rival opinion thresholds, and death notifications that scale friend/rival-died thoughts. `Pawn_InteractionsTracker` attempts random interactions on an interval (`TryInteractRandomly`, `CanInteractNowWith`, `InteractedTooRecentlyToInteract`), picks an `InteractionWorker` by `RandomSelectionWeight` (chitchat, insult, romance attempt with `SuccessChance`), adds interaction thoughts, and may start a social fight (`SocialFightChance`, `StartSocialFight`).

**Integration points.** Health → mood via hediff thought workers (pain, missing parts, bleeding/illness stages). Health → social via `opinionOfOthersFactor` and disfigurement. Social → health via social fights. Death → mood via relation-scaled memories.

## 2. Rimtera before this pass

| Area | Rimtera had | Gap |
|---|---|---|
| Body | 6 parts, two levels, function flags, coverage | no organs, no inside/outside depth, no groups |
| Damage | every wound on the torso; HP pool separate from body | no hit-part choice, no propagation, no part destruction |
| Conditions | one injury kind (severity, heal rate, pain) | no kinds, bleeding, tending, infection, scars, illness |
| Capacities | consciousness only; unused by gameplay | no moving/manipulation/sight/breathing/blood pumping/talking effects |
| States | death at 0 HP | no downed, no organ/blood-loss death, no pain shock |
| Mood | fixed formula of needs + trait offsets − pain | no thoughts, memories, situational thoughts, decay |
| Breaks | none | no thresholds, gating or break states |
| Relations | none | no opinions, compatibility, relations |
| Interactions | none | no chats, insults, romance, social fights |
| Saves | colonist position and needs only | injuries, memories and relations were not saved |

## 3. Rimtera design (original)

- **Anatomy** (`ColonistAnatomy`): torso core with inside organs (heart, lungs, stomach, liver, spine) and outside parts (neck → head → skull → brain; eyes, jaw; shoulders → arms → hands; legs → feet). Each part: max HP, coverage, depth, functional tags (consciousness, sight, breathing, blood pumping, digestion, moving, manipulation, talking).
- **Conditions**: kinds (cut, bruise, burn, frostbite, bite, shot, crush, radiation sickness, infection, scar, blood loss, missing part) with bleed rates, pain, staged labels, tending (quality, duration, stops bleeding, speeds healing), infection chance for untended open wounds, a chance for scars on heavy wounds.
- **Damage**: coverage-weighted outside-part selection, chance to pass inward to an organ, part destroyed when damage ≥ part HP (children go with it), fresh stumps bleed; HP stays as the combat pool but death/downed also follow organs and blood loss.
- **Capacities** drive play: moving → walk speed, manipulation → attack/mining cooldown, sight → ranged spread, consciousness ↓ → downed; pain shock above a threshold.
- **Thoughts**: memories (duration, stack limit with diminishing stacks, merge), situational thoughts recomputed on the story interval (pain stages, bleeding, missing parts, hunger, exhaustion, cold/heat, toxic fallout, in a room vs outdoors, shared colony with a friend/rival), social memories with fading opinion offsets. Mood eases toward base + Σ thoughts.
- **Breaks**: minor/major/extreme thresholds shifted by traits, time-below gates, seeded rolls; states (sad wander, hide in room, food binge, tantrum, daze), recovery with a catharsis memory.
- **Relations**: compatibility from a stable pair hash + traits, opinion = compatibility + Σ social memories, derived Friend/Rival, explicit Lover/Partner/Crewmate/Sibling; interactions on an interval (chitchat, deep talk, kind words, slight, insult, romance attempt), social fights that cause bruises; deaths add relation-scaled memories.
- **Saves**: per-part conditions, memories, relations and break state go into `GameSnapshot` as trailing optional data.
