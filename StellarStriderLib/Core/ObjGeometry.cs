using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StellarStriderLib.Core
{
    internal readonly struct Float2
    {
        internal readonly float X, Y;

        internal Float2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    internal readonly struct Float3
    {
        internal readonly float X, Y, Z;

        internal Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>Parses OBJ geometry into indexed vertices, UV channels and triangles for Unity conversion.</summary>
    internal sealed class ObjGeometry
    {
        internal Float3[] Vertices, Normals;
        internal Float2[] Uv, Palette;
        internal int[] Triangles;

        internal static ObjGeometry Parse(string text)
        {
            var positions = new List<Float3>();
            var normals = new List<Float3>();
            var uv = new List<Float2>();
            var palette = new List<Float2>();
            var vertices = new List<Float3>();
            var outputNormals = new List<Float3>();
            var outputUv = new List<Float2>();
            var outputPalette = new List<Float2>();
            var triangles = new List<int>();
            var indices = new Dictionary<string, int>();
            var completeNormals = true;
            using (var reader = new StringReader(text))
            {
                while (reader.ReadLine() is { } line)
                {
                    var fields = line.Split('#')[0].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length == 0)
                    {
                        continue;
                    }

                    switch (fields[0])
                    {
                        case "v":
                            positions.Add(new Float3(-Number(fields[1]), Number(fields[2]), Number(fields[3])));
                            break;
                        case "vn":
                            normals.Add(new Float3(-Number(fields[1]), Number(fields[2]), Number(fields[3])));
                            break;
                        case "vt":
                            uv.Add(new Float2(Number(fields[1]), Number(fields[2])));
                            palette.Add(fields.Length >= 5
                                ? new Float2(Number(fields[3]), Number(fields[4]))
                                : new Float2(0, 0));
                            break;
                        case "f":
                            if (fields.Length < 4)
                            {
                                throw new FormatException("OBJ face needs at least three vertices.");
                            }

                            var face = new int[fields.Length - 1];
                            for (var i = 1; i < fields.Length; i++)
                            {
                                var parts = fields[i].Split('/');
                                var positionIndex = Index(parts[0], positions.Count);
                                var uvIndex = parts.Length > 1 && parts[1].Length > 0 ? Index(parts[1], uv.Count) : -1;
                                var normalIndex = parts.Length > 2 && parts[2].Length > 0
                                    ? Index(parts[2], normals.Count)
                                    : -1;
                                var key = positionIndex + "/" + uvIndex + "/" + normalIndex;
                                if (!indices.TryGetValue(key, out var index))
                                {
                                    index = vertices.Count;
                                    indices.Add(key, index);
                                    vertices.Add(positions[positionIndex]);
                                    outputUv.Add(uvIndex < 0 ? new Float2(0, 0) : uv[uvIndex]);
                                    outputPalette.Add(uvIndex < 0 ? new Float2(0, 0) : palette[uvIndex]);
                                    outputNormals.Add(normalIndex < 0 ? new Float3(0, 0, 0) : normals[normalIndex]);
                                    completeNormals &= normalIndex >= 0;
                                }

                                face[i - 1] = index;
                            }

                            for (var i = 1; i < face.Length - 1; i++)
                            {
                                triangles.Add(face[0]);
                                triangles.Add(face[i + 1]);
                                triangles.Add(face[i]);
                            }

                            break;
                    }
                }
            }

            if (vertices.Count == 0 || triangles.Count == 0)
            {
                throw new FormatException("OBJ has no faces.");
            }

            return new ObjGeometry
            {
                Vertices = vertices.ToArray(),
                Normals = completeNormals ? outputNormals.ToArray() : null,
                Uv = outputUv.ToArray(),
                Palette = outputPalette.ToArray(),
                Triangles = triangles.ToArray()
            };
        }

        private static float Number(string value)
        {
            var result = float.Parse(value, CultureInfo.InvariantCulture);
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                throw new FormatException("OBJ numbers must be finite.");
            }

            return result;
        }

        private static int Index(string value, int count)
        {
            var index = int.Parse(value, CultureInfo.InvariantCulture);
            index = index > 0 ? index - 1 : count + index;
            if (index < 0 || index >= count)
            {
                throw new FormatException("OBJ index outside its array.");
            }

            return index;
        }
    }
}
