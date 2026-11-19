using System.Collections.Generic;
using Ships;
using StellarStriderLib.Core;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    internal sealed class ThermalGraph
    {
        private const float CellSize = 2f;
        private const int MaxCellsPerPart = 4096;

        internal readonly ThermalPart[] Parts;
        internal readonly ThermalProfile[] Profiles;
        internal readonly List<ThermalContact> Contacts = new();
        internal readonly double[] Temperatures, Rates, Changes;

        private ThermalGraph(List<ThermalPart> parts)
        {
            Parts = parts.ToArray();
            Profiles = new ThermalProfile[Parts.Length];
            Temperatures = new double[Parts.Length];
            Rates = new double[Parts.Length];
            Changes = new double[Parts.Length];
            for (var i = 0; i < Parts.Length; i++) Profiles[i] = Parts[i].Profile;
        }

        internal static ThermalGraph Build(TrackedShipServer ship)
        {
            var parts = new List<ThermalPart>(ship.PartCount);
            foreach (var part in ship.StatefulParts) parts.Add(new ThermalPart(ship.Id, part));
            foreach (var hull in ship.State.GetHullPartObjects()) parts.Add(new ThermalPart(ship.Id, hull));

            var graph = new ThermalGraph(parts);
            graph.BuildContacts(ship);
            return graph;
        }

        private void BuildContacts(TrackedShip ship)
        {
            var cells = new Dictionary<Vector3Int, List<int>>();
            var oversized = new List<int>();
            var candidates = new HashSet<int>();
            var shapes = new ThermalShape[Parts.Length][];
            for (var i = 0; i < Parts.Length; i++)
            {
                if (Profiles[i].Conductivity == 0) continue;
                var partShapes = shapes[i] = ThermalGeometry.For(ship, Parts[i]);
                if (partShapes.Length == 0) continue;
                var bounds = partShapes[0].Bounds;
                foreach (var shape in partShapes) bounds.Encapsulate(shape.Bounds);
                bounds.Expand(2 * ThermalShape.ContactTolerance);

                var min = Vector3Int.FloorToInt(bounds.min / CellSize);
                var max = Vector3Int.FloorToInt(bounds.max / CellSize);
                var cellCount = ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1) *
                                ((double)max.z - min.z + 1);
                candidates.Clear();
                if (cellCount > MaxCellsPerPart)
                {
                    oversized.Add(i);
                    for (var j = 0; j < i; j++)
                        if (shapes[j] != null) candidates.Add(j);
                }
                else
                {
                    candidates.UnionWith(oversized);
                    for (long x = min.x; x <= max.x; x++)
                    for (long y = min.y; y <= max.y; y++)
                    for (long z = min.z; z <= max.z; z++)
                    {
                        var cell = new Vector3Int((int)x, (int)y, (int)z);
                        if (!cells.TryGetValue(cell, out var occupants))
                            cells.Add(cell, occupants = new List<int>());
                        candidates.UnionWith(occupants);
                        occupants.Add(i);
                    }
                }

                foreach (var candidate in candidates)
                    if (Touches(partShapes, shapes[candidate]))
                        Contacts.Add(new ThermalContact(candidate, i,
                            ThermalModel.PairRate(Profiles[candidate].Conductivity, Profiles[i].Conductivity)));
            }
        }

        private static bool Touches(ThermalShape[] left, ThermalShape[] right)
        {
            foreach (var a in left)
            foreach (var b in right)
                if (a.Touches(b)) return true;
            return false;
        }

    }
}
