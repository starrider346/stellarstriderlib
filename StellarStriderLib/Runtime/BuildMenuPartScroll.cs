using UnityEngine;
using UnityEngine.UI;

namespace StellarStriderLib.Runtime
{
    internal static class BuildMenuPartScroll
    {
        internal static void Attach(RectTransform viewport, Color color)
        {
            if (viewport == null || viewport.childCount == 0)
            {
                return;
            }

            var content = viewport.GetChild(viewport.childCount - 1) as RectTransform;
            if (content == null)
            {
                return;
            }

            if (viewport.GetComponent<RectMask2D>() == null)
            {
                viewport.gameObject.AddComponent<RectMask2D>();
            }

            var background = viewport.GetComponent<Image>();
            if (background == null)
            {
                background = viewport.gameObject.AddComponent<Image>();
                background.color = Color.clear;
            }

            background.raycastTarget = true;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            var categories = viewport.parent.GetComponentInChildren<BuildMenuTabLayout>();
            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (categories != null && layout != null)
            {
                layout.padding.top += categories.ContentTopPadding;
            }

            foreach (var graphic in content.GetComponentsInChildren<MaskableGraphic>(true))
            {
                graphic.maskable = true;
                graphic.RecalculateClipping();
            }

            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var track = new GameObject("Parts Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            track.layer = viewport.gameObject.layer;
            var trackRect = (RectTransform)track.transform;
            trackRect.SetParent(viewport, false);
            trackRect.anchorMin = new Vector2(1, 0);
            trackRect.anchorMax = Vector2.one;
            trackRect.pivot = new Vector2(1, 0.5f);
            trackRect.sizeDelta = new Vector2(12, -8);
            trackRect.anchoredPosition = new Vector2(-2, 0);
            track.GetComponent<Image>().color = new Color(color.r, color.g, color.b, 0.15f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.layer = viewport.gameObject.layer;
            var handleRect = (RectTransform)handle.transform;
            handleRect.SetParent(trackRect, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = color;
            var scrollbar = track.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var navigation = scrollbar.navigation;
            navigation.mode = Navigation.Mode.None;
            scrollbar.navigation = navigation;

            var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            if (viewport.GetComponent<BuildMenuWheelInput>() == null)
            {
                viewport.gameObject.AddComponent<BuildMenuWheelInput>();
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }
    }
}
