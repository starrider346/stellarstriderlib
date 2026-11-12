using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Models
{
    /// <summary>Selects native material cloning, solid tinting or unlit rendering.</summary>
    public enum MaterialKind
    {
        Native,
        Solid,
        Unlit
    }

    /// <summary>A native, solid-tinted or unlit material definition.</summary>
    public sealed class MaterialDefinition
    {
        public MaterialKind Kind { get; set; } = MaterialKind.Solid;

        public string NativeName { get; set; } = "LogicGates";

        public Color Tint { get; set; } = Color.white;

        public string TextureResource { get; set; }
    }

    /// <summary>An embedded OBJ mesh, transform and material.</summary>
    public sealed class MeshDefinition
    {
        /// <summary>Manifest resource name, or resource.zip!entry.obj.</summary>
        public string Resource { get; set; }

        public Vector3 Position { get; set; }

        public Vector3 Rotation { get; set; }

        public Vector3 Scale { get; set; } = Vector3.one;

        public MaterialDefinition Material { get; set; } = new MaterialDefinition();
    }

    /// <summary>Assembly-scoped asset cache. Owned and released by the library.</summary>
    public sealed class AssetScope
    {
        private readonly Assembly _assembly;
        private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Material> _materials = new(StringComparer.Ordinal);

        internal AssetScope(Assembly assembly)
        {
            _assembly = assembly;
        }

        /// <summary>Reads a manifest resource or archive.zip!entry path from the owning assembly.</summary>
        public byte[] Read(string resource)
        {
            var split = resource.Split(new[] { '!' }, 2);
            using (var stream = _assembly.GetManifestResourceStream(split[0]) ??
                                throw new FileNotFoundException("Missing embedded asset in " +
                                                                _assembly.GetName().Name + ": " + resource))
            {
                if (split.Length == 1)
                {
                    using (var output = new MemoryStream())
                    {
                        stream.CopyTo(output);
                        return output.ToArray();
                    }
                }

                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                using (var input = (archive.GetEntry(split[1]) ??
                                    throw new FileNotFoundException("Missing ZIP asset: " + resource)).Open())
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        /// <summary>Loads and caches an embedded OBJ mesh within this assembly scope.</summary>
        public Mesh Mesh(string resource)
        {
            StriderRegistry.MainThread();
            if (_meshes.TryGetValue(resource, out var mesh))
            {
                return mesh;
            }

            mesh = ObjReader.ParseObj(System.Text.Encoding.UTF8.GetString(Read(resource)));
            mesh.name = resource;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            _meshes.Add(resource, mesh);
            return mesh;
        }

        /// <summary>Loads and caches an embedded image within this assembly scope.</summary>
        public Texture2D Texture(string resource)
        {
            StriderRegistry.MainThread();
            if (_textures.TryGetValue(resource, out var texture))
            {
                return texture;
            }

            texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!ImageConversion.LoadImage(texture, Read(resource), false))
                {
                    throw new InvalidDataException("Invalid image: " + resource);
                }

                _textures.Add(resource, texture);
                return texture;
            }
            catch
            {
                Object.DestroyImmediate(texture);
                throw;
            }
        }

        /// <summary>Creates or retrieves a cached material with this scope's resources.</summary>
        public Material Material(MaterialDefinition definition)
        {
            StriderRegistry.MainThread();
            var materialDefinition = definition ?? new MaterialDefinition();
            var tint = materialDefinition.Tint;
            var key = materialDefinition.Kind + "|" + materialDefinition.NativeName + "|" +
                      materialDefinition.TextureResource + "|" + string.Join(",",
                          new[] { tint.r, tint.g, tint.b, tint.a }.Select(v =>
                              v.ToString("R", CultureInfo.InvariantCulture)));
            if (_materials.TryGetValue(key, out var material))
            {
                return material;
            }

            if (materialDefinition.Kind == MaterialKind.Unlit)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            }
            else
            {
                var source = Resources.FindObjectsOfTypeAll<Material>()
                    .FirstOrDefault(m => m.name == materialDefinition.NativeName);
                if (source == null)
                {
                    throw new InvalidOperationException("Native material unavailable: " +
                                                        materialDefinition.NativeName);
                }

                material = new Material(source);
            }

            try
            {
                material.hideFlags = HideFlags.HideAndDontSave;
                if (materialDefinition.Kind == MaterialKind.Solid)
                {
                    foreach (var property in new[]
                             {
                                 "_Albedo", "_ColorPalette", "_BaseMap", "_MainTex", "_Palette", "_PaletteTex",
                                 "_PaletteTexture"
                             })
                    {
                        if (material.HasProperty(property))
                        {
                            material.SetTexture(property, Texture2D.whiteTexture);
                        }
                    }
                }

                if (materialDefinition.TextureResource != null)
                {
                    foreach (var property in new[] { "_Albedo", "_BaseMap", "_MainTex" })
                    {
                        if (material.HasProperty(property))
                        {
                            material.SetTexture(property, Texture(materialDefinition.TextureResource));
                        }
                    }
                }

                foreach (var property in new[] { "_BaseColor", "_Color", "_RenderColor" })
                {
                    if (material.HasProperty(property))
                    {
                        material.SetColor(property, tint);
                    }
                }

                foreach (var property in new[] { "_Albedo", "_BaseMap", "_MainTex" })
                {
                    if (material.HasProperty(property) && material.GetTexture(property) == null)
                    {
                        material.SetTexture(property, Texture2D.whiteTexture);
                    }
                }

                _materials.Add(key, material);
                return material;
            }
            catch
            {
                Object.DestroyImmediate(material);
                throw;
            }
        }

        /// <summary>Adds a mesh renderer under a parent using this scope's embedded assets.</summary>
        public GameObject AddMesh(Transform parent, MeshDefinition definition)
        {
            StriderRegistry.MainThread();
            var child = new GameObject(definition.Resource) { layer = parent.gameObject.layer };
            child.transform.SetParent(parent, false);
            child.transform.localPosition = definition.Position;
            child.transform.localRotation = Quaternion.Euler(definition.Rotation);
            child.transform.localScale = definition.Scale;
            child.AddComponent<MeshFilter>().sharedMesh = Mesh(definition.Resource);
            child.AddComponent<MeshRenderer>().sharedMaterial = Material(definition.Material);
            return child;
        }

        internal void Release()
        {
            foreach (var value in _meshes.Values)
            {
                Object.Destroy(value);
            }

            foreach (var value in _materials.Values)
            {
                Object.Destroy(value);
            }

            foreach (var value in _textures.Values)
            {
                Object.Destroy(value);
            }

            _meshes.Clear();
            _materials.Clear();
            _textures.Clear();
        }
    }
}
