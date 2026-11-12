using System;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using StellarStriderLib.Runtime;
using UnityEngine;

namespace StellarStriderLib
{
    /// <summary>A box in part-local metres, independent of placement size and model bounds.</summary>
    public sealed class BoxShape
    {
        public Vector3 Center { get; set; }

        public Vector3 Size { get; set; } = Vector3.one;

        public Vector3 Rotation { get; set; }
    }

    /// <summary>Replacement entry triggers for an existing donor PartInteraction.</summary>
    public sealed class InteractionShapeDefinition
    {
        /// <summary>Unique donor interaction GameObject name, or null to select its sole interaction.</summary>
        public string Target { get; set; }

        public BoxShape[] Boxes { get; set; } = Array.Empty<BoxShape>();
    }

    /// <summary>Declares a JSON field with its default value and persistence and replication settings.</summary>
    public sealed class PartStateField
    {
        public string Name { get; set; }

        /// <summary>JSON value defining the field's type and default. Null defaults are not supported.</summary>
        public JToken Default { get; set; }

        public bool Saved { get; set; } = true;

        public bool Replicated { get; set; } = true;
    }

    /// <summary>Defines versioned part state, validation and migration of saved fields.</summary>
    public sealed class PartStateDefinition
    {
        public int Version { get; set; } = 1;

        public PartStateField[] Fields { get; set; } = Array.Empty<PartStateField>();

        /// <summary>Converts old saved fields to the current version. Receives a private copy.</summary>
        public Func<int, JObject, JObject> Migrate { get; set; }

        /// <summary>Throw to reject a complete proposed state. Must not mutate it.</summary>
        public Action<JObject> Validate { get; set; }
    }

    /// <summary>Provides optional callbacks for part lifecycle, simulation and state changes.</summary>
    public sealed class PartCallbacks
    {
        public Action<PartInstance> Spawned { get; set; }

        public Action<PartInstance> Removed { get; set; }

        public Action<PartInstance, float> ServerTick { get; set; }

        public Action<PartInstance, float> ClientUpdate { get; set; }

        public Action<PartInstance, StatefulPartStateChangeEvent> NativeStateChanged { get; set; }

        public Action<PartInstance> StateChanged { get; set; }
    }

    /// <summary>A runtime part on one role. Hosts have separate server and client instances.</summary>
    public sealed class PartInstance
    {
        public PartContext Id { get; }

        public string ContentKey { get; }

        public bool IsServer { get; }

        public IShipStateRead Ship { get; }

        public StatefulPart Part { get; internal set; }

        public PartState State { get; }

        public bool IsActive => Active;
        internal JObject RemovedState;
        internal bool Active = true;
        internal object NativeState;
        internal PartDefinition Definition;

        internal PartInstance(PartContext id, string key, bool server, IShipStateRead ship, StatefulPart part,
            PartDefinition definition)
        {
            Id = id;
            ContentKey = key;
            IsServer = server;
            Ship = ship;
            Part = part;
            Definition = definition;
            State = new PartState(this);
        }
    }

    /// <summary>Server-owned JSON fields. Values returned to callers are always copies.</summary>
    public sealed class PartState
    {
        private readonly PartInstance _instance;

        internal PartState(PartInstance instance)
        {
            _instance = instance;
        }

        public T Get<T>(string field) => PartRuntime.Read(_instance, field).ToObject<T>();
        public JObject Snapshot() => PartRuntime.Snapshot(_instance);

        public void Set<T>(string field, T value) => Set(new JObject
            { [field] = value == null ? JValue.CreateNull() : JToken.FromObject(value) });

        /// <summary>Atomically validates and commits multiple fields. Client or removed instances cannot write.</summary>
        public void Set(JObject fields) => PartRuntime.Write(_instance, fields);
    }
}
