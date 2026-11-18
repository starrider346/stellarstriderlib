using System;
using System.Collections.Generic;

namespace StellarStriderLib.Core
{
    /// <summary>Owns temporary clock freezes and restores every captured rate, including pre-existing pauses.</summary>
    internal sealed class LoadingPause
    {
        private readonly List<(Action<float> Write, float Previous)> _clocks = new();
        internal bool Active => _clocks.Count != 0;

        internal void Capture(Func<float> read, Action<float> write)
        {
            var previous = read();
            _clocks.Add((write, previous));
            write(0);
        }

        internal void Hold()
        {
            foreach (var clock in _clocks) clock.Write(0);
        }

        internal void Restore()
        {
            var clocks = _clocks.ToArray();
            _clocks.Clear();
            List<Exception> errors = null;
            for (var index = clocks.Length - 1; index >= 0; index--)
            {
                try { clocks[index].Write(clocks[index].Previous); }
                catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            }
            if (errors != null) throw new AggregateException("Loading pause restoration failed.", errors);
        }
    }
}
