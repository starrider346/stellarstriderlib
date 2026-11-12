using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model.Parts;

namespace StellarStriderLib.Runtime
{
    /// <summary>Captures persistent part fields and validates and restores their versioned save sidecar.</summary>
    internal static class PartStateSave
    {
        internal const string FileName = "stellarstriderlib.parts.json";
        [ThreadStatic] private static JObject _capture;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Saving.Services.GameSaver), "SaveGame", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(PartStateSave), nameof(Capture)),
                finalizer: new HarmonyMethod(typeof(PartStateSave), nameof(Finished)));
            harmony.Patch(AccessTools.Method(typeof(Saving.Services.IO.SaveFileWriter), "WriteGameFile"),
                postfix: new HarmonyMethod(typeof(PartStateSave), nameof(Saved)));
        }

        private static void Capture()
        {
            PartRuntime.ReconcileServer();
            _capture = Document();
        }

        private static void Finished()
        {
            _capture = null;
        }

        internal static JObject Document()
        {
            var records = new JArray();
            foreach (var pair in PartRuntime.Server)
            {
                var definition = StriderRegistry.Parts[pair.Value.Key].Definition.State;
                if (definition == null)
                {
                    continue;
                }

                records.Add(new JObject
                {
                    ["ship"] = pair.Key.ShipId, ["part"] = pair.Key.PartId, ["key"] = pair.Value.Key,
                    ["version"] = definition.Version,
                    ["fields"] = StateFields.Select(definition, pair.Value.Values, true)
                });
            }

            return new JObject { ["format"] = 1, ["parts"] = records };
        }

        private static void Saved(Saving.Services.Model.GameFileInfo __result)
        {
            if (_capture == null || __result?.SaveFileInfo?.Directory == null)
            {
                return;
            }

            var path = Path.Combine(__result.SaveFileInfo.Directory.FullName, FileName);
            var temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(file, System.Text.Encoding.UTF8, 1024, true))
                {
                    writer.Write(_capture.ToString(Formatting.None));
                }

                file.Flush(true);
            }

            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        internal static void Load(string directory, bool template)
        {
            if (template)
            {
                return;
            }

            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                return;
            }

            if (new FileInfo(path).Length > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("Part state save exceeds 64 MiB.");
            }

            Restore(JObject.Parse(File.ReadAllText(path)));
        }

        internal static void Restore(JObject root)
        {
            if ((int?)root["format"] != 1 || !(root["parts"] is JArray rows) || rows.Count > 100000)
            {
                throw new InvalidDataException("Unsupported part state save.");
            }

            var parsed = new System.Collections.Generic.Dictionary<PartContext, PartRecord>();
            foreach (var row in rows)
            {
                var key = (string)row["key"];
                if (key == null || !StriderRegistry.Parts.TryGetValue(key, out var entry))
                {
                    throw new InvalidDataException("Saved part definition is unavailable: " + key);
                }

                var id = new PartContext((uint)row["ship"], (ushort)row["part"]);
                var fields = row["fields"] as JObject ??
                             throw new InvalidDataException("Saved part fields are missing.");
                var values = StateFields.Restore(entry.Definition.State, (int)row["version"], fields);
                if (parsed.ContainsKey(id))
                {
                    throw new InvalidDataException("Duplicate saved part state.");
                }

                parsed.Add(id, new PartRecord { Key = key, Values = values });
            }

            if (parsed.Keys.Any(id => PartRuntime.Server.ContainsKey(id)))
            {
                throw new InvalidDataException("Part state was already restored.");
            }

            foreach (var pair in parsed)
            {
                PartRuntime.RestoreRecord(pair.Key, pair.Value.Key, pair.Value.Values);
            }
        }
    }
}
