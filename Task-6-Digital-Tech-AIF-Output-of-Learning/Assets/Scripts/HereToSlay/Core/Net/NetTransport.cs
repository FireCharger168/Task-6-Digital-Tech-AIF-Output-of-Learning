using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace HereToSlay.Net
{
    /// <summary>
    /// One player's link to the host, whatever carries it (a direct TCP socket on the same network, or the
    /// Unity Relay service over the internet). Messages are whole byte arrays; order is preserved.
    /// </summary>
    public abstract class NetConnection
    {
        private static int nextId;

        public readonly int id = Interlocked.Increment(ref nextId);
        public string name = "";
        public int seatIndex = -1;

        public abstract bool Connected { get; }
        public abstract string Remote { get; }
        public abstract bool Send(byte[] payload);
        public abstract void Close();
    }

    public readonly struct NetInbound
    {
        public readonly NetConnection connection;
        /// <summary>null means the connection closed.</summary>
        public readonly byte[] payload;

        public NetInbound(NetConnection connection, byte[] payload)
        {
            this.connection = connection;
            this.payload = payload;
        }
    }

    /// <summary>Base for host and client: owns the inbox that transports fill (from any thread).</summary>
    public abstract class NetPeer
    {
        protected readonly ConcurrentQueue<NetInbound> inbox = new ConcurrentQueue<NetInbound>();

        /// <summary>Main-thread transports (e.g. Relay) do their per-frame work here.</summary>
        protected virtual void Pump()
        {
        }

        /// <summary>Hands every queued message to the handler on the calling thread.</summary>
        public void Poll(Action<NetInbound> handler)
        {
            Pump();
            while (inbox.TryDequeue(out NetInbound message))
            {
                handler(message);
            }
        }

        public abstract void Stop();
    }

    // ===================================================================== direct TCP

    /// <summary>A TCP connection carrying length-prefixed messages. A background thread reads.</summary>
    public sealed class TcpConnection : NetConnection
    {
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly object writeLock = new object();
        private readonly ConcurrentQueue<NetInbound> inbox;
        private volatile bool closed;
        private readonly string remote;

        public override bool Connected => !closed && client.Connected;
        public override string Remote => remote;

        internal TcpConnection(TcpClient tcp, ConcurrentQueue<NetInbound> inbox)
        {
            client = tcp;
            client.NoDelay = true;
            stream = tcp.GetStream();
            this.inbox = inbox;
            remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
            Thread reader = new Thread(ReadLoop) { IsBackground = true, Name = "HTS net reader " + id };
            reader.Start();
        }

        private void ReadLoop()
        {
            try
            {
                byte[] header = new byte[4];
                while (!closed)
                {
                    if (!ReadExactly(header, 4))
                    {
                        break;
                    }

                    int length = BitConverter.ToInt32(header, 0);
                    if (length <= 0 || length > NetProtocol.MaxMessageBytes)
                    {
                        break;
                    }

                    byte[] payload = new byte[length];
                    if (!ReadExactly(payload, length))
                    {
                        break;
                    }

                    inbox.Enqueue(new NetInbound(this, payload));
                }
            }
            catch (Exception)
            {
                // fall through to close
            }

            Close();
            inbox.Enqueue(new NetInbound(this, null));
        }

        private bool ReadExactly(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0)
                {
                    return false;
                }

                read += n;
            }

            return true;
        }

        public override bool Send(byte[] payload)
        {
            if (closed)
            {
                return false;
            }

            try
            {
                lock (writeLock)
                {
                    stream.Write(BitConverter.GetBytes(payload.Length), 0, 4);
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush();
                }

                return true;
            }
            catch (Exception)
            {
                Close();
                return false;
            }
        }

        public override void Close()
        {
            if (closed)
            {
                return;
            }

            closed = true;
            try
            {
                client.Close();
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    /// <summary>
    /// An extra way for players to reach the host (e.g. Unity Relay). It reports arrivals, messages and
    /// departures to the host's inbox and is pumped on the main thread every frame.
    /// </summary>
    public abstract class HostTransport
    {
        protected ConcurrentQueue<NetInbound> Inbox { get; private set; }

        internal void Bind(ConcurrentQueue<NetInbound> inbox)
        {
            Inbox = inbox;
        }

        public abstract void Pump();
        public abstract void Stop();
    }

    /// <summary>The host listens for players on a TCP port, plus any attached <see cref="HostTransport"/>s.</summary>
    public sealed class NetHost : NetPeer
    {
        private TcpListener listener;
        private volatile bool running;
        private readonly List<NetConnection> connections = new List<NetConnection>();
        private readonly List<HostTransport> transports = new List<HostTransport>();

        public int Port { get; private set; }

        /// <summary>Starts listening on the first free port from <paramref name="port"/> upwards.</summary>
        public void Start(int port = JoinCode.DefaultPort)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, port + attempt);
                    listener.Start();
                    Port = port + attempt;
                    running = true;
                    Thread accept = new Thread(AcceptLoop) { IsBackground = true, Name = "HTS net accept" };
                    accept.Start();
                    return;
                }
                catch (SocketException e)
                {
                    last = e;
                }
            }

            throw new IOException("Could not open a network port for hosting.", last);
        }

        public void AddTransport(HostTransport transport)
        {
            transport.Bind(inbox);
            transports.Add(transport);
        }

        protected override void Pump()
        {
            foreach (HostTransport transport in transports)
            {
                transport.Pump();
            }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient tcp = listener.AcceptTcpClient();
                    TcpConnection connection = new TcpConnection(tcp, inbox);
                    lock (connections)
                    {
                        connections.Add(connection);
                    }
                }
                catch (Exception)
                {
                    if (!running)
                    {
                        return;
                    }
                }
            }
        }

        public void Remove(NetConnection connection)
        {
            lock (connections)
            {
                connections.Remove(connection);
            }

            connection.Close();
        }

        public override void Stop()
        {
            running = false;
            try
            {
                listener?.Stop();
            }
            catch (Exception)
            {
                // ignored
            }

            List<NetConnection> open;
            lock (connections)
            {
                open = new List<NetConnection>(connections);
                connections.Clear();
            }

            foreach (NetConnection connection in open)
            {
                connection.Close();
            }

            // Give transports one last pump so goodbye messages get flushed, then shut them.
            Pump();
            foreach (HostTransport transport in transports)
            {
                transport.Stop();
            }

            transports.Clear();
        }
    }

    public enum LinkState
    {
        Connecting,
        Connected,
        Failed,
        Closed
    }

    /// <summary>A player's link to a host: direct TCP or Relay.</summary>
    public abstract class ClientLink : NetPeer
    {
        private volatile LinkState state = LinkState.Connecting;

        public LinkState State
        {
            get => state;
            protected set => state = value;
        }

        public string Error { get; protected set; } = "";
        public abstract bool Send(byte[] payload);

        /// <summary>Tell the session the link is gone (it sees a null payload).</summary>
        protected void ReportClosed()
        {
            inbox.Enqueue(new NetInbound(null, null));
        }

        protected void Receive(byte[] payload)
        {
            inbox.Enqueue(new NetInbound(null, payload));
        }
    }

    /// <summary>Direct TCP link (same network, VPN, or a forwarded port).</summary>
    public sealed class TcpClientLink : ClientLink
    {
        private TcpConnection connection;

        /// <summary>Connects on a background thread; watch <see cref="ClientLink.State"/>.</summary>
        public void Connect(IPEndPoint endPoint, int timeoutMs = 5000)
        {
            Thread thread = new Thread(() =>
            {
                try
                {
                    TcpClient tcp = new TcpClient(endPoint.AddressFamily);
                    IAsyncResult result = tcp.BeginConnect(endPoint.Address, endPoint.Port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(timeoutMs) || !tcp.Connected)
                    {
                        tcp.Close();
                        throw new IOException($"Could not reach a game at {endPoint}.");
                    }

                    tcp.EndConnect(result);
                    connection = new TcpConnection(tcp, inbox);
                    State = LinkState.Connected;
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    State = LinkState.Failed;
                }
            }) { IsBackground = true, Name = "HTS connect" };
            thread.Start();
        }

        public override bool Send(byte[] payload)
        {
            return connection != null && connection.Send(payload);
        }

        public override void Stop()
        {
            connection?.Close();
            State = LinkState.Closed;
        }
    }
}
