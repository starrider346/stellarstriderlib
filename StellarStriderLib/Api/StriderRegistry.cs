using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Items.Model;
using Research.Interface.Values;
using Ships.Interface.Settings;
using StellarStriderLib.Core;
using StellarStriderLib.Models;
using StellarStriderLib.Runtime;

namespace StellarStriderLib
{
    /// <summary>Associates a registered definition with its owning mod, content key and grouping metadata.</summary>
    internal sealed class Entry<T>
    {
        internal ModRegistry Owner;
        internal string Key, Group;
        internal T Definition;
        internal bool Fluid, Gas;
    }

    /// <summary>Shared registration entrypoint. Register definitions during mod initialization.</summary>
    public static class StriderRegistry
    {
        internal static readonly SortedDictionary<string, ModRegistry> Mods = new(StringComparer.Ordinal);
        internal static readonly Dictionary<string, Entry<CategoryDefinition>> Categories = new(StringComparer.Ordinal);
        internal static readonly Dictionary<string, Entry<PartDefinition>> Parts = new(StringComparer.Ordinal);
        internal static readonly Dictionary<string, Entry<ItemDefinition>> Items = new(StringComparer.Ordinal);

        internal static readonly Dictionary<string, Entry<LoadingWallpaperDefinition>> Wallpapers =
            new(StringComparer.Ordinal);

        internal static readonly Dictionary<string, Entry<LoadingStepDefinition>> LoadingSteps =
            new(StringComparer.Ordinal);

        internal static readonly Dictionary<string, Entry<ResearchNodeDefinition>> Research =
            new(StringComparer.Ordinal);

        internal static bool Frozen;

        public const int RegistryProtocolVersion = 3;

        public static string LastError => Plugin.LastError;
        public static bool WorldReady => ContentSession.Ready;

        /// <summary>Creates or retrieves a mod-scoped registry. An identifier may belong to only one assembly.</summary>
        public static ModRegistry Create(string modId, Assembly assembly)
        {
            MainThread();
            Keys.Identifier(modId);
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (Mods.TryGetValue(modId, out var existing))
            {
                return existing.Assembly == assembly
                    ? existing
                    : throw new ArgumentException("Mod identifier already owned: " + modId);
            }

            Mutable();
            var result = new ModRegistry(modId, assembly);
            Mods.Add(modId, result);
            return result;
        }

        internal static void MainThread() => Plugin.CheckThread();

        internal static void Mutable()
        {
            MainThread();
            if (Frozen)
            {
                throw new InvalidOperationException(
                    "Register content during mod initialization, before a world starts.");
            }
        }

        internal static IReadOnlyDictionary<ContentKind, string[]> Definitions() =>
            new Dictionary<ContentKind, string[]>
            {
                [ContentKind.Part] = ContentSession.Parts.Select(part => part.Key).ToArray(),
                [ContentKind.Item] = Items.Keys.ToArray(),
                [ContentKind.Research] = Research.Keys.ToArray()
            };

        internal static uint Number(ContentKind kind, ContentReference reference)
        {
            if (reference.IsNative)
            {
                if (reference.NativeId > WorldRegistry.Maximum(kind))
                {
                    throw new ArgumentOutOfRangeException(nameof(reference));
                }

                return reference.NativeId;
            }

            return ContentSession.Registry?.Resolve(kind, reference.Key) ??
                   throw new InvalidOperationException("No active world registry.");
        }

        /// <summary>Tries to find a part in the ready world; unknown references return false.</summary>
        public static bool TryGetPart(PartRef reference, out PartSettings settings)
        {
            settings = null;
            return TryNumber(ContentKind.Part, reference.Value, out var id) &&
                   ContentSession.PartSettings.TryGetValue(id, out settings);
        }

        /// <summary>Tries to find an item in the ready world; unknown references return false.</summary>
        public static bool TryGetItem(ItemRef reference, out ItemSettings settings)
        {
            settings = null;
            return TryNumber(ContentKind.Item, reference.Value, out var id) &&
                   ContentSession.ItemSettings.TryGetValue(id, out settings);
        }

