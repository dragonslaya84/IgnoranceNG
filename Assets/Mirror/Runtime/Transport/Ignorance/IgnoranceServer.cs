#region Statements

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using Cysharp.Threading.Tasks;
using ENet;
using UnityEngine;
using Event = ENet.Event;
using EventType = ENet.EventType;

#endregion

namespace Mirror.ENet
{
    public class IgnoranceServer
    {
        #region Fields

        private readonly Configuration _config;

        private Host _enetHost = new Host();
        private Address _enetAddress;
        public bool ServerStarted;
        internal readonly ConcurrentQueue<ENetServerConnection> IncomingConnection = new ConcurrentQueue<ENetServerConnection>();
        private Dictionary<uint, ENetServerConnection> ConnectedClients = new Dictionary<uint, ENetServerConnection>();

        #endregion

        /// <summary>
        ///     Initialize constructor.
        /// </summary>
        /// <param name="config"></param>
        public IgnoranceServer(Configuration config)
        {
            _config = config;
        }

        /// <summary>
        ///     Easy way to check if our host is not null and has been set.
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static bool IsValid(Host host) => host != null && host.IsSet;

        /// <summary>
        ///     Processes and accepts new incoming connections.
        /// </summary>
        /// <returns></returns>
        private async UniTaskVoid AcceptConnections()
        {
            while (ServerStarted)
            {
                // Never attempt to process anything if the server is not valid.
                if (!IsValid(_enetHost)) continue;

                bool serverWasPolled = false;

                while (!serverWasPolled)
                {
                    if (_enetHost.CheckEvents(out Event networkEvent) <= 0)
                    {
                        if (_enetHost.Service(_config.EnetPollTimeout, out networkEvent) <= 0) break;

                        serverWasPolled = true;
                    }

                    ConnectedClients.TryGetValue(networkEvent.Peer.ID, out ENetServerConnection client);

                    switch (networkEvent.Type)
                    {
                        case EventType.Connect:

                            // A client connected to the server. Assign a new ID to them.
                            if (_config.DebugEnabled)
                            {
                                Debug.Log(
                                    $"Ignorance: New connection from {networkEvent.Peer.IP}:{networkEvent.Peer.Port}");
                                Debug.Log(
                                    $"Ignorance: Map {networkEvent.Peer.IP}:{networkEvent.Peer.Port} (ENET Peer {networkEvent.Peer.ID})");
                            }

                            if (_config.CustomTimeoutLimit)
                                networkEvent.Peer.Timeout(Library.throttleScale, _config.CustomTimeoutBaseTicks,
                                    _config.CustomTimeoutBaseTicks * _config.CustomTimeoutMultiplier);

                            var connection = new ENetServerConnection(networkEvent.Peer, _config);

                            IncomingConnection.Enqueue(connection);

                            ConnectedClients.Add(networkEvent.Peer.ID, connection);

                            break;
                        case EventType.Timeout:
                        case EventType.Disconnect:

                            if(!(client is null))
                            {
                                if (_config.DebugEnabled) Debug.Log($"Ignorance: Dead Peer. {networkEvent.Peer.ID}.");

                                client.Disconnect();
                            }
                            else
                            {
                                if (_config.DebugEnabled)
                                    Debug.LogWarning(
                                        "Ignorance: Peer is already dead, received another disconnect message.");
                            }

                            networkEvent.Packet.Dispose();

                            break;
                        case EventType.Receive:

                            // Client recieving some data.
                            if (client?.Client.ID != networkEvent.Peer.ID)
                            {
                                // Emit a warning and clean the packet. We don't want it in memory.
                                if (_config.DebugEnabled)
                                    Debug.LogWarning(
                                        $"Ignorance: Unknown packet from Peer {networkEvent.Peer.ID}. Be cautious - if you get this error too many times, you're likely being attacked.");
                                networkEvent.Packet.Dispose();
                                break;
                            }

                            if (!networkEvent.Packet.IsSet)
                            {
                                if (_config.DebugEnabled)
                                    Debug.LogWarning("Ignorance WARNING: A incoming packet is not set correctly.");
                                break;
                            }

                            if (networkEvent.Packet.Length > _config.PacketCache.Length)
                            {
                                if (_config.DebugEnabled)
                                    Debug.LogWarning(
                                        $"Ignorance: Packet too big to fit in buffer. {networkEvent.Packet.Length} packet bytes vs {_config.PacketCache.Length} cache bytes {networkEvent.Peer.ID}.");
                                networkEvent.Packet.Dispose();
                            }
                            else
                            {
                                // invoke on the client.
                                try
                                {
                                    var incomingIgnoranceMessage =
                                        new IgnoranceIncomingMessage
                                        {
                                            ChannelId = networkEvent.ChannelID,
                                            Data = new byte[networkEvent.Packet.Length]
                                        };

                                    networkEvent.Packet.CopyTo(incomingIgnoranceMessage.Data);

                                    client.IncomingQueuedData.Enqueue(incomingIgnoranceMessage);

                                    if (_config.DebugEnabled)
                                        Debug.Log(
                                            $"Ignorance: Queuing up incoming data packet: {BitConverter.ToString(incomingIgnoranceMessage.Data)}");
                                }
                                catch (Exception e)
                                {
                                    Debug.LogError(
                                        $"Ignorance caught an exception while trying to copy data from the unmanaged (ENET) world to managed (Mono/IL2CPP) world. Please consider reporting this to the Ignorance developer on GitHub.\n" +
                                        $"Exception returned was: {e.Message}\n" +
                                        $"Debug details: {(_config.PacketCache == null ? "packet buffer was NULL" : $"{_config.PacketCache.Length} byte work buffer")}, {networkEvent.Packet.Length} byte(s) network packet length\n" +
                                        $"Stack Trace: {e.StackTrace}");
                                }
                            }

                            networkEvent.Packet.Dispose();

                            break;
                        default:
                            networkEvent.Packet.Dispose();
                            break;
                    }
                }

                await UniTask.Delay(1);
            }
        }

