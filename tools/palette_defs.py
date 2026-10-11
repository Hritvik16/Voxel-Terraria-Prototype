"""palette_defs.py -- the candidate palette directions (owner question (ii),
2026-09-25), as GenMat code -> sRGB hex. Shared by palette-board.py (flat
sections) and palette-render.py (ray-traced terrain).

Round 1 showed A, B, C. The owner rejected B (Pastel Atlas); round 2 replaces it
with D. B stays here, marked dropped, so round 1 stays reproducible.
"""

NAMES = {1: "water", 2: "lava", 3: "honey", 4: "oil", 5: "acid", 6: "sludge", 7: "quicksand",
         10: "grass", 11: "jungle grass", 12: "sand", 13: "snow", 14: "beach sand",
         20: "loam", 21: "packed sand", 22: "packed ice", 23: "mud",
         30: "limestone", 31: "slate", 32: "ochre clay", 33: "granite", 34: "shale",
         40: "cream sandstone", 41: "red sandstone", 42: "orange sandstone", 43: "maroon mudstone", 44: "pink siltstone",
         50: "blue slate", 51: "marble", 52: "basalt", 53: "teal quartzite", 54: "silver schist",
         60: "karst limestone", 61: "black basalt", 62: "laterite", 63: "moss rock", 64: "jade serpentine",
         70: "deepstone", 71: "cavern rock", 72: "ash", 73: "obsidian", 74: "magma crust", 75: "bedrock",
         80: "sinter", 81: "crystal blue", 82: "crystal amber", 83: "crystal rose", 84: "crystal green",
         85: "bone", 86: "meteor ore", 87: "bark", 88: "leaves", 89: "mushroom stem", 90: "mushroom cap",
         91: "volcanic rock", 92: "standing stone",
         # B10 (map v4): savanna and badlands
         15: "savanna grass", 16: "badlands clay", 24: "red earth", 25: "hardpan",
         35: "kopje granite", 36: "ironstone", 37: "gneiss",
         45: "red band", 46: "cream band", 47: "ochre band", 48: "umber mudstone", 49: "plum shale",
         # gameplay step 3 (map v4): ores, one per depth layer
         65: "copper ore", 66: "iron ore", 67: "gold ore", 68: "cobalt ore", 69: "emberstone"}

BIOMES = [("Forest", [10, 20, 30, 31, 32, 33, 34]), ("Desert", [12, 21, 40, 41, 42, 43, 44]),
          ("Snow", [13, 22, 50, 51, 52, 53, 54]), ("Jungle", [11, 23, 60, 61, 62, 63, 64]),
          ("Savanna", [15, 24, 35, 36, 37]), ("Badlands", [16, 25, 45, 46, 47, 48, 49]),
          ("Depths", [70, 71, 72, 73, 74, 75]), ("Fluids", [1, 2, 3, 4, 5, 6, 7]),
          ("Landmarks", [80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92])]

# Self-lit materials and their emission strength (linear multiplier of albedo).
EMISSIVE = {2: 0.55, 6: 0.9, 74: 0.25, 81: 0.55, 82: 0.55, 83: 0.55, 84: 0.55, 86: 1.0}


def hexes(d):
    return {k: tuple(int(v[i:i + 2], 16) / 255 for i in (0, 2, 4)) for k, v in d.items()}


