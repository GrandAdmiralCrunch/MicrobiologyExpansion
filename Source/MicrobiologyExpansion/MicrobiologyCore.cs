using UnityEngine;
using KSP.UI.Screens;

[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public class MicrobiologyCore : MonoBehaviour
{
    private static MicrobiologyCore Instance;

    private ApplicationLauncherButton toolbarButton;

    private const string ICON_REGULAR = "MicrobiologyExpansion/Icons/Toolbar_Icon";
    private const string ICON_ACTIVE = "MicrobiologyExpansion/Icons/Toolbar_Icon_Active";

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[Microbiology Expansion] Core initialized.");
    }

    private void Start()
    {
        GameEvents.onGUIApplicationLauncherReady.Add(OnGUIAppLauncherReady);
        GameEvents.onGUIApplicationLauncherDestroyed.Add(OnGUIAppLauncherDestroyed);
    }

    private void OnDestroy()
    {
        GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIAppLauncherReady);
        GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnGUIAppLauncherDestroyed);

        if (toolbarButton != null && ApplicationLauncher.Instance != null)
        {
            ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
            toolbarButton = null;
        }

        if (Instance == this)
            Instance = null;
    }

    // Keeps toolbar button visual state in sync with the Codex open/close state
    // across scene transitions. SetTrue/SetFalse are idempotent so safe to call every frame.
    private void Update()
    {
        if (toolbarButton == null) return;

        bool codexOpen = MicrobiologyCodexUI.Instance != null &&
                         MicrobiologyCodexUI.Instance.IsOpen;

        if (codexOpen)
            toolbarButton.SetTrue(false);
        else
            toolbarButton.SetFalse(false);
    }

    private void OnGUIAppLauncherReady()
    {
        if (toolbarButton != null) return;

        Texture2D regularTex = GameDatabase.Instance.GetTexture(ICON_REGULAR, false);
        Texture2D activeTex = GameDatabase.Instance.GetTexture(ICON_ACTIVE, false);

        if (regularTex == null)
            Debug.LogWarning("[Microbiology Expansion] Toolbar_Icon texture not found at: " + ICON_REGULAR);
        if (activeTex == null)
            Debug.LogWarning("[Microbiology Expansion] Toolbar_Icon_Active texture not found at: " + ICON_ACTIVE);

        toolbarButton = ApplicationLauncher.Instance.AddModApplication(
            OnToolbarButtonTrue,   // called when button is toggled ON
            OnToolbarButtonFalse,  // called when button is toggled OFF
            null, null, null, null,
            ApplicationLauncher.AppScenes.FLIGHT |
            ApplicationLauncher.AppScenes.SPACECENTER |
            ApplicationLauncher.AppScenes.VAB |
            ApplicationLauncher.AppScenes.SPH,
            regularTex
        );

        Debug.Log("[Microbiology Expansion] Toolbar button added.");
    }

    // Button toggled ON — open the codex
    private void OnToolbarButtonTrue()
    {
        if (MicrobiologyCodexUI.Instance != null)
            MicrobiologyCodexUI.Instance.OpenCodex();
        else
            Debug.LogWarning("[Microbiology Expansion] CodexUI instance not available in this scene.");
    }

    // Button toggled OFF — close the codex
    private void OnToolbarButtonFalse()
    {
        if (MicrobiologyCodexUI.Instance != null)
            MicrobiologyCodexUI.Instance.CloseCodex();
    }

    private void OnGUIAppLauncherDestroyed()
    {
        if (toolbarButton != null)
        {
            ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
            toolbarButton = null;
        }
    }

    public static void DebugDiscoverAllMicrobes()
    {
        if (MicrobiologyDatabase.Instance != null)
            MicrobiologyDatabase.Instance.DebugDiscoverAllMicrobes();
    }
}