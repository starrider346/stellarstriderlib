using System;
using System.Runtime.CompilerServices;
using FishNet;
using FishNet.Managing.Timing;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using HarmonyLib;
using MelonLoader;
using StellarStriderLib.Core;
using UnityEngine;
using WorldTracking.Services;
using WorldTracking.Services.Model;

namespace StellarStriderLib.Runtime
{
    /// <summary>Freezes gameplay while transport, visual loading, and the unscaled loading UI continue.</summary>
    internal static class LoadingSimulation
    {
        private sealed class WorldClock { internal double PreviousSpeed; }
        private static readonly LoadingPause Pause = new();
        private static readonly ConditionalWeakTable<WorldTimeServerTracker, WorldClock> WorldClocks = new();
        private static TimeManager _physics;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(WorldTimeServerTracker), "UpdateFixedTime"),
                prefix: new HarmonyMethod(typeof(LoadingSimulation), nameof(ServerTime)),
                postfix: new HarmonyMethod(typeof(LoadingSimulation), nameof(ServerTimeResumed)));
            harmony.Patch(AccessTools.Method(typeof(WorldTimeClientTracker), "UpdateVisualTime"),
                prefix: new HarmonyMethod(typeof(LoadingSimulation), nameof(ClientInterpolationTime)));
            harmony.Patch(AccessTools.Method(typeof(WorldTimeClientTracker), "TickToWorldTime"),
                postfix: new HarmonyMethod(typeof(LoadingSimulation), nameof(PausedTickTime)));
        }

        internal static void Begin()
        {
            if (!Pause.Active) Pause.Capture(() => Time.timeScale, value => Time.timeScale = value);
            Hold();
        }

        internal static void Hold()
        {
            if (!Pause.Active) return;
            var physics = InstanceFinder.TimeManager;
            if (physics != null && physics != _physics)
            {
                Pause.Capture(physics.GetPhysicsTimeScale, value =>
                {
                    if (physics != null) physics.SetPhysicsTimeScale(value);
                });
                _physics = physics;
            }
            Pause.Hold();
        }

        internal static void End()
        {
            try { Pause.Restore(); }
            catch (Exception error) { MelonLogger.Warning("Could not fully restore loading simulation: " + error); }
            finally { _physics = null; }
        }

        private static bool ServerTime(WorldTimeServerTracker __instance, uint __0,
            SyncVar<WorldTimeTick> ____serverTimeTick, WorldTimeClientTracker ___timeClientTracker,
            ref double ____timeSinceLastSync, float ___timeSyncInterval, out bool __state)
        {
            __state = false;
            var tick = ____serverTimeTick.Value;
            if (!Pause.Active)
            {
                if (WorldClocks.TryGetValue(__instance, out var clock))
                {
                    tick.TimeSpeed = clock.PreviousSpeed;
                    ____serverTimeTick.Value = tick;
                    WorldClocks.Remove(__instance);
                    __state = true;
                }
                return true;
            }

            bool first = !WorldClocks.TryGetValue(__instance, out _);
            if (first) WorldClocks.Add(__instance, new WorldClock { PreviousSpeed = tick.TimeSpeed });
            tick.Tick = __0;
            tick.TimeSpeed = 0;
            ____serverTimeTick.Value = tick;
            ____timeSinceLastSync += __instance.TimeManager.TickDelta;
            if (first || ____timeSinceLastSync >= Math.Max(.1, ___timeSyncInterval))
            {
                ____timeSinceLastSync = 0;
                if (___timeClientTracker != null) ___timeClientTracker.SyncTime_AllClientRPC(tick, Channel.Unreliable);
            }
            return false;
        }

        private static void ServerTimeResumed(WorldTimeServerTracker __instance, bool __state,
            WorldTimeClientTracker ___timeClientTracker)
        {
            if (__state && ___timeClientTracker != null)
                ___timeClientTracker.SyncTime_AllClientRPC(__instance.CurrentWorldTimeTick, Channel.Unreliable);
        }

        private static void ClientInterpolationTime(ref double ____clientTime)
        {
            if (Pause.Active) ____clientTime += Time.unscaledDeltaTime - Time.deltaTime;
        }

        private static void PausedTickTime(WorldTimeTick ____lastReceivedTick, ref double __result)
        {
            if (____lastReceivedTick.TimeSpeed == 0) __result = ____lastReceivedTick.WorldTime;
        }
    }
}