PALETTES = {
    "A": ("Umber & Ember", "Low-chroma earths; biomes differ by the temperature of their neutrals. "
          "Saturation is kept for life, minerals and fluids.", hexes({
        1: "24566a", 2: "f26b1d", 3: "c98f28", 4: "1a1510", 5: "a8d81f", 6: "66cc33", 7: "9e855c",
        10: "6b7f47", 11: "4d7540", 12: "dbc9a1", 13: "eef0f2", 14: "e3d4b0",
        20: "66503a", 21: "b8a37f", 22: "b8ccdb", 23: "4d3e33",
        30: "b8b09e", 31: "545a61", 32: "a88c5c", 33: "948a85", 34: "474545",
        40: "d4c29e", 41: "94543d", 42: "bf8554", 43: "663b36", 44: "c79e94",
        50: "667385", 51: "d9d9d4", 52: "383b40", 53: "789494", 54: "9ea3a8",
        60: "b3b399", 61: "333633", 62: "94593d", 63: "617352", 64: "668c73",
        70: "40404a", 71: "54504d", 72: "6b6661", 73: "141219", 74: "59291a", 75: "1f1f1f",
        80: "e0d6bd", 81: "59a6fa", 82: "faad33", 83: "f273b3", 84: "59eb8c",
        85: "dbd1b3", 86: "f24d8c", 87: "5c422e", 88: "476b38", 89: "d1c7b3", 90: "9e4d8c",
        91: "2e2929", 92: "8f8c85"})),
    "B": ("Pastel Atlas (DROPPED)", "Round 1 only -- the owner rejected it.", hexes({
        1: "5aa9c9", 2: "ff8a4d", 3: "f0c060", 4: "4a4050", 5: "c8f060", 6: "8fe07a", 7: "d6bf94",
        10: "9cc48a", 11: "6fc2a0", 12: "f5d9b8", 13: "f4f6ff", 14: "f7e6cc",
        20: "a88f7a", 21: "e8c9a8", 22: "d0e4f5", 23: "8a7a88",
        30: "d9d4c4", 31: "a39bc2", 32: "d9bf8f", 33: "c4b4b8", 34: "8a8699",
        40: "fbe3c4", 41: "e8978a", 42: "f5b98a", 43: "b8828f", 44: "f2c4c8",
        50: "a3b3e8", 51: "f7f5f0", 52: "7d80a8", 53: "a8dcd9", 54: "c8cfe0",
        60: "d6e8d9", 61: "5e7a80", 62: "f09a7a", 63: "94c9a8", 64: "7cc9b8",
        70: "7a7894", 71: "948a99", 72: "b8b3b0", 73: "3d3552", 74: "b86a5c", 75: "4d4a57",
        80: "fff5e6", 81: "8fc4ff", 82: "ffc97a", 83: "ffa3cf", 84: "8ff0b8",
        85: "fff0d9", 86: "ff7ab3", 87: "a8866e", 88: "8fbf7a", 89: "fff0e0", 90: "d98ac4",
        91: "5c5566", 92: "c4c0cc"})),
    "C": ("Ink & Neon", "Dark, high-contrast rock with bold light bands; vivid keys per biome "
          "(emerald, vermilion, ultramarine, lime) and neon fluids and crystals.", hexes({
        1: "1f8fff", 2: "ff5a00", 3: "ffb000", 4: "0d0a0f", 5: "b6ff1a", 6: "39ff5a", 7: "b3884d",
        10: "1fb35a", 11: "8fd61f", 12: "f2c266", 13: "ffffff", 14: "f5d58f",
        20: "3d2618", 21: "c28a3d", 22: "7fd1ff", 23: "261a14",
        30: "e8e0cc", 31: "1a2433", 32: "d99a2e", 33: "4d4a52", 34: "0f1419",
        40: "ffd9a1", 41: "c7331f", 42: "ff8a1f", 43: "40100f", 44: "ff99a3",
        50: "1f3fa6", 51: "f5f5ff", 52: "0d1026", 53: "1fd1c7", 54: "8a9ec7",
        60: "e6f2cc", 61: "0a1a14", 62: "e0522e", 63: "2e8c4d", 64: "14b38a",
        70: "1a1a2e", 71: "2b2433", 72: "595266", 73: "05030a", 74: "8c1a0a", 75: "000000",
        80: "fff2d9", 81: "33b3ff", 82: "ffb31a", 83: "ff4db3", 84: "33ff99",
        85: "fff5e0", 86: "ff1a8c", 87: "4d2e1a", 88: "1fa64d", 89: "f2e6d9", 90: "b31a8c",
        91: "140f14", 92: "a6a3b3"})),
    # D (round 2): saturated, MID-value naturalism -- the storybook-adventure
    # register. Sits between A's restraint and C's contrast: every material is
    # recognisably what it is (green grass, red sandstone, blue ice), pushed a
    # step richer than life, with light bands kept in the mid range so cliffs
    # read as rock rather than as stripes.
    "D": ("Gouache Frontier", "Rich mid-value naturalism: every material looks like what it is, pushed a step "
          "richer than life. Warm sunlit earths, cobalt and teal in the cold biomes, fluids saturated but not neon.", hexes({
        1: "2f7fb8", 2: "ff7a1f", 3: "e8a52a", 4: "241c14", 5: "b4e02a", 6: "7ad13a", 7: "b89a60",
        10: "5f9e3a", 11: "3fae4a", 12: "e8b86a", 13: "f4f7fb", 14: "ecd29a",
        20: "7a4a2a", 21: "c9914f", 22: "9fd0e8", 23: "5a3a24",
        30: "d8c9a3", 31: "4a5a6e", 32: "c98b2b", 33: "a3928a", 34: "3b3440",
        40: "f0d9a8", 41: "b8452a", 42: "de7a35", 43: "6e2a26", 44: "d99a8a",
        50: "3f5f99", 51: "e6e3dc", 52: "2c3140", 53: "3f9e9a", 54: "a8b0bd",
        60: "c8c7a0", 61: "26302a", 62: "c0502c", 63: "4f7a3a", 64: "2f8f6a",
        70: "4a4a5e", 71: "6b5d52", 72: "7d7670", 73: "1a1424", 74: "7a2a14", 75: "222222",
        80: "efe3c4", 81: "4fb0ff", 82: "ffb23a", 83: "ff6fae", 84: "4fe39a",
        85: "ece0c0", 86: "ff3f7a", 87: "6e4a2e", 88: "3f8f35", 89: "e8dcc6", 90: "c2407a",
        91: "3a3030", 92: "9a958c"})),
}

