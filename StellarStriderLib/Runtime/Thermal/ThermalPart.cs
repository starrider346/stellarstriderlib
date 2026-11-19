using System;
using HarmonyLib;
using Ships;
using Ships.Grid;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Placement;
using Ships.Interface.Settings;
using StellarStriderLib.Core;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    internal readonly struct ThermalPartKey : IEquatable<ThermalPartKey>
    {
        internal readonly uint ShipId;
        internal readonly bool IsHull;
        internal readonly ushort PartId;
        internal readonly HullPartPosition HullPosition;

        private ThermalPartKey(uint shipId, bool isHull, ushort partId, HullPartPosition position)
        {
            ShipId = shipId;
            IsHull = isHull;
            PartId = partId;
            HullPosition = position;
        }

        internal static ThermalPartKey Stateful(uint shipId, ushort id) => new(shipId, false, id, default);
        internal static ThermalPartKey Hull(uint shipId, HullPartPosition position) => new(shipId, true, 0, position);

        public bool Equals(ThermalPartKey other) => ShipId == other.ShipId && IsHull == other.IsHull &&
                                                   PartId == other.PartId && HullPosition.Equals(other.HullPosition);
        public override bool Equals(object value) => value is ThermalPartKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ShipId, IsHull, PartId, HullPosition);
    }

    internal readonly struct ThermalPart
    {
        private static readonly Func<TrackedShip, ShipGrid> Grid = (Func<TrackedShip, ShipGrid>)AccessTools
            .PropertyGetter(typeof(TrackedShip), "Grid").CreateDelegate(typeof(Func<TrackedShip, ShipGrid>));

        internal readonly ThermalPartKey Key;
        internal readonly HullPart? Hull;
        internal readonly StatefulPart Stateful;
        internal readonly PartSettings Settings;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;

        internal ThermalPart(uint ship, HullPart hull)
        {
            Key = ThermalPartKey.Hull(ship, hull.HullPlacement.Position);
            Hull = hull;
            Stateful = null;
            Settings = hull.HullSettings;
            Position = hull.HullPlacement.LocalPosition;
            Rotation = hull.HullPlacement.Rotation;
        }

        internal ThermalPart(uint ship, StatefulPart part)
        {
            Key = ThermalPartKey.Stateful(ship, part.Id);
            Hull = null;
            Stateful = part;
            Settings = part.Settings;
            Position = part.LocalPosition;
            Rotation = part.LocalRotation;
        }

        internal ThermalProfile Profile
        {
            get
            {
                if (PartRuntime.TryDefinition(Settings.id, out _, out var definition))
                    return ThermalModel.Custom(definition.ThermalCapacityKelvin, definition.ThermalConductivity);
                if (Stateful?.State is IDoorState)
                    return ThermalModel.Door;
                if (Settings is HullPartSettings hull)
                {
                    switch (hull.hullMaterialType)
                    {
                        case HullMaterialType.Metal: return ThermalModel.Iron;
                        case HullMaterialType.Wood: return ThermalModel.Wood;
                        case HullMaterialType.Glass: return ThermalModel.Glass;
                    }
                }

                return ThermalModel.Default;
            }
        }

        internal static bool TryFind(TrackedShip ship, ThermalPartKey key, out ThermalPart part)
        {
            part = default;
            if (!key.IsHull)
            {
                if (!ship.TryGetStatefulPart(key.PartId, out var stateful)) return false;
                part = new ThermalPart(ship.Id, stateful);
                return true;
            }

            var tile = Grid(ship).GetPart(key.HullPosition);
            if (!tile.HasValue) return false;
            var position = tile.Value.HullPlacement.Position;
            if (ship.State.ContainsHullPart(position))
            {
                part = new ThermalPart(ship.Id, tile.Value);
                return true;
            }

            foreach (var candidate in ship.StatefulParts)
            {
                if (candidate.Placement is HullPartPlacement hull && hull.Position.Equals(position))
                {
                    part = new ThermalPart(ship.Id, candidate);
                    return true;
                }
            }

            return false;
        }
    }

    internal readonly struct ThermalReading
    {
        internal readonly ushort SettingsId;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly float Temperature;

        internal ThermalReading(ThermalPart part, float temperature) :
            this(part.Settings.id, part.Position, part.Rotation, temperature) { }

        internal ThermalReading(ushort settingsId, Vector3 position, Quaternion rotation, float temperature)
        {
            SettingsId = settingsId;
            Position = position;
            Rotation = rotation;
            Temperature = temperature;
        }

        internal bool Matches(ThermalPart part) =>
            part.Settings.id == SettingsId && (Position - part.Position).sqrMagnitude <= 0.000001f &&
            Mathf.Abs(Quaternion.Dot(Rotation, part.Rotation)) >= 0.999999f;

        internal ThermalReading Rounded() => new(SettingsId, Position, Rotation, (float)Math.Round(Temperature));

        internal bool SameValue(ThermalReading other) =>
            SettingsId == other.SettingsId && Position.Equals(other.Position) && Rotation.Equals(other.Rotation) &&
            Temperature.Equals(other.Temperature);
    }
}
