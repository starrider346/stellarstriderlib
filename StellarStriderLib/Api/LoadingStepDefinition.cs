using System;

namespace StellarStriderLib
{
    /// <summary>A client-local loading step, registered once and started afresh for every world load.</summary>
    public sealed class LoadingStepDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Positive progress weight. Native loading has a volume of 100.</summary>
        public double Volume { get; set; } = 100;

        /// <summary>Lower values display first. Native loading has order 1; this does not schedule work.</summary>
        public double Order { get; set; } = 10;

        /// <summary>Called on the Unity main thread at load start. Schedule work and return promptly.</summary>
        public Action<LoadingStepContext> Start { get; set; }

        internal LoadingStepDefinition Snapshot()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Loading step ID is required.");
            if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Loading step name is required.");
            if (double.IsNaN(Volume) || double.IsInfinity(Volume) || Volume <= 0)
                throw new ArgumentOutOfRangeException(nameof(Volume), "Loading step volume must be finite and positive.");
            if (double.IsNaN(Order) || double.IsInfinity(Order))
                throw new ArgumentOutOfRangeException(nameof(Order), "Loading step order must be finite.");
            if (Start == null) throw new ArgumentException("Loading step start callback is required.");
            return new LoadingStepDefinition { Id = Id, Name = Name, Volume = Volume, Order = Order, Start = Start };
        }
    }
}
