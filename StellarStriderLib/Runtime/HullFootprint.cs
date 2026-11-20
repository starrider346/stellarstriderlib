using System;
using System.Collections.Generic;
using Ships.Interface.Settings;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    internal static class HullRegistration
    {
        internal static HullPartSettings RequireDonor(string key, string donorName, PartSettings donor)
        {
            if (donor is not HullPartSettings hull || hull.IsStateful ||
                !string.IsNullOrEmpty(hull.customCreationClass))
            {
                throw new InvalidOperationException($"Hull {key} requires a native stateless hull donor: {donorName}");
            }

            return hull;
        }
    }

    internal static class HullFootprint
    {
        internal static bool IsValid(Vector3 size) => WholeCells(size.x) && WholeCells(size.y) &&
            WholeCells(size.z) && (double)size.x * size.y * size.z <= int.MaxValue;

        private static bool WholeCells(float count) => count >= 1 && count == MathF.Floor(count);

        internal static IReadOnlyList<Vector3Int> Coordinates(Vector3Int anchor, Quaternion rotation, Vector3 size)
        {
            var right = GridStep(rotation * Vector3.right);
            var up = GridStep(rotation * Vector3.up);
            var forward = GridStep(rotation * Vector3.forward);
            var cells = new List<Vector3Int>();
            var seen = new HashSet<Vector3Int>();

            for (var x = 0; x < (int)size.x; x++)
            {
                for (var y = 0; y < (int)size.y; y++)
                {
                    for (var z = 0; z < (int)size.z; z++)
                    {
                        var cell = anchor + right * x + up * y + forward * z;
                        if (seen.Add(cell)) cells.Add(cell);
                    }
                }
            }

            return cells;
        }

        internal static Vector3Int SlopeRun(Quaternion rotation)
        {
            var direction = rotation * Vector3.up;
            return GridStep(new Vector3(direction.x, 0, direction.z).normalized);
        }

        internal static IReadOnlyList<Vector3Int> SlopeCoordinates(Vector3Int anchor, Quaternion rotation, int run)
        {
            var direction = SlopeRun(rotation);
            var cells = new Vector3Int[run];
            for (var cell = 0; cell < run; cell++) cells[cell] = anchor + direction * cell;
            return cells;
        }

        internal static Vector3Int[] SlopeRoomVoxels(Vector3Int anchor, Quaternion rotation, int run,
            int resolution, bool scan)
        {
            var half = resolution / 2;
            var origin = anchor * resolution + new Vector3Int(half, half, half);
            var along = SlopeRun(rotation);
            var across = GridStep(rotation * Vector3.right);
            var rise = (rotation * Vector3.up).y >= 0 ? 1 : -1;
            var voxels = new List<Vector3Int>();
            for (var step = 0; step <= run * resolution; step++)
            {
                var height = rise * ((float)step / run - half);
                var lower = (int)MathF.Floor(height);
                var upper = (int)MathF.Ceiling(height);
                for (var width = -half; width <= half; width++)
                {
                    var column = origin + along * (step - half) + across * width;
                    if (scan)
                    {
                        voxels.Add(column + Vector3Int.up * (lower - 1));
                        voxels.Add(column + Vector3Int.up * (upper + 1));
                    }
                    else
                    {
                        for (var level = lower; level <= upper; level++)
                            voxels.Add(column + Vector3Int.up * level);
                    }
                }
            }

            return voxels.ToArray();
        }

        private static Vector3Int GridStep(Vector3 direction) => new(
            (int)MathF.Round(direction.x, MidpointRounding.AwayFromZero),
            (int)MathF.Round(direction.y, MidpointRounding.AwayFromZero),
            (int)MathF.Round(direction.z, MidpointRounding.AwayFromZero));
    }
}
