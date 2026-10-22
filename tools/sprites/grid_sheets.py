"""Slice grid-layout generated sheets (docs/ART_PROMPTS.md format) into sprites.

Usage: python tools/sprites/grid_sheets.py [project_dir]

Each sheet is a 6x4 grid of cells on a flat dark background with a label strip at the bottom
of every cell. Gutters are found from column/row brightness minima, so sheets of any size work.
Per cell: drop the label strip, flood-fill the flat background from the border, drop tiny
islands (cell-number digits, JPEG specks), and trim. `None` names skip a cell; names ending in
"!" keep their background (scene paintings).
"""
import sys
from collections import deque
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from extract_sprites import strip_thin_lines  # noqa: E402  (shared panel-line remover)

# Sheet file -> 24 names in reading order.
GRID_SHEETS = {
    "sheet_24_colonist_anim.jpg": [
        "anim_walk_1", "anim_walk_2", "anim_walk_3", "anim_walk_4", "anim_walk_5", "anim_walk_6",
        "anim_walk_7", "anim_walk_8", "anim_idle", "anim_jump", "anim_fall", "anim_land",
        "anim_mine_start", "anim_mine_hit", "anim_slash_start", "anim_slash_end", "anim_aim", "anim_hurt",
        "anim_down", "anim_climb_1", "anim_climb_2", "anim_sit_1", "anim_sit_2", "anim_sit_3",
    ],
    "sheet_04_furniture.jpg": [
        "furn_armchair", "furn_sofa", "furn_coffee_table", "furn_modular_bed", "furn_shelving", "furn_wardrobe_locker",
        "furn_control_console", "furn_display_panel", "furn_observation_chair", "furn_dining_table", "furn_stool", "furn_bunk_bed",
        "furn_ceiling_light", "furn_floor_lamp", "furn_desk_lamp", "furn_chemical_analyzer", "furn_crafting_bench", "furn_advanced_fabricator",
        "furn_research_lab", "furn_metal_furnace", "furn_food_synthesizer", "furn_water_purifier", "furn_battery_charger", "furn_power_hub",
    ],
    "sheet_20_events_npcs_critters.jpg": [
        "event_grunt", "event_scout", "event_brute", "event_marksman", "event_drone", "event_commander",
        "event_swarmer", "event_abomination", "event_energy_vortex", "event_rogue_terraformer", "event_void_siphon", "event_corrupted_survivor",
        "npc_store_clerk", "npc_base_engineer", "npc_medical_doctor", "npc_xeno_biologist", "npc_security_guard", "npc_colony_worker",
        "critter_terra_rat", "critter_scavenger_beetle", "critter_sky_flier", "critter_sand_skitterer", "critter_glow_grub", "critter_mega_fauna",
    ],
    "sheet_21_structures_scenes.jpg": [
        "struct_colony_pod", "struct_solar_array", "struct_oxygen_generator", "struct_resource_drill", "struct_hydroponics_bay", "struct_storage_container",
        "struct_sentinel_turret", "struct_blast_wall", "struct_power_hub", "struct_robotics_workshop", "struct_medical_bay", "struct_communications_array",
        "struct_nano_fabricator", "struct_plasma_generator", "scene_acid_coast!", "scene_fungal_hollows!", "scene_crystal_spires!", "scene_verdant_canopy!",
        "scene_precursor_vault!", "creature_neo_terra_reaver", "struct_bloom_hive", "item_glowing_ore", "creature_acid_skitterer", "creature_terraformer_drone",
    ],
    "sheet_06_weapons_partial.jpg": [
        None, None, None, None, None, None,
        None, "item_energy_cutlass_mk2", "item_magnetic_carbine", "item_charge_rifle", "item_drone_controller", None,
        None, None, None, None, None, None,
        None, None, None, None, None, None,
    ],
    "sheet_10_gear_partial.jpg": [
        None, None, None, None, None, None,
        None, None, None, "item_reinforced_armor_suit", None, None,
        "item_metal_ingots", "item_power_pickaxe", None, None, None, None,
        None, None, None, None, None, None,
    ],
}

