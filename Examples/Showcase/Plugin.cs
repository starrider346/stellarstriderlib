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

        public override void OnInitializeMelon()
        {
            _content = StriderRegistry.Create(ShowcaseContent.ModId, typeof(Plugin).Assembly);
            var icon = _content.Assets.Texture(ShowcaseContent.PanelTexture);
            ShowcaseContent.RegisterContent(icon);

            _content.WorldBound += OnWorldBound;
            _content.WorldUnbinding += OnWorldUnbinding;
        }

        public override void OnGUI()
        {
            if (!StriderRegistry.WorldReady || ConsoleState.DisplayedSeconds.Count == 0)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20, 240, 340, 180), GUI.skin.box);
            GUILayout.Label("StellarStriderLib Showcase");
            var shown = 0;
            foreach (var pair in ConsoleState.DisplayedSeconds)
            {
                GUILayout.Label($"Ship {pair.Key.ShipId}, console {pair.Key.PartId}: {pair.Value:F1}s");
                if (++shown == 4)
                {
                    break;
                }
            }

            GUILayout.EndArea();
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
