using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Core.Services;
using FishNet;
using FishNet.Managing.Timing;
using HarmonyLib;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Services;
using StellarStriderLib.Core;
using static StellarStriderLib.Core.ThermalModel;

namespace StellarStriderLib.Runtime
{
    internal static class ThermalRuntime
    {
        private sealed class Topology
        {
            internal int Version;
        }

        private sealed class WarmShip
        {
            internal readonly TrackedShipServer Ship;
            internal readonly Dictionary<ThermalPartKey, ThermalReading> Readings = new();
            internal ThermalGraph Graph;
            internal int BuiltVersion = -1;

            internal WarmShip(TrackedShipServer ship) => Ship = ship;

            internal void Put(ThermalPart part, float temperature)
            {
                if (temperature == Ambient) Readings.Remove(part.Key);
                else Readings[part.Key] = new ThermalReading(part, temperature);
            }
        }

        private static ConditionalWeakTable<ShipState, Topology> _topology = new();
        private static readonly Dictionary<uint, WarmShip> WarmShips = new();
        private static Dictionary<ThermalPartKey, ThermalReading> _restoring;
        private static TimeManager _clock;
        private static double _elapsed;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            void After(string nativeMethod, string callback) =>
                harmony.Patch(AccessTools.DeclaredMethod(typeof(ShipState), nativeMethod),
                    postfix: new HarmonyMethod(typeof(ThermalRuntime), callback));
            void Before(string nativeMethod, string callback) =>
                harmony.Patch(AccessTools.DeclaredMethod(typeof(ShipState), nativeMethod),
                    prefix: new HarmonyMethod(typeof(ThermalRuntime), callback));

