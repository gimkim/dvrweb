using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GimDvr;

public sealed partial class MediaService(Store store,CameraClient cameras,Paths paths,ILogger<MediaService> log) : BackgroundService
{
    readonly ConcurrentDictionary<string, DateTimeOffset> watchers=new();
    readonly ConcurrentDictionary<string, Run> runs=new();
    readonly ConcurrentDictionary<string, string> errors=new();
    readonly ConcurrentDictionary<string, DateTimeOffset> retryAfter=new();
    readonly Dictionary<string,long> indexedLengths=new(StringComparer.OrdinalIgnoreCase);
    readonly object leaseGate=new();
    readonly Dictionary<string,DateTimeOffset> leaseWrites=new();
    sealed record Manifest(string CameraId,string CameraName,string Root,string Csv,string Session);
    sealed class Run(Camera camera,Process process,string live,Manifest? manifest,IDisposable? job)
    {
        public int WebRtcPort; public int SegmentMs; public Task? FragmentPump; public string Overview=live; public Camera Camera=camera;public Process Process=process;public string Live=live;public Manifest? Manifest=manifest;public IDisposable? Job=job; public Process? Encoder; public IDisposable? EncoderJob; public int RelayPort; public bool QsvFailed; public string EncoderName="none"; public DateTimeOffset EncoderStarted;
    }
    sealed record SharedState(DateTimeOffset Updated,bool Running,bool Recording,string? Live,int? ProcessId,string? Error,int? EncoderProcessId=null,string? Encoder=null,string? Overview=null,int? WebRtcPort=null);
    public void Watch(string id,bool focus=false)
    {
        if(focus)id+=".focus";
        var now=DateTimeOffset.UtcNow;watchers[id]=now;
        if(paths.ExternalMedia)lock(leaseGate)
        {
            if(now-leaseWrites.GetValueOrDefault(id)<=TimeSpan.FromSeconds(2))return;
            Directory.CreateDirectory(paths.Runtime);
            File.WriteAllText(Path.Combine(paths.Runtime,id+".watch"),now.ToString("O"));
            leaseWrites[id]=now;
        }
    }
    DateTimeOffset LastWatch(string id,bool focus=false)
    {
        if(focus)id+=".focus";
        var file=Path.Combine(paths.Runtime,id+".watch");
        return new[]{watchers.GetValueOrDefault(id),File.Exists(file)?new DateTimeOffset(File.GetLastWriteTimeUtc(file)):DateTimeOffset.MinValue}.Max();
    }
    DateTimeOffset LastAnyWatch(string id)=>new[]{LastWatch(id),LastWatch(id,true)}.Max();
    SharedState? ReadShared(string id)
    {
        try{using var f=new FileStream(Path.Combine(paths.Runtime,id+".json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var s=JsonSerializer.Deserialize<SharedState>(f);return s?.Updated>DateTimeOffset.UtcNow.AddSeconds(-10)?s:null;}
        catch(IOException){return null;}catch(JsonException){return null;}
    }
    void PublishState(Camera c)
    {
        var active=runs.TryGetValue(c.Id,out var run)&&!run.Process.HasExited;
        var state=new SharedState(DateTimeOffset.UtcNow,active,active&&run!.Camera.RecordingEnabled,active&&run!.Encoder is {HasExited:false}?run.Live:null,active?run!.Process.Id:null,errors.GetValueOrDefault(c.Id),active&&run!.Encoder is {HasExited:false}?run.Encoder.Id:null,run?.EncoderName,active?run!.Overview:null,active?run!.WebRtcPort:null);
        var file=Path.Combine(paths.Runtime,c.Id+".json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(state));File.Move(file+".tmp",file,true);
    }
    public int? WebRtcPort(string id)=>paths.ExternalMedia?ReadShared(id)?.WebRtcPort:runs.TryGetValue(id,out var run)&&!run.Process.HasExited?run.WebRtcPort:null;
    public static string[] WebRtcRelayArguments(int port)=>["-map","0:v:0","-c:v","copy","-an","-f","mpegts","-mpegts_flags","resend_headers","-muxdelay","0","-flush_packets","1",$"udp://127.0.0.1:{port}?pkt_size=1316"];
    public object Status(string id)
    {
        if(paths.ExternalMedia)
        {
            var state=ReadShared(id);
            return new{running=state?.Running??false,recording=state?.Recording??false,liveReady=state?.Overview is {} live&&File.Exists(Path.Combine(live,"index.m3u8")),error=state?.Error??(state is null?"Background recorder ยังไม่พร้อม กรุณาตรวจ Windows Service":null),owner="worker",encoding=state?.EncoderProcessId is not null,encoder=state?.Encoder,encoderProcessId=state?.EncoderProcessId,processId=state?.ProcessId,inputConnections=state?.Running==true?1:0};
        }
        var active=runs.TryGetValue(id,out var run)&&!run.Process.HasExited;
        return new{running=active,recording=active&&run!.Camera.RecordingEnabled,liveReady=active&&File.Exists(Path.Combine(run!.Overview,"index.m3u8")),error=errors.GetValueOrDefault(id),owner="web",encoding=active&&run!.Encoder is {HasExited:false},encoder=active?run!.EncoderName:null,encoderProcessId=active&&run!.Encoder is {HasExited:false}?(int?)run.Encoder.Id:null,processId=active?(int?)run!.Process.Id:null,inputConnections=active?1:0};
    }
    public string? LiveFile(string id,string name,bool focus=false)
    {
        if(!Regex.IsMatch(name,@"^(index\.m3u8|seg\d+\.ts)$"))return null;
        Watch(id,focus);
        if(paths.ExternalMedia)
        {
            var state=ReadShared(id);var live=focus?state?.Live:state?.Overview;
            return live is not null&&Path.GetFullPath(live).StartsWith(Path.Combine(paths.Live,id)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?Path.Combine(live,name):null;
        }
        return runs.TryGetValue(id,out var run)&&!run.Process.HasExited&&(!focus||run.Encoder is {HasExited:false})?Path.Combine(focus?run.Live:run.Overview,name):null;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(paths.ExternalMedia)return; // Web proxy never starts a second RTSP reader in worker mode.
        Directory.CreateDirectory(paths.Live);Directory.CreateDirectory(paths.Manifests);Directory.CreateDirectory(paths.Runtime);
        FileStream? singleton=null;
        while(!stoppingToken.IsCancellationRequested&&singleton is null)
        {try{singleton=new FileStream(Path.Combine(paths.Data,"recorder.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){await Task.Delay(2000,stoppingToken);}}
        using(singleton)
        try
        {
            var nextSweep=DateTimeOffset.MinValue;
            while(!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var configured=store.Cameras();var segmentMs=store.StreamSettings().SegmentMs;
                    foreach(var pair in runs.ToArray())
                    {
                        var current=configured.Find(c=>c.Id==pair.Key);
                        var wanted=current is not null&&current.Enabled&&(current.RecordingEnabled||LastAnyWatch(current.Id)>DateTimeOffset.UtcNow.AddSeconds(-8));
                        if(!wanted||current!.Revision!=pair.Value.Camera.Revision||pair.Value.Process.HasExited||pair.Value.SegmentMs!=segmentMs)
                        {await Stop(pair.Key);retryAfter[pair.Key]=DateTimeOffset.UtcNow.AddSeconds(3);}
                    }
                    foreach(var c in configured.Where(c=>c.Enabled&&(c.RecordingEnabled||LastAnyWatch(c.Id)>DateTimeOffset.UtcNow.AddSeconds(-8))))
                    {
                        if(runs.ContainsKey(c.Id)||retryAfter.GetValueOrDefault(c.Id)>DateTimeOffset.UtcNow)continue;
                        try{Start(c);}
                        catch(Exception e){errors[c.Id]=Sanitize(e.Message,c);retryAfter[c.Id]=DateTimeOffset.UtcNow.AddSeconds(15);log.LogWarning("Cannot start camera {Id}: {Error}",c.Id,errors[c.Id]);}
                    }
                    foreach(var run in runs.Values)await UpdateEncoder(run);
                    IndexCompleted();
                    foreach(var c in configured)PublishState(c);
                    File.WriteAllText(Path.Combine(paths.Runtime,"worker-heartbeat.txt"),DateTimeOffset.UtcNow.ToString("O"));
                    if(DateTimeOffset.UtcNow>=nextSweep){Retain(configured);CleanLive();nextSweep=DateTimeOffset.UtcNow.AddMinutes(5);}
                }
                catch(Exception e){log.LogError("Recorder supervisor: {Type}",e.GetType().Name);}
                await Task.Delay(250,stoppingToken);
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
        finally{foreach(var id in runs.Keys)await Stop(id);IndexCompleted();foreach(var c in store.Cameras())PublishState(c);}
    }
    string Sanitize(string value,Camera c)=>Regex.Replace(value.Replace(store.Password(c),"[redacted]"),@"rtsp://[^\s]+","[camera]");
    void Start(Camera c)
    {
        var session=DateTime.UtcNow.ToString("yyyyMMddTHHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];
        var live=Path.Combine(paths.Live,c.Id,session);Directory.CreateDirectory(live);
        var info=new ProcessStartInfo(paths.Ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardInput=true,RedirectStandardOutput=true};
        void Add(params string[] a){foreach(var v in a)info.ArgumentList.Add(v);}
        Add("-hide_banner","-loglevel","error","-nostats","-fflags","+genpts","-rtsp_transport","tcp","-timeout","10000000","-i",cameras.Rtsp(c));
        using var socket=new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,0));
        var relayPort=((System.Net.IPEndPoint)socket.Client.LocalEndPoint!).Port;socket.Close();
        Add("-map","0:v:0","-map","0:a:0?","-c:v","copy","-c:a","aac","-ar","16000","-ac","1","-b:a","48k","-f","mpegts","-mpegts_flags","resend_headers","-muxdelay","0","-flush_packets","1",$"udp://127.0.0.1:{relayPort}?pkt_size=1316");
        using var rtcSocket=new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,0));
        var rtcPort=((System.Net.IPEndPoint)rtcSocket.Client.LocalEndPoint!).Port;rtcSocket.Close();
        Add(WebRtcRelayArguments(rtcPort));
        Add(OverviewArguments(live));
        var segmentMs=store.StreamSettings().SegmentMs;Add(FragmentCache.Arguments(segmentMs));
        Manifest? manifest=null;
        if(c.RecordingEnabled)
        {
            ValidateRecordingRoot(c.RecordingRoot);
            var root=Path.Combine(Path.GetFullPath(c.RecordingRoot),"gimdvr-"+c.Id);Directory.CreateDirectory(root);
            // Unique session folder prevents replacement of any existing recording.
            var folder=Path.Combine(root,session);Directory.CreateDirectory(folder);
            var csv=Path.Combine(paths.Manifests,c.Id+"-"+session+".csv");
            manifest=new(c.Id,c.Name,folder,csv,session);
            File.WriteAllText(Path.ChangeExtension(csv,".json"),JsonSerializer.Serialize(manifest));
            Add(RecordingArguments(csv,folder));
        }
        var process=new Process{StartInfo=info,EnableRaisingEvents=true};
        process.ErrorDataReceived+=(_,e)=>{if(!string.IsNullOrWhiteSpace(e.Data)){errors[c.Id]="สตรีมมีข้อผิดพลาด กำลังลองเชื่อมต่อใหม่ ตรวจกล้องหรือพื้นที่บันทึกหากยังไม่กลับมา";log.LogWarning("Camera {Id}: {Error}",c.Id,Sanitize(e.Data,c));}};
        if(!process.Start())throw new InvalidOperationException("เริ่ม FFmpeg ไม่สำเร็จ");
        IDisposable? job=null;
        try{job=ProcessJob.Attach(process);process.BeginErrorReadLine();var run=new Run(c,process,live,manifest,job){WebRtcPort=rtcPort,SegmentMs=segmentMs,RelayPort=relayPort};runs[c.Id]=run;
            run.FragmentPump=Task.Run(async()=>{try{await FragmentCache.Pump(process.StandardOutput.BaseStream,Path.Combine(live,"fragments"));}catch(Exception e){log.LogWarning("Copy fragment cache {Id}: {Error}",c.Id,Sanitize(e.Message,c));try{await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);}catch(IOException){}}});errors.TryRemove(c.Id,out _);}
        catch{process.Kill(true);process.Dispose();job?.Dispose();throw;}
    }
    // Overview is a video-only remux: no decoder, scaling or video encoder.
    public static string[] OverviewArguments(string live)=>["-map","0:v:0","-c:v","copy","-an","-f","hls","-hls_time","2","-hls_list_size","6","-hls_start_number_source","epoch_us","-hls_flags","delete_segments+independent_segments+temp_file","-hls_segment_filename",Path.Combine(live,"seg%09d.ts"),Path.Combine(live,"index.m3u8")];
    public static string[] RecordingArguments(string csv,string folder)=>["-map","0:v:0","-map","0:a:0?","-c:v","copy","-bsf:v","extract_extradata","-c:a","aac","-ar","16000","-ac","1","-b:a","48k","-f","segment","-segment_time","60","-reset_timestamps","1","-strftime","1","-segment_format","mp4","-segment_format_options","movflags=+faststart","-segment_list",csv,"-segment_list_type","csv",Path.Combine(folder,"%Y%m%dT%H%M%S.mp4")];
    public void ValidateRecordingRoot(string root)
    {
        if(!Path.IsPathFullyQualified(root))throw new ArgumentException("กรุณาใช้ path เต็มบน NAS");
        var full=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        foreach(var forbidden in new[]{AppContext.BaseDirectory,paths.Data,Environment.GetFolderPath(Environment.SpecialFolder.Windows)})
            if(forbidden.Length>0&&full.StartsWith(Path.GetFullPath(forbidden).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("เลือกโฟลเดอร์วิดีโอแยกจากโฟลเดอร์แอป ข้อมูลระบบ และ Windows");
    }
    async Task Stop(string id)
    {
        if(!runs.TryRemove(id,out var run))return;
        await StopEncoder(run);
        try
        {
            if(!run.Process.HasExited)
            {
                await run.Process.StandardInput.WriteLineAsync("q");
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try{await run.Process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){run.Process.Kill(true);await run.Process.WaitForExitAsync();}
            }
        }
        catch(InvalidOperationException){}
        finally{if(run.FragmentPump is not null)try{await run.FragmentPump;}catch(IOException){}run.Job?.Dispose();run.Process.Dispose();}
    }
    void IndexCompleted()
    {
        foreach(var file in Directory.EnumerateFiles(paths.Manifests,"*.json"))
        {
            try
            {
                var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(file))!;if(!File.Exists(m.Csv))continue;
                var length=new FileInfo(m.Csv).Length;
                if(indexedLengths.TryGetValue(m.Csv,out var seen)&&seen==length)continue;
                using var stream=new FileStream(m.Csv,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                using var reader=new StreamReader(stream);
                while(reader.ReadLine() is {} line)
                {
                    var match=Regex.Match(line,"^(?:\"(?<file>[^\"]+)\"|(?<file>[^,]+)),(?<start>[0-9.]+),(?<end>[0-9.]+)$");if(!match.Success)continue;
                    var path=Path.GetFullPath(Path.IsPathFullyQualified(match.Groups["file"].Value)?match.Groups["file"].Value:Path.Combine(m.Root,match.Groups["file"].Value));
                    if(!path.StartsWith(Path.GetFullPath(m.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))continue;
                    var info=new FileInfo(path);var name=Path.GetFileNameWithoutExtension(path);
                    if(!DateTime.TryParseExact(name,"yyyyMMddTHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeLocal,out var local))continue;
                    var duration=double.Parse(match.Groups["end"].Value,CultureInfo.InvariantCulture)-double.Parse(match.Groups["start"].Value,CultureInfo.InvariantCulture);
                    if(duration<=0||info.Length==0)continue;
                    var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
                    store.AddRecording(new(id,m.CameraId,m.CameraName,path,new DateTimeOffset(local),duration,info.Length));
                }
                indexedLengths[m.Csv]=length;
            }
            catch(IOException){}
            catch(JsonException){}
            catch(FormatException){}
        }
    }
    void Retain(List<Camera> cameras)
    {
        foreach(var c in cameras.Where(c=>c.RetentionDays is >0))
        foreach(var r in store.Recordings(c.Id,to:DateTimeOffset.UtcNow.AddDays(-c.RetentionDays!.Value),limit:10000))
        {
            // Only catalogued, finalized app recordings, never arbitrary files in a selected folder.
            if(!Regex.IsMatch(Path.GetFileName(r.Path),@"^\d{8}T\d{6}\.mp4$")||!r.Path.Contains(Path.DirectorySeparatorChar+"gimdvr-"+c.Id+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
            try{File.Delete(r.Path);store.ForgetRecording(r.Id);}catch(IOException){}catch(UnauthorizedAccessException){errors[c.Id]="ไม่มีสิทธิ์ลบวิดีโอหมดอายุ";}
        }
    }
    void CleanLive()
    {
        foreach(var camera in Directory.EnumerateDirectories(paths.Live))
        foreach(var session in Directory.EnumerateDirectories(camera))
            if(!runs.Values.Any(r=>r.Live==session||r.Overview==session)&&Directory.GetLastWriteTimeUtc(session)<DateTime.UtcNow.AddMinutes(-10))Directory.Delete(session,true);
    }
}