# B10 (owner Q10 (a), 2026-10-02): savanna and badlands colour CANDIDATES, in D's
# register, for the owner's sign-off. Only D carries the new materials; "a" ships
# (merged into D below). gen-tg2-materials.py --b10 b|c writes another one into
# the engine, so every candidate can be rendered in the real renderer at the
# same poses.
B10_CANDIDATES = {
    "a": ("Golden straw / Terracotta", hexes({
        15: "c9a640", 24: "a4532e", 35: "c2a48c", 36: "6e3424", 37: "7a706a",
        16: "c98a68", 25: "a0644a", 45: "c2573a", 46: "ead3b0", 47: "d4923c", 48: "5e3a28", 49: "5c3644"})),
    "b": ("Olive savanna / Painted hills", hexes({
        15: "a8a24a", 24: "9a4a2c", 35: "b8a58f", 36: "6e3424", 37: "7a706a",
        16: "b8735a", 25: "8c5038", 45: "a83a2a", 46: "e0c9a0", 47: "c9a040", 48: "4a3028", 49: "4e2e3e"})),
    "c": ("Tawny / Pale clay", hexes({
        15: "d39a4a", 24: "b0582f", 35: "c9ab92", 36: "7a3c28", 37: "807670",
        16: "dcc0a0", 25: "b08a68", 45: "b5654a", 46: "f0e2c8", 47: "d8a868", 48: "6e4a36", 49: "6a4652"})),
}
B10_SHIPPED = "a"
PALETTES["D"][2].update(B10_CANDIDATES[B10_SHIPPED][1])

# Gameplay step 3 (task 66, agent's pick, owner may change): ore colours, bright
# against their dark host rock. Copper reads as verdigris (malachite green), iron
# as rust, gold, cobalt blue, emberstone red-orange. NOT emissive (CLAUDE.md:
# emissive materials register as point lights).
ORES = hexes({65: "3faa86", 66: "c08a6a", 67: "eac43c", 68: "4a6ff0", 69: "ff5530"})
PALETTES["D"][2].update(ORES)

ROUND2 = ["A", "C", "D"]
