using System;
using System.Collections.Generic;
using MelonLoader;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model.Parts;

namespace StellarStriderLib.Showcase
{
    internal static class ConsoleState
    {
        internal static readonly Dictionary<PartContext, double> DisplayedSeconds = new();

        internal static PartStateDefinition CreateDefinition()
        {
            return new PartStateDefinition
            {
                Version = 2,
                Fields = new[]
                {
                    new PartStateField { Name = "elapsed", Default = new JValue(0d) },
                    new PartStateField { Name = "sessionTicks", Default = new JValue(0L), Saved = false },
                    new PartStateField { Name = "privateCounter", Default = new JValue(0L), Replicated = false },
                    new PartStateField { Name = "enabled", Default = new JValue(true) },
                    new PartStateField { Name = "label", Default = new JValue("Showcase timer") },
                    new PartStateField { Name = "metadata", Default = new JObject { ["units"] = "seconds" } },
                    new PartStateField { Name = "samples", Default = new JArray(), Saved = false, Replicated = false }
                },
                Migrate = Migrate,
                Validate = Validate
            };
        }

        private static JObject Migrate(int oldVersion, JObject oldSavedFields)
        {
            if (oldVersion != 1)
            {
                throw new ArgumentException("Unsupported showcase state version: " + oldVersion);
            }

            return new JObject { ["elapsed"] = (double?)oldSavedFields["seconds"] ?? 0d };
        }

        private static void Validate(JObject values)
        {
            if ((double)values["elapsed"] < 0 || (long)values["sessionTicks"] < 0 ||
                (long)values["privateCounter"] < 0)
            {
                throw new ArgumentException("Showcase timer values cannot be negative.");
            }
        }

        internal static PartCallbacks CreateCallbacks()
        {
            return new PartCallbacks
            {
                Spawned = OnSpawned,
                Removed = OnRemoved,
                ServerTick = OnServerTick,
                ClientUpdate = (part, _) => RefreshDisplay(part),
                NativeStateChanged = (part, _) =>
                    MelonLogger.Msg(
                        $"Showcase native Constant changed on {(part.IsServer ? "server" : "client")}: {part.Id}"),
                StateChanged = RefreshDisplay
            };
        }

        private static void OnSpawned(PartInstance part)
        {
            MelonLogger.Msg($"Showcase console spawned on {(part.IsServer ? "server" : "client")}: {part.Id}");
            RefreshDisplay(part);
        }

        private static void OnServerTick(PartInstance part, float deltaTime)
        {
            if (!part.State.Get<bool>("enabled"))
            {
                return;
            }

            part.State.Set(new JObject
            {
                ["elapsed"] = part.State.Get<double>("elapsed") + deltaTime,
                ["sessionTicks"] = part.State.Get<long>("sessionTicks") + 1,
                ["privateCounter"] = part.State.Get<long>("privateCounter") + 1
            });

            if (part.State.Get<long>("sessionTicks") % 60 != 0)
            {
                return;
            }

            var snapshot = part.State.Snapshot();
            var samples = (JArray)snapshot["samples"];
            samples.Add((double)snapshot["elapsed"]);
            if (samples.Count > 4)
            {
                samples.RemoveAt(0);
            }

            part.State.Set("samples", samples);
        }

        private static void RefreshDisplay(PartInstance part)
        {
            if (part.IsServer || !part.IsActive)
            {
                return;
            }

            DisplayedSeconds[part.Id] = part.State.Get<double>("elapsed");
        }

        private static void OnRemoved(PartInstance part)
        {
            if (!part.IsServer)
            {
                DisplayedSeconds.Remove(part.Id);
            }

            MelonLogger.Msg($"Showcase console removed: {part.Id}, elapsed={part.State.Get<double>("elapsed"):F1}s");
        }

        internal static void ClearDisplay() => DisplayedSeconds.Clear();
    }
}
