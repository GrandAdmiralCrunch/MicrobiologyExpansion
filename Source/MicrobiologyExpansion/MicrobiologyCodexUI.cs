using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens;

[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public class MicrobiologyCodexUI : MonoBehaviour
{
    public static MicrobiologyCodexUI Instance { get; private set; }

    private PopupDialog codexDialog;
    private bool isOpen = false;
    public bool IsOpen => isOpen;

    private enum SortMode { None, Planet, Biome, Category, Discovery, Rarity, Hazard }
    private SortMode currentSort = SortMode.None;

    private enum HazardFilter { All, Safe, Risky, Hazardous }
    private HazardFilter currentHazardFilter = HazardFilter.All;

    private string currentPlanetFilter = "All";
    private bool hideUndiscovered = false;

    // Scroll memory — normalized 0..1 where 1 = top
    private float savedScrollPosition = 1f;

    // Entry to scroll to on the next build (set by ShowMicrobe after a new discovery)
    private Microbe focusMicrobe;
    private DialogGUIBase focusItem;

    private const string UNKNOWN_ICON_PATH = "MicrobiologyExpansion/Icons/unknown_microbe";

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Listen for scene changes to close the codex when leaving valid scenes
        GameEvents.onGameSceneLoadRequested.Add(OnSceneChangeRequested);
    }

    private void OnDestroy()
    {
        GameEvents.onGameSceneLoadRequested.Remove(OnSceneChangeRequested);
        SaveScrollPosition();

        if (codexDialog != null)
        {
            codexDialog.Dismiss();
            codexDialog = null;
        }
        isOpen = false;

        if (Instance == this)
            Instance = null;
    }

    private void OnSceneChangeRequested(GameScenes scene)
    {
        // Close the codex on scene transitions — it will reopen fresh in the new scene
        if (isOpen)
            CloseCodex();
    }

    private bool IsValidScene()
    {
        return HighLogic.LoadedScene == GameScenes.FLIGHT ||
               HighLogic.LoadedScene == GameScenes.SPACECENTER ||
               HighLogic.LoadedScene == GameScenes.EDITOR ||
               HighLogic.LoadedScene == GameScenes.TRACKSTATION;
    }

    // Called by MicrobiologyCore toolbar button ON
    public void OpenCodex()
    {
        if (isOpen) return;
        if (MicrobiologyDatabase.Instance == null) return;
        if (!IsValidScene()) return;
        BuildCodexDialog();
        isOpen = true;
    }

    // Called by MicrobiologyCore toolbar button OFF, and Close button
    public void CloseCodex()
    {
        SaveScrollPosition();

        if (codexDialog != null)
        {
            codexDialog.Dismiss();
            codexDialog = null;
        }
        isOpen = false;
    }

    // Called by the part's "Open Codex" action
    public void ToggleCodex()
    {
        if (isOpen) CloseCodex();
        else OpenCodex();
    }

    // Called after a new discovery: opens the codex, or rebuilds it if it is already open,
    // scrolled to that microbe's entry
    public void ShowMicrobe(Microbe microbe)
    {
        if (MicrobiologyDatabase.Instance == null) return;
        if (!IsValidScene()) return;

        focusMicrobe = microbe;

        // Clear the filters if they would hide the new entry
        if (microbe != null && !GetFilteredSortedMicrobes().Contains(microbe))
        {
            currentHazardFilter = HazardFilter.All;
            currentPlanetFilter = "All";
        }

        if (isOpen) RefreshDialog();
        else OpenCodex();
    }

    // Rebuilds the codex if it is open, e.g. after the lab reveals new data
    public void Refresh()
    {
        if (isOpen) RefreshDialog();
    }

    // -------------------------------------------------------------------------
    // Dialog builder
    // -------------------------------------------------------------------------

    private void BuildCodexDialog()
    {
        List<DialogGUIBase> elements = new List<DialogGUIBase>();

        int discovered = MicrobiologyDatabase.Instance.DiscoveredMicrobes.Count;
        float science = MicrobiologyDatabase.Instance.TotalMicrobeScienceEarned;

        // Reset planet filter if the selected planet no longer has any discoveries
        if (currentPlanetFilter != "All")
        {
            bool stillValid = MicrobiologyDatabase.Instance.DiscoveredMicrobes
                .Exists(m => m.homePlanet.Equals(currentPlanetFilter, StringComparison.OrdinalIgnoreCase));
            if (!stillValid)
                currentPlanetFilter = "All";
        }

        // ── HEADER ──────────────────────────────────────────────────────────
        int lifeDiscovered = MicrobiologyDatabase.Instance.DiscoveredLifeCount;
        int lifeTotal = MicrobiologyDatabase.Instance.TotalLifeCount;
        int debrisDiscovered = MicrobiologyDatabase.Instance.DiscoveredDebrisCount;
        int debrisTotal = MicrobiologyDatabase.Instance.TotalDebrisCount;

        string debrisStr = debrisTotal > 0 && debrisDiscovered > 0
            ? $"   <color=#555555>|</color>   <color=#AAAAAA>Debris: {debrisDiscovered}/{debrisTotal}</color>"
            : "";

        elements.Add(new DialogGUILabel(
            "<b><color=#FFAA00>✦ MICROBIOLOGY CODEX ✦</color></b>", false, false));
        elements.Add(new DialogGUILabel(
            $"<color=#CCCCCC>Life Catalogued:</color> <color=#88FF88>{lifeDiscovered}/{lifeTotal}</color>" +
            debrisStr +
            $"   <color=#555555>|</color>   <color=#CCCCCC>Science:</color> <color=#FFAA00>{science:F0}</color>",
            true, false));
        elements.Add(new DialogGUISpace(6));

        // ── CATEGORY BADGES — only show discovered categories ───────────────
        var categoryGroups = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in MicrobiologyDatabase.Instance.AllMicrobes)
        {
            if (!categoryGroups.ContainsKey(m.category))
                categoryGroups[m.category] = new int[2];
            categoryGroups[m.category][1]++;
            if (MicrobiologyDatabase.Instance.DiscoveredMicrobes.Contains(m))
                categoryGroups[m.category][0]++;
        }

        // Only show categories where at least one microbe has been discovered
        var discoveredCats = new List<string>();
        foreach (var kvp in categoryGroups)
            if (kvp.Value[0] > 0)
                discoveredCats.Add(kvp.Key);
        discoveredCats.Sort(StringComparer.OrdinalIgnoreCase);

        if (discoveredCats.Count > 0)
        {
            var catParts = new List<string>();
            foreach (string cat in discoveredCats)
            {
                int disc = categoryGroups[cat][0];
                int total2 = categoryGroups[cat][1];
                bool isDebrisCat = cat.Equals("Debris", StringComparison.OrdinalIgnoreCase);
                string color = isDebrisCat ? "#888888"
                    : (disc == total2 ? "#88FF88" : "#FFCC44");
                catParts.Add($"<color={color}>{cat} {disc}/{total2}</color>");
            }
            elements.Add(new DialogGUILabel(
                "<size=10>" + string.Join("  <color=#444444>·</color>  ", catParts) + "</size>",
                true, false));
        }
        elements.Add(new DialogGUISpace(8));

        // ── FILTERS ROW — hazard + hide undiscovered in one compact line ────
        string allLabel = currentHazardFilter == HazardFilter.All
            ? "<b><color=#FFFFFF>[All]</color></b>" : "<color=#888888>All</color>";
        string safeLabel = currentHazardFilter == HazardFilter.Safe
            ? "<b><color=#88FF88>[Safe]</color></b>" : "<color=#888888>Safe</color>";
        string riskyLabel = currentHazardFilter == HazardFilter.Risky
            ? "<b><color=#FFAA00>[Risky]</color></b>" : "<color=#888888>Risky</color>";
        string hazLabel = currentHazardFilter == HazardFilter.Hazardous
            ? "<b><color=#FF4444>[Hazardous]</color></b>" : "<color=#888888>Hazardous</color>";
        string hideLabel = hideUndiscovered
            ? "<b><color=#44AAFF>[Hide ???]</color></b>" : "<color=#888888>Hide ???</color>";

        elements.Add(new DialogGUIHorizontalLayout(
            new DialogGUIButton(allLabel,
                () => { currentHazardFilter = HazardFilter.All; RefreshDialog(); }, 50f, 22f, true),
            new DialogGUIButton(safeLabel,
                () => { currentHazardFilter = HazardFilter.Safe; RefreshDialog(); }, 55f, 22f, true),
            new DialogGUIButton(riskyLabel,
                () => { currentHazardFilter = HazardFilter.Risky; RefreshDialog(); }, 65f, 22f, true),
            new DialogGUIButton(hazLabel,
                () => { currentHazardFilter = HazardFilter.Hazardous; RefreshDialog(); }, 105f, 22f, true),
            new DialogGUISpace(12),
            new DialogGUIButton(hideLabel,
                () => { hideUndiscovered = !hideUndiscovered; RefreshDialog(); }, 90f, 22f, true),
            new DialogGUIFlexibleSpace()
        ));
        elements.Add(new DialogGUISpace(4));

        // ── PLANET FILTER — only discovered planets, no label ───────────────
        List<string> planets = GetKnownPlanets();
        if (planets.Count > 0)
        {
            List<DialogGUIBase> planetButtons = new List<DialogGUIBase>();
            planetButtons.Add(new DialogGUIButton(
                currentPlanetFilter == "All"
                    ? "<b><color=#FFFFFF>[All]</color></b>" : "<color=#888888>All</color>",
                () => { currentPlanetFilter = "All"; RefreshDialog(); }, 42f, 22f, true));

            foreach (string planet in planets)
            {
                string p = planet;
                planetButtons.Add(new DialogGUIButton(
                    currentPlanetFilter == p
                        ? $"<b><color=#FFCC44>[{p}]</color></b>" : $"<color=#888888>{p}</color>",
                    () => { currentPlanetFilter = p; RefreshDialog(); }, 75f, 22f, true));
            }
            planetButtons.Add(new DialogGUIFlexibleSpace());

            if (planets.Count > 7)
                elements.Add(new DialogGUIScrollList(
                    new Vector2(620f, 30f), true, false,
                    new DialogGUIHorizontalLayout(planetButtons.ToArray())));
            else
                elements.Add(new DialogGUIHorizontalLayout(planetButtons.ToArray()));
            elements.Add(new DialogGUISpace(4));
        }

        // ── SORT ROW ─────────────────────────────────────────────────────────
        string SortLabel(SortMode mode, string text) =>
            currentSort == mode
                ? $"<b><color=#FFCC44>[{text}]</color></b>"
                : $"<color=#888888>{text}</color>";

        elements.Add(new DialogGUIHorizontalLayout(
            new DialogGUILabel("<size=10><color=#666666>Sort:</color></size>  ", false, false),
            new DialogGUIButton(SortLabel(SortMode.Planet, "Planet"),
                () => { currentSort = SortMode.Planet; RefreshDialog(); }, 72f, 22f, true),
            new DialogGUIButton(SortLabel(SortMode.Biome, "Biome"),
                () => { currentSort = SortMode.Biome; RefreshDialog(); }, 65f, 22f, true),
            new DialogGUIButton(SortLabel(SortMode.Category, "Category"),
                () => { currentSort = SortMode.Category; RefreshDialog(); }, 85f, 22f, true),
            new DialogGUIButton(SortLabel(SortMode.Discovery, "Discovery"),
                () => { currentSort = SortMode.Discovery; RefreshDialog(); }, 90f, 22f, true),
            new DialogGUIButton(SortLabel(SortMode.Rarity, "Rarity"),
                () => { currentSort = SortMode.Rarity; RefreshDialog(); }, 68f, 22f, true),
            new DialogGUIButton(SortLabel(SortMode.Hazard, "Hazard"),
                () => { currentSort = SortMode.Hazard; RefreshDialog(); }, 68f, 22f, true),
            new DialogGUIFlexibleSpace()
        ));
        elements.Add(new DialogGUISpace(8));

        // Scroll content
        List<Microbe> filteredList = GetFilteredSortedMicrobes();
        List<DialogGUIBase> scrollItems = new List<DialogGUIBase>();

        if (filteredList.Count == 0)
        {
            string emptyMsg = discovered == 0
                ? "\n<color=#888888>No microbes discovered yet.\n\n" +
                  "Equip a vessel with a Mystery Goo container and run a bioscan\n" +
                  "to begin cataloguing microbial life.</color>"
                : "\n<color=#888888>No microbes match the current filter.</color>";

            scrollItems.Add(new DialogGUILabel(emptyMsg, true, false));
        }
        else
        {
            foreach (var microbe in filteredList)
            {
                bool isDiscovered = MicrobiologyDatabase.Instance.DiscoveredMicrobes.Contains(microbe);
                Texture2D iconTex = GetSafeIcon(microbe, isDiscovered);

                string nameLabel;
                string descLabel;

                if (isDiscovered)
                {
                    string dateStr = MicrobiologyDatabase.Instance.GetDiscoveryDate(microbe);
                    string discoverer = MicrobiologyDatabase.Instance.GetDiscoverer(microbe);

                    string dateLine = dateStr != null
                        ? (discoverer != null
                            ? $"\n<size=10><color=#666666>Discovered by {discoverer} on {dateStr}</color></size>"
                            : $"\n<size=10><color=#666666>Discovered: {dateStr}</color></size>")
                        : (discoverer != null
                            ? $"\n<size=10><color=#666666>Discovered by {discoverer}</color></size>"
                            : "");

                    List<string> foundBiomes = MicrobiologyDatabase.Instance.GetDiscoveryBiomes(microbe);
                    string biomeLine = foundBiomes.Count > 0
                        ? $"\n<size=10><color=#6699CC>Found in: {string.Join(", ", foundBiomes)}</color></size>"
                        : "";

                    int recoveryCount = MicrobiologyDatabase.Instance.GetSampleRecoveryCount(microbe);
                    bool isDebris = microbe.category.Equals("Debris", StringComparison.OrdinalIgnoreCase);
                    string recoveryLine = (!isDebris && recoveryCount > 0)
                        ? $"\n<size=10><color=#888888>Samples recovered: {recoveryCount}</color></size>"
                        : "";

                    // Processing lab status and trophic relations
                    string processedLine = "";
                    string trophicLine = "";
                    if (!isDebris)
                    {
                        bool isProcessed = MicrobiologyDatabase.Instance.IsMicrobeProcessed(microbe);
                        if (isProcessed)
                        {
                            processedLine = "\n<size=10><color=#44CCFF>✔ Processed in Mobile Lab</color></size>";

                            List<string> revealed = MicrobiologyDatabase.Instance.GetRevealedTrophicRelations(microbe);
                            bool hasHidden = MicrobiologyDatabase.Instance.HasHiddenTrophicRelations(microbe);

                            if (revealed.Count > 0)
                                trophicLine = $"\n<size=10><color=#FF9944>Consumes: {string.Join(", ", revealed)}</color></size>";

                            if (hasHidden)
                                trophicLine += "\n<size=10><color=#888888>  (further relations unconfirmed — process prey specimens)</color></size>";
                        }
                        else if (microbe.trophicRelations.Count > 0)
                        {
                            processedLine = "\n<size=10><color=#888888>Lab processing required to reveal trophic data</color></size>";
                        }
                    }

                    // Debris entries don't show hazard label or science value
                    if (isDebris)
                    {
                        nameLabel = $"<b>{microbe.DisplayName}</b>   " +
                                    $"<size=11><color=#AAAAAA>{microbe.CategoryTag}</color></size>" +
                                    dateLine +
                                    biomeLine;
                    }
                    else
                    {
                        nameLabel = $"<b>{microbe.DisplayName}</b>   " +
                                    $"<size=11><color=#88FF88>{microbe.CategoryTag}</color></size>   " +
                                    $"<color=#FFAA00>{microbe.ScienceDisplay}</color>   " +
                                    $"<size=11>{microbe.HazardDisplay}</size>" +
                                    dateLine +
                                    biomeLine +
                                    recoveryLine +
                                    processedLine +
                                    trophicLine;
                    }
                    descLabel = microbe.Description;
                }
                else
                {
                    nameLabel = "<b>???</b>   <size=11><color=#888888>Unknown Microbe</color></size>";
                    descLabel = "<color=#888888>???</color>";
                }

                DialogGUIHorizontalLayout entry = new DialogGUIHorizontalLayout(
                    new DialogGUIImage(new Vector2(128f, 128f), Vector2.zero, Color.white, iconTex),
                    new DialogGUISpace(15),
                    new DialogGUIVerticalLayout(
                        new DialogGUILabel(nameLabel, false, false),
                        new DialogGUILabel(descLabel, false, false)
                    )
                );
                if (microbe == focusMicrobe)
                    focusItem = entry;
                scrollItems.Add(entry);
                scrollItems.Add(new DialogGUISpace(10));
            }
        }

        DialogGUIBase[] scrollContent = new DialogGUIBase[]
        {
            new DialogGUIContentSizer(ContentSizeFitter.FitMode.Unconstrained,
                                      ContentSizeFitter.FitMode.PreferredSize, true),
            new DialogGUIVerticalLayout(scrollItems.ToArray())
        };

        elements.Add(new DialogGUIScrollList(
            new Vector2(620f, 300f), false, true,
            new DialogGUIVerticalLayout(scrollContent)));

        elements.Add(new DialogGUISpace(12));
        elements.Add(new DialogGUIHorizontalLayout(
            new DialogGUIFlexibleSpace(),
            new DialogGUIButton("Close", CloseCodex, 160f, 32f, true),
            new DialogGUIFlexibleSpace()
        ));

        Rect windowRect = new Rect(0.5f, 0.5f, 680f, 640f);

        MultiOptionDialog mod = new MultiOptionDialog(
            "MicrobiologyCodexWindow",
            "",
            "",
            HighLogic.UISkin,
            windowRect,
            elements.ToArray()
        );

        codexDialog = PopupDialog.SpawnPopupDialog(
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            mod,
            false,
            HighLogic.UISkin,
            true
        );

        // Restore scroll position after one frame — dialog builds asynchronously
        StartCoroutine(RestoreScrollNextFrame());
    }

    // -------------------------------------------------------------------------
    // Scroll memory — coroutine waits one frame for the ScrollRect to exist
    // -------------------------------------------------------------------------

    private IEnumerator RestoreScrollNextFrame()
    {
        yield return null; // wait one frame for PopupDialog to finish building

        if (codexDialog == null) yield break;

        var scrollRects = codexDialog.GetComponentsInChildren<ScrollRect>();

        // The main content scroll rect is the tallest one
        ScrollRect mainScroll = null;
        float tallest = 0f;
        foreach (var sr in scrollRects)
        {
            float h = sr.GetComponent<RectTransform>()?.rect.height ?? 0f;
            if (h > tallest)
            {
                tallest = h;
                mainScroll = sr;
            }
        }

        if (mainScroll != null)
        {
            if (focusItem != null && focusItem.uiItem != null)
                ScrollTo(mainScroll, focusItem.uiItem.GetComponent<RectTransform>());
            else
                mainScroll.verticalNormalizedPosition = savedScrollPosition;
        }

        focusMicrobe = null;
        focusItem = null;
    }

    // Puts the top of the item at the top of the visible area (or as close as the list allows)
    private static void ScrollTo(ScrollRect scroll, RectTransform item)
    {
        if (item == null || scroll.content == null) return;

        RectTransform content = scroll.content;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        float scrollable = content.rect.height - viewport.rect.height;
        if (scrollable <= 0f) return;

        // World corners: [0] bottom-left, [1] top-left
        Vector3[] contentCorners = new Vector3[4];
        Vector3[] itemCorners = new Vector3[4];
        content.GetWorldCorners(contentCorners);
        item.GetWorldCorners(itemCorners);

        float scale = content.lossyScale.y != 0f ? content.lossyScale.y : 1f;
        float offset = (contentCorners[1].y - itemCorners[1].y) / scale;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - offset / scrollable);
    }

    private void SaveScrollPosition()
    {
        if (codexDialog == null) return;

        var scrollRects = codexDialog.GetComponentsInChildren<ScrollRect>();

        ScrollRect mainScroll = null;
        float tallest = 0f;
        foreach (var sr in scrollRects)
        {
            float h = sr.GetComponent<RectTransform>()?.rect.height ?? 0f;
            if (h > tallest)
            {
                tallest = h;
                mainScroll = sr;
            }
        }

        if (mainScroll != null)
            savedScrollPosition = mainScroll.verticalNormalizedPosition;
    }

    // -------------------------------------------------------------------------
    // Filtering + sorting
    // -------------------------------------------------------------------------

    private List<string> GetKnownPlanets()
    {
        // Only show planets where at least one microbe has been discovered
        HashSet<string> planets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var microbe in MicrobiologyDatabase.Instance.DiscoveredMicrobes)
            planets.Add(microbe.homePlanet);

        List<string> sorted = new List<string>(planets);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    private List<Microbe> GetFilteredSortedMicrobes()
    {
        List<Microbe> list = new List<Microbe>(MicrobiologyDatabase.Instance.AllMicrobes);

        // Hide undiscovered filter
        if (hideUndiscovered)
            list.RemoveAll(m => !MicrobiologyDatabase.Instance.DiscoveredMicrobes.Contains(m));

        // Hazard filter
        switch (currentHazardFilter)
        {
            case HazardFilter.Safe: list.RemoveAll(m => m.hazardLevel != HazardLevel.Safe); break;
            case HazardFilter.Risky: list.RemoveAll(m => m.hazardLevel != HazardLevel.Risky); break;
            case HazardFilter.Hazardous: list.RemoveAll(m => m.hazardLevel != HazardLevel.Hazardous); break;
        }

        // Planet filter
        if (currentPlanetFilter != "All")
            list.RemoveAll(m => !m.homePlanet.Equals(currentPlanetFilter, StringComparison.OrdinalIgnoreCase));

        // Base: discovered first
        list.Sort((a, b) =>
        {
            bool aDisc = MicrobiologyDatabase.Instance.DiscoveredMicrobes.Contains(a);
            bool bDisc = MicrobiologyDatabase.Instance.DiscoveredMicrobes.Contains(b);
            if (aDisc != bDisc) return bDisc.CompareTo(aDisc);
            return 0;
        });

        switch (currentSort)
        {
            case SortMode.Planet:
                list.Sort((a, b) => string.Compare(a.homePlanet, b.homePlanet, StringComparison.OrdinalIgnoreCase));
                break;
            case SortMode.Biome:
                list.Sort((a, b) => string.Compare(a.biomes, b.biomes, StringComparison.OrdinalIgnoreCase));
                break;
            case SortMode.Category:
                list.Sort((a, b) => string.Compare(a.category, b.category, StringComparison.OrdinalIgnoreCase));
                break;
            case SortMode.Rarity:
                list.Sort((a, b) => a.spawnChance.CompareTo(b.spawnChance));
                break;
            case SortMode.Hazard:
                list.Sort((a, b) => b.HazardSortKey.CompareTo(a.HazardSortKey));
                break;
            case SortMode.Discovery:
                list.Sort((a, b) =>
                {
                    string dA = MicrobiologyDatabase.Instance.GetDiscoveryDate(a);
                    string dB = MicrobiologyDatabase.Instance.GetDiscoveryDate(b);
                    if (dA == null && dB == null) return 0;
                    if (dA == null) return 1;
                    if (dB == null) return -1;
                    return string.Compare(dB, dA, StringComparison.OrdinalIgnoreCase);
                });
                break;
        }

        return list;
    }

    // -------------------------------------------------------------------------
    // Icon loading
    // -------------------------------------------------------------------------

    private Texture2D GetSafeIcon(Microbe microbe, bool isDiscovered)
    {
        string path = isDiscovered && !string.IsNullOrEmpty(microbe.iconPath)
            ? microbe.iconPath
            : UNKNOWN_ICON_PATH;

        Texture2D tex = GameDatabase.Instance.GetTexture(path, false);

        if (tex == null)
        {
            Debug.LogWarning($"[Microbiology Codex] Failed to load icon: {path} for {microbe.name}");
            tex = new Texture2D(128, 128);
            Color color = isDiscovered ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.15f, 0.15f, 0.15f);
            for (int x = 0; x < 128; x++)
                for (int y = 0; y < 128; y++)
                    tex.SetPixel(x, y, color);
            tex.Apply();
        }

        return tex;
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    private void RefreshDialog()
    {
        SaveScrollPosition();

        if (codexDialog != null)
        {
            codexDialog.Dismiss();
            codexDialog = null;
        }

        BuildCodexDialog();
    }
}