            After(nameof(ShipState.AddHullPart), nameof(TopologyChanged));
            After(nameof(ShipState.UpdateHullPart), nameof(TopologyChanged));
            After(nameof(ShipState.AddStatefulPart), nameof(TopologyChanged));
            After(nameof(ShipState.AddStatefulPartWithoutInit), nameof(TopologyChanged));
            Before(nameof(ShipState.RemoveHullPart), nameof(RemovingHull));
            Before(nameof(ShipState.RemoveStatefulPart), nameof(RemovingStateful));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ShipState), nameof(ShipState.SetPartState)),
                prefix: new HarmonyMethod(typeof(ThermalRuntime), nameof(BeforeStateChange)),
                postfix: new HarmonyMethod(typeof(ThermalRuntime), nameof(AfterStateChange)));
        }

        private static void TopologyChanged(ShipState __instance) => _topology.GetValue(__instance, _ => new Topology()).Version++;

        internal static int TopologyVersion(ShipState state) =>
            _topology.TryGetValue(state, out var topology) ? topology.Version : 0;

        private static void BeforeStateChange(ShipState __instance, ushort __0, out bool? __state)
        {
            __state = null;
            if (WarmShips.TryGetValue(__instance.Id, out var warm) && ReferenceEquals(warm.Ship.State, __instance))
                __state = (__instance.GetPartState(__0) as IDoorState)?.IsOpen;
        }

        private static void AfterStateChange(ShipState __instance, ushort __0, bool? __state)
        {
            if (WarmShips.TryGetValue(__instance.Id, out var warm) && ReferenceEquals(warm.Ship.State, __instance) &&
                __instance.TryGetPart(__0, out var part) &&
                (part.State is IDoorState door ? __state != door.IsOpen : ThermalGeometry.HasDynamicCollider(part.Settings)))
                TopologyChanged(__instance);
        }

        private static void RemovingHull(ShipState __instance, HullPartPosition __0) =>
            Removing(__instance, ThermalPartKey.Hull(__instance.Id, __0));

        private static void RemovingStateful(ShipState __instance, ushort __0) =>
            Removing(__instance, ThermalPartKey.Stateful(__instance.Id, __0));

        private static void Removing(ShipState state, ThermalPartKey key)
        {
            TopologyChanged(state);
            if (WarmShips.TryGetValue(key.ShipId, out var warm) && ReferenceEquals(warm.Ship.State, state))
                warm.Readings.Remove(key);

            if (ServiceLocator.GetService<ShipsClientTracker>()?.TryGetTrackedShip(key.ShipId, out var client) == true &&
                ReferenceEquals(client.State, state))
            {
                ThermalNetwork.RemoveReplica(key);
                ThermalVisuals.RemovePart(client, key);
            }
        }

        internal static bool TryFind(ThermalPartKey key, bool server, out TrackedShip ship, out ThermalPart part)
        {
            ship = null;
            part = default;
            if (!ContentSession.Ready) return false;
            if (server)
            {
                if (InstanceFinder.NetworkManager?.IsServerStarted != true ||
                    ServiceLocator.GetService<ShipsServerTracker>()?.TryGetTrackedShip(key.ShipId, out var source) != true ||
                    source.IsShipDestroyed)
                    return false;
                ship = source;
            }
            else
            {
                if (InstanceFinder.NetworkManager?.IsClientStarted != true ||
                    ServiceLocator.GetService<ShipsClientTracker>()?.TryGetTrackedShip(key.ShipId, out var source) != true)
                    return false;
                ship = source;
            }

            return ThermalPart.TryFind(ship, key, out part);
        }

        internal static bool TryRead(ThermalPartKey key, out float kelvin)
        {
            StriderRegistry.MainThread();
            kelvin = default;
            if (!ContentSession.Ready) return false;
            var server = InstanceFinder.NetworkManager?.IsServerStarted == true;
            if (!TryFind(key, server, out var ship, out var part)) return false;
            kelvin = Read(ship, part, server);
            return true;
        }

        internal static float Read(TrackedShip ship, ThermalPart part, bool server)
        {
            if (server)
            {
                if (WarmShips.TryGetValue(ship.Id, out var warm) &&
                    (ship is TrackedShipClient || ReferenceEquals(warm.Ship, ship)) &&
                    warm.Readings.TryGetValue(part.Key, out var reading) && reading.Matches(part))
                    return reading.Temperature;
                if (_restoring != null && _restoring.TryGetValue(part.Key, out var pending) && pending.Matches(part))
                    return pending.Temperature;
                return Ambient;
            }

            return ThermalNetwork.TryRead(part, out var temperature) ? temperature : Ambient;
        }

        internal static bool TryWrite(ThermalPartKey key, float kelvin)
        {
            StriderRegistry.MainThread();
            if (!ValidTemperature(kelvin) || !TryFind(key, true, out var ship, out var part) ||
                HasFailed(Read(ship, part, true), part.Profile))
                return false;

            _restoring?.Remove(part.Key);
            if (kelvin != Ambient || WarmShips.ContainsKey(ship.Id))
                GetWarmShip((TrackedShipServer)ship).Put(part, kelvin);
            return true;
        }

        private static WarmShip GetWarmShip(TrackedShipServer ship)
        {
            if (!WarmShips.TryGetValue(ship.Id, out var warm) || !ReferenceEquals(warm.Ship, ship))
                WarmShips[ship.Id] = warm = new WarmShip(ship);
            return warm;
        }

        internal static void UpdateClock()
        {
            var manager = ContentSession.Ready ? InstanceFinder.NetworkManager : null;
            SetClock(manager != null && manager.IsServerStarted ? manager.TimeManager : null);
        }

        private static void SetClock(TimeManager clock)
        {
            if (ReferenceEquals(_clock, clock)) return;
            if (!ReferenceEquals(_clock, null)) _clock.OnPostPhysicsSimulation -= AfterPhysics;
            _clock = clock;
            _elapsed = 0;
            if (_clock != null) _clock.OnPostPhysicsSimulation += AfterPhysics;
        }

        private static void AfterPhysics(float delta)
        {
            if (!ContentSession.Ready || LoadingSimulation.IsPaused || delta <= 0 ||
                InstanceFinder.PredictionManager?.IsReconciling == true ||
                (WarmShips.Count == 0 && _restoring == null))
                return;

            _elapsed += delta;
            while (_elapsed >= StepSeconds)
            {
                _elapsed -= StepSeconds;
                Step();
            }
        }

        private static void Step()
        {
            var ships = ServiceLocator.GetService<ShipsServerTracker>();
            if (ships == null) return;
            RestorePending();
            foreach (var warm in WarmShips.Values.ToArray())
            {
                if (warm.Readings.Count == 0 || warm.Ship.IsShipDestroyed ||
                    !ships.TryGetTrackedShip(warm.Ship.Id, out var tracked) || !ReferenceEquals(tracked, warm.Ship))
                {
                    WarmShips.Remove(warm.Ship.Id);
                    continue;
                }

                StepShip(warm);
                if (warm.Readings.Count == 0) WarmShips.Remove(warm.Ship.Id);
            }
        }

        private static void StepShip(WarmShip warm)
        {
            // A direct write at the limit is latched until the next simulation step.
            foreach (var pair in warm.Readings.ToArray())
            {
                if (!ThermalPart.TryFind(warm.Ship, pair.Key, out var part) || !pair.Value.Matches(part))
                    warm.Readings.Remove(pair.Key);
                else if (HasFailed(pair.Value.Temperature, part.Profile))
                    Destroy(warm, part);
            }

            if (warm.Readings.Count == 0 || warm.Ship.IsShipDestroyed) return;
            var version = TopologyVersion(warm.Ship.State);
            if (warm.Graph == null || warm.BuiltVersion != version)
            {
                warm.Graph = ThermalGraph.Build(warm.Ship);
                warm.BuiltVersion = version;
            }

            var graph = warm.Graph;
            for (var i = 0; i < graph.Parts.Length; i++)
                graph.Temperatures[i] = warm.Readings.TryGetValue(graph.Parts[i].Key, out var reading)
                    ? reading.Temperature : Ambient;

            Exchange(graph.Temperatures, graph.Contacts, graph.Rates, graph.Changes);
            for (var i = 0; i < graph.Parts.Length; i++)
            {
                if (!HasFailed(graph.Temperatures[i], graph.Profiles[i]))
                    warm.Put(graph.Parts[i], Cool(graph.Temperatures[i]));
            }
            for (var i = 0; i < graph.Parts.Length; i++)
            {
                var part = graph.Parts[i];
                if (HasFailed(graph.Temperatures[i], graph.Profiles[i]))
                {
                    // Earlier destruction in this step can remove another queued part.
                    if (ThermalPart.TryFind(warm.Ship, part.Key, out var live) && live.Settings == part.Settings &&
                        ReferenceEquals(live.Stateful, part.Stateful))
                        Destroy(warm, live);
                }
            }
        }

        private static void Destroy(WarmShip warm, ThermalPart part)
        {
            if (part.Key.IsHull) warm.Ship.DestroyHullPart(part.Key.HullPosition);
            else warm.Ship.DestroyStatefulPart(part.Key.PartId);
            warm.Readings.Remove(part.Key);
        }

        internal static IEnumerable<KeyValuePair<ThermalPartKey, ThermalReading>> Readings()
        {
            var ships = ServiceLocator.GetService<ShipsServerTracker>();
            if (ships == null) yield break;
            foreach (var warm in WarmShips.Values)
            {
                if (warm.Ship.IsShipDestroyed || !ships.TryGetTrackedShip(warm.Ship.Id, out var live) ||
                    !ReferenceEquals(live, warm.Ship))
                    continue;
                foreach (var pair in warm.Readings)
                    if (ThermalPart.TryFind(live, pair.Key, out var part) && pair.Value.Matches(part))
                        yield return pair;
            }
        }

        internal static void SetRestore(Dictionary<ThermalPartKey, ThermalReading> readings) => _restoring = readings;

        internal static IEnumerable<ThermalPartKey> VisualKeys(uint shipId) =>
            WarmShips.TryGetValue(shipId, out var warm) ? warm.Readings.Keys : Array.Empty<ThermalPartKey>();

        internal static void RestorePending()
        {
            if (_restoring == null || ServiceLocator.GetService<ShipsServerTracker>() == null) return;
            foreach (var pair in _restoring)
                if (TryFind(pair.Key, true, out var ship, out var part) && pair.Value.Matches(part))
                    GetWarmShip((TrackedShipServer)ship).Put(part, pair.Value.Temperature);
            _restoring = null;
        }

        internal static void Reset()
        {
            SetClock(null);
            _topology = new ConditionalWeakTable<ShipState, Topology>();
            WarmShips.Clear();
            _restoring = null;
            ThermalGeometry.Reset();
            ThermalNetwork.Reset();
            ThermalVisuals.Reset();
        }
    }
}
