using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class MicroBioscanModule : PartModule
{
    [KSPField(isPersistant = false, guiActive = true, guiName = "Bioscan Status")]
    public string status = "Ready";

    // Tunable per part from config, e.g. in the part's MODULE node or an MM patch:
    //   bioscanCooldown = 6     (seconds between scans)
    //   ecPerScan = 30          (ElectricCharge used per scan)
    [KSPField]
    public float bioscanCooldown = 6f;

    [KSPField]
    public double ecPerScan = 30.0;

    private float lastRunTime = 0f;
    private float COOLDOWN => Mathf.Max(0f, bioscanCooldown);
    private double EC_COST => Math.Max(0.0, ecPerScan);

    // Detection chances loaded from PLANET_BIOSCAN nodes in cfg
    private static Dictionary<string, float> planetDetectionChances = null;
    private const float FALLBACK_CHANCE = 0.40f;

    // -------------------------------------------------------------------------
    // Detection chance — loaded once from cfg, cached for all instances
    // -------------------------------------------------------------------------

    private static void EnsureDetectionChancesLoaded()
    {
        if (planetDetectionChances != null) return;

        planetDetectionChances = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("PLANET_BIOSCAN");
        foreach (ConfigNode node in nodes)
        {
            string planet = node.GetValue("planet");
            string chanceStr = node.GetValue("detectionChance");

            if (string.IsNullOrEmpty(planet) || string.IsNullOrEmpty(chanceStr)) continue;

            if (float.TryParse(chanceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float chance))
            {
                planetDetectionChances[planet] = Mathf.Clamp01(chance);
                Debug.Log($"[Microbiology Expansion] Loaded detection chance for {planet}: {chance}");
            }
        }

        Debug.Log($"[Microbiology Expansion] Loaded {planetDetectionChances.Count} PLANET_BIOSCAN entries.");
    }

    private SituationRequirement GetSituationRequirement(Vessel.Situations vesselSituation)
    {
        switch (vesselSituation)
        {
            case Vessel.Situations.LANDED:
            case Vessel.Situations.SPLASHED:
            case Vessel.Situations.PRELAUNCH:
                return SituationRequirement.Surface;

            case Vessel.Situations.FLYING:
            case Vessel.Situations.SUB_ORBITAL:
                return SituationRequirement.InFlight;

            case Vessel.Situations.ORBITING:
            case Vessel.Situations.ESCAPING:
            case Vessel.Situations.DOCKED:
                return SituationRequirement.InSpace;

            default:
                return SituationRequirement.Any;
        }
    }

    private float GetDetectionChance(string bodyName)
    {
        EnsureDetectionChancesLoaded();

        if (planetDetectionChances.TryGetValue(bodyName, out float chance))
            return chance;

        Debug.LogWarning($"[Microbiology Expansion] No PLANET_BIOSCAN entry for '{bodyName}', using fallback {FALLBACK_CHANCE}.");
        return FALLBACK_CHANCE;
    }

    // -------------------------------------------------------------------------
    // Update — drives the PAW countdown timer
    // -------------------------------------------------------------------------

    public override void OnUpdate()
    {
        if (!HighLogic.LoadedSceneIsFlight) return;

        float elapsed = Time.time - lastRunTime;

        if (elapsed < COOLDOWN)
        {
            int remaining = Mathf.CeilToInt(COOLDOWN - elapsed);
            status = $"Recharging: {remaining}s";
        }
        else if (status.StartsWith("Recharging"))
        {
            // Cooldown just finished — reset to Ready
            status = "Ready";
        }
    }

    // -------------------------------------------------------------------------
    // Bioscan
    // -------------------------------------------------------------------------

    [KSPEvent(guiActive = true, guiActiveUnfocused = true, guiName = "Run Bioscan",
              active = true, externalToEVAOnly = false, unfocusedRange = 2f)]
    public void RunBioscan()
    {
        float elapsed = Time.time - lastRunTime;
        if (elapsed < COOLDOWN)
        {
            int remaining = Mathf.CeilToInt(COOLDOWN - elapsed);
            ScreenMessages.PostScreenMessage(
                $"Bioscan recharging... ({remaining}s remaining)",
                2f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // --- Pre-flight checks (before consuming resources) ---

        if (vessel == null || MicrobiologyDatabase.Instance == null)
        {
            status = "Error";
            ScreenMessages.PostScreenMessage(
                "[Microbiology] Database not loaded!",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // CommNet or crewed check
        bool hasCrew = vessel.GetCrewCount() > 0;
        bool hasSignal = vessel.connection != null && vessel.connection.IsConnected;

        if (!hasCrew && !hasSignal)
        {
            status = "No connection";
            ScreenMessages.PostScreenMessage(
                "<color=orange>Bioscan requires a CommNet connection or a Kerbal on board.</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Electric Charge check — request EC, abort if insufficient
        double ecReceived = part.RequestResource("ElectricCharge", EC_COST, ResourceFlowMode.ALL_VESSEL);
        if (ecReceived < EC_COST * 0.99)
        {
            // Refund whatever was taken and abort
            part.RequestResource("ElectricCharge", -ecReceived, ResourceFlowMode.ALL_VESSEL);
            status = "Insufficient power";
            ScreenMessages.PostScreenMessage(
                $"<color=orange>Bioscan requires {EC_COST} ElectricCharge. Not enough power.</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        lastRunTime = Time.time;
        status = "Scanning...";

        string bodyName = vessel.mainBody.bodyName;
        string biomeName = ScienceUtil.GetExperimentBiome(vessel.mainBody, vessel.latitude, vessel.longitude);

        // Map KSP vessel situation to our SituationRequirement enum
        SituationRequirement situation = GetSituationRequirement(vessel.situation);

        // Roll for detection
        float detectionChance = GetDetectionChance(bodyName);
        if (UnityEngine.Random.value > detectionChance)
        {
            status = "No signal";
            ScreenMessages.PostScreenMessage(
                "<color=yellow>Bioscan inconclusive — no biosignatures detected this attempt.</color>",
                6f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        List<Microbe> candidates = MicrobiologyDatabase.Instance.GetMicrobesInBiome(bodyName, biomeName, situation);

        if (candidates.Count == 0)
        {
            status = "No life detected";
            string situationHint = situation == SituationRequirement.Surface ? "Try landing or splashing down." :
                                   situation == SituationRequirement.InFlight ? "Try scanning while flying lower." :
                                   situation == SituationRequirement.InSpace ? "Try scanning from orbit." : "";
            ScreenMessages.PostScreenMessage(
                $"<color=yellow>No microbial activity detected for this situation. {situationHint}</color>",
                6f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        Microbe discovered = PickWeightedRandom(candidates);
        // Get the name of the kerbal running the bioscan
        string kerbalName = null;
        var crew = vessel.GetVesselCrew();
        if (crew != null && crew.Count > 0)
            kerbalName = crew[0].name;

        bool isNew = MicrobiologyDatabase.Instance.DiscoverMicrobe(discovered, biomeName, kerbalName);

        if (isNew)
        {
            switch (discovered.hazardLevel)
            {
                case HazardLevel.Hazardous:
                    status = "HAZARDOUS detected!";
                    ScreenMessages.PostScreenMessage(
                        $"<color=#FF4444><b>☣ BIOHAZARD DETECTED: {discovered.Name}</b></color>",
                        8f, ScreenMessageStyle.UPPER_CENTER);
                    ScreenMessages.PostScreenMessage(
                        "<color=#FF4444>WARNING: Hazardous biosignature confirmed. Extreme caution advised.</color>",
                        7f, ScreenMessageStyle.UPPER_CENTER);
                    break;

                case HazardLevel.Risky:
                    status = "Risky microbe detected!";
                    ScreenMessages.PostScreenMessage(
                        $"<color=#FFAA00><b>⚠ CAUTION — NEW DISCOVERY: {discovered.Name}</b></color>",
                        6f, ScreenMessageStyle.UPPER_CENTER);
                    ScreenMessages.PostScreenMessage(
                        "<color=#FFAA00>Positive biosignature confirmed. Exercise caution.</color>",
                        5f, ScreenMessageStyle.UPPER_CENTER);
                    break;

                default:
                    status = "New microbe detected!";
                    ScreenMessages.PostScreenMessage(
                        $"<color=#88FF88><b>NEW DISCOVERY: {discovered.Name}</b></color>",
                        6f, ScreenMessageStyle.UPPER_CENTER);
                    ScreenMessages.PostScreenMessage(
                        "<color=#FFAA00>Positive biosignature confirmed!</color>",
                        5f, ScreenMessageStyle.UPPER_CENTER);
                    break;
            }

            ScreenMessages.PostScreenMessage(
                $"<color=#FFAA00>+{discovered.AdjustedScienceValue} Science</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);

            if (MicrobiologyCodexUI.Instance != null)
                MicrobiologyCodexUI.Instance.ShowMicrobe(discovered);
        }
        else
        {
            status = "Known microbe";
            ScreenMessages.PostScreenMessage(
                $"<color=#AAAAAA>Detected: {discovered.Name} (already catalogued — no science awarded)</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
        }
    }

    // -------------------------------------------------------------------------
    // Weighted random pick
    // -------------------------------------------------------------------------

    private Microbe PickWeightedRandom(List<Microbe> candidates)
    {
        float totalWeight = 0f;
        foreach (var m in candidates)
            totalWeight += Mathf.Max(m.spawnChance, 0.01f);

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float cumulative = 0f;

        foreach (var m in candidates)
        {
            cumulative += Mathf.Max(m.spawnChance, 0.01f);
            if (roll <= cumulative)
                return m;
        }

        return candidates[candidates.Count - 1];
    }

    // -------------------------------------------------------------------------
    // Codex button + part description
    // -------------------------------------------------------------------------

    [KSPEvent(guiActive = true, guiActiveUnfocused = true, guiName = "Open Microbiology Codex",
              active = true, externalToEVAOnly = false, unfocusedRange = 2f)]
    public void OpenCodex()
    {
        if (MicrobiologyDatabase.Instance != null)
            MicrobiologyDatabase.Instance.ShowCodexUI();
        else
            ScreenMessages.PostScreenMessage(
                "[Microbiology] Codex unavailable!", 4f, ScreenMessageStyle.UPPER_CENTER);
    }

    public override string GetInfo()
    {
        return "Biosphere Scanner\n" +
               "Detects one microbe per use in the current biome.\n" +
               $"Cooldown: {COOLDOWN} seconds.\n" +
               $"Requires: {EC_COST} ElectricCharge per scan.\n" +
               "Requires: CommNet connection OR crewed vessel.\n" +
               "Awards science on first discovery only.\n" +
               "Rarer microbes are harder to find.\n" +
               "Detection chance varies by planet — see mission briefing.\n" +
               "<color=#FF4444>Hazardous microbes award bonus science but may pose risks.</color>";
    }
}