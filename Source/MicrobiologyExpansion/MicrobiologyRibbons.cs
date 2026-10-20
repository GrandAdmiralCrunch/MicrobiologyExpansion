using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// =============================================================================
// MicrobiologyRibbons — awards the Final Frontier ribbons of
// FinalFrontier/FinalFrontierCustomRibbons.cfg when a microbe is discovered.
// Final Frontier stays optional: it is reached through reflection, and this
// does nothing when it isn't installed.
// =============================================================================

[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public class MicrobiologyRibbons : MonoBehaviour
{
    // BASE in FinalFrontierCustomRibbons.cfg. Final Frontier names a custom ribbon "X" + (BASE + id).
    private const int RIBBON_BASE = 8300;

    private const int FIRST_LIFE = 1;
    private const int MICROBIOLOGIST = 12;     // 10 species discovered by the same kerbal
    private const int EXOBIOLOGIST = 13;       // first discovery beyond Kerbin
    private const int ASTROBIOLOGIST = 14;     // life discovered on 5 or more worlds by the same kerbal

    private const int MICROBIOLOGIST_SPECIES = 10;
    private const int ASTROBIOLOGIST_WORLDS = 5;

    // First life on a world. Worlds whose ribbon has no image yet are commented out in the cfg;
    // they are skipped because Final Frontier doesn't know them.
    private static readonly Dictionary<string, int> FirstOnWorld = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Kerbin", 2 }, { "Mun", 3 }, { "Minmus", 4 }, { "Duna", 5 }, { "Eve", 6 },
        { "Laythe", 7 }, { "Dres", 8 }, { "Jool", 9 }, { "Eeloo", 10 }, { "Tylo", 11 },
    };

    private static MicrobiologyRibbons Instance;

    private object externalInterface;
    private MethodInfo awardRibbon;          // ExternalInterface.AwardRibbonToKerbal(object, ProtoCrewMember)
    private MethodInfo isRibbonAwarded;      // ExternalInterface.IsRibbonAwardedToKerbal(object, ProtoCrewMember)
    private object ribbonPool;
    private MethodInfo getRibbonForCode;     // RibbonPool.GetRibbonForCode(string)

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (FindFinalFrontier())
        {
            MicrobiologyDatabase.OnMicrobeDiscovered += OnMicrobeDiscovered;
            Debug.Log("[Microbiology Expansion] Final Frontier found, ribbons will be awarded on discovery.");
        }
    }

    private void OnDestroy()
    {
        MicrobiologyDatabase.OnMicrobeDiscovered -= OnMicrobeDiscovered;
        if (Instance == this)
            Instance = null;
    }

    private bool FindFinalFrontier()
    {
        var ff = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "FinalFrontier");
        if (ff == null) return false;

        try
        {
            Type externalType = ff.assembly.GetType("Nereid.FinalFrontier.ExternalInterface");
            Type poolType = ff.assembly.GetType("Nereid.FinalFrontier.RibbonPool");
            if (externalType == null || poolType == null) return false;

            Type[] ribbonAndKerbal = { typeof(object), typeof(ProtoCrewMember) };
            awardRibbon = externalType.GetMethod("AwardRibbonToKerbal", ribbonAndKerbal);
            isRibbonAwarded = externalType.GetMethod("IsRibbonAwardedToKerbal", ribbonAndKerbal);
            getRibbonForCode = poolType.GetMethod("GetRibbonForCode", new[] { typeof(string) });
            MethodInfo poolInstance = poolType.GetMethod("Instance", BindingFlags.Public | BindingFlags.Static);
            if (awardRibbon == null || isRibbonAwarded == null || getRibbonForCode == null || poolInstance == null)
            {
                Debug.LogWarning("[Microbiology Expansion] Final Frontier found, but not the expected API; no ribbons.");
                return false;
            }

            externalInterface = Activator.CreateInstance(externalType);
            ribbonPool = poolInstance.Invoke(null, null);
            return ribbonPool != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Microbiology Expansion] Couldn't hook into Final Frontier: " + e.Message);
            return false;
        }
    }

    private void OnMicrobeDiscovered(Microbe microbe, string kerbalName)
    {
        try
        {
            if (microbe == null || IsDebris(microbe)) return;
            if (string.IsNullOrEmpty(kerbalName) || HighLogic.CurrentGame == null) return;

            ProtoCrewMember kerbal = HighLogic.CurrentGame.CrewRoster[kerbalName];
            if (kerbal == null) return;

            MicrobiologyDatabase db = MicrobiologyDatabase.Instance;
            List<Microbe> life = db.DiscoveredMicrobes.FindAll(m => !IsDebris(m));

            // The new microbe is already in DiscoveredMicrobes when the event fires
            if (life.Count == 1)
                Award(FIRST_LIFE, kerbal);

            if (FirstOnWorld.TryGetValue(microbe.homePlanet, out int worldRibbon) &&
                life.Count(m => m.homePlanet.Equals(microbe.homePlanet, StringComparison.OrdinalIgnoreCase)) == 1)
                Award(worldRibbon, kerbal);

            if (!microbe.homePlanet.Equals("Kerbin", StringComparison.OrdinalIgnoreCase) &&
                life.Count(m => !m.homePlanet.Equals("Kerbin", StringComparison.OrdinalIgnoreCase)) == 1)
                Award(EXOBIOLOGIST, kerbal);

            List<Microbe> byKerbal = life.FindAll(m => db.GetDiscoverer(m) == kerbalName);
            if (byKerbal.Count >= MICROBIOLOGIST_SPECIES)
                Award(MICROBIOLOGIST, kerbal);

            int worlds = byKerbal.Select(m => m.homePlanet).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (worlds >= ASTROBIOLOGIST_WORLDS)
                Award(ASTROBIOLOGIST, kerbal);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Microbiology Expansion] Ribbon award failed: " + e.Message);
        }
    }

    private void Award(int id, ProtoCrewMember kerbal)
    {
        object ribbon = getRibbonForCode.Invoke(ribbonPool, new object[] { "X" + (RIBBON_BASE + id) });
        if (ribbon == null) return; // no image yet, or the ribbon pack isn't loaded

        if ((bool)isRibbonAwarded.Invoke(externalInterface, new object[] { ribbon, kerbal })) return;

        awardRibbon.Invoke(externalInterface, new object[] { ribbon, kerbal });
        Debug.Log($"[Microbiology Expansion] Awarded ribbon X{RIBBON_BASE + id} to {kerbal.name}.");
    }

    private static bool IsDebris(Microbe microbe)
    {
        return microbe.category.Equals("Debris", StringComparison.OrdinalIgnoreCase);
    }
}
