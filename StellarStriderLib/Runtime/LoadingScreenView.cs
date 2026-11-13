using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    internal sealed class LoadingScreenView : IDisposable
    {
        private GameObject _root;
        private CanvasGroup _group;
        private RawImage _wallpaper;
        private Text _percentage;
        private RectTransform _fill;

        internal void Show()
        {
            if (_root == null) Create();
            SetOpacity(0);
            _root.SetActive(true);
            SetWallpaper(null);
            SetProgress(0);
        }

        private void Create()
        {
            // Assign before adding components so a partially constructed canvas can be cleaned up.
            _root = new GameObject("SSLib Loading Screen", typeof(RectTransform));
            Object.DontDestroyOnLoad(_root);
            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0;
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            _wallpaper = Child("Wallpaper", _root.transform).gameObject.AddComponent<RawImage>();
            _wallpaper.raycastTarget = true;
            var footer = Child("Footer", _root.transform);
            footer.anchorMax = new Vector2(1, 0);
            footer.sizeDelta = new Vector2(0, 130);
            footer.pivot = new Vector2(0.5f, 0);
            footer.anchoredPosition = Vector2.zero;
            footer.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.8f);

            var track = Child("Progress Track", footer);
            track.anchorMin = track.anchorMax = Vector2.zero;
            track.anchorMax = new Vector2(1, 0);
            track.offsetMin = new Vector2(40, 32);
            track.offsetMax = new Vector2(-40, 44);
            track.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.2f);
            _fill = Child("Progress", track);
            _fill.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.8f, 1, 1);

            _percentage = Label("0%", footer, 28, TextAnchor.LowerLeft);
            _percentage.rectTransform.offsetMin = new Vector2(40, 58);
            _percentage.rectTransform.offsetMax = new Vector2(-40, -18);
            var status = Label("Loading…", footer, 24, TextAnchor.LowerRight);
            status.rectTransform.offsetMin = new Vector2(160, 58);
            status.rectTransform.offsetMax = new Vector2(-40, -18);
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text Label(string text, Transform parent, int size, TextAnchor alignment)
        {
            var label = Child(text, parent).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) throw new InvalidOperationException("Loading screen font unavailable.");
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = alignment;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        internal void SetWallpaper(Texture2D texture)
        {
            _wallpaper.texture = texture;
            _wallpaper.color = texture == null ? Color.black : Color.white;
            ResizeWallpaper();
        }

        internal void ResizeWallpaper()
        {
            var texture = _wallpaper.texture;
            if (texture == null || Screen.height <= 0) return;
            var imageAspect = (float)texture.width / texture.height;
            var screenAspect = (float)Screen.width / Screen.height;
            var width = Mathf.Min(1, screenAspect / imageAspect);
            var height = Mathf.Min(1, imageAspect / screenAspect);
            _wallpaper.uvRect = new Rect((1 - width) / 2, (1 - height) / 2, width, height);
        }

        internal void SetProgress(double value)
        {
            _fill.anchorMax = new Vector2((float)value, 1);
            _percentage.text = (value >= 1 ? 100 : Math.Min(99, (int)(value * 100))) + "%";
        }

        internal void SetOpacity(double opacity)
        {
            _group.alpha = (float)opacity;
        }

        internal void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }
    }
}
