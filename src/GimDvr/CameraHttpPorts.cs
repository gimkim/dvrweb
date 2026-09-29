using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;

namespace GimDvr;

// Camera firmware can choose a new CGI port after reboot. RTSP is independent.
public sealed class CameraHttpPorts
{
    sealed class Entry { public readonly SemaphoreSlim Gate = new(1, 1); public int Port; public DateTimeOffset Expires; }
    readonly ConcurrentDictionary<string, Entry> entries = new();
    static string Key(Camera c) => $"{c.Id}|{c.Host}|{c.HttpPort}|{c.Uid}";
    public void Invalidate(Camera c) { if (entries.TryGetValue(Key(c), out var e)) Volatile.Write(ref e.Port, 0); }
    public async Task<int> Resolve(Camera c, CancellationToken ct, bool refreshExpired = false)
    {
        var e = entries.GetOrAdd(Key(c), _ => new());
        ct.ThrowIfCancellationRequested();
        var cached = Volatile.Read(ref e.Port);
        if (!refreshExpired && cached != 0) return cached;
        await e.Gate.WaitAsync(ct);
        try
        {
            // A held pan/stop must reuse its port, never pause for periodic discovery.
            if (e.Port != 0 && (!refreshExpired || e.Expires > DateTimeOffset.UtcNow)) return e.Port;
            var found = await Discover(c, ct);
            e.Port = found ?? c.HttpPort;
            e.Expires = DateTimeOffset.UtcNow.AddSeconds(found.HasValue ? 300 : 15);
            return e.Port;
        }
        finally { e.Gate.Release(); }
    }
    public static int? ParseReply(byte[] data, IPAddress sender, Camera c)
    {
        if (!IPAddress.TryParse(c.Host, out var target) || !sender.Equals(target) || data.Length < 124 ||
            data[0] != 0x44 || data[1] != 0x48 || data[2] != 1 || data[3] != 8) return null;
        var ip = Encoding.ASCII.GetString(data, 4, 16).TrimEnd('\0');
        if (ip != c.Host) return null;
        var uid = Encoding.ASCII.GetString(data, 92, 32).Split('\0')[0];
        if (!string.IsNullOrWhiteSpace(c.Uid) && !string.Equals(uid, c.Uid, StringComparison.OrdinalIgnoreCase)) return null;
        var port = data[90] | data[91] << 8;
        return port == 0 ? null : port;
    }
    static IPAddress BroadcastFor(IPAddress target)
    {
        var address = target.GetAddressBytes();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        foreach (var local in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
        {
            var ip = local.Address.GetAddressBytes(); var mask = local.IPv4Mask.GetAddressBytes();
            if (Enumerable.Range(0,4).All(i => (ip[i] & mask[i]) == (address[i] & mask[i])))
                return new IPAddress(Enumerable.Range(0,4).Select(i => (byte)(address[i] | ~mask[i])).ToArray());
        }
        return IPAddress.Broadcast;
    }
    static async Task<int?> Discover(Camera c, CancellationToken ct)
    {
        if (!IPAddress.TryParse(c.Host, out var target)) return null;
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        // Some firmware answers only broadcast discovery. Accept only this camera's reply.
        foreach (var destination in new[] { target, BroadcastFor(target) })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(destination.Equals(target) ? 1 : 3));
            try
            {
                await udp.SendAsync(new byte[] { 0x44, 0x48, 1, 1 }, new IPEndPoint(destination, 8600), timeout.Token);
                while (true)
                {
                    var reply = await udp.ReceiveAsync(timeout.Token);
                    if (ParseReply(reply.Buffer, reply.RemoteEndPoint.Address, c) is int port) return port;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (SocketException) { }
        }
        return null;
    }
}
