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
    /// One TCP connection carrying length-prefixed messages. A background thread reads; everything else
    /// (sending and handling) happens on the caller's thread via <see cref="NetPeer.Poll"/>.
    /// </summary>
    public sealed class NetConnection
    {
        private static int nextId = 1;

        public readonly int id;
        public string name = "";
        public int seatIndex = -1;

        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly object writeLock = new object();
        private readonly ConcurrentQueue<NetInbound> inbox;
        private volatile bool closed;

        public bool Connected => !closed && client.Connected;
        public string Remote { get; }

        internal NetConnection(TcpClient tcp, ConcurrentQueue<NetInbound> inbox)
        {
            id = Interlocked.Increment(ref nextId);
            client = tcp;
            client.NoDelay = true;
            stream = tcp.GetStream();
            this.inbox = inbox;
            Remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
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

        public bool Send(byte[] payload)
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

        public void Close()
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

    /// <summary>Base for host and client: owns the inbox that background threads fill.</summary>
    public abstract class NetPeer
    {
        protected readonly ConcurrentQueue<NetInbound> inbox = new ConcurrentQueue<NetInbound>();

        /// <summary>Hands every queued message to the handler on the calling thread.</summary>
        public void Poll(Action<NetInbound> handler)
        {
            while (inbox.TryDequeue(out NetInbound message))
            {
                handler(message);
            }
        }

        public abstract void Stop();
    }

    /// <summary>The host listens for players on a TCP port.</summary>
    public sealed class NetHost : NetPeer
    {
        private TcpListener listener;
        private volatile bool running;
        private readonly List<NetConnection> connections = new List<NetConnection>();

        public int Port { get; private set; }
        public IReadOnlyList<NetConnection> Connections => connections;

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

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient tcp = listener.AcceptTcpClient();
                    NetConnection connection = new NetConnection(tcp, inbox);
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

        public List<NetConnection> Snapshot()
        {
            lock (connections)
            {
                return new List<NetConnection>(connections);
            }
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

            foreach (NetConnection connection in Snapshot())
            {
                connection.Close();
            }

            lock (connections)
            {
                connections.Clear();
            }
        }
    }

    /// <summary>A player's connection to a host.</summary>
    public sealed class NetClient : NetPeer
    {
        public NetConnection Connection { get; private set; }
        public bool Connected => Connection != null && Connection.Connected;

        /// <summary>Connects (blocking up to the timeout). Call from a background thread or accept a short hitch.</summary>
        public void Connect(IPEndPoint endPoint, int timeoutMs = 5000)
        {
            TcpClient tcp = new TcpClient(endPoint.AddressFamily);
            IAsyncResult result = tcp.BeginConnect(endPoint.Address, endPoint.Port, null, null);
            if (!result.AsyncWaitHandle.WaitOne(timeoutMs) || !tcp.Connected)
            {
                tcp.Close();
                throw new IOException($"Could not reach a game at {endPoint}.");
            }

            tcp.EndConnect(result);
            Connection = new NetConnection(tcp, inbox);
        }

        public bool Send(byte[] payload)
        {
            return Connection != null && Connection.Send(payload);
        }

        public override void Stop()
        {
            Connection?.Close();
        }
    }
}
