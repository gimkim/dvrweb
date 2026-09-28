using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
namespace GimDvr;
public sealed record WebRtcOffer(string Sdp);
public sealed class WebRtcService(Store store,MediaService media,Paths paths,ILogger<WebRtcService> log):BackgroundService
{
    const string Origin="http://127.0.0.1:18889";
    readonly HttpClient http=new(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
    readonly ConcurrentDictionary<string,Session> sessions=new();
    readonly Dictionary<string,int> registered=new();
    readonly SemaphoreSlim gate=new(1),slots=new(64);
    volatile bool ready;
    Process? gateway;
    string? lastGatewayWarning;
    public object Status()=>new{ready,activeSessions=sessions.Count};
    void PublishStatus(){try{var file=Path.Combine(paths.Runtime,"webrtc-state.json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(new{updated=DateTimeOffset.UtcNow,ready,activeSessions=sessions.Count,version="1.21.1",processId=gateway?.Id,lastGatewayWarning}));File.Move(file+".tmp",file,true);}catch(IOException){}}
    sealed class Session(string user,string stamp,string camera,int port,Uri upstream){public string User=user,Stamp=stamp,Camera=camera;public int Port=port;public Uri Upstream=upstream;public long Seen=Environment.TickCount64;}
    public static bool ValidOffer(string? sdp)=>sdp is {Length:>0 and <=100000}&&sdp.StartsWith("v=0")&&sdp.Contains("m=video ")&&sdp.Contains("a=recvonly")&&!sdp.Contains("a=sendonly")&&!sdp.Contains("a=sendrecv");
    public static Uri SessionLocation(string camera,Uri? location){
        if(location is null)throw new IOException("Missing WebRTC session");
        var uri=new Uri(new Uri(Origin+"/"+Uri.EscapeDataString(camera)+"/whep"),location);
        var prefix="/"+Uri.EscapeDataString(camera)+"/whep/";
        if(uri.Scheme!="http"||uri.Host!="127.0.0.1"||uri.Port!=18889||!uri.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParse(uri.AbsolutePath[prefix.Length..],out _)||uri.Query.Length!=0||uri.Fragment.Length!=0)throw new IOException("Invalid WebRTC session");
        return uri;
    }
    public async Task<object> Create(Camera camera,string sdp,ClaimsPrincipal user,CancellationToken ct){
        if(!ValidOffer(sdp))throw new ArgumentException("Invalid receive-only WebRTC offer");
        if(!ready)throw new IOException("WebRTC gateway unavailable");
        if(!await slots.WaitAsync(0,ct))throw new IOException("WebRTC session limit reached");
        Uri? upstream=null;bool saved=false;
        try{
            media.Watch(camera.Id);int? port=null;
            for(int i=0;i<80;i++){port=media.WebRtcPort(camera.Id);if(port is >0)break;await Task.Delay(100,ct);}
            if(port is not >0)throw new IOException("WebRTC source unavailable");
            await gate.WaitAsync(ct);
            try{
                if(!registered.TryGetValue(camera.Id,out var current)||current!=port){
                    var method=current>0?HttpMethod.Patch:HttpMethod.Post;
                    var action=current>0?"patch":"add";
                    using var request=new HttpRequestMessage(method,$"http://127.0.0.1:19997/v3/config/paths/{action}/{Uri.EscapeDataString(camera.Id)}"){
                        Content=JsonContent.Create(new{source=$"udp+mpegts://127.0.0.1:{port}",sourceOnDemand=true,sourceOnDemandStartTimeout="15s",sourceOnDemandCloseAfter="2s"})};
                    using var result=await http.SendAsync(request,ct);result.EnsureSuccessStatusCode();registered[camera.Id]=port.Value;
                }
            }finally{gate.Release();}
            // Do not cancel the upstream POST after it creates a session: always capture Location for cleanup.
            using var offer=new StringContent(sdp,Encoding.UTF8,"application/sdp");
            using var response=await http.PostAsync(Origin+"/"+Uri.EscapeDataString(camera.Id)+"/whep",offer);
            if(response.StatusCode!=HttpStatusCode.Created)throw new IOException("WebRTC negotiation failed");
            upstream=SessionLocation(camera.Id,response.Headers.Location);
            var answer=await response.Content.ReadAsStringAsync(ct);
            if(answer.Length>100000||!answer.StartsWith("v=0"))throw new IOException("Invalid WebRTC answer");
            ct.ThrowIfCancellationRequested();var id=Guid.NewGuid().ToString("N");
            sessions[id]=new(user.FindFirstValue(ClaimTypes.NameIdentifier)!,user.FindFirstValue("stamp")!,camera.Id,port.Value,upstream);saved=true;
            log.LogInformation("WebRTC session opened for camera {Camera}; active={Count}",camera.Id,sessions.Count);
            return new{session=id,sdp=answer};
        }finally{if(!saved){if(upstream is not null)await Delete(upstream);slots.Release();}}
    }
    public bool Touch(string id,ClaimsPrincipal user){if(!sessions.TryGetValue(id,out var s)||s.User!=user.FindFirstValue(ClaimTypes.NameIdentifier)||s.Stamp!=user.FindFirstValue("stamp"))return false;Interlocked.Exchange(ref s.Seen,Environment.TickCount64);return true;}
    public async Task CloseOwned(string id,ClaimsPrincipal user){if(sessions.TryGetValue(id,out var s)&&s.User==user.FindFirstValue(ClaimTypes.NameIdentifier))await Close(id,s);}
    async Task<bool> Delete(Uri uri){try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));using var response=await http.DeleteAsync(uri,timeout.Token);return response.IsSuccessStatusCode||response.StatusCode==HttpStatusCode.NotFound;}catch(HttpRequestException){return false;}catch(TaskCanceledException){return false;}}
    async Task Close(string id,Session s){if(!await Delete(s.Upstream)){ready=false;try{gateway?.Kill(true);}catch(InvalidOperationException){}return;}if(sessions.TryRemove(id,out _)){slots.Release();log.LogInformation("WebRTC session closed; active={Count}",sessions.Count);}}
    protected override async Task ExecuteAsync(CancellationToken ct){
        var exe=Path.Combine(Path.GetDirectoryName(paths.Ffmpeg)??"","mediamtx.exe");
        if(!File.Exists(exe)){log.LogWarning("WebRTC gateway binary missing; copy fallback remains available");return;}
        Directory.CreateDirectory(paths.Runtime);var config=Path.Combine(paths.Runtime,"mediamtx.json");
        await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new{
            logLevel="warn",logDestinations=new[]{"stdout"},udpReadBufferSize=4194304,writeQueueSize=2048,api=true,apiAddress="127.0.0.1:19997",rtsp=false,rtmp=false,hls=false,srt=false,moq=false,
            webrtc=true,webrtcAddress="127.0.0.1:18889",webrtcLocalUDPAddress=":8189",webrtcLocalTCPAddress=":8189",webrtcIPsFromInterfaces=true,webrtcAdditionalHosts=new[]{"gimgim.ddns.net"},
            authInternalUsers=new[]{new{user="any",pass="",ips=new[]{"127.0.0.1"},permissions=new[]{new{action="read",path=""},new{action="api",path=""}}}},paths=new Dictionary<string,object>()}),ct);
        while(!ct.IsCancellationRequested){
            using var process=new Process{StartInfo=new(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};IDisposable? job=null;
            try{
                process.StartInfo.ArgumentList.Add(config);process.Start();gateway=process;job=ProcessJob.Attach(process);
                process.OutputDataReceived+=(_,e)=>{if(e.Data is not null){lastGatewayWarning=System.Text.RegularExpressions.Regex.Replace(e.Data.Length>512?e.Data[..512]:e.Data,@"[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,}","[id]");log.LogWarning("WebRTC gateway reported a warning");}};process.ErrorDataReceived+=(_,e)=>{if(e.Data is not null)log.LogWarning("WebRTC gateway reported an error");};process.BeginOutputReadLine();process.BeginErrorReadLine();
                await gate.WaitAsync(ct);try{registered.Clear();}finally{gate.Release();}
                for(int i=0;i<30&&!process.HasExited;i++){try{using var r=await http.GetAsync("http://127.0.0.1:19997/v3/config/global/get",ct);if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200,ct);}
                log.LogInformation("WebRTC gateway ready={Ready}",ready);
                while(!process.HasExited&&!ct.IsCancellationRequested){
                    var users=store.Users();var cameras=store.Cameras();
                    foreach(var pair in sessions.ToArray()){
                        var s=pair.Value;var u=users.Find(x=>x.Id==s.User);var c=cameras.Find(x=>x.Id==s.Camera);
                        if(Environment.TickCount64-Interlocked.Read(ref s.Seen)>12000||u is null||!u.Enabled||u.Stamp!=s.Stamp||c is null||!c.Enabled||media.WebRtcPort(s.Camera)!=s.Port)await Close(pair.Key,s);
                        else media.Watch(s.Camera);
                    }
                    PublishStatus();await Task.Delay(2000,ct);
                }
            }catch(OperationCanceledException)when(ct.IsCancellationRequested){}catch(Exception e){log.LogWarning("WebRTC gateway stopped: {Type}",e.GetType().Name);}
            finally{ready=false;gateway=null;PublishStatus();try{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}catch(InvalidOperationException){}job?.Dispose();foreach(var id in sessions.Keys)if(sessions.TryRemove(id,out _))slots.Release();}
            if(!ct.IsCancellationRequested)await Task.Delay(3000,ct);
        }
    }
}
