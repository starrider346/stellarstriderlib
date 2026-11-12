using System.Linq;
using StellarStriderLib.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace StellarStriderLib.Models
{
    /// <summary>Reads exported OBJ geometry, reflecting X and reversing winding for Unity.</summary>
    public static class ObjReader
    {
        /// <summary>Creates a Unity mesh on the main thread, preserving normals, UVs and extended palette UVs.</summary>
        public static Mesh ParseObj(string text)
        {
            StriderRegistry.MainThread();
            var data = ObjGeometry.Parse(text);
            var mesh = new Mesh
                { indexFormat = data.Vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(data.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray());
            mesh.SetUVs(0, data.Uv.Select(v => new Vector2(v.X, v.Y)).ToArray());
            mesh.SetUVs(1, data.Palette.Select(v => new Vector2(v.X, v.Y)).ToArray());
            mesh.SetTriangles(data.Triangles, 0);
            if (data.Normals != null)
            {
                mesh.SetNormals(data.Normals.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray());
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
