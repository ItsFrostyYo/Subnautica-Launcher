using System.Collections.Generic;
using System.Linq;

namespace SubnauticaLauncher.Gameplay
{
    internal sealed partial class SubnauticaChecklistCatalog
    {
        internal sealed record ChecklistEntry(string Name, bool IsPreInstalled);

        internal sealed class ChecklistGroup
        {
            public ChecklistGroup(string name, List<ChecklistEntry> entries)
            {
                Name = name;
                Entries = entries;
            }

            public string Name { get; }
            public List<ChecklistEntry> Entries { get; }
        }

        private static readonly object Sync = new();
        private static SubnauticaChecklistCatalog? _cached;

        private readonly List<ChecklistGroup> _blueprintGroups;
        private readonly List<ChecklistGroup> _databankGroups;

        private SubnauticaChecklistCatalog(List<ChecklistGroup> blueprintGroups, List<ChecklistGroup> databankGroups)
        {
            _blueprintGroups = blueprintGroups;
            _databankGroups = databankGroups;
        }

        public IReadOnlyList<ChecklistGroup> BlueprintGroups => _blueprintGroups;
        public IReadOnlyList<ChecklistGroup> DatabankGroups => _databankGroups;

        public IEnumerable<ChecklistEntry> BlueprintEntries => _blueprintGroups.SelectMany(group => group.Entries);
        public IEnumerable<ChecklistEntry> DatabankEntries => _databankGroups.SelectMany(group => group.Entries);

