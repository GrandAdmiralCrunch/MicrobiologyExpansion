using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Added to the Mobile Processing Lab via Module Manager patch.
/// Processes microbiologySample ScienceData over time based on scientist level,
/// reveals trophic relations, and awards science on completion.
/// Uses KSP universal time so processing continues through time warp.
/// </summary>
public class MicroLabProcessor : PartModule
{
    private const string EXPERIMENT_ID = "microbiologySample";
    private const double EC_PER_SAMPLE = 50.0;

    // Processing time in seconds per sample by scientist level
    // Level 0: 3 weeks, 1: 1 week, 2: 5 days, 3: 3 days, 4: 1 day, 5: 5 hours
    private static readonly double[] ProcessingTimePerSample = new double[]
    {
        21 * 6 * 3600,  // Level 0 — 3 weeks  (KSP day = 6 hours)
         7 * 6 * 3600,  // Level 1 — 1 week
         5 * 6 * 3600,  // Level 2 — 5 days
         3 * 6 * 3600,  // Level 3 — 3 days
         1 * 6 * 3600,  // Level 4 — 1 day
         5 * 3600,      // Level 5 — 5 hours
    };

    // Science multiplier for processing — bonus for lab analysis
    private const float PROCESSING_SCIENCE_MULTIPLIER = 0.5f; // 50% of original sample value

    // -------------------------------------------------------------------------
    // Persistent state — survives scene changes and quicksaves
    // -------------------------------------------------------------------------

    [KSPField(isPersistant = true)]
    public bool isProcessing = false;

    [KSPField(isPersistant = true)]
    public double processingStartUT = 0.0;

    [KSPField(isPersistant = true)]
    public double processingEndUT = 0.0;

    [KSPField(isPersistant = true)]
    public int samplesBeingProcessed = 0;

    // Comma-separated microbe names queued for processing — persisted as string
    [KSPField(isPersistant = true)]
    public string processingQueueStr = "";

    // -------------------------------------------------------------------------
    // PAW display fields
    // -------------------------------------------------------------------------

    [KSPField(isPersistant = false, guiActive = true, guiName = "Lab Status")]
    public string processorStatus = "Idle";

    [KSPField(isPersistant = false, guiActive = false, guiName = "Time Remaining")]
    public string timeRemainingStr = "";

    // -------------------------------------------------------------------------
    // OnUpdate — drives timer display and completion check
    // -------------------------------------------------------------------------

    public override void OnUpdate()
    {
        if (!HighLogic.LoadedSceneIsFlight) return;

        if (isProcessing)
        {
            double now = Planetarium.GetUniversalTime();
            double remaining = processingEndUT - now;

            if (remaining <= 0)
            {
                CompleteProcessing();
                return;
            }

            processorStatus = $"Processing {samplesBeingProcessed} sample(s)...";
            timeRemainingStr = FormatTime(remaining);
            Fields["timeRemainingStr"].guiActive = true;

            Events["StartProcessing"].guiActive = false;
            Events["CancelProcessing"].guiActive = true;
        }
        else
        {
            Fields["timeRemainingStr"].guiActive = false;
            Events["CancelProcessing"].guiActive = false;

            int unprocessed = CountUnprocessedSamples();
            int total = CountMicrobeSamples();

            if (total == 0)
                processorStatus = "No microbe samples stored";
            else if (unprocessed == 0)
                processorStatus = $"All {total} sample(s) processed";
            else
                processorStatus = $"{unprocessed} unprocessed / {total} total";

            Events["StartProcessing"].guiActive = unprocessed > 0 && HasScientist();
        }
    }

    // -------------------------------------------------------------------------
    // PAW buttons
    // -------------------------------------------------------------------------

