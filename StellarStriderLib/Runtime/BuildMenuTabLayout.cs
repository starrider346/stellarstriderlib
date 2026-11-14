using System.Collections.Generic;
using StellarStriderLib.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StellarStriderLib.Runtime
{
    internal sealed class BuildMenuTabLayout : LayoutGroup
    {
        private RectTransform _panel;
        private Vector2 _panelOffsetMin;
        private Vector2 _panelOffsetMax;
        private Vector2 _panelAnchoredPosition;
        private float _originalHeight;
        private float _spacing;
        private float _rowSpacing;
        private RectTransform _dividerTemplate;
        private readonly List<RectTransform> _dividers = new List<RectTransform>();
        private readonly List<float> _rowBottoms = new List<float>();
        private bool _dividersDirty;
        private bool _configured;
        private bool _panelManagedByLayout;
        private Float2[] _sizes = System.Array.Empty<Float2>();
        private Float2[] _positions = System.Array.Empty<Float2>();
        private float _height;
        private float _appliedExtraHeight = -1;

        internal int ContentTopPadding { get; private set; }

        internal static void Attach(RectTransform tabs, RectTransform panel)
        {
            if (tabs == null || panel == null || !(tabs.parent is RectTransform parent))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabs);
            var wrapping = tabs.GetComponent<BuildMenuTabLayout>();
            if (wrapping == null)
            {
                var original = tabs.GetComponent<HorizontalLayoutGroup>();
                var padding = original == null
                    ? new RectOffset()
                    : new RectOffset(
                        original.padding.left, original.padding.right, original.padding.top, original.padding.bottom);
                padding.left = Mathf.Max(8, padding.left);
                padding.right = Mathf.Max(8, padding.right);
                padding.top = Mathf.Max(8, padding.top);
                padding.bottom = Mathf.Max(16, padding.bottom);
                var spacing = original == null ? 16 : Mathf.Max(16, original.spacing);
                var topLeft = parent.InverseTransformPoint(tabs.TransformPoint(
                    new Vector3(tabs.rect.xMin, tabs.rect.yMax, 0)));
                var inset = Mathf.Max(0, topLeft.x - parent.rect.xMin);
                var originalHeight = tabs.rect.height;
                foreach (var group in tabs.GetComponents<LayoutGroup>())
                {
                    Object.DestroyImmediate(group);
                }

                var previousFitter = tabs.GetComponent<ContentSizeFitter>();
                if (previousFitter != null)
                {
                    previousFitter.enabled = false;
                }

                tabs.anchorMin = new Vector2(0, 1);
                tabs.anchorMax = Vector2.one;
                tabs.pivot = new Vector2(0, 1);
                tabs.sizeDelta = new Vector2(-2 * inset, originalHeight);
                tabs.anchoredPosition = new Vector2(inset, topLeft.y - parent.rect.yMax);
                wrapping = tabs.gameObject.AddComponent<BuildMenuTabLayout>();
                wrapping.padding = padding;
                wrapping._spacing = spacing;
                wrapping._rowSpacing = Mathf.Max(24, spacing);
                wrapping._dividerTemplate = parent.Find("Line") as RectTransform;
                if (wrapping._dividerTemplate != null)
                {
                    wrapping._dividerTemplate.gameObject.SetActive(false);
                }
                wrapping._panel = panel;
                if (panel.anchorMin.y < panel.anchorMax.y)
                {
                    var offsetMin = panel.offsetMin;
                    offsetMin.y = 32;
                    panel.offsetMin = offsetMin;
                }

                wrapping._panelOffsetMin = panel.offsetMin;
                wrapping._panelOffsetMax = panel.offsetMax;
                wrapping._panelAnchoredPosition = panel.anchoredPosition;
                wrapping._originalHeight = originalHeight;
                wrapping._configured = true;
                var parentLayout = parent.GetComponent<VerticalLayoutGroup>();
                var panelLayout = panel.GetComponent<LayoutElement>();
                wrapping._panelManagedByLayout = panel.parent == parent && parentLayout != null &&
                                                 parentLayout.isActiveAndEnabled &&
                                                 (panelLayout == null || !panelLayout.ignoreLayout);

                var fitter = tabs.GetComponent<ContentSizeFitter>() ??
                             tabs.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.enabled = true;
                if (tabs.GetComponent<RectMask2D>() == null)
                {
                    tabs.gameObject.AddComponent<RectMask2D>();
                }
            }

            foreach (RectTransform child in tabs)
            {
                var fitter = child.GetComponent<ContentSizeFitter>();
                if (fitter == null || !fitter.enabled)
                {
                    continue;
                }

                var size = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                var childWidth = LayoutUtility.GetPreferredWidth(child);
                var childHeight = LayoutUtility.GetPreferredHeight(child);
                size.preferredWidth = childWidth > 0 ? childWidth : child.rect.width;
                size.preferredHeight = childHeight > 0 ? childHeight : child.rect.height;
                fitter.enabled = false;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(tabs);
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(padding.horizontal, rectTransform.rect.width, 0, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            Measure();
            SetLayoutInputForAxis(_height, _height, 0, 1);
        }

        public override void SetLayoutHorizontal()
        {
            Measure();
            for (var index = 0; index < rectChildren.Count; index++)
            {
                SetChildAlongAxis(rectChildren[index], 0, padding.left + _positions[index].X, _sizes[index].X);
            }
        }

        public override void SetLayoutVertical()
        {
            Measure();
            for (var index = 0; index < rectChildren.Count; index++)
            {
                SetChildAlongAxis(rectChildren[index], 1, padding.top + _positions[index].Y, _sizes[index].Y);
            }

            _dividersDirty = true;

            if (!_configured || _panel == null || _panelManagedByLayout)
            {
                return;
            }

            var extraHeight = Mathf.Max(0, _height - _originalHeight);
            if (Mathf.Approximately(extraHeight, _appliedExtraHeight))
            {
                return;
            }

            _appliedExtraHeight = extraHeight;
            if (Mathf.Approximately(_panel.anchorMin.y, _panel.anchorMax.y))
            {
                _panel.anchoredPosition = _panelAnchoredPosition - new Vector2(0, extraHeight);
            }
            else
            {
                var nativeTop = _panelOffsetMax.y - extraHeight;
                var viewportTop = nativeTop;
                if (_panel.parent is RectTransform panelParent)
                {
                    var tabsBottom = panelParent.InverseTransformPoint(rectTransform.TransformPoint(
                        new Vector3(0, rectTransform.rect.yMin, 0)));
                    viewportTop = Mathf.Max(nativeTop, tabsBottom.y - panelParent.rect.yMax);
                }

                ContentTopPadding = Mathf.CeilToInt(viewportTop - nativeTop);
                _panel.offsetMin = _panelOffsetMin;
                _panel.offsetMax = new Vector2(_panelOffsetMax.x, viewportTop);
            }
        }

        private void Measure()
        {
            var width = Mathf.Max(1, rectTransform.rect.width - padding.horizontal);
            _sizes = new Float2[rectChildren.Count];
            for (var index = 0; index < rectChildren.Count; index++)
            {
                var child = rectChildren[index];
                var childWidth = LayoutUtility.GetPreferredWidth(child);
                var childHeight = LayoutUtility.GetPreferredHeight(child);
                _sizes[index] = new Float2(
                    Mathf.Min(width, Mathf.Max(1, childWidth > 0 ? childWidth : child.rect.width)),
                    Mathf.Max(1, childHeight > 0 ? childHeight : child.rect.height));
            }

            _positions = CategoryLayout.Arrange(_sizes, width, _spacing, _rowSpacing, out var height);
            _height = height + padding.vertical;
            _rowBottoms.Clear();
            var bottom = 0f;
            for (var index = 0; index < _positions.Length; index++)
            {
                bottom = Mathf.Max(bottom, _positions[index].Y + _sizes[index].Y);
                if (index + 1 == _positions.Length || _positions[index + 1].X <= _positions[index].X)
                {
                    _rowBottoms.Add(bottom);
                    bottom = 0;
                }
            }
        }

        private void ArrangeDividers()
        {
            if (_dividerTemplate == null)
            {
                return;
            }

            while (_dividers.Count < _rowBottoms.Count)
            {
                var line = Object.Instantiate(_dividerTemplate.gameObject, rectTransform, false);
                line.name = "Category Row Divider";
                var element = line.GetComponent<LayoutElement>() ?? line.AddComponent<LayoutElement>();
                element.ignoreLayout = true;
                foreach (var graphic in line.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = false;
                }

                _dividers.Add((RectTransform)line.transform);
            }

            for (var index = 0; index < _dividers.Count; index++)
            {
                var line = _dividers[index];
                var visible = index < _rowBottoms.Count;
                if (line.gameObject.activeSelf != visible)
                {
                    line.gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                var lineHeight = Mathf.Max(1, _dividerTemplate.rect.height);
                line.anchorMin = new Vector2(0, 1);
                line.anchorMax = Vector2.one;
                line.pivot = new Vector2(.5f, .5f);
                line.sizeDelta = new Vector2(-padding.horizontal, lineHeight);
                line.anchoredPosition = new Vector2((padding.left - padding.right) / 2f,
                    -padding.top - _rowBottoms[index] - lineHeight / 2);
            }
        }

        private void LateUpdate()
        {
            if (!_dividersDirty || CanvasUpdateRegistry.IsRebuildingLayout() ||
                CanvasUpdateRegistry.IsRebuildingGraphics())
            {
                return;
            }

            _dividersDirty = false;
            ArrangeDividers();
        }
    }
}
