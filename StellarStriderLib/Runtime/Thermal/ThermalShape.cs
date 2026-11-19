using System.Collections.Generic;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    /// <summary>Solid collider geometry in ship-local metres.</summary>
    internal sealed class ThermalShape
    {
        internal const float ContactTolerance = 0.005f;
        private const float ParallelTolerance = 0.000001f;
        internal readonly Bounds Bounds;
        private readonly Vector3[] _vertices, _normals, _edges;

        private ThermalShape(Vector3[] vertices, Vector3[] normals, Vector3[] edges)
        {
            _vertices = vertices;
            _normals = normals;
            _edges = edges;
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(vertex);
            Bounds = bounds;
        }

        internal static ThermalShape Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            var half = size * 0.5f;
            var axes = new[] { rotation * Vector3.right, rotation * Vector3.up, rotation * Vector3.forward };
            var vertices = new Vector3[8];
            for (var i = 0; i < vertices.Length; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                vertices[i] = center + rotation * corner;
            }

            return new ThermalShape(vertices, axes, axes);
        }

        internal static IEnumerable<ThermalShape> Mesh(Vector3[] vertices, int[] triangles, bool convex)
        {
            if (vertices.Length == 0 || triangles.Length == 0) yield break;
            var normals = new List<Vector3>();
            var edges = new List<Vector3>();
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var triangle = new[] { vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]] };
                var ab = triangle[1] - triangle[0];
                var bc = triangle[2] - triangle[1];
                var ca = triangle[0] - triangle[2];
                var normal = Vector3.Cross(ab, bc);
                if (normal.sqrMagnitude < 1e-12f) continue;
                if (convex)
                {
                    AddAxis(normals, normal);
                    AddAxis(edges, ab);
                    AddAxis(edges, bc);
                    AddAxis(edges, ca);
                }
                else
                {
                    normal.Normalize();
                    var directions = new[] { ab.normalized, bc.normalized, ca.normalized };
                    yield return new ThermalShape(triangle, new[]
                    {
                        normal, Vector3.Cross(normal, directions[0]), Vector3.Cross(normal, directions[1]),
                        Vector3.Cross(normal, directions[2])
                    }, directions);
                }
            }

            if (convex && normals.Count > 0)
                yield return new ThermalShape(vertices, normals.ToArray(), edges.ToArray());
        }

        private static void AddAxis(List<Vector3> axes, Vector3 direction)
        {
            direction.Normalize();
            foreach (var axis in axes)
                if (Mathf.Abs(Vector3.Dot(axis, direction)) > 1 - ParallelTolerance) return;
            axes.Add(direction);
        }

        internal ThermalShape Place(Vector3 position, Quaternion rotation)
        {
            var vertices = new Vector3[_vertices.Length];
            for (var i = 0; i < vertices.Length; i++) vertices[i] = position + rotation * _vertices[i];
            var normals = new Vector3[_normals.Length];
            for (var i = 0; i < normals.Length; i++) normals[i] = rotation * _normals[i];
            var edges = new Vector3[_edges.Length];
            for (var i = 0; i < edges.Length; i++) edges[i] = rotation * _edges[i];
            return new ThermalShape(vertices, normals, edges);
        }

        internal bool Touches(ThermalShape other)
        {
            var bounds = Bounds;
            bounds.Expand(2 * ContactTolerance);
            if (!bounds.Intersects(other.Bounds)) return false;
            foreach (var axis in _normals)
                if (Separates(other, axis)) return false;
            foreach (var axis in other._normals)
                if (Separates(other, axis)) return false;
            foreach (var edge in _edges)
            foreach (var otherEdge in other._edges)
            {
                var axis = Vector3.Cross(edge, otherEdge);
                if (axis.sqrMagnitude > ParallelTolerance && Separates(other, axis.normalized))
                    return false;
            }

            return true;
        }

        private bool Separates(ThermalShape other, Vector3 axis)
        {
            Project(axis, out var min, out var max);
            other.Project(axis, out var otherMin, out var otherMax);
            return min - otherMax > ContactTolerance || otherMin - max > ContactTolerance;
        }

        private void Project(Vector3 axis, out float min, out float max)
        {
            min = max = Vector3.Dot(_vertices[0], axis);
            for (var i = 1; i < _vertices.Length; i++)
            {
                var value = Vector3.Dot(_vertices[i], axis);
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }
        }
    }
}
