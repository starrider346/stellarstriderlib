using System.Collections.Generic;
using System.Linq;
using Core.Services;
using HarmonyLib;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Ships.Parts.Common.Model.Reactions;
using Ships.Physics.Collision;
using Ships.Values;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    internal static class ThermalGeometry
    {
        private static readonly AccessTools.FieldRef<TrackedShip, ShipPrefabs> Prefabs =
            AccessTools.FieldRefAccess<TrackedShip, ShipPrefabs>("Prefabs");
        private static readonly AccessTools.FieldRef<ShipHullColliders, ShipHullColliderChunk> ChunkPrefab =
            AccessTools.FieldRefAccess<ShipHullColliders, ShipHullColliderChunk>("chunkColliderPrefab");

        private static readonly Dictionary<(ushort, HullPartPosition, PartOrientation, PartOrientation), ThermalShape[]>
            HullShapes = new();
        private static readonly Dictionary<ushort, ThermalShape[]> StaticShapes = new();
        private static readonly Dictionary<(ushort, bool), ThermalShape[]> DoorShapes = new();
        private static readonly Dictionary<ushort, IShipPartDynamicCollider> DynamicPrefabs = new();
        private static GameObject _staging;

        private static Transform Staging
        {
            get
            {
                if (_staging == null)
                {
                    _staging = new GameObject("SSLib thermal collider templates") { hideFlags = HideFlags.HideAndDontSave };
                    _staging.SetActive(false);
                    Object.DontDestroyOnLoad(_staging);
                }

                return _staging.transform;
            }
        }

        internal static ThermalShape[] For(TrackedShip ship, ThermalPart part)
        {
            if (part.Hull is { } hull)
            {
                var placement = hull.HullPlacement;
                var origin = new Vector3Int(4, 4, 4);
                var normalized = new HullPartPosition(origin, placement.Position.Type);
                var key = (part.Settings.id, normalized, placement.ForwardOrientation, placement.UpOrientation);
                if (!HullShapes.TryGetValue(key, out var hullShapes))
                {
                    var template = Prefabs(ship).shipColliders.GetComponent<ShipHullColliders>();
                    var chunk = Object.Instantiate(ChunkPrefab(template), Staging, false);
                    chunk.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    try
                    {
                        chunk.Initialize(Vector3Int.zero);
                        chunk.AddHullPart(new HullPart(hull.HullSettings,
                            new HullPartPlacement(normalized, placement.ForwardOrientation, placement.UpOrientation)));
                        chunk.UpdateColliders();
                        HullShapes[key] = hullShapes = Read(chunk.gameObject)
                            .Select(shape => shape.Place(-(Vector3)origin, Quaternion.identity)).ToArray();
                    }
                    finally
                    {
                        Object.Destroy(chunk.gameObject);
                    }
                }

                var offset = (Vector3)placement.Position.VoxelCoord;
                return hullShapes.Select(shape => shape.Place(offset, Quaternion.identity)).ToArray();
            }

            var settings = part.Settings;
            var dynamicPrefab = DynamicPrefab(settings);
            ThermalShape[] shapes;
            if (dynamicPrefab != null)
            {
                if (part.Stateful.State is IDoorState door)
                {
                    var key = (settings.id, door.IsOpen);
                    if (!DoorShapes.TryGetValue(key, out shapes))
                        DoorShapes[key] = shapes = ReadDynamic(dynamicPrefab, part.Stateful.State);
                }
                else
                {
                    shapes = ReadDynamic(dynamicPrefab, part.Stateful.State);
                }
            }
            else if (!StaticShapes.TryGetValue(settings.id, out shapes))
            {
                var instancer = ServiceLocator.GetService<IShipPartInstancer>();
                var bounds = instancer.InstantiateBoundsCollider(settings, Staging);
                try { StaticShapes[settings.id] = shapes = Read(bounds.gameObject); }
                finally { Object.Destroy(bounds.gameObject); }
            }

            var position = part.Position;
            var rotation = part.Rotation;
            return shapes.Select(shape => shape.Place(position, rotation)).ToArray();
        }

        internal static bool HasDynamicCollider(PartSettings settings) => DynamicPrefab(settings) != null;

        private static IShipPartDynamicCollider DynamicPrefab(PartSettings settings)
        {
            if (!DynamicPrefabs.TryGetValue(settings.id, out var prefab))
                DynamicPrefabs[settings.id] = prefab = settings.part.GetComponentInChildren<IShipPartDynamicCollider>();
            return prefab;
        }

        private static ThermalShape[] ReadDynamic(IShipPartDynamicCollider prefab, IStatefulPartState state)
        {
            var root = Object.Instantiate(prefab.GameObject, Staging, false);
            root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                root.GetComponent<IShipPartDynamicCollider>().SetNetworkState(state);
                return Read(root);
            }
            finally { Object.Destroy(root); }
        }

        private static ThermalShape[] Read(GameObject root)
        {
            var shapes = new List<ThermalShape>();
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger || !ActiveUnder(collider.transform, root.transform))
                    continue;

                var transform = collider.transform;
                switch (collider)
                {
                    case BoxCollider box:
                        var scale = transform.lossyScale;
                        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        shapes.Add(ThermalShape.Box(transform.TransformPoint(box.center),
                            Vector3.Scale(box.size, scale), transform.rotation));
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null && mesh.sharedMesh.isReadable:
                        var vertices = mesh.sharedMesh.vertices;
                        for (var i = 0; i < vertices.Length; i++) vertices[i] = transform.TransformPoint(vertices[i]);
                        shapes.AddRange(ThermalShape.Mesh(vertices, mesh.sharedMesh.triangles, mesh.convex));
                        break;
                }
            }

            return shapes.ToArray();
        }

        private static bool ActiveUnder(Transform current, Transform root)
        {
            while (current != root)
            {
                if (!current.gameObject.activeSelf) return false;
                current = current.parent;
            }

            return root.gameObject.activeSelf;
        }

        internal static void Reset()
        {
            HullShapes.Clear();
            StaticShapes.Clear();
            DoorShapes.Clear();
            DynamicPrefabs.Clear();
            if (_staging != null) Object.Destroy(_staging);
            _staging = null;
        }
    }
}