# Sheets holding several 6x4 grids: file -> [(region box, 24 names), ...].
REGION_SHEETS = {
    "sheet_05_08_stations_weapons.jpg": [
        ((0, 0, 632, 426), [
            "station_field_workbench", "station_nano_fabricator_mk2", "station_ore_refinery_mk2", "station_power_forge_mk2", "station_alloy_anvil", "station_void_forge",
            "station_astral_assembler", "station_chem_lab", "station_bio_lab", "station_loom", "station_sawmill", "station_kiln",
            "station_cooking_station", "station_food_locker", "station_tinker_bench", "station_drone_bay", "station_ammo_press", "station_charge_reactor",
            "station_research_terminal", "station_water_recycler", "station_generator", "station_battery_bank", "station_wiring_panel", "station_teleporter_pad",
        ]),
        ((632, 0, 1264, 426), [
            "weapon_ferrite_shiv", "weapon_scrap_cleaver", "weapon_cobalt_saber", "weapon_frostbite_edge", "weapon_pyrocite_maul", "weapon_uranium_glaive",
            "weapon_viridium_katana", "weapon_lumenite_greatsword", "weapon_voidsteel_scythe", "weapon_astral_blade", "weapon_shock_spear", "weapon_plasma_pike",
            "weapon_tether_whip", "weapon_chain_flail", "weapon_spiked_chain", "weapon_throwing_disc", "weapon_return_boomerang", "weapon_yoyo_drone",
            "weapon_power_fist", "weapon_chainsaw_sword", "weapon_bone_club", "weapon_coral_trident", "weapon_mushroom_mace", "weapon_hive_stinger_rapier",
        ]),
        ((0, 444, 632, 848), [
            "weapon_scrap_pistol", "weapon_ferrite_revolver", "weapon_cobalt_carbine", "weapon_pump_scattergun", "weapon_glacial_crossbow", "weapon_pyrocite_flamer",
            "weapon_uranium_railgun", "weapon_viridium_minigun", "weapon_rad_minigun", "weapon_lumenite_longbow", "weapon_voidsteel_sniper", "weapon_harpoon_gun",
            "weapon_dart_blower", "weapon_nail_gun", "weapon_mortar_tube", "ammo_energy_cells", "ammo_slug_rounds", "ammo_shotgun_shells",
            "weapon_laser_scalpel", "ammo_mini_rocket", "ammo_fuel_canister", "ammo_explosive_charge", "ammo_cryo_rounds", "ammo_toxic_darts",
        ]),
        ((632, 444, 1264, 848), [
            "tech_spark_emitter", "tech_arc_glove", "tech_frost_projector", "tech_ember_rod", "tech_rad_beam_rod", "tech_prism_lens_staff",
            "tech_datapad_chain_lightning", "tech_void_orb_focus", "tech_astral_conductor", "tech_datapad_gravity_well", "tech_datapad_swarm_missiles", "tech_datapad_nova",
            "tech_datapad_storm", "tech_datapad_seeker", "tech_datapad_flare", "tech_datapad_tesla", "tech_datapad_sonic", "tech_spore_censer",
            "tech_coral_wand", "tech_crystal_shard_wand", "tech_meteor_caller", "tech_charge_cell_small", "tech_overcharge_capacitor", "tech_focus_lens_crystal",
        ]),
    ],
}

GRID_SHEETS.update({
    "sheet_09_command.jpg": [
        "cmd_scout_drone_remote", "cmd_hornet_drone_remote", "cmd_repair_drone_remote", "cmd_frost_drone_remote", "cmd_ember_drone_remote", "cmd_rad_swarm_hive",
        "cmd_lumenite_sprite_beacon", "cmd_void_familiar_core", "cmd_astral_guardian_core", "cmd_turret_autogun", "cmd_turret_flame", "cmd_turret_tesla",
        "cmd_turret_mortar", "cmd_signal_whip_mark", "cmd_signal_whip_overdrive", "cmd_signal_whip_chain", "cmd_drone_spare_parts", "cmd_drone_battery",
        "cmd_command_visor", "cmd_swarm_controller", "cmd_mech_companion_key", "cmd_spore_familiar", "cmd_coral_jelly_familiar", "cmd_hive_familiar",
    ],
    "sheet_10_armor.jpg": [
        "armor_scav_helmet", "armor_scav_chest", "armor_scav_legs", "armor_cobalt_trooper_helmet", "armor_cobalt_trooper_chest", "armor_cobalt_trooper_legs",
        "armor_glacial_ranger_helmet", "armor_glacial_ranger_chest", "armor_glacial_ranger_legs", "armor_pyrocite_bulwark_helmet", "armor_pyrocite_bulwark_chest", "armor_pyrocite_bulwark_legs",
        "armor_uranium_hazmat_helmet", "armor_uranium_hazmat_chest", "armor_uranium_hazmat_legs", "armor_viridium_stalker_helmet", "armor_viridium_stalker_chest", "armor_viridium_stalker_legs",
        "armor_lumenite_paladin_helmet", "armor_lumenite_paladin_chest", "armor_lumenite_paladin_legs", "armor_voidsteel_wraith_helmet", "armor_voidsteel_wraith_chest", "armor_voidsteel_wraith_legs",
    ],
})

