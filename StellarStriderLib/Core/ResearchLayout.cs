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

    /// <summary>Places registered research near its prerequisites while preserving existing node positions.</summary>
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

            var result = new Dictionary<string, GridPoint>(StringComparer.Ordinal);
            var occupied = new Dictionary<string, GridPoint>(fixedNodes, StringComparer.Ordinal);
            var connections = new List<(GridPoint From, GridPoint To)>();
            foreach (var node in nodes.Values.Where(n => fixedNodes.ContainsKey(n.Key)))
            {
                foreach (var parent in node.Parents.Where(fixedNodes.ContainsKey))
                {
                    connections.Add((fixedNodes[parent], fixedNodes[node.Key]));
                }
            }

            foreach (var layer in nodes.Values.Where(n => !fixedNodes.ContainsKey(n.Key))
                         .GroupBy(n => columns[n.Key]).OrderBy(g => g.Key))
            {
                // Parents are already placed, including prerequisites belonging to a later mod/tree.
                foreach (var node in layer.OrderBy(n => n.Group, StringComparer.Ordinal)
                             .ThenByDescending(n => n.Parents.Length == 0 ? 0 : n.Parents.Average(p => occupied[p].Y))
                             .ThenBy(n => n.Key, StringComparer.Ordinal))
                {
                    var parents = node.Parents.Select(p => occupied[p]).ToArray();
                    var preferred = parents.Length == 0 ? 0 : parents.Average(p => (double)p.Y);
                    var center = checked((int)Math.Round(preferred));
                    var best = new GridPoint(layer.Key, center);
                    var bestScore = double.PositiveInfinity;
                    var takenRows = new HashSet<int>(occupied.Values.Where(p => p.X == layer.Key).Select(p => p.Y));

                    // At most takenRows.Count consecutive rows can be occupied. Looking one row
                    // beyond each obstacle also gives connectors room without using a global bottom row.
                    var radius = occupied.Count + 1;
                    for (var distance = 0; distance <= radius; distance++)
                    {
                        foreach (var row in distance == 0 ? new[] { center } :
                                     new[] { checked(center - distance), checked(center + distance) })
                        {
                            if (takenRows.Contains(row))
                            {
                                continue;
                            }

                            var point = new GridPoint(layer.Key, row);
                            var score = Math.Abs(row - preferred);
                            // A modest detour is preferable to drawing through another card. Keep
                            // the penalty bounded so distant, unrelated research cannot stretch a tree.
                            score += 4 * connections.Count(c => IntersectsCard(c.From, c.To, point));
                            foreach (var parent in parents)
                            {
                                score += 4 * occupied.Values.Count(p => IntersectsCard(parent, point, p));
                            }

                            if (score < bestScore)
                            {
                                best = point;
                                bestScore = score;
                            }
                        }

                        if (distance > bestScore + 1)
                        {
                            break;
                        }
                    }

                    result.Add(node.Key, best);
                    occupied.Add(node.Key, best);
                    connections.AddRange(parents.Select(p => (p, best)));
                }
            }

            return result;
        }

        private static bool IntersectsCard(GridPoint from, GridPoint to, GridPoint card)
        {
            if ((card.X == from.X && card.Y == from.Y) || (card.X == to.X && card.Y == to.Y))
            {
                return false;
            }

            // Native cards are 400 x 160 on a 500 x 260 grid. Include clearance
            // around the card, and start/end connectors at the card's horizontal edges.
            var startX = from.X + 0.4;
            var endX = to.X - 0.4;
            var deltaX = endX - startX;
            var entry = Math.Max(0, Math.Min((card.X - 0.45 - startX) / deltaX,
                (card.X + 0.45 - startX) / deltaX));
            var exit = Math.Min(1, Math.Max((card.X - 0.45 - startX) / deltaX,
                (card.X + 0.45 - startX) / deltaX));
            if (entry > exit)
            {
                return false;
            }

            var entryY = from.Y + (to.Y - (double)from.Y) * entry;
            var exitY = from.Y + (to.Y - (double)from.Y) * exit;
            return Math.Min(entryY, exitY) <= card.Y + 0.4 && Math.Max(entryY, exitY) >= card.Y - 0.4;
        }
    }
}
