using GimDvr;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

// A controllable process fixture for supervisor-lifetime tests; no camera IO.
if(args.Contains("-i")){while(await Console.In.ReadLineAsync() is {} line&&line!="q"){}return;}

var root=Path.GetFullPath(Path.Combine("artifacts","checks-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(root);
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DataRoot"]=Path.Combine(root,"data")}).Build();
var paths=new Paths(config,new Env(root));
var store=new Store(paths,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));
var media=new MediaService(store,new CameraClient(store),paths,NullLogger<MediaService>.Instance);
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
var discoveryCamera=new Camera { Host="192.168.1.37", Uid="TESTCAM" };
var discoveryReply=new byte[524]; discoveryReply[0]=0x44; discoveryReply[1]=0x48; discoveryReply[2]=1; discoveryReply[3]=8;
System.Text.Encoding.ASCII.GetBytes(discoveryCamera.Host).CopyTo(discoveryReply,4);
System.Text.Encoding.ASCII.GetBytes(discoveryCamera.Uid).CopyTo(discoveryReply,92);
discoveryReply[90]=1; discoveryReply[91]=93;
var sender=System.Net.IPAddress.Parse(discoveryCamera.Host);
Check(CameraHttpPorts.ParseReply(discoveryReply,sender,discoveryCamera)==23809,"discovery decodes little endian HTTP port");
Check(CameraHttpPorts.ParseReply(discoveryReply,System.Net.IPAddress.Loopback,discoveryCamera)==null,"discovery rejects other sender");
Check(CameraHttpPorts.ParseReply(discoveryReply,sender,discoveryCamera with { Uid="OTHER" })==null,"discovery rejects different camera UID");
Check(CameraHttpPorts.ParseReply(new byte[10],sender,discoveryCamera)==null,"discovery rejects truncated packet");
discoveryReply[3]=1;
Check(CameraHttpPorts.ParseReply(discoveryReply,sender,discoveryCamera)==null,"discovery rejects request packet");
discoveryReply[3]=8; discoveryReply[90]=0; discoveryReply[91]=0;
Check(CameraHttpPorts.ParseReply(discoveryReply,sender,discoveryCamera)==null,"discovery rejects zero port");
var loopCamera=new Camera { Host="127.0.0.1", Uid="TESTCAM", HttpPort=1234 };
var loopReply=(byte[])discoveryReply.Clone();Array.Clear(loopReply,4,16);
System.Text.Encoding.ASCII.GetBytes(loopCamera.Host).CopyTo(loopReply,4);loopReply[90]=1;loopReply[91]=93;
var portResolver=new CameraHttpPorts();
using(var responder=new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,8600)))
{
    var resolve=portResolver.Resolve(loopCamera,CancellationToken.None);
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var request=await responder.ReceiveAsync(deadline.Token);
    Check(request.Buffer.SequenceEqual(new byte[]{0x44,0x48,1,1}),"discovery sends credential-free request");
    await responder.SendAsync(loopReply,request.RemoteEndPoint);
    Check(await resolve==23809,"resolver uses discovered rather than stale configured port");
}
Check(await portResolver.Resolve(loopCamera,CancellationToken.None)==23809,"pan and stop reuse cached port without UDP responder");
using(var canceled=new CancellationTokenSource())
{
    canceled.Cancel();bool observed=false;
    try { await portResolver.Resolve(loopCamera,canceled.Token); } catch(OperationCanceledException) { observed=true; }
    Check(observed,"resolver honors caller cancellation");
}
Check(File.Exists(Path.Combine(paths.Data,"bootstrap.txt")),"bootstrap outside web root");
var admin=store.Users().Single();
Reject(()=>store.SaveUser(admin.Id,new("admin","viewer",true,null)),"cannot demote last admin");
Reject(()=>store.SaveUser(admin.Id,new("admin","admin",false,null)),"cannot disable last admin");
var viewer=store.SaveUser(null,new("viewer","viewer",true,"1"));
Check(Store.Verify("1",viewer.Hash)&&!Store.Verify("wrong",viewer.Hash),"single-character password accepted and verified");
Check(store.SaveUser(viewer.Id,new("viewer","operator",true,null)).Stamp!=viewer.Stamp,"role changes revoke sessions");
for(int i=0;i<5;i++)store.LoginResult("bad-user",false);
Check(!store.LoginAllowed("bad-user"),"login lockout after repeated failures");
store.LoginResult("bad-user",true);Check(store.LoginAllowed("bad-user"),"login lockout reset");
var cam=new Camera{Id="fixture",Name="Synthetic test",Host="192.168.1.250"};
store.SaveCamera(cam,"camera-fixture-secret");
Check(!cam.RecordingEnabled&&cam.RecordingRoot=="","recording defaults off without path");
Check(store.Password(cam)=="camera-fixture-secret"&&!cam.Secret.Contains("camera-fixture-secret"),"camera secret encryption");
Reject(()=>(cam with{Id="../escape"}).Validate(),"camera traversal id rejected");
Reject(()=>(cam with{RecordingEnabled=true}).Validate(),"recording requires owner path");
Reject(()=>media.ValidateRecordingRoot(paths.Data),"data root cannot be recording root");
Directory.CreateDirectory(paths.Manifests);
var folder=Path.Combine(root,"video","gimdvr-fixture","test-session");Directory.CreateDirectory(folder);
var own=Path.Combine(folder,"20260101T000000.mp4");var unrelated=Path.Combine(folder,"20260101T000100.mp4");
File.WriteAllText(own,"owned fixture");File.WriteAllText(unrelated,"uncatalogued fixture");
store.AddRecording(new("old","fixture","Synthetic",own,DateTimeOffset.UtcNow.AddDays(-10),60,13));
typeof(MediaService).GetMethod("Retain",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(media,[new List<Camera>{cam with{RetentionDays=1}}]);
Check(!File.Exists(own)&&File.Exists(unrelated),"retention only deletes catalogued app clips");
if(args.Length>0)
{
 var ffmpeg=args[0];var source=Path.Combine(root,"synthetic.mp4");
 async Task Run(params string[] arguments){var p=new Process{StartInfo=new(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}};foreach(var a in arguments)p.StartInfo.ArgumentList.Add(a);p.Start();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new Exception(await error);}
 await Run("-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc2=size=160x90:rate=10","-f","lavfi","-i","sine=frequency=440:sample_rate=16000","-t","125","-c:v","libx264","-preset","ultrafast","-g","10","-c:a","aac",source);
 var csv=Path.Combine(paths.Manifests,"fixture.csv");
 await Run(new[]{"-hide_banner","-loglevel","error","-readrate","10","-i",source}.Concat(MediaService.RecordingArguments(csv,folder)).ToArray());
 File.WriteAllText(Path.ChangeExtension(csv,".json"),JsonSerializer.Serialize(new{CameraId="fixture",CameraName="Synthetic",Root=folder,Csv=csv,Session="test-session"}));
 typeof(MediaService).GetMethod("IndexCompleted",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(media,null);
 var clips=store.Recordings("fixture").OrderBy(r=>r.Start).ToList();
 Check(clips.Count==3,"segmentation indexes 3 finalized files for 125 seconds");
 Check(Math.Abs(clips[0].Duration-60)<1.2&&Math.Abs(clips[1].Duration-60)<0.2&&clips[2].Duration<6,"one-minute segments within first keyframe/audio offset and short final tail");
 Check(clips.All(r=>File.Exists(r.Path)&&r.Bytes>0),"catalog paths refer to completed media");
 typeof(MediaService).GetMethod("IndexCompleted",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(media,null);
 Check(store.Recordings("fixture").Count==3,"indexing is idempotent");
}
var fake=new FakeCamera(store);var ptz=new PtzService(fake,NullLogger<PtzService>.Instance);
await ptz.StartAsync(CancellationToken.None);
var hold=Guid.NewGuid().ToString();ptz.Set(cam,"tester",new("left",hold));await Task.Delay(90);
for(int i=0;i<4;i++){ptz.Set(cam,"tester",new("left",hold));await Task.Delay(100);}
Check(fake.Commands.Count(x=>x==4)==1,"hold heartbeats do not queue repeated motor commands");
ptz.Set(cam,"tester",new("stop",hold));await Task.Delay(100);
Check(fake.Commands.Last()==5,"release stops matching horizontal direction");
var delayed=Guid.NewGuid().ToString();ptz.Set(cam,"tester",new("stop",delayed));
Check(!ptz.Set(cam,"tester",new("right",delayed)),"release arriving before start prevents delayed movement");
var lost=Guid.NewGuid().ToString();ptz.Set(cam,"tester",new("up",lost));await Task.Delay(850);
Check(fake.Commands.TakeLast(2).SequenceEqual(new[]{0,1}),"lost heartbeat stops automatically");
Check(!ptz.Set(cam,"tester",new("up",lost)),"expired press cannot restart on a delayed heartbeat");
await ptz.StopAsync(CancellationToken.None);
if(OperatingSystem.IsWindows())
{
 var ownerConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DataRoot"]=paths.Data,["Dvr:LiveEncoder"]="cpu",["Dvr:Ffmpeg"]=Path.Combine(AppContext.BaseDirectory,"GimDvr.Checks.exe")}).Build();
 var ownerPaths=new Paths(ownerConfig,new Env(root));
 var recorder=new MediaService(store,new CameraClient(store),ownerPaths,NullLogger<MediaService>.Instance);
 store.SaveCamera(cam with{RecordingEnabled=true,RecordingRoot=Path.Combine(root,"background-video")},null);
 await recorder.StartAsync(CancellationToken.None);await Task.Delay(2400);
 int Pid(MediaService m)=>JsonSerializer.SerializeToElement(m.Status(cam.Id)).GetProperty("processId").GetInt32();
 var pid=Pid(recorder);
 Check(!JsonSerializer.SerializeToElement(recorder.Status(cam.Id)).GetProperty("encoding").GetBoolean(),"recording alone does not start HLS encoder");
 for(int i=0;i<25;i++)recorder.Watch(cam.Id);
 await Task.Delay(600);
 Check(!JsonSerializer.SerializeToElement(recorder.Status(cam.Id)).GetProperty("encoding").GetBoolean(),"overview viewers do not start a video encoder");
 Check(Pid(recorder)==pid,"overview viewers reuse recording reader");
 for(int i=0;i<25;i++)recorder.Watch(cam.Id,true);
 await Task.Delay(2200);
 Check(Pid(recorder)==pid,"25 viewers reuse the recording process");
 var encoderPid=JsonSerializer.SerializeToElement(recorder.Status(cam.Id)).GetProperty("encoderProcessId").GetInt32();
 for(int i=0;i<25;i++)recorder.Watch(cam.Id,true);
 await Task.Delay(500);
 Check(JsonSerializer.SerializeToElement(recorder.Status(cam.Id)).GetProperty("encoderProcessId").GetInt32()==encoderPid,"25 viewers share one live encoder");
 var proxyConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DataRoot"]=paths.Data,["Dvr:MediaOwner"]="worker",["Dvr:Ffmpeg"]="must-not-execute"}).Build();
 var proxy=new MediaService(store,new CameraClient(store),new Paths(proxyConfig,new Env(root)),NullLogger<MediaService>.Instance);
 await proxy.StartAsync(CancellationToken.None);proxy.Watch(cam.Id,true);await Task.Delay(100);
 Check(Pid(proxy)==pid,"web proxy reuses external worker process without spawning FFmpeg");
 await proxy.StopAsync(CancellationToken.None);
 await Task.Delay(2200);
 Check(Pid(recorder)==pid,"stopping web proxy leaves background recording process alive");
 for(int i=0;i<5;i++){recorder.Watch(cam.Id);await Task.Delay(1800);}
 Check(!JsonSerializer.SerializeToElement(recorder.Status(cam.Id)).GetProperty("encoding").GetBoolean(),"focus encoder stops despite ongoing overview watches");
 Check(Pid(recorder)==pid,"recording reader survives viewer departure");
 await recorder.StopAsync(CancellationToken.None);
 Check(!JsonSerializer.SerializeToElement(recorder.Status(cam.Id)).GetProperty("running").GetBoolean(),"recorder shutdown closes owned process");
}
Check(MediaService.OverviewArguments("fixture").Contains("copy")&&!MediaService.OverviewArguments("fixture").Contains("aac"),"overview remux uses video copy with no audio encoding");
var rangeStart=DateTimeOffset.Parse("2026-09-28T10:00:00Z");
store.AddRecording(new("range-before","range-camera","Range","before.mp4",rangeStart.AddSeconds(-60),60,1));
store.AddRecording(new("range-overlap","range-camera","Range","overlap.mp4",rangeStart.AddSeconds(-30),60,1));
store.AddRecording(new("range-inside","range-camera","Range","inside.mp4",rangeStart.AddSeconds(10),20,1));
store.AddRecording(new("range-after","range-camera","Range","after.mp4",rangeStart.AddSeconds(60),60,1));
store.AddRecording(new("range-other","other-camera","Other","other.mp4",rangeStart,60,1));
var ranged=store.RecordingRange("range-camera",rangeStart,rangeStart.AddSeconds(60),0);
Check(ranged.Select(x=>x.Id).SequenceEqual(new[]{"range-overlap","range-inside"}),"range includes overlap, excludes adjacent boundaries and other cameras, oldest first");
Check(store.RecordingRange("range-camera",rangeStart,rangeStart.AddSeconds(60),1).Single().Id=="range-inside","range pagination offset preserves order");
Check(store.RecordingRange("range-camera",rangeStart.ToOffset(TimeSpan.FromHours(7)),rangeStart.AddSeconds(60).ToOffset(TimeSpan.FromHours(7)),0).Count==2,"range respects timezone offsets");
var assetRoot=Path.Combine(root,"asset-fixture");Directory.CreateDirectory(assetRoot);
File.WriteAllText(Path.Combine(assetRoot,"app.js"),"old");
File.WriteAllText(Path.Combine(assetRoot,"talk-worklet.js"),"worklet");
File.WriteAllText(Path.Combine(assetRoot,"index.html"),"<html><head><script src=\"app.js?v=manual\"></script></head></html>");
using(var provider=new PhysicalFileProvider(assetRoot))
{
 var versions=new AssetVersions(new Env(root){WebRootFileProvider=provider});
 var oldUrl=versions.Url("app.js?v=manual&x=1#part");
 Check(oldUrl.Contains("v=")&&!oldUrl.Contains("manual")&&oldUrl.Contains("x=1")&&oldUrl.EndsWith("#part"),"asset version replaces manual token and preserves query/fragment");
 File.WriteAllText(Path.Combine(assetRoot,"app.js"),"changed-content");
 Check(versions.Url("app.js?v=manual&x=1#part")!=oldUrl,"asset content change changes URL without restart");
 var html=await versions.Index(CancellationToken.None);
 Check(html.Contains("app.js?v=")&&!html.Contains("v=manual")&&html.Contains("talk-worklet.js?v="),"HTML and dynamic worklet URLs are versioned");
 Check(versions.Url("https://example.org/app.js")=="https://example.org/app.js"&&versions.Url("#anchor")=="#anchor","external and anchor URLs remain intact");
}
var unknown=store.WithDetection(ranged).First();
Check(unknown.Detection is {State:"pending",Motion:null,Human:null},"unanalysed recording is unknown, not no motion");
store.SaveDetection(unknown.Id,new("processing"));
store.ResetInterruptedDetections();
Check(store.WithDetection(ranged).First().Detection?.State=="pending","interrupted analysis returns to pending");
store.SaveDetection(unknown.Id,new("complete",true,false,120,20,0.1,"fixture","fixture"));
Check(store.WithDetection(ranged).First().Detection is {Motion:true,Human:false,Frames:120},"SQLite persists independent motion/person flags and coverage");
Check(new Store(paths,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys")))) .WithDetection(ranged).First().Detection?.Motion==true,"detection survives store recreation/schema initialization");
store.SaveDetection("range-inside",new("partial",false,null,120,0,0,null,"fixture","model_unavailable"));
Check(store.WithDetection(ranged)[1].Detection is {State:"partial",Human:null},"unavailable person detector retains unknown instead of false");
store.ForgetRecording(unknown.Id);
store.AddRecording(unknown);
Check(store.WithDetection([unknown]).Single().Detection?.State=="pending","retention removes detection result with recording");
store.SaveDetection("not-catalogued",new("complete",true,true));
Check(store.WithDetection([unknown with{Id="not-catalogued"}]).Single().Detection?.State=="pending","cannot create orphan detection for deleted recording");
Check(store.StreamSettings()==new LiveStreamSettings(150,300,300,300),"live settings default to150ms fragments and300ms reserves");
store.SaveStreamSettings(new(100,250,350,450));
Check(new Store(paths,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys")))).StreamSettings()==new LiveStreamSettings(100,250,350,450),"live settings persist and are shared across store instances");
foreach(var bad in new[]{new LiveStreamSettings(0,300,300,300),new LiveStreamSettings(150,150,300,300),new LiveStreamSettings(150,300,6000,300)})
{try{store.SaveStreamSettings(bad);throw new Exception("Invalid settings accepted");}catch(ArgumentException){}}
Check(store.StreamSettings()==new LiveStreamSettings(100,250,350,450),"invalid settings cannot replace saved values");
Check(FragmentCache.Arguments(150).Contains("150000")&&FragmentCache.Arguments(250).Contains("250000"),"fragment duration converts configured milliseconds to FFmpeg microseconds");
Check(WebRtcService.ValidOffer("v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=recvonly\r\n"),"WebRTC receive-only video offer accepted");
Check(!WebRtcService.ValidOffer("v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=sendrecv\r\n")&&!WebRtcService.ValidOffer(new string('x',100001)),"WebRTC publishing and oversized offers rejected");
var secret=Guid.NewGuid().ToString();Check(WebRtcService.SessionLocation("a",new Uri("/a/whep/"+secret,UriKind.Relative)).Host=="127.0.0.1","WebRTC session location restricted to local gateway");
foreach(var bad in new[]{"http://evil.invalid/a/whep/"+secret,"http://127.0.0.1:18889/b/whep/"+secret,"/a/whep/"+secret+"?x=1"}){bool rejected=false;try{WebRtcService.SessionLocation("a",new Uri(bad,UriKind.RelativeOrAbsolute));}catch(IOException){rejected=true;}Check(rejected,"WebRTC foreign or mismatched session location rejected");}
Check(!MediaService.WebRtcRelayArguments(12345).Any(a=>a.Contains("setts"))&&!FragmentCache.Arguments().Any(a=>a.Contains("setts")),"both live outputs preserve input timing instead of forcing a frame rate");
var relay=MediaService.WebRtcRelayArguments(12345);Check(relay.Contains("copy")&&relay.Contains("-an")&&relay[^1]=="udp://127.0.0.1:12345?pkt_size=1316"&&!relay.Contains("-i"),"WebRTC video-only copy relay adds no camera input or encoder");
Check(MediaService.InputClockArguments(true).SequenceEqual(new[]{"-use_wallclock_as_timestamps","1"}),"arrival-clock opt-in replaces bad camera timestamps without encoding");
Check(MediaService.InputClockArguments(false).SequenceEqual(new[]{"-fflags","+genpts"}),"unconfigured cameras retain original clock policy");
for(int i=0;i<8;i++)store.AddRecording(new("parallel-"+i,"test","Test","parallel-"+i,DateTimeOffset.UtcNow.AddDays(2).AddSeconds(i),60,1));
var claims=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>store.ClaimDetection())));
Check(claims.All(r=>r is not null)&&claims.Select(r=>r!.Id).Distinct().Count()==8,"parallel detection claims never duplicate a recording");
Check(store.DetectionSettings(config)==new DetectionSettings(),"detection settings default to compatible runtime policy");
store.SaveDetectionSettings(new(2,0));
Check(store.DetectionSettings(config)==new DetectionSettings(2,0),"detection workers and unlimited read speed persist");
foreach(var invalid in new[]{new DetectionSettings(0,4),new DetectionSettings(5,4),new DetectionSettings(2,-1),new DetectionSettings(2,33),new DetectionSettings(2,double.NaN)}){try{store.SaveDetectionSettings(invalid);throw new Exception("Accepted invalid detection setting");}catch(ArgumentException){}}
Check(store.DetectionSettings(config)==new DetectionSettings(2,0),"invalid detection settings leave saved configuration intact");
Console.WriteLine($"{passed} checks passed. Evidence: {root}");
sealed class FakeCamera(Store store):CameraClient(store)
{
 public System.Collections.Concurrent.ConcurrentQueue<int> Commands=new();
 public override async Task PtzCommand(Camera camera,int code,CancellationToken ct){Commands.Enqueue(code);await Task.Delay(15,ct);}
}
sealed class Env(string root):IWebHostEnvironment
{
 public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="Checks";
 public string WebRootPath{get;set;}=Path.Combine(root,"wwwroot");public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
 public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
