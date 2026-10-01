using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GimDvr;

// Discovery gathers LAN candidates; MAC (and configured UID) establishes identity.
public class CameraNetworkDiscovery(Store store, ILogger<CameraNetworkDiscovery> log,Paths? paths=null)
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates=new();
    readonly ConcurrentDictionary<string, DateTimeOffset> checkedUntil=new();
    public static string? NormalizeMac(string? value)
    {
        if(value is null)return null;
        var clean=value.Replace("-", "").Replace(":", "");
        return Regex.IsMatch(clean,"^[0-9a-fA-F]{12}$")?clean.ToUpperInvariant():null;
    }
    static string Key(Camera c)=>$"{c.Id}|{c.Host}|{c.Revision}|{c.MacAddress}";
    public void Invalidate(Camera c)=>checkedUntil.TryRemove(Key(c),out _);
    public async Task<Camera> Resolve(Camera camera, CancellationToken ct)
    {
        var mac=NormalizeMac(camera.MacAddress);
        if(mac is null || !OperatingSystem.IsWindows() && GetType()==typeof(CameraNetworkDiscovery))return camera;
        var gate=gates.GetOrAdd(camera.Id,_=>new(1,1));await gate.WaitAsync(ct);
        try
        {
            // Reread on NAS locally; never overwrite a concurrent administrator edit.
            camera=store.Camera(camera.Id);var key=Key(camera);
            mac=NormalizeMac(camera.MacAddress);if(mac is null||!camera.Enabled)return camera;
            if(checkedUntil.GetValueOrDefault(key)>DateTimeOffset.UtcNow)return camera;
            checkedUntil[key]=DateTimeOffset.UtcNow.AddSeconds(15);
            if(await Reachable(camera,ct) && NormalizeMac(await ReadMac(camera.Host,ct))==mac)return camera;
            foreach(var candidate in await Discover(camera,ct))
            {
                if(NormalizeMac(await ReadMac(candidate.Host,ct))!=mac)continue;
                var updated=store.UpdateCameraNetwork(camera,candidate.Host,candidate.Port);
                checkedUntil[Key(updated)]=DateTimeOffset.UtcNow.AddSeconds(15);
                if(updated.Host!=camera.Host||updated.HttpPort!=camera.HttpPort)
                {
                    log.LogInformation("Camera {Id} address recovered by MAC: {OldHost} -> {Host}",camera.Id,camera.Host,updated.Host);
                    if(paths is not null)try
                    {
                        Directory.CreateDirectory(paths.Runtime);
                        File.WriteAllText(Path.Combine(paths.Runtime,updated.Id+".network.json"),System.Text.Json.JsonSerializer.Serialize(new{updated.Id,updated.Host,updated.HttpPort,updated.MacAddress,previousHost=camera.Host,recoveredAt=DateTimeOffset.UtcNow}));
                    }
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){log.LogWarning("Cannot save camera network diagnostics ({Type})",ex.GetType().Name);}
                }
                return updated;
            }
            return camera;
        }
        finally{gate.Release();}
    }
    protected virtual async Task<bool> Reachable(Camera c,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(750);
        using var socket=new TcpClient();
        try{await socket.ConnectAsync(c.Host,c.RtspPort,deadline.Token);return true;}
        catch(SocketException){return false;}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return false;}
    }
    [DllImport("iphlpapi.dll",ExactSpelling=true)]
    static extern int SendARP(uint destination,uint source,byte[] address,ref uint length);
    protected virtual async Task<string?> ReadMac(string host,CancellationToken ct)
    {
        if(!IPAddress.TryParse(host,out var ip))return null;
        try{return await Task.Run(()=>{var bytes=new byte[6];uint length=6;return SendARP(BitConverter.ToUInt32(ip.GetAddressBytes()),0,bytes,ref length)==0&&length==6?Convert.ToHexString(bytes):null;},ct).WaitAsync(TimeSpan.FromSeconds(2),ct);}
        catch(TimeoutException){return null;}
    }
    public static (string Host,int Port)? ParseCandidate(byte[] data,IPAddress sender,string? uid)
    {
        if(data.Length<124||data[0]!=0x44||data[1]!=0x48||data[2]!=1||data[3]!=8)return null;
        var host=Encoding.ASCII.GetString(data,4,16).TrimEnd('\0');
        if(host!=sender.ToString()||sender.AddressFamily!=AddressFamily.InterNetwork)return null;
        var b=sender.GetAddressBytes();if(!(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31))return null;
        if(!string.IsNullOrWhiteSpace(uid)&&Encoding.ASCII.GetString(data,92,32).Split('\0')[0]!=uid)return null;
        int port=data[90]|data[91]<<8;return port>0?(host,port):null;
    }
    protected virtual async Task<List<(string Host,int Port)>> Discover(Camera c,CancellationToken ct)
    {
        var candidates=new List<(string Host,int Port)>();
        if(c.Driver!="vstarcam")return candidates;
        using var udp=new UdpClient(AddressFamily.InterNetwork){EnableBroadcast=true};udp.Client.Bind(new IPEndPoint(IPAddress.Any,0));
        var broadcasts=new HashSet<IPAddress>();
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up))
        foreach(var local in nic.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork))
        {
            var ip=local.Address.GetAddressBytes();var mask=local.IPv4Mask.GetAddressBytes();
            if(ip[0]==10||ip[0]==192&&ip[1]==168||ip[0]==172&&ip[1]>=16&&ip[1]<=31)
                broadcasts.Add(new IPAddress(Enumerable.Range(0,4).Select(i=>(byte)(ip[i]|~mask[i])).ToArray()));
        }
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(2500);
        try
        {
            foreach(var destination in broadcasts)await udp.SendAsync(new byte[]{0x44,0x48,1,1},new IPEndPoint(destination,8600),deadline.Token);
            while(true){var reply=await udp.ReceiveAsync(deadline.Token);if(ParseCandidate(reply.Buffer,reply.RemoteEndPoint.Address,c.Uid) is {} candidate&&!candidates.Contains(candidate))candidates.Add(candidate);}
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){}
        catch(SocketException){}
        return candidates;
    }
}
