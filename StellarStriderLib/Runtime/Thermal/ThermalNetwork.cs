using System;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using Ships.Interface.Model;
using Ships.Interface.Model.Placement;
using StellarStriderLib.Core;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    internal static class ThermalNetwork
    {
        private const int EntriesPerPacket = 20;
        private const int PacketsPerInterval = 4;
        private const float Interval = 0.25f;

        internal struct Request : IBroadcast
        {
            internal string Token;
        }

        internal struct Packet : IBroadcast
        {
            internal string Token;
            internal bool IsSnapshot, SnapshotComplete;
            internal Update[] Updates;
        }

        internal readonly struct Update
        {
            internal readonly ThermalPartKey Key;
            internal readonly ThermalReading Reading;
            internal readonly bool Remove;

            internal Update(ThermalPartKey key, ThermalReading reading, bool remove = false)
            {
                Key = key;
                Reading = reading;
                Remove = remove;
            }
        }

        private sealed class Peer
        {
            internal readonly NetworkConnection Connection;
            internal readonly string Token;
            internal readonly Dictionary<ThermalPartKey, ThermalReading> Known = new();
            internal KeyValuePair<ThermalPartKey, ThermalReading>[] Snapshot;
            internal int SnapshotCursor, DeltaCursor;
            internal bool SnapshotSent;

            internal Peer(NetworkConnection connection, string token)
            {
                Connection = connection;
                Token = token;
            }
        }

        private static readonly Dictionary<int, Peer> Peers = new();
        private static Dictionary<uint, Dictionary<ThermalPartKey, ThermalReading>> _replica = new();
        private static Dictionary<uint, Dictionary<ThermalPartKey, ThermalReading>> _incoming = new();
        private static NetworkManager _network;
        private static string _token = NewToken();
        private static bool _received;
        private static float _nextRequest, _nextPublish;

        internal static void Install()
        {
            GenericWriter<Request>.SetWrite((writer, request) => writer.WriteString(request.Token));
            GenericReader<Request>.SetRead(reader => new Request { Token = reader.ReadStringAllocated() });
            GenericWriter<Packet>.SetWrite(WritePacket);
            GenericReader<Packet>.SetRead(ReadPacket);
        }

        private static string NewToken() => Guid.NewGuid().ToString("N");

        private static void WritePacket(Writer writer, Packet packet)
        {
            writer.WriteString(packet.Token);
            writer.WriteBoolean(packet.IsSnapshot);
            writer.WriteBoolean(packet.SnapshotComplete);
            writer.WriteUInt16((ushort)packet.Updates.Length);
            foreach (var update in packet.Updates)
            {
                writer.WriteUInt32(update.Key.ShipId);
                writer.WriteBoolean(update.Key.IsHull);
                if (update.Key.IsHull)
                {
                    var hull = update.Key.HullPosition;
                    writer.WriteInt32(hull.VoxelCoord.x);
                    writer.WriteInt32(hull.VoxelCoord.y);
                    writer.WriteInt32(hull.VoxelCoord.z);
                    writer.WriteInt32((int)hull.Type);
                }
                else
                {
                    writer.WriteUInt16(update.Key.PartId);
                }

                writer.WriteBoolean(update.Remove);
                if (update.Remove) continue;
                writer.WriteUInt16(update.Reading.SettingsId);
                writer.WriteVector3(update.Reading.Position);
                var rotation = update.Reading.Rotation;
                writer.WriteSingle(rotation.x);
                writer.WriteSingle(rotation.y);
                writer.WriteSingle(rotation.z);
                writer.WriteSingle(rotation.w);
                writer.WriteSingle(update.Reading.Temperature);
            }
        }

        private static Packet ReadPacket(Reader reader)
        {
            var token = reader.ReadStringAllocated();
            var isSnapshot = reader.ReadBoolean();
            var complete = reader.ReadBoolean();
            var count = reader.ReadUInt16();
            if (count > EntriesPerPacket) return default;

            var updates = new Update[count];
            for (var i = 0; i < count; i++)
            {
                var ship = reader.ReadUInt32();
                ThermalPartKey key;
                if (reader.ReadBoolean())
                {
                    var coord = new Vector3Int(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                    key = ThermalPartKey.Hull(ship, new HullPartPosition(coord, (VoxelPartType)reader.ReadInt32()));
                }
                else
                {
                    key = ThermalPartKey.Stateful(ship, reader.ReadUInt16());
                }

                var remove = reader.ReadBoolean();
                var value = remove ? default : new ThermalReading(reader.ReadUInt16(), reader.ReadVector3(),
                    new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    reader.ReadSingle());
                updates[i] = new Update(key, value, remove);
            }

            return new Packet { Token = token, IsSnapshot = isSnapshot, SnapshotComplete = complete, Updates = updates };
        }

        internal static void Pump()
        {
            if (!ContentSession.Ready) return;
            Attach(InstanceFinder.NetworkManager);
            if (_network == null) return;
            var now = Time.unscaledTime;
            if (_network.IsServerStarted)
            {
                if (now < _nextPublish) return;
                _nextPublish = now + Interval;
                Publish();
                return;
            }

            if (_network.IsClientStarted && !_received && now >= _nextRequest)
            {
                _nextRequest = now + 2f;
                _network.ClientManager.Broadcast(new Request { Token = _token }, Channel.Reliable);
            }
        }

        private static void Attach(NetworkManager network)
        {
            if (_network == network) return;
            if (_network != null)
            {
                _network.ServerManager.UnregisterBroadcast<Request>(Requested);
                _network.ClientManager.UnregisterBroadcast<Packet>(Received);
            }

            _network = network;
            Reset();
            if (_network == null) return;
            _network.ServerManager.RegisterBroadcast<Request>(Requested);
            _network.ClientManager.RegisterBroadcast<Packet>(Received);
        }

        private static void Requested(NetworkConnection connection, Request request, Channel channel)
        {
            if (!_network.IsServerStarted || !ContentSession.Ready || !connection.IsAuthenticated ||
                channel != Channel.Reliable || !Guid.TryParseExact(request.Token, "N", out _))
                return;
            if (!Peers.TryGetValue(connection.ClientId, out var peer) || !ReferenceEquals(peer.Connection, connection))
                Peers[connection.ClientId] = new Peer(connection, request.Token);
        }

        private static void Publish()
        {
            if (Peers.Count == 0) return;
            var current = new Dictionary<ThermalPartKey, ThermalReading>();
            foreach (var pair in ThermalRuntime.Readings())
            {
                var value = pair.Value.Rounded();
                if (value.Temperature != ThermalModel.Ambient) current.Add(pair.Key, value);
            }

            var mtu = _network.TransportManager.GetMTU((byte)Channel.Reliable);
            var perPacket = Math.Min(EntriesPerPacket, Math.Max(1, (mtu - 128) / 56));
            var records = current.ToArray();
            foreach (var pair in Peers.ToArray())
            {
                var peer = pair.Value;
                if (!_network.ServerManager.Clients.TryGetValue(pair.Key, out var connection) ||
                    !ReferenceEquals(connection, peer.Connection) || !connection.IsAuthenticated)
                {
                    Peers.Remove(pair.Key);
                    continue;
                }

                if (!peer.SnapshotSent)
                {
                    PublishSnapshot(peer, records, perPacket);
                    continue;
                }

                var updates = new List<Update>(perPacket * PacketsPerInterval);
                foreach (var key in peer.Known.Keys)
                {
                    if (!current.ContainsKey(key)) updates.Add(new Update(key, default, true));
                    if (updates.Count == updates.Capacity) break;
                }

                for (var i = 0; i < records.Length && updates.Count < updates.Capacity; i++)
                {
                    var index = peer.DeltaCursor % records.Length;
                    var reading = records[index];
                    peer.DeltaCursor = (index + 1) % records.Length;
                    if (!peer.Known.TryGetValue(reading.Key, out var known) || !known.SameValue(reading.Value))
                        updates.Add(new Update(reading.Key, reading.Value));
                }

                for (var offset = 0; offset < updates.Count; offset += perPacket)
                {
                    var count = Math.Min(perPacket, updates.Count - offset);
                    var batch = new Update[count];
                    updates.CopyTo(offset, batch, 0, count);
                    Send(peer, batch);
                    foreach (var update in batch)
                    {
                        if (update.Remove) peer.Known.Remove(update.Key);
                        else peer.Known[update.Key] = update.Reading;
                    }
                }

            }
        }

        private static void PublishSnapshot(Peer peer, KeyValuePair<ThermalPartKey, ThermalReading>[] records,
            int perPacket)
        {
            peer.Snapshot ??= records;
            for (var packet = 0; packet < PacketsPerInterval; packet++)
            {
                var count = Math.Min(perPacket, peer.Snapshot.Length - peer.SnapshotCursor);
                var batch = new Update[count];
                for (var i = 0; i < count; i++)
                {
                    var record = peer.Snapshot[peer.SnapshotCursor++];
                    batch[i] = new Update(record.Key, record.Value);
                    peer.Known.Add(record.Key, record.Value);
                }

                var complete = peer.SnapshotCursor == peer.Snapshot.Length;
                Send(peer, batch, true, complete);
                if (!complete) continue;
                peer.Snapshot = null;
                peer.SnapshotSent = true;
                return;
            }
        }

        private static void Send(Peer peer, Update[] updates, bool snapshot = false, bool complete = false) =>
            _network.ServerManager.Broadcast(peer.Connection, new Packet
            {
                Token = peer.Token, Updates = updates, IsSnapshot = snapshot, SnapshotComplete = complete
            }, true, Channel.Reliable);

        private static void Received(Packet packet, Channel channel)
        {
            if (channel != Channel.Reliable || _network == null || _network.IsServerStarted || !ContentSession.Ready ||
                packet.Token != _token || packet.Updates == null)
                return;

            _received = true;
            if (packet.IsSnapshot && _incoming == null) return;
            var target = packet.IsSnapshot ? _incoming : _replica;
            foreach (var update in packet.Updates)
            {
                if (update.Remove) Remove(target, update.Key);
                else if (ThermalModel.ValidTemperature(update.Reading.Temperature))
                {
                    if (!target.TryGetValue(update.Key.ShipId, out var parts))
                        target.Add(update.Key.ShipId, parts = new Dictionary<ThermalPartKey, ThermalReading>());
                    parts[update.Key] = update.Reading;
                }
            }

            if (packet.IsSnapshot && packet.SnapshotComplete)
            {
                _replica = _incoming;
                _incoming = null;
            }
        }

        internal static IEnumerable<ThermalPartKey> ForShip(uint shipId) =>
            _replica.TryGetValue(shipId, out var parts) ? parts.Keys : Array.Empty<ThermalPartKey>();

        internal static bool TryRead(ThermalPart part, out float temperature)
        {
            temperature = default;
            if (!_replica.TryGetValue(part.Key.ShipId, out var parts) ||
                !parts.TryGetValue(part.Key, out var reading) || !reading.Matches(part)) return false;
            temperature = reading.Temperature;
            return true;
        }

        internal static void RemoveReplica(ThermalPartKey key)
        {
            Remove(_replica, key);
            if (_incoming != null) Remove(_incoming, key);
        }

        private static void Remove(Dictionary<uint, Dictionary<ThermalPartKey, ThermalReading>> readings,
            ThermalPartKey key)
        {
            if (!readings.TryGetValue(key.ShipId, out var parts)) return;
            parts.Remove(key);
            if (parts.Count == 0) readings.Remove(key.ShipId);
        }

        internal static void Reset()
        {
            Peers.Clear();
            _replica.Clear();
            _incoming = new Dictionary<uint, Dictionary<ThermalPartKey, ThermalReading>>();
            _token = NewToken();
            _received = false;
            _nextRequest = _nextPublish = 0;
        }

        internal static void Shutdown() => Attach(null);
    }
}
