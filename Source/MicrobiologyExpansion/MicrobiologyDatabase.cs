using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =============================================================================
// MicrobiologyScenario — KSP ScenarioModule for persistent save/load
// This is the correct KSP pattern for mod data that must survive
// game sessions. KSP writes SCENARIO nodes directly into persistent.sfs
// and quicksaves, and calls OnSave/OnLoad automatically.
// =============================================================================

[KSPScenario(
    ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames,
    GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.EDITOR
)]
public class MicrobiologyScenario : ScenarioModule
{
    public override void OnSave(ConfigNode node)
    {
        if (MicrobiologyDatabase.Instance == null) return;
        MicrobiologyDatabase.Instance.SaveToNode(node);
        Debug.Log($"[Microbiology Expansion] Scenario saved.");
    }

    public override void OnLoad(ConfigNode node)
    {
        if (MicrobiologyDatabase.Instance == null) return;
        MicrobiologyDatabase.Instance.LoadFromNode(node);
        Debug.Log($"[Microbiology Expansion] Scenario loaded.");
    }
}

// =============================================================================
// MicrobiologyDatabase — persistent singleton, loaded at MainMenu
// =============================================================================

[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public class MicrobiologyDatabase : MonoBehaviour
{
    public static MicrobiologyDatabase Instance { get; private set; }

    // Static event fired when a genuinely new microbe is discovered and science awarded.
    // MicrobiologyRibbons subscribes here. Safe to fire even if Final Frontier isn't installed.
    // Signature: (Microbe discovered, string kerbalName)
    // kerbalName may be null when called from non-crew contexts.
    public static event Action<Microbe, string> OnMicrobeDiscovered;

    public List<Microbe> AllMicrobes { get; private set; } = new List<Microbe>();
    public List<Microbe> DiscoveredMicrobes { get; private set; } = new List<Microbe>();
    public float TotalMicrobeScienceEarned { get; private set; } = 0f;

    private Dictionary<string, double> discoveryTimestamps = new Dictionary<string, double>();
    private Dictionary<string, HashSet<string>> discoveryBiomes = new Dictionary<string, HashSet<string>>();
    private Dictionary<string, string> discoveryKerbals = new Dictionary<string, string>();
    // Tracks how many times each microbe sample has been physically recovered at KSC
    private Dictionary<string, int> sampleRecoveryCounts = new Dictionary<string, int>();
    // Tracks which microbes have been processed in the Mobile Processing Lab
    private HashSet<string> processedMicrobes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private const string SAVE_NODE_NAME = "MicrobiologyExpansion";

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadMicrobesFromConfig();
        GameEvents.onVesselRecovered.Add(OnVesselRecovered);
        GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
    }

    private void Start()
    {
        ValidateMicrobeConfigs();
    }

    private void OnLevelLoaded(GameScenes scene)
    {
        ValidateMicrobeConfigs();
    }

    private void OnDestroy()
    {
        GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
        GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);

        if (Instance == this)
            Instance = null;
    }

    // Called when the player recovers a vessel — scan science data for microbe samples
    private void OnVesselRecovered(ProtoVessel vessel, bool quick)
    {
        foreach (ProtoPartSnapshot part in vessel.protoPartSnapshots)
        {
            foreach (ProtoPartModuleSnapshot module in part.modules)
            {
                if (module.moduleName != "ModuleScienceContainer") continue;

                ConfigNode moduleData = module.moduleValues;
                foreach (ConfigNode scienceData in moduleData.GetNodes("ScienceData"))
                {
                    string subjectID = scienceData.GetValue("subjectID") ?? "";
                    if (!subjectID.StartsWith("microbiologySample")) continue;

                    // Extract microbe name from the title field
                    string title = scienceData.GetValue("title") ?? "";
                    // Title format: "Microbe Sample: {microbe.name} ({biomeName}, {bodyName})"
                    if (title.StartsWith("Microbe Sample: "))
                    {
                        string remainder = title.Substring("Microbe Sample: ".Length);
                        int parenIndex = remainder.IndexOf(" (");
                        string microbeName = parenIndex > 0
                            ? remainder.Substring(0, parenIndex)
                            : remainder;
                        IncrementSampleRecovery(microbeName.Trim());
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Config loading
    // -------------------------------------------------------------------------

    private void LoadMicrobesFromConfig()
    {
        AllMicrobes.Clear();

        ConfigNode[] microbeNodes = GameDatabase.Instance.GetConfigNodes("MICROBE");
        foreach (ConfigNode node in microbeNodes)
        {
            Microbe microbe = new Microbe();
            if (microbe.Load(node))
                AllMicrobes.Add(microbe);
        }

        Debug.Log($"[Microbiology Expansion] Loaded {AllMicrobes.Count} microbes from config.");
    }

    // -------------------------------------------------------------------------
    // Config validation
    // Checks every MICROBE against the planets actually loaded in this game
    // (stock or planet pack) and logs a warning for anything that can never
    // spawn: unknown planets, biome names the planet doesn't have, specific
    // biomes on a planet with no biome map, and unknown trophic prey.
    // Runs once, as soon as the celestial bodies are available.
    // -------------------------------------------------------------------------

    private static bool configsValidated = false;

    private void ValidateMicrobeConfigs()
    {
        if (configsValidated) return;
        if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
        configsValidated = true;

        int warnings = 0;
        foreach (Microbe m in AllMicrobes)
        {
            CelestialBody body = FlightGlobals.Bodies.Find(b =>
                b.bodyName.Equals(m.homePlanet, StringComparison.OrdinalIgnoreCase));
            if (body == null)
            {
                Debug.LogWarning($"[Microbiology Expansion] Config warning: '{m.name}' has homePlanet '{m.homePlanet}', which is not a body in this game. It will never spawn.");
                warnings++;
                continue;
            }

            if (!m.AllowsAnyBiome)
            {
                if (body.BiomeMap == null || body.BiomeMap.Attributes == null || body.BiomeMap.Attributes.Length == 0)
                {
                    Debug.LogWarning($"[Microbiology Expansion] Config warning: '{m.name}' lists specific biomes, but {body.bodyName} has no biomes. Use 'biomes = Any'. It will never spawn.");
                    warnings++;
                }
                else
                {
                    foreach (string biome in m.BiomeList)
                    {
                        bool found = false;
                        foreach (CBAttributeMapSO.MapAttribute att in body.BiomeMap.Attributes)
                        {
                            if (Microbe.BiomeNamesMatch(biome, att.name)) { found = true; break; }
                        }
                        if (!found)
                        {
                            Debug.LogWarning($"[Microbiology Expansion] Config warning: '{m.name}' lists biome '{biome}', which {body.bodyName} does not have. That entry will never match.");
                            warnings++;
                        }
                    }
                }
            }

            foreach (string prey in m.trophicRelations)
            {
                if (!AllMicrobes.Exists(x => x.name.Equals(prey, StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.LogWarning($"[Microbiology Expansion] Config warning: '{m.name}' lists trophic prey '{prey}', which is not a defined microbe.");
                    warnings++;
                }
            }
        }

        if (warnings == 0)
            Debug.Log($"[Microbiology Expansion] Config check passed: all {AllMicrobes.Count} microbes have valid planets and biomes.");
        else
            Debug.LogWarning($"[Microbiology Expansion] Config check found {warnings} problem(s). Search KSP.log for 'Config warning'.");
    }

    // -------------------------------------------------------------------------
    // Save / Load — called by MicrobiologyScenario
    // -------------------------------------------------------------------------

    public void SaveToNode(ConfigNode node)
    {
        node.AddValue("totalScienceEarned", TotalMicrobeScienceEarned);

        foreach (Microbe microbe in DiscoveredMicrobes)
        {
            ConfigNode entry = node.AddNode("DISCOVERY");
            entry.AddValue("name", microbe.name);

            double timestamp = discoveryTimestamps.ContainsKey(microbe.name)
                ? discoveryTimestamps[microbe.name] : 0.0;
            entry.AddValue("timestamp", timestamp);

            if (discoveryBiomes.ContainsKey(microbe.name))
                foreach (string biome in discoveryBiomes[microbe.name])
                    entry.AddValue("biome", biome);

            if (discoveryKerbals.ContainsKey(microbe.name))
                entry.AddValue("discoveredBy", discoveryKerbals[microbe.name]);

            if (sampleRecoveryCounts.ContainsKey(microbe.name))
                entry.AddValue("samplesRecovered", sampleRecoveryCounts[microbe.name]);
        }

        // Save processed microbes
        foreach (string name in processedMicrobes)
            node.AddValue("processed", name);

        Debug.Log($"[Microbiology Expansion] Saved {DiscoveredMicrobes.Count} discovered microbes.");
    }

    public void LoadFromNode(ConfigNode node)
    {
        DiscoveredMicrobes.Clear();
        discoveryTimestamps.Clear();
        discoveryBiomes.Clear();
        discoveryKerbals.Clear();
        sampleRecoveryCounts.Clear();
        processedMicrobes.Clear();
        TotalMicrobeScienceEarned = 0f;

        float.TryParse(node.GetValue("totalScienceEarned"), out float savedScience);
        TotalMicrobeScienceEarned = savedScience;

        foreach (ConfigNode entry in node.GetNodes("DISCOVERY"))
        {
            string microbeName = entry.GetValue("name");
            double.TryParse(entry.GetValue("timestamp"), out double timestamp);

            Microbe microbe = AllMicrobes.Find(m =>
                m.name.Equals(microbeName, StringComparison.OrdinalIgnoreCase));

            if (microbe != null && !DiscoveredMicrobes.Contains(microbe))
            {
                DiscoveredMicrobes.Add(microbe);
                discoveryTimestamps[microbe.name] = timestamp;

                string[] savedBiomes = entry.GetValues("biome");
                if (savedBiomes.Length > 0)
                    discoveryBiomes[microbe.name] = new HashSet<string>(
                        savedBiomes, StringComparer.OrdinalIgnoreCase);

                string discoveredBy = entry.GetValue("discoveredBy");
                if (!string.IsNullOrEmpty(discoveredBy))
                    discoveryKerbals[microbe.name] = discoveredBy;

                string recoveryCountStr = entry.GetValue("samplesRecovered");
                if (!string.IsNullOrEmpty(recoveryCountStr) &&
                    int.TryParse(recoveryCountStr, out int recoveryCount) && recoveryCount > 0)
                    sampleRecoveryCounts[microbe.name] = recoveryCount;
            }
            else if (microbe == null)
                Debug.LogWarning($"[Microbiology Expansion] Save referenced unknown microbe: {microbeName}");
        }

        // Backwards compatibility — old flat "discovered" format
        if (DiscoveredMicrobes.Count == 0)
        {
            foreach (string microbeName in node.GetValues("discovered"))
            {
                Microbe microbe = AllMicrobes.Find(m =>
                    m.name.Equals(microbeName, StringComparison.OrdinalIgnoreCase));

                if (microbe != null && !DiscoveredMicrobes.Contains(microbe))
                {
                    DiscoveredMicrobes.Add(microbe);
                    discoveryTimestamps[microbe.name] = 0.0;
                }
            }
        }

        // Load processed microbes
        foreach (string name in node.GetValues("processed"))
            if (!string.IsNullOrEmpty(name))
                processedMicrobes.Add(name);

        Debug.Log($"[Microbiology Expansion] Loaded {DiscoveredMicrobes.Count} discovered microbes from save.");
    }

    // -------------------------------------------------------------------------
    // Discovery + Science
    // -------------------------------------------------------------------------

    public bool DiscoverMicrobe(Microbe microbe, string biomeName = null, string kerbalName = null)
    {
        if (microbe == null) return false;

        bool isNew = !DiscoveredMicrobes.Contains(microbe);

        if (isNew)
        {
            DiscoveredMicrobes.Add(microbe);
            discoveryTimestamps[microbe.name] = Planetarium.GetUniversalTime();
            if (!string.IsNullOrEmpty(kerbalName))
                discoveryKerbals[microbe.name] = kerbalName;
            AwardScience(microbe, kerbalName);
            AwardExperience(microbe, kerbalName);
        }

        if (!string.IsNullOrEmpty(biomeName))
        {
            if (!discoveryBiomes.ContainsKey(microbe.name))
                discoveryBiomes[microbe.name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            discoveryBiomes[microbe.name].Add(biomeName);
        }

        return isNew;
    }

    private void AwardScience(Microbe microbe, string kerbalName = null)
    {
        float science = microbe.AdjustedScienceValue;

        if (ResearchAndDevelopment.Instance != null)
        {
            ResearchAndDevelopment.Instance.AddScience(science, TransactionReasons.ScienceTransmission);
            TotalMicrobeScienceEarned += science;
            Debug.Log($"[Microbiology Expansion] Awarded {science} science for {microbe.name}.");
        }
        else
        {
            TotalMicrobeScienceEarned += science;
            Debug.LogWarning("[Microbiology Expansion] R&D unavailable — sandbox mode.");
        }

        // Notify ribbon tracker and any other subscribers
        OnMicrobeDiscovered?.Invoke(microbe, kerbalName);
    }

    private void AwardExperience(Microbe microbe, string kerbalName)
    {
        if (string.IsNullOrEmpty(kerbalName)) return;
        if (HighLogic.CurrentGame == null) return;

        ProtoCrewMember crew = HighLogic.CurrentGame.CrewRoster[kerbalName];
        if (crew == null) return;

        // XP values by hazard level
        float xp;
        string entryType;
        switch (microbe.hazardLevel)
        {
            case HazardLevel.Hazardous:
                xp = 10f;
                entryType = "MicrobiologyHazardousDiscovery";
                break;
            case HazardLevel.Risky:
                xp = 5f;
                entryType = "MicrobiologyRiskyDiscovery";
                break;
            default:
                xp = 1f;
                entryType = "MicrobologySafeDiscovery";
                break;
        }

        // Add flight log entry — KSP uses this to calculate experience
        crew.flightLog.AddEntry(entryType, microbe.homePlanet);
        crew.UpdateExperience();

        Debug.Log($"[Microbiology Expansion] Awarded {xp} XP to {kerbalName} " +
                  $"for discovering {microbe.name} ({microbe.hazardLevel}).");
    }

    // Registers discovery and biome without awarding science (used by sample collection)
    public bool RegisterDiscovery(Microbe microbe, string biomeName = null, string kerbalName = null)
    {
        if (microbe == null) return false;

        bool isNew = !DiscoveredMicrobes.Contains(microbe);

        if (isNew)
        {
            DiscoveredMicrobes.Add(microbe);
            discoveryTimestamps[microbe.name] = Planetarium.GetUniversalTime();
            if (!string.IsNullOrEmpty(kerbalName))
                discoveryKerbals[microbe.name] = kerbalName;
        }

        if (!string.IsNullOrEmpty(biomeName))
        {
            if (!discoveryBiomes.ContainsKey(microbe.name))
                discoveryBiomes[microbe.name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            discoveryBiomes[microbe.name].Add(biomeName);
        }

        return isNew;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    public List<Microbe> GetMicrobesInBiome(string planetName, string biomeName,
        SituationRequirement situation = SituationRequirement.Any)
    {
        // biomeName is empty on bodies with no biome map (Jool, the Sun).
        // There, only microbes with 'biomes = Any' can be found.
        return AllMicrobes.FindAll(m =>
            (string.IsNullOrEmpty(planetName) ||
                m.homePlanet.Equals(planetName, StringComparison.OrdinalIgnoreCase)) &&
            m.MatchesBiome(biomeName) &&
            m.AllowsSituation(situation)
        );
    }

    public string GetDiscoveryDate(Microbe microbe)
    {
        if (!discoveryTimestamps.ContainsKey(microbe.name)) return null;
        double ut = discoveryTimestamps[microbe.name];
        if (ut <= 0) return "Date unknown";
        return KSPUtil.PrintDate(ut, true, false);
    }

    public List<string> GetDiscoveryBiomes(Microbe microbe)
    {
        if (!discoveryBiomes.ContainsKey(microbe.name)) return new List<string>();
        List<string> sorted = new List<string>(discoveryBiomes[microbe.name]);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    // Returns the name of the kerbal who first discovered this microbe, or null
    public string GetDiscoverer(Microbe microbe)
    {
        if (!discoveryKerbals.ContainsKey(microbe.name)) return null;
        return discoveryKerbals[microbe.name];
    }

    // Mark a microbe as processed in the Mobile Processing Lab
    public bool ProcessMicrobe(Microbe microbe)
    {
        if (microbe == null) return false;
        if (processedMicrobes.Contains(microbe.name)) return false;
        processedMicrobes.Add(microbe.name);
        Debug.Log($"[Microbiology Expansion] {microbe.name} processed in lab.");
        return true;
    }

    public bool IsMicrobeProcessed(Microbe microbe)
    {
        return microbe != null && processedMicrobes.Contains(microbe.name);
    }

    // Returns trophic relations that have been revealed for this microbe.
    // A relation is revealed when BOTH this microbe AND the prey microbe have been processed.
    public List<string> GetRevealedTrophicRelations(Microbe microbe)
    {
        List<string> revealed = new List<string>();
        if (microbe == null || !IsMicrobeProcessed(microbe)) return revealed;

        foreach (string preyName in microbe.trophicRelations)
        {
            Microbe prey = AllMicrobes.Find(m =>
                m.name.Equals(preyName, StringComparison.OrdinalIgnoreCase));
            // Reveal if the prey has also been processed
            if (prey != null && IsMicrobeProcessed(prey))
                revealed.Add(prey.name);
        }
        return revealed;
    }

    // Returns true if any trophic relations exist but are not yet revealed
    public bool HasHiddenTrophicRelations(Microbe microbe)
    {
        if (microbe == null || microbe.trophicRelations.Count == 0) return false;
        if (!IsMicrobeProcessed(microbe)) return microbe.trophicRelations.Count > 0;
        return GetRevealedTrophicRelations(microbe).Count < microbe.trophicRelations.Count;
    }

    // Call this when a microbe sample is recovered at KSC
    public void IncrementSampleRecovery(string microbeName)
    {
        if (string.IsNullOrEmpty(microbeName)) return;
        if (!sampleRecoveryCounts.ContainsKey(microbeName))
            sampleRecoveryCounts[microbeName] = 0;
        sampleRecoveryCounts[microbeName]++;
        Debug.Log($"[Microbiology Expansion] Sample recovery recorded for {microbeName}. Total: {sampleRecoveryCounts[microbeName]}");
    }

    public int GetSampleRecoveryCount(Microbe microbe)
    {
        if (microbe == null) return 0;
        return sampleRecoveryCounts.ContainsKey(microbe.name)
            ? sampleRecoveryCounts[microbe.name] : 0;
    }

    // Discovered life count — excludes Debris category
    public int DiscoveredLifeCount =>
        DiscoveredMicrobes.FindAll(m =>
            !m.category.Equals("Debris", StringComparison.OrdinalIgnoreCase)).Count;

    // Discovered debris count
    public int DiscoveredDebrisCount =>
        DiscoveredMicrobes.FindAll(m =>
            m.category.Equals("Debris", StringComparison.OrdinalIgnoreCase)).Count;

    // Total life microbes (excludes Debris)
    public int TotalLifeCount =>
        AllMicrobes.FindAll(m =>
            !m.category.Equals("Debris", StringComparison.OrdinalIgnoreCase)).Count;

    // Total debris entries
    public int TotalDebrisCount =>
        AllMicrobes.FindAll(m =>
            m.category.Equals("Debris", StringComparison.OrdinalIgnoreCase)).Count;

    public void ShowCodexUI()
    {
        if (MicrobiologyCodexUI.Instance != null)
            MicrobiologyCodexUI.Instance.ToggleCodex();
    }

    public void DebugDiscoverAllMicrobes()
    {
        foreach (var microbe in AllMicrobes)
            DiscoverMicrobe(microbe);
    }
}

// =============================================================================
// Microbe data class
// =============================================================================

public enum HazardLevel { Safe, Risky, Hazardous }
public enum SituationRequirement { Any, Surface, InFlight, InSpace }

public class Microbe
{
    public string name;
    public string category;
    public string homePlanet;
    public string biomes;
    public string flavorText;
    public float spawnChance;
    public float scienceValue;
    public string iconPath;
    public HazardLevel hazardLevel;
    public SituationRequirement situation; // first listed situation (kept for display/compatibility)
    public HashSet<SituationRequirement> allowedSituations = new HashSet<SituationRequirement>();
    public List<string> BiomeList = new List<string>();
    public bool AllowsAnyBiome = true;
    public List<string> trophicRelations = new List<string>(); // names of microbes this one consumes

    private static readonly Dictionary<HazardLevel, float> HazardMultipliers =
        new Dictionary<HazardLevel, float>
    {
        { HazardLevel.Safe,      1.0f },
        { HazardLevel.Risky,     1.5f },
        { HazardLevel.Hazardous, 3.0f },
    };

    public float AdjustedScienceValue => scienceValue * HazardMultipliers[hazardLevel];

    public bool Load(ConfigNode node)
    {
        name = node.GetValue("name") ?? "Unknown Microbe";
        category = node.GetValue("category") ?? "Unknown";
        homePlanet = node.GetValue("homePlanet") ?? "Unknown";
        biomes = node.GetValue("biomes") ?? "Any";
        flavorText = node.GetValue("flavorText") ?? "No data available.";
        iconPath = node.GetValue("icon") ?? "";

        // Config files always use '.' as the decimal separator, whatever the player's locale
        float.TryParse(node.GetValue("spawnChance"), NumberStyles.Float, CultureInfo.InvariantCulture, out spawnChance);
        float.TryParse(node.GetValue("scienceValue"), NumberStyles.Float, CultureInfo.InvariantCulture, out scienceValue);
        if (scienceValue <= 0) scienceValue = 25f;

        string hazardStr = node.GetValue("hazardLevel") ?? "Safe";
        if (!Enum.TryParse(hazardStr, true, out hazardLevel))
            hazardLevel = HazardLevel.Safe;

        // Biomes: comma-separated list, or Any
        BiomeList.Clear();
        AllowsAnyBiome = false;
        foreach (string entry in biomes.Split(','))
        {
            string trimmed = entry.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.Equals("Any", StringComparison.OrdinalIgnoreCase)) AllowsAnyBiome = true;
            else BiomeList.Add(trimmed);
        }
        if (BiomeList.Count == 0) AllowsAnyBiome = true;

        // Situations: comma-separated list (e.g. "Surface,InFlight"), or Any.
        // Each value is parsed on its own. Enum.TryParse on the whole string
        // would combine the values numerically (Surface + InFlight = InSpace).
        allowedSituations.Clear();
        situation = SituationRequirement.Any;
        string situationStr = node.GetValue("situation") ?? "Any";
        foreach (string entry in situationStr.Split(','))
        {
            string trimmed = entry.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            SituationRequirement parsed;
            if (Array.Exists(Enum.GetNames(typeof(SituationRequirement)),
                    n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                parsed = (SituationRequirement)Enum.Parse(typeof(SituationRequirement), trimmed, true);
                if (allowedSituations.Count == 0) situation = parsed;
                allowedSituations.Add(parsed);
            }
            else
            {
                Debug.LogWarning($"[Microbiology Expansion] Config warning: '{name}' has unknown situation '{trimmed}'. Valid values: Surface, InFlight, InSpace, Any.");
            }
        }
        if (allowedSituations.Count == 0 || allowedSituations.Contains(SituationRequirement.Any))
        {
            allowedSituations.Clear();
            allowedSituations.Add(SituationRequirement.Any);
            situation = SituationRequirement.Any;
        }

        // Parse comma-separated trophic relations
        string trophicStr = node.GetValue("trophicRelations") ?? "";
        trophicRelations.Clear();
        if (!string.IsNullOrEmpty(trophicStr))
        {
            foreach (string entry in trophicStr.Split(','))
            {
                string trimmed = entry.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    trophicRelations.Add(trimmed);
            }
        }

        return !string.IsNullOrEmpty(name);
    }

    public bool AllowsSituation(SituationRequirement current)
    {
        return allowedSituations.Contains(SituationRequirement.Any) || allowedSituations.Contains(current);
    }

    // Exact match against each listed biome (not a substring search, so
    // "Canyons" no longer matches "any" and "Highlands" no longer matches
    // "Polar Highlands"). An empty biomeName means the body has no biomes.
    public bool MatchesBiome(string biomeName)
    {
        if (AllowsAnyBiome) return true;
        if (string.IsNullOrEmpty(biomeName)) return false;
        foreach (string b in BiomeList)
            if (BiomeNamesMatch(b, biomeName)) return true;
        return false;
    }

    // Biome names are compared ignoring case and spaces, so "Ice Caps",
    // "IceCaps" and "ice caps" are all the same biome.
    public static bool BiomeNamesMatch(string a, string b)
    {
        if (a == null || b == null) return false;
        return string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    public string Name => name;
    public string DisplayName => name;
    public string Description => flavorText;
    public string CategoryTag => $"{category} — {homePlanet}";
    public string ScienceDisplay => $"+{AdjustedScienceValue} Science";

    public string HazardDisplay
    {
        get
        {
            switch (hazardLevel)
            {
                case HazardLevel.Risky: return "<color=#FFAA00>⚠ Risky</color>";
                case HazardLevel.Hazardous: return "<color=#FF4444>☣ Hazardous</color>";
                default: return "<color=#88FF88>✓ Safe</color>";
            }
        }
    }

    public int HazardSortKey => (int)hazardLevel;

    private Texture2D _iconTexture;
    public Texture2D IconTexture
    {
        get
        {
            if (_iconTexture == null && !string.IsNullOrEmpty(iconPath))
                _iconTexture = GameDatabase.Instance.GetTexture(
                    iconPath.Replace(".png", ""), false);
            return _iconTexture;
        }
    }
}