        /// <summary>Tries to find a research node in the ready world; unknown references return false.</summary>
        public static bool TryGetResearch(ResearchRef reference, out ResearchSettings settings)
        {
            settings = null;
            return TryNumber(ContentKind.Research, reference.Value, out var id) &&
                   ContentSession.ResearchSettings.TryGetValue(id, out settings);
        }

        /// <summary>Tries to resolve a reference using the active world mapping; returns false before the world is ready.</summary>
        public static bool TryGetNumericId(PartRef reference, out ushort id)
        {
            id = 0;
            if (!TryNumber(ContentKind.Part, reference.Value, out var numericId))
            {
                return false;
            }

            id = (ushort)numericId;
            return true;
        }

        /// <summary>Tries to resolve a reference using the active world mapping; returns false before the world is ready.</summary>
        public static bool TryGetNumericId(ItemRef reference, out uint id) =>
            TryNumber(ContentKind.Item, reference.Value, out id);

        /// <summary>Tries to resolve a reference using the active world mapping; returns false before the world is ready.</summary>
        public static bool TryGetNumericId(ResearchRef reference, out ushort id)
        {
            id = 0;
            if (!TryNumber(ContentKind.Research, reference.Value, out var numericId))
            {
                return false;
            }

            id = (ushort)numericId;
            return true;
        }

        private static bool TryNumber(ContentKind kind, ContentReference reference, out uint id)
        {
            MainThread();
            id = 0;
            if (!ContentSession.Ready)
            {
                return false;
            }

            if (reference.IsNative)
            {
                id = reference.NativeId;
                return id <= WorldRegistry.Maximum(kind);
            }

            return ContentSession.Registry.Map(kind).TryGetValue(reference.Key, out id);
        }
    }

    /// <summary>Owns content keys and embedded assets contributed by one mod.</summary>
    public sealed class ModRegistry
    {
        public string ModId { get; }

        public Assembly Assembly { get; }

        public AssetScope Assets { get; }

        /// <summary>Called after this world's settings and caches are ready.</summary>
        public event Action WorldBound;

        /// <summary>Called before world-owned prototypes are destroyed, including failed builds.</summary>
        public event Action WorldUnbinding;

        internal void NotifyBound()
        {
            try
            {
                WorldBound?.Invoke();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("World binding failed for " + ModId + ": " + error.Message, error);
            }
        }

        internal void NotifyUnbinding()
        {
            foreach (var handler in WorldUnbinding?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception error)
                {
                    MelonLoader.MelonLogger.Error("Cleanup failed for " + ModId + ": " + error);
                }
            }
        }

        private readonly HashSet<string> _trees = new(StringComparer.Ordinal);

        internal ModRegistry(string id, Assembly assembly)
        {
            ModId = id;
            Assembly = assembly;
            Assets = new AssetScope(assembly);
        }

        /// <summary>Combines this mod identifier with a local identifier.</summary>
        public string Key(string localId) => ModId + ":" + Keys.Identifier(localId);

        internal string CategoryKey(string id)
        {
            if (id == null)
            {
                throw new ArgumentException("A part category is required.");
            }

            if (!id.Contains(':'))
            {
                return Key(id);
            }

            Keys.Owner(id);
            return id;
        }

        private Entry<T> Add<T>(Dictionary<string, Entry<T>> entries, string id, T definition)
        {
            StriderRegistry.Mutable();
            var key = Key(id);
            if (entries.ContainsKey(key))
            {
                throw new ArgumentException("Content key already registered: " + key);
            }

            var entry = new Entry<T> { Owner = this, Key = key, Definition = definition };
            entries.Add(key, entry);
            return entry;
        }

        /// <summary>Registers a reusable client-local loading step. Not included in save IDs or network checks.</summary>
        public void RegisterLoadingStep(LoadingStepDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var snapshot = definition.Snapshot();
            Add(StriderRegistry.LoadingSteps, snapshot.Id, snapshot);
        }