# Grids with their own geometry: (file, region or None, columns, rows, names). A name ending in
# "$" is a tile texture: kept edge to edge and cropped square instead of cut out.
CUSTOM_GRIDS = [
    ('sheet_30_boss_anims.jpg', None, 6, 4, ['alpha_skitter_walk_1', 'alpha_skitter_walk_2', 'alpha_skitter_walk_3', 'alpha_skitter_walk_4', 'alpha_skitter_walk_5', 'alpha_skitter_walk_6', 'void_stalker_phase_in', 'void_stalker_attack_1', 'void_stalker_attack_2', 'void_stalker_strike_1', 'void_stalker_strike_2', 'void_stalker_phase_out', 'cyber_swarm_leader_command', 'cyber_swarm_leader_charge_1', 'cyber_swarm_leader_charge_2', 'cyber_swarm_leader_shoot_1', 'cyber_swarm_leader_shoot_2', 'cyber_swarm_leader_return', 'astro_god_rising', 'astro_god_pulse', 'astro_god_summon', 'astro_god_discharge_1', 'astro_god_discharge_2', 'astro_god_dissipation']),
    ('sheet_31_abomination_anims.jpg', None, 6, 4, ['abomination_idle_1', 'abomination_mutation_1', 'abomination_mutation_2', 'abomination_final_reveal', 'abomination_final_idle', 'abomination_final_walk', 'abomination_slap', 'abomination_smash', 'abomination_gore', 'abomination_tail', 'fx_toxic_spit', 'fx_slime_puddle', 'abomination_pulse', 'abomination_beam_charge', 'abomination_beam', 'abomination_hit_1', 'abomination_hit_2', 'abomination_core', 'fx_ultimate_charge', 'fx_core_blast', 'abomination_defeat_1', 'abomination_defeat_2', 'fx_core_disintegrate', 'fx_radioactive_remains']),
    ('sheet_32_crystal_beast_anims.png', None, 6, 4, ['crystal_beast_idle_1', 'crystal_beast_idle_2', 'crystal_beast_idle_3', 'crystal_beast_idle_4', 'crystal_beast_idle_5', 'crystal_beast_idle_6', 'crystal_beast_walk_1', 'crystal_beast_walk_2', 'crystal_beast_walk_3', 'crystal_beast_walk_4', 'crystal_beast_walk_5', 'crystal_beast_walk_6', 'crystal_beast_attack_1', 'crystal_beast_attack_2', 'crystal_beast_attack_3', 'crystal_beast_attack_4', 'crystal_beast_attack_5', 'crystal_beast_attack_6', 'crystal_beast_hit_1', 'crystal_beast_hit_2', 'crystal_beast_hit_3', 'crystal_beast_death_1', 'crystal_beast_death_2', 'crystal_beast_death_3']),
    ('sheet_33_biome_mix.png', None, 6, 4, ['tile_icy_tile_alt', 'snow_bank', 'frost_crystal', 'glacial_ore', 'cryopterous', 'snow_wolf', 'tile_lava_rock_alt', 'tile_molten_lava_alt', 'obsidian_shard', 'lava_skitter', 'magma_turtle', None, 'pyrocite_ore', 'geiger_bug', 'rad_stalker_mech', 'tile_mutant_flora_alt', 'radiant_fungus', 'mutant_vinelash', 'uranium_ore', 'rad_stalker_mech_b', 'tile_scrap_heap_alt', 'salvaged_electronics', 'scrap_iron_ore', 'junk_bot']),
    ('sheet_19_precursor_vault.jpg', None, 6, 4, ['tile_precursor_brick$', 'tile_precursor_glyph$', 'tile_geode_cyan$', 'tile_geode_magenta$', 'deco_basalt_column_top', 'deco_alabaster_pillar', 'deco_precursor_statue', 'deco_glyph_tablet', 'deco_energy_pylon', 'deco_ancient_trap_dart', 'deco_geode_cluster', 'deco_mite_silk_cocoon', 'creature_vault_sentinel', 'creature_glyph_drone', 'creature_ancient_guardian', 'creature_geode_crab', 'creature_basalt_golem_mini', 'creature_alabaster_hoplite', 'creature_mite_broodmother', 'creature_silk_spider_mite', 'boss_vault_colossus', 'item_vault_key', 'item_ancient_power_cell', 'item_precursor_relic']),
    ('sheet_17_crystal_spires.jpg', None, 6, 4, ['tile_prismatic_grass$', 'tile_pearl_stone$', 'tile_crystal_spire$', 'tile_pearl_sand$', 'flora_crystal_tree', 'flora_prism_flower', 'flora_light_reed', 'flora_singing_crystal', 'flora_halo_bush', 'flora_pearl_shell_plant', 'critter_prism_butterfly', 'critter_light_wisp_bird', 'critter_crystal_bunny', 'critter_halo_snail', 'creature_prism_knight', 'creature_shard_unicorn', 'creature_light_mimic', 'creature_crystal_wraith', 'creature_refractor_turret', 'creature_halo_bat', 'creature_pearl_golem', 'creature_spire_dragonfly', 'boss_prism_empress', 'struct_crystal_altar']),
    ('sheet_14b_tools_full.jpg', None, 6, 4, ['tool_ferrite_drill', 'tool_cobalt_drill', 'tool_glacial_drill', 'tool_viridium_drill', 'tool_lumenite_drill', 'tool_voidsteel_drill', 'tool_astral_drill', 'tool_ferrite_cutter', 'tool_cobalt_cutter', 'tool_viridium_cutter', 'tool_lumenite_cutter', 'tool_ferrite_demolisher', 'tool_cobalt_demolisher', 'tool_viridium_demolisher', 'tool_lumenite_demolisher', 'tool_paint_sprayer', 'tool_wire_tool', 'tool_bucket_water', 'tool_bucket_lava', 'tool_fishing_rod', 'tool_bug_net', 'tool_rope_coil', 'tool_glow_sticks', 'tool_teleport_beacon']),
    ('sheet_23_critters.jpg', None, 6, 4, ['critter_hop_bunny', 'critter_glow_firefly', 'critter_pebble_beetle', 'critter_grass_snake', 'critter_snow_hare', 'critter_penguin_drone', 'critter_scrap_rat', 'critter_dune_lizard', 'critter_ember_newt', 'critter_cave_bat', 'critter_worm', 'critter_butterfly_blue', 'critter_butterfly_gold', 'critter_dragonfly', 'critter_squirrel_bot', 'critter_owl', 'critter_duck', 'critter_fish_silver', 'critter_fish_glow', 'critter_crab_small', 'critter_snail', 'critter_ladybug_bot', 'critter_frog', 'critter_golden_critter']),
    ('sheet_20_event_enemies.jpg', None, 6, 4, ['event_husk', 'event_rust_wraith', 'event_blood_moth_swarm', 'event_rust_hound', 'event_eye_drone', 'event_corroded_mech', 'event_swarm_bug_falling', 'event_swarm_queen', 'event_dust_devil', 'event_sand_ray', 'event_scrap_elemental', 'event_dust_raider', 'event_acid_slime', 'event_acid_bird', 'event_toxic_cloud', 'boss_moon_eater', 'event_aurora_wisp', 'event_festival_balloon_drone', 'event_meteor_head', 'event_meteor_crawler', 'event_eclipse_shade', 'event_eclipse_reaper', 'event_eclipse_moth_mech', 'boss_eclipse_lord']),
    ('sheet_13_materials.jpg', None, 8, 4, ['item_lumenite_ore', 'item_voidsteel_ore', 'item_astral_ore', 'item_meteorite_ore', 'item_ferrite_bar', 'item_cobalt_bar', 'item_glacial_bar', 'item_pyrocite_bar', None, None, None, 'item_uranium_rod', None, 'item_viridium_bar', 'item_lumenite_bar', 'item_voidsteel_bar', 'item_astral_bar', None, None, None, 'item_ruby_shard', 'item_sapphire_shard', 'item_emerald_shard', 'item_amethyst_shard', None, 'item_topaz_shard', 'item_diamond_shard', 'item_credits', 'item_cloth_roll', 'item_circuit_board', 'item_power_cell', 'item_bio_gel']),
    ('sheet_16_fungal_hollows.jpg', None, 6, 4, ['flora_glowshroom_patch', 'tile_mycelium_stone$', 'tile_spore_block$', 'tile_mushroom_cap$', 'flora_giant_glowshroom', 'flora_shelf_fungus', 'flora_spore_puffball', 'flora_glow_vine', 'flora_mycelium_tendrils', 'flora_biolum_moss', 'critter_glow_snail', 'critter_spore_moth', 'critter_cave_frog', 'critter_truffle_mite', 'creature_spore_zombie', 'creature_fungal_crawler', 'creature_puff_drifter', 'creature_mycelid_shaman', 'creature_shroom_stalker', 'creature_cap_turtle', 'creature_lantern_bat', 'creature_spore_golem', 'boss_spore_monarch', 'struct_fungal_hab_ruin']),
    ('sheet_15_acid_coast.jpg', (0, 50, 1408, 768), 8, 4, ['tile_acid_water$', 'tile_salt_crust_coast$', 'tile_coral_reef$', None, 'tile_shell_beach$', 'flora_tube_coral', 'flora_kelp_strand', None, 'flora_salt_spire', 'flora_shell_pile', None, 'flora_tide_pool', 'flora_driftwood', None, 'critter_shell_crab', 'critter_glow_jelly', 'critter_beach_skitter', 'critter_sea_bird', 'creature_acid_shark', '+', 'creature_tide_leech', 'creature_reef_crawler', None, 'creature_brine_golem', 'creature_spitting_barnacle', 'creature_storm_ray', 'creature_drowned_diver', None, 'creature_coral_sentinel', 'struct_coastal_hab_pod', None, 'struct_beacon_tower']),
    ('sheet_22_town_npcs.jpg', None, 6, 4, ['npc_quartermaster', 'npc_medic', 'npc_armorer', 'npc_demolitionist', 'npc_tailor', 'npc_cartographer', 'npc_mechanic', 'npc_tinkerer', 'npc_pilot', 'npc_cook', 'npc_arborist', 'npc_archivist', 'npc_quartermaster_bound', 'npc_medic_bound', 'npc_armorer_bound', 'npc_demolitionist_bound', 'npc_tailor_bound', 'npc_cartographer_bound', 'npc_mechanic_bound', 'npc_tinkerer_bound', 'npc_pilot_bound', 'npc_cook_bound', 'npc_arborist_bound', 'npc_archivist_bound']),
    ('sheet_18_verdant_canopy.jpg', (0, 50, 1408, 768), 8, 4, ['tile_canopy_grass$', None, 'tile_mud$', None, 'tile_hive_comb$', None, 'tile_nectar$', None, 'flora_canopy_tree', 'flora_giant_leaf', 'flora_pitcher_plant', 'flora_thorn_bush', 'flora_bloom_flower', None, 'flora_hanging_vines', None, 'critter_canopy_parrot', 'critter_leaf_frog', 'critter_pollen_bee', 'critter_vine_snake', 'creature_pollen_drone_swarm', 'creature_thorn_lurker', None, None, 'creature_canopy_ape', 'creature_snapjaw_plant', 'creature_hive_guard', 'creature_mud_crawler', 'creature_spitting_orchid', 'creature_jungle_stalker', None, 'struct_precursor_vault_door']),

    ("sheet_01_natural_blocks.jpg", (0, 62, 1264, 848), 6, 4, [
        "tile_mud$", "tile_clay$", "tile_gravel$", "tile_silt$", "tile_salt_crust$", "tile_acid_sand$",
        "tile_shell_sand$", "tile_coral_reef$", "tile_ash$", "tile_slate$", "tile_alabaster$", "tile_basalt_column$",
        "tile_hive_comb$", "tile_nectar$", "tile_pearl_stone$", "tile_prismatic_grass$", "tile_glowshroom_grass$", "tile_thin_ice$",
        "tile_meteorite$", "tile_mite_silk$", "tile_moss_red$", "tile_moss_blue$", "tile_moss_violet$", "tile_moss_gold$",
    ]),
    ("sheet_02_crafted_blocks.jpg", None, 6, 4, [
        "tile_ferrite_brick$", "tile_cobalt_brick$", "tile_viridium_brick$", "tile_lumenite_brick$", "tile_voidsteel_brick$", "tile_astral_brick$",
        "tile_basalt_brick$", "tile_alabaster_brick$", "tile_slate_brick$", "tile_glass$", "tile_reinforced_glass$", "tile_hull_plating_grey$",
        "tile_hull_plating_red$", "tile_hull_plating_blue$", "tile_hull_plating_green$", "tile_hull_plating_hazard$", "tile_astro_timber_planks$", "tile_charred_planks$",
        "tile_crystal_block", "tile_pearl_brick$", "tile_hive_brick$", "tile_coral_brick$", "tile_circuit_panel$", "tile_conveyor$",
    ]),
    ("sheet_03_walls.jpg", (0, 34, 1408, 768), 6, 4, [
        "wall_regolith$", "wall_rock$", "wall_ice$", "wall_scrap$", "wall_mud$", "wall_hive$",
        "wall_fungal$", "wall_pearl$", "wall_radiated$", "wall_overgrowth$", "wall_obsidian$", "wall_coral$",
        "wall_plank$", "wall_plating_grey$", "wall_plating_blue$", "wall_glass$", "wall_brick_ferrite$", "wall_brick_alabaster$",
        "wall_lattice$", "wall_circuit$", "wall_derelict_hull$", "wall_precursor$", "wall_crystal$", "wall_basalt$",
    ]),
    ("sheet_04b_furniture_lighting.jpg", None, 6, 4, [
        "furn_plank_door", "furn_plank_door_open", "furn_hull_door", "furn_hull_door_open", "furn_trapdoor", "furn_plank_chair",
        "furn_hull_chair", "furn_plank_table", "furn_hull_table", "furn_bunk_bed_metal", "furn_cryo_pod_bed", "furn_storage_crate",
        "furn_locked_vault_chest", "furn_shelf_jars", "furn_glow_lamp_cyan", "furn_glow_lamp_amber", "furn_glow_lamp_violet", "furn_wall_torch",
        "furn_standing_floodlight", "furn_plant_pot", "furn_herb_planter", "furn_colony_banner", "furn_sign_post", "furn_painting_planet",
    ]),
    ("sheet_09b_drones.jpg", (0, 0, 1408, 404), 7, 2, [
        "minion_scout_drone", "minion_hornet_drone", "minion_repair_drone", "minion_frost_drone", "minion_ember_drone", "minion_rad_swarm", "minion_rad_swarmling",
        "minion_lumenite_sprite", "minion_void_familiar", "minion_astral_guardian", "sentry_autogun", "sentry_flame", "sentry_tesla", "sentry_mortar",
    ]),
    ("sheet_14_tools.jpg", None, 6, 4, [
        "tool_ferrite_drill", "tool_cobalt_drill", "tool_glacial_drill", "tool_ferrite_cutter", "tool_cobalt_cutter", "tool_paint_sprayer",
        "tool_wire_tool", "tool_bucket_water", "tool_bucket_lava", "tool_fishing_rod", "tool_bug_net", "tool_rope_coil",
        None, None, None, None, None, None,
        None, None, None, None, None, None,
    ]),
]

