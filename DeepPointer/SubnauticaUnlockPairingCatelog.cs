using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SubnauticaLauncher.Gameplay
{
    internal sealed partial class SubnauticaUnlockPairingCatalog
    {
        internal sealed record BiomeUnlockGroup(
            string DisplayName,
            string CanonicalBiomeKey,
            IReadOnlyList<string> DatabankEntries,
            IReadOnlyList<string> BlueprintEntries);

        internal readonly record struct BiomeCycleEntries(
            IReadOnlyList<string> DatabankEntries,
            IReadOnlyList<string> BlueprintEntries);

        // Community-editable cycle overrides for the Biome Tracker.
        // Keep entries empty to fall back to the fully accurate BiomeUnlockGroup list.
        // Fill any biome here to customize what appears in cycle mode.
        private static readonly Dictionary<string, BiomeCycleEntries> CycleOverridesByBiomeDisplayName =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Safe Shallows"] = new(
                    new[]
                    {
                "4546B Environment Scan",
                "Bladderfish",
                "Boomerang",
                "Floater",
                "Garryfish",
                "Gasopod",
                "Holefish",
                "Peeper",
                "Rabbit Ray",
                "Crashfish",
                "Shuttlebug",
                "Acid Mushroom",
                "Blue Palm",
                "Veined Nettle",
                "Writhing Weed",
                "Sulfur Plant",
                "Brain Coral",
                "Coral Shell Plate",
                "Giant Coral Tubes",
                "Table Coral",
                "Alien Eggs",
                "Scattered Wreckage",
                "Degasi Crew Manifest: Paul Torgal",
                "Limestone Outcrops",
                "Sandstone Outcrops",
                    },
                    new[]
                    {
                "Trash Can",
                "Grav Trap",
                "Beacon",
                "Mobile Vehicle Bay",
                "Seaglide",
                "Stasis Rifle",
                "Air Bladder",
                "Filtered Water",
                "Cooked + Cured Holefish",
                "Cooked + Cured Peeper",
                "Cooked + Cured Bladderfish",
                "Cooked + Cured Garryfish",
                "Cooked + Cured Boomerang",
                "Power Cell",
                "Disinfected Water",
                "High Capacity O2 Tank",
                "Rebreather",
                    }),
                ["Kelp Forest"] = new(
                    new[]
                    {
                "Bioreactor",
                "Stasis Rifle",
                "Alien Eggs",
                "Floodlight",
                "Brain Coral",
                "Creepvine",
                "Drooping Stinger",
                "Eye Stalk",
                "Sulfur Plant",
                "Crashfish",
                "Boomerang",
                "Eyeye",
                "Hoopfish",
                "Hoverfish",
                "Peeper",
                "Rabbit Ray",
                "Stalker Tooth",
                "Stalker",
                "Mesmer",
                "Shuttlebug",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Limestone Outcrops",
                "Lifepod 3 Crew Log",
                "Degasi Crew Manifest: Bart Torgal",
                "Creepvine Seeds"
                    },
                    new[]
                    {
                "Swivel Chair",
                "Grav Trap",
                "Desk",
                "Bench",
                "Bioreactor",
                "Floodlight",
                "Mobile Vehicle Bay",
                "Seaglide",
                "Stasis Rifle",
                "Compass",
                "Fiber Mesh",
                "Lubricant",
                "Enameled Glass",
                "Cooked + Cured Peeper",
                "Cooked + Cured Hoverfish",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Eyeye",
                "Cooked + Cured Hoopfish",
                "Pathfinder Tool",
                    }),
                ["Grassy Plateaus"] = new(
                    new[]
                    {
                "Bioreactor",
                "Scanner Room",
                "Modification Station",
                "Alien Eggs",
                "Floodlight",
                "Planters & Pots",
                "Seamoth",
                "Brain Coral",
                "Coral Shell Plate",
                "Giant Coral Tubes",
                "Table Coral",
                "Acid Mushroom",
                "Furled Papyrus",
                "Drooping Stinger",
                "Redwort",
                "Tiger Plant",
                "Veined Nettle",
                "Violet Beau",
                "Writhing Weed",
                "Regress Shell",
                "Rouge Cradle",
                "Biter",
                "Boomerang",
                "Floater",
                "Hoopfish",
                "Peeper",
                "Reefback Leviathan",
                "Reginald",
                "Rockgrub",
                "Sand Shark",
                "Shuttlebug",
                "Spadefish",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Limestone Outcrops",
                "Lifepod 17 Crew Log",
                "Lifepod 6 Crew Log #1",
                "Lifepod 6 Crew Log #2",
                "Lifepod 6 Transmission Origin",
                "Degasi Crew Manifest: Marguerit Maida",
                "Propulsion Cannon",
                "Alterra Citizen Testimonials",
                "Trans-Gov Profile: Mongolian Independent States",
                    },
                    new[]
                    {
                "Nuclear Waste Disposal",
                "Trash Can",
                "Command Chair",
                "Swivel Chair",
                "Desk",
                "Bench",
                "Plant Shelf",
                "Chic Plant Pot",
                "Composite Plant Pot",
                "Basic Plant Pot",
                "Modification Station",
                "Battery Charger",
                "Bioreactor",
                "Floodlight",
                "Scanner Room",
                "Vehicle Upgrade Console",
                "Ultra Glide Fins",
                "Lightweight High Capacity Tank",
                "Seamoth",
                "Mobile Vehicle Bay",
                "Propulsion Cannon",
                "Laser Cutter",
                "Cooked + Cured Spadefish",
                "Plasteel Ingot",
                "Cooked + Cured Peeper",
                "Cooked + Cured Reginald",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Hoopfish",
                    }),
                ["Mushroom Forest"] = new(
                    new[]
                    {
                "Planters & Pots",
                "Modification Station",
                "Floodlight",
                "Light Stick",
                "Moonpool",
                "Cyclops",
                "Tree Mushrooms",
                "Brain Coral",
                "Coral Shell Plate",
                "Acid Mushroom",
                "Pygmy Fan",
                "Tree Leech",
                "Veined Nettle",
                "Writhing Weed",
                "Boneshark",
                "Holefish",
                "Jellyray",
                "Mesmer",
                "Peeper",
                "Reefback Leviathan",
                "Shuttlebug",
                "Spadefish",
                "Alien Eggs",
                "Shale Outcrops",
                "Limestone Outcrops",
                "Alien Arch",
                "Alien Vent",
                "Aurora Scanner Room Voice Log",
                "Lifepod 13 Emissary's Voicelog",            
                    },
                    new[]
                    {
                "Command Chair",
                "Office Chair",
                "Desk",
                "Bench",
                "Cyclops Bridge",
                "Cyclops Hull",
                "Plant Shelf",
                "Composite Plant Pot",
                "Modification Station",
                "Power Cell Charger",
                "Floodlight",
                "Power Transmitter",
                "Moonpool",
                "Cyclops",
                "Vehicle Upgrade Console",
                "Ultra Glide Fins",
                "Light Stick",
                "Cooked + Cured Peeper",
                "Cooked + Cured Spadefish",
                    }),
                ["Jellyshroom Caves"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Bulb Zone"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Mountains"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Floating Island"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Underwater Islands"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Crash Zone"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Aurora"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Dunes"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Crag Field"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Sparse Reef"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Sea Treader's Path"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Grand Reef"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Blood Kelp Zone"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Blood Kelp Trench"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Lost River"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Inactive Lava Zone"] = new(new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Active Lava Zone"] = new(new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Alien Thermal Plant"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Disease Research Facility"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Primary Containment Facility"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Aquarium"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Quarantine Enforcement Platform"] = new(
                    new[]
                    {
                        "",
                    },
                    new[]
                    {
                        "",
                    }),
                ["Void"] = new(Array.Empty<string>(), Array.Empty<string>()),
            };

        private static readonly object Sync = new();
        private static SubnauticaUnlockPairingCatalog? _cached;
        private static bool _catalogWritten;

        private readonly List<BiomeUnlockGroup> _groups;

        private SubnauticaUnlockPairingCatalog(List<BiomeUnlockGroup> groups)
        {
            _groups = groups;
        }

        public IReadOnlyList<BiomeUnlockGroup> Groups => _groups;

        public static SubnauticaUnlockPairingCatalog GetOrLoad()
        {
            lock (Sync)
            {
                _cached ??= BuildDefaultCatalog();
                return _cached;
            }
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
                SubnauticaUnlockPairingCatalog catalog = GetOrLoad();
                Directory.CreateDirectory(AppPaths.DataPath);
                string file = Path.Combine(AppPaths.DataPath, "biome_unlock_pairing_catalog.json");

                var payload = catalog.Groups
                    .OrderBy(group => group.DisplayName, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        group.DisplayName,
                        group.CanonicalBiomeKey,
                        DatabankEntries = group.DatabankEntries,
                        BlueprintEntries = group.BlueprintEntries
                    })
                    .ToArray();

                string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(file, json);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[BiomePairing] Failed to write biome unlock pairing catalog.");
            }
        }

        public static BiomeCycleEntries GetCycleEntries(BiomeUnlockGroup group)
        {
            if (CycleOverridesByBiomeDisplayName.TryGetValue(group.DisplayName, out BiomeCycleEntries overrides))
            {
                IReadOnlyList<string> databanks = overrides.DatabankEntries.Count > 0
                    ? overrides.DatabankEntries
                    : group.DatabankEntries;

                IReadOnlyList<string> blueprints = overrides.BlueprintEntries.Count > 0
                    ? overrides.BlueprintEntries
                    : group.BlueprintEntries;

                return new BiomeCycleEntries(databanks, blueprints);
            }

            return new BiomeCycleEntries(group.DatabankEntries, group.BlueprintEntries);
        }
    }
}
namespace SubnauticaLauncher.Gameplay
{
    internal sealed partial class SubnauticaUnlockPairingCatalog
    {
        private static SubnauticaUnlockPairingCatalog BuildDefaultCatalog()
        {
            var groups = new List<BiomeUnlockGroup>();

            AddGroup(groups, "Safe Shallows", new string[]
            {
                "Stasis Rifle",
                "4546B Environment Scan",
                "Bladderfish",
                "Boomerang",
                "Floater",
                "Garryfish",
                "Gasopod",
                "Holefish",
                "Peeper",
                "Rabbit Ray",
                "Skyray",
                "Crashfish",
                "Shuttlebug",
                "Acid Mushroom",
                "Blue Palm",               
                "Veined Nettle",
                "Writhing Weed",
                "Sulfur Plant",
                "Brain Coral",
                "Coral Shell Plate",
                "Giant Coral Tubes",
                "Table Coral",
                "Alien Eggs",
                "Scattered Wreckage",
                "Degasi Crew Manifest: Paul Torgal",
                "Limestone Outcrops",
                "Sandstone Outcrops",
                "Laser Cutter",
                "Radiation Suit",
            }, new string[]
            {
                "Counter",
                "Coffee Vending Machine",
                "Vending Machine",
                "Trash Can",
                "Command Chair",
                "Office Chair",
                "Desk",
                "Grav Trap",
                "Beacon",
                "Mobile Vehicle Bay",
                "Seaglide",
                "Stasis Rifle",
                "Air Bladder",
                "Filtered Water",
                "Cooked + Cured Holefish",
                "Cooked + Cured Peeper",
                "Cooked + Cured Bladderfish",
                "Cooked + Cured Garryfish",
                "Cooked + Cured Boomerang",
                "Power Cell",
                "Disinfected Water",
                "High Capacity O2 Tank",
                "Rebreather",
                "Radiation Suit",                
            });

            AddGroup(groups, "Kelp Forest", new string[]
            {
                "Bioreactor",
                "Stasis Rifle",
                "Alien Eggs",
                "Floodlight",
                "Brain Coral",
                "Giant Coral Tubes",
                "Creepvine",
                "Drooping Stinger",
                "Eye Stalk",
                "Tree Leech",
                "Sulfur Plant",
                "Bleeder",
                "Crashfish",
                "Boomerang",
                "Eyeye",
                "Hoopfish",
                "Hoverfish",
                "Peeper",
                "Rabbit Ray",
                "Stalker Tooth",
                "Stalker",
                "Mesmer",
                "Shuttlebug",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Limestone Outcrops",
                "Lifepod 3 Crew Log",
                "Degasi Crew Manifest: Bart Torgal",
                "Creepvine Seeds"
            }, new string[]
            {
                "Swivel Chair",
                "Grav Trap",
                "Desk",
                "Bench",
                "Bioreactor",
                "Floodlight",
                "Mobile Vehicle Bay",
                "Seaglide",
                "Stasis Rifle",
                "Compass",
                "Fiber Mesh",
                "Lubricant",
                "Enameled Glass",
                "Cooked + Cured Peeper",
                "Cooked + Cured Hoverfish",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Eyeye",
                "Cooked + Cured Hoopfish",
                "Pathfinder Tool",
            });

            AddGroup(groups, "Grassy Plateaus", new string[]
            {
                "Bioreactor",
                "Scanner Room",
                "Modification Station",
                "Light Stick",
                "Alien Eggs",
                "Floodlight",
                "Planters & Pots",
                "Seamoth",
                "Brain Coral",
                "Coral Shell Plate",
                "Giant Coral Tubes",
                "Table Coral",
                "Acid Mushroom",
                "Furled Papyrus",
                "Drooping Stinger",
                "Redwort",
                "Tiger Plant",
                "Tree Leech",
                "Veined Nettle",
                "Violet Beau",
                "Writhing Weed",
                "Regress Shell",
                "Rouge Cradle",
                "Sea Crown",
                "Biter",
                "Boomerang",
                "Floater",
                "Hoopfish",
                "Peeper",
                "Reefback Leviathan",
                "Reginald",
                "Rockgrub",
                "Sand Shark",
                "Shuttlebug",
                "Spadefish",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Limestone Outcrops",
                "Lifepod 17 Crew Log",
                "Lifepod 6 Crew Log #1",
                "Lifepod 6 Crew Log #2",
                "Lifepod 6 Transmission Origin",
                "Degasi Crew Manifest: Marguerit Maida",
                "Propulsion Cannon",
                "Alterra Citizen Testimonials",
                "Trans-Gov Profile: Mongolian Independent States",
            }, new string[]
            {
                "Vending Machine",
                "Nuclear Waste Disposal",
                "Trash Can",
                "Bar Table",
                "Picture Frame",
                "Command Chair",
                "Swivel Chair",
                "Desk",
                "Bench",
                "Plant Shelf",
                "Chic Plant Pot",
                "Composite Plant Pot",
                "Basic Plant Pot",
                "Modification Station",
                "Battery Charger",
                "Bioreactor",
                "Floodlight",
                "Scanner Room HUD Chip",
                "Camera Drone",
                "Scanner Room Range Upgrade",
                "Scanner Room Speed Upgrade",
                "Scanner Room",
                "Vortex Torpedo",
                "Gas Torpedo",
                "Seamoth Depth Module MK1",
                "Hull Reinforcement",
                "Engine Efficiency Module",
                "Storage Module",
                "Seamoth Solar Charger",
                "Seamoth Perimeter Defense System",
                "Seamoth Torpedo System",
                "Seamoth Sonar",
                "Prawn Suit Depth Module MK1",
                "Prawn Suit Thermal Reactor",
                "Prawn Suit Jump Jet Upgrade",
                "Prawn Suit Depth Module MK2",
                "Seamoth Depth Module MK3",
                "Seamoth Depth Module MK2",
                "Vehicle Upgrade Console",
                "Ultra Glide Fins",
                "Lightweight High Capacity Tank",
                "Seamoth",
                "Mobile Vehicle Bay",
                "Light Stick",
                "Propulsion Cannon",
                "Laser Cutter",
                "Cooked + Cured Spadefish",
                "Plasteel Ingot",
                "Cooked + Cured Peeper",
                "Cooked + Cured Reginald",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Hoopfish",
            });

            AddGroup(groups, "Mushroom Forest", new string[]
            {
                "Planters & Pots",
                "Modification Station",
                "Floodlight",
                "Light Stick",
                "Moonpool",
                "Cyclops",
                "Tree Mushrooms",
                "Brain Coral",
                "Coral Shell Plate",
                "Acid Mushroom",
                "Pygmy Fan",
                "Tree Leech",
                "Veined Nettle",
                "Writhing Weed",
                "Boneshark",
                "Holefish",
                "Jellyray",
                "Mesmer",
                "Peeper",
                "Reefback Leviathan",
                "Shuttlebug",
                "Spadefish",
                "Alien Eggs",
                "Shale Outcrops",
                "Limestone Outcrops",
                "Alien Arch",
                "Alien Vent",
                "Aurora Scanner Room Voice Log",
                "Lifepod 13 Emissary's Voicelog",
            }, new string[]
            {
                "Picture Frame",
                "Command Chair",
                "Office Chair",
                "Desk",
                "Bench",
                "Cyclops Bridge",
                "Cyclops Hull",
                "Plant Shelf",
                "Composite Plant Pot",
                "Modification Station",
                "Power Cell Charger",
                "Floodlight",
                "Power Transmitter",
                "Moonpool",
                "Cyclops",
                "Cyclops Sonar Upgrade",
                "Vortex Torpedo",
                "Gas Torpedo",
                "Seamoth Depth Module MK1",
                "Hull Reinforcement",
                "Engine Efficiency Module",
                "Storage Module",
                "Seamoth Solar Charger",
                "Seamoth Perimeter Defense System",
                "Seamoth Torpedo System",
                "Seamoth Sonar",
                "Prawn Suit Depth Module MK1",
                "Prawn Suit Thermal Reactor",
                "Prawn Suit Jump Jet Upgrade",
                "Prawn Suit Depth Module MK2",
                "Seamoth Depth Module MK3",
                "Seamoth Depth Module MK2",
                "Vehicle Upgrade Console",
                "Ultra Glide Fins",
                "Light Stick",
                "Cooked + Cured Peeper",
                "Cooked + Cured Spadefish",
            });

            AddGroup(groups, "Jellyshroom Caves", new string[]
            {
                "Thermal Plant",
                "Nuclear Reactor",
                "Spotlight",
                "Moonpool",
                "Modification Station",
                "Stasis Rifle",
                "Floodlight",
                "Water Filtration System",
                "Drooping Stinger",
                "Cave Bush",
                "Furled Papyrus",
                "Jellyshroom",
                "Redwort",
                "Regress Shell",
                "Rouge Cradle",
                "Violet Beau",
                "Biter",
                "Crabsnake",
                "Eyeye",
                "Oculus",
                "Alien Eggs",
                "Shale Outcrops",
                "Magnetite",
                "Bart Torgal's Log #1 - This World",
                "Bart Torgal's Log #2 - Stalker Teeth",
                "Degasi Voice Log #5 - Pecking Order",
                "Degasi Voice Log #6 - Deeper?!",
                "Paul Torgal's Log #2 - Dilemma",
                "Marguerit Maida's Log - Speaking Freely",
                "Environment Log",
            }, new string[]
            {
                "Wall Planter",
                "Swivel Chair",
                "Desk",
                "Basic Double Bed",
                "Modification Station",
                "Battery Charger",
                "Nuclear Reactor",
                "Water Filtration Machine",
                "Spotlight",
                "Floodlight",
                "Thermal Plant",
                "Observatory",
                "Moonpool",
                "Multipurpose Room",
                "Ultra High Capacity Tank",
                "Stasis Rifle",
                "Reactor Rod",
                "Cooked + Cured Eyeye",
                "Cooked + Cured Oculus",
            });

            AddGroup(groups, "Bulb Zone", new string[]
            {
                "Water Filtration System",
                "Scanner Room",
                "Planters & Pots",
                "Modification Station",
                "Indoor Growbed",
                "Floodlight",
                "Exterior Growbed",
                "Alien Containment",
                "Stillsuit",
                "Stasis Rifle",
                "Repulsion Cannon",
                "Reinforced Dive Suit",
                "Light Stick",
                "Brain Coral",
                "Coral Shell Plate",
                "Earthen Coral Tubes",
                "Table Coral",
                "Bulb Bush",
                "Cave Bush",
                "Eye Stalk",
                "Ghost Weed",
                "Pygmy Fan",
                "Redwort",
                "Rouge Cradle",
                "Sea Crown",
                "Spiked Horn Grass",
                "Spotted Dockleaf",
                "Giant Bulb Bush",
                "Tree Leech",
                "Violet Beau",
                "Writhing Weed",
                "Ampeel",
                "Boneshark",
                "Boomerang",
                "Eyeye",
                "Holefish",
                "Hoopfish",
                "Mesmer",
                "Peeper",
                "Reaper Leviathan",
                "Reefback Leviathan",
                "Rockgrub",
                "Shuttlebug",
                "Warper",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Ruby",
                "Alien Arch",
                "Lifepod 12 Medical Officer Danby's Crew Log",
                "The Charter",
            }, new string[]
            {
                "Single Wall Shelf",
                "Vending Machine",
                "Bar Table",
                "Command Chair",
                "Swivel Chair",
                "Desk",
                "Basic Double Bed",
                "Bench",
                "Indoor Growbed",
                "Chic Plant Pot",
                "Basic Plant Pot",
                "Modification Station",
                "Alien Containment",
                "Water Filtration Machine",
                "Exterior Growbed",
                "Floodlight",
                "Power Transmitter",
                "Scanner Room HUD Chip",
                "Camera Drone",
                "Scanner Room Range Upgrade",
                "Scanner Room Speed Upgrade",
                "Scanner Room",
                "Repulsion Cannon",
                "Lightweight High Capacity Tank",
                "Light Stick",
                "Cooked + Cured Peeper",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Eyeye",
                "Cooked + Cured Hoopfish",
                "Reinforced Dive Suit",
                "Stillsuit",
                "Stasis Rifle",
            });

            AddGroup(groups, "Mountains", new string[]
            {
                "Speckled Rattler",
                "Pink Cap",
                "Skyray",
                "Cave Crawler",
                "Bulbo Tree",
                "Pyrocoral",
                "Prawn Suit",
                "Cyclops",
                "Nuclear Reactor",
                "Planters & Pots",
                "Moonpool",
                "Modification Station",
                "Floodlight",
                "Alien Containment",
                "Stillsuit",
                "Light Stick",
                "Brain Coral",
                "Coral Shell Plate",
                "Cave Bush",
                "Blue Palm",
                "Drooping Stinger",
                "Eye Stalk",
                "Furled Papyrus",
                "Gel Sack",
                "Ghost Weed",
                "Regress Shell",
                "Spiked Horn Grass",
                "Spotted Dockleaf",
                "Tree Leech",
                "Violet Beau",
                "Biter",
                "Hoopfish",
                "Boomerang",
                "Peeper",
                "Reaper Leviathan",
                "Reginald",
                "Rockgrub",
                "Shuttlebug",
                "Warper",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Sandstone Outcrops",
                "Magnetite",
                "Alien Arch",
                "Alien Vent",
                "Purple Tablet",
            }, new string[]
            {
                "Wall Shelves",
                "Single Wall Shelf",
                "Picture Frame",
                "Command Chair",
                "Office Chair",
                "Swivel Chair",
                "Desk",
                "Quilted Double Bed",
                "Basic Double Bed",
                "Bench",
                "Cyclops Engine",
                "Plant Shelf",
                "Chic Plant Pot",
                "Composite Plant Pot",
                "Basic Plant Pot",
                "Modification Station",
                "Alien Containment",
                "Nuclear Reactor",
                "Floodlight",
                "Moonpool",
                "Prawn Suit Torpedo Arm",
                "Prawn Suit Grappling Arm",
                "Prawn Suit Propulsion Cannon",
                "Prawn Suit",
                "Light Stick",
                "Aerogel",
                "Reactor Rod",
                "Cooked + Cured Peeper",
                "Cooked + Cured Reginald",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Hoopfish",
                "Stillsuit",
                "Purple Tablet",
            });

            AddGroup(groups, "Floating Island", new string[]
            {
                "Spotlight",
                "Planters & Pots",
                "Interior Growbed",
                "Exterior Growbed",
                "Bulkhead Door",
                "Stasis Rifle",
                "Chinese Potato Plant",
                "Fern Palm",
                "Lantern Tree",
                "Marblemelon Plant",
                "Bulbo Tree",
                "Grub Basket",
                "Jaffa Cup",
                "Marblemelon Plant",
                "Ming Plant",
                "Pink Cap",
                "Speckled Rattler",
                "Voxel Shrub",
                "Ancient Floater",
                "Cave Crawler",
                "Skyray",
                "Alien Arch",
                "Purple Tablet",
                "Rendezvous Voicelog",
                "Bart Torgal's Log #3 - Return From the Deep",
                "Degasi Voice Log #1 - Habitation Location",
                "Degasi Voice Log #2 - Storm!",
                "Degasi Voice Log #3 - Aftermath",
                "Degasi Voice Log #4 - Curious Discovery",
                "Paul Torgal's Log #1 - Marooned",
            }, new string[]
            {
                "Bulkhead",
                "Wall Planter",
                "Swivel Chair",
                "Desk",
                "Indoor Growbed",
                "Composite Plant Pot",
                "Exterior Growbed",
                "Spotlight",
                "Observatory",
                "Stasis Rifle",
                "Multipurpose Room",
                "Ultra Glide Fins",
                "Purple Tablet",
            });

            AddGroup(groups, "Underwater Islands", new string[]
            {
                "Prawn Suit",
                "Cyclops",
                "Planters & Pots",
                "Floodlight",
                "Alien Containment",
                "Stasis Rifle",
                "Repulsion Cannon",
                "Creature Decoy",
                "Earthen Coral Tubes",
                "Coral Shell Plate",
                "Redwort",
                "Violet Beau",
                "Drooping Stinger",
                "Ancient Floater",
                "Boneshark",
                "Boomerang",
                "Cave Crawler",
                "Garryfish",
                "Hoopfish",
                "Peeper",
                "Reefback Leviathan",
                "Spotted Dockleaf",
                "Spadefish",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Sandstone Outcrops",
                "Ruby",
                "Limestone Outcrops",
                "Alien Vent",
            }, new string[]
            {
                "Picture Frame",
                "Command Chair",
                "Office Chair",
                "Swivel Chair",
                "Desk",
                "Bench",
                "Cyclops Engine",
                "Chic Plant Pot",
                "Composite Plant Pot",
                "Power Cell Charger",
                "Alien Containment",
                "Floodlight",
                "Cyclops",
                "Prawn Suit Drill Arm",
                "Prawn Suit Grappling Arm",
                "Prawn Suit Propulsion Cannon",
                "Repulsion Cannon",
                "Swim Charge Fins",
                "Prawn Suit",
                "Creature Decoy",
                "Propulsion Cannon",
                "Cooked + Cured Peeper",
                "Cooked + Cured Garryfish",
                "Cooked + Cured Spadefish",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Hoopfish",
                "Stasis Rifle",
            });

            AddGroup(groups, "Crash Zone", new string[]
            {
                "Cyclops",
                "Brain Coral",
                "Giant Coral Tubes",
                "Table Coral",
                "Acid Mushroom",
                "Spotted Dockleaf",
                "Veined Nettle",
                "Writhing Weed",
                "Bladderfish",
                "Boneshark",
                "Boomerang",
                "Eyeye",
                "Holefish",
                "Hoverfish",
                "Peeper",
                "Rabbit Ray",
                "Reaper Leviathan",
                "Reginald",
                "Sand Shark",
                "Shuttlebug",
                "Skyray",
                "Stalker",
                "Alien Eggs",
                "Shale Outcrops",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Ruby",
                "Limestone Outcrops",
                "Lifepod 4 Crew Log",
                "Lifepod 4 Transmission Origin",
                "Creature Decoy",
            }, new string[]
            {
                "Cyclops Bridge",
                "Cyclops Engine",
                "Power Transmitter",
                "Creature Decoy",
                "Propulsion Cannon",
                "Enameled Glass",
                "Cooked + Cured Holefish",
                "Cooked + Cured Peeper",
                "Cooked + Cured Hoverfish",
                "Cooked + Cured Reginald",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Eyeye",
            });

            AddGroup(groups, "Aurora", new string[]
            {
                "Seamoth",
                "Prawn Suit",
                "Cyclops",
                "Floodlight",
                "Time Capsule",
                "Repulsion Cannon",
                "Bleeder",
                "Cave Crawler",
                "Reaper Leviathan",
                "Neptune Escape Rocket",
                "Aurora Engineering Drone - Log",
                "Aurora Ship Status",
                "Captain's Log",
                "High Security Terminal - Captain's Quarters",
                "Lab Access",
                "Notes to self",
                "Sweet Offer",
                "Aurora Auxiliary Mission Orders",
                "Alterra Alms Pamphlet",
                "Alterra Launches the Aurora",
                "Relationship Contract Legal Recording",
                "Responsible Autonomous Relationships",
                "Today's Menu",
                "Trans-Gov Profile: Alterra Corp",
                "What Can We Learn From the Hive Mind of Strader VI?",
                "VR Suite Log",
                "Drive Core Shielding Breach",
                "Aurora Black Box Data",
                "Alterra HQ - Last Recorded Transmissions",
            }, new string[]
            {
                "Wall Shelves",
                "Single Wall Shelf",
                "Counter",
                "Vending Machine",
                "Trash Can",
                "Bar Table",
                "Command Chair",
                "Swivel Chair",
                "Desk",
                "Single Bed",
                "Basic Double Bed",
                "Bench",
                "Cyclops Engine",
                "Floodlight",
                "Repulsion Cannon",
                "Cyclops Engine Efficiency Module",
                "Cyclops Docking Bay Repair Module",
                "Cyclops Fire Suppression System",
                "Neptune Launch Platform",
                "Prawn Suit",
                "Seamoth",
                "Propulsion Cannon",
                "Plasteel Ingot",
            });

            AddGroup(groups, "Dunes", new string[]
            {
                "Thermal Plant",
                "Nuclear Reactor",
                "Water Filtration System",
                "Planters & Pots",
                "Moonpool",
                "Floodlight",
                "Stillsuit",
                "All-Environment Protection Suit",
                "Reinforced Dive Suit",
                "Brain Coral",
                "Acid Mushroom",
                "Drooping Stinger",
                "Furled Papyrus",
                "Gel Sack",
                "Redwort",
                "Regress Shell",
                "Rouge Cradle",
                "Sea Crown",
                "Violet Beau",
                "Writhing Weed",
                "Bladderfish",
                "Boomerang",
                "Cave Crawler",
                "Eyeye",
                "Garryfish",
                "Gasopod",
                "Hoopfish",
                "Peeper",
                "Cuddlefish",
                "Reaper Leviathan",
                "Reefback Leviathan",
                "Rockgrub",
                "Sand Shark",
                "Shuttlebug",
                "Spadefish",
                "Warper",
                "Alien Eggs",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Ruby",
                "Limestone Outcrops",
                "Alien Vent",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Alien Biological History",
                "Profitability Projections",
            }, new string[]
            {
                "Single Wall Shelf",
                "Counter",
                "Coffee Vending Machine",
                "Vending Machine",
                "Nuclear Waste Disposal",
                "Bar Table",
                "Command Chair",
                "Swivel Chair",
                "Desk",
                "Bench",
                "Chic Plant Pot",
                "Basic Plant Pot",
                "Power Cell Charger",
                "Nuclear Reactor",
                "Water Filtration Machine",
                "Floodlight",
                "Thermal Plant",
                "Moonpool",
                "Prawn Suit Drill Arm",
                "Prawn Suit Grappling Arm",
                "Vortex Torpedo",
                "Gas Torpedo",
                "Seamoth Depth Module MK1",
                "Hull Reinforcement",
                "Engine Efficiency Module",
                "Storage Module",
                "Seamoth Solar Charger",
                "Seamoth Perimeter Defense System",
                "Seamoth Torpedo System",
                "Seamoth Sonar",
                "Prawn Suit Depth Module MK1",
                "Prawn Suit Thermal Reactor",
                "Prawn Suit Jump Jet Upgrade",
                "Prawn Suit Depth Module MK2",
                "Seamoth Depth Module MK3",
                "Seamoth Depth Module MK2",
                "Cyclops Depth Module MK3",
                "Cyclops Depth Module MK2",
                "Cyclops Depth Module MK1",
                "Cyclops Shield Generator",
                "Cyclops Decoy Tube Upgrade",
                "Vehicle Upgrade Console",
                "Ultra High Capacity Tank",
                "Air Bladder",
                "Cooked + Cured Spadefish",
                "Aerogel",
                "Filtered Water",
                "Reactor Rod",
                "Cooked + Cured Peeper",
                "Cooked + Cured Bladderfish",
                "Cooked + Cured Garryfish",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Eyeye",
                "Cooked + Cured Hoopfish",
                "Reinforced Dive Suit",
                "Stillsuit",
            });

            AddGroup(groups, "Crag Field", new string[]
            {
                "Cyclops",
                "Scanner Room",
                "Modification Station",
                "Eye Stalk",
                "Pink Cap",
                "Speckled Rattler",
                "Tiger Plant",
                "Boneshark",
                "Boomerang",
                "Cave Crawler",
                "Hoopfish",
                "Mesmer",
                "Reefback Leviathan",
                "Reginald",
                "Shuttlebug",
                "Alien Eggs",
                "Shale Outcrops",
                "Scattered Wreckage",
                "Sandstone Outcrops",
                "Limestone Outcrops",
                "Alien Arch",
                "Lifepod 7 Crew Log",
                "Lifepod 7 Transmission Origin",
            }, new string[]
            {
                "An Unusual Doll",
                "Cyclops Engine",
                "Modification Station",
                "Scanner Room HUD Chip",
                "Camera Drone",
                "Scanner Room Range Upgrade",
                "Scanner Room Speed Upgrade",
                "Scanner Room",
                "Cooked + Cured Reginald",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Hoopfish",
            });

            AddGroup(groups, "Sparse Reef", new string[]
            {
                "Thermal Plant",
                "Bioreactor",
                "Moonpool",
                "Modification Station",
                "Floodlight",
                "Reinforced Dive Suit",
                "Light Stick",
                "Table Coral",
                "Eye Stalk",
                "Furled Papyrus",
                "Gabe's Feather",
                "Gel Sack",
                "Redwort",
                "Regress Shell",
                "Rouge Cradle",
                "Spiked Horn Grass",
                "Tiger Plant",
                "Violet Beau",
                "Bleeder",
                "Peeper",
                "Reefback Leviathan",
                "Reginald",
                "Rockgrub",
                "Shuttlebug",
                "Spadefish",
                "Alien Eggs",
                "Shale Outcrops",
                "Ruby",
                "Limestone Outcrops",
                "Alien Vent",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Alien Sanctuary Alpha",
                "Lifepod 19 Second Officer Keen's Voicelog",
                "Lifepod 19 Second Officer Keen's Crew Log",
            }, new string[]
            {
                "Command Chair",
                "Office Chair",
                "Desk",
                "Bench",
                "Modification Station",
                "Power Cell Charger",
                "Bioreactor",
                "Floodlight",
                "Thermal Plant",
                "Moonpool",
                "Prawn Suit Torpedo Arm",
                "Vortex Torpedo",
                "Gas Torpedo",
                "Seamoth Depth Module MK1",
                "Hull Reinforcement",
                "Engine Efficiency Module",
                "Storage Module",
                "Seamoth Solar Charger",
                "Seamoth Perimeter Defense System",
                "Seamoth Torpedo System",
                "Seamoth Sonar",
                "Prawn Suit Depth Module MK1",
                "Prawn Suit Thermal Reactor",
                "Prawn Suit Jump Jet Upgrade",
                "Prawn Suit Depth Module MK2",
                "Seamoth Depth Module MK3",
                "Seamoth Depth Module MK2",
                "Vehicle Upgrade Console",
                "Ultra High Capacity Tank",
                "Light Stick",
                "Cooked + Cured Peeper",
                "Cooked + Cured Reginald",
                "Cooked + Cured Spadefish",
                "Reinforced Dive Suit",
            });

            AddGroup(groups, "Sea Treader's Path", new string[]
            {
                "Prawn Suit",
                "Cyclops",
                "Nuclear Reactor",
                "Scanner Room",
                "Planters & Pots",
                "Floodlight",
                "Cave Bush",
                "Deep Shroom",
                "Furled Papyrus",
                "Gabe's Feather",
                "Gel Sack",
                "Membrain Tree",
                "Regress Shell",
                "Spiked Horn Grass",
                "Violet Beau",
                "Boomerang",
                "Eyeye",
                "Hoopfish",
                "Peeper",
                "Reginald",
                "Sea Treader Leviathan",
                "Shuttlebug",
                "Spadefish",
                "Warper",
                "Alien Eggs",
                "Shale Outcrops",
                "Sandstone Outcrops",
                "Ruby",
                "Magnetite",
                "Limestone Outcrops",
            }, new string[]
            {
                "Picture Frame",
                "Command Chair",
                "Swivel Chair",
                "Desk",
                "Bench",
                "Cyclops Bridge",
                "Cyclops Hull",
                "Plant Shelf",
                "Composite Plant Pot",
                "Nuclear Reactor",
                "Floodlight",
                "Scanner Room HUD Chip",
                "Camera Drone",
                "Scanner Room Range Upgrade",
                "Scanner Room Speed Upgrade",
                "Scanner Room",
                "Prawn Suit Propulsion Cannon",
                "Prawn Suit",
                "Cooked + Cured Spadefish",
                "Hydrochloric Acid",
                "Polyaniline",
                "Reactor Rod",
                "Cooked + Cured Peeper",
                "Cooked + Cured Reginald",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Eyeye",
                "Cooked + Cured Hoopfish",
            });

            AddGroup(groups, "Grand Reef", new string[]
            {
                "Thermal Plant",
                "Scanner Room",
                "Planters & Pots",
                "Moonpool",
                "Alien Containment",
                "Repulsion Cannon",
                "Anchor Pods",
                "Gel Sack",
                "Membrain Tree",
                "Bladderfish",
                "Boneshark",
                "Boomerang",
                "Crabsquid",
                "Eyeye",
                "Ghost Leviathan",
                "Hoopfish",
                "Jellyray",
                "Mesmer",
                "Peeper",
                "Reefback Leviathan",
                "Reginald",
                "Sea Treader Leviathan",
                "Spadefish",
                "Warper",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Sandstone Outcrops",
                "Ruby",
                "Limestone Outcrops",
                "Alien Vent",
                "Orange Tablet",
                "Surveillance Log, Leisure Deck B",
                "Degasi Voice Log #7 - Malady",
                "Degasi Voice Log #8 - Risk Taking",
                "Degasi Voice Log #9 - Disaster",
                "Corporate Profile: Torgal Corp",
                "Paul Torgal's Log #3 - The End",
            }, new string[]
            {
                "Wall Shelves",
                "Counter",
                "Coffee Vending Machine",
                "Nuclear Waste Disposal",
                "Trash Can",
                "Picture Frame",
                "Command Chair",
                "Office Chair",
                "Swivel Chair",
                "Desk",
                "Single Bed",
                "Quilted Double Bed",
                "Basic Double Bed",
                "Bench",
                "Plant Shelf",
                "Composite Plant Pot",
                "Alien Containment",
                "Thermal Plant",
                "Observatory",
                "Moonpool",
                "Multipurpose Room",
                "Scanner Room HUD Chip",
                "Camera Drone",
                "Scanner Room Range Upgrade",
                "Scanner Room Speed Upgrade",
                "Scanner Room",
                "Prawn Suit Drill Arm",
                "Prawn Suit Grappling Arm",
                "Cyclops Depth Module MK3",
                "Cyclops Depth Module MK2",
                "Cyclops Depth Module MK1",
                "Repulsion Cannon",
                "Swim Charge Fins",
                "Air Bladder",
                "Cooked + Cured Spadefish",
                "Aerogel",
                "Filtered Water",
                "Cooked + Cured Peeper",
                "Cooked + Cured Bladderfish",
                "Cooked + Cured Eyeye",
                "Cooked + Cured Hoopfish",
                "Orange Tablet",
            });

            AddGroup(groups, "Blood Kelp Zone", new string[]
            {
                "Bloodroot",
                "Bloodvine",
                "Deep Shroom",
                "Gabe's Feather",
                "Gel Sack",
                "Ghost Weed",
                "Regress Shell",
                "Rouge Cradle",
                "Ancient Floater",
                "Ampeel",
                "Blighter",
                "Blood Crawler",
                "Crabsquid",
                "Ghost Leviathan",
                "Spinefish",
                "Warper",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Magnetite",
                "Alien Sanctuary Beta",
                "Lifepod 2 Chief Technical Officer Yu's Voicelog (T+2min)",
            }, new string[]
            {
                "Cyclops Depth Module MK3",
                "Cyclops Depth Module MK2",
                "Cyclops Depth Module MK1",
                "Hydrochloric Acid",
                "Benzene",
                "Synthetic Fibers",
                "Aerogel",
                "Polyaniline",
                "Cooked + Cured Spinefish",
            });

            AddGroup(groups, "Blood Kelp Trench", new string[]
            {
                "Nuclear Reactor",
                "Scanner Room",
                "Planters & Pots",
                "Moonpool",
                "Floodlight",
                "Bloodroot",
                "Bloodvine",
                "Deep Shroom",
                "Gabe's Feather",
                "Gel Sack",
                "Ghost Weed",
                "Ampeel",
                "Blighter",
                "Blood Crawler",
                "Spinefish",
                "Warper",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Magnetite",
                "Join Alterra's Board of Directors",
            }, new string[]
            {
                "Command Chair",
                "Desk",
                "Bench",
                "Plant Shelf",
                "Nuclear Reactor",
                "Floodlight",
                "Moonpool",
                "Scanner Room HUD Chip",
                "Camera Drone",
                "Scanner Room Range Upgrade",
                "Scanner Room Speed Upgrade",
                "Scanner Room",
                "Prawn Suit Torpedo Arm",
                "Prawn Suit Drill Arm",
                "Prawn Suit Grappling Arm",
                "Hydrochloric Acid",
                "Benzene",
                "Synthetic Fibers",
                "Aerogel",
                "Polyaniline",
                "Reactor Rod",
                "Cooked + Cured Spinefish",
            });

            AddGroup(groups, "Lost River", new string[]
            {
                "Bladderfish",
                "Brine Lily",
                "Sea Dragon Skeleton",
                "Mixed Leviathan Fossils",
                "Gargantuan Fossil",
                "Ancient Fossilized Skeleton",
                "Pyrocoral",
                "Table Coral",
                "Crab Claw Kelp",
                "Gel Sack",
                "Ghost Weed",
                "Amoeboid",
                "Bleeder",
                "Ghost Leviathan Juvenile",
                "Ghostray",
                "Mesmer",
                "River Prowler",
                "Spinefish",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Sandstone Outcrops",
                "Ruby",
                "Nickel Ore",
                "Magnetite",
                "Limestone Outcrops",
                "Crystalline Sulfur",
                "Alien Arch",
                "Alien Flora Research",
                "Giant Cove Tree",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Research Equipment",
                "Research Probes",
                "Fauna Reproductive Data",
            }, new string[]
            {
                "Aerogel",
                "Cooked + Cured Spinefish",
                "Cooked + Cured Bladderfish",
            });

            AddGroup(groups, "Inactive Lava Zone", new string[]
            {
                "Reaper Leviathan Skeleton",
                "Deep Shroom",
                "Crimson Ray",
                "Lava Larva",
                "Lava Lizard",
                "Magmarang",
                "Red Eyeye",
                "Sea Dragon Leviathan",
                "Warper",
                "Alien Eggs",
                "Uraninite",
                "Shale Outcrops",
                "Nickel Ore",
                "Magnetite",
                "Kyanite",
                "Crystalline Sulfur",
            }, new string[]
            {
                "Hydrochloric Acid",
                "Polyaniline",
                "Cooked + Cured Magmarang",
                "Cooked + Cured Red Eyeye",
            });

            AddGroup(groups, "Active Lava Zone", new string[]
            {
                "Crimson Ray",
                "Lava Larva",
                "Lava Lizard",
                "Magmarang",
                "Red Eyeye",
                "Sea Dragon Leviathan",
                "Warper",
                "Alien Eggs",
                "Kyanite",
                "Crystalline Sulfur",
                "Sonic Deterrent",
            }, new string[]
            {
                "Cooked + Cured Magmarang",
                "Cooked + Cured Red Eyeye",
            });

            AddGroup(groups, "Alien Thermal Plant", new string[]
            {
                "Alien Arch",
                "Alien Robot",
                "Alien Thermal Plant",
                "Blue Tablet",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Fossil Data",
                "Ion Power Data",
                "Primary Containment Facility",
            }, new string[]
            {
                "Ion Battery",
                "Ion Power Cell",
                "Blue Tablet",
                "Cyclops Thermal Reactor Module",
            });

            AddGroup(groups, "Disease Research Facility", new string[]
            {
                "Rib Cage Samples",
                "Remains of Research Specimen",
                "Bloodvine",
                "Amoeboid",
                "Spinefish",
                "Damaged Anchor Cable",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Production Line",
                "Self-Warping Quarantine Enforcer Unit",
                "Warper Parts",
                "Damage Report",
                "Kharaa Contagion Profile",
                "Specimen Research Data",
                "Bacterial Infection Report",
                "Ray Species on 4546B",
                "Sea Dragon Egg",
            }, new string[]
            {
                "Cooked + Cured Spinefish",
            });

            AddGroup(groups, "Primary Containment Facility", new string[]
            {
                "Ancient Earth Blade",
                "Alien Statue",
                "Alien Carving",
                "Alien Building Block",
                "Holographic Projector",
                "Nanobots",
                "Organic Matter Particulator",
                "Rudimentary Tablet",
                "Tracking Implant",
                "Translation Device",
                "Alien Arch",
                "Alien Robot",
                "Aquarium Pipe Access Point",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Ion Cube Fabricator",
                "Enzyme 42 Project Data",
                "Sea Emperor Leviathan Research Data",
                "Ventilation Control",
                "Enzyme Host Peepers Leaving the Containment Facility",
                "Peepers Entering the Containment Facility",
                "Sea Emperor Egg Casing",
                "Sea Emperor Fetus",
            }, new string[]
            {
                "Aerogel",
                "Cooked + Cured Peeper",
            });

            AddGroup(groups, "Aquarium", new string[]
            {
                "Giant Coral Tubes",
                "Brain Coral",
                "Acid Mushroom",
                "Cave Bush",
                "Blue Palm",
                "Furled Papyrus",
                "Gabe's Feather",
                "Gel Sack",
                "Redwort",
                "Rouge Cradle",
                "Sea Crown",
                "Spotted Dockleaf",
                "Veined Nettle",
                "Violet Beau",
                "Writhing Weed",
                "Bladderfish",
                "Boneshark",
                "Boomerang",
                "Cave Crawler",
                "Holefish",
                "Hoopfish",
                "Garryfish",
                "Hoverfish",
                "Oculus",
                "Peeper",
                "Rabbit Ray",
                "Reginald",
                "Sea Emperor Leviathan",
                "Sea Emperor Juvenile",
                "The Sea Emperor's Eggs",
                "Shuttlebug",
                "Stalker",
                "Alien Eggs",
                "Aquarium",
                "Alien Arch",
                "Aquarium Arch",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Enzyme Host Peeper",
                "Peepers Inside the Containment Facility",
                "Hatching Enzymes",
                "Hatching Enzymes Old",
                "Specimen with Infection Symptoms Inhibited",
                "Specimen with Symptoms of Infection",
                "The Sea Emperor's Eggs",
                "The Sea Emperor's Lifecycle",
            }, new string[]
            {
                "Cooked + Cured Reginald",
                "Cooked + Cured Hoverfish",
                "Enameled Glass",
                "Aerogel",
                "Filtered Water",
                "Cooked + Cured Holefish",
                "Cooked + Cured Peeper",
                "Cooked + Cured Bladderfish",
                "Cooked + Cured Garryfish",
                "Cooked + Cured Boomerang",
                "Cooked + Cured Oculus",
                "Cooked + Cured Hoopfish",
                "Air Bladder",
                "Hatching Enzymes",
            });

            AddGroup(groups, "Quarantine Enforcement Platform", new string[]
            {
                "Doomsday Device",
                "Alien Rifle",
                "Alien Arch",
                "Energy Core",
                "Forcefield Control Terminal",
                "Ion Cube",
                "Purple Tablet",
                "Alien Data Terminal",
                "Enforcement Platform Schematic",
                "Alien Facility Locations",
            }, new string[]
            {
                "Purple Tablet",
            });

            AddGroup(groups, "Void", new string[]
            {
                "Crater Edge",
            }, new string[]
            {
            });

            Logger.Log($"[BiomePairing] Loaded groups={groups.Count}");
            return new SubnauticaUnlockPairingCatalog(groups);
        }

        private static void AddGroup(List<BiomeUnlockGroup> groups, string displayName, string[] databankEntries, string[] blueprintEntries)
        {
            if (!SubnauticaBiomeCatalog.TryGetCanonicalByDisplayName(displayName, out string canonicalBiomeKey))
            {
                canonicalBiomeKey = "raw_" + displayName.Replace(" ", "_");
                Logger.Warn($"[BiomePairing] Biome display name is not mapped to a known canonical biome: {displayName}");
            }

            groups.Add(new BiomeUnlockGroup(
                displayName,
                canonicalBiomeKey,
                DeduplicateEntries(databankEntries),
                DeduplicateEntries(blueprintEntries)));
        }

        private static string[] DeduplicateEntries(string[] entries)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(entries.Length);

            foreach (string entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                string trimmed = entry.Trim();
                if (seen.Add(trimmed))
                    result.Add(trimmed);
            }

            return result.ToArray();
        }
    }
}
