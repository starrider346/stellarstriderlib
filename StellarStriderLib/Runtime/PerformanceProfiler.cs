using System;
using FishNet;
using FishNet.Managing.Timing;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StellarStriderLib.Runtime
{
    internal static class PerformanceProfiler
    {
        private const double SampleInterval = 1;
        private const string CpuLabel = "CPU main thread   ";
        private const string SimulationLabel = "TPS       ";
        private static bool _visible;
        private static ProfilerRecorder _mainThread;
        private static double _cpuWindowStart;
        private static long _cpuTotalNanoseconds, _cpuPeakNanoseconds;
        private static int _cpuSamples;
        private static string _cpuText;
        private static TimeManager _simulationClock;
        private static double _simulationWindowStart;
        private static int _simulationTicks;
        private static string _simulationText;

        internal static void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame &&
                keyboard.ctrlKey.isPressed && keyboard.shiftKey.isPressed)
            {
                if (_visible) Close();
                else Open();
            }
            if (!_visible) return;

            var now = Time.realtimeSinceStartupAsDouble;
            SampleCpu(now);
            SampleSimulation(now);
        }

        private static void Open()
        {
            try
            {
                _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            }
            catch (ArgumentException)
            {
                _mainThread = default;
            }
            _cpuText = CpuLabel + (_mainThread.Valid ? "Sampling…" : "Unavailable");
            _cpuWindowStart = Time.realtimeSinceStartupAsDouble;
            _cpuTotalNanoseconds = 0;
            _cpuPeakNanoseconds = 0;
            _cpuSamples = 0;
            _simulationText = SimulationLabel + "No active world";
            _visible = true;
        }

        internal static void Close()
        {
            _visible = false;
            _mainThread.Dispose();
            SetSimulationClock(null, 0);
        }

        private static void SampleCpu(double now)
        {
            if (!_mainThread.Valid) return;
            if (_mainThread.Count > 0)
            {
                var nanoseconds = _mainThread.LastValue;
                _cpuTotalNanoseconds += nanoseconds;
                _cpuPeakNanoseconds = Math.Max(_cpuPeakNanoseconds, nanoseconds);
                _cpuSamples++;
            }

            if (now - _cpuWindowStart < SampleInterval || _cpuSamples == 0) return;
            var averageMs = _cpuTotalNanoseconds * 1e-6 / _cpuSamples;
            var peakMs = _cpuPeakNanoseconds * 1e-6;
            _cpuText = $"{CpuLabel}{averageMs:F2} ms avg / {peakMs:F2} ms peak";
            _cpuWindowStart = now;
            _cpuTotalNanoseconds = 0;
            _cpuPeakNanoseconds = 0;
            _cpuSamples = 0;
        }

        private static void SampleSimulation(double now)
        {
            var network = ContentSession.Ready ? InstanceFinder.NetworkManager : null;
            var clock = network != null && (network.IsServerStarted || network.IsClientStarted)
                ? network.TimeManager : null;
            if (!ReferenceEquals(clock, _simulationClock)) SetSimulationClock(clock, now);

            var elapsed = now - _simulationWindowStart;
            if (_simulationClock == null || elapsed < SampleInterval) return;
            _simulationText = $"{SimulationLabel}{_simulationTicks / elapsed:F1}";
            _simulationWindowStart = now;
            _simulationTicks = 0;
        }

        private static void SetSimulationClock(TimeManager clock, double now)
        {
            if (!ReferenceEquals(_simulationClock, null))
                _simulationClock.OnPostPhysicsSimulation -= CountSimulationTick;

            _simulationClock = clock;
            _simulationWindowStart = now;
            _simulationTicks = 0;
            _simulationText = SimulationLabel + "No active world";
            if (_simulationClock == null) return;
            _simulationClock.OnPostPhysicsSimulation += CountSimulationTick;
            _simulationText = SimulationLabel + "Sampling…";
        }

        private static void CountSimulationTick(float delta)
        {
            if (delta > 0 && InstanceFinder.PredictionManager?.IsReconciling != true)
                _simulationTicks++;
        }

        internal static void Draw()
        {
            if (!_visible || Event.current.type != EventType.Repaint) return;
            const float margin = 12;
            var width = Mathf.Min(338, Screen.width - 2 * margin);
            var left = Screen.width - width - margin;
            GUI.Box(new Rect(left, margin, width, 76), "SSLib Performance Profiler");
            GUI.Label(new Rect(left + 10, margin + 25, width - 20, 20), _cpuText);
            GUI.Label(new Rect(left + 10, margin + 47, width - 20, 20), _simulationText);
        }
    }
}