# Hand-placed boxes for sheets that ignored the grid (the isometric terrain sheet's wall pack).
BOX_SPRITES = {
    'sheet_21_invasions.jpg': [('event_scavenger_grunt', (6, 8, 170, 166)), ('event_scavenger_brute', (182, 8, 346, 166)), ('event_scavenger_sniper', (358, 8, 522, 166)), ('event_scavenger_rigger', (534, 8, 698, 166)), ('event_scavenger_rigger_b', (710, 8, 874, 166)), ('event_scavenger_bike_rider', (886, 8, 1110, 166)), ('boss_scavenger_warlord', (1122, 8, 1402, 166)), ('event_pirate_deckhand', (6, 210, 170, 356)), ('event_pirate_gunner', (182, 210, 346, 350)), ('event_pirate_parrot_drone', (358, 210, 522, 345)), ('event_pirate_cutlass_bot', (534, 210, 698, 356)), ('event_pirate_cannon', (710, 210, 874, 345)), ('boss_pirate_dreadnought', (886, 210, 1110, 356)), ('boss_protocol_dragon', (1112, 215, 1402, 545)), ('event_protocol_grunt', (6, 403, 170, 540)), ('event_protocol_shieldbearer', (182, 403, 346, 536)), ('event_protocol_wyvern', (358, 403, 522, 536)), ('event_protocol_ogre', (534, 403, 698, 536)), ('event_protocol_mage', (710, 403, 874, 536)), ('event_protocol_mage_b', (886, 403, 1108, 540)), ('event_precursor_probe', (6, 588, 170, 732)), ('event_probe_gunner', (182, 588, 346, 732)), ('event_probe_tesla_walker', (358, 588, 522, 732)), ('event_probe_brain_drone', (534, 588, 698, 732)), ('event_probe_core', (710, 588, 874, 732)), ('boss_mothership_core', (886, 588, 1226, 732)), ('item_invasion_banner', (1238, 588, 1402, 732))],
    'sheet_18_verdant_canopy.jpg': [('boss_bloom_matriarch', (1062, 412, 1232, 728))],

    "sheet_01_terrain_isometric.jpg": [
        ("wallart_hull_hazard", (861, 30, 956, 108)), ("wallart_glass_rail", (964, 30, 1053, 108)),
        ("wallart_rock_pitted", (861, 114, 956, 189)), ("wallart_rust_cracked", (964, 114, 1053, 189)),
        ("wallart_green_glass", (861, 194, 956, 269)), ("wallart_cross_brace", (964, 194, 1053, 269)),
        ("wallart_bulkhead_door", (861, 274, 956, 351)), ("wallart_panel_seam", (964, 274, 1053, 351)),
    ],
}

