using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HereToSlay.Net;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace HereToSlay.Online
{
    /// <summary>
    /// Internet play through Unity Relay: the host asks Relay for a short join code and every player connects
    /// to the Relay server, so nobody needs port forwarding and players can be on completely different networks.
    /// Messages travel over Unity Transport on a reliable, ordered pipeline.
    /// </summary>
    public static class RelayNet
    {
        /// <summary>Relay join codes are 6 characters.</summary>
        public const int CodeLength = 6;

        private static Task signIn;

        /// <summary>Accepts "abc 123", "ABC-123" etc. Returns null if it is not a Relay code.</summary>
        public static string NormalizeCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string code = text.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
            if (code.Length != CodeLength)
            {
                return null;
            }

            foreach (char c in code)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    return null;
                }
            }

            return code;
        }

        public static Task EnsureSignedInAsync()
        {
            if (signIn == null || signIn.IsFaulted || signIn.IsCanceled)
            {
                signIn = SignInAsync();
            }

            return signIn;
        }

        private static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                InitializationOptions options = new InitializationOptions();
                // A separate anonymous player for every copy of the game that is running, so two copies on one
                // computer (handy for testing) can host and join each other.
                options.SetProfile("hts" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        public static RelayServerData ServerData(List<RelayServerEndpoint> endpoints, byte[] allocationId, byte[] connectionData,
            byte[] hostConnectionData, byte[] key)
        {
            RelayServerEndpoint endpoint = endpoints.Find(e => e.ConnectionType == "dtls") ?? endpoints.Find(e => e.ConnectionType == "udp");
            if (endpoint == null)
            {
                throw new InvalidOperationException("Relay did not offer a usable server.");
            }

            return new RelayServerData(endpoint.Host, (ushort)endpoint.Port, allocationId, connectionData, hostConnectionData, key, endpoint.Secure);
        }

        public static NetworkDriver CreateDriver(ref RelayServerData data, out NetworkPipeline pipeline)
        {
            NetworkSettings settings = new NetworkSettings(Allocator.Temp);
            settings.WithRelayParameters(ref data);
            settings.WithReliableStageParameters(windowSize: 64);
            NetworkDriver driver = NetworkDriver.Create(settings);
            pipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            return driver;
        }

        /// <summary>A short, player-friendly explanation of why Relay did not work.</summary>
        public static string Explain(Exception e)
        {
            while (e is AggregateException aggregate && aggregate.InnerException != null)
            {
                e = aggregate.InnerException;
            }

            string message = e.Message ?? e.GetType().Name;
            string lower = message.ToLowerInvariant();
            if (lower.Contains("join code not found") || lower.Contains("not found"))
            {
                return "No game with that code. Check the code (codes stop working when the host closes their lobby).";
            }

            if (lower.Contains("cloud project") || lower.Contains("project id") || lower.Contains("projectid"))
            {
                return "Online services are not set up for this build (Unity project not linked to Unity Cloud).";
            }

            if (lower.Contains("network") || lower.Contains("connect") || lower.Contains("resolve") || lower.Contains("timeout"))
            {
                return "Could not reach Unity's online services. Check your internet connection.";
            }

            return message.Length > 160 ? message.Substring(0, 160) + "..." : message;
        }
    }

    /// <summary>
    /// Splits messages into ~1 KB chunks sent on the reliable pipeline (in order, resent if lost) and glues them
    /// back together on arrival. Chunks that cannot be sent yet (window full) wait for the next frame, so a
    /// message is never half-sent.
    /// </summary>
    internal sealed class RelayChannel
    {
        private const int ChunkBytes = 1000;
        private readonly Queue<byte[]> outgoing = new Queue<byte[]>();
        private readonly MemoryStream incoming = new MemoryStream();

        public bool HasPending => outgoing.Count > 0;

        public void Enqueue(byte[] payload)
        {
            int offset = 0;
            do
            {
                int size = Math.Min(ChunkBytes, payload.Length - offset);
                byte[] chunk = new byte[size + 1];
                chunk[0] = offset + size < payload.Length ? (byte)1 : (byte)0; // 1 = more chunks follow
                Buffer.BlockCopy(payload, offset, chunk, 1, size);
                outgoing.Enqueue(chunk);
                offset += size;
            }
            while (offset < payload.Length);
        }

        public void Flush(NetworkDriver driver, NetworkPipeline pipeline, NetworkConnection connection)
        {
            while (outgoing.Count > 0)
            {
                byte[] chunk = outgoing.Peek();
                if (driver.BeginSend(pipeline, connection, out DataStreamWriter writer, chunk.Length) < 0)
                {
                    return;
                }

                NativeArray<byte> native = new NativeArray<byte>(chunk, Allocator.Temp);
                writer.WriteBytes(native);
                native.Dispose();
                if (writer.HasFailedWrites)
                {
                    driver.AbortSend(writer);
                    return;
                }

                if (driver.EndSend(writer) < 0)
                {
                    return;
                }

                outgoing.Dequeue();
            }
        }

        /// <summary>Returns a whole message once its last chunk arrived, otherwise null.</summary>
        public byte[] Receive(ref DataStreamReader reader)
        {
            int length = reader.Length;
            if (length <= 0)
            {
                return null;
            }

            NativeArray<byte> native = new NativeArray<byte>(length, Allocator.Temp);
            reader.ReadBytes(native);
            byte[] chunk = native.ToArray();
            native.Dispose();

            incoming.Write(chunk, 1, chunk.Length - 1);
            if (incoming.Length > NetProtocol.MaxMessageBytes)
            {
                incoming.SetLength(0);
                return null;
            }

            if (chunk[0] == 1)
            {
                return null;
            }

            byte[] message = incoming.ToArray();
            incoming.SetLength(0);
            return message;
        }
    }

    /// <summary>The host's side of Relay: players who joined with the internet code.</summary>
    public sealed class RelayHostTransport : HostTransport
    {
        private NetworkDriver driver;
        private NetworkPipeline pipeline;
        private readonly Dictionary<NetworkConnection, RelayPeer> peers = new Dictionary<NetworkConnection, RelayPeer>();
        private bool stopped;

        public string JoinCode { get; private set; } = "";

        public static async Task<RelayHostTransport> CreateAsync(int maxPlayersJoining)
        {
            await RelayNet.EnsureSignedInAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayersJoining);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            RelayServerData data = RelayNet.ServerData(allocation.ServerEndpoints, allocation.AllocationIdBytes, allocation.ConnectionData,
                allocation.ConnectionData, allocation.Key);
            RelayHostTransport transport = new RelayHostTransport { JoinCode = code };
            transport.driver = RelayNet.CreateDriver(ref data, out transport.pipeline);
            if (transport.driver.Bind(NetworkEndpoint.AnyIpv4) != 0 || transport.driver.Listen() != 0)
            {
                transport.Stop();
                throw new InvalidOperationException("Could not start the Relay connection.");
            }

            return transport;
        }

        public override void Pump()
        {
            if (stopped || !driver.IsCreated)
            {
                return;
            }

            driver.ScheduleUpdate().Complete();

            NetworkConnection accepted;
            while ((accepted = driver.Accept()) != default)
            {
                peers[accepted] = new RelayPeer(this, accepted);
            }

            NetworkEvent.Type type;
            while ((type = driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty)
            {
                if (!peers.TryGetValue(connection, out RelayPeer peer))
                {
                    continue;
                }

                if (type == NetworkEvent.Type.Data)
                {
                    byte[] message = peer.channel.Receive(ref reader);
                    if (message != null)
                    {
                        Inbox.Enqueue(new NetInbound(peer, message));
                    }
                }
                else if (type == NetworkEvent.Type.Disconnect)
                {
                    peers.Remove(connection);
                    peer.closed = true;
                    Inbox.Enqueue(new NetInbound(peer, null));
                }
            }

            List<NetworkConnection> finished = null;
            foreach (KeyValuePair<NetworkConnection, RelayPeer> pair in peers)
            {
                RelayPeer peer = pair.Value;
                peer.channel.Flush(driver, pipeline, pair.Key);
                if (peer.closing && (!peer.channel.HasPending || Time.realtimeSinceStartup > peer.closeDeadline))
                {
                    (finished ??= new List<NetworkConnection>()).Add(pair.Key);
                }
            }

            if (finished != null)
            {
                foreach (NetworkConnection connection in finished)
                {
                    peers[connection].closed = true;
                    peers.Remove(connection);
                    driver.Disconnect(connection);
                }
            }
        }

        internal void Send(RelayPeer peer, byte[] payload)
        {
            if (stopped || !driver.IsCreated)
            {
                return;
            }

            peer.channel.Enqueue(payload);
            peer.channel.Flush(driver, pipeline, peer.connection);
        }

        public override void Stop()
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            if (!driver.IsCreated)
            {
                return;
            }

            try
            {
                foreach (KeyValuePair<NetworkConnection, RelayPeer> pair in peers)
                {
                    pair.Value.channel.Flush(driver, pipeline, pair.Key);
                }

                driver.ScheduleUpdate().Complete();
                foreach (NetworkConnection connection in peers.Keys)
                {
                    driver.Disconnect(connection);
                }

                driver.ScheduleUpdate().Complete();
            }
            catch (Exception)
            {
                // shutting down anyway
            }

            peers.Clear();
            driver.Dispose();
        }

        /// <summary>One player connected through Relay.</summary>
        internal sealed class RelayPeer : NetConnection
        {
            private readonly RelayHostTransport owner;
            public readonly NetworkConnection connection;
            public readonly RelayChannel channel = new RelayChannel();
            public bool closing;
            public bool closed;
            public float closeDeadline;

            public RelayPeer(RelayHostTransport owner, NetworkConnection connection)
            {
                this.owner = owner;
                this.connection = connection;
            }

            public override bool Connected => !closed && !closing && !owner.stopped;
            public override string Remote => "Relay";

            public override bool Send(byte[] payload)
            {
                if (closed || closing)
                {
                    return false;
                }

                owner.Send(this, payload);
                return true;
            }

            /// <summary>Lets queued messages (e.g. "the game is full") go out, then hangs up.</summary>
            public override void Close()
            {
                if (closed || closing)
                {
                    return;
                }

                closing = true;
                closeDeadline = Time.realtimeSinceStartup + 1.5f;
            }
        }
    }

    /// <summary>A player's link to a host through Relay, using the host's 6-character code.</summary>
    public sealed class RelayClientLink : ClientLink
    {
        private const float ConnectTimeoutSeconds = 15f;

        private NetworkDriver driver;
        private NetworkPipeline pipeline;
        private NetworkConnection connection;
        private readonly RelayChannel channel = new RelayChannel();
        private float connectDeadline = float.MaxValue;
        private bool stopped;

        public async void Connect(string joinCode)
        {
            try
            {
                await RelayNet.EnsureSignedInAsync();
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                if (stopped)
                {
                    return;
                }

                RelayServerData data = RelayNet.ServerData(allocation.ServerEndpoints, allocation.AllocationIdBytes, allocation.ConnectionData,
                    allocation.HostConnectionData, allocation.Key);
                driver = RelayNet.CreateDriver(ref data, out pipeline);
                if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    throw new InvalidOperationException("Could not start the Relay connection.");
                }

                connection = driver.Connect(data.Endpoint);
                connectDeadline = Time.realtimeSinceStartup + ConnectTimeoutSeconds;
            }
            catch (Exception e)
            {
                Error = RelayNet.Explain(e);
                State = LinkState.Failed;
            }
        }

        protected override void Pump()
        {
            if (stopped || !driver.IsCreated)
            {
                return;
            }

            driver.ScheduleUpdate().Complete();

            NetworkEvent.Type type;
            while ((type = driver.PopEvent(out NetworkConnection from, out DataStreamReader reader)) != NetworkEvent.Type.Empty)
            {
                switch (type)
                {
                    case NetworkEvent.Type.Connect:
                        State = LinkState.Connected;
                        break;
                    case NetworkEvent.Type.Data:
                    {
                        byte[] message = channel.Receive(ref reader);
                        if (message != null)
                        {
                            Receive(message);
                        }

                        break;
                    }

                    case NetworkEvent.Type.Disconnect:
                        if (State == LinkState.Connecting)
                        {
                            Error = "The host's game could not be reached. Ask them for a fresh code.";
                            State = LinkState.Failed;
                        }
                        else
                        {
                            State = LinkState.Closed;
                            ReportClosed();
                        }

                        connection = default;
                        break;
                }
            }

            if (State == LinkState.Connecting && Time.realtimeSinceStartup > connectDeadline)
            {
                Error = "Timed out connecting to the host.";
                State = LinkState.Failed;
            }

            if (connection != default)
            {
                channel.Flush(driver, pipeline, connection);
            }
        }

        public override bool Send(byte[] payload)
        {
            if (stopped || !driver.IsCreated || connection == default)
            {
                return false;
            }

            channel.Enqueue(payload);
            channel.Flush(driver, pipeline, connection);
            return true;
        }

        public override void Stop()
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            if (State == LinkState.Connecting || State == LinkState.Connected)
            {
                State = LinkState.Closed;
            }

            if (!driver.IsCreated)
            {
                return;
            }

            try
            {
                if (connection != default)
                {
                    channel.Flush(driver, pipeline, connection);
                    driver.ScheduleUpdate().Complete();
                    driver.Disconnect(connection);
                    driver.ScheduleUpdate().Complete();
                }
            }
            catch (Exception)
            {
                // shutting down anyway
            }

            driver.Dispose();
        }
    }
}
