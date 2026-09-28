using GimDvr;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

var root=Path.GetFullPath(Path.Combine("artifacts","copy-checks-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(root);
var ffmpeg=args.FirstOrDefault()??"ffmpeg";
int passed=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);passed++;}
async Task<byte[]> Run(params string[] a){using var p=new Process{StartInfo=new(ffmpeg){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}};foreach(var arg in a)p.StartInfo.ArgumentList.Add(arg);p.Start();var err=p.StandardError.ReadToEndAsync();using var output=new MemoryStream();await Task.WhenAll(p.StandardOutput.BaseStream.CopyToAsync(output),p.WaitForExitAsync());if(p.ExitCode!=0)throw new Exception(await err);return output.ToArray();}
var source=Path.Combine(root,"source.ts");
await Run("-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc2=size=320x180:rate=30","-t","10","-c:v","libx264","-preset","veryfast","-g","120","-keyint_min","120","-sc_threshold","0","-bf","2",source);
var raw=await Run(new[]{"-hide_banner","-loglevel","error","-i",source}.Concat(FragmentCache.Arguments()).ToArray());
await File.WriteAllBytesAsync(Path.Combine(root,"raw.mp4"),raw);
var boxes=new List<(string Type,int Offset,int Size)>();for(int pos=0;pos<raw.Length;){int size=(int)BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(pos,4));boxes.Add((Encoding.ASCII.GetString(raw,pos+4,4),pos,size));pos+=size;}
var mdats=boxes.Where(b=>b.Type=="mdat").ToArray();Console.WriteLine($"Fragment count: {mdats.Length}");Check(mdats.Length>=45,"10-second video with four-second GOP yields about 50 short fragments");
var cut=mdats[1].Offset+mdats[1].Size;var cache=Path.Combine(root,"cache");
using var gated=new GatedStream(raw,cut);var pumping=FragmentCache.Pump(gated,cache);
for(int i=0;i<250&&(FragmentCache.ReadIndex(cache)?.Fragments.Length??0)<2;i++)await Task.Delay(20);
if(pumping.IsFaulted)await pumping;
var early=FragmentCache.ReadIndex(cache)!;
Check(!pumping.IsCompleted&&early.Fragments.Length==2,"two fragments published before next keyframe or input EOF");
Check(early.Fragments[0].Keyframe&&!early.Fragments[1].Keyframe,"keyframe classification distinguishes dependent short fragments");
gated.Release();await pumping;var index=FragmentCache.ReadIndex(cache)!;
Check(index.Fragments.Count(f=>f.Keyframe)==3,"four-second IDRs recognized in 10-second fixture");
Check(FragmentCache.NextKeyframe(index,1)==index.Fragments.First(f=>f.Keyframe&&f.Sequence>1).Sequence,"join waits for next keyframe rather than replaying initial GOP");
Check(FragmentCache.NextKeyframe(index,index.Fragments[^1].Sequence) is null,"join after latest fragment waits for a future keyframe");
async Task<string> Join(string name,long from){var file=Path.Combine(root,name);await using var output=File.Create(file);await output.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(cache,"init.mp4")));foreach(var item in index.Fragments.Where(f=>f.Sequence>=from))await output.WriteAsync(await File.ReadAllBytesAsync(FragmentCache.Chunk(cache,item.Sequence)));return file;}
var full=await Join("full.mp4",1);var joined=await Join("joined.mp4",FragmentCache.NextKeyframe(index,1)!.Value);
string[] Hashes(byte[] b)=>Encoding.UTF8.GetString(b).Split('\n').Where(x=>x.Length>0&&!x.StartsWith('#')).Select(x=>x.Split(',')[^1].Trim()).ToArray();
var original=Hashes(await Run("-v","error","-i",source,"-map","0:v:0","-f","framemd5","-"));var all=Hashes(await Run("-v","error","-i",full,"-map","0:v:0","-f","framemd5","-"));
Check(original.SequenceEqual(all),"all decoded frames remain identical after copy-only short fragmentation");
var late=Hashes(await Run("-v","error","-i",joined,"-map","0:v:0","-f","framemd5","-"));Check(late.Length>0&&original.TakeLast(late.Length).SequenceEqual(late),"init plus joined keyframe and later fragments decodes correct remaining frames");
using var framed=new MemoryStream();await FragmentCache.SendPacket(framed,new byte[]{1,2,3},default);Check(framed.ToArray().SequenceEqual(new byte[]{0,0,0,3,1,2,3}),"wire packets use bounded big-endian lengths");
var tiny=new byte[]{0,0,0,7,109,100,97,116};try{await FragmentCache.Pump(new MemoryStream(tiny),Path.Combine(root,"bad"));throw new Exception("Malformed box accepted");}catch(InvalidDataException){Check(true,"malformed MP4 size rejected");}
using var cancel=new CancellationTokenSource();cancel.Cancel();try{await FragmentCache.Pump(new MemoryStream(raw),Path.Combine(root,"cancel"),cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Check(true,"fragment pump honors cancellation");}
var muxHls=Path.Combine(root,"mux-hls");var muxRecord=Path.Combine(root,"mux-record");Directory.CreateDirectory(muxHls);Directory.CreateDirectory(muxRecord);
var simultaneous=await Run(new[]{"-hide_banner","-loglevel","error","-i",source}.Concat(MediaService.OverviewArguments(muxHls)).Concat(FragmentCache.Arguments()).Concat(MediaService.RecordingArguments(Path.Combine(root,"mux.csv"),muxRecord)).ToArray());
await FragmentCache.Pump(new MemoryStream(simultaneous),Path.Combine(root,"mux-copy"));
var recorded=Directory.GetFiles(muxRecord,"*.mp4").Single();var recordHashes=Hashes(await Run("-v","error","-i",recorded,"-map","0:v:0","-f","framemd5","-"));
Check(File.Exists(Path.Combine(muxHls,"index.m3u8"))&&FragmentCache.ReadIndex(Path.Combine(root,"mux-copy"))!.Fragments.Length>=45&&recordHashes.SequenceEqual(original),"one FFmpeg input simultaneously preserves recording, snapshot HLS and short copy fragments");
// Publish through the real HTTP handler with isolated store/runtime fixtures (no listening server).
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DataRoot"]=Path.Combine(root,"data"),["Dvr:MediaOwner"]="worker"}).Build();
var paths=new Paths(config,new Env(root));var store=new Store(paths,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));
var camera=store.SaveCamera(new Camera{Id="fixture",Name="Fixture",Host="192.168.99.250"},"fixture");
var overview=Path.Combine(paths.Live,camera.Id,"session");var streamRoot=Path.Combine(overview,"fragments");Directory.CreateDirectory(streamRoot);Directory.CreateDirectory(paths.Runtime);
foreach(var file in Directory.GetFiles(cache))File.Copy(file,Path.Combine(streamRoot,Path.GetFileName(file)));
void Publish(IEnumerable<FragmentCache.Entry> entries)=>File.WriteAllText(Path.Combine(streamRoot,"index.json"),JsonSerializer.Serialize(new FragmentCache.Index(entries.ToArray())));
File.WriteAllText(Path.Combine(paths.Runtime,"fixture.json"),JsonSerializer.Serialize(new{Updated=DateTimeOffset.UtcNow,Running=true,Recording=false,Overview=overview}));
Publish(index.Fragments.Take(10));
var media=new MediaService(store,new CameraClient(store),paths,NullLogger<MediaService>.Instance);var user=store.Users().Single();
using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(8));var context=new DefaultHttpContext();context.RequestAborted=stop.Token;context.Response.Body=new MemoryStream();
context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim("stamp",user.Stamp)},"fixture"));
var serving=media.CopyStream(camera,context);await Task.Delay(150);Check(context.Response.Body.Length==0,"HTTP join does not publish cached pre-join GOP");
Publish(index.Fragments.Take(25));for(int i=0;i<100&&context.Response.Body.Length==0;i++)await Task.Delay(20);
var wire=((MemoryStream)context.Response.Body).ToArray();var initSize=(int)BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(0,4));var firstSize=(int)BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(4+initSize,4));
var firstPayload=wire.AsSpan(8+initSize,firstSize).ToArray();var expected=await File.ReadAllBytesAsync(FragmentCache.Chunk(cache,FragmentCache.NextKeyframe(index,10)!.Value));
Check(firstPayload.SequenceEqual(expected),"real handler sends init then first future keyframe, not dependent fragments");
store.SaveUser(user.Id,new(user.Username,user.Role,true,"changed-fixture-password"));await serving.WaitAsync(TimeSpan.FromSeconds(4));Check(true,"long-lived response stops when user authentication stamp is revoked");
var ring=Path.Combine(root,"ring");using var repeated=new MemoryStream();var firstMoof=boxes.First(b=>b.Type=="moof").Offset;repeated.Write(raw,0,firstMoof);
var fragmentBytes=raw.AsSpan(firstMoof,mdats[0].Offset+mdats[0].Size-firstMoof).ToArray();for(int i=0;i<150;i++)repeated.Write(fragmentBytes);repeated.Position=0;await FragmentCache.Pump(repeated,ring);
Check(FragmentCache.ReadIndex(ring)!.Fragments.Length==128&&Directory.GetFiles(ring,"chunk*.m4s").Length==128,"cache evicts old fragment files and caps published entries");
var goodInit=await File.ReadAllBytesAsync(Path.Combine(cache,"init.mp4"));var avc=Encoding.Latin1.GetString(goodInit).IndexOf("avcC",StringComparison.Ordinal)-4;
int removed=(int)BinaryPrimitives.ReadUInt32BigEndian(goodInit.AsSpan(avc,4))-8;var emptyInit=goodInit.ToArray();
foreach(var type in new[]{"moov","trak","mdia","minf","stbl","stsd","avc1","avcC"}){int at=Encoding.Latin1.GetString(emptyInit).IndexOf(type,StringComparison.Ordinal)-4;BinaryPrimitives.WriteUInt32BigEndian(emptyInit.AsSpan(at,4),BinaryPrimitives.ReadUInt32BigEndian(emptyInit.AsSpan(at,4))-(uint)removed);}
emptyInit=emptyInit[..(avc+8)].Concat(emptyInit[(avc+8+removed)..]).ToArray();
var repaired=FragmentCache.CompleteInitialization(emptyInit,fragmentBytes);var fixedFile=Path.Combine(root,"repaired-init.mp4");await File.WriteAllBytesAsync(fixedFile,repaired.Concat(fragmentBytes).ToArray());
var referenceFile=Path.Combine(root,"reference-first-fragment.mp4");await File.WriteAllBytesAsync(referenceFile,goodInit.Concat(fragmentBytes).ToArray());
var fixedHashes=Hashes(await Run("-v","error","-i",fixedFile,"-map","0:v:0","-f","framemd5","-"));var referenceHashes=Hashes(await Run("-v","error","-i",referenceFile,"-map","0:v:0","-f","framemd5","-"));
Check(fixedHashes.SequenceEqual(referenceHashes),"empty avcC rebuilt from in-band SPS/PPS preserves decoded first fragment");
Check(FragmentCache.CompleteInitialization(goodInit,fragmentBytes).SequenceEqual(goodInit),"existing nonempty codec initialization remains unchanged");
var privateFixture=Path.GetFullPath("artifacts/init-diagnosis");
if(File.Exists(Path.Combine(privateFixture,"key.m4s"))){
 var cameraInit=await File.ReadAllBytesAsync(Path.Combine(privateFixture,"init.mp4"));var cameraPacket=await File.ReadAllBytesAsync(Path.Combine(privateFixture,"key.m4s"));
 var fixedInit=FragmentCache.CompleteInitialization(cameraInit,cameraPacket);var target=Path.Combine(root,"private-camera-repaired.mp4");await File.WriteAllBytesAsync(target,fixedInit.Concat(cameraPacket).ToArray());
 Check(fixedInit.Length>cameraInit.Length&&Hashes(await Run("-v","error","-i",target,"-map","0:v:0","-f","framemd5","-")).Length>0,"captured camera cache with empty avcC repaired and decoded locally");
 await File.WriteAllBytesAsync(Path.Combine(privateFixture,"fixed-init.mp4"),fixedInit);
}
Console.WriteLine($"{passed} stream checks passed. Evidence: {root}");
sealed class GatedStream(byte[] bytes,int cut):MemoryStream(bytes)
{
 readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);public void Release()=>release.TrySetResult();
 public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){if(Position>=cut)await release.Task.WaitAsync(ct);var remaining=Position<cut?cut-(int)Position:buffer.Length;return await base.ReadAsync(buffer[..Math.Min(Math.Min(buffer.Length,remaining),31)],ct);}
}

sealed class Env(string root):IWebHostEnvironment
{
 public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="StreamChecks";
 public string WebRootPath{get;set;}=root;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
 public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
