using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Serializing;
using FishNet.Transporting;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StellarStriderLib.Core;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    /// <summary>Exchanges the host registry before releasing native multiplayer authentication.</summary>
    internal static class RegistryNetwork
    {
        internal struct Chunk : IBroadcast
        {
            public string Token, Digest;
            public int Index, Count;
            public byte[] Data;
        }

        internal struct Ack : IBroadcast
        {
            public string Token, Digest, Error;
        }

        /// <summary>Tracks an awaiting connection’s registry ticket, outgoing chunks and retry deadlines.</summary>
        private sealed class Pending
        {
            internal ServerManager Server;
            internal readonly AuthenticationTicket Ticket = new();
            internal string Token => Ticket.Token;
            internal string Digest => Ticket.Digest;
            internal byte[][] Chunks;
            internal int Cursor;
            internal float Expires, Retry;
        }

        /// <summary>Compares connections by object identity so mutable FishNet client IDs cannot invalidate tracking.</summary>
        private sealed class ConnectionIdentity : IEqualityComparer<NetworkConnection>
        {
            public bool Equals(NetworkConnection a, NetworkConnection b) => ReferenceEquals(a, b);

            public int GetHashCode(NetworkConnection connection) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(connection);
        }

        private static readonly ConnectionIdentity Identity = new();
        private static readonly Dictionary<NetworkConnection, Pending> PendingAuth = new(Identity);
        private static readonly HashSet<NetworkConnection> Approved = new(Identity);

        private static readonly MethodInfo Authenticate =
            AccessTools.Method(typeof(ServerManager), "ClientAuthenticated");

        private static NetworkManager _network;
        private static RegistryTransfer.Receiver _receiver = new();
        private static string _completedToken, _completedDigest;
        private static string _incomingToken;
        private static readonly HashSet<string> RetiredTokens = new();
        private static bool _connected;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            GenericWriter<Chunk>.SetWrite((writer, value) =>
            {
                writer.WriteString(value.Token);
                writer.WriteString(value.Digest);
                writer.WriteInt32(value.Index);
                writer.WriteInt32(value.Count);
                writer.WriteUInt8ArrayAndSize(value.Data);
            });
            GenericReader<Chunk>.SetRead(reader => new Chunk
            {
                Token = reader.ReadStringAllocated(), Digest = reader.ReadStringAllocated(), Index = reader.ReadInt32(),
                Count = reader.ReadInt32(), Data = reader.ReadUInt8ArrayAndSizeAllocated()
            });
            GenericWriter<Ack>.SetWrite((writer, value) =>
            {
                writer.WriteString(value.Token);
                writer.WriteString(value.Digest);
                writer.WriteString(value.Error);
            });
            GenericReader<Ack>.SetRead(reader => new Ack
            {
                Token = reader.ReadStringAllocated(), Digest = reader.ReadStringAllocated(),
                Error = reader.ReadStringAllocated()
            });
            if (Authenticate == null)
            {
                throw new MissingMethodException("FishNet authentication hook is unavailable.");
            }

            harmony.Patch(Authenticate, prefix: new HarmonyMethod(typeof(RegistryNetwork), nameof(Gate)));
        }

        private static bool Gate(ServerManager __instance, NetworkConnection __0)
        {
            Attach(__instance.NetworkManager);
            if (Approved.Contains(__0))
            {
                return true;
            }

            if (!PendingAuth.ContainsKey(__0))
            {
                PendingAuth.Add(__0, new Pending { Server = __instance, Expires = Time.unscaledTime + 30 });
            }

            return false;
        }

        private static void Attach(NetworkManager network)
        {
            if (_network == network)
            {
                return;
            }

            if (_network != null)
            {
                _network.ServerManager.UnregisterBroadcast<Ack>(ReceiveAck);
                _network.ServerManager.OnRemoteConnectionState -= RemoteState;
                _network.ClientManager.UnregisterBroadcast<Chunk>(ReceiveChunk);
            }

            Reset();
            _network = network;
            if (_network == null)
            {
                return;
            }

            _network.ServerManager.RegisterBroadcast<Ack>(ReceiveAck, false);
            _network.ServerManager.OnRemoteConnectionState += RemoteState;
            _network.ClientManager.RegisterBroadcast<Chunk>(ReceiveChunk);
        }

        private static void RemoteState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            Approved.Remove(connection);
            PendingAuth.Remove(connection);
        }

        internal static void Update()
        {
            Attach(InstanceFinder.NetworkManager);
            var connected = _network != null && (_network.IsServerStarted || _network.IsClientStarted);
            if (_connected && !connected)
            {
                Reset();
                ContentSession.End();
            }

            _connected = connected;
            if (_network == null)
            {
                return;
            }

            var budget = 32768;
            foreach (var pair in PendingAuth.ToArray())
            {
                var connection = pair.Key;
                var pending = pair.Value;
                if (!connection.IsActive || Time.unscaledTime > pending.Expires)
                {
                    PendingAuth.Remove(connection);
                    if (connection.IsActive)
                    {
                        connection.Disconnect(false);
                    }

                    continue;
                }

                if (!ContentSession.Ready || !WorldDataService.Ready)
                {
                    continue;
                }

                if (pending.Chunks == null)
                {
                    var manifest = new JObject
                    {
                        ["format"] = StriderRegistry.RegistryProtocolVersion,
                        ["registry"] = JObject.Parse(ContentSession.Registry.Serialize()),
                        ["modWorldData"] = WorldDataService.Capture()
                    };
                    var bytes = Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None));
                    if (bytes.Length > RegistryTransfer.MaximumBytes)
                    {
                        MelonLoader.MelonLogger.Warning(
                            "Cannot synchronize this world: content snapshot exceeds 4 MiB.");
                        PendingAuth.Remove(connection);
                        connection.Disconnect(false);
                        continue;
                    }

                    pending.Ticket.SetManifest(bytes);
                    var size = Math.Min(4096,
                        pending.Server.NetworkManager.TransportManager.GetMTU((byte)Channel.Reliable) - 256);
                    pending.Chunks = RegistryTransfer.Split(bytes, size);
                }

                if (pending.Cursor == pending.Chunks.Length)
                {
                    if (Time.unscaledTime < pending.Retry)
                    {
                        continue;
                    }

                    pending.Cursor = 0;
                }

                while (pending.Cursor < pending.Chunks.Length && budget >= pending.Chunks[pending.Cursor].Length)
                {
                    var data = pending.Chunks[pending.Cursor];
                    pending.Server.Broadcast(connection,
                        new Chunk
                        {
                            Token = pending.Token, Digest = pending.Digest, Index = pending.Cursor,
                            Count = pending.Chunks.Length, Data = data
                        }, false, Channel.Reliable);
                    pending.Cursor++;
                    budget -= data.Length;
                }

                if (pending.Cursor != pending.Chunks.Length)
                {
                    continue;
                }

                pending.Ticket.MarkSent();
                pending.Retry = Time.unscaledTime + 2;
            }
        }

        private static void ReceiveChunk(Chunk chunk, Channel channel)
        {
            if (_network == null)
            {
                return;
            }

            try
            {
                if (_completedToken == chunk.Token && _completedDigest == chunk.Digest)
                {
                    _network.ClientManager.Broadcast(new Ack { Token = chunk.Token, Digest = chunk.Digest, Error = "" },
                        Channel.Reliable);
                    return;
                }

                if (_completedToken == chunk.Token || RetiredTokens.Contains(chunk.Token)) return;
                if (_incomingToken != chunk.Token)
                {
                    if (_incomingToken != null) RetiredTokens.Add(_incomingToken);
                    if (RetiredTokens.Count > 64)
                        throw new InvalidOperationException("Too many registry revisions during authentication.");
                    _incomingToken = chunk.Token;
                    _receiver = new RegistryTransfer.Receiver();
                }

                var bytes = _receiver.Add(chunk.Token, chunk.Digest, chunk.Index, chunk.Count, chunk.Data);
                if (bytes == null)
                {
                    return;
                }

                var manifest = JObject.Parse(Encoding.UTF8.GetString(bytes));
                if ((int?)manifest["format"] != StriderRegistry.RegistryProtocolVersion)
                    throw new InvalidOperationException("Incompatible SSLib multiplayer protocol.");
                ApplyRegistry(WorldRegistry.Parse(manifest["registry"]?.ToString(Formatting.None)), chunk,
                    manifest["modWorldData"] as JObject);
            }
            catch (Exception error)
            {
                Plugin.Report(error.Message, false);
                _network.ClientManager.Broadcast(
                    new Ack
                    {
                        Token = chunk.Token, Digest = chunk.Digest,
                        Error = error.Message.Length > 512 ? error.Message.Substring(0, 512) : error.Message
                    }, Channel.Reliable);
            }
        }

        private static void ApplyRegistry(WorldRegistry registry, Chunk chunk, JObject modWorldData)
        {
            try
            {
                if (_network == null)
                {
                    throw new InvalidOperationException("Registry connection is unavailable. Reconnect to try again.");
                }

                if (_network.IsServerStarted)
                {
                    if (ContentSession.Registry.Serialize() != registry.Serialize())
                    {
                        throw new InvalidOperationException("Host registry changed during authentication.");
                    }
                }
                else if (ContentSession.Registry?.Serialize() != registry.Serialize() || !ContentSession.Ready)
                {
                    var check = WorldRegistry.Parse(registry.Serialize());
                    ContentSession.End();
                    ContentSession.PrimeExternal();
                    check.Bind(StriderRegistry.Definitions(), StriderRegistry.Mods.Keys, ContentSession.Occupied);
                    StriderRegistry.Frozen = true;
                    ContentSession.Bind(registry);
                }

                if (!_network.IsServerStarted) WorldDataService.Restore(modWorldData);
                Plugin.ClearError();
                _completedToken = chunk.Token;
                _completedDigest = chunk.Digest;
                _network.ClientManager.Broadcast(new Ack { Token = chunk.Token, Digest = chunk.Digest, Error = "" },
                    Channel.Reliable);
            }
            catch (Exception error)
            {
                Plugin.Report(error.Message, false);
                _network?.ClientManager.Broadcast(
                    new Ack
                    {
                        Token = chunk.Token, Digest = chunk.Digest,
                        Error = error.Message.Length > 512 ? error.Message.Substring(0, 512) : error.Message
                    }, Channel.Reliable);
            }
        }

        private static void ReceiveAck(NetworkConnection connection, Ack ack, Channel channel)
        {
            if (!PendingAuth.TryGetValue(connection, out var pending))
            {
                return;
            }

            var decision = pending.Ticket.Acknowledge(ack.Token, ack.Digest, ack.Error);
            if (decision == Acknowledgement.Ignored)
            {
                return;
            }

            PendingAuth.Remove(connection);
            if (decision == Acknowledgement.Rejected)
            {
                MelonLoader.MelonLogger.Warning("Client registry rejected: " + ack.Error);
                connection.Disconnect(false);
                return;
            }

            // Authentication may still be delayed by another prefix. Keep approval for this connection.
            Approved.Add(connection);
            Authenticate.Invoke(pending.Server, new object[] { connection });
        }

        internal static void WorldChanging()
        {
            foreach (var pair in PendingAuth.Where(pending => pending.Value.Digest != null).ToArray())
            {
                PendingAuth.Remove(pair.Key);
                if (pair.Key.IsActive)
                {
                    pair.Key.Disconnect(false);
                }
            }

            foreach (var connection in Approved.ToArray())
                if (connection.IsActive && !connection.IsAuthenticated) connection.Disconnect(false);
            Approved.Clear();
            _completedDigest = null;
            _receiver = new RegistryTransfer.Receiver();
            _completedToken = null;
            _incomingToken = null;
            RetiredTokens.Clear();
        }

        internal static void Reset()
        {
            PendingAuth.Clear();
            Approved.Clear();
            _completedDigest = null;
            _receiver = new RegistryTransfer.Receiver();
            _completedToken = null;
            _incomingToken = null;
            RetiredTokens.Clear();
        }

        internal static void Shutdown()
        {
            Reset();
            Attach(null);
        }
    }
}
