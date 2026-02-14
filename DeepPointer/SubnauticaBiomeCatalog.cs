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
        private static readonly HashSet<string> LoggedUnknownBiome = new(StringComparer.Ordinal);
        private static readonly HashSet<string> ObservedBiomeRaw = new(StringComparer.Ordinal);
        private static bool _catalogWritten;

        private static readonly BiomeGroup[] Groups =
        {
            new(
                "safe_shallows",
                "Safe Shallows",
                new[] { "safeShallows", "SafeShallows" },
                new[] { "safeShallows" }),
            new(
                "kelp_forest",
                "Kelp Forest",
                new[] { "kelpForest", "KelpForest" },
                new[] { "kelpForest" }),
            new(
                "grassy_plateaus",
                "Grassy Plateaus",
                new[] { "grassyPlateaus", "grassyPlateau", "GrassyPlateaus" },
                new[] { "grassyPlateau", "redgrass" }),
            new(
                "mushroom_forest",
                "Mushroom Forest",
                new[] { "mushroomForest", "MushroomForest" },
                new[] { "mushroomForest" }),
            new(
                "jellyshroom_caves",
                "Jellyshroom Caves",
                new[] { "jellyShroomCaves", "JellyShroomCaves", "jellyshroom", "jellyshroomCave" },
                new[] { "jellyShroom", "jellyshroom" }),
            new(
                "bulb_zone",
                "Bulb Zone",
                new[] { "kooshZone", "KooshZone", "bulbZone", "BulbZone" },
                new[] { "koosh", "bulbZone" }),
            new(
                "mountains",
                "Mountains",
                new[] { "mountains", "Mountains", "mountain" },
                new[] { "mountains", "mountain" }),
            new(
                "mountain_island",
                "Mountain Island",
                new[] { "mountainIsland", "MountainIsland" },
                new[] { "mountainIsland" }),
            new(
                "floating_island",
                "Floating Island",
                new[] { "floatingIsland", "FloatingIsland" },
                new[] { "floatingIsland" }),
            new(
                "underwater_islands",
                "Underwater Islands",
                new[] { "underwaterIslands", "UnderwaterIslands", "islands" },
                new[] { "underwaterIslands", "islands" }),
            new(
                "crash_zone",
                "Crash Zone",
                new[] { "crashZone", "CrashZone", "crashedShip", "crash" },
                new[] { "crashZone", "crashedShip" }),
            new(
                "aurora",
                "Aurora",
                new[] { "aurora", "Aurora", "generatorRoom", "GeneratorRoom", "auroraDriveRoom" },
                new[] { "aurora", "generatorRoom" }),
            new(
                "dunes",
                "Dunes",
                new[] { "dunes", "Dunes" },
                new[] { "dunes" }),
            new(
                "crag_field",
                "Crag Field",
                new[] { "cragField", "CragField" },
                new[] { "cragField" }),
            new(
                "sparse_reef",
                "Sparse Reef",
                new[] { "sparseReef", "SparseReef" },
                new[] { "sparseReef" }),
            new(
                "sea_treaders_path",
                "Sea Treader's Path",
                new[] { "seaTreaderPath", "seaTreaderPath_wreck", "SeaTreaderPath" },
                new[] { "seaTreaderPath" }),
            new(
                "grand_reef",
                "Grand Reef",
                new[] { "grandReef", "GrandReef" },
                new[] { "grandReef" }),
            new(
                "blood_kelp_zone",
                "Blood Kelp Zone",
                new[] { "bloodKelp", "bloodKelpZone", "BloodKelpZone" },
                new[] { "bloodKelpZone", "bloodKelp" }),
            new(
                "blood_kelp_trench",
                "Blood Kelp Trench",
                new[] { "bloodKelpTrench", "BloodKelpTrench" },
                new[] { "bloodKelpTrench" }),
            new(
                "lost_river",
                "Lost River",
                new[] { "lostRiver", "LostRiver", "coveTree", "CoveTree" },
                new[] { "lostRiver", "coveTree" }),
            new(
                "inactive_lava_zone",
                "Inactive Lava Zone",
                new[] { "inactiveLavaZone", "InactiveLavaZone", "ILZCastleChamber", "Precursor_LavaCastleBase" },
                new[] { "inactiveLavaZone", "ilz" }),
            new(
                "lava_lakes",
                "Lava Lakes",
                new[] { "lavaLakes", "LavaLakes" },
                new[] { "lavaLakes" }),
            new(
                "alien_thermal_plant",
                "Alien Thermal Plant",
                new[] { "PrecursorThermalRoom", "precursorThermalRoom", "alienThermalPlant" },
                new[] { "precursorThermal", "alienThermalPlant" }),
            new(
                "disease_research_facility",
                "Disease Research Facility",
                new[] { "Precursor_LostRiverBase", "precursorLostRiverBase", "diseaseResearchFacility" },
                new[] { "precursorLostRiverBase", "diseaseResearchFacility" }),
            new(
                "primary_containment_facility",
                "Primary Containment Facility",
                new[] { "prison", "Prison", "prisonMoonpool", "Prison_Moonpool" },
                new[] { "prison", "precursorPrison" }),
            new(
                "sea_emperor_aquarium",
                "Sea Emperor Aquarium",
                new[] { "Prison_Aquarium_Upper", "Prison_Aquarium", "prisonAquarium" },
                new[] { "prisonAquarium" }),
            new(
                "quarantine_enforcement_platform",
                "Quarantine Enforcement Platform",
                new[] { "Precursor_Gun", "Precursor_Gun_ControlRoom", "quarantineEnforcementPlatform" },
                new[] { "precursorGun", "quarantineEnforcementPlatform" }),
            new(
                "void",
                "Void",
                new[] { "void", "Void", "deadZone", "DeadZone" },
                new[] { "void", "deadZone" })
        };

        static SubnauticaBiomeCatalog()
        {
            foreach (BiomeGroup group in Groups)
            {
                DisplayByCanonical[group.CanonicalKey] = group.DisplayName;

                foreach (string alias in group.ExactAliases)
                {
                    string normalized = Normalize(alias);
                    if (normalized.Length > 0)
                        ExactAliasMap[normalized] = group;
                }

                foreach (string alias in group.PrefixAliases)
                {
                    string normalized = Normalize(alias);
                    if (normalized.Length > 0)
                        PrefixAliasMap.Add((normalized, group));
                }
            }

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
                        ExactAliases = group.ExactAliases.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray(),
                        PrefixAliases = group.PrefixAliases.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray()
                    })
                    .OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
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

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
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
