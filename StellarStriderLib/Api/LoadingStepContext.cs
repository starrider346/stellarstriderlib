using System;
using System.Threading;

namespace StellarStriderLib
{
    /// <summary>A single load's progress handle. Report and Release may be called from worker threads.</summary>
    public sealed class LoadingStepContext
    {
        private readonly object _sync = new();
        private double _percentage;
        private bool _released, _canceled;

        /// <summary>Canceled when the loading screen finishes or the load is abandoned.</summary>
        public CancellationToken CancellationToken { get; }

        internal LoadingStepContext(CancellationToken cancellationToken) => CancellationToken = cancellationToken;

        /// <summary>Reports absolute progress from 0 to 100. Progress cannot decrease; 100 does not release.</summary>
        public void Report(double percentage)
        {
            lock (_sync)
            {
                if (_released || _canceled) return;
                if (double.IsNaN(percentage) || double.IsInfinity(percentage) || percentage < 0 || percentage > 100)
                    throw new ArgumentOutOfRangeException(nameof(percentage), "Loading progress must be between 0 and 100.");
                _percentage = Math.Max(_percentage, percentage);
            }
        }

        /// <summary>Marks this step fully complete and releases its gate. Repeated calls are harmless.</summary>
        public void Release()
        {
            lock (_sync)
            {
                if (_released || _canceled) return;
                _percentage = 100;
                _released = true;
            }
        }

        internal void Read(out double completion, out bool released)
        {
            lock (_sync)
            {
                completion = _percentage / 100;
                released = _released;
            }
        }

        internal void Invalidate()
        {
            lock (_sync) _canceled = true;
        }
    }
}
