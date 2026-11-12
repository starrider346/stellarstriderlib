using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model.Parts;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    /// <summary>Replicates versioned part state using bounded chunk transfers and removal revisions.</summary>
    internal static class PartStateNetwork
    {
        internal struct Request : IBroadcast
        {
            public string Token;
        }

        internal struct Packet : IBroadcast
        {
            public string Token, Key;
            public uint Ship;
            public ushort Part;
            public int Version, Index, Count;
            public ulong Revision;
            public byte[] Data;
        }

        /// <summary>Tracks a client’s world token, request timing and acknowledged part revisions.</summary>
        private sealed class Peer
        {
            internal string Token;
            internal float LastRequest;
            internal int Cursor;
            internal readonly Dictionary<PartContext, ulong> Known = new();
        }

        /// <summary>Buffers chunks for one part revision until its snapshot is complete or expires.</summary>
        private sealed class Transfer
        {
            internal string Key;
            internal int Version;
            internal ulong Revision;
            internal byte[][] Chunks;
            internal float Expires;
        }

        private static readonly Dictionary<int, Peer> Peers = new();
        private static readonly Dictionary<PartContext, Transfer> Transfers = new();
        private static readonly Dictionary<PartContext, ulong> Deleted = new();
        private static NetworkManager _network;
        private static string _token;
        private static float _retry;
        private static bool _requested;

        internal static void Install()
        {
            GenericWriter<Request>.SetWrite((writer, value) => writer.WriteString(value.Token));
            GenericReader<Request>.SetRead(reader => new Request { Token = reader.ReadStringAllocated() });
            GenericWriter<Packet>.SetWrite((writer, value) =>
            {
                writer.WriteString(value.Token);
                writer.WriteString(value.Key);
                writer.WriteUInt32(value.Ship);
                writer.WriteUInt16(value.Part);
                writer.WriteInt32(value.Version);
                writer.WriteInt32(value.Index);
                writer.WriteInt32(value.Count);
                writer.WriteUInt64(value.Revision);
                writer.WriteUInt8ArrayAndSize(value.Data);
            });
            GenericReader<Packet>.SetRead(reader => new Packet
            {
                Token = reader.ReadStringAllocated(), Key = reader.ReadStringAllocated(), Ship = reader.ReadUInt32(),
                Part = reader.ReadUInt16(), Version = reader.ReadInt32(), Index = reader.ReadInt32(),
                Count = reader.ReadInt32(), Revision = reader.ReadUInt64(),
                Data = reader.ReadUInt8ArrayAndSizeAllocated()
            });
        }

        internal static void Reset()
        {
            Peers.Clear();
            Transfers.Clear();
            Deleted.Clear();
            _token = Guid.NewGuid().ToString("N");
            _requested = false;
            _retry = 0;
        }

        private static void Attach(NetworkManager network)
        {
            if (_network == network)
            {
                return;
            }

            if (_network != null)
            {
                _network.ServerManager.UnregisterBroadcast<Request>(Requested);
                _network.ClientManager.UnregisterBroadcast<Packet>(Receive);
            }

            _network = network;
            Reset();
            if (_network == null)
            {
                return;
            }

            _network.ServerManager.RegisterBroadcast<Request>(Requested);
            _network.ClientManager.RegisterBroadcast<Packet>(Receive);
        }

        internal static void Update()
        {
            Attach(InstanceFinder.NetworkManager);
            if (_network == null || !ContentSession.Ready)
            {
                return;
            }

            if (!_network.IsClientStarted)
            {
                _requested = false;
                _token = Guid.NewGuid().ToString("N");
            }
            else if (!_network.IsServerStarted && !_requested && Time.unscaledTime >= _retry)
            {
                _network.ClientManager.Broadcast(new Request { Token = _token }, Channel.Reliable);
                _retry = Time.unscaledTime + 2;
            }

            foreach (var id in Transfers.Where(p => p.Value.Expires < Time.unscaledTime).Select(p => p.Key).ToArray())
            {
                Transfers.Remove(id);
            }

            if (!_network.IsServerStarted)
            {
                return;
            }

            PartRuntime.ReconcileServer();
            if (_network.IsClientStarted)
            {
                foreach (var id in PartRuntime.Client.Keys.Where(id => !PartRuntime.Server.ContainsKey(id)).ToArray())
                {
                    PartRuntime.Client.Remove(id);
                }

                foreach (var pair in PartRuntime.Server)
                {
                    var schema = StriderRegistry.Parts[pair.Value.Key].Definition.State;
                    if (schema != null)
                    {
                        PartRuntime.ApplyReplica(pair.Key, pair.Value.Key, pair.Value.Revision,
                            StateFields.Select(schema, pair.Value.Values, false));
                    }
                }
            }

            foreach (var id in Peers.Keys.Where(id => !_network.ServerManager.Clients.ContainsKey(id)).ToArray())
            {
                Peers.Remove(id);
            }

            foreach (var pair in Peers.ToArray())
            {
                if (!_network.ServerManager.Clients.TryGetValue(pair.Key, out var connection) ||
                    !connection.IsAuthenticated)
                {
                    continue;
                }

                var peer = pair.Value;
                var budget = 32768;
                foreach (var id in peer.Known.Keys.Where(id => !PartRuntime.Server.ContainsKey(id)).Take(32).ToArray())
                {
                    Send(connection,
                        new Packet
                        {
                            Token = peer.Token, Ship = id.ShipId, Part = id.PartId, Key = "", Revision = peer.Known[id],
                            Data = Array.Empty<byte>()
                        });
                    peer.Known.Remove(id);
                }

                var records = PartRuntime.Server.ToArray();
                var start = peer.Cursor;
                for (var recordOffset = 0; recordOffset < records.Length; recordOffset++)
                {
                    var index = (start + recordOffset) % records.Length;
                    var entry = records[index];
                    var record = entry.Value;
                    if (peer.Known.TryGetValue(entry.Key, out var revision) && revision == record.Revision)
                    {
                        continue;
                    }

                    var schema = StriderRegistry.Parts[record.Key].Definition.State;
                    if (schema == null)
                    {
                        continue;
                    }

                    var data = Encoding.UTF8.GetBytes(StateFields.Select(schema, record.Values, false)
                        .ToString(Formatting.None));
                    var size = Math.Min(1024, _network.TransportManager.GetMTU((byte)Channel.Reliable) - 256);
                    if (size < 64)
                    {
                        throw new InvalidOperationException("Transport MTU is too small for part state.");
                    }

                    var count = (data.Length + size - 1) / size;
                    if (data.Length + count * 256 > budget)
                    {
                        peer.Cursor = index;
                        break;
                    }

                    for (var chunkIndex = 0; chunkIndex < count; chunkIndex++)
                    {
                        Send(connection,
                            new Packet
                            {
                                Token = peer.Token, Ship = entry.Key.ShipId, Part = entry.Key.PartId, Key = record.Key,
                                Version = schema.Version, Revision = record.Revision, Index = chunkIndex, Count = count,
                                Data = data.Skip(chunkIndex * size).Take(size).ToArray()
                            });
                    }

                    budget -= data.Length + count * 256;
                    peer.Known[entry.Key] = record.Revision;
                    peer.Cursor = (index + 1) % records.Length;
                }
            }
        }

        private static void Send(NetworkConnection connection, Packet value) =>
            _network.ServerManager.Broadcast(connection, value, true, Channel.Reliable);

        private static void Requested(NetworkConnection connection, Request request, Channel channel)
        {
            if (connection == null || !connection.IsAuthenticated || request.Token == null ||
                request.Token.Length != 32)
            {
                return;
            }

            if (Peers.TryGetValue(connection.ClientId, out var old) && Time.unscaledTime - old.LastRequest < 1)
            {
                return;
            }

            Peers[connection.ClientId] = new Peer { Token = request.Token, LastRequest = Time.unscaledTime };
            Send(connection, new Packet { Token = request.Token, Key = null, Data = Array.Empty<byte>() });
        }

        private static void Receive(Packet packet, Channel channel)
        {
            if (_network == null || _network.IsServerStarted || packet.Token != _token)
            {
                return;
            }

            _requested = true;
            if (packet.Key == null)
            {
                return;
            }

            try
            {
                Apply(packet, Time.unscaledTime);
            }
            catch (Exception e)
            {
                Plugin.Report("Part state replication failed: " + e.Message);
            }
        }

        internal static void Apply(Packet packet, float currentTime)
        {
            var id = new PartContext(packet.Ship, packet.Part);
            if (packet.Key == "")
            {
                if (!Deleted.TryGetValue(id, out var previous) || previous < packet.Revision)
                {
                    Deleted[id] = packet.Revision;
                }

                if (PartRuntime.Client.TryGetValue(id, out var old) && old.Revision <= packet.Revision)
                {
                    PartRuntime.Client.Remove(id);
                }

                if (Transfers.TryGetValue(id, out var pending) && pending.Revision <= packet.Revision)
                {
                    Transfers.Remove(id);
                }

                return;
            }

            if (Deleted.TryGetValue(id, out var deleted) && packet.Revision <= deleted)
            {
                return;
            }

            if (PartRuntime.Client.TryGetValue(id, out var current) && current.Revision >= packet.Revision)
            {
                return;
            }

            if (!StriderRegistry.Parts.TryGetValue(packet.Key, out var entry) || entry.Definition.State == null ||
                entry.Definition.State.Version != packet.Version)
            {
                throw new ArgumentException("Part state schema mismatch: " + packet.Key);
            }

            if (packet.Count < 1 || packet.Count > 256 || packet.Index < 0 || packet.Index >= packet.Count ||
                packet.Data == null || packet.Data.Length > 1024)
            {
                throw new ArgumentException("Invalid part state chunk.");
            }

            if (!Transfers.TryGetValue(id, out var transfer) || transfer.Revision < packet.Revision)
            {
                if (Transfers.Count >= 256)
                {
                    throw new ArgumentException("Too many unfinished part state transfers.");
                }

                transfer = new Transfer
                {
                    Key = packet.Key, Version = packet.Version, Revision = packet.Revision,
                    Chunks = new byte[packet.Count][], Expires = currentTime + 10
                };
                Transfers[id] = transfer;
            }

            if (transfer.Revision > packet.Revision)
            {
                return;
            }

            if (transfer.Key != packet.Key || transfer.Version != packet.Version ||
                transfer.Chunks.Length != packet.Count)
            {
                throw new ArgumentException("Conflicting part state chunks.");
            }

            transfer.Chunks[packet.Index] = packet.Data;
            if (transfer.Chunks.Where(x => x != null).Sum(x => x.Length) > StateFields.MaxBytes)
            {
                throw new ArgumentException("Part state transfer exceeds 16 KiB.");
            }

            if (transfer.Chunks.Any(x => x == null))
            {
                return;
            }

            var bytes = transfer.Chunks.SelectMany(x => x).ToArray();
            PartRuntime.ApplyReplica(id, packet.Key, packet.Revision, JObject.Parse(Encoding.UTF8.GetString(bytes)));
            Transfers.Remove(id);
        }

        internal static void Shutdown()
        {
            Attach(null);
            Reset();
        }
    }
}
