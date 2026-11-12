using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StellarStriderLib.Core
{
    internal enum ContentKind
    {
        Part,
        Item,
        Research
    }

    /// <summary>Validates local identifiers and extracts owners from namespaced content keys.</summary>
    internal static class Keys
    {
        internal static string Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains(':') || value != value.Trim())
            {
                throw new ArgumentException("Identifiers must be nonempty, unpadded strings without ':'.");
            }

            return value;
        }

        internal static string Owner(string key)
        {
            var split = key?.Split(':');
            if (split == null || split.Length != 2)
            {
                throw new InvalidDataException("Invalid content key: " + key);
            }

            Identifier(split[0]);
            Identifier(split[1]);
            return split[0];
        }
    }

    /// <summary>Allocates stable save-owned content IDs and reads and writes their persistent mappings.</summary>
    internal sealed class WorldRegistry
    {
        internal const string FileName = "stellarstrider.registry.json";
        internal readonly SortedSet<string> Mods = new(StringComparer.Ordinal);

        private readonly Dictionary<ContentKind, SortedDictionary<string, uint>> _maps =
            Enum.GetValues(typeof(ContentKind)).Cast<ContentKind>().ToDictionary(k => k,
                k => new SortedDictionary<string, uint>(StringComparer.Ordinal));

        internal IReadOnlyDictionary<string, uint> Map(ContentKind kind) => _maps[kind];

        internal uint Resolve(ContentKind kind, string key) => _maps[kind].TryGetValue(key, out var id)
            ? id
            : throw new InvalidOperationException("Unresolved content: " + key);

        internal static uint Maximum(ContentKind kind) => kind == ContentKind.Item ? uint.MaxValue : ushort.MaxValue;

        internal void Bind(IReadOnlyDictionary<ContentKind, string[]> definitions, IEnumerable<string> owners,
            Func<ContentKind, IEnumerable<uint>> occupied)
        {
            var available = new HashSet<string>(owners, StringComparer.Ordinal);
            foreach (var kind in _maps.Keys)
            {
                var declared = new HashSet<string>(definitions[kind], StringComparer.Ordinal);
                var reserved = new HashSet<uint>(occupied(kind));
                foreach (var id in _maps[kind].Values)
                {
                    if (reserved.Contains(id))
                    {
                        throw new InvalidDataException("Cannot load save, numeric ID collision: " + kind + " " + id);
                    }
                }

                var next = _maps[kind].Count == 0 ? 10001UL : Math.Max(10001UL, (ulong)_maps[kind].Values.Max() + 1);
                foreach (var key in declared.OrderBy(k => k, StringComparer.Ordinal))
                {
                    Keys.Owner(key);
                    if (_maps[kind].ContainsKey(key))
                    {
                        continue;
                    }

                    while (next <= Maximum(kind) && reserved.Contains((uint)next))
                    {
                        next++;
                    }

                    if (next > Maximum(kind))
                    {
                        throw new InvalidDataException("No numeric IDs remain for " + kind + ".");
                    }

                    _maps[kind].Add(key, (uint)next++);
                }
            }

            Mods.UnionWith(available);
        }

        internal string Serialize()
        {
            var root = new JObject { ["format"] = 1, ["mods"] = new JArray(Mods) };
            foreach (var kind in _maps.Keys)
            {
                root[Name(kind)] = JObject.FromObject(_maps[kind]);
            }

            return root.ToString(Formatting.None);
        }

        private static string Name(ContentKind kind) =>
            kind == ContentKind.Part ? "parts" : kind == ContentKind.Item ? "items" : "research";

        internal static WorldRegistry Parse(string text)
        {
            var root = JObject.Parse(text,
                new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if ((int?)root["format"] != 1 || !(root["mods"] is JArray mods))
            {
                throw new InvalidDataException("Unsupported or incomplete StellarStriderLib registry.");
            }

            var result = new WorldRegistry();
            foreach (var mod in mods)
            {
                if (mod.Type != JTokenType.String || !result.Mods.Add(Keys.Identifier((string)mod)))
                {
                    throw new InvalidDataException("Invalid mandatory mod list.");
                }
            }

            foreach (var kind in result._maps.Keys)
            {
                if (!(root[Name(kind)] is JObject map))
                {
                    throw new InvalidDataException("Missing registry section: " + Name(kind));
                }

                var ids = new HashSet<uint>();
                foreach (var pair in map.Properties())
                {
                    if (!result.Mods.Contains(Keys.Owner(pair.Name)) || pair.Value.Type != JTokenType.Integer)
                    {
                        throw new InvalidDataException("Invalid registry entry: " + pair.Name);
                    }

                    var id = (ulong)pair.Value;
                    if (id < 10001 || id > Maximum(kind) || !ids.Add((uint)id))
                    {
                        throw new InvalidDataException("Invalid or duplicated registry ID.");
                    }

                    result._maps[kind].Add(pair.Name, (uint)id);
                }
            }

            return result;
        }

        internal void Write(string directory)
        {
            var path = Path.Combine(directory, FileName);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 1024, true))
                    {
                        writer.Write(Serialize());
                    }

                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
