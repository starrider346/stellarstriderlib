using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(StellarStriderLib.Showcase.Plugin), "StellarStriderLib Showcase", "1.0.0", "StriderDev")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: MelonAdditionalDependencies("StellarStriderLib")]

namespace StellarStriderLib.Showcase
{
    public sealed class Plugin : MelonMod
    {
        private ModRegistry _content;
        private ModConfig _config;

        public override void OnInitializeMelon()
        {
            _config = RegisterOptions();
            _content = StriderRegistry.Create(ShowcaseContent.ModId, typeof(Plugin).Assembly);
            _content.RegisterLoadingWallpaper(new LoadingWallpaperDefinition
            {
                Id = "panel", TextureResource = ShowcaseContent.PanelTexture
            });
            var icon = _content.Assets.Texture(ShowcaseContent.PanelTexture);
            ShowcaseContent.RegisterContent(icon);

            _content.WorldBound += OnWorldBound;
            _content.WorldUnbinding += OnWorldUnbinding;
        }

        public override void OnGUI()
        {
            if (_config == null || !_config.Get<bool>("show_timer") ||
                !StriderRegistry.WorldReady || ConsoleState.DisplayedSeconds.Count == 0)
            {
                return;
            }

            var rows = _config.Get<int>("timer_rows");
            var format = _config.Get<string>("timer_precision") == "seconds" ? "F0" : "F1";
            var previousColor = GUI.color;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b,
                previousColor.a * _config.Get<int>("timer_opacity") / 100f);
            GUILayout.BeginArea(new Rect(20, 240, 340, Mathf.Max(180, 54 + rows * 24)), GUI.skin.box);
            try
            {
                GUILayout.Label(_config.Get<string>("timer_heading"));
                var shown = 0;
                foreach (var pair in ConsoleState.DisplayedSeconds)
                {
                    GUILayout.Label($"Ship {pair.Key.ShipId}, console {pair.Key.PartId}: {pair.Value.ToString(format)}s");
                    if (++shown == rows) break;
                }
            }
            finally
            {
                GUILayout.EndArea();
                GUI.color = previousColor;
            }
        }

        private static ModConfig RegisterOptions()
        {
            var config = StriderConfig.Create(ShowcaseContent.ModId, typeof(Plugin).Assembly);
            config.Register("show_timer", true);
            config.Register("timer_heading", "Bleb");
            config.Register("timer_opacity", 100);
            config.Register("timer_rows", 4);
            config.Register("timer_precision", "tenths");

            var category = config.RegisterCategory("showcase", "Strider Showcase");
            category.RegisterToggle("Show console timer", config.Key("show_timer"));
            category.RegisterText("Timer heading", config.Key("timer_heading"));
            category.RegisterSlider("Timer opacity", config.Key("timer_opacity"), 1, 100);
            category.SetEnabledWhen(config.Key("timer_opacity"), () => config.Get<bool>("show_timer"));
            category.RegisterNumber("Visible console rows", config.Key("timer_rows"), 1, 12);
            category.RegisterDropdown("Timer precision", config.Key("timer_precision"),
                ("seconds", "Whole seconds"), ("tenths", "Tenths of a second"));
            return config;
        }

        private void OnWorldBound()
        {
            var console = PartRef.Mod(_content.Key("console"));
            var chip = ItemRef.Mod(_content.Key("chip"));
            var research = ResearchRef.Mod(_content.Key("start"));

            if (StriderRegistry.TryGetPart(console, out var part) &&
                StriderRegistry.TryGetItem(chip, out var item) &&
                StriderRegistry.TryGetResearch(research, out var node))
            {
                MelonLogger.Msg($"Showcase bound: {part.fullLabel}, {item.itemName}, {node.techName}.");
            }

            if (StriderRegistry.TryGetNumericId(console, out var partId) &&
                StriderRegistry.TryGetNumericId(chip, out var itemId) &&
                StriderRegistry.TryGetNumericId(research, out var researchId))
            {
                MelonLogger.Msg($"Save-owned IDs: part={partId}, item={itemId}, research={researchId}.");
            }
            
            StriderRegistry.TryGetItem(ItemRef.Native(Items.Model.ItemsConstants.IronItemId), out _);
        }

        private static void OnWorldUnbinding()
        {
            ConsoleState.ClearDisplay();
        }

        public override void OnDeinitializeMelon()
        {
            if (_content == null)
            {
                return;
            }

            _content.WorldBound -= OnWorldBound;
            _content.WorldUnbinding -= OnWorldUnbinding;
            ConsoleState.ClearDisplay();
        }
    }
}
