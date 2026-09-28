using GimDvr;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

if(args.Length!=2)throw new ArgumentException("runtime directory and ffmpeg required; synthetic fixture only");
var root=Path.GetFullPath("artifacts/detection-integration-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(root);
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DetectionConcurrency"]="2",["Dvr:DetectionReadRate"]="0",["Dvr:DataRoot"]=root,["Dvr:DetectionRuntime"]=Path.GetFullPath(args[0]),["Dvr:Ffmpeg"]=args[1]}).Build();
var paths=new Paths(config,new Env(root));var store=new Store(paths,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));
var clip=Path.Combine(root,"synthetic.mp4");
using(var ffmpeg=new Process{StartInfo=new(args[1]){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}})
{
 foreach(var a in new[]{"-hide_banner","-loglevel","error","-f","lavfi","-i","color=c=black:s=640x360:r=10","-t","6","-c:v","libx264","-pix_fmt","yuv420p",clip})ffmpeg.StartInfo.ArgumentList.Add(a);
 ffmpeg.Start();var stderr=ffmpeg.StandardError.ReadToEndAsync();await ffmpeg.WaitForExitAsync();if(ffmpeg.ExitCode!=0)throw new Exception(await stderr);
}
var recording=new Recording("synthetic","test","Synthetic",clip,DateTimeOffset.UtcNow,6,new FileInfo(clip).Length);store.AddRecording(recording);
var clip2=Path.Combine(root,"synthetic2.mp4");File.Copy(clip,clip2);var recording2=recording with{Id="synthetic2",Path=clip2};store.AddRecording(recording2);
using var service=new DetectionService(store,paths,config,NullLogger<DetectionService>.Instance);
await service.StartAsync(CancellationToken.None);
DetectionResult? result=null;
try
{
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
 while(true){result=store.WithDetection([recording]).Single().Detection;if(result?.State is "complete" or "error" or "partial" && store.WithDetection([recording2]).Single().Detection?.State is "complete" or "error" or "partial")break;await Task.Delay(250,timeout.Token);}
}
finally{using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(15));await service.StopAsync(stop.Token);}
if(result is not {State:"complete",Motion:false,Human:false,Frames:12,HumanSamples:2})throw new Exception("Unexpected synthetic result: "+System.Text.Json.JsonSerializer.Serialize(result));
Console.WriteLine("PASS FFmpeg -> actual OpenVINO -> JSON worker -> SQLite -> recording projection: "+result.Device+" / "+result.Decoder);
using(var gate=File.Open(Path.Combine(paths.Runtime,"detection.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Console.WriteLine("PASS shutdown releases analysis owner lock");
var logs=Directory.GetFiles(Path.Combine(root,"logs","detection"),"*.jsonl").SelectMany(File.ReadAllLines).Select(l=>System.Text.Json.JsonDocument.Parse(l)).ToList();
if(!logs.Any(j=>j.RootElement.GetProperty("kind").GetString()=="clip_finish")||!logs.Any(j=>j.RootElement.GetProperty("kind").GetString()=="summary"))throw new Exception("Missing throughput log");
var events=logs.Select(j=>j.RootElement.GetProperty("kind").GetString()).ToList();
if(events.Take(events.IndexOf("clip_finish")).Count(x=>x=="clip_start")!=2)throw new Exception("Lanes did not overlap");
if(logs.Count(j=>j.RootElement.GetProperty("kind").GetString()=="clip_finish")!=2)throw new Exception("Duplicate/missing results");
Console.WriteLine("PASS two independent detector lanes overlap and complete exactly once");
var finished=logs.First(j=>j.RootElement.GetProperty("kind").GetString()=="clip_finish").RootElement.GetProperty("data");
if(finished.GetProperty("elapsedSeconds").GetDouble()<=0||finished.GetProperty("videoSeconds").GetDouble()!=6)throw new Exception("Invalid timing");
var summary=logs.Last(j=>j.RootElement.GetProperty("kind").GetString()=="summary").RootElement.GetProperty("data");
if(summary.GetProperty("stats").GetProperty("completedVideoSeconds").GetDouble()!=12||summary.GetProperty("queue").GetProperty("unresolvedFiles").GetInt64()!=0)throw new Exception("Invalid queue summary");
if(logs.Any(j=>j.RootElement.ToString().Contains(clip)))throw new Exception("Private path in logs");
Console.WriteLine("PASS JSONL lifecycle, clip timing, aggregate throughput/queue and no private paths");
foreach(var item in logs)item.Dispose();
var rotationDir=Path.Combine(root,"rotation");Directory.CreateDirectory(rotationDir);File.WriteAllText(Path.Combine(rotationDir,"unrelated.txt"),"keep");
var rotating=new DetectionLog(rotationDir,NullLogger.Instance,100,2);
for(int i=0;i<5;i++)rotating.Write("fixture",new{iteration=i,padding=new string('x',100)});
if(Directory.GetFiles(rotationDir,"*.jsonl").Length!=2||!File.Exists(Path.Combine(rotationDir,"unrelated.txt")))throw new Exception("Rotation/retention failure");
Console.WriteLine("PASS bounded rotation preserves unrelated files");
Console.WriteLine("Evidence: "+root);

sealed class Env(string root):IWebHostEnvironment
{
 public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="DetectionChecks";
 public string WebRootPath{get;set;}=root;public string ContentRootPath{get;set;}=root;
 public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