        public static SubnauticaChecklistCatalog GetOrLoad()
        {
            lock (Sync)
            {
                _cached ??= BuildDefaultCatalog();
                return _cached;
            }
        }
    }
}
namespace SubnauticaLauncher.Gameplay
{
    internal sealed partial class SubnauticaChecklistCatalog
    {
        private static SubnauticaChecklistCatalog BuildDefaultCatalog()
        {
            var blueprintGroups = new List<ChecklistGroup>
            {
                new ChecklistGroup("BASIC MATERIALS (9)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Titanium", true),
                    new ChecklistEntry("Titanium Ingot", true),
                    new ChecklistEntry("Fiber Mesh", false),
                    new ChecklistEntry("Silicone Rubber", true),
                    new ChecklistEntry("Glass", true),
                    new ChecklistEntry("Bleach", true),
                    new ChecklistEntry("Lubricant", false),
                    new ChecklistEntry("Enameled Glass", false),
                    new ChecklistEntry("Plasteel Ingot", false),
                }),
                new ChecklistGroup("ADVANCED MATERIALS (6)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Hydrochloric Acid", false),
                    new ChecklistEntry("Benzene", false),
                    new ChecklistEntry("Synthetic Fibers", false),
                    new ChecklistEntry("Aerogel", false),
                    new ChecklistEntry("Polyaniline", false),
                    new ChecklistEntry("Hatching Enzymes", false),
                }),
                new ChecklistGroup("ELECTRONICS (9)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Copper Wire", true),
                    new ChecklistEntry("Battery", true),
                    new ChecklistEntry("Ion Battery", false),
                    new ChecklistEntry("Power Cell", false),
                    new ChecklistEntry("Ion Power Cell", false),
                    new ChecklistEntry("Computer Chip", true),
                    new ChecklistEntry("Wiring Kit", true),
                    new ChecklistEntry("Advanced Wiring Kit", false),
                    new ChecklistEntry("Reactor Rod", false),
                }),
                new ChecklistGroup("WATER (2)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Filtered Water", false),
                    new ChecklistEntry("Disinfected Water", true),
                }),
                new ChecklistGroup("COOKED + CURED FOOD (28)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Cooked + Cured Holefish", false),
                    new ChecklistEntry("Cooked + Cured Peeper", false),
                    new ChecklistEntry("Cooked + Cured Bladderfish", false),
                    new ChecklistEntry("Cooked + Cured Garryfish", false),
                    new ChecklistEntry("Cooked + Cured Hoverfish", false),
                    new ChecklistEntry("Cooked + Cured Reginald", false),
                    new ChecklistEntry("Cooked + Cured Spadefish", false),
                    new ChecklistEntry("Cooked + Cured Boomerang", false),
                    new ChecklistEntry("Cooked + Cured Magmarang", false),
                    new ChecklistEntry("Cooked + Cured Eyeye", false),
                    new ChecklistEntry("Cooked + Cured Red Eyeye", false),
                    new ChecklistEntry("Cooked + Cured Oculus", false),
                    new ChecklistEntry("Cooked + Cured Hoopfish", false),
                    new ChecklistEntry("Cooked + Cured Spinefish", false),
                }),
                new ChecklistGroup("EQUIPMENT (15)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Standard O2 Tank", true),
                    new ChecklistEntry("High Capacity O2 Tank", false),
                    new ChecklistEntry("Fins", true),
                    new ChecklistEntry("Radiation Suit", false),
                    new ChecklistEntry("Reinforced Dive Suit", false),
                    new ChecklistEntry("Stillsuit", false),
                    new ChecklistEntry("First Aid Kit", true),
                    new ChecklistEntry("Fire Extinguisher", true),
                    new ChecklistEntry("Rebreather", false),
                    new ChecklistEntry("Compass", false),
                    new ChecklistEntry("Pipe", true),
                    new ChecklistEntry("Floating Air Pump", true),
                    new ChecklistEntry("Purple Tablet (1)", false),
                    new ChecklistEntry("Blue Tablet", false),
                    new ChecklistEntry("Orange Tablet", false),
                }),
                new ChecklistGroup("TOOLS (12)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Scanner", true),
                    new ChecklistEntry("Repair Tool", true),
                    new ChecklistEntry("Flashlight", true),
                    new ChecklistEntry("Survival Knife", true),
                    new ChecklistEntry("Pathfinder Tool", false),
                    new ChecklistEntry("Air Bladder", false),
                    new ChecklistEntry("Flare", true),
                    new ChecklistEntry("Habitat Builder", true),
                    new ChecklistEntry("Laser Cutter (3)", false),
                    new ChecklistEntry("Stasis Rifle (2)", false),
                    new ChecklistEntry("Propulsion Cannon (2)", false),
                    new ChecklistEntry("Light Stick (2)", false),
                }),
                new ChecklistGroup("MACHINES (6)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Seaglide (2)", false),
                    new ChecklistEntry("Mobile Vehicle Bay (3)", false),
                    new ChecklistEntry("Beacon (2)", false),
                    new ChecklistEntry("Waterproof Locker", true),
                    new ChecklistEntry("Grav Trap (2)", false),
                    new ChecklistEntry("Creature Decoy", false),
                }),
                new ChecklistGroup("MOBILE VEHICLE BAY (7)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Seamoth (3)", false),
                    new ChecklistEntry("Prawn Suit (4)", false),
                    new ChecklistEntry("Neptune Launch Platform", false),
                    new ChecklistEntry("Neptune Gantry", false),
                    new ChecklistEntry("Neptune Ion Boosters", false),
                    new ChecklistEntry("Neptune Fuel Reserve", false),
                    new ChecklistEntry("Neptune Cockpit", false),
                }),
                new ChecklistGroup("MODIFICATION STATION (11)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Thermoblade", false),
                    new ChecklistEntry("Lightweight High Capacity Tank", false),
                    new ChecklistEntry("Ultra High Capacity Tank", false),
                    new ChecklistEntry("Ultra Glide Fins", false),
                    new ChecklistEntry("Swim Charge Fins", false),
                    new ChecklistEntry("Repulsion Cannon", false),
                    new ChecklistEntry("Cyclops Depth Module MK2", false),
                    new ChecklistEntry("Cyclops Depth Module MK3", false),
                    new ChecklistEntry("Seamoth Depth Module MK2", false),
                    new ChecklistEntry("Seamoth Depth Module MK3", false),
                    new ChecklistEntry("Prawn Suit Depth Module MK2", false),
                }),
                new ChecklistGroup("VEHICLE UPGRADES (17)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Seamoth Depth Module MK1", false),
                    new ChecklistEntry("Hull Reinforcement", false),
                    new ChecklistEntry("Engine Efficiency Module", false),
                    new ChecklistEntry("Storage Module", false),
                    new ChecklistEntry("Seamoth Solar Charger", false),
                    new ChecklistEntry("Seamoth Perimeter Defense System", false),
                    new ChecklistEntry("Seamoth Torpedo System", false),
                    new ChecklistEntry("Seamoth Sonar", false),
                    new ChecklistEntry("Prawn Suit Depth Module MK1", false),
                    new ChecklistEntry("Prawn Suit Thermal Reactor", false),
                    new ChecklistEntry("Prawn Suit Jump Jet Upgrade", false),
                    new ChecklistEntry("Prawn Suit Propulsion Cannon (2)", false),
                    new ChecklistEntry("Prawn Suit Grappling Arm (2)", false),
                    new ChecklistEntry("Prawn Suit Drill Arm (2)", false),
                    new ChecklistEntry("Prawn Suit Torpedo Arm (2)", false),
                    new ChecklistEntry("Vortex Torpedo", false),
                    new ChecklistEntry("Gas Torpedo", false),
                }),
                new ChecklistGroup("SCANNER ROOM UPGRADES (4)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Scanner Room HUD Chip", false),
                    new ChecklistEntry("Camera Drone", false),
                    new ChecklistEntry("Scanner Room Range Upgrade", false),
                    new ChecklistEntry("Scanner Room Speed Upgrade", false),
                }),
                new ChecklistGroup("CYCLOPS (4)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Cyclops Hull (3)", false),
                    new ChecklistEntry("Cyclops Bridge (3)", false),
                    new ChecklistEntry("Cyclops Engine (3)", false),
                    new ChecklistEntry("Cyclops", false),
                    new ChecklistEntry("Cyclops Depth Module MK1", false),
                    new ChecklistEntry("Cyclops Engine Efficiency Module", false),
                    new ChecklistEntry("Cyclops Shield Generator", false),
                    new ChecklistEntry("Cyclops Sonar Upgrade", false),
                    new ChecklistEntry("Cyclops Docking Bay Repair Module", false),
                    new ChecklistEntry("Cyclops Fire Suppression System", false),
                    new ChecklistEntry("Cyclops Decoy Tube Upgrade", false),
                    new ChecklistEntry("Cyclops Thermal Reactor Module", false),
                }),
                new ChecklistGroup("BASE PIECES (8)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Foundation", true),
                    new ChecklistEntry("I Compartment", true),
                    new ChecklistEntry("L Compartment", true),
                    new ChecklistEntry("T Compartment", true),
                    new ChecklistEntry("X Compartment", true),
                    new ChecklistEntry("I Glass Compartment", true),
                    new ChecklistEntry("L Glass Compartment", true),
                    new ChecklistEntry("Vertical Connector", true),
                }),
                new ChecklistGroup("BASE ROOMS (4)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Multipurpose Room (1)", false),
                    new ChecklistEntry("Scanner Room (3)", false),
                    new ChecklistEntry("Moonpool (2)", false),
                    new ChecklistEntry("Observatory (1)", false),
                }),
                new ChecklistGroup("BASE WALLS (3)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Hatch", true),
                    new ChecklistEntry("Window", true),
                    new ChecklistEntry("Reinforcement", true),
                }),
                new ChecklistGroup("EXTERIOR MODULES (3)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Solar Panel", true),
                    new ChecklistEntry("Thermal Plant (2)", false),
                    new ChecklistEntry("Power Transmitter (1)", false),
                }),
                new ChecklistGroup("EXTERIOR LIGHTS (2)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Floodlight (1)", false),
                    new ChecklistEntry("Spotlight (1)", false),
                }),
                new ChecklistGroup("EXTERIOR OTHER (2)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Exterior Growbed (1)", false),
                    new ChecklistEntry("Base-Attached Air Pump", true),
                }),
                new ChecklistGroup("INTERIOR INSTALLATION (4)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Ladder", true),
                    new ChecklistEntry("Water Filtration Machine (1)", false),
                    new ChecklistEntry("Bulkhead (1)", false),
                    new ChecklistEntry("Vehicle Upgrade Console", false),
                }),
                new ChecklistGroup("INTERIOR ROOMS (3)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Bioreactor (2)", false),
                    new ChecklistEntry("Nuclear Reactor (3)", false),
                    new ChecklistEntry("Alien Containment", false),
                }),
                new ChecklistGroup("INTERIOR MODULES (14)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Fabricator", true),
                    new ChecklistEntry("Radio", true),
                    new ChecklistEntry("Medical Kit Fabricator", true),
                    new ChecklistEntry("Wall Locker", true),
                    new ChecklistEntry("Locker", true),
                    new ChecklistEntry("Battery Charger (2)", false),
                    new ChecklistEntry("Power Cell Charger (2)", false),
                    new ChecklistEntry("Aquarium", true),
                    new ChecklistEntry("Modification Station (2)", false),
                    new ChecklistEntry("Basic Plant Pot (1)", false),
                    new ChecklistEntry("Composite Plant Pot (1)", false),
                    new ChecklistEntry("Chic Plant Pot (1)", false),
                    new ChecklistEntry("Indoor Growbed (1)", false),
                    new ChecklistEntry("Plant Shelf (1)", false),
                }),
                new ChecklistGroup("MISCELLANEOUS (19)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Bench (1)", false),
                    new ChecklistEntry("Basic Double Bed (1)", false),
                    new ChecklistEntry("Quilted Double Bed (1)", false),
                    new ChecklistEntry("Single Bed (1)", false),
                    new ChecklistEntry("Desk (1)", false),
                    new ChecklistEntry("Swivel Chair (1)", false),
                    new ChecklistEntry("Office Chair (1)", false),
                    new ChecklistEntry("Command Chair (1)", false),
                    new ChecklistEntry("Sign", true),
                    new ChecklistEntry("Picture Frame (1)", false),
                    new ChecklistEntry("Bar Table (1)", false),
                    new ChecklistEntry("Trash Can (1)", false),
                    new ChecklistEntry("Nuclear Waste Disposal (1)", false),
                    new ChecklistEntry("Vending Machine (1)", false),
                    new ChecklistEntry("Coffee Vending Machine (1)", false),
                    new ChecklistEntry("Counter (1)", false),
                    new ChecklistEntry("Wall Planter (1)", false),
                    new ChecklistEntry("Single Wall Shelf (1)", false),
                    new ChecklistEntry("Wall Shelves (1)", false),
                }),
                new ChecklistGroup("HULL PLATES (1)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("An Unusual Doll (1)", false),
                }),
            };

            var databankGroups = new List<ChecklistGroup>
            {
                new ChecklistGroup("ADVANCED THEORIES (16)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alien Eggs", false),
                    new ChecklistEntry("Bacterial Infection Report", false),
                    new ChecklistEntry("Enzyme Host Peeper", false),
                    new ChecklistEntry("Enzyme Host Peepers Leaving the Containment Facility", false),
                    new ChecklistEntry("Hatching Enzymes", false),
                    new ChecklistEntry("Hatching Enzymes Old", false),
                    new ChecklistEntry("Peepers Entering the Containment Facility", false),
                    new ChecklistEntry("Peepers Inside the Containment Facility", false),
                    new ChecklistEntry("Ray Species on 4546B", false),
                    new ChecklistEntry("Sea Dragon Egg", false),
                    new ChecklistEntry("Sea Emperor Egg Casing", false),
                    new ChecklistEntry("Sea Emperor Fetus", false),
                    new ChecklistEntry("Specimen with Infection Symptoms Inhibited", false),
                    new ChecklistEntry("Specimen with Symptoms of Infection", false),
                    new ChecklistEntry("The Sea Emperor's Eggs", false),
                    new ChecklistEntry("The Sea Emperor's Lifecycle", false),
                }),
                new ChecklistGroup("BLUEPRINTS (38)", new List<ChecklistEntry>
                {
                }),
                new ChecklistGroup("EQUIPMENT (13)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Air Pumps", true),
                    new ChecklistEntry("Creature Decoy", false),
                    new ChecklistEntry("Handheld Scanner", true),
                    new ChecklistEntry("Laser Cutter", false),
                    new ChecklistEntry("Light Stick", false),
                    new ChecklistEntry("Propulsion Cannon", false),
                    new ChecklistEntry("Radiation Suit", false),
                    new ChecklistEntry("Reinforced Dive Suit", false),
                    new ChecklistEntry("Repair Tool", true),
                    new ChecklistEntry("Repulsion Cannon", false),
                    new ChecklistEntry("Stasis Rifle", false),
                    new ChecklistEntry("Stillsuit", false),
                    new ChecklistEntry("Time Capsule", false),
                }),
                new ChecklistGroup("HABITAT INSTALLATIONS (15)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alien Containment", false),
                    new ChecklistEntry("Aquarium", true),
                    new ChecklistEntry("Bulkhead Door", false),
                    new ChecklistEntry("Exterior Growbed", false),
                    new ChecklistEntry("Fabricator", true),
                    new ChecklistEntry("Floodlight", false),
                    new ChecklistEntry("Habitat Builder", true),
                    new ChecklistEntry("Interior Growbed", false),
                    new ChecklistEntry("Modification Station", false),
                    new ChecklistEntry("Moonpool", false),
                    new ChecklistEntry("Planters & Pots", false),
                    new ChecklistEntry("Scanner Room", false),
                    new ChecklistEntry("Solar Panel", true),
                    new ChecklistEntry("Spotlight", false),
                    new ChecklistEntry("Water Filtration System", false),
                }),
                new ChecklistGroup("POWER (3)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Bioreactor", false),
                    new ChecklistEntry("Nuclear Reactor", false),
                    new ChecklistEntry("Thermal Plant", false),
                }),
                new ChecklistGroup("VEHICLES (6)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Cyclops", false),
                    new ChecklistEntry("Mobile Vehicle Bay", true),
                    new ChecklistEntry("Neptune Escape Rocket", false),
                    new ChecklistEntry("Prawn Suit (Mk.III)", false),
                    new ChecklistEntry("Seaglide", true),
                    new ChecklistEntry("Seamoth", false),
                }),
                new ChecklistGroup("DATA DOWNLOADS (110)", new List<ChecklistEntry>
                {
                }),
                new ChecklistGroup("ALIEN DATA (47)", new List<ChecklistEntry>
                {
                }),
                new ChecklistGroup("ARTIFACTS (12)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alien Building Block", false),
                    new ChecklistEntry("Alien Carving", false),
                    new ChecklistEntry("Alien Rifle", false),
                    new ChecklistEntry("Alien Statue", false),
                    new ChecklistEntry("Ancient Earth Blade", false),
                    new ChecklistEntry("Doomsday Device", false),
                    new ChecklistEntry("Holographic Projector", false),
                    new ChecklistEntry("Nanobots", false),
                    new ChecklistEntry("Organic Matter Particulator", false),
                    new ChecklistEntry("Rudimentary Tablet", false),
                    new ChecklistEntry("Tracking Implant", false),
                    new ChecklistEntry("Translation Device", false),
                }),
                new ChecklistGroup("SCAN DATA (21)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alien Arch", false),
                    new ChecklistEntry("Alien Flora Research", false),
                    new ChecklistEntry("Alien Robot", false),
                    new ChecklistEntry("Alien Thermal Plant", false),
                    new ChecklistEntry("Alien Vent", false),
                    new ChecklistEntry("Aquarium Arch", false),
                    new ChecklistEntry("Aquarium Pipe Access Point", false),
                    new ChecklistEntry("Blue Tablet", false),
                    new ChecklistEntry("Damaged Anchor Cable", false),
                    new ChecklistEntry("Energy Core", false),
                    new ChecklistEntry("Forcefield Control Terminal", false),
                    new ChecklistEntry("Ion Cube", false),
                    new ChecklistEntry("Ion Cube Fabricator", false),
                    new ChecklistEntry("Orange Tablet", false),
                    new ChecklistEntry("Production Line", false),
                    new ChecklistEntry("Purple Tablet", false),
                    new ChecklistEntry("Research Equipment", false),
                    new ChecklistEntry("Research Probes", false),
                    new ChecklistEntry("Self-Warping Quarantine Enforcer Unit", false),
                    new ChecklistEntry("Sonic Deterrent", false),
                    new ChecklistEntry("Warper Parts", false),
                }),
                new ChecklistGroup("TERMINAL DATA (14)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alien Biological History", false),
                    new ChecklistEntry("Alien Data Terminal", false),
                    new ChecklistEntry("Alien Sanctuary Alpha", false),
                    new ChecklistEntry("Alien Sanctuary Beta", false),
                    new ChecklistEntry("Damage Report", false),
                    new ChecklistEntry("Enforcement Platform Schematic", false),
                    new ChecklistEntry("Enzyme 42 Project Data", false),
                    new ChecklistEntry("Fauna Reproductive Data", false),
                    new ChecklistEntry("Fossil Data", false),
                    new ChecklistEntry("Ion Power Data", false),
                    new ChecklistEntry("Kharaa Contagion Profile", false),
                    new ChecklistEntry("Sea Emperor Leviathan Research Data", false),
                    new ChecklistEntry("Specimen Research Data", false),
                    new ChecklistEntry("Ventilation Control", false),
                }),
                new ChecklistGroup("AURORA SURVIVORS (17)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Aurora Engineering Drone - Log", false),
                    new ChecklistEntry("Aurora Scanner Room Voice Log", false),
                    new ChecklistEntry("Captain's Log", false),
                    new ChecklistEntry("Lifepod 12 Medical Offier Danby's Crew Log", false),
                    new ChecklistEntry("Lifepod 13 Emissary's Voicelog", false),
                    new ChecklistEntry("Lifepod 17 Crew Log", false),
                    new ChecklistEntry("Lifepod 19 Second Officer Keen's Crew Log", false),
                    new ChecklistEntry("Lifepod 19 Second Officer Keen's Voicelog", false),
                    new ChecklistEntry("Lifepod 2 Chief Technical Officer Yu's Voicelog (T+2min)", false),
                    new ChecklistEntry("Lifepod 3 Crew Log", false),
                    new ChecklistEntry("Lifepod 4 Crew Log", false),
                    new ChecklistEntry("Lifepod 6 Crew Log #1", false),
                    new ChecklistEntry("Lifepod 6 Crew Log #2", false),
                    new ChecklistEntry("Lifepod 7 Crew Log", false),
                    new ChecklistEntry("Profitability Projections", false),
                    new ChecklistEntry("Rendezvous Voicelog", false),
                    new ChecklistEntry("Surveillance Log, Leisure Deck B", false),
                }),
                new ChecklistGroup("CODES & CLUES (9)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alien Facility Locations", false),
                    new ChecklistEntry("High Security Terminal - Captain's Quarters", false),
                    new ChecklistEntry("Lab Access", false),
                    new ChecklistEntry("Lifepod 4 Transmission Origin", false),
                    new ChecklistEntry("Lifepod 6 Transmission Origin", false),
                    new ChecklistEntry("Lifepod 7 Transmission Origin", false),
                    new ChecklistEntry("Notes to self", false),
                    new ChecklistEntry("Primary Containment Facility", false),
                    new ChecklistEntry("Sweet Offer", false),
                }),
                new ChecklistGroup("DEGASI SURVIVORS (21)", new List<ChecklistEntry>
                {
                }),
                new ChecklistGroup("ALTERRA SEARCH & RESCUE MISSION (4)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Aurora Auxiliary Mission Orders", false),
                    new ChecklistEntry("Degasi Crew Manifest: Bart Torgal", false),
                    new ChecklistEntry("Degasi Crew Manifest: Marguerit Maida", false),
                    new ChecklistEntry("Degasi Crew Manifest: Paul Torgal", false),
                    new ChecklistEntry("Bart Torgal's Log #1 - This World", false),
                    new ChecklistEntry("Bart Torgal's Log #2 - Stalker Teeth", false),
                    new ChecklistEntry("Bart Torgal's Log #3 - Return From the Deep", false),
                    new ChecklistEntry("Degasi Voice Log #1 - Habitation Location", false),
                    new ChecklistEntry("Degasi Voice Log #2 - Storm!", false),
                    new ChecklistEntry("Degasi Voice Log #3 - Aftermath", false),
                    new ChecklistEntry("Degasi Voice Log #4 - Curious Discovery", false),
                    new ChecklistEntry("Degasi Voice Log #5 - Pecking Order", false),
                    new ChecklistEntry("Degasi Voice Log #6 - Deeper?!", false),
                    new ChecklistEntry("Degasi Voice Log #7 - Malady", false),
                    new ChecklistEntry("Degasi Voice Log #8 - Risk Taking", false),
                    new ChecklistEntry("Degasi Voice Log #9 - Disaster", false),
                    new ChecklistEntry("Environment Log", false),
                    new ChecklistEntry("Marguerit Maida's Log - Speaking Freely", false),
                    new ChecklistEntry("Paul Torgal's Log #1 - Marooned", false),
                    new ChecklistEntry("Paul Torgal's Log #2 - Dilemma", false),
                    new ChecklistEntry("Paul Torgal's Log #3 - The End", false),
                }),
                new ChecklistGroup("OPERATIONS LOGS (4)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alterra HQ - Last Recorded Transmissions", false),
                    new ChecklistEntry("Aurora Black Box Data", false),
                    new ChecklistEntry("Drive Core Shielding Breach", false),
                    new ChecklistEntry("VR Suite Log", false),
                }),
                new ChecklistGroup("PUBLIC DOCUMENTS (12)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Alterra Alms Pamphlet", false),
                    new ChecklistEntry("Alterra Citizen Testimonials", false),
                    new ChecklistEntry("Alterra Launches the Aurora", false),
                    new ChecklistEntry("Corporate Profile: Torgal Corp.", false),
                    new ChecklistEntry("Join Alterra's Board of Directors", false),
                    new ChecklistEntry("Relationship Contract Legal Recording", false),
                    new ChecklistEntry("Responsible Autonomous Relationships", false),
                    new ChecklistEntry("The Charter", false),
                    new ChecklistEntry("Today's Menu", false),
                    new ChecklistEntry("Trans-Gov Profile: Alterra Corp.", false),
                    new ChecklistEntry("Trans-Gov Profile: Mongolian Independent States", false),
                    new ChecklistEntry("What Can We Learn From the Hive Mind of Strader VI?", false),
                }),
                new ChecklistGroup("GEOLOGICAL DATA (12)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("4546B Environment Scan", false),
                    new ChecklistEntry("Crater Edge", false),
                    new ChecklistEntry("Crystalline Sulfur", false),
                    new ChecklistEntry("Kyanite", false),
                    new ChecklistEntry("Limestone Outcrops", false),
                    new ChecklistEntry("Magnetite", false),
                    new ChecklistEntry("Nickel Ore", false),
                    new ChecklistEntry("Ruby", false),
                    new ChecklistEntry("Sandstone Outcrops", false),
                    new ChecklistEntry("Scattered Wreckage", false),
                    new ChecklistEntry("Shale Outcrops", false),
                    new ChecklistEntry("Uraninite", false),
                }),
                new ChecklistGroup("INDIGENOUS LIFEFORMS (111)", new List<ChecklistEntry>
                {
                }),
                new ChecklistGroup("CORAL (7)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Brain Coral", false),
                    new ChecklistEntry("Coral Shell Plate", false),
                    new ChecklistEntry("Earthen Coral Tubes", false),
                    new ChecklistEntry("Giant Coral Tubes", false),
                    new ChecklistEntry("Pyrocoral", false),
                    new ChecklistEntry("Table Coral", false),
                    new ChecklistEntry("Tree Mushrooms", false),
                }),
                new ChecklistGroup("FAUNA (58)", new List<ChecklistEntry>
                {
                }),
                new ChecklistGroup("CARNIVORES (14)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Ampeel", false),
                    new ChecklistEntry("Biter", false),
                    new ChecklistEntry("Blighter", false),
                    new ChecklistEntry("Boneshark", false),
                    new ChecklistEntry("Crabsnake", false),
                    new ChecklistEntry("Crabsquid", false),
                    new ChecklistEntry("Crashfish", false),
                    new ChecklistEntry("Lava Lizard", false),
                    new ChecklistEntry("Mesmer", false),
                    new ChecklistEntry("River Prowler", false),
                    new ChecklistEntry("Sand Shark", false),
                    new ChecklistEntry("Stalker", false),
                    new ChecklistEntry("Stalker Teeth", false),
                    new ChecklistEntry("Warper", false),
                }),
                new ChecklistGroup("DECEASED (7)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Ancient Fossilized Skeleton", false),
                    new ChecklistEntry("Gargantuan Fossil", false),
                    new ChecklistEntry("Mixed Leviathan Fossils", false),
                    new ChecklistEntry("Reaper Leviathan Skeleton", false),
                    new ChecklistEntry("Remains of Research Specimen", false),
                    new ChecklistEntry("Rib Cage Samples", false),
                    new ChecklistEntry("Sea Dragon Skeleton", false),
                }),
                new ChecklistGroup("HERBIVORES - LARGE (7)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Crimson Ray", false),
                    new ChecklistEntry("Cuddlefish", false),
                    new ChecklistEntry("Gasopod", false),
                    new ChecklistEntry("Ghostray", false),
                    new ChecklistEntry("Jellyray", false),
                    new ChecklistEntry("Rabbit Ray", false),
                    new ChecklistEntry("Skyray", false),
                }),
                new ChecklistGroup("HERBIVORES - SMALL (14)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Bladderfish", false),
                    new ChecklistEntry("Boomerang", false),
                    new ChecklistEntry("Eyeye", false),
                    new ChecklistEntry("Garryfish", false),
                    new ChecklistEntry("Holefish", false),
                    new ChecklistEntry("Hoopfish", false),
                    new ChecklistEntry("Hoverfish", false),
                    new ChecklistEntry("Magmarang", false),
                    new ChecklistEntry("Oculus", false),
                    new ChecklistEntry("Peeper", false),
                    new ChecklistEntry("Red Eyeye", false),
                    new ChecklistEntry("Reginald", false),
                    new ChecklistEntry("Spadefish", false),
                    new ChecklistEntry("Spinefish", false),
                }),
                new ChecklistGroup("LEVIATHANS (7)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Ghost Leviathan", false),
                    new ChecklistEntry("Ghost Leviathan Juvenile", false),
                    new ChecklistEntry("Reaper Leviathan", false),
                    new ChecklistEntry("Reefback Leviathan", false),
                    new ChecklistEntry("Sea Dragon Leviathan", false),
                    new ChecklistEntry("Sea Emperor Juvenile", false),
                    new ChecklistEntry("Sea Treader Leviathan", false),
                }),
                new ChecklistGroup("SCAVENGERS & PARASITES (9)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Amoeboid", false),
                    new ChecklistEntry("Ancient Floater", false),
                    new ChecklistEntry("Bleeder", false),
                    new ChecklistEntry("Blood Crawler", false),
                    new ChecklistEntry("Cave Crawler", false),
                    new ChecklistEntry("Floater", false),
                    new ChecklistEntry("Lava Larva", false),
                    new ChecklistEntry("Rockgrub", false),
                    new ChecklistEntry("Shuttlebug", false),
                }),
                new ChecklistGroup("FLORA (46)", new List<ChecklistEntry>
                {
                }),
                new ChecklistGroup("EXPLOITABLE (12)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Acid Mushroom", false),
                    new ChecklistEntry("Bloodroot", false),
                    new ChecklistEntry("Bloodvine", false),
                    new ChecklistEntry("Bulbo Tree", false),
                    new ChecklistEntry("Chinese Potato Plant", false),
                    new ChecklistEntry("Creepvine", false),
                    new ChecklistEntry("Creepvine Seeds", false),
                    new ChecklistEntry("Deep Shroom", false),
                    new ChecklistEntry("Gel Sack", false),
                    new ChecklistEntry("Lantern Tree", false),
                    new ChecklistEntry("Marblemelon Plant", false),
                    new ChecklistEntry("Sulfur Plant", false),
                }),
                new ChecklistGroup("LAND (7)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Fern Palm", false),
                    new ChecklistEntry("Grub Basket", false),
                    new ChecklistEntry("Jaffa Cup", false),
                    new ChecklistEntry("Ming Plant", false),
                    new ChecklistEntry("Pink Cap", false),
                    new ChecklistEntry("Speckled Rattler", false),
                    new ChecklistEntry("Voxel Shrub", false),
                }),
                new ChecklistGroup("SEA (27)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("Anchor Pods", false),
                    new ChecklistEntry("Blue Palm", false),
                    new ChecklistEntry("Brine Lily", false),
                    new ChecklistEntry("Bulb Bush", false),
                    new ChecklistEntry("Cave Bush", false),
                    new ChecklistEntry("Crab Claw Kelp", false),
                    new ChecklistEntry("Drooping Stinger", false),
                    new ChecklistEntry("Eye Stalk", false),
                    new ChecklistEntry("Furled Papyrus", false),
                    new ChecklistEntry("Gabe's Feather", false),
                    new ChecklistEntry("Ghost Weed", false),
                    new ChecklistEntry("Giant Bulb Bush", false),
                    new ChecklistEntry("Giant Cove Tree", false),
                    new ChecklistEntry("Jellyshroom", false),
                    new ChecklistEntry("Membrain Tree", false),
                    new ChecklistEntry("Pygmy Fan", false),
                    new ChecklistEntry("Redwort", false),
                    new ChecklistEntry("Regress Shell", false),
                    new ChecklistEntry("Rouge Cradle", false),
                    new ChecklistEntry("Sea Crown", false),
                    new ChecklistEntry("Spiked Horn Grass", false),
                    new ChecklistEntry("Spotted Dockleaf", false),
                    new ChecklistEntry("Tiger Plant", false),
                    new ChecklistEntry("Tree Leech", false),
                    new ChecklistEntry("Veined Nettle", false),
                    new ChecklistEntry("Violet Beau", false),
                    new ChecklistEntry("Writhing Weed", false),
                }),
                new ChecklistGroup("SURVIVAL PACKAGE (6)", new List<ChecklistEntry>
                {
                    new ChecklistEntry("2-Berth Emergency Lifepod", true),
                    new ChecklistEntry("All-Environment Protection Suit", true),
                    new ChecklistEntry("Aurora Ship Status", true),
                    new ChecklistEntry("Start Here", true),
                    new ChecklistEntry("Survival Checklist", true),
                    new ChecklistEntry("WARNING: Blueprint Database Corrupted", true),
                }),
            };

            return new SubnauticaChecklistCatalog(blueprintGroups, databankGroups);
        }
    }
}
