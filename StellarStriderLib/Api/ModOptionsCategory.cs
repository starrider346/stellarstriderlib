using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StellarStriderLib
{
    /// <summary>A mod-owned category in the game's Options menu.</summary>
    public sealed class ModOptionsCategory
    {
        private static int _nextOrder;
        private readonly List<ConfigOption> _options = new();

        public string Key { get; }
        public string Label { get; }
        internal int Order { get; }
        internal ModConfig Config { get; }
        internal IReadOnlyList<ConfigOption> Options => _options;

        internal ModOptionsCategory(ModConfig config, string key, string label)
        {
            Config = config;
            Key = key;
            Label = label;
            Order = Interlocked.Increment(ref _nextOrder);
        }

        /// <summary>Binds an int or float setting to a slider with inclusive bounds.</summary>
        public void RegisterSlider(string label, string configKey, float min, float max) =>
            Add(configKey, new ConfigOption(label, ConfigControl.Slider, min, max));

        /// <summary>Binds a string setting to a single-line text field.</summary>
        public void RegisterText(string label, string configKey) =>
            Add(configKey, new ConfigOption(label, ConfigControl.Text));

        /// <summary>Binds a bool setting to a toggle.</summary>
        public void RegisterToggle(string label, string configKey) =>
            Add(configKey, new ConfigOption(label, ConfigControl.Toggle));

        /// <summary>Binds an int, float or double setting to a number field with optional inclusive bounds.</summary>
        public void RegisterNumber(string label, string configKey, double? min = null, double? max = null) =>
            Add(configKey, new ConfigOption(label, ConfigControl.Number, min, max));

        /// <summary>Binds a string setting to a dropdown that persists the choice value.</summary>
        public void RegisterDropdown(string label, string configKey, params (string Value, string Label)[] choices) =>
            Add(configKey, new ConfigOption(label, ConfigControl.Dropdown, choices: choices));

        /// <summary>
        /// Sets a condition for editing this category's control. The condition runs on the Unity main thread
        /// while the panel is open. Pass null to keep the control enabled.
        /// </summary>
        public void SetEnabledWhen(string configKey, Func<bool> enabledWhen)
        {
            var option = _options.Find(candidate => Config.Key(candidate.Setting.Id) == configKey);
            if (option == null)
                throw new ArgumentException("No control in this category is bound to " + configKey + ".", nameof(configKey));
            option.EnabledWhen = enabledWhen;
        }

        private void Add(string configKey, ConfigOption option)
        {
            Config.Bind(option, configKey);
            _options.Add(option);
        }
    }

    internal enum ConfigControl
    {
        Slider,
        Text,
        Toggle,
        Number,
        Dropdown
    }

    internal sealed class ConfigOption
    {
        internal readonly string Label;
        internal readonly ConfigControl Control;
        internal readonly double? Min, Max;
        internal readonly (string Value, string Label)[] Choices;
        internal ConfigSetting Setting;
        internal Func<bool> EnabledWhen;

        internal ConfigOption(string label, ConfigControl control, double? min = null, double? max = null,
            (string Value, string Label)[] choices = null)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("An options control requires a label.", nameof(label));
            if (!Finite(min) || !Finite(max) || min > max ||
                control == ConfigControl.Slider && (min == max || max - min > float.MaxValue))
                throw new ArgumentException("Invalid numeric bounds for " + label + ".");
            if (control == ConfigControl.Dropdown &&
                (choices == null || choices.Length == 0 ||
                 choices.Any(choice => choice.Value == null || string.IsNullOrWhiteSpace(choice.Label)) ||
                 choices.Select(choice => choice.Value).Distinct(StringComparer.Ordinal).Count() != choices.Length))
                throw new ArgumentException("Dropdowns require choices with unique values and nonempty labels.", nameof(choices));

            Label = label;
            Control = control;
            Min = min;
            Max = max;
            Choices = choices?.ToArray();
        }

        private static bool Finite(double? value) =>
            !value.HasValue || !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);

        internal void ValidateBinding(ConfigSetting setting)
        {
            var type = setting.Type;
            var compatible = Control switch
            {
                ConfigControl.Slider => type == typeof(int) || type == typeof(float),
                ConfigControl.Number => type == typeof(int) || type == typeof(float) || type == typeof(double),
                ConfigControl.Toggle => type == typeof(bool),
                _ => type == typeof(string)
            };
            if (!compatible)
                throw new ArgumentException($"{Control} cannot bind a {type.Name} setting.");
            if (Control == ConfigControl.Slider && type == typeof(int) &&
                (Min < -16777216 || Max > 16777216 || Math.Truncate(Min.Value) != Min || Math.Truncate(Max.Value) != Max))
                throw new ArgumentException("Integer slider bounds must be whole numbers in the slider's exact integer range.");
            if (!Accepts(setting.DefaultValue))
                throw new ArgumentException("The config default must satisfy the options control: " + Label);
        }

        internal bool Accepts(object value)
        {
            if (Control == ConfigControl.Dropdown)
                return Choices.Any(choice => choice.Value == (string)value);
            if (Control is ConfigControl.Slider or ConfigControl.Number)
            {
                if (value is float single)
                    return !(single < (float?)Min || single > (float?)Max);
                var number = Convert.ToDouble(value);
                return !(number < Min || number > Max);
            }
            return true;
        }
    }
}
