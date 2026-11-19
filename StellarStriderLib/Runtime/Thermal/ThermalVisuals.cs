using System;
using System.Collections.Generic;
using System.IO;
using Core.Services;
using FishNet;
using HarmonyLib;
using MelonLoader;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Settings;
using Ships.Services;
using Ships.Visuals.Model;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    internal static class ThermalVisuals
    {
        private const float GlowStart = 350f;
        private const string ShaderResource = "StellarStriderLib.ThermalGlow";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Vector3Int SampleVoxel = new(4, 4, 4);
        private static readonly Dictionary<(ushort, VoxelPartType, PartOrientation, PartOrientation), Mesh[]> HullMeshes = new();
        private static readonly Dictionary<TrackedShipClient, ShipGlow> Ships = new();
        private static Material _material;
        private static Shader _shader;
        private static bool _shaderUnavailable;

        private sealed class ShipGlow
        {
            internal readonly Dictionary<ThermalPartKey, PartGlow> Parts = new();
            internal readonly HashSet<ThermalPartKey> Visible = new();
            internal readonly List<ThermalPartKey> Removed = new();
        }

        private sealed class PartGlow
        {
            internal readonly ThermalReading Placement;
            internal readonly List<(Renderer Source, Renderer Overlay)> Renderers = new();
            private readonly MaterialPropertyBlock _properties = new();
            private float _intensity = -1;

            internal PartGlow(ThermalPart part) => Placement = new ThermalReading(part, 0);

            internal void Update(float intensity)
            {
                var changed = intensity != _intensity;
                if (changed)
                {
                    var color = new Color(2f, 1.55f, 0.08f, 0.85f * intensity);
                    _properties.SetColor(BaseColor, color);
                    _intensity = intensity;
                }

                foreach (var pair in Renderers)
                {
                    if (pair.Overlay == null) continue;
                    if (pair.Source != null) pair.Overlay.enabled = pair.Source.enabled;
                    if (changed) pair.Overlay.SetPropertyBlock(_properties);
                    if (pair.Source is SkinnedMeshRenderer source && pair.Overlay is SkinnedMeshRenderer overlay)
                    {
                        for (var i = 0; i < source.sharedMesh.blendShapeCount; i++)
                            overlay.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
                    }
                }
            }

            internal void Destroy()
            {
                foreach (var pair in Renderers)
                {
                    if (pair.Overlay == null) continue;
                    pair.Overlay.gameObject.SetActive(false);
                    Object.Destroy(pair.Overlay.gameObject);
                }
            }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(TrackedShipClient), nameof(TrackedShipClient.UpdateVisuals)),
                postfix: new HarmonyMethod(typeof(ThermalVisuals), nameof(Update)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(TrackedShipClient), nameof(TrackedShipClient.UnloadVisuals)),
                prefix: new HarmonyMethod(typeof(ThermalVisuals), nameof(Unload)));
        }

        private static void Update(TrackedShipClient __instance)
        {
            var ship = __instance;
            if (!ContentSession.Ready || !ship.IsVisualLoaded || _shaderUnavailable) return;
            var server = InstanceFinder.NetworkManager?.IsServerStarted == true;
            var keys = server ? ThermalRuntime.VisualKeys(ship.Id) : ThermalNetwork.ForShip(ship.Id);
            Ships.TryGetValue(ship, out var glow);
            glow?.Visible.Clear();
            foreach (var key in keys)
            {
                if (!ThermalPart.TryFind(ship, key, out var part)) continue;
                var temperature = ThermalRuntime.Read(ship, part, server);
                var capacity = part.Profile.Capacity;
                var start = Mathf.Min(GlowStart, (capacity + Core.ThermalModel.Ambient) * 0.5f);
                var intensity = Mathf.InverseLerp(start, capacity, temperature);
                if (intensity <= 0) continue;

                if (glow == null)
                    Ships.Add(ship, glow = new ShipGlow());
                if (glow.Parts.TryGetValue(key, out var overlay) && !overlay.Placement.Matches(part))
                {
                    overlay.Destroy();
                    glow.Parts.Remove(key);
                    overlay = null;
                }

                if (overlay == null)
                {
                    if (!EnsureMaterial()) break;
                    overlay = CreateOverlay(ship, part);
                    if (overlay == null) continue;
                    glow.Parts.Add(key, overlay);
                }

                glow.Visible.Add(key);
                overlay.Update(intensity);
            }

            if (glow == null) return;
            foreach (var pair in glow.Parts)
            {
                if (glow.Visible.Contains(pair.Key)) continue;
                pair.Value.Destroy();
                glow.Removed.Add(pair.Key);
            }
            foreach (var key in glow.Removed) glow.Parts.Remove(key);
            glow.Removed.Clear();
            if (glow.Parts.Count == 0) Ships.Remove(ship);
        }

        internal static void RemovePart(TrackedShipClient ship, ThermalPartKey key)
        {
            if (Ships.TryGetValue(ship, out var glow) && glow.Parts.Remove(key, out var part)) part.Destroy();
        }

        private static void Unload(TrackedShipClient __instance)
        {
            if (!Ships.Remove(__instance, out var glow)) return;
            foreach (var part in glow.Parts.Values) part.Destroy();
        }

        private static PartGlow CreateOverlay(TrackedShipClient ship, ThermalPart part)
        {
            var glow = new PartGlow(part);
            if (part.Hull is { } hull)
            {
                AddHull(glow, ship.VisualObject.transform, hull.HullSettings, hull.HullPlacement);
            }
            else if (ship.TryGetFloatingPartVisual(part.Stateful.Id, out var visual) && visual?.GameObject != null)
            {
                var root = visual.GameObject;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || !filter.TryGetComponent<MeshRenderer>(out var source)) continue;
                    if (source.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    var renderer = MeshOverlay(filter.transform, filter.sharedMesh, Vector3.zero);
                    glow.Renderers.Add((source, renderer));
                }

                foreach (var source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (source.sharedMesh == null || source.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    var renderer = CreateObject(source.transform).AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = source.sharedMesh;
                    renderer.bones = source.bones;
                    renderer.rootBone = source.rootBone;
                    renderer.localBounds = source.localBounds;
                    Configure(renderer, source.sharedMesh);
                    glow.Renderers.Add((source, renderer));
                }

                if (part.Settings is HullPartSettings { hasItsOwnMesh: false } settings &&
                    part.Stateful.Placement is HullPartPlacement placement)
                {
                    foreach (var coord in placement.CalculateAllCoords(settings.size))
                        AddHull(glow, ship.VisualObject.transform, settings, new HullPartPlacement(
                            coord, placement.Position.Type, placement.ForwardOrientation, placement.UpOrientation));
                }
            }

            return glow.Renderers.Count != 0 ? glow : null;
        }

        private static void AddHull(PartGlow glow, Transform parent, HullPartSettings settings, HullPartPlacement placement)
        {
            var key = (settings.id, placement.Position.Type, placement.ForwardOrientation, placement.UpOrientation);
            if (!HullMeshes.TryGetValue(key, out var meshes))
            {
                var builder = ServiceLocator.GetService<HullMeshBuilder>();
                if (builder == null) return;
                var sample = new HullPart(settings, new HullPartPlacement(SampleVoxel, placement.Position.Type,
                    placement.ForwardOrientation, placement.UpOrientation));
                var generated = builder.BuildChunkMesh(Vector3Int.zero, new[] { sample }, Array.Empty<HullPart>());
                var surfaces = new List<Mesh>();
                foreach (var pair in generated)
                {
                    var mesh = pair.Value.Mesh;
                    if (pair.Key.Type == HullChunkMeshSliceType.Edge)
                        Object.Destroy(mesh);
                    else
                        surfaces.Add(mesh);
                }
                HullMeshes.Add(key, meshes = surfaces.ToArray());
            }

            var offset = (Vector3)(placement.Position.VoxelCoord - SampleVoxel);
            foreach (var mesh in meshes)
                glow.Renderers.Add((null, MeshOverlay(parent, mesh, offset)));
        }

        private static GameObject CreateObject(Transform parent)
        {
            var overlay = new GameObject("SSLib heat glow") { layer = parent.gameObject.layer };
            overlay.transform.SetParent(parent, false);
            return overlay;
        }

        private static MeshRenderer MeshOverlay(Transform parent, Mesh mesh, Vector3 offset)
        {
            var overlay = CreateObject(parent);
            overlay.transform.localPosition = offset;
            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = overlay.AddComponent<MeshRenderer>();
            Configure(renderer, mesh);
            return renderer;
        }

        private static void Configure(Renderer renderer, Mesh mesh)
        {
            var materials = new Material[mesh.subMeshCount];
            Array.Fill(materials, _material);
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (_shader == null) _shader = LoadShader();
            if (_shader == null || !_shader.isSupported)
            {
                _shaderUnavailable = true;
                MelonLogger.Warning("SSLib could not load the part heat glow shader.");
                return false;
            }

            _material = new Material(_shader)
            {
                name = "SSLib heat glow",
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private static Shader LoadShader()
        {
            using var resource = typeof(ThermalVisuals).Assembly.GetManifestResourceStream(ShaderResource);
            if (resource == null) return null;
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            var bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
            if (bundle == null) return null;
            try { return bundle.LoadAsset<Shader>("assets/thermalglow.shader"); }
            finally { bundle.Unload(false); }
        }

        internal static void Reset()
        {
            foreach (var ship in Ships.Values)
            foreach (var part in ship.Parts.Values)
                part.Destroy();
            Ships.Clear();

            foreach (var meshes in HullMeshes.Values)
            foreach (var mesh in meshes)
                Object.Destroy(mesh);
            HullMeshes.Clear();

            if (_material != null) Object.Destroy(_material);
            _material = null;
            _shaderUnavailable = false;
        }

        internal static void Shutdown()
        {
            if (_shader != null) Resources.UnloadAsset(_shader);
            _shader = null;
        }
    }
}
