using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace StellarStriderLib.Runtime
{
    internal sealed class BuildMenuWheelInput : MonoBehaviour, IScrollHandler
    {
        private static readonly Func<float> WheelDeltaPerTick = (Func<float>)Delegate.CreateDelegate(
            typeof(Func<float>), AccessTools.PropertyGetter(typeof(InputSystem), "scrollWheelDeltaPerTick"));

        private ScrollRect _scroll;
        private Canvas _canvas;
        private EventSystem _eventSystem;
        private PointerEventData _pointer;
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private int _lastScrollFrame = -1;

        private void Awake()
        {
            _scroll = GetComponent<ScrollRect>();
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnScroll(PointerEventData eventData)
        {
            _lastScrollFrame = Time.frameCount;
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            var events = EventSystem.current;
            if (_lastScrollFrame == Time.frameCount || mouse == null || events == null ||
                _scroll == null || !_scroll.IsActive() || !Application.isFocused ||
                Cursor.lockState == CursorLockMode.Locked)
            {
                return;
            }

            var delta = mouse.scroll.ReadValue();
            if (delta.sqrMagnitude == 0)
            {
                return;
            }

            var position = mouse.position.ReadValue();
            var camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_scroll.viewport, position, camera))
            {
                return;
            }

            if (_eventSystem != events)
            {
                _eventSystem = events;
                _pointer = new PointerEventData(events);
            }

            _pointer.Reset();
            _pointer.position = position;
            _hits.Clear();
            events.RaycastAll(_pointer, _hits);
            if (_hits.Count == 0 || !_hits[0].gameObject.transform.IsChildOf(transform))
            {
                return;
            }

            var module = events.currentInputModule as InputSystemUIInputModule;
            _pointer.scrollDelta = delta / WheelDeltaPerTick() *
                                   (module == null ? 1 : module.scrollDeltaPerTick);
            _scroll.OnScroll(_pointer);
        }
    }
}
