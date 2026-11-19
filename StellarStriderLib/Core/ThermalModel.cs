using System;
using System.Collections.Generic;

namespace StellarStriderLib.Core
{
    internal static class ThermalModel
    {
        internal const float Ambient = 300f;
        internal const double StepSeconds = 0.25;
        internal const double CoolingHalfLife = 8 * 60;
        internal const float AmbientTolerance = 0.25f;
        private static readonly double CoolingFactor = Math.Pow(0.5, StepSeconds / CoolingHalfLife);
        internal static readonly ThermalProfile Default = new ThermalProfile(1000, 0.10f);
        internal static readonly ThermalProfile Iron = new ThermalProfile(2000, 0.25f);
        internal static readonly ThermalProfile Wood = new ThermalProfile(750, 0.025f);
        internal static readonly ThermalProfile Glass = new ThermalProfile(1500, 0.075f);
        internal static readonly ThermalProfile Door = new ThermalProfile(1500, 0.15f);

        internal static bool ValidTemperature(float kelvin) => float.IsFinite(kelvin) && kelvin >= 0;

        internal static void ValidateProfile(float? capacity, float? conductivity)
        {
            if (capacity.HasValue && (!float.IsFinite(capacity.Value) || capacity.Value <= Ambient))
                throw new ArgumentException("Thermal capacity must be finite and above 300 K.");
            if (conductivity.HasValue && (!float.IsFinite(conductivity.Value) || conductivity.Value < 0))
                throw new ArgumentException("Thermal conductivity must be finite and nonnegative.");
        }

        internal static ThermalProfile Custom(float? capacity, float? conductivity) =>
            new (capacity ?? Default.Capacity, conductivity ?? Default.Conductivity);

        internal static double PairRate(float a, float b) => a == 0 || b == 0 ? 0 : 2d * a * b / ((double)a + b);

        internal static bool HasFailed(double temperature, ThermalProfile profile) => temperature >= profile.Capacity;

        internal static float Cool(double temperature)
        {
            var cooled = Ambient + (temperature - Ambient) * CoolingFactor;
            return Math.Abs(cooled - Ambient) <= AmbientTolerance ? Ambient : (float)cooled;
        }

        internal static void Exchange(double[] temperatures, IReadOnlyList<ThermalContact> contacts,
            double[] rates, double[] changes)
        {
            Array.Clear(rates, 0, rates.Length);
            Array.Clear(changes, 0, changes.Length);
            foreach (var contact in contacts)
            {
                rates[contact.A] += contact.Rate;
                rates[contact.B] += contact.Rate;
            }

            foreach (var contact in contacts)
            {
                var scale = Math.Max(1, Math.Max(rates[contact.A], rates[contact.B]));
                var change = (temperatures[contact.A] - temperatures[contact.B]) * (StepSeconds * contact.Rate / scale);
                changes[contact.A] -= change;
                changes[contact.B] += change;
            }

            for (var i = 0; i < temperatures.Length; i++) temperatures[i] += changes[i];
        }
    }

    internal readonly struct ThermalProfile
    {
        internal readonly float Capacity, Conductivity;

        internal ThermalProfile(float capacity, float conductivity)
        {
            Capacity = capacity;
            Conductivity = conductivity;
        }
    }

    internal readonly struct ThermalContact
    {
        internal readonly int A, B;
        internal readonly double Rate;

        internal ThermalContact(int a, int b, double rate)
        {
            A = a;
            B = b;
            Rate = rate;
        }
    }
}
