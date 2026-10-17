using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Added to the EVA kerbal via Module Manager patch.
/// Allows a Scientist on EVA to collect a microbe sample from the current biome.
/// The sample is stored as ScienceData on the EVA part, compatible with stock
/// science containers, transmission, and recovery systems.
/// </summary>
public class MicroSampleCollector : PartModule
{
    // Experiment ID must match the EXPERIMENT_DEFINITION in the cfg
    private const string EXPERIMENT_ID = "microbiologySample";
    private const int MAX_SAMPLES = 1; // EVA kerbal can only carry one sample at a time

    [KSPField(isPersistant = false, guiActive = false, guiName = "Sample Status")]
    public string sampleStatus = "No sample";

    // -------------------------------------------------------------------------
    // OnStart — show status field only on EVA
    // -------------------------------------------------------------------------

    public override void OnStart(StartState state)
    {
        base.OnStart(state);
        UpdateUI();
    }

    private void UpdateUI()
    {
        bool onEVA = vessel != null && vessel.isEVA;
        Fields["sampleStatus"].guiActive = onEVA;
        Events["CollectSample"].guiActive = onEVA && IsScientist();
        Events["CollectSample"].guiActiveUnfocused = false;
        Events["DiscardSample"].guiActive = onEVA && HasSample();
    }

    // -------------------------------------------------------------------------
    // EVA PAW buttons
    // -------------------------------------------------------------------------

