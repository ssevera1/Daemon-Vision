// ClassSystem.cs — The Daemon's 7 darknet operative classes
// Each class has unique capabilities, skill trees, and level-gated abilities.
// Classes come from Sobol's MMORPG roots — but applied to the real world.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Social
{
    public class ClassSystem : SubsystemBase
    {
        public override string Name => "Classes";

        private readonly Dictionary<DarknetClass, ClassDefinition> classDefinitions
            = new Dictionary<DarknetClass, ClassDefinition>();

        private DarknetIdentityManager identityManager;

        public event Action<DarknetClass> OnClassChanged;

        protected override Task OnInitialize()
        {
            RegisterClassDefinitions();
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
        }

        public ClassDefinition GetClassDefinition(DarknetClass darknetClass)
        {
            classDefinitions.TryGetValue(darknetClass, out var def);
            return def;
        }

        /// <summary>
        /// Choose or change class. In the Daemon, class choice is significant —
        /// it determines what abilities and tools you can access.
        /// </summary>
        public bool ChooseClass(DarknetClass newClass)
        {
            if (identityManager?.LocalIdentity == null) return false;
            if (newClass == DarknetClass.Unassigned) return false;

            var identity = identityManager.LocalIdentity;

            if (!classDefinitions.TryGetValue(newClass, out var def))
                return false;

            if (identity.Level < def.MinimumLevel)
            {
                Warn($"Level {def.MinimumLevel} required for {newClass}. Current: {identity.Level}");
                return false;
            }

            identity.DarknetClass = newClass;
            identityManager.SaveLocalIdentity(identity);
            OnClassChanged?.Invoke(newClass);
            Log($"Class set to: {newClass}");
            return true;
        }

        /// <summary>
        /// Get abilities unlocked at the current level for a class.
        /// </summary>
        public List<ClassAbility> GetUnlockedAbilities(DarknetClass darknetClass, int level)
        {
            if (!classDefinitions.TryGetValue(darknetClass, out var def))
                return new List<ClassAbility>();

            return def.Abilities.FindAll(a => a.UnlockLevel <= level);
        }

        public List<ClassAbility> GetLockedAbilities(DarknetClass darknetClass, int level)
        {
            if (!classDefinitions.TryGetValue(darknetClass, out var def))
                return new List<ClassAbility>();

            return def.Abilities.FindAll(a => a.UnlockLevel > level);
        }

        private void RegisterClassDefinitions()
        {
            classDefinitions[DarknetClass.Fighter] = new ClassDefinition
            {
                Class = DarknetClass.Fighter,
                Name = "Fighter",
                Description = "Combat specialists. Tactical awareness, threat assessment, and defensive capabilities.",
                MinimumLevel = 1,
                PrimaryColor = new Color(1f, 0.3f, 0.2f),
                Abilities = new List<ClassAbility>
                {
                    new ClassAbility { Name = "Threat Scan", Description = "Enhanced threat detection range", UnlockLevel = 1 },
                    new ClassAbility { Name = "Tactical Overlay", Description = "Combat-relevant environmental highlighting", UnlockLevel = 5 },
                    new ClassAbility { Name = "Shield Wall", Description = "Mark and coordinate defensive perimeters", UnlockLevel = 15 },
                    new ClassAbility { Name = "AutoM8 Command", Description = "Direct autonomous defense units", UnlockLevel = 30 },
                    new ClassAbility { Name = "Razorback Link", Description = "Network with Razorback autonomous vehicles", UnlockLevel = 50 },
                }
            };

            classDefinitions[DarknetClass.Sorcerer] = new ClassDefinition
            {
                Class = DarknetClass.Sorcerer,
                Name = "Sorcerer",
                Description = "Hackers and tech specialists. Network manipulation, encryption, and digital warfare.",
                MinimumLevel = 1,
                PrimaryColor = new Color(0.5f, 0.2f, 1f),
                Abilities = new List<ClassAbility>
                {
                    new ClassAbility { Name = "Network Probe", Description = "Scan nearby wireless networks", UnlockLevel = 1 },
                    new ClassAbility { Name = "Encrypt Channel", Description = "Create encrypted communication channels", UnlockLevel = 5 },
                    new ClassAbility { Name = "Darknet Curse", Description = "Flag and track hostile operatives", UnlockLevel = 20 },
                    new ClassAbility { Name = "Invisibility Ring", Description = "Mask your D-Space presence from lower-level operatives", UnlockLevel = 40 },
                    new ClassAbility { Name = "System Override", Description = "Interface with compatible IoT/smart systems", UnlockLevel = 60 },
                }
            };

            classDefinitions[DarknetClass.Shaman] = new ClassDefinition
            {
                Class = DarknetClass.Shaman,
                Name = "Shaman",
                Description = "Community builders and healers. Mediation, resource management, and social infrastructure.",
                MinimumLevel = 1,
                PrimaryColor = new Color(0.2f, 0.8f, 0.4f),
                Abilities = new List<ClassAbility>
                {
                    new ClassAbility { Name = "Community Pulse", Description = "View local community health metrics", UnlockLevel = 1 },
                    new ClassAbility { Name = "Mediation Circle", Description = "Create dispute resolution spaces", UnlockLevel = 5 },
                    new ClassAbility { Name = "Resource Map", Description = "Overlay community resource locations", UnlockLevel = 10 },
                    new ClassAbility { Name = "Heal Network", Description = "Repair damaged mesh network connections", UnlockLevel = 25 },
                    new ClassAbility { Name = "Faction Diplomacy", Description = "Cross-faction communication privileges", UnlockLevel = 35 },
                }
            };

            classDefinitions[DarknetClass.Scout] = new ClassDefinition
            {
                Class = DarknetClass.Scout,
                Name = "Scout",
                Description = "Reconnaissance and intelligence. Extended sensor range, stealth, and information gathering.",
                MinimumLevel = 1,
                PrimaryColor = new Color(0.3f, 0.7f, 1f),
                Abilities = new List<ClassAbility>
                {
                    new ClassAbility { Name = "Extended Scan", Description = "Double detection range for people and objects", UnlockLevel = 1 },
                    new ClassAbility { Name = "Tracker", Description = "Place persistent tracking markers", UnlockLevel = 8 },
                    new ClassAbility { Name = "Low Profile", Description = "Reduced nameplate visibility to others", UnlockLevel = 15 },
                    new ClassAbility { Name = "Drone Link", Description = "Interface with surveillance drones", UnlockLevel = 30 },
                    new ClassAbility { Name = "Ghost Mode", Description = "Temporarily invisible in D-Space", UnlockLevel = 50 },
                }
            };

            classDefinitions[DarknetClass.Fabricator] = new ClassDefinition
            {
                Class = DarknetClass.Fabricator,
                Name = "Fabricator",
                Description = "Builders, makers, and engineers. Create physical and virtual infrastructure.",
                MinimumLevel = 1,
                PrimaryColor = new Color(1f, 0.7f, 0.1f),
                Abilities = new List<ClassAbility>
                {
                    new ClassAbility { Name = "Blueprint Overlay", Description = "View and share construction blueprints in AR", UnlockLevel = 1 },
                    new ClassAbility { Name = "Material Scanner", Description = "Identify materials and structural properties", UnlockLevel = 5 },
                    new ClassAbility { Name = "D-Space Architect", Description = "Create persistent virtual structures", UnlockLevel = 15 },
                    new ClassAbility { Name = "AutoM8 Builder", Description = "Design and program autonomous units", UnlockLevel = 35 },
                    new ClassAbility { Name = "Infrastructure Link", Description = "Interface with power grids and manufacturing", UnlockLevel = 50 },
                }
            };

            classDefinitions[DarknetClass.Journalist] = new ClassDefinition
            {
                Class = DarknetClass.Journalist,
                Name = "Journalist",
                Description = "Information and media. Document, broadcast, and verify information across the darknet.",
                MinimumLevel = 1,
                PrimaryColor = new Color(1f, 1f, 0.3f),
                Abilities = new List<ClassAbility>
                {
                    new ClassAbility { Name = "Record Mode", Description = "Capture and timestamp verifiable recordings", UnlockLevel = 1 },
                    new ClassAbility { Name = "Broadcast", Description = "Stream to darknet channels", UnlockLevel = 5 },
                    new ClassAbility { Name = "Fact Check", Description = "Cross-reference claims against darknet knowledge base", UnlockLevel = 10 },
                    new ClassAbility { Name = "Public Quest", Description = "Create and publish community quests", UnlockLevel = 20 },
                    new ClassAbility { Name = "Archive Access", Description = "Query the distributed darknet archive", UnlockLevel = 30 },
                }
            };

            classDefinitions[DarknetClass.Rogue] = new ClassDefinition
            {
                Class = DarknetClass.Rogue,
                Name = "Rogue",
                Description = "Covert operations. Stealth, deception, and unconventional tactics.",
                MinimumLevel = 5, // Higher entry barrier
                PrimaryColor = new Color(0.4f, 0.4f, 0.4f),
                Abilities = new List<ClassAbility>
                {
                    new ClassAbility { Name = "Spoof ID", Description = "Temporarily display a false callsign", UnlockLevel = 5 },
                    new ClassAbility { Name = "Dead Drop", Description = "Create hidden, encrypted message caches", UnlockLevel = 10 },
                    new ClassAbility { Name = "Shadow Step", Description = "Move without updating position to mesh network", UnlockLevel = 20 },
                    new ClassAbility { Name = "Counter Intel", Description = "Detect and neutralize tracking attempts", UnlockLevel = 35 },
                    new ClassAbility { Name = "Phantom Network", Description = "Create decoy mesh network nodes", UnlockLevel = 50 },
                }
            };
        }
    }

    [Serializable]
    public class ClassDefinition
    {
        public DarknetClass Class;
        public string Name;
        public string Description;
        public int MinimumLevel;
        public Color PrimaryColor;
        public List<ClassAbility> Abilities;
    }

    [Serializable]
    public class ClassAbility
    {
        public string Name;
        public string Description;
        public int UnlockLevel;
        public string IconId;
    }
}
