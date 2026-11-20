using System;
using System.Collections.Generic;
using System.Linq;
using Core.Services;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.State;
using Ships.Services;

namespace StellarStriderLib.Runtime
{
    /// <summary>Stores a part’s content key, JSON field values and authoritative revision.</summary>
    internal sealed class PartRecord
    {
        internal string Key;
        internal JObject Values;
        internal ulong Revision;
    }

    /// <summary>Tracks server and client part instances, dispatches callbacks and enforces state authority.</summary>
    internal static class PartRuntime
    {
        internal static readonly Dictionary<PartContext, PartRecord> Server = new();
        internal static readonly Dictionary<PartContext, PartRecord> Client = new();

        private static readonly Dictionary<(bool, PartContext), PartInstance> Instances =
            new();

        private static readonly Dictionary<ushort, (string Key, PartDefinition Definition)> Definitions =
            new();

        private static readonly HashSet<PartInstance> Dirty = new();
        private static readonly HashSet<(PartInstance, string)> Failed = new();
        private static ulong _revision;

        internal static bool TryDefinition(ushort id, out string key, out PartDefinition definition)
        {
            key = null;
            definition = null;
            if (!Definitions.TryGetValue(id, out var entry))
            {
                return false;
            }

            key = entry.Key;
            definition = entry.Definition;
            return true;
        }

