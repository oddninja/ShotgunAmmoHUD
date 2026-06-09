using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShotgunAmmoHUD
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(LethalConfigGuid, BepInDependency.DependencyFlags.SoftDependency)]

    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "oddninja.shotgunammohud";
        public const string Name = "ShotgunAmmoHUD";
        public const string Version = "1.0.1";

        internal const string LethalConfigGuid = "ainavt.lc.lethalconfig";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnlyWhenHeld;
        internal static ConfigEntry<bool> HideInTerminal;
        internal static ConfigEntry<bool> ShowWhenEmpty;
        internal static ConfigEntry<int> MaxShells;
        internal static ConfigEntry<string> Format;

        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true, "Show the shotgun ammo line.");
            OnlyWhenHeld = Config.Bind("General", "ShowOnlyWhenHeld", true,
                "Only show while a shotgun is the actively held item. If false, also shows a shotgun in another inventory slot.");
            HideInTerminal = Config.Bind("General", "HideInTerminal", true, "Hide the line while using the terminal.");
            ShowWhenEmpty = Config.Bind("General", "ShowWhenEmpty", true, "Show the line even when 0 shells are loaded.");
            MaxShells = Config.Bind("General", "MaxShells", 2, "Capacity shown as the denominator (vanilla shotgun holds 2).");
            Format = Config.Bind("Text", "Format", "Shells : [{ammo} / {max}]",
                "Text of the line. Tokens: {ammo} = shells loaded, {max} = MaxShells.");

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (_runner != null)
                {
                    Destroy(_runner);
                }
                _runner = new GameObject("ShotgunAmmoHUD_Runner");
                DontDestroyOnLoad(_runner);
                _runner.AddComponent<HudRunner>();
            };

            Log.LogInfo($"{Name} {Version} loaded.");
        }
    }

    internal sealed class HudRunner : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<ShotgunItem, int> ShellsLoadedRef =
            AccessTools.FieldRefAccess<ShotgunItem, int>("shellsLoaded");
        private static readonly AccessTools.FieldRef<HUDManager, TextMeshProUGUI[]> ControlTipLinesRef =
            AccessTools.FieldRefAccess<HUDManager, TextMeshProUGUI[]>("controlTipLines");

        private const string LineName = "ShotgunAmmoLine";

        private TextMeshProUGUI _line;

        private void LateUpdate()
        {

            if (!Plugin.Enabled.Value)
            {
                Hide();
                return;
            }

            int shells;
            if (!TryGetShells(out shells))
            {
                Hide();
                return;
            }

            TextMeshProUGUI[] tips = GetControlTipLines();
            if (tips == null || tips.Length == 0 || tips[0] == null)
            {
                return;
            }

            if (!EnsureLine(tips[0]))
            {
                return;
            }

            string text = Plugin.Format.Value
                .Replace("{ammo}", shells.ToString())
                .Replace("{max}", Plugin.MaxShells.Value.ToString());

            _line.text = text;
            PositionAboveTopTip(tips);
            if (!_line.gameObject.activeSelf)
            {
                _line.gameObject.SetActive(true);
            }
        }

        // Resolves the shell count to show, or returns false if nothing should display.
        private bool TryGetShells(out int shells)
        {
            shells = 0;
            PlayerControllerB player = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
            if (player == null || (Plugin.HideInTerminal.Value && player.inTerminalMenu))
            {
                return false;
            }

            ShotgunItem shotgun = GetShotgun(player);
            if (shotgun == null)
            {
                return false;
            }

            shells = Mathf.Max(0, ShellsLoadedRef(shotgun));
            if (shells <= 0 && !Plugin.ShowWhenEmpty.Value)
            {
                return false;
            }
            return true;
        }

        private static ShotgunItem GetShotgun(PlayerControllerB player)
        {
            if (player.currentlyHeldObjectServer is ShotgunItem held)
            {
                return held;
            }
            if (Plugin.OnlyWhenHeld.Value)
            {
                return null;
            }

            GrabbableObject[] slots = player.ItemSlots;
            if (slots != null)
            {
                foreach (GrabbableObject slot in slots)
                {
                    if (slot is ShotgunItem shotgun)
                    {
                        return shotgun;
                    }
                }
            }
            return null;
        }

        private static TextMeshProUGUI[] GetControlTipLines()
        {
            HUDManager hud = HUDManager.Instance;
            return hud != null ? ControlTipLinesRef(hud) : null;
        }

        // Clone a real control-tip line so we inherit the game's font/material/alignment exactly.
        private bool EnsureLine(TextMeshProUGUI template)
        {
            if (_line != null)
            {
                return true;
            }

            Transform parent = template.transform.parent;
            if (parent != null)
            {
                Transform existing = parent.Find(LineName);
                if (existing != null)
                {
                    _line = existing.GetComponent<TextMeshProUGUI>();
                }
            }

            if (_line == null)
            {
                _line = Instantiate(template, template.transform.parent);
                _line.name = LineName;
            }

            // Detach from any coroutine-driven fade the game might run on the original line.
            _line.enabled = true;
            _line.richText = true;
            return _line != null;
        }

        private void PositionAboveTopTip(TextMeshProUGUI[] tips)
        {
            RectTransform src = tips[0].rectTransform;
            RectTransform rt = _line.rectTransform;

            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.sizeDelta = src.sizeDelta;

            float spacing = 0f;
            if (tips.Length > 1 && tips[1] != null)
            {
                spacing = Mathf.Abs(src.anchoredPosition.y - tips[1].rectTransform.anchoredPosition.y);
            }
            if (spacing < 1f)
            {
                spacing = src.rect.height > 1f ? src.rect.height : 26f;
            }

            rt.anchoredPosition = src.anchoredPosition + new Vector2(0f, spacing);
        }

        private void Hide()
        {
            if (_line != null && _line.gameObject.activeSelf)
            {
                _line.gameObject.SetActive(false);
            }
        }
    }
}