        /// <summary>
        ///     Shutdown the server and cleanup.
        /// </summary>
        public void Shutdown()
        {
            if (_config.DebugEnabled)
            {
                Debug.Log("[DEBUGGING MODE] Ignorance: ServerStop()");
                Debug.Log("[DEBUGGING MODE] Ignorance: Cleaning the packet cache...");
            }

            if (IsValid(_enetHost))
            {
                _enetHost.Dispose();
            }

            ServerStarted = false;
        }

        /// <summary>
        ///     Start up the server and initialize things
        /// </summary>
        /// <returns></returns>
        public UniTask Start()
        {
            if (!_config.ServerBindAll)
            {
                if (_config.DebugEnabled)
                    Debug.Log(
                        "Ignorance: Not binding to all interfaces, checking if supplied info is actually an IP address");

                if (IPAddress.TryParse(_config.ServerBindAddress, out _))
                {
                    // Looks good to us. Let's use it.
                    if (_config.DebugEnabled) Debug.Log($"Ignorance: Valid IP Address {_config.ServerBindAddress}");

                    _enetAddress.SetIP(_config.ServerBindAddress);
                }
                else
                {
                    // Might be a hostname.
                    if (_config.DebugEnabled)
                        Debug.Log("Ignorance: Doesn't look like a valid IP address, assuming it's a hostname?");

                    _enetAddress.SetHost(_config.ServerBindAddress);
                }
            }
            else
            {
                if (_config.DebugEnabled)
                    Debug.Log($"Ignorance: Setting address to all interfaces, port {_config.CommunicationPort}");
#if UNITY_IOS
                // Coburn: temporary fix until I figure out if this is similar to the MacOS bug again...
                ENETAddress.SetIP("::0");
#endif
            }

            _enetAddress.Port = (ushort) _config.CommunicationPort;

            if (_enetHost == null || !_enetHost.IsSet) _enetHost = new Host();

            // Go go go! Clear those corners!
            _enetHost.Create(_enetAddress, _config.CustomMaxPeerLimit ? _config.CustomMaxPeers : (int) Library.maxPeers,
                _config.Channels.Length, 0, 0);

            if (_config.DebugEnabled)
                Debug.Log(
                    "[DEBUGGING MODE] Ignorance: Server should be created now... If Ignorance immediately crashes after this line, please file a bug report on the GitHub.");

            ServerStarted = true;

            UniTask.Run(AcceptConnections).Forget();

            return UniTask.CompletedTask;
        }
    }
}