        /// <summary>Registers an embedded loading wallpaper. Client-local; not included in save IDs or network checks.</summary>
        public void RegisterLoadingWallpaper(LoadingWallpaperDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.TextureResource))
            {
                throw new ArgumentException("Wallpaper image resource is required.");
            }

            Add(StriderRegistry.Wallpapers, definition.Id, new LoadingWallpaperDefinition
            {
                Id = definition.Id, TextureResource = definition.TextureResource
            });
        }

        /// <summary>Registers a build-menu category owned by this mod.</summary>
        public void RegisterCategory(CategoryDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new ArgumentException("Category name is required.");
            }

            Add(StriderRegistry.Categories, definition.Id, definition);
        }

        /// <summary>Registers a donor-based part. Numeric IDs are assigned by the active save.</summary>
        public void RegisterPart(PartDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name) ||
                string.IsNullOrWhiteSpace(definition.Donor))
            {
                throw new ArgumentException("Part name and donor are required.");
            }

            CategoryKey(definition.Category);
            if (definition.Size.HasValue && (!Finite(definition.Size.Value.x) || !Finite(definition.Size.Value.y) ||
                                             !Finite(definition.Size.Value.z) || definition.Size.Value.x <= 0 ||
                                             definition.Size.Value.y <= 0 || definition.Size.Value.z <= 0))
            {
                throw new ArgumentException("Part size must be positive.");
            }

            if (definition.Mass.HasValue && (!Finite(definition.Mass.Value) || definition.Mass.Value <= 0))
            {
                throw new ArgumentException("Part mass must be finite and positive.");
            }

            if (definition.Sockets != null &&
                definition.Sockets.Select(s => s.Id).Distinct().Count() != definition.Sockets.Length)
            {
                throw new ArgumentException("Duplicate socket IDs.");
            }

            PartShapes.Validate(definition);
            ThermalModel.ValidateProfile(definition.ThermalCapacityKelvin, definition.ThermalConductivity);
            StateFields.ValidateDefinition(definition.State);
            CheckCosts(definition.BuildingCost);
            Add(StriderRegistry.Parts, definition.Id, definition);
        }

        /// <summary>Registers a stateless hull using a native hull donor for snapping and room sealing.</summary>
        public void RegisterHullPart(PartDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            var key = Key(definition.Id);
            HullParts.Validate(key, definition);
            RegisterPart(definition);
            HullParts.Register(key);
        }

        /// <summary>Registers a solid inventory item in the save-owned item namespace.</summary>
        public void RegisterItem(ItemDefinition definition) => Item(definition, false, false);

        /// <summary>Registers an item with the native fluid flag and fluid color.</summary>
        public void RegisterFluid(FluidDefinition definition) => Item(definition, true, false);

        /// <summary>Registers an item with both native fluid and gas flags.</summary>
        public void RegisterGas(FluidDefinition definition) => Item(definition, true, true);

        private void Item(ItemDefinition definition, bool fluid, bool gas)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name) || definition.MaxStackSize == 0 ||
                !Finite(definition.Mass) || definition.Mass <= 0)
            {
                throw new ArgumentException("Item requires a name, positive mass and stack size.");
            }

            var entry = Add(StriderRegistry.Items, definition.Id, definition);
            entry.Fluid = fluid;
            entry.Gas = gas;
        }

        /// <summary>Atomically registers a tree of nodes grouped by mod and tree identifier.</summary>
        public void RegisterResearchTree(ResearchTreeDefinition definition)
        {
            StriderRegistry.Mutable();
            if (definition == null || definition.Nodes == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var group = Key(definition.Id);
            if (_trees.Contains(group))
            {
                throw new ArgumentException("Research tree already registered: " + group);
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in definition.Nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Name))
                {
                    throw new ArgumentException("Research node requires a name.");
                }

                var key = Key(node.Id);
                if (!keys.Add(key) || StriderRegistry.Research.ContainsKey(key))
                {
                    throw new ArgumentException("Research key already registered: " + key);
                }

                CheckCosts(node.Costs);
            }

            foreach (var node in definition.Nodes)
            {
                Add(StriderRegistry.Research, node.Id, node).Group = group;
            }

            _trees.Add(group);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void CheckCosts(ItemCost[] costs)
        {
            if (costs != null && costs.Any(c => c == null || c.Quantity <= 0))
            {
                throw new ArgumentException("Costs must have positive quantities.");
            }
        }
    }
}
