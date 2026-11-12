using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StellarStriderLib.Runtime
{
    /// <summary>Validates JSON state schemas, completes defaults and selects or migrates saved and replicated fields.</summary>
    internal static class StateFields
    {
        internal const int MaxBytes = 16384;

        internal static void ValidateDefinition(PartStateDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            if (definition.Version < 1 || definition.Fields == null || definition.Fields.Length > 128)
            {
                throw new ArgumentException("Invalid part state schema.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in definition.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 64 ||
                    !names.Add(field.Name) || field.Default == null || field.Default.Type == JTokenType.Null)
                {
                    throw new ArgumentException("State fields require unique names and non-null JSON defaults.");
                }

                CheckJson(field.Default, 0);
            }

            Complete(definition, null);
        }

        internal static void CheckJson(JToken value, int depth)
        {
            if (value == null || depth > 16)
            {
                throw new ArgumentException("State JSON exceeds depth 16.");
            }

            switch (value.Type)
            {
                case JTokenType.Object:
                    foreach (var property in ((JObject)value).Properties())
                    {
                        CheckJson(property.Value, depth + 1);
                    }

                    break;
                case JTokenType.Array:
                    foreach (var element in (JArray)value)
                    {
                        CheckJson(element, depth + 1);
                    }

                    break;
                case JTokenType.Float:
                    var number = (double)value;
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw new ArgumentException("State numbers must be finite.");
                    }

                    break;
                case JTokenType.Integer:
                case JTokenType.Boolean:
                case JTokenType.String:
                case JTokenType.Null:
                    break;
                default:
                    throw new ArgumentException("State must contain JSON values only.");
            }
        }

        internal static JObject Complete(PartStateDefinition definition, JObject fields, bool validate = true)
        {
            var result = new JObject();
            if (definition == null)
            {
                if (fields != null && fields.Count > 0)
                {
                    throw new ArgumentException("Part has no state schema.");
                }

                return result;
            }

            foreach (var field in definition.Fields)
            {
                result[field.Name] = field.Default.DeepClone();
            }

            foreach (var property in fields?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == property.Name)
                            ?? throw new ArgumentException("Unknown state field: " + property.Name);
                var actualType = property.Value.Type;
                var expectedType = field.Default.Type;
                if (actualType != expectedType &&
                    !(expectedType == JTokenType.Float && actualType == JTokenType.Integer))
                {
                    throw new ArgumentException("Wrong JSON type for state field: " + property.Name);
                }

                result[property.Name] = property.Value.DeepClone();
            }

            CheckJson(result, 0);
            if (Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) > MaxBytes)
            {
                throw new ArgumentException("Part state exceeds 16 KiB.");
            }

            if (validate)
            {
                definition.Validate?.Invoke((JObject)result.DeepClone());
            }

            return result;
        }

        internal static JObject Select(PartStateDefinition definition, JObject values, bool saved)
        {
            var fields = definition?.Fields ?? Array.Empty<PartStateField>();
            return new JObject(fields
                .Where(field => saved ? field.Saved : field.Replicated)
                .Select(field => new JProperty(field.Name, values[field.Name].DeepClone())));
        }

        internal static JObject Restore(PartStateDefinition definition, int version, JObject fields)
        {
            if (definition == null)
            {
                throw new ArgumentException("Saved part has no state schema.");
            }

            if (version != definition.Version)
            {
                if (version < 1 || version > definition.Version || definition.Migrate == null)
                {
                    throw new ArgumentException("Unsupported part state version: " + version);
                }

                fields = definition.Migrate(version, (JObject)fields.DeepClone())
                         ?? throw new ArgumentException("State migration returned null.");
            }

            foreach (var property in fields.Properties())
            {
                if (!definition.Fields.Any(field => field.Name == property.Name && field.Saved))
                {
                    throw new ArgumentException("Non-persistent or unknown field in save: " + property.Name);
                }
            }

            return Complete(definition, fields);
        }
    }
}