        internal static void Bind()
        {
            Definitions.Clear();
            foreach (var entry in ContentSession.Parts)
            {
                Definitions.Add((ushort)ContentSession.Registry.Resolve(Core.ContentKind.Part, entry.Key),
                    (entry.Key, entry.Definition));
            }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TrackedShipServer), "AddStatefulPart"),
                postfix: new HarmonyMethod(typeof(PartRuntime), nameof(ServerAdded)));
            harmony.Patch(AccessTools.Method(typeof(TrackedShipServer), "UpdateParts"),
                prefix: new HarmonyMethod(typeof(PartRuntime), nameof(ServerBefore)),
                postfix: new HarmonyMethod(typeof(PartRuntime), nameof(ServerAfter)));
            harmony.Patch(AccessTools.Method(typeof(TrackedShipClient), "UpdateVisuals"),
                postfix: new HarmonyMethod(typeof(PartRuntime), nameof(ClientFrame)));
            harmony.Patch(AccessTools.Method(typeof(TrackedShipClient), "Despawn"),
                prefix: new HarmonyMethod(typeof(PartRuntime), nameof(ClientGone)));
            harmony.Patch(AccessTools.Method(typeof(ShipState), "SetPartState"),
                prefix: new HarmonyMethod(typeof(PartRuntime), nameof(BeforeNative)),
                postfix: new HarmonyMethod(typeof(PartRuntime), nameof(AfterNative)));
            harmony.Patch(AccessTools.Method(typeof(ShipState), "RemoveStatefulPart"),
                prefix: new HarmonyMethod(typeof(PartRuntime), nameof(Removing)));
        }

        internal static PartInstance Observe(bool server, IShipStateRead ship, StatefulPart part)
        {
            if (!TryDefinition(part.Settings.id, out var key, out var definition) ||
                (definition.Callbacks == null && definition.State == null))
            {
                return null;
            }

            var id = new PartContext(ship.Id, part.Id);
            var index = (server, id);
            if (Instances.TryGetValue(index, out var old))
            {
                if (old.ContentKey == key && ReferenceEquals(old.Ship, ship))
                {
                    old.Part = part;
                    return old;
                }

                Drop(old);
            }

            var records = server ? Server : Client;
            if (!records.TryGetValue(id, out var record) || record.Key != key)
            {
                record = new PartRecord
                {
                    Key = key, Values = StateFields.Complete(definition.State, null),
                    Revision = server ? ++_revision : 0
                };
                records[id] = record;
            }

            var instance = new PartInstance(id, key, server, ship, part, definition)
            {
                RemovedState = (JObject)record.Values.DeepClone(),
                NativeState = (ship as TrackedShip)?.State
            };
            Instances.Add(index, instance);
            Safe(instance, "spawn", () => definition.Callbacks?.Spawned?.Invoke(instance));
            return instance;
        }

        private static void Sync(bool server, TrackedShip ship)
        {
            var alive = new HashSet<PartContext>();
            foreach (var part in ship.StatefulParts)
            {
                var instance = Observe(server, ship, part);
                if (instance != null)
                {
                    alive.Add(instance.Id);
                }
            }

            foreach (var instance in Instances.Values
                         .Where(instance =>
                             instance.IsServer == server && instance.Id.ShipId == ship.Id &&
                             !alive.Contains(instance.Id))
                         .ToArray())
            {
                Drop(instance);
            }

            if (!server)
            {
                return;
            }

            foreach (var id in Server.Keys.Where(id => id.ShipId == ship.Id && !alive.Contains(id)).ToArray())
            {
                Server.Remove(id);
            }
        }

        private static void ServerAdded(TrackedShipServer __instance, ushort __result)
        {
            if (__instance.TryGetStatefulPart(__result, out var part))
            {
                Observe(true, __instance, part);
            }
        }

        private static void ServerBefore(TrackedShipServer __instance) => Sync(true, __instance);

        private static void ServerAfter(TrackedShipServer __instance, float __0)
        {
            foreach (var instance in Instances.Values
                         .Where(instance => instance.IsServer && instance.Id.ShipId == __instance.Id).ToArray())
            {
                Safe(instance, "server tick", () => instance.Definition.Callbacks?.ServerTick?.Invoke(instance, __0));
            }

            NotifyDirty(true);
        }

        private static void ClientFrame(TrackedShipClient __instance)
        {
            if (!__instance.IsVisualLoaded)
            {
                return;
            }

            Sync(false, __instance);
            foreach (var instance in Instances.Values
                         .Where(instance => !instance.IsServer && instance.Id.ShipId == __instance.Id).ToArray())
            {
                Safe(instance, "client update",
                    () => instance.Definition.Callbacks?.ClientUpdate?.Invoke(instance, UnityEngine.Time.deltaTime));
            }

            NotifyDirty(false);
        }

        private static void ClientGone(TrackedShipClient __instance)
        {
            foreach (var instance in Instances.Values
                         .Where(instance => !instance.IsServer && instance.Id.ShipId == __instance.Id).ToArray())
            {
                Drop(instance);
            }
        }

        private static void BeforeNative(ShipState __instance, ushort __0,
            out Ships.Interface.Model.Parts.StateTypes.IStatefulPartState __state) =>
            __state = __instance.GetPartState(__0);

        private static void AfterNative(ShipState __instance, ushort __0,
            Ships.Interface.Model.Parts.StateTypes.IStatefulPartState __state)
        {
            foreach (var instance in Instances.Values
                         .Where(instance =>
                             ReferenceEquals(instance.NativeState, __instance) && instance.Id.PartId == __0).ToArray())
            {
                var change = new StatefulPartStateChangeEvent(instance.Part, __state, instance.Part.State);
                Safe(instance, "native state",
                    () => instance.Definition.Callbacks?.NativeStateChanged?.Invoke(instance, change));
            }
        }

        private static void Removing(ShipState __instance, ushort __0)
        {
            foreach (var instance in Instances.Values
                         .Where(instance =>
                             ReferenceEquals(instance.NativeState, __instance) && instance.Id.PartId == __0).ToArray())
            {
                if (!instance.IsServer)
                {
                    Client.Remove(instance.Id);
                }

                Drop(instance);
            }
        }

        private static void Drop(PartInstance instance)
        {
            var records = instance.IsServer ? Server : Client;
            if (records.TryGetValue(instance.Id, out var last))
            {
                instance.RemovedState = (JObject)last.Values.DeepClone();
            }

            Instances.Remove((instance.IsServer, instance.Id));
            instance.Active = false;
            Dirty.Remove(instance);
            Safe(instance, "remove", () => instance.Definition.Callbacks?.Removed?.Invoke(instance));
            if (instance.IsServer)
            {
                Server.Remove(instance.Id);
            }

            Failed.RemoveWhere(pair => ReferenceEquals(pair.Item1, instance));
        }

        private static void Safe(PartInstance instance, string phase, Action call)
        {
            if (Failed.Contains((instance, phase)))
            {
                return;
            }

            try
            {
                call();
            }
            catch (Exception error)
            {
                Failed.Add((instance, phase));
                MelonLoader.MelonLogger.Error("Part callback disabled: " + instance.ContentKey + " " + phase + ": " +
                                              error);
            }
        }

        private static void NotifyDirty(bool server)
        {
            foreach (var instance in Dirty.Where(instance => instance.IsServer == server).ToArray())
            {
                Dirty.Remove(instance);
                if (instance.Active)
                {
                    Safe(instance, "state changed",
                        () => instance.Definition.Callbacks?.StateChanged?.Invoke(instance));
                }
            }
        }

        internal static JObject Snapshot(PartInstance instance)
        {
            StriderRegistry.MainThread();
            if (!instance.Active)
            {
                return (JObject)(instance.RemovedState?.DeepClone() ??
                                 throw new InvalidOperationException("Part instance is no longer available."));
            }

            var values = instance.IsServer ? Server : Client;
            if (!values.TryGetValue(instance.Id, out var record) || record.Key != instance.ContentKey)
            {
                return (JObject)(instance.RemovedState?.DeepClone() ??
                                 throw new InvalidOperationException("Part instance is no longer available."));
            }

            return (JObject)record.Values.DeepClone();
        }

        internal static JToken Read(PartInstance instance, string field) => Snapshot(instance)[field]?.DeepClone() ??
                                                                            throw new ArgumentException(
                                                                                "Unknown state field: " + field);

        internal static void Write(PartInstance instance, JObject fields)
        {
            StriderRegistry.MainThread();
            if (!instance.Active || !instance.IsServer)
            {
                throw new InvalidOperationException("Only a live server part may change state.");
            }

            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            var record = Server[instance.Id];
            var proposed = (JObject)record.Values.DeepClone();
            foreach (var property in fields.Properties())
            {
                proposed[property.Name] = property.Value.DeepClone();
            }

            var validated = StateFields.Complete(instance.Definition.State, proposed);
            if (JToken.DeepEquals(validated, record.Values))
            {
                return;
            }

            record.Values = validated;
            record.Revision = ++_revision;
            instance.RemovedState = (JObject)validated.DeepClone();
            Dirty.Add(instance);
        }

        internal static void ApplyReplica(PartContext id, string key, ulong revision, JObject fields)
        {
            if (Client.TryGetValue(id, out var old) && old.Revision >= revision)
            {
                return;
            }

            if (!StriderRegistry.Parts.TryGetValue(key, out var entry))
            {
                throw new ArgumentException("Unknown replicated part: " + key);
            }

            foreach (var property in fields.Properties())
            {
                if (!entry.Definition.State.Fields.Any(f => f.Name == property.Name && f.Replicated))
                {
                    throw new ArgumentException("Non-replicated field received.");
                }
            }

            var value = StateFields.Complete(entry.Definition.State, fields, false);
            Client[id] = new PartRecord { Key = key, Revision = revision, Values = value };
            if (!Instances.TryGetValue((false, id), out var instance) || instance.ContentKey != key)
            {
                return;
            }

            instance.RemovedState = (JObject)value.DeepClone();
            Dirty.Add(instance);
        }

        internal static void ReconcileServer()
        {
            if (!Definitions.Values.Any(entry => entry.Definition.Callbacks != null || entry.Definition.State != null))
            {
                return;
            }

            var ships = ServiceLocator.GetService<ShipsServerTracker>();
            if (ships == null)
            {
                return;
            }

            var alive = new HashSet<uint>();
            foreach (var ship in ships.Ships)
            {
                alive.Add(ship.Id);
                Sync(true, ship);
            }

            foreach (var instance in Instances.Values
                         .Where(instance => instance.IsServer && !alive.Contains(instance.Id.ShipId)).ToArray())
            {
                Drop(instance);
            }

            foreach (var id in Server.Keys.Where(id => !alive.Contains(id.ShipId)).ToArray())
            {
                Server.Remove(id);
            }
        }

        internal static void Reset()
        {
            foreach (var instance in Instances.Values.ToArray())
            {
                Drop(instance);
            }

            Instances.Clear();
            Server.Clear();
            Client.Clear();
            Definitions.Clear();
            Dirty.Clear();
            Failed.Clear();
            _revision = 0;
        }

        internal static void RestoreRecord(PartContext id, string key, JObject values)
        {
            if (Server.ContainsKey(id))
            {
                throw new ArgumentException("Duplicate saved part state.");
            }

            Server.Add(id, new PartRecord { Key = key, Values = values, Revision = ++_revision });
        }
    }
}
