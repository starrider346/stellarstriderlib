using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Items.Model;
using Research.Interface.Values;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Settings;
using Ships.Parts.Common;
using Ships.Parts.Common.Model.Reactions;
using Ships.Parts.Gyroscope;
using StellarStriderLib.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    /// <summary>Builds and owns the active world’s content prefabs, settings and numeric lookups.</summary>
    internal static class ContentSession
    {
        internal static WorldRegistry Registry { get; private set; }

        internal static bool Ready { get; private set; }

        internal static readonly Dictionary<uint, PartSettings> PartSettings = new();
        internal static readonly Dictionary<uint, ItemSettings> ItemSettings = new();
        internal static readonly Dictionary<uint, ResearchSettings> ResearchSettings = new();
        internal static readonly Dictionary<SocketObject, Vector3> SocketPositions = new();
        internal static readonly Dictionary<ushort, PartSettings> Donors = new();
        private static readonly List<Object> Owned = new();
        private static GameObject _holder;

        internal static HashSet<uint> OwnedIds(ContentKind kind) =>
            new(Registry?.Map(kind).Values ?? Enumerable.Empty<uint>());

        internal static IEnumerable<uint> Occupied(ContentKind kind)
        {
            if (kind == ContentKind.Part)
            {
                return Resources.FindObjectsOfTypeAll<PartSettings>().Select(s => (uint)s.id).ToArray();
            }

            if (kind == ContentKind.Item)
            {
                return Resources.FindObjectsOfTypeAll<ItemSettings>().Select(s => s.id).ToArray();
            }

            return Resources.FindObjectsOfTypeAll<ResearchSettingsList>()
                .SelectMany(l => l.list ?? new List<ResearchSettings>()).Where(n => n != null).Select(n => (uint)n.id)
                .Distinct().ToArray();
        }

        internal static void PrimeExternal()
        {
            new Ships.Interface.Services.PartSettingsProvider().GetAllPartSettings();
            foreach (var list in Resources.FindObjectsOfTypeAll<ItemSettingsList>())
            {
                var items = (ItemSettings[])AccessTools.Field(typeof(ItemSettingsList), "items").GetValue(list);
                if (items != null && items.Length > 0)
                {
                    list.GetItemSettingsById(items[0].id);
                }
            }

            foreach (var list in Resources.FindObjectsOfTypeAll<ResearchSettingsList>())
            {
                if (list.list != null && list.list.Count > 0)
                {
                    list.GetResearchSettingById(list.list[0].id);
                }
            }
        }

        internal static WorldRegistry Prepare(WorldRegistry registry)
        {
            End();
            PrimeExternal();
            StriderRegistry.Frozen = true;
            registry.Bind(StriderRegistry.Definitions(), StriderRegistry.Mods.Keys, Occupied);
            Bind(registry);
            return registry;
        }

        internal static void Bind(WorldRegistry registry)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(registry.Serialize()) > RegistryTransfer.MaximumBytes)
            {
                throw new InvalidOperationException("Registry exceeds the 4 MiB session limit.");
            }

            Registry = registry;
            try
            {
                foreach (var item in Resources.FindObjectsOfTypeAll<ItemSettings>())
                {
                    ItemSettings.TryAdd(item.id, item);
                }

                foreach (var part in Resources.FindObjectsOfTypeAll<PartSettings>())
                {
                    PartSettings.TryAdd(part.id, part);
                }

                foreach (var list in Resources.FindObjectsOfTypeAll<ResearchSettingsList>())
                {
                    foreach (var node in list.list ?? new List<ResearchSettings>())
                    {
                        if (node != null)
                        {
                            ResearchSettings.TryAdd(node.id, node);
                        }
                    }
                }

                ValidateReferences();
                _holder = new GameObject("StellarStriderLib prefabs") { hideFlags = HideFlags.HideAndDontSave };
                _holder.SetActive(false);
                _holder.transform.position = new Vector3(0, -100000, 0);
                Object.DontDestroyOnLoad(_holder);
                foreach (var entry in StriderRegistry.Items.Values.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    BuildItem(entry);
                }

                foreach (var entry in StriderRegistry.Parts.Values.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    BuildPart(entry);
                }

                BuildResearch();
                Ready = true;
                PartRuntime.Bind();
                GameHooks.RefreshProviders();
                foreach (var mod in StriderRegistry.Mods.Values)
                {
                    mod.NotifyBound();
                }
            }
            catch
            {
                End();
                throw;
            }
        }

        private static void ValidateReferences()
        {
            void ValidateReference(ContentKind kind, ContentReference reference)
            {
                var id = StriderRegistry.Number(kind, reference);
                var exists = !reference.IsNative || (kind == ContentKind.Part ? PartSettings.ContainsKey(id) :
                    kind == ContentKind.Item ? ItemSettings.ContainsKey(id) : ResearchSettings.ContainsKey(id));
                if (!exists)
                {
                    throw new InvalidOperationException("Missing native " + kind + ": " + id);
                }
            }

            void ValidateCosts(ItemCost[] costs)
            {
                foreach (var cost in costs ?? Array.Empty<ItemCost>())
                {
                    ValidateReference(ContentKind.Item, cost.Item.Value);
                }
            }

            foreach (var entry in StriderRegistry.Parts.Values)
            {
                if (!StriderRegistry.Categories.ContainsKey(entry.Owner.CategoryKey(entry.Definition.Category)))
                {
                    throw new InvalidOperationException("Missing category for " + entry.Key);
                }

                ValidateCosts(entry.Definition.BuildingCost);
            }

            foreach (var entry in StriderRegistry.Research.Values)
            {
                ValidateCosts(entry.Definition.Costs);
                foreach (var part in entry.Definition.UnlockedParts)
                {
                    ValidateReference(ContentKind.Part, part.Value);
                }

                foreach (var parent in entry.Definition.Prerequisites)
                {
                    ValidateReference(ContentKind.Research, parent.Value);
                }
            }
        }

        private static T Own<T>(T value) where T : Object
        {
            Owned.Add(value);
            return value;
        }

        private static GameObject Root(string name)
        {
            var root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetParent(_holder.transform, false);
            return root;
        }

        private static void BuildItem(Entry<ItemDefinition> entry)
        {
            var definition = entry.Definition;
            var item = Own(ScriptableObject.CreateInstance<ItemSettings>());
            item.hideFlags = HideFlags.HideAndDontSave;
            item.id = Registry.Resolve(ContentKind.Item, entry.Key);
            item.name = entry.Key;
            item.itemName = definition.Name;
            item.mass = definition.Mass;
            item.maxStackSize = definition.MaxStackSize;
            item.isFluid = entry.Fluid;
            item.isGas = entry.Gas;
            if (definition is FluidDefinition fluid)
            {
                item.fluidColor = fluid.Color;
            }

            item.itemObject = Root(entry.Key);
            foreach (var model in definition.Models ?? Array.Empty<Models.MeshDefinition>())
            {
                entry.Owner.Assets.AddMesh(item.itemObject.transform, model);
            }

            ItemSettings.Add(item.id, item);
            definition.Configure?.Invoke(new ItemBuildContext(item, entry.Owner.Assets));
            if (item.id != Registry.Resolve(ContentKind.Item, entry.Key))
            {
                throw new InvalidOperationException("Configure must not change numeric IDs: " + entry.Key);
            }
        }

        private static void BuildPart(Entry<PartDefinition> entry)
        {
            var definition = entry.Definition;
            var donor = PartSettings.Values.FirstOrDefault(p => p.name == definition.Donor) ??
                        throw new InvalidOperationException("Missing part donor for " + entry.Key + ": " +
                                                            definition.Donor);
            var settings = Own(Object.Instantiate(donor));
            settings.part = Object.Instantiate(donor.part, _holder.transform, false);
            settings.name = settings.part.name = entry.Key;
            settings.hideFlags = settings.part.hideFlags = HideFlags.HideAndDontSave;
            settings.id = (ushort)Registry.Resolve(ContentKind.Part, entry.Key);
            settings.fullLabel = definition.Name;
            settings.description = definition.Description;
            settings.localizedDescription = null;
            if (definition.Size.HasValue)
            {
                Resize(settings, definition.Size.Value);
            }

            if (definition.Mass.HasValue)
            {
                settings.mass = definition.Mass.Value;
            }

            if (definition.BuildingCost != null)
            {
                AccessTools.Field(typeof(PartSettings), "buildingCost")
                    .SetValue(settings, Costs(definition.BuildingCost).ToList());
            }

            if (definition.Models != null)
            {
                ReplaceModels(entry, settings);
            }

            if (definition.Sockets != null)
            {
                ReplaceSockets(definition.Sockets, settings.part);
            }

            PartShapes.Configure(definition, settings.part);
            definition.Configure?.Invoke(new PartBuildContext(settings, entry.Owner.Assets));
            if (settings.id != Registry.Resolve(ContentKind.Part, entry.Key))
            {
                throw new InvalidOperationException("Configure must not change numeric IDs: " + entry.Key);
            }

            PartMovement.Configure(definition, settings.part);
            foreach (var socket in settings.part.GetComponentsInChildren<SocketObject>(true))
            {
                SocketPositions.Add(socket, settings.part.transform.InverseTransformPoint(socket.transform.position));
            }

            PartSettings.Add(settings.id, settings);
            Donors.Add(settings.id, donor);
        }

        private static void Resize(PartSettings settings, Vector3 size)
        {
            var originalSize = settings.size;
            var scale = new Vector3(size.x / originalSize.x, size.y / originalSize.y, size.z / originalSize.z);
            var root = settings.part.transform;
            foreach (var collider in settings.part.GetComponentsInChildren<BoxCollider>(true))
            {
                if (collider.GetComponentInParent<SocketObject>() != null)
                {
                    continue;
                }

                collider.center = collider.transform.InverseTransformPoint(root.TransformPoint(
                    Vector3.Scale(root.InverseTransformPoint(collider.transform.TransformPoint(collider.center)),
                        scale)));
                collider.size = Vector3.Scale(collider.size, scale);
            }

            foreach (var socket in settings.part.GetComponentsInChildren<SocketObject>(true))
            {
                socket.transform.position =
                    root.TransformPoint(Vector3.Scale(root.InverseTransformPoint(socket.transform.position), scale));
            }

            var bounds = settings.part.GetComponent<Ships.Interface.Model.Parts.ShipPartBounds>();
            if (bounds != null)
            {
                bounds.center = Vector3.Scale(bounds.center, scale);
                bounds.bounds = Vector3.Scale(bounds.bounds, scale);
            }

            settings.size = size;
        }

        private static void ReplaceModels(Entry<PartDefinition> entry, PartSettings settings)
        {
            var prefab = settings.part;
            var visual = prefab.GetComponentInChildren<IShipPartVisual>(true) ??
                         throw new InvalidOperationException("Donor has no visuals: " + entry.Key);
            var root = visual.GameObject;
            foreach (var child in root.transform.Cast<Transform>().ToArray())
            {
                Object.DestroyImmediate(child.gameObject);
            }

            foreach (var component in root.GetComponents<Component>())
            {
                // The donor's stick transforms were removed with its original model.
                if (component is IShipPartVisual || component is Renderer || component is MeshFilter ||
                    component is FlightSticksAnimator)
                {
                    Object.DestroyImmediate(component);
                }
            }

            root.AddComponent<DefaultShipPartVisuals>();
            var interaction = prefab.GetComponentInChildren<PartInteraction>(true);
            var model = new GameObject(interaction == null ? "Model" : interaction.name) { layer = root.layer };
            model.transform.SetParent(root.transform, false);
            var context = new PartBuildContext(settings, entry.Owner.Assets);
            foreach (var mesh in entry.Definition.Models)
            {
                var child = entry.Owner.Assets.AddMesh(model.transform, mesh);
                entry.Definition.ConfigureMesh?.Invoke(context, child, mesh);
            }
        }

        private static void ReplaceSockets(SocketDefinition[] sockets, GameObject prefab)
        {
            var native = Resources.FindObjectsOfTypeAll<PartSettings>().Where(p => !Owned.Contains(p) && p.part != null)
                .SelectMany(p => p.part.GetComponentsInChildren<SocketObject>(true)).GroupBy(s => s.Type)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var socket in prefab.GetComponentsInChildren<SocketObject>(true))
            {
                Object.DestroyImmediate(socket.gameObject);
            }

            foreach (var definition in sockets)
            {
                if (!native.TryGetValue(definition.Type, out var template))
                {
                    throw new InvalidOperationException("Socket template unavailable: " + definition.Type);
                }

                var socket = Object.Instantiate(template, prefab.transform, false);
                socket.transform.localPosition = definition.Position;
                socket.transform.localRotation = Quaternion.Euler(definition.Rotation);
                AccessTools.Field(typeof(SocketObject), "id").SetValue(socket, definition.Id);
                AccessTools.Field(typeof(SocketObject), "relayedSockets").SetValue(socket, Array.Empty<SocketObject>());
                socket.shouldCreateFluidBufferWhenTouchingAnotherSocket =
                    definition.Type == Ships.Interface.Model.Parts.CableType.Fluid;
            }
        }

        internal static ItemInstance[] Costs(ItemCost[] costs) => (costs ?? Array.Empty<ItemCost>()).Select(c =>
                new ItemInstance(ItemSettings[StriderRegistry.Number(ContentKind.Item, c.Item.Value)], c.Quantity))
            .ToArray();

        internal static void Relayout(IEnumerable<ResearchSettings> source)
        {
            var existing = source.Where(n => n != null && !Owned.Contains(n)).ToArray();
            var fixedNodes = existing.ToDictionary(n => "native:" + n.id,
                n => new GridPoint(n.positionInTechTree.x, n.positionInTechTree.y));
            var layout = ResearchLayout.Arrange(
                StriderRegistry.Research.Values.Select(e => new LayoutNode
                {
                    Key = e.Key, Group = e.Group,
                    Parents = e.Definition.Prerequisites
                        .Select(p => p.Value.IsNative ? "native:" + p.Value.NativeId : p.Value.Key).ToArray()
                }).Concat(existing.Select(n => new LayoutNode
                {
                    Key = "native:" + n.id,
                    Parents = (n.requirements ?? Array.Empty<ResearchSettings>())
                        .Where(p => p != null).Select(p => "native:" + p.id).ToArray()
                })), fixedNodes);
            foreach (var entry in layout)
            {
                ResearchSettings[Registry.Resolve(ContentKind.Research, entry.Key)].positionInTechTree =
                    new Vector2Int(entry.Value.X, entry.Value.Y);
            }
        }

        private static void BuildResearch()
        {
            var fixedNodes = ResearchSettings.Values.ToDictionary(n => "native:" + n.id,
                n => new GridPoint(n.positionInTechTree.x, n.positionInTechTree.y), StringComparer.Ordinal);

            string Parent(ContentReference reference) =>
                reference.IsNative ? "native:" + reference.NativeId : reference.Key;

            var layout =
                ResearchLayout.Arrange(
                    StriderRegistry.Research.Values.Select(e => new LayoutNode
                    {
                        Key = e.Key, Group = e.Group,
                        Parents = e.Definition.Prerequisites.Select(p => Parent(p.Value)).ToArray()
                    }).Concat(ResearchSettings.Values.Select(n => new LayoutNode
                    {
                        Key = "native:" + n.id,
                        Parents = (n.requirements ?? Array.Empty<ResearchSettings>())
                            .Where(p => p != null).Select(p => "native:" + p.id).ToArray()
                    })), fixedNodes);
            foreach (var entry in StriderRegistry.Research.Values)
            {
                var definition = entry.Definition;
                var node = Own(ScriptableObject.CreateInstance<ResearchSettings>());
                node.hideFlags = HideFlags.HideAndDontSave;
                node.name = entry.Key;
                node.id = (ushort)Registry.Resolve(ContentKind.Research, entry.Key);
                node.techName = definition.Name;
                node.availableInDemo = definition.AvailableInDemo;
                node.exclusiveToDemo = definition.ExclusiveToDemo;
                node.unlockedParts = definition.UnlockedParts
                    .Select(p => PartSettings[StriderRegistry.Number(ContentKind.Part, p.Value)]).ToArray();
                node.resourceCost = Costs(definition.Costs);
                node.unlockedFeatures = Array.Empty<Research.Interface.Model.ResearchUnlockableFeature>();
                node.unlockedGuideEntriesIds = Array.Empty<uint>();
                node.positionInTechTree = new Vector2Int(layout[entry.Key].X, layout[entry.Key].Y);
                ResearchSettings.Add(node.id, node);
            }

            foreach (var entry in StriderRegistry.Research.Values)
            {
                ResearchSettings[Registry.Resolve(ContentKind.Research, entry.Key)].requirements = entry.Definition
                    .Prerequisites.Select(p => ResearchSettings[StriderRegistry.Number(ContentKind.Research, p.Value)])
                    .ToArray();
            }

            if (Donors.Count == 0)
            {
                return;
            }

            var ownedParts = new HashSet<ushort>(Donors.Keys);
            var explicitUnlocks = new HashSet<ushort>(StriderRegistry.Research.Keys
                .SelectMany(key => ResearchSettings[Registry.Resolve(ContentKind.Research, key)].unlockedParts)
                .Select(p => p.id));
            foreach (var node in ResearchSettings.Values.Where(n => !Owned.Contains(n)))
            {
                var parts = (node.unlockedParts ?? Array.Empty<PartSettings>()).Where(p =>
                    p == null || !ownedParts.Contains(p.id) || !explicitUnlocks.Contains(p.id)).ToList();
                foreach (var pair in Donors)
                {
                    if (!explicitUnlocks.Contains(pair.Key) && parts.Contains(pair.Value) &&
                        !parts.Contains(PartSettings[pair.Key]))
                    {
                        parts.Add(PartSettings[pair.Key]);
                    }
                }

                if (!parts.SequenceEqual(node.unlockedParts ?? Array.Empty<PartSettings>()))
                {
                    node.unlockedParts = parts.ToArray();
                }
            }
        }

        internal static void End()
        {
            Ready = false;
            PartRuntime.Reset();
            PartStateNetwork.Reset();
            if (Registry != null)
            {
                foreach (var mod in StriderRegistry.Mods.Values)
                {
                    mod.NotifyUnbinding();
                }
            }

            GameHooks.RemoveOwned();
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
            }

            _holder = null;
            foreach (var value in Owned)
            {
                if (value != null)
                {
                    Object.DestroyImmediate(value);
                }
            }

            Owned.Clear();
            PartSettings.Clear();
            ItemSettings.Clear();
            ResearchSettings.Clear();
            Donors.Clear();
            SocketPositions.Clear();
            Registry = null;
        }
    }
}