    [KSPEvent(guiActive = false, guiName = "Collect Microbe Sample", active = true)]
    public void CollectSample()
    {
        if (!IsScientist())
        {
            ScreenMessages.PostScreenMessage(
                "<color=orange>Only a Scientist can collect microbe samples.</color>",
                4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        if (vessel == null || !vessel.isEVA)
        {
            ScreenMessages.PostScreenMessage(
                "<color=orange>Must be on EVA to collect samples.</color>",
                4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        if (MicrobiologyDatabase.Instance == null)
        {
            ScreenMessages.PostScreenMessage(
                "[Microbiology] Database not loaded!",
                4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Map the kerbal's situation to our enum
        SituationRequirement situation = GetEVASituation();

        if (situation == SituationRequirement.Any)
        {
            ScreenMessages.PostScreenMessage(
                "<color=orange>Cannot collect samples in this situation.</color>",
                4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        string bodyName = vessel.mainBody.bodyName;
        string biomeName = ScienceUtil.GetExperimentBiome(vessel.mainBody, vessel.latitude, vessel.longitude);

        List<Microbe> candidates = MicrobiologyDatabase.Instance.GetMicrobesInBiome(
            bodyName, biomeName, situation);

        if (candidates.Count == 0)
        {
            sampleStatus = "No sample";
            string situationHint =
                situation == SituationRequirement.Surface ? "Try scanning on the surface." :
                situation == SituationRequirement.InFlight ? "Try scanning while flying lower." :
                situation == SituationRequirement.InSpace ? "Try scanning from orbit." : "";
            ScreenMessages.PostScreenMessage(
                $"<color=yellow>No microbial life detectable here. {situationHint}</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Pick a microbe before checking duplicates so we can give a useful message
        Microbe microbe = PickWeightedRandom(candidates);

        // Scientist level gate
        int scientistLevel = GetScientistLevel();
        if (!CanCollectHazardLevel(microbe.hazardLevel, scientistLevel))
        {
            string reason = microbe.hazardLevel == HazardLevel.Hazardous
                ? $"<color=orange>{GetKerbalName()} doesn't feel safe collecting a sample of {microbe.name}. " +
                  $"A Level 3 Scientist is required for Hazardous specimens.</color>"
                : $"<color=orange>{GetKerbalName()} doesn't feel safe collecting a sample of {microbe.name}. " +
                  $"A Level 2 Scientist is required for Risky specimens.</color>";
            ScreenMessages.PostScreenMessage(reason, 6f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Block if already holding this exact species
        if (HasSampleOfMicrobe(microbe))
        {
            ScreenMessages.PostScreenMessage(
                $"<color=orange>Already carrying a sample of {microbe.name}. " +
                $"Transfer existing samples to a container before collecting again.</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Block if carrying the maximum number of samples
        if (SampleCount() >= MAX_SAMPLES)
        {
            ScreenMessages.PostScreenMessage(
                "<color=orange>Already carrying a sample. " +
                "Transfer it to an Experiment Storage Unit before collecting another.</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Build the ScienceSubject for this microbe + body + biome combination
        ScienceExperiment experiment = ResearchAndDevelopment.GetExperiment(EXPERIMENT_ID);
        if (experiment == null)
        {
            Debug.LogError("[Microbiology Expansion] EXPERIMENT_DEFINITION 'microbiologySample' not found!");
            ScreenMessages.PostScreenMessage(
                "[Microbiology] Experiment definition missing. Check MicrobiologyExperiment.cfg.",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Map to KSP experiment situation
        ExperimentSituations expSituation;
        switch (situation)
        {
            case SituationRequirement.InFlight:
                expSituation = ExperimentSituations.FlyingLow;
                break;
            case SituationRequirement.InSpace:
                expSituation = ExperimentSituations.InSpaceLow;
                break;
            default:
                expSituation = vessel.situation == Vessel.Situations.SPLASHED
                    ? ExperimentSituations.SrfSplashed
                    : ExperimentSituations.SrfLanded;
                break;
        }

        ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(
            experiment,
            expSituation,
            vessel.mainBody,
            biomeName,
            ""
        );

        // Science value = microbe's adjusted value, capped by subject's science cap
        float scienceValue = Mathf.Min(
            microbe.AdjustedScienceValue,
            subject.scienceCap - subject.science
        );
        scienceValue = Mathf.Max(scienceValue, 0f);

        // Build the data payload — transmit value is 0 (must be physically returned)
        // ScienceData(float amount, float xmitValue, float xmitBonus, string subjectID, string title)
        ScienceData data = new ScienceData(
            scienceValue,
            0f,
            0f,
            subject.id,
            $"Microbe Sample: {microbe.name} ({biomeName}, {bodyName})"
        );

        // Store on the EVA part using ModuleScienceContainer
        ModuleScienceContainer container = part.FindModuleImplementing<ModuleScienceContainer>();
        if (container != null)
        {
            container.AddData(data);
            int count = SampleCount();
            sampleStatus = count == 1
                ? $"1 sample: {microbe.name}"
                : $"{count} samples held";

            ScreenMessages.PostScreenMessage(
                $"<color=lime>Sample collected: <b>{microbe.name}</b></color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
            ScreenMessages.PostScreenMessage(
                $"<color=#FFAA00>Return to KSC to recover {scienceValue:F0} science.</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);

            // Register discovery without awarding science — recovery handles that
            string kerbalName = null;
            var evaKerbal = vessel.GetVesselCrew();
            if (evaKerbal != null && evaKerbal.Count > 0)
                kerbalName = evaKerbal[0].name;
            MicrobiologyDatabase.Instance.RegisterDiscovery(microbe, biomeName, kerbalName);
        }
        else
        {
            // Fallback: store directly on part's science data list
            part.AddModule("ModuleScienceContainer");
            container = part.FindModuleImplementing<ModuleScienceContainer>();
            if (container != null)
                container.AddData(data);

            int count = SampleCount();
            sampleStatus = count == 1
                ? $"1 sample: {microbe.name}"
                : $"{count} samples held";
            ScreenMessages.PostScreenMessage(
                $"<color=lime>Sample collected: <b>{microbe.name}</b></color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
        }

        UpdateUI();
    }

    [KSPEvent(guiActive = false, guiName = "Discard Microbe Sample", active = true)]
    public void DiscardSample()
    {
        ModuleScienceContainer container = part.FindModuleImplementing<ModuleScienceContainer>();
        if (container != null)
        {
            // Remove only our experiment's data
            var toRemove = new List<ScienceData>();
            foreach (ScienceData d in container.GetData())
                if (d.subjectID.StartsWith(EXPERIMENT_ID))
                    toRemove.Add(d);

            foreach (var d in toRemove)
                container.RemoveData(d);
        }

        sampleStatus = "No sample";
        ScreenMessages.PostScreenMessage(
            "<color=yellow>All microbe samples discarded.</color>",
            4f, ScreenMessageStyle.UPPER_CENTER);

        UpdateUI();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private bool IsScientist()
    {
        if (vessel == null || !vessel.isEVA) return false;
        ProtoCrewMember crew = vessel.GetVesselCrew().Count > 0
            ? vessel.GetVesselCrew()[0]
            : null;
        return crew != null && crew.trait == "Scientist";
    }

    private bool HasSample()
    {
        return SampleCount() > 0;
    }

    private bool HasSampleOfMicrobe(Microbe microbe)
    {
        ModuleScienceContainer container = part.FindModuleImplementing<ModuleScienceContainer>();
        if (container == null) return false;

        foreach (ScienceData d in container.GetData())
            if (d.subjectID.StartsWith(EXPERIMENT_ID) &&
                d.title.Contains(microbe.name))
                return true;

        return false;
    }

    private int SampleCount()
    {
        ModuleScienceContainer container = part.FindModuleImplementing<ModuleScienceContainer>();
        if (container == null) return 0;

        int count = 0;
        foreach (ScienceData d in container.GetData())
            if (d.subjectID.StartsWith(EXPERIMENT_ID))
                count++;
        return count;
    }

    private SituationRequirement GetEVASituation()
    {
        if (vessel == null) return SituationRequirement.Any;

        switch (vessel.situation)
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
                return SituationRequirement.InSpace;

            default:
                return SituationRequirement.Any; // DOCKED etc — block collection
        }
    }

    private int GetScientistLevel()
    {
        if (vessel == null || !vessel.isEVA) return 0;
        var crew = vessel.GetVesselCrew();
        if (crew == null || crew.Count == 0) return 0;
        return crew[0].experienceLevel;
    }

    private string GetKerbalName()
    {
        if (vessel == null || !vessel.isEVA) return "This Kerbal";
        var crew = vessel.GetVesselCrew();
        if (crew == null || crew.Count == 0) return "This Kerbal";
        return crew[0].name;
    }

    private bool CanCollectHazardLevel(HazardLevel hazard, int scientistLevel)
    {
        switch (hazard)
        {
            case HazardLevel.Safe: return scientistLevel >= 0; // any scientist
            case HazardLevel.Risky: return scientistLevel >= 2;
            case HazardLevel.Hazardous: return scientistLevel >= 3;
            default: return true;
        }
    }

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

    public override string GetInfo()
    {
        return "Microbe Sample Collector\n" +
               "Scientist EVA only.\n" +
               "Collect surface microbe samples for recovery science.\n" +
               "Samples cannot be transmitted — must be physically returned to KSC.";
    }
}