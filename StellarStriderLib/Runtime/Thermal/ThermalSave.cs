using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model;
using Ships.Interface.Model.Placement;
using StellarStriderLib.Core;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    internal static class ThermalSave
    {
        private const int MaximumRows = 100000;

        internal static JArray Capture()
        {
            ThermalRuntime.RestorePending();
            var rows = new JArray();
            foreach (var pair in ThermalRuntime.Readings())
            {
                var key = pair.Key;
                var reading = pair.Value;
                var row = new JObject
                {
                    ["ship"] = key.ShipId,
                    ["settings"] = reading.SettingsId,
                    ["position"] = new JArray(reading.Position.x, reading.Position.y, reading.Position.z),
                    ["rotation"] = new JArray(reading.Rotation.x, reading.Rotation.y, reading.Rotation.z, reading.Rotation.w),
                    ["kelvin"] = reading.Temperature
                };
                if (key.IsHull)
                {
                    var coord = key.HullPosition.VoxelCoord;
                    row["hull"] = new JArray(coord.x, coord.y, coord.z, (int)key.HullPosition.Type);
                }
                else
                {
                    row["part"] = key.PartId;
                }

                rows.Add(row);
                if (rows.Count > MaximumRows) throw new InvalidDataException("Too many thermal readings to save.");
            }

            return rows;
        }

        internal static Dictionary<ThermalPartKey, ThermalReading> Parse(JToken document)
        {
            if (document == null) return null;
            if (document is not JArray rows || rows.Count > MaximumRows)
                throw new InvalidDataException("Invalid thermal state in the part save.");

            var readings = new Dictionary<ThermalPartKey, ThermalReading>();
            foreach (var row in rows)
            {
                if (row is not JObject part || part["position"] is not JArray position || position.Count != 3 ||
                    part["rotation"] is not JArray rotation || rotation.Count != 4)
                    throw new InvalidDataException("Invalid thermal part placement.");

                var ship = (uint)part["ship"];
                ThermalPartKey key;
                if (part["hull"] is JArray hull && hull.Count == 4 && part["part"] == null)
                {
                    var type = (VoxelPartType)(int)hull[3];
                    if (!System.Enum.IsDefined(typeof(VoxelPartType), type))
                        throw new InvalidDataException("Invalid thermal hull position.");

                    var coord = new Vector3Int((int)hull[0], (int)hull[1], (int)hull[2]);
                    key = ThermalPartKey.Hull(ship, new HullPartPosition(coord, type));
                }
                else if (part["hull"] == null && part["part"] != null)
                {
                    key = ThermalPartKey.Stateful(ship, (ushort)part["part"]);
                }
                else
                {
                    throw new InvalidDataException("Invalid thermal part identifier.");
                }

                var value = new ThermalReading((ushort)part["settings"],
                    new Vector3((float)position[0], (float)position[1], (float)position[2]),
                    new Quaternion((float)rotation[0], (float)rotation[1], (float)rotation[2], (float)rotation[3]),
                    (float)part["kelvin"]);
                if (!ThermalModel.ValidTemperature(value.Temperature) || !Finite(value.Position) ||
                    !Finite(new Vector3(value.Rotation.x, value.Rotation.y, value.Rotation.z)) ||
                    !float.IsFinite(value.Rotation.w) || !readings.TryAdd(key, value))
                    throw new InvalidDataException("Invalid or duplicate thermal part reading.");
            }

            return readings;
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