    [KSPEvent(guiActive = true, guiName = "Start Processing", active = true)]
    public void StartProcessing()
    {
        if (MicrobiologyDatabase.Instance == null)
        {
            ScreenMessages.PostScreenMessage(
                "[Microbiology] Database not loaded!", 4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        if (!HasScientist())
        {
            ScreenMessages.PostScreenMessage(
                "<color=orange>A Scientist crew member is required to process microbe samples.</color>",
                4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        if (isProcessing)
        {
            ScreenMessages.PostScreenMessage(
                "<color=yellow>Processing already in progress.</color>",
                4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        List<ScienceData> unprocessed = GetUnprocessedSamples();
        if (unprocessed.Count == 0)
        {
            ScreenMessages.PostScreenMessage(
                "<color=yellow>No unprocessed microbe samples found.</color>",
                4f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // EC check upfront
        double totalEC = EC_PER_SAMPLE * unprocessed.Count;
        double ecReceived = part.RequestResource("ElectricCharge", totalEC, ResourceFlowMode.ALL_VESSEL);
        if (ecReceived < totalEC * 0.99)
        {
            part.RequestResource("ElectricCharge", -ecReceived, ResourceFlowMode.ALL_VESSEL);
            ScreenMessages.PostScreenMessage(
                $"<color=orange>Insufficient power. Processing requires {totalEC:F0} EC " +
                $"({EC_PER_SAMPLE:F0} per sample × {unprocessed.Count} samples).</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }

        // Calculate total processing time based on best scientist on board
        int level = GetBestScientistLevel();
        int clampedLevel = Mathf.Clamp(level, 0, ProcessingTimePerSample.Length - 1);
        double timePerSample = ProcessingTimePerSample[clampedLevel];
        double totalTime = timePerSample * unprocessed.Count;

        // Store queue as comma-separated microbe names
        var names = new List<string>();
        foreach (ScienceData d in unprocessed)
        {
            Microbe m = GetMicrobeFromScienceData(d);
            if (m != null) names.Add(m.name);
        }
        processingQueueStr = string.Join(",", names);
        samplesBeingProcessed = unprocessed.Count;
        processingStartUT = Planetarium.GetUniversalTime();
        processingEndUT = processingStartUT + totalTime;
        isProcessing = true;

        string timeStr = FormatTime(totalTime);
        ScreenMessages.PostScreenMessage(
            $"<color=#44CCFF>Processing {unprocessed.Count} sample(s) started. " +
            $"Estimated time: {timeStr} (Scientist Level {level}).</color>",
            7f, ScreenMessageStyle.UPPER_CENTER);

        Debug.Log($"[Microbiology Expansion] Processing started. {unprocessed.Count} samples, " +
                  $"level {level}, duration {timeStr}.");
    }

    [KSPEvent(guiActive = false, guiName = "Cancel Processing", active = true)]
    public void CancelProcessing()
    {
        if (!isProcessing) return;

        isProcessing = false;
        processingQueueStr = "";
        samplesBeingProcessed = 0;
        processingStartUT = 0;
        processingEndUT = 0;

        ScreenMessages.PostScreenMessage(
            "<color=yellow>Processing cancelled. Samples remain unprocessed.</color>",
            5f, ScreenMessageStyle.UPPER_CENTER);
    }

    // -------------------------------------------------------------------------
    // Completion — called when timer expires
    // -------------------------------------------------------------------------

    private void CompleteProcessing()
    {
        isProcessing = false;

        if (MicrobiologyDatabase.Instance == null)
        {
            processingQueueStr = "";
            return;
        }

        // Parse the queued microbe names
        List<string> queuedNames = new List<string>(
            processingQueueStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

        int processedCount = 0;
        int newRelationsCount = 0;
        float totalScience = 0f;

        foreach (string microbeName in queuedNames)
        {
            Microbe microbe = MicrobiologyDatabase.Instance.AllMicrobes.Find(m =>
                m.name.Equals(microbeName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (microbe == null) continue;

            int relationsBefore = MicrobiologyDatabase.Instance
                .GetRevealedTrophicRelations(microbe).Count;

            bool isNew = MicrobiologyDatabase.Instance.ProcessMicrobe(microbe);

            if (isNew)
            {
                processedCount++;

                // Award processing science bonus
                float scienceBonus = microbe.AdjustedScienceValue * PROCESSING_SCIENCE_MULTIPLIER;
                if (ResearchAndDevelopment.Instance != null && scienceBonus > 0)
                {
                    ResearchAndDevelopment.Instance.AddScience(
                        scienceBonus, TransactionReasons.ScienceTransmission);
                    totalScience += scienceBonus;
                }

                int relationsAfter = MicrobiologyDatabase.Instance
                    .GetRevealedTrophicRelations(microbe).Count;
                newRelationsCount += relationsAfter - relationsBefore;
            }
        }

        // Check if newly processed microbes reveal relations for existing predators
        foreach (string microbeName in queuedNames)
        {
            Microbe prey = MicrobiologyDatabase.Instance.AllMicrobes.Find(m =>
                m.name.Equals(microbeName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (prey == null) continue;

            foreach (Microbe predator in MicrobiologyDatabase.Instance.AllMicrobes)
            {
                if (!MicrobiologyDatabase.Instance.IsMicrobeProcessed(predator)) continue;
                if (queuedNames.Contains(predator.name)) continue; // already counted above
                if (!predator.trophicRelations.Contains(prey.name)) continue;
                newRelationsCount++;
            }
        }

        // Clear queue
        processingQueueStr = "";
        samplesBeingProcessed = 0;

        // Report
        if (processedCount > 0)
        {
            ScreenMessages.PostScreenMessage(
                $"<color=#44CCFF>Lab processing complete: {processedCount} sample(s) analysed!</color>",
                7f, ScreenMessageStyle.UPPER_CENTER);

            if (totalScience > 0)
                ScreenMessages.PostScreenMessage(
                    $"<color=#FFAA00>+{totalScience:F0} Science from lab analysis.</color>",
                    6f, ScreenMessageStyle.UPPER_CENTER);

            if (newRelationsCount > 0)
                ScreenMessages.PostScreenMessage(
                    $"<color=#FF9944>{newRelationsCount} trophic relation(s) revealed! " +
                    $"Check the Codex.</color>",
                    7f, ScreenMessageStyle.UPPER_CENTER);
        }
        else
        {
            ScreenMessages.PostScreenMessage(
                "<color=yellow>Processing complete — samples were already analysed.</color>",
                5f, ScreenMessageStyle.UPPER_CENTER);
        }

        // Refresh codex if open
        if (MicrobiologyCodexUI.Instance != null && MicrobiologyCodexUI.Instance.IsOpen)
            MicrobiologyDatabase.Instance.ShowCodexUI();

        Debug.Log($"[Microbiology Expansion] Processing complete. " +
                  $"{processedCount} processed, +{totalScience:F0} science, " +
                  $"{newRelationsCount} new relations.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private int GetBestScientistLevel()
    {
        int best = 0;
        if (vessel == null) return best;
        foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
            if (crew.trait == "Scientist" && crew.experienceLevel > best)
                best = crew.experienceLevel;
        return best;
    }

    private bool HasScientist()
    {
        if (vessel == null) return false;
        foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
            if (crew.trait == "Scientist") return true;
        return false;
    }

    private List<ScienceData> GetMicrobeSamples()
    {
        var result = new List<ScienceData>();
        ModuleScienceContainer container = part.FindModuleImplementing<ModuleScienceContainer>();
        if (container == null) return result;
        foreach (ScienceData d in container.GetData())
            if (d.subjectID.StartsWith(EXPERIMENT_ID))
                result.Add(d);
        return result;
    }

    private List<ScienceData> GetUnprocessedSamples()
    {
        var result = new List<ScienceData>();
        foreach (ScienceData d in GetMicrobeSamples())
        {
            Microbe microbe = GetMicrobeFromScienceData(d);
            if (microbe != null && !MicrobiologyDatabase.Instance.IsMicrobeProcessed(microbe))
                result.Add(d);
        }
        return result;
    }

    private int CountMicrobeSamples() => GetMicrobeSamples().Count;
    private int CountUnprocessedSamples() => MicrobiologyDatabase.Instance != null
        ? GetUnprocessedSamples().Count : 0;

    private Microbe GetMicrobeFromScienceData(ScienceData data)
    {
        if (data == null || MicrobiologyDatabase.Instance == null) return null;
        string title = data.title ?? "";
        if (!title.StartsWith("Microbe Sample: ")) return null;
        string remainder = title.Substring("Microbe Sample: ".Length);
        int parenIndex = remainder.IndexOf(" (");
        string microbeName = parenIndex > 0 ? remainder.Substring(0, parenIndex) : remainder;
        return MicrobiologyDatabase.Instance.AllMicrobes.Find(m =>
            m.name.Equals(microbeName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private string FormatTime(double seconds)
    {
        // KSP uses 6-hour days, 426-day years
        int weeks = (int)(seconds / (7 * 6 * 3600));
        seconds -= weeks * 7 * 6 * 3600;
        int days = (int)(seconds / (6 * 3600));
        seconds -= days * 6 * 3600;
        int hours = (int)(seconds / 3600);
        seconds -= hours * 3600;
        int minutes = (int)(seconds / 60);

        var parts = new List<string>();
        if (weeks > 0) parts.Add($"{weeks}w");
        if (days > 0) parts.Add($"{days}d");
        if (hours > 0) parts.Add($"{hours}h");
        if (minutes > 0) parts.Add($"{minutes}m");
        if (parts.Count == 0) parts.Add("<1m");

        return string.Join(" ", parts);
    }

    public override string GetInfo()
    {
        return "Microbiology Processing Lab\n" +
               "Analyses stored microbe samples over time.\n" +
               "Reveals trophic relationships in the Codex.\n" +
               "Awards bonus science on completion.\n" +
               $"Requires: {EC_PER_SAMPLE:F0} EC per sample (upfront).\n" +
               "Requires: Scientist crew member.\n\n" +
               "Processing time per sample by Scientist level:\n" +
               "  Lvl 0: 3 weeks  |  Lvl 1: 1 week\n" +
               "  Lvl 2: 5 days   |  Lvl 3: 3 days\n" +
               "  Lvl 4: 1 day    |  Lvl 5: 5 hours";
    }
}