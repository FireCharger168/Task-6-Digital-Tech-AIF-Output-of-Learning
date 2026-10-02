using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace HereToSlay.Net
{
    /// <summary>
    /// A join code is the host's IPv4 address and port (6 bytes) written as 10 easy-to-read characters, e.g. "8F2KQ-7DM0X".
    /// Players can also type the address directly, e.g. "192.168.1.20:7777".
    /// </summary>
    public static class JoinCode
    {
        public const int DefaultPort = 7777;

        // Crockford base32: no I, L, O or U, so codes are hard to misread.
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string Encode(IPAddress address, int port)
        {
            byte[] ip = address.GetAddressBytes();
            if (ip.Length != 4)
            {
                throw new ArgumentException("Join codes need an IPv4 address.");
            }

            ulong value = 0;
            foreach (byte b in ip)
            {
                value = (value << 8) | b;
            }

            value = (value << 16) | (ushort)port;

            char[] chars = new char[10];
            for (int i = 9; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(value & 31)];
                value >>= 5;
            }

            string code = new string(chars);
            return code.Substring(0, 5) + "-" + code.Substring(5);
        }

        public static bool TryDecode(string text, out IPEndPoint endPoint)
        {
            endPoint = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();

            // Direct address: "1.2.3.4" or "1.2.3.4:7777" or "localhost"
            if (text.Contains(".") || text.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                string host = text;
                int port = DefaultPort;
                int colon = text.LastIndexOf(':');
                if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int parsedPort))
                {
                    host = text.Substring(0, colon);
                    port = parsedPort;
                }

                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    endPoint = new IPEndPoint(IPAddress.Loopback, port);
                    return true;
                }

                if (IPAddress.TryParse(host, out IPAddress address))
                {
                    endPoint = new IPEndPoint(address, port);
                    return true;
                }

                return false;
            }

            StringBuilder clean = new StringBuilder();
            foreach (char raw in text.ToUpperInvariant())
            {
                char c = raw;
                if (c == '-' || c == ' ')
                {
                    continue;
                }

                // Forgive the usual look-alikes.
                if (c == 'O') c = '0';
                if (c == 'I' || c == 'L') c = '1';
                if (c == 'U') c = 'V';
                clean.Append(c);
            }

            if (clean.Length != 10)
            {
                return false;
            }

            ulong value = 0;
            foreach (char c in clean.ToString())
            {
                int digit = Alphabet.IndexOf(c);
                if (digit < 0)
                {
                    return false;
                }

                value = (value << 5) | (uint)digit;
            }

            int decodedPort = (int)(value & 0xFFFF);
            value >>= 16;
            byte[] bytes = new byte[4];
            for (int i = 3; i >= 0; i--)
            {
                bytes[i] = (byte)(value & 0xFF);
                value >>= 8;
            }

            endPoint = new IPEndPoint(new IPAddress(bytes), decodedPort);
            return true;
        }

        /// <summary>Best guess at this computer's address on the local network (prefers 192.168.x / 10.x / 172.16-31.x with a gateway).</summary>
        public static IPAddress LocalAddress()
        {
            List<(IPAddress address, int score)> candidates = new List<(IPAddress, int)>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    IPInterfaceProperties properties = nic.GetIPProperties();
                    bool hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    string description = (nic.Description + " " + nic.Name).ToLowerInvariant();
                    bool virtualAdapter = description.Contains("virtual") || description.Contains("vmware") || description.Contains("hyper-v") || description.Contains("vbox") || description.Contains("wsl");
                    foreach (UnicastIPAddressInformation info in properties.UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(info.Address))
                        {
                            continue;
                        }

                        byte[] b = info.Address.GetAddressBytes();
                        int score = 0;
                        if (b[0] == 192 && b[1] == 168) score += 30;
                        else if (b[0] == 10) score += 25;
                        else if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) score += 20;
                        else if (b[0] == 169 && b[1] == 254) score -= 50;
                        if (hasGateway) score += 40;
                        if (virtualAdapter) score -= 30;
                        candidates.Add((info.Address, score));
                    }
                }
            }
            catch (Exception)
            {
                // Some platforms do not support NetworkInterface; fall back to DNS below.
            }

            if (candidates.Count == 0)
            {
                try
                {
                    foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    {
                        if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                        {
                            candidates.Add((address, 0));
                        }
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            return candidates.Count == 0 ? IPAddress.Loopback : candidates.OrderByDescending(c => c.score).First().address;
        }
    }
}
