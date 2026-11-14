using System;
using System.Linq;
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
        private RawImage _sidebarBackdrop;
        private RawImage _logo;
        private Text _world;
        private Text _percentage;
        private RectTransform _fill;

        internal void Show()
        {
            if (_root == null) Create();
            SetOpacity(0);
            _root.SetActive(true);
            if (_logo.texture == null)
                _logo.texture = Resources.FindObjectsOfTypeAll<Texture2D>()
                    .FirstOrDefault(texture => texture.name == "sd_white_small");
            _logo.enabled = _logo.texture != null;
            SetWallpaper(null);
            SetWorldName(null);
            SetProgress(0);
        }

        private void Create()
        {
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

            var sidebar = Child("Title Sidebar", _root.transform);
            sidebar.anchorMax = new Vector2(0, 1);
            sidebar.pivot = new Vector2(0, 0.5f);
            sidebar.sizeDelta = new Vector2(415, 0);
            sidebar.anchoredPosition = Vector2.zero;
            _sidebarBackdrop = Child("Sidebar Backdrop", sidebar).gameObject.AddComponent<RawImage>();
            _sidebarBackdrop.raycastTarget = false;

            _logo = Child("StellarDrive Title", sidebar).gameObject.AddComponent<RawImage>();
            _logo.raycastTarget = false;
            var logoRect = _logo.rectTransform;
            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0, 1);
            logoRect.pivot = new Vector2(0, 1);
            logoRect.anchoredPosition = new Vector2(40, -40);
            logoRect.sizeDelta = new Vector2(315, 131.25f);

            _world = Label("Loading…", sidebar, 32, TextAnchor.UpperLeft);
            _world.supportRichText = false;
            var titleFont = Resources.FindObjectsOfTypeAll<Font>()
                .FirstOrDefault(font => font.name == "TitlesTabsLabels_Anta-Regular");
            if (titleFont != null) _world.font = titleFont;
            var worldRect = _world.rectTransform;
            worldRect.anchorMin = new Vector2(0, 1);
            worldRect.anchorMax = Vector2.one;
            worldRect.pivot = new Vector2(0, 1);
            worldRect.offsetMin = new Vector2(40, -275);
            worldRect.offsetMax = new Vector2(-40, -191.25f);

            var track = Child("Progress Track", sidebar);
            track.anchorMin = Vector2.zero;
            track.anchorMax = new Vector2(1, 0);
            track.offsetMin = new Vector2(40, 32);
            track.offsetMax = new Vector2(-40, 44);
            track.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.2f);
            _fill = Child("Progress", track);
            _fill.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.8f, 1, 1);

            _percentage = Label("0%", sidebar, 28, TextAnchor.LowerLeft);
            _percentage.rectTransform.anchorMin = Vector2.zero;
            _percentage.rectTransform.anchorMax = new Vector2(1, 0);
            _percentage.rectTransform.offsetMin = new Vector2(40, 58);
            _percentage.rectTransform.offsetMax = new Vector2(-40, 112);
        }

        internal void SetWorldName(string worldName)
        {
            _world.text = string.IsNullOrWhiteSpace(worldName) ? "Loading…" : "Loading " + worldName;
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
            _sidebarBackdrop.texture = texture;
            _sidebarBackdrop.color = texture == null
                ? Color.black
                : new Color(0.55f, 0.55f, 0.55f, 1);
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
            var canvasSize = ((RectTransform)_root.transform).rect.size;
            if (canvasSize.x <= 0 || canvasSize.y <= 0) return;
            var sidebarWidth = Mathf.Clamp01(415 / canvasSize.x);
            var uv = _wallpaper.uvRect;
            _sidebarBackdrop.uvRect = new Rect(uv.x, uv.y, uv.width * sidebarWidth, uv.height);
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
