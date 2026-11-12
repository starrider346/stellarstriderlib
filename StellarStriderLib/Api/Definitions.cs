using System;
using Ships.Interface.Model.Parts;
using Ships.Interface.Settings;
using Items.Model;
using UnityEngine;
using StellarStriderLib.Models;
using StellarStriderLib.Core;

namespace StellarStriderLib
{
    /// <summary>A native numeric reference or a namespaced library content key.</summary>
    public readonly struct ContentReference
    {
        public string Key { get; }

        public uint NativeId { get; }

        public bool IsNative => Key == null;

        private ContentReference(string key, uint id)
        {
            Key = key;
            NativeId = id;
        }

        /// <summary>References an existing native numeric identifier.</summary>
        public static ContentReference Native(uint id) => new(null, id);

        /// <summary>References a full, case-sensitive mod:local content key.</summary>
        public static ContentReference Mod(string key)
        {
            Keys.Owner(key);
            return new ContentReference(key, 0);
        }
    }

    /// <summary>A typed reference to a native or namespaced part.</summary>
    public readonly struct PartRef
    {
        internal ContentReference Value { get; }

        private PartRef(ContentReference value)
        {
            Value = value;
        }

        /// <summary>References an existing native numeric identifier.</summary>
        public static PartRef Native(ushort id) => new(ContentReference.Native(id));

        /// <summary>References a full, case-sensitive mod:local content key.</summary>
        public static PartRef Mod(string key) => new(ContentReference.Mod(key));
    }

    /// <summary>A typed reference to a native or namespaced item, fluid or gas.</summary>
    public readonly struct ItemRef
    {
        internal ContentReference Value { get; }

        private ItemRef(ContentReference value)
        {
            Value = value;
        }

        /// <summary>References an existing native numeric identifier.</summary>
        public static ItemRef Native(uint id) => new(ContentReference.Native(id));

        /// <summary>References a full, case-sensitive mod:local content key.</summary>
        public static ItemRef Mod(string key) => new(ContentReference.Mod(key));
    }

    /// <summary>A typed reference to a native or namespaced research node.</summary>
    public readonly struct ResearchRef
    {
        internal ContentReference Value { get; }

        private ResearchRef(ContentReference value)
        {
            Value = value;
        }

        /// <summary>References an existing native numeric identifier.</summary>
        public static ResearchRef Native(ushort id) => new(ContentReference.Native(id));

        /// <summary>References a full, case-sensitive mod:local content key.</summary>
        public static ResearchRef Mod(string key) => new(ContentReference.Mod(key));
    }

    /// <summary>A namespaced build-menu category.</summary>
    public sealed class CategoryDefinition
    {
        /// <summary>Local case-sensitive identifier. The mod prefix is supplied by the owning registry.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        public Texture Icon { get; set; }

        public int SortOrder { get; set; }
    }

    /// <summary>An item reference and positive quantity used in a recipe or research cost.</summary>
    public sealed class ItemCost
    {
        public ItemRef Item { get; set; }

        public int Quantity { get; set; }
    }

    /// <summary>A replacement socket on a part prefab.</summary>
    public sealed class SocketDefinition
    {
        /// <summary>Numeric socket identifier within the part.</summary>
        public byte Id { get; set; }

        public CableType Type { get; set; }

        public Vector3 Position { get; set; }

        public Vector3 Rotation { get; set; }
    }

    /// <summary>A donor-based part definition registered before world loading.</summary>
    public sealed class PartDefinition
    {
        /// <summary>Local case-sensitive identifier. The mod prefix is supplied by the owning registry.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; } = "";

        public string Donor { get; set; }

        /// <summary>Local category ID, or a fully namespaced category key.</summary>
        public string Category { get; set; }

        /// <summary>Null retains the donor's visual model.</summary>
        public MeshDefinition[] Models { get; set; }

        /// <summary>Null retains donor sockets; an empty array removes them.</summary>
        public SocketDefinition[] Sockets { get; set; }

        /// <summary>Placement footprint. Physics and interaction shapes can be defined independently.</summary>
        public Vector3? Size { get; set; }

        /// <summary>Null retains donor solid bounds; empty disables solid collisions. Boxes use part-local coordinates.</summary>
        public BoxShape[] PhysicsShapes { get; set; }

        /// <summary>Null retains donor entry triggers. Replacements preserve native actions and occupied-seat toggling.</summary>
        public InteractionShapeDefinition[] InteractionShapes { get; set; }

        public PartCallbacks Callbacks { get; set; }

        public PartStateDefinition State { get; set; }

        public float? Mass { get; set; }

        public ItemCost[] BuildingCost { get; set; }

        /// <summary>Called immediately after each mesh is created, before the next mesh and before part configuration.</summary>
        public Action<PartBuildContext, GameObject, MeshDefinition> ConfigureMesh { get; set; }

        public Action<PartBuildContext> Configure { get; set; }
    }

    /// <summary>A solid inventory item definition.</summary>
    public class ItemDefinition
    {
        /// <summary>Local case-sensitive identifier. The mod prefix is supplied by the owning registry.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        public float Mass { get; set; } = 1;

        public byte MaxStackSize { get; set; } = 100;

        public MeshDefinition[] Models { get; set; } = Array.Empty<MeshDefinition>();

        public Action<ItemBuildContext> Configure { get; set; }
    }

    /// <summary>A fluid or gas item definition with a display color.</summary>
    public sealed class FluidDefinition : ItemDefinition
    {
        public Color Color { get; set; } = Color.white;
    }

    /// <summary>A named group of research nodes laid out together.</summary>
    public sealed class ResearchTreeDefinition
    {
        /// <summary>Local case-sensitive identifier. The mod prefix is supplied by the owning registry.</summary>
        public string Id { get; set; }

        public ResearchNodeDefinition[] Nodes { get; set; } = Array.Empty<ResearchNodeDefinition>();
    }

    /// <summary>A research node with costs, prerequisites and unlocked parts.</summary>
    public sealed class ResearchNodeDefinition
    {
        /// <summary>Local case-sensitive identifier. The mod prefix is supplied by the owning registry.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        public ResearchRef[] Prerequisites { get; set; } = Array.Empty<ResearchRef>();

        public ItemCost[] Costs { get; set; } = Array.Empty<ItemCost>();

        public PartRef[] UnlockedParts { get; set; } = Array.Empty<PartRef>();

        public bool AvailableInDemo { get; set; } = true;

        public bool ExclusiveToDemo { get; set; }
    }

    /// <summary>Settings and assets available while configuring a cloned part.</summary>
    public sealed class PartBuildContext
    {
        public PartSettings Settings { get; }

        public GameObject Prefab => Settings.part;

        public AssetScope Assets { get; }

        internal PartBuildContext(PartSettings settings, AssetScope assets)
        {
            Settings = settings;
            Assets = assets;
        }
    }

    /// <summary>Settings and assets available while configuring a new item.</summary>
    public sealed class ItemBuildContext
    {
        public ItemSettings Settings { get; }

        public GameObject Prefab => Settings.itemObject;

        public AssetScope Assets { get; }

        internal ItemBuildContext(ItemSettings settings, AssetScope assets)
        {
            Settings = settings;
            Assets = assets;
        }
    }
}
