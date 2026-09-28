using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace GimDvr;

public sealed class RemoteDetection:IDisposable
{
    readonly HttpClient? client;
    readonly Action<string,object> events;
    int available=-1;
    public bool Available=>Volatile.Read(ref available)==1;
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    public RemoteDetection(IConfiguration config,Action<string,object> events,HttpMessageHandler? handler=null)
    {
        this.events=events;
        var url=config["Dvr:DetectionRemoteUrl"];var key=config["Dvr:DetectionRemoteKey"];
        if(string.IsNullOrWhiteSpace(url)||string.IsNullOrWhiteSpace(key))return;
        var uri=new Uri(url.TrimEnd('/')+"/");
        if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo))throw new ArgumentException("Remote detection requires HTTPS");
        var pin=config["Dvr:DetectionRemoteCertificateSha256"];
        handler??=new HttpClientHandler{AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,cert,_,errors)=>TrustedCertificate(cert,errors,pin)};
        client=new(handler){BaseAddress=uri,Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",key);
    }
    public static bool TrustedCertificate(X509Certificate2? cert,SslPolicyErrors errors,string? pin)
    {
        if(string.IsNullOrWhiteSpace(pin))return errors==SslPolicyErrors.None;
        return cert is not null&&DateTime.UtcNow>=cert.NotBefore.ToUniversalTime()&&DateTime.UtcNow<cert.NotAfter.ToUniversalTime()
            &&string.Equals(Convert.ToHexString(SHA256.HashData(cert.RawData)),pin,StringComparison.OrdinalIgnoreCase);
    }
    void Set(bool next,string reason)
    {
        if(Interlocked.Exchange(ref available,next?1:0)!=(next?1:0))events("remote_status",new{available=next,reason});
    }
    public async Task Probe(CancellationToken ct)
    {
        if(client is null)return;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response=await client.GetAsync("health",timeout.Token);
            var ready=response.IsSuccessStatusCode&&(await response.Content.ReadFromJsonAsync<RemoteHealth>(timeout.Token)) is {Ready:true,Protocol:1};
            Set(ready,ready?"ready":"not_ready");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch{Set(false,"probe_failed");}
    }
    public async Task Poll(CancellationToken ct)
    {
        if(client is null)return;
        try{while(!ct.IsCancellationRequested){await Probe(ct);await Task.Delay(TimeSpan.FromSeconds(15),ct);}}
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
    }
    public async Task<DetectionResult?> Analyze(Recording recording,CancellationToken ct)
    {
        if(client is null||!Available)return null;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            await using var stream=new FileStream(recording.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
            if(stream.Length>256L*1024*1024)return null;
            using var body=new StreamContent(stream);body.Headers.ContentType=new("application/octet-stream");
            using var response=await client.PostAsync("analyze?duration="+recording.Duration.ToString("R",CultureInfo.InvariantCulture),body,timeout.Token);
            response.EnsureSuccessStatusCode();
            var result=await response.Content.ReadFromJsonAsync<DetectionResult>(Json,timeout.Token);
            if(result is not {State:"complete",Motion:not null,Human:not null}||result.Frames<Math.Max(2,(int)(recording.Duration*2)-1)||result.HumanSamples<2||!double.IsFinite(result.Confidence))throw new IOException("Incomplete remote result");
            events("remote_complete",new{recordingId=recording.Id,result.Device,result.Decoder});
            return result;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception e){Set(false,"request_failed");events("remote_fallback",new{recordingId=recording.Id,errorType=e.GetType().Name});return null;}
    }
    public void Dispose()=>client?.Dispose();
    sealed record RemoteHealth(bool Ready,int Protocol);
}
