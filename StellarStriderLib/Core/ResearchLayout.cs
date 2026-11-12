using System;
using System.Collections.Generic;
using System.Linq;

namespace StellarStriderLib.Core
{
    internal readonly struct GridPoint
    {
        internal readonly int X, Y;

        internal GridPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>Describes a research node and its prerequisites for deterministic grid layout.</summary>
    internal sealed class LayoutNode
    {
        internal string Key, Group;
        internal string[] Parents = Array.Empty<string>();
    }

    /// <summary>Places registered research in separate tree bands while preserving existing node positions.</summary>
    internal static class ResearchLayout
    {
        internal static Dictionary<string, GridPoint> Arrange(IEnumerable<LayoutNode> definitions,
            IReadOnlyDictionary<string, GridPoint> fixedNodes)
        {
            var nodes = definitions.ToDictionary(n => n.Key, StringComparer.Ordinal);
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);

            int Column(string key)
            {
                if (fixedNodes.TryGetValue(key, out var point))
                {
                    return point.X;
                }

                if (columns.TryGetValue(key, out var column))
                {
                    return column;
                }

                if (!nodes.TryGetValue(key, out var node))
                {
                    throw new InvalidOperationException("Missing research prerequisite: " + key);
                }

                if (!visiting.Add(key))
                {
                    throw new InvalidOperationException("Research prerequisite cycle: " + key);
                }

                column = node.Parents.Length == 0 ? 0 : checked(node.Parents.Max(Column) + 1);
                visiting.Remove(key);
                columns.Add(key, column);
                return column;
            }

            foreach (var node in nodes.Values)
            {
                Column(node.Key);
            }

            var top = fixedNodes.Count == 0 ? 0 : checked(fixedNodes.Values.Min(p => p.Y) - 2);
            var result = new Dictionary<string, GridPoint>(StringComparer.Ordinal);
            foreach (var group in nodes.Values.GroupBy(n => n.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var height = 0;
                foreach (var layer in group.GroupBy(n => columns[n.Key]).OrderBy(g => g.Key))
                {
                    var row = 0;
                    foreach (var node in layer.OrderBy(n => n.Key, StringComparer.Ordinal))
                    {
                        result.Add(node.Key, new GridPoint(layer.Key, checked(top - row++)));
                    }

                    height = Math.Max(height, row);
                }

                top = checked(top - height - 1);
            }

            return result;
        }
    }
}
