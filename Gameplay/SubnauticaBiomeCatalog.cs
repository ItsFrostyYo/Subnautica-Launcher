using SubnauticaLauncher.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SubnauticaLauncher.Gameplay
{
    internal static class SubnauticaBiomeCatalog
    {
        internal readonly record struct BiomeMatch(string CanonicalKey, string DisplayName, bool IsKnown);

        private readonly record struct BiomeGroup(
            string CanonicalKey,
            string DisplayName,
            string[] ExactAliases,
            string[] PrefixAliases);

        private static readonly object Sync = new();
        private static readonly Dictionary<string, BiomeGroup> ExactAliasMap = new(StringComparer.Ordinal);
        private static readonly List<(string Prefix, BiomeGroup Group)> PrefixAliasMap = new();
        private static readonly Dictionary<string, string> DisplayByCanonical = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> CanonicalByDisplayName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoggedUnknownBiome = new(StringComparer.Ordinal);
        private static readonly HashSet<string> ObservedBiomeRaw = new(StringComparer.Ordinal);
        private static bool _catalogWritten;

        // IMPORTANT:
        // - Case-sensitive matching is now supported:
        //   Normalize() no longer lowercases, so "safeShallows" and "SafeShallows" are different keys.
        // - Normalize() now preserves '_' so prefix forms like "Foo_" actually work.
        // - We dedupe aliases within each group, and we guard against cross-group alias collisions in the static ctor.
        private static readonly BiomeGroup[] Groups =
        {
            new(
                "safe_shallows",
                "Safe Shallows",
                new[]
                {
                    "safeShallows",
                    "SafeShallows",
                    "Lifepod",
                    "lifepod",
                    "LifePod",
                    "safeShallows_CaveEntrance",
                    "safeShallows_HugeTube",
                },
                new[]
                {
                    "safeShallows",
                    "SafeShallows",
                    "Lifepod",
                    "lifepod",
                    "LifePod",
                    "safeShallows_",
                    "SafeShallows_",
                }),

            new(
                "kelp_forest",
                "Kelp Forest",
                new[]
                {
                    "kelpForest",
                    "KelpForest",
                    "KelpForest_CaveEntrance",
                    "KelpForest_Dense",
                    "KelpForest_DenseVine",
                    "kelpForest_WreckInterior",
                },
                new[]
                {
                    "kelpForest",
                    "KelpForest",
                    "kelpForest_",
                    "KelpForest_",
                }),

            new(
                "grassy_plateaus",
                "Grassy Plateaus",
                new[]
                {
                    "grassyPlateaus",
                    "grassyPlateau",
                    "GrassyPlateaus",
                    "GrassyPlateaus_Tower",
                    "GrassyPlateaus_WreckInterior",
                    "grassyPlateaus_Cave",
                },
                new[]
                {
                    // include both singular/plural and case variants intentionally (case-sensitive)
                    "grassyPlateau",
                    "grassyPlateaus",
                    "GrassyPlateau",
                    "GrassyPlateaus",
                    "grassyPlateau_",
                    "grassyPlateaus_",
                    "GrassyPlateau_",
                    "GrassyPlateaus_",
                    "redgrass",
                    "redgrass_",
                }),

            new(
                "mushroom_forest",
                "Mushroom Forest",
                new[]
                {
                    "mushroomForest",
                    "MushroomForest",
                    "PrecursorCave_MushroomForest",
                    "mushroomForest_cave_dark",
                    "mushroomForest_cave_light",
                    "mushroomForest_wreck",
                },
                new[]
                {
                    "mushroomForest",
                    "MushroomForest",
                    "mushroomForest_",
                    "MushroomForest_",
                    "PrecursorCave_MushroomForest",
                    "PrecursorCave_MushroomForest_",
                }),

            new(
                "jellyshroom_caves",
                "Jellyshroom Caves",
                new[]
                {
                    "jellyShroomCaves",
                    "JellyShroomCaves",
                    "jellyshroom",
                    "jellyshroomCave",
                    "JellyshroomCaves",
                    "JellyshroomCaves_Geyser",
                },
                new[]
                {
                    "jellyShroom",
                    "JellyShroom",
                    "jellyshroom",
                    "Jellyshroom",
                    "jellyShroom_",
                    "JellyShroom_",
                    "jellyshroom_",
                    "Jellyshroom_",
                }),

/* ----------------------------- MID BIOMES ----------------------------- */

            new(
                "bulb_zone",
                "Bulb Zone",
                new[]
                {
                    "kooshZone",
                    "KooshZone",
                    "bulbZone",
                    "BulbZone",
                    "KooshZone_Geyser",
                    "PrecursorCave_KooshZone",
                    "kooshZone_cave_dark",
                    "kooshZone_cave_trans",
                    "kooshZone_wreck",
                },
                new[]
                {
                    "kooshZone",
                    "KooshZone",
                    "bulbZone",
                    "BulbZone",
                    "kooshZone_",
                    "KooshZone_",
                    "bulbZone_",
                    "BulbZone_",
                    "koosh",
                    "Koosh",
                }),

            new(
                "mountains",
                "Mountains",
                new[]
                {
                    "mountains",
                    "Mountains",
                    "mountain",
                    "Mountains_Cave",
                    "Mountains_CaveEntrance",
                    "Mountains_IslandSurface_Cave",
                    "Mountains_IslandSurface_CaveEntrance",
                    "Mountains_Island_Cave",
                    "Mountains_Island_CaveEntrance",
                    "Mountains_Island_Teleporter",
                    "Mountains_ThermalVent",
                    "mountains_wreckinterior",
                },
                new[]
                {
                    "mountains",
                    "Mountains",
                    "mountain",
                    "Mountain",
                    "mountains_",
                    "Mountains_",
                    "mountain_",
                    "Mountain_",
                }),

            new(
                "floating_island",
                "Floating Island",
                new[]
                {
                    "floatingIsland",
                    "FloatingIsland",
                    "FloatingIslandBelow",
                    "FloatingIslandCaveTeleporter",
                },
                new[]
                {
                    "floatingIsland",
                    "FloatingIsland",
                    "floatingIsland_",
                    "FloatingIsland_",
                }),

            new(
                "underwater_islands",
                "Underwater Islands",
                new[]
                {
                    "underwaterIslands",
                    "UnderwaterIslands",
                    "islands",
                    "UnderwaterIslands_Island",
                    "UnderwaterIslands_ValleyFloor",
                    "underwaterIslands_Cave",
                    "underwaterIslands_Geyser",
                    "underwaterIslands_IslandCave",
                    "underwaterIslands_wreck",
                },
                new[]
                {
                    "underwaterIslands",
                    "UnderwaterIslands",
                    "underwaterIslands_",
                    "UnderwaterIslands_",
                    "islands",
                    "Islands",
                    "islands_",
                    "Islands_",
                }),

/* ----------------------------- AURORA / CRASH ----------------------------- */

            new(
                "crash_zone",
                "Crash Zone",
                new[]
                {
                    "crashZone",
                    "CrashZone",
                    "crash",
                    "CrashZone_Mesa",
                    "CrashZone_NoLoot",
                    "CrashZone_Trench",
                },
                new[]
                {
                    "crashZone",
                    "CrashZone",
                    "crashZone_",
                    "CrashZone_",
                    "crash",
                    "crash_",
                }),

            new(
                "aurora",
                "Aurora",
                new[]
                {
                    "aurora",
                    "Aurora",
                    "generatorRoom",
                    "GeneratorRoom",
                    "auroraDriveRoom",

                    "CrashedShip",
                    "crashedShip",

                    "CrashedShip_Interior_Cargo",
                    "CrashedShip_Interior_Dark",
                    "CrashedShip_Interior_Elevator",
                    "CrashedShip_Interior_Entrance_01_01",
                    "CrashedShip_Interior_Entrance_01_02",
                    "CrashedShip_Interior_Entrance_01_03",
                    "CrashedShip_Interior_Entrance_02_01",
                    "CrashedShip_Interior_Entrance_02_02",
                    "CrashedShip_Interior_Entrance_03",
                    "CrashedShip_Interior_Exo",
                    "CrashedShip_Interior_ExoPipes",
                    "CrashedShip_Interior_LivingArea",
                    "CrashedShip_Interior_Locker",
                    "CrashedShip_Interior_LockerCorridor",
                    "CrashedShip_Interior_Power",
                    "CrashedShip_Interior_PowerCorridor",
                    "CrashedShip_Interior_SeamothRoom",
                    "CrashedShip_Interior_THallway",
                    "CrashedShip_Interior_THallwayLower",
                },
                new[]
                {
                    "aurora",
                    "Aurora",
                    "aurora_",
                    "Aurora_",
                    "generatorRoom",
                    "GeneratorRoom",
                    "generatorRoom_",
                    "GeneratorRoom_",
                    "CrashedShip",
                    "crashedShip",
                    "CrashedShip_",
                    "crashedShip_",
                    "auroraDriveRoom",
                    "auroraDriveRoom_",
                }),

/* ----------------------------- OPEN OCEAN BIOMES ----------------------------- */

            new(
                "dunes",
                "Dunes",
                new[]
                {
                    "dunes",
                    "Dunes",
                    "Dunes_Cave_Dark",
                    "Dunes_Cave_light",
                    "Dunes_ThermalVents",
                    "Dunes_wreck",
                },
                new[]
                {
                    "dunes",
                    "Dunes",
                    "dunes_",
                    "Dunes_",
                }),

            new(
                "crag_field",
                "Crag Field",
                new[]
                {
                    "cragField",
                    "CragField",
                    "PrecursorCave_CragField",
                },
                new[]
                {
                    "cragField",
                    "CragField",
                    "cragField_",
                    "CragField_",
                    "PrecursorCave_CragField",
                    "PrecursorCave_CragField_",
                }),

            new(
                "sparse_reef",
                "Sparse Reef",
                new[]
                {
                    "sparseReef",
                    "SparseReef",
                    "SparseReef_Deep",
                    "SparseReef_Spike",
                    "sparseReef_Wreck",
                },
                new[]
                {
                    "sparseReef",
                    "SparseReef",
                    "sparseReef_",
                    "SparseReef_",
                }),

            new(
                "sea_treaders_path",
                "Sea Treader's Path",
                new[]
                {
                    "seaTreaderPath",
                    "SeaTreaderPath",
                    "seaTreaderPath_Cave_dark",
                    "seaTreaderPath_Cave_light",
                    "seaTreaderPath_wreck",
                },
                new[]
                {
                    "seaTreaderPath",
                    "SeaTreaderPath",
                    "seaTreaderPath_",
                    "SeaTreaderPath_",
                }),

            new(
                "grand_reef",
                "Grand Reef",
                new[]
                {
                    "grandReef",
                    "GrandReef",
                    "DeepGrandReef",
                    "deepGrandReef",
                    "GrandReef_ThermalVent",
                    "grandReef_wreck",
                },
                new[]
                {
                    "grandReef",
                    "GrandReef",
                    "DeepGrandReef",
                    "deepGrandReef",
                    "grandReef_",
                    "GrandReef_",
                    "DeepGrandReef_",
                    "deepGrandReef_",
                }),

/* ----------------------------- BLOOD KELP ----------------------------- */

            new(
                "blood_kelp_zone",
                "Blood Kelp Zone",
                new[]
                {
                    "bloodKelp",
                    "BloodKelp",
                    "bloodKelpZone",
                    "BloodKelpZone",
                    "BloodKelp_Cave",
                    "bloodKelpTwo",
                },
                new[]
                {
                    "bloodKelp",
                    "BloodKelp",
                    "bloodKelpZone",
                    "BloodKelpZone",
                    "bloodKelp_",
                    "BloodKelp_",
                    "bloodKelpZone_",
                    "BloodKelpZone_",
                }),

            new(
                "blood_kelp_trench",
                "Blood Kelp Trench",
                new[]
                {
                    "bloodKelpTrench",
                    "BloodKelpTrench",
                    "BloodKelp_DeepTrench",
                    "BloodKelp_Trench",
                    "bloodKelp_wreck",
                },
                new[]
                {
                    "bloodKelpTrench",
                    "BloodKelpTrench",
                    "bloodKelpTrench_",
                    "BloodKelpTrench_",
                    "BloodKelp_Trench",
                    "BloodKelp_Trench_",
                }),

/* ----------------------------- LOST RIVER / LAVA ----------------------------- */

            new(
                "lost_river",
                "Lost River",
                new[]
                {
                    "lostRiver",
                    "LostRiver",
                    "coveTree",
                    "CoveTree",

                    "LostRiver_BonesField",
                    "LostRiver_BonesField_Cave",
                    "LostRiver_BonesField_Corridor",
                    "LostRiver_BonesField_Corridor_Stream",
                    "LostRiver_BonesField_Lake",
                    "LostRiver_BonesField_LakePit",
                    "LostRiver_BonesField_Ledge",
                    "LostRiver_BonesField_Skeleton",
                    "LostRiver_BonesField_ThermalVent",

                    "LostRiver_Canyon",
                    "LostRiver_Corridor",
                    "LostRiver_Corridor_ThermalVents",

                    "LostRiver_GhostTree",
                    "LostRiver_GhostTree_Lake",
                    "LostRiver_GhostTree_Lower",
                    "LostRiver_GhostTree_Skeleton",

                    "LostRiver_Junction",
                    "LostRiver_Junction_ThermalVent",
                    "LostRiver_Junction_Water",

                    "LostRiver_SkeletonCave",
                    "LostRiver_SkeletonCave_Skeleton",

                    "LostRiver_TreeCove",
                    "LostRiver_TreeCove_Tree",
                    "LostRiver_TreeCove_Water",

                    "PrecursorCave_GhostTree",
                },
                new[]
                {
                    "lostRiver",
                    "LostRiver",
                    "coveTree",
                    "CoveTree",
                    "lostRiver_",
                    "LostRiver_",
                    "coveTree_",
                    "CoveTree_",
                    "PrecursorCave_GhostTree",
                    "PrecursorCave_GhostTree_",
                }),

            new(
                "inactive_lava_zone",
                "Inactive Lava Zone",
                new[]
                {
                    "inactiveLavaZone",
                    "InactiveLavaZone",
                    "ilzLava",

                    "ILZCastleChamber",
                    "ILZCastleTunnel",
                    "ILZChamber",
                    "ILZChamberEntrance",
                    "ILZChamber_Dragon",
                    "ILZChamber_MagmaBubble",
                    "ILZChamber_MagmaTree",
                    "ILZCorridor",                    
                },
                new[]
                {
                    "inactiveLavaZone",
                    "InactiveLavaZone",
                    "inactiveLavaZone_",
                    "InactiveLavaZone_",

                    "ILZ",
                    "ilz",
                    "ILZ_",
                    "ilz_",

                    "Precursor_LavaCastleBase",
                    "Precursor_LavaCastleBase_",
                }),

            new(
                "lava_lakes",
                "Active Lava Zone",
                new[]
                {
                    "lavaLakes",
                    "LavaLakes",
                    "ALZChamber",
                    "ALZFalls",
                    "LavaPit",
                    "LavaFalls",
                    "lavaFalls",
                    "LavaLakes_LavaPool",
                },
                new[]
                {
                    "lavaLakes",
                    "LavaLakes",
                    "lavaLakes_",
                    "LavaLakes_",
                    "ALZ",
                    "ALZ_",
                    "LavaFalls",
                    "lavaFalls",
                    "LavaFalls_",
                    "lavaFalls_",
                }),

/* ----------------------------- ALIEN BASES ----------------------------- */

            new(
                "alien_thermal_plant",
                "Alien Thermal Plant",
                new[]
                {
                    "alienThermalPlant",
                    "PrecursorThermalRoom",
                    "precursorThermalRoom",
                    "Precursor_LavaCastleBase",
                },
                new[]
                {
                    "alienThermalPlant",
                    "Precursor_LavaCastleBase",
                    "Precursor_LavaCastleBase_",
                    "alienThermalPlant_",
                    "PrecursorThermal",
                    "PrecursorThermal_",
                    "precursorThermal",
                    "precursorThermal_",
                }),

            new(
                "disease_research_facility",
                "Disease Research Facility",
                new[]
                {
                    "diseaseResearchFacility",
                    "Precursor_LostRiverBase",
                    "precursorLostRiverBase",
                },
                new[]
                {
                    "diseaseResearchFacility",
                    "diseaseResearchFacility_",
                    "Precursor_LostRiverBase",
                    "Precursor_LostRiverBase_",
                    "precursorLostRiverBase",
                    "precursorLostRiverBase_",
                }),

            new(
                "primary_containment_facility",
                "Primary Containment Facility",
                new[]
                {
                    "prison",
                    "Prison",
                    "prisonMoonpool",
                    "Prison_Moonpool",
                    "EmperorFacility",
                },
                new[]
                {
                    "prison",
                    "Prison",
                    "prison_",
                    "Prison_",
                    "precursorPrison",
                    "precursorPrison_",
                    "EmperorFacility",
                    "EmperorFacility_",
                }),

            new(
                "sea_emperor_aquarium",
                "Aquarium",
                new[]
                {
                    "prisonAquarium",
                    "Prison_Aquarium",
                    "Prison_Aquarium_Upper",
                    "Prison_Aquarium_Cave",
                    "Prison_Aquarium_Mid",
                    "Prison_Antechamber",
                    "Prison_UpperRoom",
                },
                new[]
                {
                    "prisonAquarium",
                    "Prison_Aquarium",
                    "Prison_Aquarium_",
                    "prisonAquarium_",
                }),

            new(
                "quarantine_enforcement_platform",
                "Quarantine Enforcement Platform",
                new[]
                {
                    "quarantineEnforcementPlatform",
                    "PrecursorGun",
                    "Precursor_Gun",
                    "Precursor_Gun_ControlRoom",
                    "Precursor_Gun_InnerRooms",
                    "Precursor_Gun_MoonPoolWater",
                    "Precursor_Gun_NoLoot",
                    "Precursor_Gun_OuterRooms",
                },
                new[]
                {
                    "quarantineEnforcementPlatform",
                    "quarantineEnforcementPlatform_",
                    "PrecursorGun",
                    "PrecursorGun_",
                    "precursorGun",
                    "precursorGun_",
                    "Precursor_Gun",
                    "Precursor_Gun_",
                }),

/* ----------------------------- VOID ----------------------------- */

            new(
                "void",
                "Void",
                new[] { "void", "Void", "deadZone", "DeadZone" },
                new[] { "void", "Void", "void_", "Void_", "deadZone", "DeadZone", "deadZone_", "DeadZone_" }),
        };

        static SubnauticaBiomeCatalog()
        {
            foreach (BiomeGroup group in Groups)
            {
                DisplayByCanonical[group.CanonicalKey] = group.DisplayName;
                if (!CanonicalByDisplayName.ContainsKey(group.DisplayName))
                    CanonicalByDisplayName[group.DisplayName] = group.CanonicalKey;
                else
                    Logger.Warn($"[BiomeMap] Duplicate display name mapping: '{group.DisplayName}'.");

                // Dedupe within group (case-sensitive, ordinal)
                var exactUnique = new HashSet<string>(StringComparer.Ordinal);
                foreach (string alias in group.ExactAliases ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;

                    if (!exactUnique.Add(alias))
                        continue;

                    string normalized = Normalize(alias);
                    if (normalized.Length == 0)
                        continue;

                    // Guard against cross-group collisions
                    if (ExactAliasMap.TryGetValue(normalized, out var existing) &&
                        !string.Equals(existing.CanonicalKey, group.CanonicalKey, StringComparison.Ordinal))
                    {
                        Logger.Warn($"[BiomeMap] Exact alias collision: '{alias}' maps to '{existing.CanonicalKey}' and '{group.CanonicalKey}'. Keeping '{existing.CanonicalKey}'.");
                        continue;
                    }

                    ExactAliasMap[normalized] = group;
                }

                var prefixUnique = new HashSet<string>(StringComparer.Ordinal);
                foreach (string alias in group.PrefixAliases ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;

                    if (!prefixUnique.Add(alias))
                        continue;

                    string normalized = Normalize(alias);
                    if (normalized.Length == 0)
                        continue;

                    // Guard against cross-group collisions (same normalized prefix)
                    var existingPrefix = PrefixAliasMap.FirstOrDefault(p => p.Prefix == normalized);
                    if (existingPrefix.Prefix != null &&
                        !string.Equals(existingPrefix.Group.CanonicalKey, group.CanonicalKey, StringComparison.Ordinal))
                    {
                        Logger.Warn($"[BiomeMap] Prefix alias collision: '{alias}' maps to '{existingPrefix.Group.CanonicalKey}' and '{group.CanonicalKey}'. Keeping '{existingPrefix.Group.CanonicalKey}'.");
                        continue;
                    }

                    PrefixAliasMap.Add((normalized, group));
                }
            }

            // Longest-prefix wins
            PrefixAliasMap.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        }

        public static void EnsureCatalogWritten()
        {
            lock (Sync)
            {
                if (_catalogWritten)
                    return;

                _catalogWritten = true;
            }

            try
            {
                Directory.CreateDirectory(AppPaths.DataPath);
                string file = Path.Combine(AppPaths.DataPath, "biome_catalog.json");

                var payload = Groups
                    .Select(group => new
                    {
                        group.CanonicalKey,
                        group.DisplayName,
                        // Keep stable ordering but DO NOT ignore case (since case is meaningful)
                        ExactAliases = group.ExactAliases.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                        PrefixAliases = group.PrefixAliases.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray()
                    })
                    .OrderBy(g => g.DisplayName, StringComparer.Ordinal)
                    .ToArray();

                string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(file, json);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[BiomeMap] Failed to write biome catalog.");
            }
        }

        public static BiomeMatch Resolve(string rawBiome)
        {
            if (string.IsNullOrWhiteSpace(rawBiome))
                return new BiomeMatch("unknown", "Unknown", false);

            string normalized = Normalize(rawBiome);
            if (normalized.Length == 0)
                return new BiomeMatch("unknown", "Unknown", false);

            if (ExactAliasMap.TryGetValue(normalized, out BiomeGroup exactGroup))
                return new BiomeMatch(exactGroup.CanonicalKey, exactGroup.DisplayName, true);

            foreach ((string prefix, BiomeGroup group) in PrefixAliasMap)
            {
                if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                    return new BiomeMatch(group.CanonicalKey, group.DisplayName, true);
            }

            string canonical = "raw_" + normalized;
            string display = Humanize(rawBiome);

            lock (Sync)
            {
                if (!DisplayByCanonical.ContainsKey(canonical))
                    DisplayByCanonical[canonical] = display;
            }

            return new BiomeMatch(canonical, display, false);
        }

        public static BiomeMatch Resolve(string rawBiome, float? playerY)
        {
            BiomeMatch match = Resolve(rawBiome);
            if (string.IsNullOrWhiteSpace(rawBiome))
                return match;

            string normalized = Normalize(rawBiome);
            if (normalized.Length == 0)
                return match;

            if (IsUnqualifiedInteriorBiome(normalized))
                return new BiomeMatch("unknown", "Unknown", false);

            if (playerY.HasValue &&
                playerY.Value <= -1000f &&
                (normalized.StartsWith("Precursor_Gun", StringComparison.Ordinal) ||
                 normalized.StartsWith("PrecursorGun", StringComparison.Ordinal) ||
                 normalized.StartsWith("precursorGun", StringComparison.Ordinal)))
            {
                return new BiomeMatch("alien_thermal_plant", GetDisplayName("alien_thermal_plant"), true);
            }

            return match;
        }

        public static string GetDisplayName(string canonicalKey)
        {
            if (string.IsNullOrWhiteSpace(canonicalKey))
                return "Unknown";

            lock (Sync)
            {
                if (DisplayByCanonical.TryGetValue(canonicalKey, out string? value))
                    return value;
            }

            return Humanize(canonicalKey);
        }

        public static bool TryGetCanonicalByDisplayName(string displayName, out string canonicalKey)
        {
            canonicalKey = string.Empty;
            if (string.IsNullOrWhiteSpace(displayName))
                return false;

            lock (Sync)
            {
                if (CanonicalByDisplayName.TryGetValue(displayName.Trim(), out string? mapped))
                {
                    canonicalKey = mapped;
                    return true;
                }
            }

            return false;
        }

        public static void RegisterObserved(string rawBiome, BiomeMatch match)
        {
            if (string.IsNullOrWhiteSpace(rawBiome))
                return;

            string normalizedRaw = Normalize(rawBiome);
            if (normalizedRaw.Length == 0)
                return;

            bool isFirstObservation;
            lock (Sync)
            {
                isFirstObservation = ObservedBiomeRaw.Add(normalizedRaw);
            }

            if (!isFirstObservation)
                return;

            try
            {
                Directory.CreateDirectory(AppPaths.DataPath);
                string file = Path.Combine(AppPaths.DataPath, "biomes_observed.jsonl");
                string line = JsonSerializer.Serialize(new
                {
                    TimestampUtc = DateTime.UtcNow,
                    RawBiome = rawBiome,
                    NormalizedBiome = normalizedRaw,
                    match.CanonicalKey,
                    match.DisplayName,
                    match.IsKnown
                });
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[BiomeMap] Failed to append observed biome.");
            }

            if (!match.IsKnown)
            {
                lock (Sync)
                {
                    if (!LoggedUnknownBiome.Add(normalizedRaw))
                        return;
                }

                Logger.Warn($"[BiomeMap] Unmapped biome encountered: raw={rawBiome}");
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // Case-sensitive + underscore-preserving normalization:
            // - keep letters/digits/underscore
            // - do NOT lowercase
            // - trim whitespace implicitly by skipping it
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private static bool IsUnqualifiedInteriorBiome(string normalized)
        {
            return string.Equals(normalized, "WreckInterior", StringComparison.Ordinal)
                || string.Equals(normalized, "wreckInterior", StringComparison.Ordinal)
                || string.Equals(normalized, "Wreck", StringComparison.Ordinal)
                || string.Equals(normalized, "wreck", StringComparison.Ordinal);
        }

        private static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var cleaned = value.Replace('_', ' ').Replace('-', ' ').Trim();
            if (cleaned.Length == 0)
                return "Unknown";

            var builder = new StringBuilder(cleaned.Length + 8);
            char previous = '\0';

            foreach (char current in cleaned)
            {
                if (builder.Length > 0)
                {
                    bool addSpace =
                        (char.IsLower(previous) && char.IsUpper(current))
                        || (char.IsLetter(previous) && char.IsDigit(current))
                        || (char.IsDigit(previous) && char.IsLetter(current));

                    if (addSpace)
                        builder.Append(' ');
                }

                builder.Append(current);
                previous = current;
            }

            return builder.ToString();
        }
    }
}
