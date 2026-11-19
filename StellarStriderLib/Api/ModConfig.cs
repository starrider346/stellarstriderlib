using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StellarStriderLib.Core;

namespace StellarStriderLib
{
    /// <summary>Owns one local config file. Register settings and options during mod initialization.</summary>
    public sealed class ModConfig
    {
        private readonly object _gate = new();
        private readonly ConfigFile _file;
        private readonly Action<string> _warn;
        private readonly Dictionary<string, ConfigSetting> _settings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ModOptionsCategory> _categories = new(StringComparer.Ordinal);

        public string ModId { get; }
        internal IEnumerable<ModOptionsCategory> Categories => _categories.Values;

        internal ModConfig(string modId, string path, Action<string> warn, string legacyPath)
        {
            ModId = modId;
            _warn = warn;
            _file = new ConfigFile(path, warn, legacyPath);
        }

        /// <summary>Declares a bool, string, int, float or double setting and its default.</summary>
        public void Register<T>(string id, T defaultValue)
        {
            Keys.Identifier(id);
            var setting = new ConfigSetting(id, typeof(T), defaultValue);

            lock (_gate)
            {
                if (_settings.ContainsKey(id))
                    throw new ArgumentException("Config already registered: " + Key(id), nameof(id));

                if (_file.Document.TryGetValue(id, out var token))
                {
                    if (setting.TryRead(token, out var value)) setting.Value = value;
                    else WarnInvalid(setting);
                }
                _settings.Add(id, setting);
            }
        }

        /// <summary>Returns the registered value from memory using its declared type.</summary>
        public T Get<T>(string id)
        {
            lock (_gate)
            {
                return (T)Find(id, typeof(T)).Value;
            }
        }

        /// <summary>Validates and saves a value. A failed write leaves the in-memory value intact.</summary>
        public void Set<T>(string id, T value)
        {
            lock (_gate)
            {
                Save(Find(id, typeof(T)), value);
            }
        }

        /// <summary>Combines this mod's ID with a local setting ID.</summary>
        public string Key(string id) => ModId + ":" + Keys.Identifier(id);

        /// <summary>Registers a category under the vanilla categories in the Options menu.</summary>
        public ModOptionsCategory RegisterCategory(string id, string label)
        {
            Keys.Identifier(id);
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("An options category requires a label.", nameof(label));
            var category = new ModOptionsCategory(this, Key(id), label);
            _categories.Add(id, category);
            return category;
        }

        internal object Get(ConfigSetting setting)
        {
            lock (_gate)
            {
                return setting.Value;
            }
        }

        internal void Set(ConfigSetting setting, object value)
        {
            lock (_gate)
            {
                Save(setting, value);
            }
        }

        private void Save(ConfigSetting setting, object value)
        {
            if (!setting.Accepts(value))
                throw new ArgumentException("Invalid value for " + Key(setting.Id) + ".", nameof(value));

            if (_file.NeedsMigration || !_file.Document.TryGetValue(setting.Id, out var previous) ||
                !setting.TryRead(previous, out var persisted) || !Equals(persisted, value))
            {
                var document = CopyWithDefaults();
                document.Property(setting.Id).Replace(new JProperty(setting.Id, new JValue(value)));
                _file.Write(document);
            }
            setting.Value = value;
        }

        internal void Bind(ConfigOption option, string configKey)
        {
            if (configKey == null || !configKey.StartsWith(ModId + ":", StringComparison.Ordinal))
                throw new ArgumentException("Use a qualified config key owned by " + ModId + ".", nameof(configKey));

            lock (_gate)
            {
                var setting = Find(configKey.Substring(ModId.Length + 1));
                if (setting.Option != null)
                    throw new ArgumentException("Config already has an options control: " + configKey, nameof(configKey));
                option.ValidateBinding(setting);
                setting.Option = option;
                option.Setting = setting;
                if (!option.Accepts(setting.Value))
                {
                    setting.Value = setting.DefaultValue;
                    WarnInvalid(setting);
                }
            }
        }

        internal void SaveDefaults()
        {
            lock (_gate)
            {
                if (!_file.CanSaveDefaults) return;
                var document = CopyWithDefaults();
                if (!_file.NeedsMigration && document.Count == _file.Document.Count) return;
                try
                {
                    _file.Write(document);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _warn($"Could not save config defaults for {ModId}. {error.Message}");
                }
            }
        }

        private JObject CopyWithDefaults()
        {
            var document = (JObject)_file.Document.DeepClone();
            foreach (var setting in _settings.Values)
            {
                if (document.Property(setting.Id) == null) document.Add(setting.Id, new JValue(setting.DefaultValue));
            }
            return document;
        }

        private ConfigSetting Find(string id, Type type = null)
        {
            if (!_settings.TryGetValue(id, out var setting))
                throw new KeyNotFoundException("Config is not registered: " + Key(id));
            if (type != null && setting.Type != type)
                throw new ArgumentException($"{Key(id)} was registered as {setting.Type.Name}.", nameof(type));
            return setting;
        }

        private void WarnInvalid(ConfigSetting setting) =>
            _warn($"Invalid saved config value for {Key(setting.Id)}. Using its registered default.");
    }

    internal sealed class ConfigSetting
    {
        internal readonly string Id;
        internal readonly Type Type;
        internal readonly object DefaultValue;
        internal object Value;
        internal ConfigOption Option;

        internal ConfigSetting(string id, Type type, object defaultValue)
        {
            Id = id;
            Type = type;
            if (type != typeof(bool) && type != typeof(string) && type != typeof(int) &&
                type != typeof(float) && type != typeof(double))
                throw new ArgumentException("Config values must be bool, string, int, float or double.");
            if (!Accepts(defaultValue))
                throw new ArgumentException("Config defaults must be non-null and numbers must be finite.", nameof(defaultValue));
            DefaultValue = defaultValue;
            Value = defaultValue;
        }

        internal bool Accepts(object value) =>
            value?.GetType() == Type &&
            (value is not float single || !float.IsNaN(single) && !float.IsInfinity(single)) &&
            (value is not double number || !double.IsNaN(number) && !double.IsInfinity(number)) &&
            (Option == null || Option.Accepts(value));

        internal bool TryRead(JToken token, out object value)
        {
            value = null;
            if (Type == typeof(bool) && token.Type != JTokenType.Boolean ||
                Type == typeof(string) && token.Type != JTokenType.String ||
                Type == typeof(int) && token.Type != JTokenType.Integer ||
                (Type == typeof(float) || Type == typeof(double)) && token.Type is not (JTokenType.Integer or JTokenType.Float))
                return false;

            try
            {
                value = token.ToObject(Type);
                return Accepts(value);
            }
            catch (Exception error) when (error is JsonException or OverflowException or FormatException or InvalidCastException)
            {
                return false;
            }
        }
    }
}