COLUMNS, ROWS = 6, 4

# Cells whose background has a glow or gradient need a looser background match.
TOLERANCE = {"boss_prism_empress": 34, "boss_bloom_matriarch": 30, "struct_crystal_altar": 28, "creature_light_mimic": 28}
LABEL_FRACTION = 0.25


def luminance_profile(image, axis):
    gray = image.convert("L")
    width, height = gray.size
    pixels = gray.load()
    if axis == 0:
        return [sum(pixels[x, y] for y in range(0, height, 2)) / (height / 2) for x in range(width)]
    return [sum(pixels[x, y] for x in range(0, width, 2)) / (width / 2) for y in range(height)]


def find_cuts(profile, parts):
    """Darkest line near each expected gutter position."""
    length = len(profile)
    cuts = [0]
    # Stay near the even split: label strips can be as dark as gutters.
    window = max(3, length // (parts * 14))
    for k in range(1, parts):
        expected = round(length * k / parts)
        lo, hi = max(1, expected - window), min(length - 1, expected + window)
        cuts.append(min(range(lo, hi), key=lambda i: profile[i]))
    cuts.append(length)
    return cuts


def remove_flat_background(image, tolerance=16):
    rgba = image.convert("RGBA")
    width, height = rgba.size
    pixels = rgba.load()
    # Sample the cell's own background just inside its corners (gutters are darker and would skew it).
    inset = 3
    samples = [pixels[x, y] for x in (inset, width - 1 - inset) for y in (inset, height - 1 - inset)]
    samples += [pixels[width // 2, inset], pixels[inset, height // 2], pixels[width - 1 - inset, height // 2]]
    samples.sort(key=lambda p: p[0] + p[1] + p[2])
    reference = samples[len(samples) // 2]

    def background(p):
        # Only colours close to the flat cell colour; dark outlines stay so the fill cannot leak inside.
        return abs(p[0] - reference[0]) + abs(p[1] - reference[1]) + abs(p[2] - reference[2]) <= tolerance * 2

    seen = bytearray(width * height)
    queue = deque((x, y) for x in range(width) for y in (0, height - 1))
    queue.extend((x, y) for y in range(height) for x in (0, width - 1))
    while queue:
        x, y = queue.popleft()
        i = y * width + x
        if seen[i]:
            continue
        seen[i] = 1
        if not background(pixels[x, y]):
            continue
        pixels[x, y] = (0, 0, 0, 0)
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < width and 0 <= ny < height:
                queue.append((nx, ny))
    strip_thin_lines(pixels, width, height, min_length=24)
    drop_small_islands(pixels, width, height, min_size=max(90, width * height // 150))
    bbox = rgba.getbbox()
    return rgba.crop(bbox) if bbox else rgba


def square_tile(crop, reference):
    """The texture inside a tile cell: rows/columns mostly unlike the sheet background, cropped square."""
    rgb = crop.convert("RGB")
    width, height = rgb.size
    pixels = rgb.load()

    def differs(p):
        return abs(p[0] - reference[0]) + abs(p[1] - reference[1]) + abs(p[2] - reference[2]) > 40

    # Texture rows are nearly full; label text and frames are sparse, so require 60% coverage.
    xs = [x for x in range(width) if sum(differs(pixels[x, y]) for y in range(0, height, 3)) > height * 0.6 / 3]
    ys = [y for y in range(height) if sum(differs(pixels[x, y]) for x in range(0, width, 3)) > width * 0.6 / 3]
    if not xs or not ys or max(xs) - min(xs) < width * 0.35 or max(ys) - min(ys) < height * 0.35:
        # Too little detected texture (e.g. a pale tile on a similar background): use the cell centre.
        side = int(min(width, height) * 0.82)
        left, top = (width - side) // 2, (height - side) // 2
        return rgb.crop((left, top, left + side, top + side)).convert("RGBA")
    box = (min(xs), min(ys), max(xs) + 1, max(ys) + 1)
    texture = rgb.crop(box)
    side = min(texture.size)
    # Trim a couple of pixels so the cell frame never shows at tile seams.
    margin = max(2, side // 40)
    left = (texture.width - side) // 2 + margin
    top = (texture.height - side) // 2 + margin
    return texture.crop((left, top, left + side - 2 * margin, top + side - 2 * margin)).convert("RGBA")


def drop_small_islands(pixels, width, height, min_size):
    """Keep the subject; drop specks, cell numbers and leftover gutter lines.

    A piece survives only if it is at least min_size pixels, at least 3% of the largest piece,
    and more than four pixels thick in both directions.
    """
    seen = bytearray(width * height)
    components = []
    for start_y in range(height):
        for start_x in range(width):
            index = start_y * width + start_x
            if seen[index] or pixels[start_x, start_y][3] == 0:
                continue
            component = []
            queue = deque([(start_x, start_y)])
            seen[index] = 1
            while queue:
                x, y = queue.popleft()
                component.append((x, y))
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < width and 0 <= ny < height:
                        ni = ny * width + nx
                        if not seen[ni] and pixels[nx, ny][3] > 0:
                            seen[ni] = 1
                            queue.append((nx, ny))
            components.append(component)
    if not components:
        return
    largest = max(len(component) for component in components)
    for component in components:
        xs = [x for x, _ in component]
        ys = [y for _, y in component]
        thin = max(xs) - min(xs) < 5 or max(ys) - min(ys) < 5
        if len(component) < max(min_size, largest * 0.03) or thin:
            for x, y in component:
                pixels[x, y] = (0, 0, 0, 0)


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
    out_dir = root / "Assets" / "Sprites"
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    jobs = [(file, None, COLUMNS, ROWS, names) for file, names in GRID_SHEETS.items()]
    jobs += [(file, box, COLUMNS, ROWS, names) for file, regions in REGION_SHEETS.items() for box, names in regions]
    jobs += CUSTOM_GRIDS
    for file, region, columns, rows, names in jobs:
        path = root / "art" / "sheets" / file
        if not path.exists():
            continue
        sheet = Image.open(path).convert("RGB")
        background = sheet.getpixel((sheet.width - 3, sheet.height - 3))
        if region is not None:
            sheet = sheet.crop(region)
        xs = find_cuts(luminance_profile(sheet, 0), columns)
        ys = find_cuts(luminance_profile(sheet, 1), rows)
        for index, name in enumerate(names):
            if name is None or name == "+":
                continue
            row, column = divmod(index, columns)
            span = 1
            while index + span < len(names) and names[index + span] == "+" and column + span < columns:
                span += 1
            inset = max(5, min(11, (xs[column + 1] - xs[column]) // 18))
            left, right = xs[column] + inset, xs[column + span] - inset
            top, bottom = ys[row] + inset, ys[row + 1] - inset
            keep = name.endswith("!")
            tile = name.endswith("$")
            name = name.rstrip("!$")
            if tile:
                # Tile cells are uniform: texture fills the cell above the label, so take a fixed centred square.
                texture_bottom = bottom - int((bottom - top) * 0.24)
                side = int(min(right - left, texture_bottom - top) * 0.90)
                cx, cy = (left + right) // 2, (top + texture_bottom) // 2
                sprite = sheet.crop((cx - side // 2, cy - side // 2, cx + side // 2, cy + side // 2)).convert("RGBA")
            elif keep:
                # Scenes span the whole cell above the label strip.
                crop = sheet.crop((left, top, right, bottom - int((bottom - top) * 0.18)))
                sprite = crop.convert("RGBA")
            else:
                top_trim = int((bottom - top) * (0.10 if file == "sheet_14_tools.jpg" else 0))
                label = 0.30 if file.endswith(".png") else LABEL_FRACTION
                crop = sheet.crop((left, top + top_trim, right, bottom - int((bottom - top) * label)))
                # Generated sheets print the cell number in the top-left corner; paint it out.
                corner = (0, 0, int(crop.width * 0.17), int(crop.height * 0.16))
                crop.paste(crop.getpixel((crop.width - 4, 4)), corner)
                sprite = remove_flat_background(crop, tolerance=TOLERANCE.get(name, 16))
            sprite.save(out_dir / f"{name}.png")
            written.append((name, sprite))
    for file, boxes in BOX_SPRITES.items():
        path = root / "art" / "sheets" / file
        if not path.exists():
            continue
        sheet = Image.open(path).convert("RGB")
        for name, box in boxes:
            sprite = remove_flat_background(sheet.crop(box), tolerance=TOLERANCE.get(name, 16))
            sprite.save(out_dir / f"{name}.png")
            written.append((name, sprite))

    cell = 128
    columns = 12
    rows = (len(written) + columns - 1) // columns
    preview = Image.new("RGBA", (columns * cell, max(1, rows) * cell), (255, 0, 255, 255))
    for i, (_, sprite) in enumerate(written):
        thumb = sprite.copy()
        thumb.thumbnail((cell - 6, cell - 6))
        preview.alpha_composite(thumb, ((i % columns) * cell + (cell - thumb.width) // 2, (i // columns) * cell + (cell - thumb.height) // 2))
    preview.save(out_dir.parent / "_grid_preview.png")
    print(f"Wrote {len(written)} grid sprites")


if __name__ == "__main__":
    main()
