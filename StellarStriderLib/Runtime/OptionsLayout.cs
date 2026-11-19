using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarStriderLib.Runtime
{
    internal static class OptionsLayout
    {
        internal static RectTransform Element(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static RectTransform Scroll(RectTransform root, out ScrollRect scroll)
        {
            var viewport = Element("Options Scroll View", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;

            var content = Element("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            return content;
        }

        internal static void RevealWhenSelected(GameObject control, ScrollRect scroll) =>
            control.AddComponent<OptionsScrollSelection>().Scroll = scroll;
    }

    internal sealed class OptionsScrollSelection : MonoBehaviour, ISelectHandler
    {
        internal ScrollRect Scroll;

        public void OnSelect(BaseEventData eventData)
        {
            var view = Scroll.viewport;
            var item = RectTransformUtility.CalculateRelativeRectTransformBounds(view, transform);
            var content = RectTransformUtility.CalculateRelativeRectTransformBounds(view, Scroll.content);
            var overflow = content.size.y - view.rect.height;
            if (overflow <= 0f) return;

            if (item.max.y > view.rect.yMax)
                Scroll.verticalNormalizedPosition += (item.max.y - view.rect.yMax) / overflow;
            else if (item.min.y < view.rect.yMin)
                Scroll.verticalNormalizedPosition -= (view.rect.yMin - item.min.y) / overflow;
        }
    }
}
