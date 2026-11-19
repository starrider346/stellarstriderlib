using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using StellarStriderLib.Runtime;

namespace StellarStriderLib
{
    /// <summary>Reads or sets a placed ship part's temperature on the Unity main thread.</summary>
    public static class PartThermal
    {
        /// <summary>Reads the temperature in Kelvin of a live stateful part.</summary>
        public static bool TryGetTemperature(PartContext part, out float kelvin) =>
            ThermalRuntime.TryRead(ThermalPartKey.Stateful(part.ShipId, part.PartId), out kelvin);

        /// <summary>Reads the temperature in Kelvin of a live hull part.</summary>
        public static bool TryGetTemperature(uint shipId, HullPartPosition position, out float kelvin) =>
            ThermalRuntime.TryRead(ThermalPartKey.Hull(shipId, position), out kelvin);

        /// <summary>Sets an absolute temperature on the server. A part at its limit will be destroyed on the next thermal step.</summary>
        public static bool TrySetTemperature(PartContext part, float kelvin) =>
            ThermalRuntime.TryWrite(ThermalPartKey.Stateful(part.ShipId, part.PartId), kelvin);

        /// <summary>Sets an absolute hull temperature on the server. Returns false for an unavailable part or invalid temperature.</summary>
        public static bool TrySetTemperature(uint shipId, HullPartPosition position, float kelvin) =>
            ThermalRuntime.TryWrite(ThermalPartKey.Hull(shipId, position), kelvin);
    }
}
