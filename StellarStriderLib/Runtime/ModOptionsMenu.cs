using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UI.Common;
using UI.Options;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    internal static class ModOptionsMenu
    {
        private static readonly ConditionalWeakTable<GameObject, object> Mounted = new();
        private static readonly ConditionalWeakTable<MenuButtonElement, CategoryView> Categories = new();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                var open = AccessTools.Method(typeof(MenuNavigation), nameof(MenuNavigation.OpenMenu));
                var init = AccessTools.Method(typeof(MenuNavigation), "InitMenu");
                if (open == null || init == null)
                    throw new MissingMethodException("StellarDrive Options menu navigation");
                harmony.Patch(open, postfix: new HarmonyMethod(typeof(ModOptionsMenu), nameof(AddCategories)));
                harmony.Patch(init, postfix: new HarmonyMethod(typeof(ModOptionsMenu), nameof(BuildPanel)));
            }
            catch (Exception error)
            {
                MelonLogger.Warning("Could not install mod Options menu: " + error.Message);
            }
        }

        private static void AddCategories(GameObject ____currentSelectorAMenu)
        {
            var selector = ____currentSelectorAMenu;
            if (selector == null || selector.name != "OptionsSelectorCanvas" ||
                !selector.TryGetComponent<SelectorMenu>(out _) || Mounted.TryGetValue(selector, out _))
                return;

            Mounted.Add(selector, new object());
            var categories = StriderConfig.Categories.ToArray();
            if (categories.Length == 0) return;
            try
            {
                MountCategories(selector, categories);
            }
            catch (Exception error)
            {
                MelonLogger.Warning("Could not add mod Options categories: " + error);
            }
        }

        private static void MountCategories(GameObject selector, ModOptionsCategory[] categories)
        {
            var originalLayout = selector.GetComponent<VerticalLayoutGroup>();
            var vanillaButtons = selector.GetComponentsInChildren<MenuButtonElement>();
            var menus = vanillaButtons.Select(button => button.submenu).Where(menu => menu != null).ToArray();
            var video = menus.First(menu => menu.name == "OptionsMenuVideo");
            var gameplay = menus.First(menu => menu.name == "OptionsMenuGameplay");
            var audio = menus.First(menu => menu.name == "OptionsMenuAudio");
            var template = vanillaButtons[0];
            var font = template.GetComponentInChildren<TextMeshProUGUI>().font;
            var toggle = gameplay.GetComponentsInChildren<Toggle>(true)
                .First(control => control.GetComponentInParent<TMP_Dropdown>(true) == null);
            var donors = new ModOptionsPanel.Controls(font, audio.GetComponentInChildren<Slider>(true),
                video.GetComponentInChildren<TMP_InputField>(true), toggle,
                gameplay.GetComponentInChildren<TMP_Dropdown>(true));

            var templates = new GameObject("SSLib Options Templates", typeof(RectTransform));
            templates.SetActive(false);
            templates.transform.SetParent(selector.transform, false);
            var buttons = categories.Select(category => CreateButton(template, templates.transform, category, donors)).ToArray();

            var list = OptionsLayout.Scroll((RectTransform)selector.transform, out var scroll);
            var layout = list.GetComponent<VerticalLayoutGroup>();
            layout.padding = originalLayout.padding;
            layout.spacing = originalLayout.spacing;
            layout.childAlignment = originalLayout.childAlignment;
            layout.childControlHeight = originalLayout.childControlHeight;
            layout.childControlWidth = originalLayout.childControlWidth;
            layout.childForceExpandWidth = originalLayout.childForceExpandWidth;
            originalLayout.enabled = false;

            foreach (var button in vanillaButtons)
            {
                button.transform.SetParent(list, false);
                OptionsLayout.RevealWhenSelected(button.gameObject, scroll);
            }

            var separator = OptionsLayout.Element("Mod Options Separator", list);
            separator.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            separator.sizeDelta = new Vector2(0f, 28f);
            var lineRect = OptionsLayout.Element("Line", separator);
            lineRect.anchorMin = new Vector2(0, .5f);
            lineRect.anchorMax = new Vector2(1, .5f);
            lineRect.sizeDelta = new Vector2(-48f, 4f);
            var line = lineRect.gameObject.AddComponent<Image>();
            line.color = new Color(1f, 1f, 1f, 0.45f);
            line.raycastTarget = false;

            foreach (var button in buttons)
            {
                button.transform.SetParent(list, false);
                OptionsLayout.RevealWhenSelected(button.gameObject, scroll);
                button.gameObject.SetActive(true);
            }
        }

        private static MenuButtonElement CreateButton(MenuButtonElement template, Transform templates,
            ModOptionsCategory category, ModOptionsPanel.Controls donors)
        {
            var button = Object.Instantiate(template, templates, false);
            button.gameObject.SetActive(false);
            button.name = category.Key;
            foreach (var localizer in button.GetComponentsInChildren<LocalizeStringEvent>(true))
            {
                Object.DestroyImmediate(localizer);
            }

            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            label.richText = false;
            label.text = category.Label;
            var click = button.GetComponent<Button>();
            click.onClick = new Button.ButtonClickedEvent();
            click.onClick.AddListener(button.OpenMenu);
            click.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.submenu = OptionsLayout.Element(category.Key, templates).gameObject;
            Categories.Add(button, new CategoryView(category, donors));
            return button;
        }

        private static void BuildPanel(MenuButtonElement __0, GameObject ____currentMenu)
        {
            if (!Categories.TryGetValue(__0, out var view)) return;
            try
            {
                ModOptionsPanel.Build((RectTransform)____currentMenu.transform, view.Category, view.Donors);
            }
            catch (Exception error)
            {
                MelonLogger.Warning($"Could not open mod Options category {view.Category.Key}. {error}");
            }
        }

        private sealed class CategoryView
        {
            internal readonly ModOptionsCategory Category;
            internal readonly ModOptionsPanel.Controls Donors;

            internal CategoryView(ModOptionsCategory category, ModOptionsPanel.Controls donors)
            {
                Category = category;
                Donors = donors;
            }
        }
    }
}
