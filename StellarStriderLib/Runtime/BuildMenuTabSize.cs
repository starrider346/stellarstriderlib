using TMPro;
using UI.BuildMenu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarStriderLib.Runtime
{
    internal sealed class BuildMenuTabSize : UIBehaviour, ILayoutElement
    {
        private TMP_Text _label;
        private RectTransform _root;
        private float _horizontalInsets;
        private float _height;
        private string _lastText;
        private float _lastFontSize;

        public float minWidth => 0;
        public float preferredWidth => _label == null ? -1 : Mathf.Ceil(
            _label.GetPreferredValues(_label.text, float.PositiveInfinity, float.PositiveInfinity).x +
            _horizontalInsets + 2);
        public float flexibleWidth => 0;
        public float minHeight => 0;
        public float preferredHeight => _height > 0 ? _height : -1;
        public float flexibleHeight => 0;
        public int layoutPriority => 1000;

        internal static void Attach(TabButton tab)
        {
            var sizing = tab.GetComponent<BuildMenuTabSize>();
            if (sizing == null)
            {
                var button = tab.GetComponentInChildren<Button>(true);
                var label = button == null ? null : button.GetComponentInChildren<TMP_Text>(true);
                if (label == null || !(tab.transform is RectTransform root))
                {
                    return;
                }

                var textRect = label.rectTransform;
                StretchToParent(textRect, root);
                var offsetMax = textRect.offsetMax;
                textRect.offsetMax = new Vector2(Mathf.Min(-16, offsetMax.x), offsetMax.y);
                var left = root.InverseTransformPoint(textRect.TransformPoint(
                    new Vector3(textRect.rect.xMin, textRect.rect.yMin, 0)));
                var right = root.InverseTransformPoint(textRect.TransformPoint(
                    new Vector3(textRect.rect.xMax, textRect.rect.yMax, 0)));
                var insets = Mathf.Max(0, left.x - root.rect.xMin) +
                    Mathf.Max(0, root.rect.xMax - right.x);
                var height = Mathf.Max(1, LayoutUtility.GetPreferredHeight(root), root.rect.height);
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                sizing = tab.gameObject.AddComponent<BuildMenuTabSize>();
                sizing._root = root;
                sizing._label = label;
                sizing._horizontalInsets = insets;
                sizing._height = height;
            }

            sizing.RefreshLabel();
        }

        private static void StretchToParent(RectTransform child, RectTransform root)
        {
            while (child != root && child.parent is RectTransform parent)
            {
                var fitter = child.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                {
                    fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                var offsetMin = child.offsetMin;
                var offsetMax = child.offsetMax;
                var left = child.localPosition.x + child.rect.xMin - parent.rect.xMin;
                var right = parent.rect.xMax - child.localPosition.x - child.rect.xMax;
                child.anchorMin = new Vector2(0, child.anchorMin.y);
                child.anchorMax = new Vector2(1, child.anchorMax.y);
                child.offsetMin = new Vector2(left, offsetMin.y);
                child.offsetMax = new Vector2(-right, offsetMax.y);
                child = parent;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        }

        protected override void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            base.OnDisable();
        }

        private void OnTextChanged(Object changed)
        {
            if (changed != _label || _label == null ||
                (_lastText == _label.text && Mathf.Approximately(_lastFontSize, _label.fontSize)))
            {
                return;
            }

            RefreshLabel();
        }

        private void RefreshLabel()
        {
            if (_root == null || _label == null)
            {
                return;
            }

            _lastText = _label.text;
            _lastFontSize = _label.fontSize;
            LayoutRebuilder.MarkLayoutForRebuild(_root);
            if (_root.parent is RectTransform parent)
            {
                LayoutRebuilder.MarkLayoutForRebuild(parent);
            }
        }

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
    }
}
