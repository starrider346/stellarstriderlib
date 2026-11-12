using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Items.Model;
using Research.Interface.Values;
using Ships.Cables;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using StellarStriderLib.Core;
using UI.BuildMenu;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    /// <summary>Integrates registered content with native providers, build menus and research views.</summary>
    internal static class GameHooks
    {
        private static readonly System.Reflection.FieldInfo ItemsField =
            AccessTools.Field(typeof(ItemSettingsList), "items");

        private static readonly System.Reflection.FieldInfo PartsField =
            AccessTools.Field(typeof(PartSettingsProvider), "_partsDict");

        private static readonly System.Reflection.FieldInfo TabsField =
            AccessTools.Field(typeof(BuildMenu), "tabsConfig");

        private static readonly HashSet<PartSettingsProvider> Providers = new();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, AccessTools.Method(typeof(PartSettingsProvider), "EnsureDictIsCached"), nameof(Parts),
                false);
            Patch(harmony, AccessTools.Method(typeof(ItemSettingsList), "GetItemSettingsById"), nameof(Items), true);
            Patch(harmony, AccessTools.Method(typeof(ResearchSettingsList), "GetResearchSettingById"), nameof(Research),
                true);
            Patch(harmony, AccessTools.Method(typeof(ResearchSettingsList), "GetTechsRequiredForPart"),
                nameof(Research), true);
            Patch(harmony, AccessTools.PropertyGetter(typeof(Research.ResearchClientProxy), "Researches"),
                nameof(ClientResearch), true);
            Patch(harmony, AccessTools.Method(typeof(Research.ResearchClientProxy), "IsPartUnlocked"),
                nameof(ClientResearch), true);
            Patch(harmony, AccessTools.Method(typeof(BuildMenu), "Start"), nameof(Menu), true);
            Patch(harmony, AccessTools.Method(typeof(BuildMenu), "GenerateTabButtonsFromConfig"),
                nameof(WrapMenuTabs), false);
            Patch(harmony, AccessTools.Method(typeof(TabButton), "Setup"), nameof(TabTitle), true);
            Patch(harmony, AccessTools.Method(typeof(TabButton), "Setup"), nameof(FitTabTitle), false);
            Patch(harmony,
                AccessTools.Method(typeof(ConnectionsTracker), "GetSocketPosition",
                    new[] { typeof(IPartPlacement), typeof(SocketObject) }), nameof(Socket), true);
            Patch(harmony, AccessTools.Method(typeof(UI.Research.ResearchCanvas), "GenerateButtons"),
                nameof(ResearchLayout), true);
            Patch(harmony, AccessTools.Method(typeof(UI.Research.ResearchCanvas), "GenerateButtons"),
                nameof(ResearchBounds), false);
        }

        internal static void Patch(HarmonyLib.Harmony harmony, System.Reflection.MethodBase method, string name,
            bool prefix)
        {
            if (method == null)
            {
                throw new MissingMethodException("Game hook unavailable: " + name);
            }

            var handler = new HarmonyMethod(typeof(GameHooks), name);
            harmony.Patch(method, prefix: prefix ? handler : null, postfix: prefix ? null : handler);
        }

        private static void Parts(PartSettingsProvider __instance)
        {
            Providers.Add(__instance);
            if (!ContentSession.Ready)
            {
                return;
            }

            var current = (Dictionary<ushort, PartSettings>)PartsField.GetValue(__instance);
            if (current == null)
            {
                return;
            }

            foreach (var pair in ContentSession.Registry.Map(ContentKind.Part))
            {
                current[(ushort)pair.Value] = ContentSession.PartSettings[pair.Value];
            }
        }

        private static void Items(ItemSettingsList __instance)
        {
            if (!ContentSession.Ready)
            {
                return;
            }

            var current = (ItemSettings[])ItemsField.GetValue(__instance) ?? Array.Empty<ItemSettings>();
            var additions = ContentSession.Registry.Map(ContentKind.Item).Values
                .Select(id => ContentSession.ItemSettings[id]).Where(s => !current.Contains(s)).ToArray();
            if (additions.Length == 0)
            {
                return;
            }

            ItemsField.SetValue(__instance, current.Concat(additions).ToArray());
            AccessTools.Method(typeof(ItemSettingsList), "ResetDict").Invoke(__instance, null);
        }

        private static void ClientResearch(ResearchSettingsList ___researchSettingsList) =>
            Research(___researchSettingsList);

        private static void Research(ResearchSettingsList __instance)
        {
            if (!ContentSession.Ready || __instance?.list == null)
            {
                return;
            }

            foreach (var id in ContentSession.Registry.Map(ContentKind.Research).Values)
            {
                if (!__instance.list.Contains(ContentSession.ResearchSettings[id]))
                {
                    __instance.list.Add(ContentSession.ResearchSettings[id]);
                }
            }
        }

        private static bool Socket(IPartPlacement __0, SocketObject __1, ref Vector3 __result)
        {
            if (!ContentSession.SocketPositions.TryGetValue(__1, out var position))
            {
                return true;
            }

            __result = __0.LocalPosition + __0.Rotation * position;
            return false;
        }

        private static void Menu(BuildMenu __instance)
        {
            if (!ContentSession.Ready)
            {
                return;
            }

            var original = (BuildMenuTabConfig[])TabsField.GetValue(__instance);
            var own = ContentSession.OwnedIds(ContentKind.Part);
            var tabKeys = new HashSet<string>(StriderRegistry.Categories.Keys);
            var tabs = original.Where(t => !tabKeys.Contains(t.name)).Select(t => new BuildMenuTabConfig
            {
                name = t.name, icon = t.icon,
                rows = t.rows.Select(r => new BuildMenuTabConfig.BuildMenuRow
                        { parts = r.parts.Where(p => p == null || !own.Contains(p.id)).ToArray() })
                    .Where(r => r.parts.Length > 0).ToArray()
            }).ToList();
            foreach (var category in StriderRegistry.Categories.Values.OrderBy(e => e.Definition.SortOrder)
                         .ThenBy(e => e.Key, StringComparer.Ordinal))
            {
                var parts = StriderRegistry.Parts.Values
                    .Where(p => p.Owner.CategoryKey(p.Definition.Category) == category.Key)
                    .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
                        ContentSession.PartSettings[ContentSession.Registry.Resolve(ContentKind.Part, p.Key)])
                    .ToArray();
                if (parts.Length == 0)
                {
                    continue;
                }

                tabs.Add(new BuildMenuTabConfig
                {
                    name = category.Key, icon = category.Definition.Icon ?? original.FirstOrDefault()?.icon,
                    rows = Enumerable.Range(0, (parts.Length + 6) / 7).Select(i => new BuildMenuTabConfig.BuildMenuRow
                        { parts = parts.Skip(i * 7).Take(7).ToArray() }).ToArray()
                });
            }

            TabsField.SetValue(__instance, tabs.ToArray());
        }

        private static void WrapMenuTabs(Transform ___tabButtonsContainer, Transform ___tabPanelContainer)
        {
            BuildMenuTabLayout.Attach(___tabButtonsContainer as RectTransform, ___tabPanelContainer as RectTransform);
        }

        private static void TabTitle(ref string __0)
        {
            if (StriderRegistry.Categories.TryGetValue(__0, out var category))
            {
                __0 = category.Definition.Name;
            }
        }

        private static void FitTabTitle(TabButton __instance) => BuildMenuTabSize.Attach(__instance);

        internal static void RefreshProviders()
        {
            foreach (var provider in Providers.ToArray())
            {
                PartsField.SetValue(provider, null);
                provider.GetAllPartSettings();
            }

            foreach (var list in Resources.FindObjectsOfTypeAll<ItemSettingsList>())
            {
                Items(list);
            }

            foreach (var list in Resources.FindObjectsOfTypeAll<ResearchSettingsList>())
            {
                Research(list);
            }
        }

        internal static void RemoveOwned()
        {
            var partIds = ContentSession.OwnedIds(ContentKind.Part);
            var itemIds = ContentSession.OwnedIds(ContentKind.Item);
            var researchIds = ContentSession.OwnedIds(ContentKind.Research);
            foreach (var provider in Providers)
            {
                PartsField.SetValue(provider, null);
            }

            Providers.Clear();
            foreach (var list in Resources.FindObjectsOfTypeAll<ItemSettingsList>())
            {
                var items = (ItemSettings[])ItemsField.GetValue(list) ?? Array.Empty<ItemSettings>();
                ItemsField.SetValue(list, items.Where(i => i == null || !itemIds.Contains(i.id)).ToArray());
                AccessTools.Method(typeof(ItemSettingsList), "ResetDict").Invoke(list, null);
            }

            foreach (var node in Resources.FindObjectsOfTypeAll<ResearchSettings>())
            {
                if (node.unlockedParts != null && node.unlockedParts.Any(p => p != null && partIds.Contains(p.id)))
                {
                    node.unlockedParts = node.unlockedParts.Where(p => p == null || !partIds.Contains(p.id)).ToArray();
                }
            }

            foreach (var list in Resources.FindObjectsOfTypeAll<ResearchSettingsList>())
            {
                list.list?.RemoveAll(n => n != null && researchIds.Contains(n.id));
            }
        }

        /// <summary>Retains research view generation and scroll state across UI rebuilds.</summary>
        private sealed class ResearchView
        {
            internal bool Generated;
            internal Vector2 Scroll;
        }

        private static readonly
            System.Runtime.CompilerServices.ConditionalWeakTable<UI.Research.ResearchCanvas, ResearchView>
            ResearchViews = new();

        private static void ResearchLayout(UI.Research.ResearchCanvas __instance, RectTransform ___buttonsContainer,
            Research.Interface.Services.IResearchProvider ____researchProvider)
        {
            if (!ContentSession.Ready)
            {
                return;
            }

            var scroll = ___buttonsContainer.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            var view = ResearchViews.GetValue(__instance, _ => new ResearchView());
            if (scroll != null)
            {
                view.Scroll = scroll.normalizedPosition;
            }

            ContentSession.Relayout(____researchProvider.Researches);
        }

        private static void ResearchBounds(UI.Research.ResearchCanvas __instance, RectTransform ___buttonsContainer,
            RectTransform ___linesContainer)
        {
            if (!ContentSession.Ready || StriderRegistry.Research.Count == 0)
            {
                return;
            }

            var buttons = ___buttonsContainer.GetComponentsInChildren<UI.Research.ResearchSelectButton>();
            if (buttons.Length == 0)
            {
                return;
            }

            var rects = buttons.Select(b => (RectTransform)b.transform).ToArray();
            var minX = rects.Min(r => r.localPosition.x - r.rect.width * r.pivot.x);
            var minY = rects.Min(r => r.localPosition.y - r.rect.height * r.pivot.y);
            var maxX = rects.Max(r => r.localPosition.x + r.rect.width * (1 - r.pivot.x));
            var maxY = rects.Max(r => r.localPosition.y + r.rect.height * (1 - r.pivot.y));
            var positions = rects.Select(r => r.localPosition).ToArray();
            var offset = new Vector3(80 - minX, -80 - maxY, 0);
            var size = new Vector2(maxX - minX + 160, maxY - minY + 160);
            var topLeft = new Vector2(0, 1);
            ___buttonsContainer.anchorMin = ___buttonsContainer.anchorMax = topLeft;
            ___buttonsContainer.pivot = topLeft;
            ___buttonsContainer.sizeDelta = size;
            ___buttonsContainer.anchoredPosition = Vector2.zero;
            for (var i = 0; i < rects.Length; i++)
            {
                rects[i].anchorMin = rects[i].anchorMax = topLeft;
                rects[i].localPosition = positions[i] + offset;
            }

            ___linesContainer.anchorMin = ___linesContainer.anchorMax = topLeft;
            ___linesContainer.pivot = topLeft;
            ___linesContainer.sizeDelta = size;
            ___linesContainer.anchoredPosition = Vector2.zero;
            var scroll = ___buttonsContainer.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            if (scroll == null)
            {
                return;
            }

            var view = ResearchViews.GetValue(__instance, _ => new ResearchView());
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.normalizedPosition = view.Generated ? view.Scroll : new Vector2(0, 1);
            view.Generated = true;
        }
    }
}
