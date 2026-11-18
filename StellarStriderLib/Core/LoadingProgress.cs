using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StellarStriderLib.Core
{
    internal sealed class LoadingProgress
    {
        internal const double FadeSeconds = 0.35;
        private double _started;
        private int _readyFrame;
        private double _nativeValue, _totalWeight;
        private Step[] _steps = Array.Empty<Step>();
        private LoadingStepContext _native;
        private CancellationTokenSource _cancellation;
        private bool _callbacksStarted;
        internal bool Active { get; private set; }
        internal bool Ready { get; private set; }
        internal bool Complete { get; private set; }
        internal string StepName { get; private set; }
        internal Exception CancellationError { get; private set; }
        internal double Value { get; private set; }
        internal double Opacity { get; private set; }
        internal bool FadingOut { get; private set; }

        internal void Begin(double now)
            => Begin(now, Array.Empty<KeyValuePair<string, LoadingStepDefinition>>());

        internal void Begin(double now, IEnumerable<KeyValuePair<string, LoadingStepDefinition>> definitions)
        {
            Cancel();
            var snapshots = definitions.Select(pair => new KeyValuePair<string, LoadingStepDefinition>(
                pair.Key, pair.Value.Snapshot())).ToArray();
            _cancellation = new CancellationTokenSource();
            _native = new LoadingStepContext(_cancellation.Token);
            _steps = snapshots.Select(pair => new Step
            {
                Key = pair.Key, Definition = pair.Value, Context = new LoadingStepContext(_cancellation.Token)
            }).Append(new Step
            {
                Key = "", Definition = new LoadingStepDefinition { Name = "Loading save", Volume = 100, Order = 1 },
                Context = _native
            }).OrderBy(step => step.Definition.Order)
                .ThenBy(step => step.Key, StringComparer.Ordinal).ToArray();
            var largest = _steps.Max(step => step.Definition.Volume);
            foreach (var step in _steps) step.Weight = step.Definition.Volume / largest;
            _totalWeight = _steps.Sum(step => step.Weight);
            _started = now;
            Active = true;
            Ready = false;
            Complete = false;
            StepName = _steps[0].Definition.Name;
            _nativeValue = 0;
            _callbacksStarted = false;
            Value = 0;
            Opacity = 0;
            FadingOut = false;
        }

        internal void StartSteps()
        {
            if (!Active || _callbacksStarted) return;
            _callbacksStarted = true;
            var cancellation = _cancellation;
            foreach (var step in _steps)
            {
                if (!Active || !ReferenceEquals(cancellation, _cancellation)) return;
                if (step.Context == _native) continue;
                try
                {
                    step.Definition.Start(step.Context);
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException("Loading step " + step.Key + " (" +
                        step.Definition.Name + ") failed: " + error.GetBaseException().Message, error);
                }
            }
        }

        internal void MarkReady(int frame)
        {
            if (!Active || Ready) return;
            Ready = true;
            _readyFrame = frame;
        }

        internal void Update(double now, double delta, int frame)
        {
            if (!Active) return;
            var elapsed = Math.Max(0, now - _started);
            var step = Math.Max(0, delta);
            Opacity = FadingOut ? Math.Max(0, Opacity - step / FadeSeconds)
                : Math.Min(1, Opacity + step / FadeSeconds);
            if (Ready && frame > _readyFrame)
            {
                _native.Release();
            }
            else
            {
                var target = 0.9 * (1 - Math.Exp(-elapsed / 3));
                _nativeValue = Math.Max(_nativeValue, Math.Min(target, _nativeValue + step * 0.8));
                _native.Report(_nativeValue * 100);
            }

            var weighted = 0.0;
            string current = null;
            foreach (var entry in _steps)
            {
                entry.Context.Read(out var completion, out var released);
                weighted += entry.Weight * completion;
                if (!released && current == null) current = entry.Definition.Name;
            }

            Complete = current == null;
            StepName = current ?? "Loading complete";
            Value = Complete ? 1 : Math.Max(Value, Math.Min(1, weighted / _totalWeight));
        }

        internal void FadeOut()
        {
            if (Active && Complete && Opacity >= 1) FadingOut = true;
        }

        internal void Cancel()
        {
            var cancellation = _cancellation;
            _cancellation = null;
            foreach (var step in _steps) step.Context.Invalidate();
            _steps = Array.Empty<Step>();
            _native = null;
            Active = false;
            Ready = false;
            Complete = false;
            StepName = null;
            Value = 0;
            Opacity = 0;
            FadingOut = false;
            CancellationError = null;
            if (cancellation == null) return;
            try
            {
                cancellation.Cancel();
            }
            catch (AggregateException error)
            {
                CancellationError = error;
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private sealed class Step
        {
            internal string Key;
            internal LoadingStepDefinition Definition;
            internal LoadingStepContext Context;
            internal double Weight;
        }
    }
}
