using System;

namespace StellarStriderLib.Core
{
    internal sealed class LoadingProgress
    {
        internal const double FadeSeconds = 0.35;
        private double _started;
        private int _readyFrame;
        internal bool Active { get; private set; }
        internal bool Ready { get; private set; }
        internal double Value { get; private set; }
        internal double Opacity { get; private set; }
        internal bool FadingOut { get; private set; }

        internal void Begin(double now)
        {
            _started = now;
            Active = true;
            Ready = false;
            Value = 0;
            Opacity = 0;
            FadingOut = false;
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
                Value = 1;
            }
            else
            {
                var target = 0.9 * (1 - Math.Exp(-elapsed / 3));
                Value = Math.Max(Value, Math.Min(target, Value + step * 0.8));
            }
        }

        internal void FadeOut()
        {
            if (Active && Value >= 1 && Opacity >= 1) FadingOut = true;
        }

        internal void Cancel()
        {
            Active = false;
            Ready = false;
            Value = 0;
            Opacity = 0;
            FadingOut = false;
        }
    }
}
