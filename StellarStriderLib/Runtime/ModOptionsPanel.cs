using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MelonLoader;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    internal sealed class ModOptionsPanel : MonoBehaviour
    {
        private readonly List<OptionBinding> _bindings = new();

        internal sealed class Controls
        {
            internal readonly TMP_FontAsset Font;
            internal readonly Slider Slider;
            internal readonly TMP_InputField Input;
            internal readonly Toggle Toggle;
            internal readonly TMP_Dropdown Dropdown;

            internal Controls(TMP_FontAsset font, Slider slider, TMP_InputField input, Toggle toggle, TMP_Dropdown dropdown)
            {
                Font = font;
                Slider = slider;
                Input = input;
                Toggle = toggle;
                Dropdown = dropdown;
            }
        }

        internal static void Build(RectTransform root, ModOptionsCategory category, Controls controls)
        {
            root.gameObject.SetActive(false);
            try
            {
                var panel = root.gameObject.AddComponent<ModOptionsPanel>();
                OptionsLayout.Stretch(root);
                var content = OptionsLayout.Scroll(root, out var scroll);
                var layout = content.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(52, 52, 34, 24);
                layout.spacing = 14;
                Text(content, category.Label, controls.Font, 62, 88).fontStyle = FontStyles.Bold;
                foreach (var option in category.Options)
                {
                    panel.AddOption(content, scroll, category.Config, option, controls);
                }
                panel.RefreshInteractivity();
            }
            finally
            {
                root.gameObject.SetActive(true);
            }
        }

        private void Update() => RefreshInteractivity();

        private void RefreshInteractivity()
        {
            foreach (var binding in _bindings)
            {
                binding.RefreshInteractivity();
            }
        }

        private void AddOption(RectTransform content, ScrollRect scroll, ModConfig config, ConfigOption option,
            Controls controls)
        {
            var row = OptionsLayout.Element(option.Setting.Id, content);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var label = Text(row, option.Label, controls.Font, 44, 66);
            label.GetComponent<LayoutElement>().flexibleWidth = 1;

            var fields = OptionsLayout.Element("Control", row);
            var fieldSize = fields.gameObject.AddComponent<LayoutElement>();
            fieldSize.minWidth = 220;
            fieldSize.preferredWidth = 480;
            var fieldLayout = fields.gameObject.AddComponent<HorizontalLayoutGroup>();
            fieldLayout.spacing = 12;
            fieldLayout.childAlignment = TextAnchor.MiddleLeft;
            fieldLayout.childControlWidth = true;
            fieldLayout.childControlHeight = true;
            fieldLayout.childForceExpandWidth = false;
            fieldLayout.childForceExpandHeight = false;

            var status = Text(content, "", controls.Font, 32, 44);
            status.gameObject.name = option.Setting.Id + " Status";
            status.color = new Color(1f, .55f, .38f);
            status.gameObject.SetActive(false);
            var binding = new OptionBinding(config, option, status, row.gameObject.AddComponent<CanvasGroup>(),
                RefreshInteractivity);

            Selectable field = option.Control switch
            {
                ConfigControl.Slider => AddSlider(fields, binding, controls),
                ConfigControl.Text or ConfigControl.Number => AddInput(fields, binding, controls.Input),
                ConfigControl.Toggle => AddToggle(fields, binding, controls.Toggle),
                ConfigControl.Dropdown => AddDropdown(fields, binding, controls.Dropdown),
                _ => throw new ArgumentOutOfRangeException()
            };
            binding.Field = field;
            _bindings.Add(binding);
            field.name = option.Control.ToString();
            field.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            OptionsLayout.RevealWhenSelected(field.gameObject, scroll);
            field.gameObject.SetActive(true);
        }

        private static TextMeshProUGUI Text(Transform parent, string value, TMP_FontAsset font, int size, int height)
        {
            var rect = OptionsLayout.Element("Label", parent);
            rect.gameObject.AddComponent<LayoutElement>().minHeight = height;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static T CloneControl<T>(T donor, Transform parent, float minWidth, float preferredWidth, float height)
            where T : Selectable
        {
            var control = Object.Instantiate(donor, parent, false);
            control.gameObject.SetActive(false);
            var element = control.GetComponent<LayoutElement>() ?? control.gameObject.AddComponent<LayoutElement>();
            element.minWidth = minWidth;
            element.preferredWidth = preferredWidth;
            element.minHeight = height;
            return control;
        }

        private static Slider AddSlider(Transform row, OptionBinding binding, Controls controls)
        {
            var slider = CloneControl(controls.Slider, row, 104, 344, 64);
            LayoutSlider(slider);
            var readout = Text(row, "", controls.Font, 40, 64);
            var readoutSize = readout.GetComponent<LayoutElement>();
            readoutSize.minWidth = 104;
            readoutSize.preferredWidth = 124;
            readout.alignment = TextAlignmentOptions.MidlineRight;
            readout.textWrappingMode = TextWrappingModes.NoWrap;
            readout.enableAutoSizing = true;
            readout.fontSizeMin = 16;
            readout.fontSizeMax = 40;
            slider.onValueChanged = new Slider.SliderEvent();
            slider.minValue = (float)binding.Option.Min.Value;
            slider.maxValue = (float)binding.Option.Max.Value;
            slider.wholeNumbers = binding.Option.Setting.Type == typeof(int);
            var edit = slider.gameObject.AddComponent<OptionsSliderEdit>();
            edit.Initialize(slider, value => binding.Commit(slider.wholeNumbers ? (object)Mathf.RoundToInt(value) : value));
            slider.onValueChanged.AddListener(value => readout.text = Format(value));
            binding.RestoreWith(value =>
            {
                var numeric = Convert.ToSingle(value);
                slider.SetValueWithoutNotify(numeric);
                readout.text = Format(value);
            });
            return slider;
        }

        private static void LayoutSlider(Slider slider)
        {
            var background = slider.transform.Find("Background") as RectTransform;
            var fillArea = slider.fillRect?.parent as RectTransform;
            var handleArea = slider.handleRect?.parent as RectTransform;
            if (background == null || fillArea == null || handleArea == null) return;

            // The audio prefab's tick marks are positioned for its original width.
            foreach (Transform child in slider.transform)
            {
                if (child.name.StartsWith("Step", StringComparison.Ordinal)) child.gameObject.SetActive(false);
            }

            CenterTrack(background, 12);
            CenterTrack(fillArea, 12);
            CenterTrack(handleArea, 1);
            slider.handleRect.localScale *= .8f;

            if (!slider.TryGetComponent<Graphic>(out _))
            {
                var hitArea = slider.gameObject.AddComponent<Image>();
                hitArea.color = Color.clear;
                hitArea.canvasRenderer.cullTransparentMesh = false;
            }

            static void CenterTrack(RectTransform rect, float height)
            {
                const float horizontalInset = 24;
                rect.anchorMin = new Vector2(0, .5f);
                rect.anchorMax = new Vector2(1, .5f);
                rect.sizeDelta = new Vector2(-2 * horizontalInset, height);
                rect.anchoredPosition = Vector2.zero;
            }
        }

        private static TMP_InputField AddInput(Transform row, OptionBinding binding, TMP_InputField donor)
        {
            var input = CloneControl(donor, row, 220, 480, 66);
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 0;
            input.onValidateInput = null;
            input.readOnly = false;
            input.richText = false;
            binding.RestoreWith(value => input.SetTextWithoutNotify(Format(value)));
            input.onEndEdit.AddListener(text =>
            {
                if (input.wasCanceled)
                {
                    binding.Restore();
                    return;
                }
                var value = binding.Option.Control == ConfigControl.Text ? text : ParseNumber(text, binding.Option.Setting.Type);
                binding.Commit(value);
            });
            return input;
        }

        private static Toggle AddToggle(Transform row, OptionBinding binding, Toggle donor)
        {
            var toggle = CloneControl(donor, row, 56, 56, 56);
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.group = null;
            binding.RestoreWith(value => toggle.SetIsOnWithoutNotify((bool)value));
            toggle.onValueChanged.AddListener(value => binding.Commit(value));
            return toggle;
        }

        private static TMP_Dropdown AddDropdown(Transform row, OptionBinding binding, TMP_Dropdown donor)
        {
            var dropdown = CloneControl(donor, row, 220, 480, 66);
            var choices = binding.Option.Choices;
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dropdown.ClearOptions();
            dropdown.AddOptions(choices.Select(choice => choice.Label).ToList());
            dropdown.captionText.richText = false;
            dropdown.itemText.richText = false;
            binding.RestoreWith(value => dropdown.SetValueWithoutNotify(Array.FindIndex(choices, choice => choice.Value == (string)value)));
            dropdown.onValueChanged.AddListener(index => binding.Commit(choices[index].Value));
            return dropdown;
        }

        private static string Format(object value) => Convert.ToString(value, CultureInfo.CurrentCulture);

        private static object ParseNumber(string text, Type type)
        {
            var culture = CultureInfo.CurrentCulture;
            if (type == typeof(int))
                return int.TryParse(text, NumberStyles.Integer, culture, out var integer) ? integer : (object)null;
            if (type == typeof(float))
                return float.TryParse(text, NumberStyles.Float, culture, out var single) ? single : (object)null;
            return double.TryParse(text, NumberStyles.Float, culture, out var number) ? number : (object)null;
        }

        private sealed class OptionBinding
        {
            internal readonly ConfigOption Option;
            internal Selectable Field;

            private readonly ModConfig _config;
            private readonly TextMeshProUGUI _status;
            private readonly CanvasGroup _row;
            private readonly Action _refreshPanel;
            private Action<object> _restore;
            private bool _enabled = true;
            private bool _conditionFailed;

            internal OptionBinding(ModConfig config, ConfigOption option, TextMeshProUGUI status, CanvasGroup row,
                Action refreshPanel)
            {
                _config = config;
                Option = option;
                _status = status;
                _row = row;
                _refreshPanel = refreshPanel;
            }

            internal void RefreshInteractivity()
            {
                if (Field == null) return;
                var enabled = EvaluateCondition();
                if (enabled == _enabled) return;

                _enabled = enabled;
                if (!enabled && Field is TMP_InputField input && input.isFocused)
                    input.DeactivateInputField(clearSelection: true);
                Field.interactable = enabled;
                _row.alpha = enabled ? 1f : .5f;
                _row.blocksRaycasts = enabled;
                if (!enabled)
                {
                    if (Field.TryGetComponent<OptionsSliderEdit>(out var slider)) slider.Cancel();
                    if (Field is TMP_Dropdown dropdown && dropdown.IsExpanded) dropdown.Hide();
                    if (_status != null) _status.gameObject.SetActive(false);
                }
                Restore();
            }

            private bool EvaluateCondition()
            {
                try
                {
                    var enabled = Option.EnabledWhen?.Invoke() ?? true;
                    _conditionFailed = false;
                    return enabled;
                }
                catch (Exception error)
                {
                    if (!_conditionFailed)
                        MelonLogger.Warning($"Could not evaluate whether {_config.Key(Option.Setting.Id)} is enabled. {error.Message}");
                    _conditionFailed = true;
                    return false;
                }
            }

            internal void RestoreWith(Action<object> restore)
            {
                _restore = restore;
                Restore();
            }

            internal void Restore()
            {
                if (_status != null) _restore(_config.Get(Option.Setting));
            }

            internal void Commit(object value)
            {
                RefreshInteractivity();
                if (!_enabled)
                {
                    Restore();
                    return;
                }

                var message = "";
                try
                {
                    _config.Set(Option.Setting, value);
                }
                catch (ArgumentException)
                {
                    message = "Enter a valid value within the allowed range.";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    MelonLogger.Warning($"Could not save {_config.Key(Option.Setting.Id)}. {error.Message}");
                    message = "Could not save this setting.";
                }

                if (_status == null) return;
                Restore();
                _status.text = message;
                _status.gameObject.SetActive(message.Length > 0);
                _refreshPanel();
            }
        }
    }

    internal sealed class OptionsSliderEdit : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDeselectHandler,
        ISubmitHandler
    {
        private Slider _slider;
        private Action<float> _commit;
        private bool _pending;
        private bool _pointerDown;

        internal void Initialize(Slider slider, Action<float> commit)
        {
            _slider = slider;
            _commit = commit;
            slider.onValueChanged.AddListener(_ => _pending = true);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _pointerDown = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pointerDown = false;
            Commit();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (!_pointerDown) Commit();
        }

        public void OnSubmit(BaseEventData eventData) => Commit();

        internal void Cancel()
        {
            _pointerDown = false;
            _pending = false;
        }

        private void OnDisable()
        {
            _pointerDown = false;
            Commit();
        }

        private void Commit()
        {
            if (!_pending || _slider == null) return;
            _pending = false;
            _commit(_slider.value);
        }
    }
}
